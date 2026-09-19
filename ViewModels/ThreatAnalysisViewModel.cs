using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    public partial class ThreatAnalysisViewModel : ObservableObject
    {
        private readonly IFileThreatAnalyzerService _analyzerService;
        private readonly IAutorunsScannerEngine? _autorunsEngine;

        [ObservableProperty]
        private ThreatAnalysisResult _result = new();

        [ObservableProperty]
        private bool _isAnalyzing;

        [ObservableProperty]
        private string _statusText = string.Empty;

        [ObservableProperty]
        private bool _isProcessKilled;

        [ObservableProperty]
        private bool _isFileDeleted;

        [ObservableProperty]
        private bool _isStartupDisabled;

        [ObservableProperty]
        private bool _isWhitelisted;

        public event Action? RequestClose;

        public string FileName => Result.FileName;
        public string FilePath => Result.FilePath;
        public int RiskScore => Result.RiskScore;
        public string RiskLevelText => Result.RiskLevelText;
        public string RiskColor => Result.RiskColor;
        public string RiskBackgroundBrush => Result.RiskBackgroundBrush;
        public string Recommendation => Result.Recommendation;

        public bool HasActiveProcess => Result.IsActiveProcess && !IsProcessKilled;
        public int ActiveProcessId => Result.ActiveProcessId;
        public string ActiveProcessMemory => Result.ActiveProcessMemory;

        public bool CanDisableStartup => Result.OriginAutorunItem != null && !IsStartupDisabled;

        public ThreatAnalysisViewModel(
            ThreatAnalysisResult result,
            IFileThreatAnalyzerService? analyzerService = null,
            IAutorunsScannerEngine? autorunsEngine = null)
        {
            _result = result;
            _analyzerService = analyzerService ?? new FileThreatAnalyzerService();
            _autorunsEngine = autorunsEngine;

            StatusText = $"Analiz tamamlandı. Risk Skoru: %{result.RiskScore} ({result.RiskLevelText})";
        }

        [RelayCommand]
        public async Task KillProcessAsync()
        {
            if (!Result.IsActiveProcess || IsProcessKilled) return;

            var confirm = MessageBox.Show(
                $"Çalışan '{FileName}' (PID: {ActiveProcessId}) süreci ve alt süreç ağacı zorla sonlandırılacaktır.\n\nİşlemi onaylıyor musunuz?",
                "Süreci Sonlandır",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            StatusText = "Süreç sonlandırılıyor...";
            bool success = await _analyzerService.KillProcessAsync(ActiveProcessId);

            if (success)
            {
                IsProcessKilled = true;
                OnPropertyChanged(nameof(HasActiveProcess));
                StatusText = $"Süreç (PID: {ActiveProcessId}) başarıyla sonlandırıldı.";
                MessageBox.Show($"'{FileName}' süreci başarıyla sonlandırıldı.", "Süreç Durduruldu", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText = "Süreç sonlandırılamadı. Yönetici izinleri gerekebilir.";
                MessageBox.Show("Süreç sonlandırılamadı. Lütfen yönetici olarak çalıştırmayı deneyin.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public async Task DisableStartupAsync()
        {
            if (Result.OriginAutorunItem == null || IsStartupDisabled) return;

            if (_autorunsEngine == null)
            {
                MessageBox.Show("Başlangıç yöneticisi motoruna erişilemedi.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            StatusText = "Başlangıç girdisi devre dışı bırakılıyor...";
            bool success = await _autorunsEngine.ToggleItemAsync(Result.OriginAutorunItem, false);

            if (success)
            {
                IsStartupDisabled = true;
                Result.OriginAutorunItem.IsEnabled = false;
                OnPropertyChanged(nameof(CanDisableStartup));
                StatusText = "Başlangıç girdisi başarıyla pasifleştirildi.";
                MessageBox.Show($"'{Result.OriginAutorunItem.Name}' başlangıçtan devre dışı bırakıldı.", "Pasifleştirildi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText = "Başlangıç girdisi pasifleştirilemedi.";
                MessageBox.Show("Başlangıç girdisi değiştirilemedi. Kayıt defteri veya görev izinlerini kontrol edin.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public async Task DeleteFileAsync()
        {
            if (IsFileDeleted || !File.Exists(FilePath)) return;

            var confirm = MessageBox.Show(
                $"DİKKAT: '{FilePath}' dosyası kalıcı olarak silinecektir.\n\nEğer dosya kilitliyse sahipliği alınıp zorla parçalanacaktır.\n\nKalıcı olarak silinsin mi?",
                "Dosyayı Zorla Sil",
                MessageBoxButton.YesNo,
                MessageBoxImage.Stop);

            if (confirm != MessageBoxResult.Yes) return;

            StatusText = "Dosya kalıcı olarak siliniyor...";
            bool success = await _analyzerService.ForceDeleteFileAsync(FilePath);

            if (success)
            {
                IsFileDeleted = true;
                StatusText = "Dosya başarıyla silindi.";
                MessageBox.Show($"'{FileName}' dosyası diskten başarıyla silindi.", "Dosya Silindi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText = "Dosya hemen silinemedi, bir sonraki yeniden başlatmada silinmek üzere işaretlendi.";
                MessageBox.Show("Dosya kullanımda olduğu için doğrudan silinemedi; sistem yeniden başlatıldığında otomatik silinmek üzere Windows açılışına kaydedildi.", "Yeniden Başlatmada Silinecek", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        public void OpenLocation()
        {
            if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
            {
                MessageBox.Show("Dosya bulunamadı.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{FilePath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Konum açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void OpenVirusTotal()
        {
            if (string.IsNullOrWhiteSpace(Result.Sha256))
            {
                MessageBox.Show("SHA-256 özeti hesaplanamadı.", "Hata", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                string url = $"https://www.virustotal.com/gui/file/{Result.Sha256}";
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        [RelayCommand]
        public void CopyReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Bakım Sezgisel Tehdit Analiz Raporu");
            sb.AppendLine($"**Dosya Adı:** {FileName}");
            sb.AppendLine($"**Tam Yol:** {FilePath}");
            sb.AppendLine($"**Risk Skoru:** %{RiskScore} ({RiskLevelText})");
            sb.AppendLine($"**Boyut:** {Result.FileSizeFormatted}");
            sb.AppendLine($"**SHA-256:** `{Result.Sha256}`");
            sb.AppendLine($"**Dijital İmza:** {Result.DigitalSignatureText}");
            sb.AppendLine($"**Entropi:** {Result.EntropyText}");
            sb.AppendLine($"**VirusTotal:** {Result.VirusTotalSummary}");
            sb.AppendLine();
            sb.AppendLine($"## Tespit Edilen Güvenlik Faktörleri");
            foreach (var f in Result.Factors)
            {
                string symbol = f.Severity == ThreatSeverity.Critical ? "❌" : (f.Severity == ThreatSeverity.Warning ? "⚠️" : "✅");
                sb.AppendLine($"- {symbol} **{f.Title}**: {f.Description}");
            }
            sb.AppendLine();
            sb.AppendLine($"## Tavsiye");
            sb.AppendLine(Recommendation);

            try
            {
                Clipboard.SetText(sb.ToString());
                StatusText = "Rapor panoya kopyalandı.";
                MessageBox.Show("Analiz raporu panoya kopyalandı!", "Rapor Kopyalandı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        [RelayCommand]
        public void Whitelist()
        {
            IsWhitelisted = true;
            StatusText = "Dosya bu oturumda güvenli olarak işaretlendi.";
            MessageBox.Show($"'{FileName}' güvenli listeye eklendi.", "Güvenli İşaretlendi", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        [RelayCommand]
        public void Close()
        {
            RequestClose?.Invoke();
        }
    }
}
