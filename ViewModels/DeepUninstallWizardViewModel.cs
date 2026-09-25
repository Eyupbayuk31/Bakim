using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Core.Text;
using Bakım.Helpers;
using Bakım.Models;
using Bakım.Services;
using Bakım.Services.Safety;
using Bakım.Services.Uninstall;

namespace Bakım.ViewModels
{
    public enum WizardStep
    {
        Ready = 1,
        Uninstalling = 2,
        Scanning = 3,
        Review = 4,
        Completed = 5
    }

    /// <summary>
    /// Derin kaldırma sihirbazı.
    ///
    /// v3.21 akışı: Onay (kapatılacak süreçler listelenir) → resmi kaldırıcı GERÇEKTEN
    /// bitene kadar beklenir → sonuç doğrulanır → yalnızca kaldırma doğrulandıysa
    /// kurulum klasörü ve Uninstall kaydı kalıntı sayılır → temizlik güvenli servislerle
    /// yapılır (Geri Dönüşüm Kutusu + gerçek .reg yedeği) → gerçek sonuç raporlanır.
    /// </summary>
    public partial class DeepUninstallWizardViewModel : ObservableObject
    {
        private readonly IDeepUninstallerService _deepUninstaller;
        private readonly IResidualScannerEngine _residualScanner;
        private readonly ISafeProcessService? _safeProcess;
        private readonly ICollectionView _filteredView;
        private IReadOnlyList<ProcessCandidate> _processesToClose = Array.Empty<ProcessCandidate>();
        private CancellationTokenSource? _waitCts;
        private string? _journalId;

        public InstalledAppItem TargetApp { get; }

        public ObservableCollection<ResidualItem> Residuals { get; } = new();
        public ICollectionView FilteredResiduals => _filteredView;

        /// <summary>Temizlenemeyen öğeler ve nedenleri (tamamlandı ekranı).</summary>
        public ObservableCollection<string> FailedItems { get; } = new();

        [ObservableProperty]
        private WizardStep _currentStep = WizardStep.Ready;

        public int StepNumber => (int)CurrentStep;

        partial void OnCurrentStepChanged(WizardStep value)
        {
            OnPropertyChanged(nameof(StepNumber));
        }

        // Adım 1 seçenekleri
        [ObservableProperty]
        private bool _createRestorePoint = true;

        [ObservableProperty]
        private bool _killRelatedProcesses = true;

        /// <summary>
        /// Kayıt defteri yedeği artık her zaman alınır (SafeRegistryService yedek
        /// alamazsa silmez). Özellik geriye dönük bağlama için tutulur.
        /// </summary>
        [ObservableProperty]
        private bool _backupRegistryBeforeClean = true;

        [ObservableProperty]
        private string _processesToCloseText = string.Empty;

        [ObservableProperty]
        private bool _canKillProcesses;

        [ObservableProperty]
        private string _restorePointHint = "Kaldırmadan önce Windows geri yükleme noktası oluşturur. Windows 24 saatte en fazla bir nokta oluşturur.";

        // Adım 2 ve 3 durumu
        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private string _liveProcessStatus = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _canStopWaiting;

        // Adım 4: kaldırma sonucu
        [ObservableProperty]
        private bool _isUninstallConfirmed;

        [ObservableProperty]
        private string _uninstallBannerTitle = string.Empty;

        [ObservableProperty]
        private string _uninstallBannerDetail = string.Empty;

        // Adım 4: filtre ve seçim
        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _selectedFilter = "All"; // All, Registry, Folders, Files

        [ObservableProperty]
        private bool _isAllSelected = true;

        [ObservableProperty]
        private int _totalCount;

        [ObservableProperty]
        private int _selectedCount;

        [ObservableProperty]
        private long _selectedSizeBytes;

        [ObservableProperty]
        private string _formattedSelectedSize = "0 B";

        // Adım 5: rapor
        [ObservableProperty]
        private int _cleanedCount;

        [ObservableProperty]
        private long _cleanedSizeBytes;

        [ObservableProperty]
        private string _formattedCleanedSize = "0 B";

        [ObservableProperty]
        private string _completionTitle = string.Empty;

        [ObservableProperty]
        private string _completionDetail = string.Empty;

        [ObservableProperty]
        private bool _hasFailures;

        [ObservableProperty]
        private string _registryBackupFilePath = string.Empty;

        [ObservableProperty]
        private bool _hasRegistryBackup;

        public event Action<bool>? RequestClose;

        public string AppName => !string.IsNullOrWhiteSpace(TargetApp.DisplayName)
            ? TargetApp.DisplayName
            : "Program";

        public string Publisher => !string.IsNullOrWhiteSpace(TargetApp.Publisher)
            ? TargetApp.Publisher
            : "Bilinmeyen Yayıncı";

        public string DisplayVersion => !string.IsNullOrWhiteSpace(TargetApp.DisplayVersion)
            ? TargetApp.DisplayVersion
            : "—";

        public string InstallLocation => !string.IsNullOrWhiteSpace(TargetApp.InstallLocation)
            ? TargetApp.InstallLocation
            : (!string.IsNullOrWhiteSpace(TargetApp.DisplayIconPath) ? Path.GetDirectoryName(TargetApp.DisplayIconPath.Split(',')[0].Trim('"')) ?? string.Empty : "Bilinmeyen Konum");

        public string FormattedSize => TargetApp.FormattedSize;

        private bool HasUninstallCommand =>
            !string.IsNullOrWhiteSpace(TargetApp.UninstallString) || !string.IsNullOrWhiteSpace(TargetApp.QuietUninstallString);

        public DeepUninstallWizardViewModel(
            InstalledAppItem targetApp,
            IDeepUninstallerService deepUninstaller,
            IResidualScannerEngine residualScanner)
            : this(targetApp, deepUninstaller, residualScanner, App.TryGetService<ISafeProcessService>())
        {
        }

        public DeepUninstallWizardViewModel(
            InstalledAppItem targetApp,
            IDeepUninstallerService deepUninstaller,
            IResidualScannerEngine residualScanner,
            ISafeProcessService? safeProcess)
        {
            TargetApp = targetApp;
            _deepUninstaller = deepUninstaller;
            _residualScanner = residualScanner;
            _safeProcess = safeProcess;

            _filteredView = CollectionViewSource.GetDefaultView(Residuals);
            _filteredView.Filter = FilterResidualItem;

            if (!UacHelper.IsAdministrator())
            {
                RestorePointHint = "Yönetici olarak çalışmadığı için geri yükleme noktası oluşturulamaz.";
            }

            RefreshProcessesToClose();
            StatusMessage = $"{AppName} için kaldırma ve kalıntı temizleme işlemi başlatılmaya hazır.";
        }

        /// <summary>Kapatılacak süreçleri önceden listeler (kullanıcı neyin kapanacağını görür).</summary>
        public void RefreshProcessesToClose()
        {
            if (_safeProcess == null)
            {
                CanKillProcesses = false;
                ProcessesToCloseText = "Süreç denetimi kullanılamıyor.";
                return;
            }

            string? folder = !string.IsNullOrWhiteSpace(TargetApp.InstallLocation) ? TargetApp.InstallLocation : null;
            _processesToClose = _safeProcess.FindProcessesUnder(folder, out string? refusal);

            if (refusal != null)
            {
                CanKillProcesses = false;
                KillRelatedProcesses = false;
                ProcessesToCloseText = folder == null
                    ? "Kurulum klasörü bilinmediği için süreç kapatılmayacak."
                    : $"Güvenli kapsam dışında olduğu için süreç kapatılmayacak: {refusal}";
            }
            else if (_processesToClose.Count == 0)
            {
                CanKillProcesses = false;
                ProcessesToCloseText = "Programa ait açık süreç yok.";
            }
            else
            {
                CanKillProcesses = true;
                ProcessesToCloseText = "Kapatılacak: " + string.Join(", ", _processesToClose.Select(p => $"{p.Name} (PID {p.ProcessId})"));
            }
        }

        partial void OnSearchTextChanged(string value)
        {
            _filteredView.Refresh();
        }

        private bool FilterResidualItem(object obj)
        {
            if (obj is not ResidualItem item) return false;

            bool categoryMatch = SelectedFilter switch
            {
                "Registry" => item.Type is ResidualType.RegistryKey or ResidualType.RegistryValue,
                "Folders" => item.Type == ResidualType.Folder,
                "Files" => item.Type == ResidualType.File,
                _ => true
            };

            if (!categoryMatch) return false;
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            string query = SearchText.Trim();
            return item.Path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   item.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   item.EvidenceText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   item.TypeName.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ResidualItem.IsSelected))
            {
                UpdateCalculations();
            }
        }

        private void UpdateCalculations()
        {
            TotalCount = Residuals.Count;
            var selectedItems = Residuals.Where(r => r.IsSelected && !r.IsDeleted).ToList();
            SelectedCount = selectedItems.Count;
            SelectedSizeBytes = selectedItems.Sum(r => r.SizeInBytes);
            FormattedSelectedSize = ByteFormatter.Format(SelectedSizeBytes);
            IsAllSelected = TotalCount > 0 && SelectedCount == TotalCount;
        }

        #region Adımlar

        [RelayCommand]
        public async Task StartUninstallAsync()
        {
            CurrentStep = WizardStep.Uninstalling;
            IsBusy = true;
            Residuals.Clear();

            try
            {
                if (CreateRestorePoint && UacHelper.IsAdministrator())
                {
                    StatusMessage = "Windows geri yükleme noktası oluşturuluyor...";
                    LiveProcessStatus = "Geri yükleme noktası oluşturuluyor...";
                    var rp = await _deepUninstaller.CreateRestorePointDetailedAsync(AppName);
                    LiveProcessStatus = rp.Message;
                }

                if (KillRelatedProcesses && CanKillProcesses && _safeProcess != null)
                {
                    StatusMessage = "Programa ait açık süreçler kapatılıyor...";
                    RefreshProcessesToClose();
                    var results = await _safeProcess.TerminateAsync(_processesToClose);
                    int closed = results.Count(r => r.Succeeded);
                    LiveProcessStatus = $"{closed}/{results.Count} süreç kapatıldı.";
                }

                if (!HasUninstallCommand)
                {
                    // Kaldırıcısı olmayan (taşınabilir ya da bozuk kayıtlı) program.
                    IsUninstallConfirmed = false;
                    UninstallBannerTitle = "Resmi kaldırıcı bulunamadı";
                    UninstallBannerDetail = "Bu program için bir kaldırma komutu kayıtlı değil. Aşağıda yalnızca programın klasörü ve ad eşleşmesiyle bulunan öğeler listeleniyor; lütfen tek tek inceleyin.";
                    await ScanHeuristicInternalAsync();
                    return;
                }

                _waitCts = new CancellationTokenSource();
                CanStopWaiting = true;

                var progress = new Progress<string>(msg => LiveProcessStatus = msg);
                StatusMessage = $"{AppName} resmi kaldırıcısı çalışıyor. Kaldırıcı penceresindeki adımları tamamlayın.";
                var run = await _deepUninstaller.RunUninstallAsync(TargetApp, silent: false, progress, _waitCts.Token);

                CanStopWaiting = false;
                IsUninstallConfirmed = run.IsRemoved;

                if (run.IsRemoved)
                {
                    UninstallBannerTitle = run.Outcome == UninstallOutcome.RebootRequired
                        ? $"{AppName} kaldırıldı (yeniden başlatma gerekiyor)"
                        : $"{AppName} kaldırıldı";
                    UninstallBannerDetail = "Kaldırma doğrulandı. Aşağıda geride kalan öğeler listeleniyor; yalnızca yüksek güvenli olanlar seçili gelir.";
                    await ScanResidualsInternalAsync(ResidualScanOptions.Confirmed);
                }
                else
                {
                    UninstallBannerTitle = run.Outcome switch
                    {
                        UninstallOutcome.Cancelled => "Kaldırma tamamlanmadı",
                        UninstallOutcome.TimedOut => "Kaldırıcı hâlâ bitmedi",
                        UninstallOutcome.Failed => "Kaldırıcı başlatılamadı",
                        _ => "Program hâlâ kurulu"
                    };
                    UninstallBannerDetail = run.Detail + " Program hâlâ kurulu göründüğü için hiçbir dosya ya da kayıt silinmeye önerilmiyor. Tekrar deneyebilir ya da Kaldırıcı ekranındaki 'Zorla Kaldır' seçeneğini kullanabilirsiniz.";
                    StatusMessage = run.Detail;
                    CurrentStep = WizardStep.Review;
                    UpdateCalculations();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Kaldırma sihirbazında hata.", ex, nameof(DeepUninstallWizardViewModel));
                IsUninstallConfirmed = false;
                UninstallBannerTitle = "Kaldırma sırasında hata";
                UninstallBannerDetail = ex.Message + " Güvenlik için hiçbir öğe silinmeye önerilmiyor.";
                StatusMessage = $"Kaldırma işlemi sırasında hata oluştu: {ex.Message}";
                CurrentStep = WizardStep.Review;
            }
            finally
            {
                CanStopWaiting = false;
                _waitCts?.Dispose();
                _waitCts = null;
                IsBusy = false;
            }
        }

        /// <summary>
        /// Kaldırıcıyı beklemeyi bırakır. Kaldırıcı ÖLDÜRÜLMEZ; sihirbaz sonucu
        /// o anki duruma göre doğrular.
        /// </summary>
        [RelayCommand]
        public void StopWaiting()
        {
            _waitCts?.Cancel();
            LiveProcessStatus = "Bekleme bırakıldı; kaldırma sonucu kontrol ediliyor...";
        }

        public async Task ScanResidualsInternalAsync(ResidualScanOptions options)
        {
            CurrentStep = WizardStep.Scanning;
            IsBusy = true;
            StatusMessage = "Dosya sistemi ve kayıt defterindeki kalıntılar taranıyor...";
            LiveProcessStatus = "Kalıntılar taranıyor...";

            try
            {
                var progress = new Progress<string>(msg => LiveProcessStatus = msg);
                var items = await _residualScanner.ScanResidualItemsAsync(TargetApp, options, progress);
                ShowResiduals(items);
            }
            catch (Exception ex)
            {
                AppLog.Error("Kalıntı taraması başarısız.", ex, nameof(DeepUninstallWizardViewModel));
                StatusMessage = $"Kalıntı taraması sırasında hata: {ex.Message}";
                CurrentStep = WizardStep.Review;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ScanHeuristicInternalAsync()
        {
            CurrentStep = WizardStep.Scanning;
            IsBusy = true;
            try
            {
                string target = !string.IsNullOrWhiteSpace(TargetApp.InstallLocation)
                    ? TargetApp.InstallLocation
                    : TargetApp.DisplayIconPath.Split(',')[0].Trim('"');
                var leftovers = await _residualScanner.ScanHeuristicResidualsAsync(target, AppName);
                var items = leftovers.Select(l => new ResidualItem
                {
                    Path = l.Path,
                    Type = l.ItemType switch
                    {
                        LeftoverType.File => ResidualType.File,
                        LeftoverType.RegistryKey => ResidualType.RegistryKey,
                        LeftoverType.RegistryValue => ResidualType.RegistryValue,
                        _ => ResidualType.Folder
                    },
                    SizeInBytes = l.SizeBytes,
                    Description = l.Description,
                    EvidenceText = l.EvidenceText,
                    ConfidenceScore = l.ConfidenceScore,
                    IsSelected = false // Kaldırıcısız programda hiçbir şey otomatik seçilmez.
                }).ToList();
                ShowResiduals(items);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ShowResiduals(IReadOnlyList<ResidualItem> items)
        {
            Application.Current?.Dispatcher.Invoke(() => AddResiduals(items));
            if (Application.Current == null) AddResiduals(items);

            CurrentStep = WizardStep.Review;
            StatusMessage = TotalCount == 0
                ? "Geride kalan öğe bulunamadı."
                : $"{TotalCount} öğe bulundu. Silinmesini istemediklerinizin işaretini kaldırın.";
        }

        private void AddResiduals(IReadOnlyList<ResidualItem> items)
        {
            Residuals.Clear();
            foreach (var itm in items)
            {
                itm.PropertyChanged += OnItemPropertyChanged;
                Residuals.Add(itm);
            }
            UpdateCalculations();
        }

        [RelayCommand]
        public async Task CleanSelectedResidualsAsync()
        {
            var selected = Residuals.Where(r => r.IsSelected && !r.IsDeleted).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Lütfen temizlenecek en az bir öğe seçin.", "Seçim Yapılmadı", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsBusy = true;
            StatusMessage = "Seçilen öğeler temizleniyor (dosyalar Geri Dönüşüm Kutusu'na, kayıtlar yedeklenerek)...";

            try
            {
                var progress = new Progress<string>(msg => StatusMessage = msg);
                var report = await _residualScanner.CleanResidualItemsDetailedAsync(selected, $"Kaldırma: {AppName}", progress);
                _journalId = report.JournalId;

                CleanedCount = report.SucceededCount;
                CleanedSizeBytes = report.BytesFreed;
                FormattedCleanedSize = ByteFormatter.Format(CleanedSizeBytes);

                FailedItems.Clear();
                foreach (var failure in report.Results.Where(r => !r.Succeeded && r.Outcome != DeleteOutcome.NotFound))
                {
                    FailedItems.Add($"{failure.Target} — {failure.Message}");
                }
                HasFailures = FailedItems.Count > 0;

                HasRegistryBackup = report.HasRegistryBackup;
                RegistryBackupFilePath = UndoJournal.GetDirectory(report.JournalId);

                CompletionTitle = HasFailures
                    ? $"{CleanedCount} öğe temizlendi, {FailedItems.Count} öğe temizlenemedi"
                    : $"{CleanedCount} öğe temizlendi";
                CompletionDetail = "Dosyalar Geri Dönüşüm Kutusu'na taşındı. Kayıt defteri öğeleri silinmeden önce yedeklendi ve geri yüklenebilir.";

                CurrentStep = WizardStep.Completed;
                StatusMessage = CompletionTitle;
            }
            catch (Exception ex)
            {
                AppLog.Error("Kalıntı temizliği başarısız.", ex, nameof(DeepUninstallWizardViewModel));
                MessageBox.Show($"Kalıntılar temizlenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Bu oturumda silinen kayıt defteri öğelerini yedekten geri yükler.</summary>
        [RelayCommand]
        public async Task RestoreRegistryAsync()
        {
            if (string.IsNullOrEmpty(_journalId)) return;

            var confirm = MessageBox.Show(
                "Bu kaldırma oturumunda silinen kayıt defteri öğeleri yedekten geri yüklensin mi?",
                "Kayıt Defterini Geri Yükle", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            var (restored, failed) = await UndoJournal.RestoreRegistryAsync(_journalId);
            StatusMessage = failed == 0
                ? $"{restored} kayıt defteri yedeği geri yüklendi."
                : $"{restored} yedek geri yüklendi, {failed} yedek geri yüklenemedi (yönetici gerekebilir).";
        }

        /// <summary>Geri Dönüşüm Kutusu'nu açar (silinen dosyalar buradan geri alınabilir).</summary>
        [RelayCommand]
        public void OpenRecycleBin()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "shell:RecycleBinFolder", UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppLog.Warning("Geri Dönüşüm Kutusu açılamadı.", ex, nameof(DeepUninstallWizardViewModel));
            }
        }

        [RelayCommand]
        public void SkipCleanup()
        {
            // Program kaldırıldıysa listeden çıkması gerekir; kalıntıları bırakmak bunu değiştirmez.
            RequestClose?.Invoke(IsUninstallConfirmed);
        }

        [RelayCommand]
        public void Close()
        {
            RequestClose?.Invoke(IsUninstallConfirmed);
        }

        [RelayCommand]
        public void SetFilter(string filter)
        {
            SelectedFilter = filter;
            _filteredView.Refresh();
        }

        [RelayCommand]
        public void ToggleSelectAll()
        {
            SelectAll(!IsAllSelected);
        }

        [RelayCommand]
        public void SelectAll(bool select)
        {
            IsAllSelected = select;
            foreach (var item in Residuals)
            {
                item.IsSelected = select;
            }
            UpdateCalculations();
        }

        [RelayCommand]
        public void SelectSafeOnly()
        {
            foreach (var item in Residuals)
            {
                item.IsSelected = item.IsSafeToDelete;
            }
            UpdateCalculations();
        }

        [RelayCommand]
        public void OpenLocation(ResidualItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Path)) return;

            try
            {
                if (item.Type == ResidualType.Folder && Directory.Exists(item.Path))
                {
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{item.Path}\"", UseShellExecute = true });
                }
                else if (item.Type == ResidualType.File && File.Exists(item.Path))
                {
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{item.Path}\"", UseShellExecute = true });
                }
                else if (item.Type is ResidualType.RegistryKey or ResidualType.RegistryValue)
                {
                    Clipboard.SetText(item.Path);
                    StatusMessage = $"Kayıt defteri yolu panoya kopyalandı: {item.Path}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Konum açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void CopyPath(ResidualItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Path)) return;
            try
            {
                Clipboard.SetText(item.Path);
                StatusMessage = $"Yol panoya kopyalandı: {item.Path}";
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                AppLog.Debug($"Pano kullanılamadı: {ex.Message}", nameof(DeepUninstallWizardViewModel));
            }
        }

        [RelayCommand]
        public void OpenBackupFolder()
        {
            if (string.IsNullOrWhiteSpace(RegistryBackupFilePath) || !Directory.Exists(RegistryBackupFilePath)) return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{RegistryBackupFilePath}\"", UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppLog.Warning("Yedek klasörü açılamadı.", ex, nameof(DeepUninstallWizardViewModel));
            }
        }

        #endregion
    }
}
