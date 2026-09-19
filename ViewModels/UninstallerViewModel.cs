using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Bakım.Models;
using Bakım.Services;
using Bakım.Views.Dialogs;

namespace Bakım.ViewModels
{
    public partial class UninstallerViewModel : ObservableObject
    {
        private readonly IDeepUninstallerService _deepUninstaller;
        private readonly IResidualScannerEngine _residualScanner;
        private readonly IInstallerMonitorService _monitorService;
        private readonly IHunterService _hunterService;

        public UninstallerViewModel() : this((IDeepUninstallerService?)null, null, null, null)
        {
        }

        public UninstallerViewModel(IUninstallerService? uninstallerService) 
            : this(uninstallerService as IDeepUninstallerService ?? (uninstallerService != null ? new DeepUninstallerService(null, uninstallerService) : null), null, null, null)
        {
        }

        public UninstallerViewModel(
            IDeepUninstallerService? deepUninstaller = null,
            IResidualScannerEngine? residualScanner = null,
            IInstallerMonitorService? monitorService = null,
            IHunterService? hunterService = null)
        {
            _residualScanner = residualScanner ?? new ResidualScannerEngine();
            _deepUninstaller = deepUninstaller ?? new DeepUninstallerService(_residualScanner);
            _monitorService = monitorService ?? new InstallerMonitorService();
            _hunterService = hunterService ?? new HunterService();

            Apps = new ObservableCollection<InstalledAppItem>();
            Leftovers = new ObservableCollection<LeftoverItem>();

            _filteredApps = CollectionViewSource.GetDefaultView(Apps);
            _filteredApps.Filter = FilterAppItem;

            _ = RefreshAppsAsync();
        }

        private readonly ICollectionView _filteredApps;
        public ICollectionView FilteredApps => _filteredApps;

        public ObservableCollection<InstalledAppItem> Apps { get; }
        public ObservableCollection<LeftoverItem> Leftovers { get; }

        [ObservableProperty]
        private string _activeViewMode = "AppList"; // AppList, Leftovers

        [ObservableProperty]
        private InstalledAppItem? _selectedApp;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _selectedSort = "Size"; // Size, Name, Date, Publisher

        [ObservableProperty]
        private string _appFilterCategory = "UserOnly"; // "UserOnly", "SystemOnly", "All"

        [ObservableProperty]
        private int _userAppsCount;

        [ObservableProperty]
        private int _systemAppsCount;

        public bool IsUserOnlyFilter => AppFilterCategory == "UserOnly";
        public bool IsSystemOnlyFilter => AppFilterCategory == "SystemOnly";
        public bool IsAllFilter => AppFilterCategory == "All";

        public bool IsSortBySize => SelectedSort == "Size";
        public bool IsSortByName => SelectedSort == "Name";
        public bool IsSortByDate => SelectedSort == "Date";
        public bool IsSortByPublisher => SelectedSort == "Publisher";

        partial void OnAppFilterCategoryChanged(string value)
        {
            _filteredApps.Refresh();
            OnPropertyChanged(nameof(IsUserOnlyFilter));
            OnPropertyChanged(nameof(IsSystemOnlyFilter));
            OnPropertyChanged(nameof(IsAllFilter));
        }

        [ObservableProperty]
        private UninstallerStats _stats = new();

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = "Yüklü yazılımlar taranıyor...";

        // V30.0 Features
        [ObservableProperty]
        private bool _isAutoCleanEnabled = false;

        [ObservableProperty]
        private int _selectedAppsCount;

        [ObservableProperty]
        private bool _hasSelectedApps;

        [ObservableProperty]
        private bool _isBatchRunning;

        [ObservableProperty]
        private int _batchProgress;

        [ObservableProperty]
        private int _batchProgressMax = 100;

        [ObservableProperty]
        private string _batchStatusText = string.Empty;

        [ObservableProperty]
        private string _lastAutoCleanReport = string.Empty;

        partial void OnSearchTextChanged(string value)
        {
            _filteredApps.Refresh();
        }

        partial void OnSelectedSortChanged(string value)
        {
            ApplySorting();
            OnPropertyChanged(nameof(IsSortBySize));
            OnPropertyChanged(nameof(IsSortByName));
            OnPropertyChanged(nameof(IsSortByDate));
            OnPropertyChanged(nameof(IsSortByPublisher));
        }

        [RelayCommand]
        public void SetAppFilterCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return;
            AppFilterCategory = category;
        }

        private bool FilterAppItem(object obj)
        {
            if (obj is not InstalledAppItem item) return false;

            // 1. Kategori Filtresi (Korumalı sistem bileşenleri süzgeci)
            if (AppFilterCategory == "UserOnly" && item.IsSystemComponent)
            {
                return false;
            }
            if (AppFilterCategory == "SystemOnly" && !item.IsSystemComponent)
            {
                return false;
            }

            // 2. Arama Filtresi
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            string query = SearchText.Trim().ToLowerInvariant();
            return item.DisplayName.ToLowerInvariant().Contains(query) ||
                   item.Publisher.ToLowerInvariant().Contains(query) ||
                   item.DisplayVersion.ToLowerInvariant().Contains(query);
        }

        private void ApplySorting()
        {
            _filteredApps.SortDescriptions.Clear();
            switch (SelectedSort)
            {
                case "Name":
                    _filteredApps.SortDescriptions.Add(new SortDescription("DisplayName", ListSortDirection.Ascending));
                    break;
                case "Date":
                    _filteredApps.SortDescriptions.Add(new SortDescription("InstallDate", ListSortDirection.Descending));
                    break;
                case "Publisher":
                    _filteredApps.SortDescriptions.Add(new SortDescription("Publisher", ListSortDirection.Ascending));
                    break;
                case "Size":
                default:
                    _filteredApps.SortDescriptions.Add(new SortDescription("EstimatedSizeBytes", ListSortDirection.Descending));
                    break;
            }
        }

        [RelayCommand]
        public void SetSort(string sortKey)
        {
            SelectedSort = sortKey;
        }

        [RelayCommand]
        public async Task RefreshAppsAsync()
        {
            if (IsBusy || IsBatchRunning) return;

            IsBusy = true;
            StatusMessage = "32-bit ve 64-bit kayıt defterinden yüklü programlar taranıyor...";

            try
            {
                var list = await _deepUninstaller.GetInstalledAppsAsync();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Apps.Clear();
                    foreach (var a in list)
                    {
                        a.PropertyChanged += OnAppItemPropertyChanged;
                        Apps.Add(a);
                    }
                    ApplySorting();
                });

                UpdateStats();
                UpdateSelectedAppsCount();
                StatusMessage = $"{Stats.TotalAppsCount} program bulundu. Toplam disk boyutu: {Stats.FormattedTotalFootprint}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Hata: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OnAppItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(InstalledAppItem.IsSelected))
            {
                UpdateSelectedAppsCount();
            }
        }

        public void UpdateSelectedAppsCount()
        {
            SelectedAppsCount = Apps.Count(a => a.IsSelected);
            HasSelectedApps = SelectedAppsCount > 0;
        }

        private void UpdateStats()
        {
            long totalBytes = Apps.Sum(a => a.EstimatedSizeBytes);
            int systemCount = Apps.Count(a => a.IsSystemComponent);
            int userCount = Apps.Count(a => !a.IsSystemComponent);

            UserAppsCount = userCount;
            SystemAppsCount = systemCount;

            Stats = new UninstallerStats
            {
                TotalAppsCount = Apps.Count,
                TotalFootprintBytes = totalBytes,
                FormattedTotalFootprint = FormatBytes(totalBytes),
                SystemComponentsCount = systemCount,
                LeftoversFoundCount = Leftovers.Count,
                TotalLeftoverBytes = Leftovers.Sum(l => l.SizeBytes),
                FormattedTotalLeftovers = FormatBytes(Leftovers.Sum(l => l.SizeBytes))
            };
        }

        #region Standard & Auto-Clean Uninstallation

        [RelayCommand]
        public async Task UninstallAsync(InstalledAppItem? app)
        {
            if (app == null) return;

            if (app.IsSystemComponent)
            {
                MessageBox.Show(
                    $"{app.DisplayName} bir sistem bileşeni veya kritik çalışma zamanı (runtime) kütüphanesidir. Windows kararlılığı için kaldırılamaz.",
                    "Sistem Koruması",
                    MessageBoxButton.OK,
                    MessageBoxImage.Stop);
                return;
            }

            string autoCleanNotice = IsAutoCleanEnabled
                ? "Kaldırma sonrası güvenli kalıntılar (%100 eşleşmeler) otomatik olarak temizlenecektir."
                : "Kaldırma sonrası bulunan tüm kalıntılar onayınız için listelenecektir.";

            var confirm = MessageBox.Show(
                $"{app.DisplayName} uygulamasını kaldırmak istiyor musunuz?\n\n{autoCleanNotice}",
                "Program Kaldır",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            // Restore Point Confirmation & Settings Evaluation
            var settings = SettingsViewModel.LoadCurrentSettings();
            bool createRestorePoint = false;

            if (settings.PromptRestorePointBeforeUninstall)
            {
                var restoreChoice = MessageBox.Show(
                    $"{app.DisplayName} uygulaması kaldırılmak üzere.\n\n" +
                    "Sistem kararlılığını korumak için kaldırma işlemine başlamadan önce bir Windows Geri Yükleme Noktası oluşturulsun mu?\n\n" +
                    "• [Evet] -> Geri Yükleme Noktası Oluştur ve Kaldır (~15 sn)\n" +
                    "• [Hayır] -> Nokta Oluşturmadan Doğrudan Kaldır (Hızlı)\n" +
                    "• [İptal] -> Kaldırma İşlemini İptal Et",
                    "Kaldırma Güvenliği - Geri Yükleme Noktası",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (restoreChoice == MessageBoxResult.Cancel) return;
                createRestorePoint = (restoreChoice == MessageBoxResult.Yes);
            }
            else
            {
                createRestorePoint = settings.CreateRestorePointOnUninstall;
            }

            SelectedApp = app;
            app.IsBusy = true;
            IsBusy = true;

            try
            {
                // 1. Restore Point (if requested or enabled)
                if (createRestorePoint)
                {
                    StatusMessage = $"{app.DisplayName} için Windows Geri Yükleme Noktası oluşturuluyor...";
                    await _deepUninstaller.CreateRestorePointAsync(app.DisplayName);
                }

                // 2. Launch Uninstaller
                StatusMessage = $"{app.DisplayName} kaldırıcısı çalıştırılıyor...";
                bool launched = await _deepUninstaller.LaunchUninstallAsync(app, silent: false);

                if (!launched)
                {
                    var askForce = MessageBox.Show(
                        $"{app.DisplayName} standart kaldırıcısı çalıştırılamadı veya eksik. Zorla Kaldır (Force Uninstall) motoru ile temizlensin mi?",
                        "Kaldırıcı Hatası",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (askForce == MessageBoxResult.Yes)
                    {
                        await ForceUninstallAsync(app);
                    }
                    return;
                }

                // 3. Post-Uninstall Process Exit -> Auto or Interactive Residual Engine (V42.0)
                StatusMessage = $"{app.DisplayName} kaldırıldı. Kalıntı taraması yürütülüyor...";

                if (IsAutoCleanEnabled)
                {
                    long cleanedBytes = await _deepUninstaller.ExecuteAutoCleanResidualsAsync(app);
                    string cleanedFormatted = FormatBytes(cleanedBytes);
                    LastAutoCleanReport = $"{app.DisplayName} kaldırıldı ve {cleanedFormatted} kalıntı otomatik temizlendi.";

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Apps.Remove(app);
                    });

                    UpdateStats();
                    UpdateSelectedAppsCount();

                    MessageBox.Show(
                        $"{app.DisplayName} başarıyla kaldırıldı!\n\nOtomatik Temizlenen Kalıntı: {cleanedFormatted}",
                        "Kaldırma & Kalıntı Temizliği Başarılı",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    await ShowResidualCleanupDialogAsync(app);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kaldırma sırasında hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                app.IsBusy = false;
                IsBusy = false;
            }
        }

        #endregion

        #region Force Uninstall

        [RelayCommand]
        public async Task ForceUninstallAsync(InstalledAppItem? app)
        {
            if (app == null) return;

            if (app.IsSystemComponent)
            {
                MessageBox.Show(
                    $"{app.DisplayName} kritik bir sistem bileşenidir ve silinemez.",
                    "Korumalı Sistem",
                    MessageBoxButton.OK,
                    MessageBoxImage.Stop);
                return;
            }

            var confirm = MessageBox.Show(
                $"Zorla Kaldır Modu:\n{app.DisplayName} için uninstaller komutu atlanacak; ilişkili süreçler sonlandırılacak ve tüm dosya/kayıt defteri bağımlılık zinciri zorla sökülecektir.\n\nDevam edilsin mi?",
                "Zorla Kaldır (Force Uninstall)",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            // Restore Point Confirmation & Settings Evaluation
            var settings = SettingsViewModel.LoadCurrentSettings();
            bool createRestorePoint = false;

            if (settings.PromptRestorePointBeforeUninstall)
            {
                var restoreChoice = MessageBox.Show(
                    $"Zorla Kaldır Güvenliği:\n{app.DisplayName} için tüm bileşenler zorla silinecektir.\n\n" +
                    "İşlem öncesinde sisteminizi güvenceye almak için bir Windows Geri Yükleme Noktası oluşturulsun mu?\n\n" +
                    "• [Evet] -> Geri Yükleme Noktası Oluştur ve Zorla Kaldır (~15 sn)\n" +
                    "• [Hayır] -> Nokta Oluşturmadan Doğrudan Zorla Kaldır (Hızlı)\n" +
                    "• [İptal] -> İşlemi İptal Et",
                    "Zorla Kaldır - Geri Yükleme Noktası",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (restoreChoice == MessageBoxResult.Cancel) return;
                createRestorePoint = (restoreChoice == MessageBoxResult.Yes);
            }
            else
            {
                createRestorePoint = settings.CreateRestorePointOnUninstall;
            }

            SelectedApp = app;
            app.IsBusy = true;
            IsBusy = true;

            try
            {
                if (createRestorePoint)
                {
                    StatusMessage = $"{app.DisplayName} için Geri Yükleme Noktası oluşturuluyor...";
                    await _deepUninstaller.CreateRestorePointAsync(app.DisplayName);
                }
                StatusMessage = $"{app.DisplayName} zorla sökülüyor...";
                int cleanedCount = await _deepUninstaller.ExecuteForceUninstallAsync(app);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Apps.Remove(app);
                });

                UpdateStats();
                UpdateSelectedAppsCount();

                MessageBox.Show(
                    $"{app.DisplayName} başarıyla zorla kaldırıldı ve {cleanedCount} kalıntı silindi!",
                    "Zorla Kaldırma Tamamlandı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Zorla kaldırma sırasında hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                app.IsBusy = false;
                IsBusy = false;
            }
        }

        #endregion

        #region Batch Silent Uninstallation

        [RelayCommand]
        public async Task BatchUninstallAsync()
        {
            var selectedApps = Apps.Where(a => a.IsSelected && !a.IsSystemComponent).ToList();
            if (selectedApps.Count == 0)
            {
                MessageBox.Show("Lütfen toplu kaldırmak için en az bir program seçin.", "Seçim Yapılmadı", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"Seçilen {selectedApps.Count} adet program sırayla ve SESSİZCE (arka planda) kaldırılacak.\n\n" +
                (IsAutoCleanEnabled ? "Her programdan sonra kalıntılar otomatik temizlenecektir.\n\n" : "") +
                "Toplu kaldırma işlemini başlatmak istiyor musunuz?",
                "Sessiz & Toplu Kaldırma (Batch Uninstall)",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            IsBatchRunning = true;
            BatchProgress = 0;
            BatchProgressMax = selectedApps.Count;
            BatchStatusText = $"Toplu kaldırma hazırlanıyor... (0/{selectedApps.Count})";

            var progress = new Progress<BatchUninstallProgress>(p =>
            {
                BatchProgress = p.CurrentIndex;
                BatchStatusText = $"Kaldırılıyor ({p.CurrentIndex}/{p.TotalCount}): {p.CurrentAppName}";
            });

            try
            {
                var result = await _deepUninstaller.ExecuteBatchSilentUninstallAsync(selectedApps, IsAutoCleanEnabled, progress);

                // Remove successfully uninstalled apps from UI
                Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var app in selectedApps)
                    {
                        Apps.Remove(app);
                    }
                });

                UpdateStats();
                UpdateSelectedAppsCount();

                MessageBox.Show(
                    $"Toplu kaldırma tamamlandı!\n\n" +
                    $"Başarılı: {result.SuccessCount}\n" +
                    $"Başarısız / Atlanan: {result.FailedCount}\n" +
                    $"Kurtarılan Alan: {result.FormattedCleanedSize}",
                    "Toplu Kaldırma Raporu",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Toplu kaldırma sırasında hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBatchRunning = false;
                StatusMessage = "Toplu kaldırma tamamlandı.";
            }
        }

        [RelayCommand]
        public void SelectAllApps(string selectAll)
        {
            bool select = selectAll == "true" || selectAll == "True";
            foreach (var app in _filteredApps.OfType<InstalledAppItem>())
            {
                if (!app.IsSystemComponent)
                {
                    app.IsSelected = select;
                }
            }
            UpdateSelectedAppsCount();
        }

        #endregion

        #region Hunter Mode (Avcı Modu)

        [RelayCommand]
        public void LaunchHunterMode()
        {
            try
            {
                var hunterWindow = new HunterTargetWindow(_hunterService, Apps, OnHunterActionRequested);
                hunterWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Avcı Modu başlatılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnHunterActionRequested(HunterTargetInfo info, HunterAction action)
        {
            if (action == HunterAction.Uninstall)
            {
                var targetApp = info.MatchedApp ?? new InstalledAppItem
                {
                    DisplayName = !string.IsNullOrWhiteSpace(info.WindowTitle) ? info.WindowTitle : info.ProcessName,
                    InstallLocation = Path.GetDirectoryName(info.ExecutablePath) ?? string.Empty,
                    DisplayIconPath = info.ExecutablePath
                };
                SelectedApp = targetApp;
                _ = UninstallAsync(targetApp);
            }
            else if (action == HunterAction.ForceUninstall)
            {
                var targetApp = info.MatchedApp ?? new InstalledAppItem
                {
                    DisplayName = !string.IsNullOrWhiteSpace(info.WindowTitle) ? info.WindowTitle : info.ProcessName,
                    InstallLocation = Path.GetDirectoryName(info.ExecutablePath) ?? string.Empty,
                    DisplayIconPath = info.ExecutablePath
                };
                SelectedApp = targetApp;
                _ = ForceUninstallAsync(targetApp);
            }
        }

        #endregion

        #region Installation Monitor (Kurulum İzleyici)

        [RelayCommand]
        public async Task LaunchInstallationMonitorAsync()
        {
            var ofd = new OpenFileDialog
            {
                Title = "İzlenecek Kurulum Dosyasını Seçin",
                Filter = "Kurulum Paketleri (*.exe;*.msi)|*.exe;*.msi|Tüm Dosyalar (*.*)|*.*"
            };

            if (ofd.ShowDialog() != true) return;

            string installerPath = ofd.FileName;
            string appName = Path.GetFileNameWithoutExtension(installerPath);

            var confirm = MessageBox.Show(
                $"Kurulum İzleyici (Snapshot Engine):\n\n" +
                $"Hedef Paket: {Path.GetFileName(installerPath)}\n\n" +
                $"1. Kurulum öncesi Kayıt Defteri ve Disk Snapshot'ı alınacak.\n" +
                $"2. Kurulum başlatılacak ve tamamlanması beklenecektir.\n" +
                $"3. Kurulum sonrası ikinci Snapshot alınarak oluşturulan tüm kalıntılar delta loguna kaydedilecektir.\n\n" +
                $"Başlatılsın mı?",
                "Kurulum İzleyici",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            IsBusy = true;
            StatusMessage = $"{appName} için kurulum öncesi snapshot alınıyor...";

            try
            {
                // 1. Pre-Snapshot
                var preSnapshot = await _monitorService.TakePreInstallSnapshotAsync(appName);

                // 2. Launch Installer
                StatusMessage = $"{appName} kurulumu başlatıldı, tamamlanması bekleniyor...";
                var psi = new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                }

                // 3. Post-Snapshot & Save Delta
                StatusMessage = $"{appName} için kurulum sonrası delta hesaplanıyor ve kaydediliyor...";
                var delta = await _monitorService.TakePostInstallSnapshotAndSaveDeltaAsync(appName, preSnapshot);

                MessageBox.Show(
                    $"Kurulum başarıyla izlendi ve kaydedildi!\n\n" +
                    $"Eklenen Dosya: {delta.AddedFiles.Count}\n" +
                    $"Eklenen Klasör: {delta.AddedFolders.Count}\n" +
                    $"Eklenen Registry Anahtarı: {delta.AddedRegistryKeys.Count}\n" +
                    $"Toplam Boyut: {delta.FormattedSize}\n\n" +
                    $"Bu yazılım ileride kaldırıldığında %100 sıfır kalıntı ile temizlenebilecektir.",
                    "Kurulum İzleyici Tamamlandı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                await RefreshAppsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kurulum izleme sırasında hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Leftover View & Actions

        [RelayCommand]
        public async Task ShowResidualCleanupDialogAsync(InstalledAppItem? app)
        {
            if (app == null) return;

            StatusMessage = $"{app.DisplayName} için kalıntılar taranıyor...";
            IsBusy = true;

            try
            {
                var residualItems = await _residualScanner.ScanResidualItemsAsync(app);

                if (residualItems.Count > 0)
                {
                    bool wasCleaned = false;

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var dialog = new Bakım.Views.Dialogs.ResidualCleanupDialog(app, residualItems, _residualScanner);
                        if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
                        {
                            dialog.Owner = Application.Current.MainWindow;
                        }
                        wasCleaned = dialog.ShowDialog() == true;
                    });

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Apps.Remove(app);
                    });

                    UpdateStats();
                    UpdateSelectedAppsCount();

                    if (wasCleaned)
                    {
                        LastAutoCleanReport = $"{app.DisplayName} kaldırıldı ve kalıntıları temizlendi.";
                        StatusMessage = LastAutoCleanReport;
                    }
                    else
                    {
                        LastAutoCleanReport = $"{app.DisplayName} kaldırıldı (kalıntılar korundu).";
                        StatusMessage = LastAutoCleanReport;
                    }
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Apps.Remove(app);
                    });

                    UpdateStats();
                    UpdateSelectedAppsCount();

                    MessageBox.Show(
                        $"{app.DisplayName} başarıyla kaldırıldı!\nSistemde herhangi bir artık kalıntı tespit edilmedi.",
                        "Kaldırma Tamamlandı",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Kalıntı taraması sırasında hata: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task AnalyzeThreatAsync(InstalledAppItem? app)
        {
            if (app == null) return;

            string targetFile = string.Empty;
            if (!string.IsNullOrWhiteSpace(app.DisplayIconPath))
            {
                string cleanIcon = app.DisplayIconPath.Split(',')[0].Trim('\"', ' ');
                if (File.Exists(cleanIcon) && cleanIcon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    targetFile = cleanIcon;
                }
            }

            if (string.IsNullOrWhiteSpace(targetFile) && !string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
            {
                try
                {
                    var exe = Directory.GetFiles(app.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(exe))
                    {
                        targetFile = exe;
                    }
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(targetFile) || !File.Exists(targetFile))
            {
                MessageBox.Show($"{app.DisplayName} için incelenecek bir çalıştırılabilir (.exe) dosyası bulunamadı.", "Dosya Bulunamadı", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            StatusMessage = $"{app.DisplayName} için sezgisel AI tehdit analizi yürütülüyor...";
            IsBusy = true;

            try
            {
                var analyzer = new FileThreatAnalyzerService();
                var analysisResult = await analyzer.AnalyzeFileAsync(targetFile);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var dialog = new Bakım.Views.Dialogs.ThreatAnalysisDialog(analysisResult, analyzer);
                    if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
                    {
                        dialog.Owner = Application.Current.MainWindow;
                    }
                    dialog.ShowDialog();
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Tehdit analizi sırasında hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                StatusMessage = "Analiz tamamlandı.";
            }
        }

        private async Task ScanLeftoversInternalAsync(InstalledAppItem app)
        {
            var foundLeftovers = await _residualScanner.ScanResidualsAsync(app);

            Application.Current.Dispatcher.Invoke(() =>
            {
                Leftovers.Clear();
                foreach (var l in foundLeftovers) Leftovers.Add(l);
                ActiveViewMode = "Leftovers";
            });

            UpdateStats();
            StatusMessage = $"{app.DisplayName} için {Leftovers.Count} adet kalıntı bulundu ({Stats.FormattedTotalLeftovers}).";
        }

        [RelayCommand]
        public async Task CleanSelectedLeftoversAsync()
        {
            var selected = Leftovers.Where(l => l.IsSelected && !l.IsDeleted).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Temizlemek için en az bir kalıntı seçmelisiniz.", "Seçim Yapılmadı", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"Seçilen {selected.Count} adet dosya, klasör ve kayıt defteri kalıntısını kalıcı olarak temizlemek istediğinize emin misiniz?",
                "Kalıntıları Temizle",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            IsBusy = true;
            StatusMessage = "Seçili kalıntılar güvenle temizleniyor...";

            try
            {
                int cleaned = await _residualScanner.CleanResidualsAsync(selected);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var toRemove = Leftovers.Where(l => l.IsDeleted).ToList();
                    foreach (var r in toRemove) Leftovers.Remove(r);
                });

                UpdateStats();
                MessageBox.Show($"{cleaned} adet kalıntı başarıyla temizlendi!", "Temizlik Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);

                if (Leftovers.Count == 0 && SelectedApp != null)
                {
                    Apps.Remove(SelectedApp);
                    ActiveViewMode = "AppList";
                }
            }
            finally
            {
                IsBusy = false;
                StatusMessage = "Kalıntı temizleme işlemi tamamlandı.";
            }
        }

        [RelayCommand]
        public void SelectAllLeftovers(string selectAll)
        {
            bool isSelect = selectAll == "true" || selectAll == "True";
            foreach (var l in Leftovers)
            {
                l.IsSelected = isSelect;
            }
        }

        [RelayCommand]
        public void BackToAppList()
        {
            ActiveViewMode = "AppList";
        }

        [RelayCommand]
        public void OpenLocation(InstalledAppItem? app)
        {
            if (app == null) return;
            _deepUninstaller.OpenInstallLocation(app);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        #endregion
    }
}
