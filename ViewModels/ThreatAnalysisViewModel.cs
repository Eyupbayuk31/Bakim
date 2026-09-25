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
using Bakım.Services.Safety;

namespace Bakım.ViewModels
{
    public partial class ThreatAnalysisViewModel : ObservableObject
    {
        private readonly IFileThreatAnalyzerService _analyzerService;
        private readonly IAutorunsScannerEngine? _autorunsEngine;
        private readonly IVirusTotalCheckService _virusTotalService;

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

        [ObservableProperty]
        private int _selectedTabIndex; // 0: Genel Bakış, 1: PE Röntgeni & Kalkanlar, 2: İçe Aktarılan API'ler, 3: Hash & VirusTotal

        [ObservableProperty]
        private bool _isUploadingToVt;

        partial void OnIsUploadingToVtChanged(bool value)
        {
            OnPropertyChanged(nameof(VtButtonText));
        }

        public event Action? RequestClose;

        public string FileName => Result.FileName;
        public string FilePath => Result.FilePath;
        public int RiskScore => Result.RiskScore;
        public string RiskLevelText => Result.RiskLevelText;
        /// <summary>
        /// Risk seviyesinin anlamsal tonu. Renk kodu taşımaz: XAML trigger'ları
        /// bunu tema fırçalarına çevirir, böylece dört temada da doğru görünür.
        /// </summary>
        public Intent RiskIntent => Result.RiskIntent;

        /// <summary>Renk körü kullanıcılar için renkten bağımsız risk göstergesi.</summary>
        public string RiskIconSymbol => Result.RiskIconSymbol;

        public string Recommendation => Result.Recommendation;

        public bool HasActiveProcess => Result.IsActiveProcess && !IsProcessKilled;
        public int ActiveProcessId => Result.ActiveProcessId;
        public string ActiveProcessMemory => Result.ActiveProcessMemory;

        public bool CanDisableStartup => Result.OriginAutorunItem != null && !IsStartupDisabled;

        public bool HasVtRecord => Result.VirusTotalTotal > 0;
        public string VtButtonText => IsUploadingToVt ? "Yükleniyor..." : (HasVtRecord ? $"VirusTotal ({Result.VirusTotalMalicious}/{Result.VirusTotalTotal})" : "VT'ye Gönder ve Tara");
        public string VtButtonIcon => HasVtRecord ? "Globe20" : "ArrowUpload20";

        // PE Binary Röntgen Properties
        public bool HasPeAnalysis => Result.HasPeAnalysis;
        public PeHeaderInfo? PeHeader => Result.PeHeader;
        public ExploitMitigationMatrix? Mitigations => Result.Mitigations;
        public List<PeSectionItem> Sections => Result.Sections;
        public bool HasSections => Result.Sections.Count > 0;
        public List<ImportedDllGroup> ImportedDlls => Result.ImportedDlls;
        public bool HasImportedDlls => Result.ImportedDlls.Count > 0;
        public int TotalSuspiciousApisCount => Result.TotalSuspiciousApisCount;

        // Hash Properties
        public string Sha256 => Result.Sha256;
        public string Md5 => Result.Md5;
        public string Sha1 => Result.Sha1;
        public string ImpHash => Result.ImpHash;

        public ThreatAnalysisViewModel(
            ThreatAnalysisResult result,
            IFileThreatAnalyzerService? analyzerService = null,
            IAutorunsScannerEngine? autorunsEngine = null,
            IVirusTotalCheckService? virusTotalService = null)
        {
            _result = result;
            _analyzerService = analyzerService ?? App.GetService<IFileThreatAnalyzerService>();
            _autorunsEngine = autorunsEngine;
            _virusTotalService = virusTotalService ?? new VirusTotalCheckService();

            StatusText = $"Analiz tamamlandı. Risk Skoru: %{result.RiskScore} ({result.RiskLevelText})";
            PreviousAnalysesText = BuildPreviousAnalysesText(result);
        }

        /// <summary>
        /// "Bu dosya daha önce 2 kez analiz edildi (son: 12 Eyl, Risk 58, Nöbetçi)" — Analizör Geçmişi'nden.
        /// Güvenilen dosyalar için bu da belirtilir.
        /// </summary>
        public string PreviousAnalysesText { get; }
        public bool HasPreviousAnalyses => PreviousAnalysesText.Length > 0;

        private static string BuildPreviousAnalysesText(ThreatAnalysisResult result)
        {
            try
            {
                var history = App.TryGetService<Services.History.IAnalysisHistoryService>();
                if (history == null || string.IsNullOrEmpty(result.Sha256)) return string.Empty;

                // Az önce kaydedilen bu analiz (son dakika içindeki en yeni kayıt) sayılmaz.
                var records = history.GetByHash(result.Sha256).ToList();
                if (records.Count > 0 && DateTime.UtcNow - records[0].AnalyzedAtUtc < TimeSpan.FromMinutes(1)) records.RemoveAt(0);

                bool trusted = history.IsTrustedHash(result.Sha256);
                if (records.Count == 0) return trusted ? "Bu dosyaya daha önce güvendiniz." : string.Empty;

                var last = records[0];
                string text = $"Bu dosya daha önce {records.Count} kez analiz edildi (son: {last.AnalyzedAtUtc.ToLocalTime():d MMM HH:mm}, " +
                              $"Risk {last.RiskScore}, {Core.History.AnalysisVerdicts.Display(last.Source)}).";
                return trusted ? text + " Güvendiğiniz bir dosya." : text;
            }
            catch
            {
                return string.Empty;
            }
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
            var result = await _analyzerService.KillProcessAsync(ActiveProcessId);

            if (result.Succeeded || result.Outcome == DeleteOutcome.NotFound)
            {
                IsProcessKilled = true;
                OnPropertyChanged(nameof(HasActiveProcess));
                StatusText = $"Süreç (PID: {ActiveProcessId}) sonlandırıldı.";
                MessageBox.Show($"'{FileName}' süreci sonlandırıldı.", "Süreç Durduruldu", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                string reason = result.Outcome == DeleteOutcome.Blocked
                    ? "Bu bir Windows sistem sürecidir; sistem kararlılığı için sonlandırılamaz."
                    : result.Message;
                StatusText = $"Süreç sonlandırılamadı: {reason}";
                MessageBox.Show($"Süreç sonlandırılamadı.\n\n{reason}", "Süreci Sonlandır", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                $"'{FilePath}' dosyası Geri Dönüşüm Kutusu'na taşınacak.\n\nYanlışlıkla silerseniz oradan geri yükleyebilirsiniz. Dosya kullanımdaysa bir sonraki yeniden başlatmada silinir.\n\nDevam edilsin mi?",
                "Dosyayı Kaldır",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            StatusText = "Dosya kaldırılıyor...";
            var result = await _analyzerService.RemoveFileAsync(FilePath);

            switch (result.Outcome)
            {
                case DeleteOutcome.Recycled:
                case DeleteOutcome.Deleted:
                case DeleteOutcome.NotFound:
                    IsFileDeleted = true;
                    StatusText = "Dosya Geri Dönüşüm Kutusu'na taşındı.";
                    MessageBox.Show($"'{FileName}' Geri Dönüşüm Kutusu'na taşındı.", "Dosya Kaldırıldı", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;

                case DeleteOutcome.ScheduledForReboot:
                    IsFileDeleted = true;
                    StatusText = "Dosya kullanımda; yeniden başlatmada silinecek.";
                    MessageBox.Show("Dosya kullanımda olduğu için hemen silinemedi; bilgisayar yeniden başlatıldığında silinecek.", "Yeniden Başlatmada Silinecek", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;

                case DeleteOutcome.Blocked:
                    StatusText = "Korumalı konum; dosya silinmedi.";
                    MessageBox.Show($"Bu dosya korumalı bir konumda olduğu için silinmedi.\n\n{result.Message}", "Korumalı Konum", MessageBoxButton.OK, MessageBoxImage.Stop);
                    break;

                default:
                    StatusText = $"Dosya kaldırılamadı: {result.Message}";
                    MessageBox.Show($"Dosya kaldırılamadı.\n\n{result.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
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
        public async Task OpenVirusTotalAsync()
        {
            if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
            {
                if (!string.IsNullOrWhiteSpace(Result.Sha256))
                {
                    _virusTotalService.SmartOpenInBrowser(string.Empty, Result.Sha256, Result.VirusTotalTotal);
                }
                return;
            }

            // If file already has a known record on VirusTotal, open the report directly
            if (HasVtRecord)
            {
                _virusTotalService.OpenInBrowser(Result.Sha256);
                return;
            }

            // File has NO record on VirusTotal (404 item not found)
            bool hasKey = _virusTotalService.HasApiKey;
            long size = Result.FileSizeBytes;

            if (hasKey && size <= 32 * 1024 * 1024)
            {
                var ask = MessageBox.Show(
                    $"Bu dosya daha önce VirusTotal'e hiç gönderilmemiş (VT veritabanında kaydı yok).\n\n" +
                    $"Dosyayı VirusTotal API üzerinden doğrudan yükleyip 70 antivirüs motoruyla taratmak ister misiniz?\n\n" +
                    $"(Dosya Boyutu: {Result.FileSizeFormatted})",
                    "VirusTotal'e Gönder ve Tara",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (ask == MessageBoxResult.Yes)
                {
                    try
                    {
                        IsUploadingToVt = true;
                        StatusText = "Dosya VirusTotal sunucularına yükleniyor...";

                        var progress = new Progress<string>(p => StatusText = p);
                        var uploadRes = await _virusTotalService.UploadFileAsync(FilePath, progress);

                        if (uploadRes.Success && !string.IsNullOrWhiteSpace(uploadRes.StatusUrl))
                        {
                            StatusText = "Yükleme başarılı! VirusTotal analiz sayfası açılıyor...";
                            MessageBox.Show(
                                "Dosya VirusTotal bulutuna başarıyla yüklendi!\n\nTarama kuyruğa alındı, analiz sonuç sayfası tarayıcınızda açılıyor.",
                                "Yükleme Tamamlandı",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);

                            _virusTotalService.OpenInBrowser(uploadRes.StatusUrl);
                        }
                        else
                        {
                            StatusText = $"Yükleme başarısız: {uploadRes.ErrorMessage}";
                            var fallback = MessageBox.Show(
                                $"API ile yükleme başarısız oldu: {uploadRes.ErrorMessage}\n\nVirusTotal web yükleme sayfası açılsın mı? (Dosya yolu panoya kopyalanıp Explorer açılacaktır)",
                                "Alternatif Yükleme",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);

                            if (fallback == MessageBoxResult.Yes)
                            {
                                _virusTotalService.OpenUploadPage(FilePath);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusText = $"Yükleme hatası: {ex.Message}";
                    }
                    finally
                    {
                        IsUploadingToVt = false;
                    }
                    return;
                }
                else if (ask == MessageBoxResult.No)
                {
                    _virusTotalService.OpenUploadPage(FilePath);
                    return;
                }
                return;
            }

            // No API key or size > 32 MB -> Open Web upload assist
            _virusTotalService.OpenUploadPage(FilePath);
        }

        [RelayCommand]
        public void CopyHash(string? hashValue)
        {
            if (string.IsNullOrWhiteSpace(hashValue)) return;
            try
            {
                Clipboard.SetText(hashValue);
                StatusText = $"Hash panoya kopyalandı: {hashValue.Substring(0, Math.Min(16, hashValue.Length))}...";
            }
            catch { }
        }

        [RelayCommand]
        public void CopyAllHashes()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Dosya: {FileName}");
            sb.AppendLine($"SHA-256: {Sha256}");
            sb.AppendLine($"MD5:     {Md5}");
            sb.AppendLine($"SHA-1:   {Sha1}");
            if (!string.IsNullOrWhiteSpace(ImpHash))
                sb.AppendLine($"ImpHash: {ImpHash}");

            try
            {
                Clipboard.SetText(sb.ToString());
                StatusText = "Tüm hash değerleri panoya kopyalandı.";
                MessageBox.Show("Tüm kriptografik hash değerleri panoya kopyalandı!", "Hash Değerleri Kopyalandı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        [RelayCommand]
        public void OpenVirusTotalWeb()
        {
            if (string.IsNullOrWhiteSpace(Sha256)) return;
            try
            {
                _virusTotalService.SmartOpenInBrowser(string.Empty, Sha256, Result.VirusTotalTotal);
            }
            catch { }
        }

        [RelayCommand]
        public void CopyReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Bakım Sezgisel Tehdit & PE Röntgen Analiz Raporu");
            sb.AppendLine($"**Dosya Adı:** {FileName}");
            sb.AppendLine($"**Tam Yol:** {FilePath}");
            sb.AppendLine($"**Risk Skoru:** %{RiskScore} ({RiskLevelText})");
            sb.AppendLine($"**Boyut:** {Result.FileSizeFormatted}");
            sb.AppendLine($"**SHA-256:** `{Result.Sha256}`");
            if (!string.IsNullOrWhiteSpace(Md5)) sb.AppendLine($"**MD5:** `{Md5}`");
            if (!string.IsNullOrWhiteSpace(Sha1)) sb.AppendLine($"**SHA-1:** `{Sha1}`");
            if (!string.IsNullOrWhiteSpace(ImpHash)) sb.AppendLine($"**ImpHash:** `{ImpHash}`");
            sb.AppendLine($"**Dijital İmza:** {Result.DigitalSignatureText}");
            sb.AppendLine($"**Entropi:** {Result.EntropyText}");
            sb.AppendLine($"**VirusTotal:** {Result.VirusTotalSummary}");
            sb.AppendLine();

            if (HasPeAnalysis && PeHeader != null)
            {
                sb.AppendLine($"## PE Binary Röntgeni");
                sb.AppendLine($"- **Mimari:** {PeHeader.MachineArchitecture}");
                sb.AppendLine($"- **Alt Sistem:** {PeHeader.Subsystem}");
                sb.AppendLine($"- **Derleme Zamanı:** {PeHeader.CompileTimeUtc}");
                sb.AppendLine($"- **Entry Point:** {PeHeader.EntryPointHex}");
                sb.AppendLine($"- **Bölüm Sayısı:** {PeHeader.SectionCount}");

                if (Mitigations != null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"### Exploit Koruma Kalkanları (Mitigations)");
                    sb.AppendLine($"- **ASLR (Adres Rastgeleleştirme):** {(Mitigations.HasAslr ? "AKTİF" : "DEVRE DIŞI")}");
                    sb.AppendLine($"- **DEP / NX (Veri Yürütme Koruması):** {(Mitigations.HasDep ? "AKTİF" : "DEVRE DIŞI")}");
                    sb.AppendLine($"- **CFG (Control Flow Guard):** {(Mitigations.HasCfg ? "AKTİF" : "DEVRE DIŞI")}");
                    sb.AppendLine($"- **High Entropy VA (64-bit ASLR):** {(Mitigations.HasHighEntropyVa ? "AKTİF" : "DEVRE DIŞI")}");
                    sb.AppendLine($"- **SafeSEH / SEH:** {(Mitigations.HasSafeSeh ? "AKTİF" : "DEVRE DIŞI")}");
                    sb.AppendLine($"- **Genel Koruma Seviyesi:** {Mitigations.SecurityGrade} ({Mitigations.MitigationScoreText})");
                }

                if (Sections.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"### PE Bölüm (Section) Tablosu");
                    foreach (var sec in Sections)
                    {
                        string packerTag = sec.IsSuspiciousPacker ? " [ŞÜPHELİ / PACKER]" : "";
                        sb.AppendLine($"- **{sec.Name}**: Sanal: {sec.VirtualSizeFormatted}, Ham: {sec.RawSizeFormatted}, Entropi: {sec.EntropyFormatted}, Yetki: {sec.PermissionsText}{packerTag}");
                    }
                }

                if (TotalSuspiciousApisCount > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"### Şüpheli Win32 API Çağrıları ({TotalSuspiciousApisCount} Adet)");
                    foreach (var dll in ImportedDlls.Where(d => d.HasSuspicious))
                    {
                        sb.AppendLine($"- **{dll.DllName}**:");
                        foreach (var fn in dll.Functions.Where(f => f.IsSuspicious))
                        {
                            sb.AppendLine($"  - `{fn.Name}`: {fn.Category} ({fn.Description})");
                        }
                    }
                }
                sb.AppendLine();
            }

            sb.AppendLine($"## Tespit Edilen Güvenlik Faktörleri");
            foreach (var f in Result.Factors)
            {
                string symbol = f.Severity == ThreatSeverity.Critical ? "[Kritik]" : (f.Severity == ThreatSeverity.Warning ? "[Uyarı]" : "[Güvenli]");
                sb.AppendLine($"- {symbol} **{f.Title}**: {f.Description}");
            }
            sb.AppendLine();
            sb.AppendLine($"## Tavsiye");
            sb.AppendLine(Recommendation);

            try
            {
                Clipboard.SetText(sb.ToString());
                StatusText = "Rapor panoya kopyalandı.";
                MessageBox.Show("Kapsamlı analiz raporu panoya kopyalandı!", "Rapor Kopyalandı", MessageBoxButton.OK, MessageBoxImage.Information);
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

        /// <summary>Segmented sekme çubuğundan sekme değiştirir.</summary>
        [RelayCommand]
        public void SelectTab(string? index)
        {
            if (int.TryParse(index, out int value) && value is >= 0 and <= 3)
            {
                SelectedTabIndex = value;
            }
        }

        [RelayCommand]
        public void Close()
        {
            RequestClose?.Invoke();
        }
    }
}
