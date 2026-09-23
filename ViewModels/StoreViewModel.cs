using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Models;
using Bakım.Services;
using Bakım.Helpers;
using Wpf.Ui.Controls;

namespace Bakım.ViewModels
{
    public partial class StoreViewModel : ObservableObject, IModuleViewModel
    {
        private readonly IStoreService _storeService;
        private readonly ILogService _log;
        private readonly IClassicAppsService _classicAppsService;
        private readonly List<StoreAppItem> _masterCatalog = new();
        private readonly List<ClassicAppItem> _masterClassicTools = new();
        private CancellationTokenSource? _currentCts;

        public ObservableCollection<StoreAppItem> DisplayApps { get; } = new();
        public ObservableCollection<StorePresetItem> Presets { get; } = new();

        #region Store Tab Navigation

        [ObservableProperty]
        private int _selectedStoreTabIndex = 0; // 0 = Uygulama Kataloğu, 1 = Hazır Paketler & Format Kurtarıcı, 2 = Windows Konsolları & Klasik Araçlar

        public bool IsCatalogTab => SelectedStoreTabIndex == 0;
        public bool IsPresetsTab => SelectedStoreTabIndex == 1;
        public bool IsClassicToolsTab => SelectedStoreTabIndex == 2;

        partial void OnSelectedStoreTabIndexChanged(int value)
        {
            OnPropertyChanged(nameof(IsCatalogTab));
            OnPropertyChanged(nameof(IsPresetsTab));
            OnPropertyChanged(nameof(IsClassicToolsTab));

            if (value == 2)
            {
                _ = RefreshClassicToolsStateAsync();
            }
        }

        [RelayCommand]
        public void SwitchToCatalog() => SelectedStoreTabIndex = 0;

        [RelayCommand]
        public void SwitchToPresets() => SelectedStoreTabIndex = 1;

        [RelayCommand]
        public void SwitchToClassicTools() => SelectedStoreTabIndex = 2;

        #endregion

        #region View Mode (Grid vs Compact List)

        [ObservableProperty]
        private bool _isGridView = true;

        [RelayCommand]
        public void SetGridView() => IsGridView = true;

        [RelayCommand]
        public void SetListView() => IsGridView = false;

        #endregion

        #region KPI Metrics

        [ObservableProperty]
        private int _totalAppsCount;

        [ObservableProperty]
        private int _installedAppsCount;

        [ObservableProperty]
        private int _availableAppsCount;

        [ObservableProperty]
        private int _selectedAppsCount;

        [ObservableProperty]
        private string _selectedAppsText = "0 Paket";

        #endregion

        #region Filters & Category Selection

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private StoreCategory _selectedCategory = StoreCategory.All;

        [ObservableProperty]
        private bool _onlyUninstalled;

        #endregion

        #region Queue & Drawer Status

        [ObservableProperty]
        private bool _isQueueDrawerOpen;

        [ObservableProperty]
        private bool _isInstalling;

        [ObservableProperty]
        private string _currentAppName = string.Empty;

        [ObservableProperty]
        private string _currentStatusText = "Hazır";

        [ObservableProperty]
        private int _overallProgress;

        [ObservableProperty]
        private string _queueProgressSummary = "0 / 0";

        [ObservableProperty]
        private string _consoleLogs = "Mağaza ve Runtimes motoru hazır.\n";

        [ObservableProperty]
        private bool _isConsoleExpanded;

        [ObservableProperty]
        private bool _isAutoScrollEnabled = true;

        #endregion

        public StoreViewModel(IStoreService storeService, ILogService log, IClassicAppsService classicAppsService)
        {
            _storeService = storeService;
            _log = log;
            _classicAppsService = classicAppsService;

            _masterCatalog = _storeService.GetCatalog();
            foreach (var app in _masterCatalog)
            {
                app.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(StoreAppItem.IsSelected))
                    {
                        UpdateSelectionMetrics();
                    }
                };
            }

            ApplyFilters();
            UpdateKpiMetrics();
            InitializePresets();
            UpdatePresetMetrics();
            _ = LoadClassicToolsAsync();
        }

        public async Task OnActivatedAsync()
        {
            await RefreshInstalledStatusAsync();
            await LoadClassicToolsAsync();
        }

        public Task OnDeactivatedAsync()
        {
            return Task.CompletedTask;
        }

        [RelayCommand]
        public async Task RefreshInstalledStatusAsync()
        {
            await _storeService.CheckInstalledStatusesAsync(_masterCatalog);
            UpdateKpiMetrics();
            UpdatePresetMetrics();
            ApplyFilters();
        }

        #region Filter Logic

        partial void OnSearchQueryChanged(string value) => ApplyFilters();
        partial void OnSelectedCategoryChanged(StoreCategory value) => ApplyFilters();
        partial void OnOnlyUninstalledChanged(bool value) => ApplyFilters();

        [RelayCommand]
        public void SetCategory(string categoryName)
        {
            if (Enum.TryParse<StoreCategory>(categoryName, true, out var cat))
            {
                SelectedCategory = cat;
            }
        }

        private void ApplyFilters()
        {
            var query = _masterCatalog.AsEnumerable();

            if (SelectedCategory != StoreCategory.All)
            {
                query = query.Where(a => a.Category == SelectedCategory);
            }

            if (OnlyUninstalled)
            {
                query = query.Where(a => !a.IsInstalled);
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                string q = SearchQuery.Trim().ToLowerInvariant();
                query = query.Where(a =>
                    a.Name.ToLowerInvariant().Contains(q) ||
                    a.Description.ToLowerInvariant().Contains(q) ||
                    a.Publisher.ToLowerInvariant().Contains(q) ||
                    a.CategoryDisplayName.ToLowerInvariant().Contains(q));
            }

            DisplayApps.Clear();
            foreach (var item in query)
            {
                DisplayApps.Add(item);
            }
        }

        private void UpdateKpiMetrics()
        {
            TotalAppsCount = _masterCatalog.Count;
            InstalledAppsCount = _masterCatalog.Count(a => a.IsInstalled);
            AvailableAppsCount = TotalAppsCount - InstalledAppsCount;
            UpdateSelectionMetrics();
        }

        private void UpdateSelectionMetrics()
        {
            var selected = _masterCatalog.Where(a => a.IsSelected).ToList();
            SelectedAppsCount = selected.Count;
            SelectedAppsText = $"{SelectedAppsCount} Paket Seçildi";
        }

        #endregion

        #region 1-Click Presets & Curated Bundles

        private void InitializePresets()
        {
            if (Presets.Count > 0) return;

            Presets.Add(new StorePresetItem
            {
                Id = "format_essentials",
                Title = "Format Sonrası Temel Paket",
                Subtitle = "VC++ AIO, DirectX, Chrome, WinRAR, 7-Zip, Spotify, VLC, Discord",
                Badge = "Format Kurtarıcı",
                Description = "Temiz kurulum sonrası oyunların ve programların açılması için zorunlu C++ ve DirectX kütüphaneleri, en popüler web tarayıcı, arşiv yöneticileri ve medya araçları.",
                IconSymbol = SymbolRegular.Flash24,
                IncludedApps = new List<string> { "VC++ 2005-2022 AIO", "DirectX Web Setup", "Google Chrome", "WinRAR", "7-Zip", "Spotify", "VLC Player", "Discord" },
                TargetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "tpu_vcredist_aio", "directx_web_setup", "chrome", "winrar", "seven_zip", "spotify", "vlc_player", "discord_gaming"
                }
            });

            Presets.Add(new StorePresetItem
            {
                Id = "gamer_pack",
                Title = "Oyuncu & Gaming Platformları",
                Subtitle = "Steam, Epic Games, Discord, Spotify, VC++ AIO, DirectX, WinRAR",
                Badge = "Oyun & İstemciler",
                Description = "Oyunların eksik DLL (d3dx9, msvcp) hatalarını giderir; Steam, Epic Games, Discord sesli iletişim ve müzik servislerini tek tıkla hazır hale getirir.",
                IconSymbol = SymbolRegular.Games24,
                IncludedApps = new List<string> { "Steam", "Epic Games", "Discord", "Spotify", "VC++ AIO", "DirectX Web", "WinRAR" },
                TargetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "tpu_vcredist_aio", "directx_web_setup", "steam", "epic_games", "discord_gaming", "spotify", "winrar"
                }
            });

            Presets.Add(new StorePresetItem
            {
                Id = "office_daily",
                Title = "Ofis, Üretkenlik & İletişim",
                Subtitle = "Chrome, WhatsApp, Telegram, 7-Zip, Notepad++, PowerToys, VLC",
                Badge = "İş & Günlük",
                Description = "Ofis çalışanları, öğrenciler ve ev kullanıcıları için modern web tarayıcı, masaüstü mesajlaşma, gelişmiş metin editörü ve sistem verimlilik araçları.",
                IconSymbol = SymbolRegular.Briefcase24,
                IncludedApps = new List<string> { "Google Chrome", "WhatsApp", "Telegram", "7-Zip", "Notepad++", "Microsoft PowerToys", "VLC Player" },
                TargetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "chrome", "whatsapp", "telegram", "seven_zip", "notepad_plus_plus", "powertoys", "vlc_player"
                }
            });

            Presets.Add(new StorePresetItem
            {
                Id = "developer_pack",
                Title = "Yazılımcı & Geliştirici Ortamı",
                Subtitle = "VS Code, Git, Python 3, Node.js LTS, Terminal, PowerToys, 7-Zip",
                Badge = "Kodlama & DevOps",
                Description = "Yazılım geliştiriciler için dünyanın en popüler kod editörü VS Code, Git sürüm kontrolü, Python, Node.js LTS ve modern Windows Terminal.",
                IconSymbol = SymbolRegular.Code24,
                IncludedApps = new List<string> { "VS Code", "Git for Windows", "Python 3", "Node.js LTS", "Windows Terminal", "PowerToys", "7-Zip" },
                TargetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "vscode", "git_for_windows", "python_3", "nodejs_lts", "windows_terminal", "powertoys", "seven_zip"
                }
            });
        }

        private void UpdatePresetMetrics()
        {
            foreach (var preset in Presets)
            {
                preset.TotalCount = preset.TargetIds.Count;
                preset.InstalledCount = _masterCatalog.Count(a => preset.TargetIds.Contains(a.Id) && a.IsInstalled);
                preset.IsSelected = preset.TargetIds.All(id =>
                {
                    var app = _masterCatalog.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                    return app != null && (app.IsSelected || app.IsInstalled);
                });
            }
        }

        [RelayCommand]
        public void ApplyPreset(StorePresetItem preset)
        {
            if (preset == null) return;
            SelectSpecificIds(preset.TargetIds);
            UpdatePresetMetrics();
            AppendLog($"📦 [Paket Seçildi] {preset.Title} — {preset.TargetIds.Count} uygulama kuyruğa işaretlendi.");
        }

        [RelayCommand]
        public void ApplyPresetAndSwitchToCatalog(StorePresetItem preset)
        {
            ApplyPreset(preset);
            SelectedStoreTabIndex = 0; // Kataloğa geç
        }

        [RelayCommand]
        public async Task InstallPresetImmediatelyAsync(StorePresetItem preset)
        {
            if (preset == null) return;
            ApplyPreset(preset);
            await InstallSelectedBatchAsync();
        }

        [RelayCommand]
        public void ApplyPresetFormatEssentials()
        {
            var p = Presets.FirstOrDefault(x => x.Id == "format_essentials");
            if (p != null) ApplyPreset(p);
        }

        [RelayCommand]
        public void ApplyPresetGamer()
        {
            var p = Presets.FirstOrDefault(x => x.Id == "gamer_pack");
            if (p != null) ApplyPreset(p);
        }

        [RelayCommand]
        public void ApplyPresetOffice()
        {
            var p = Presets.FirstOrDefault(x => x.Id == "office_daily");
            if (p != null) ApplyPreset(p);
        }

        [RelayCommand]
        public void ApplyPresetDeveloper()
        {
            var p = Presets.FirstOrDefault(x => x.Id == "developer_pack");
            if (p != null) ApplyPreset(p);
        }

        private void SelectSpecificIds(HashSet<string> targetIds)
        {
            foreach (var app in _masterCatalog)
            {
                app.IsSelected = targetIds.Contains(app.Id);
            }
            UpdateSelectionMetrics();
        }

        [RelayCommand]
        public void SelectAllVisible()
        {
            foreach (var app in DisplayApps)
            {
                app.IsSelected = true;
            }
            UpdateSelectionMetrics();
        }

        [RelayCommand]
        public void ClearSelection()
        {
            foreach (var app in _masterCatalog)
            {
                app.IsSelected = false;
            }
            UpdateSelectionMetrics();
        }

        #endregion

        #region Installation Execution

        [RelayCommand]
        public async Task InstallAllRuntimesAsync()
        {
            if (IsInstalling) return;

            var runtimes = _masterCatalog.Where(a => a.Category == StoreCategory.Runtimes).ToList();
            if (runtimes.Count == 0) return;

            foreach (var app in _masterCatalog) app.IsSelected = false;
            foreach (var r in runtimes) r.IsSelected = true;
            UpdateSelectionMetrics();

            await RunBatchInstallationAsync(runtimes, "All-in-One Runtimes Paketi");
        }

        [RelayCommand]
        public async Task InstallSelectedBatchAsync()
        {
            if (IsInstalling) return;

            var selected = _masterCatalog.Where(a => a.IsSelected).ToList();
            if (selected.Count == 0)
            {
                System.Windows.MessageBox.Show("Lütfen kurulmasını istediğiniz en az bir uygulama seçin.", "Seçim Yapılmadı", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            await RunBatchInstallationAsync(selected, $"{selected.Count} Seçili Uygulama");
        }

        [RelayCommand]
        public async Task InstallSingleAppAsync(StoreAppItem? app)
        {
            if (app == null || IsInstalling) return;

            await RunBatchInstallationAsync(new List<StoreAppItem> { app }, app.Name);
        }

        private async Task RunBatchInstallationAsync(List<StoreAppItem> queue, string batchTitle)
        {
            IsInstalling = true;
            IsQueueDrawerOpen = true;
            _currentCts = new CancellationTokenSource();
            var token = _currentCts.Token;

            AppendLog($"=======================================================");
            AppendLog($"[BAŞLATILDI] {batchTitle} Kurulum Kuyruğu ({queue.Count} Öğe)");
            AppendLog($"=======================================================");

            int completed = 0;
            int total = queue.Count;

            try
            {
                for (int i = 0; i < total; i++)
                {
                    if (token.IsCancellationRequested) break;

                    var app = queue[i];
                    CurrentAppName = app.Name;
                    QueueProgressSummary = $"{i + 1} / {total}";
                    OverallProgress = (int)(((double)i / total) * 100);

                    AppendLog($"\n>>> [{i + 1}/{total}] {app.Name} kuruluyor...");

                    bool success = await _storeService.InstallAppAsync(app, (pct, status) =>
                    {
                        CurrentStatusText = status;
                        // Sadece aşama ve durum mesajlarını konsola yaz; indirme bayt akışıyla konsolu ve UI'ı kitleme
                        if (!string.IsNullOrWhiteSpace(status) && !status.StartsWith("İndiriliyor:", StringComparison.OrdinalIgnoreCase))
                        {
                            AppendLog($"  • {status}");
                        }
                    }, token);

                    if (success)
                    {
                        completed++;
                        AppendLog($"[BAŞARILI] {app.Name} başarıyla tamamlandı.");
                    }
                    else
                    {
                        AppendLog($"[HATA] {app.Name} kurulamadı!");
                    }
                }

                OverallProgress = 100;
                CurrentStatusText = token.IsCancellationRequested ? "Kurulum iptal edildi." : "Tüm işlemler tamamlandı!";
                AppendLog($"\n=======================================================");
                AppendLog($"[BİTTİ] {completed}/{total} uygulama başarıyla kuruldu.");
                AppendLog($"=======================================================");

                await RefreshInstalledStatusAsync();
            }
            catch (OperationCanceledException)
            {
                CurrentStatusText = "Kullanıcı tarafından iptal edildi.";
                AppendLog("\n[İPTAL] İşlem kullanıcı tarafından durduruldu.");
            }
            catch (Exception ex)
            {
                CurrentStatusText = $"Beklenmeyen hata: {ex.Message}";
                AppendLog($"\n[KRİTİK HATA] {ex.Message}");
            }
            finally
            {
                IsInstalling = false;
                _currentCts?.Dispose();
                _currentCts = null;
            }
        }

        [RelayCommand]
        public void CancelCurrentOperation()
        {
            if (_currentCts != null && !_currentCts.IsCancellationRequested)
            {
                _currentCts.Cancel();
                CurrentStatusText = "İptal ediliyor...";
                AppendLog("[UYARI] İptal isteği gönderildi, mevcut süreç sonlandırılıyor...");
            }
        }

        [RelayCommand]
        public void ToggleQueueDrawer()
        {
            IsQueueDrawerOpen = !IsQueueDrawerOpen;
        }

        [RelayCommand]
        public void ClearConsoleLogs()
        {
            ConsoleLogs = $"[{DateTime.Now:HH:mm:ss}] Günlük konsolu temizlendi.\n";
        }

        [RelayCommand]
        public void CopyConsoleLogs()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(ConsoleLogs))
                {
                    Clipboard.SetText(ConsoleLogs);
                }
            }
            catch { }
        }

        [RelayCommand]
        public void ToggleConsoleExpand()
        {
            IsConsoleExpanded = !IsConsoleExpanded;
        }

        private void AppendLog(string message)
        {
            if (Application.Current == null) return;
            Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                var sb = new StringBuilder(ConsoleLogs);
                sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
                ConsoleLogs = sb.ToString();
            }));
        }

        #endregion

        #region Windows Konsolları & Klasik Araçlar (Tab 2)

        public ObservableCollection<ClassicAppItem> ClassicTools { get; } = new();
        public ObservableCollection<ClassicAppItem> DisplayClassicTools { get; } = new();

        [ObservableProperty]
        private string _toolSearchQuery = string.Empty;

        [ObservableProperty]
        private string _selectedToolCategory = "Tümü";

        [ObservableProperty]
        private bool _isPhotoViewerActivated;

        [ObservableProperty]
        private int _classicToolsTotalCount;

        [ObservableProperty]
        private string _toolOperationMessage = string.Empty;

        [ObservableProperty]
        private bool _isToolOperationMessageOpen;

        [ObservableProperty]
        private InfoBarSeverity _toolOperationSeverity = InfoBarSeverity.Informational;

        partial void OnToolSearchQueryChanged(string value) => ApplyToolFilters();
        partial void OnSelectedToolCategoryChanged(string value) => ApplyToolFilters();

        [RelayCommand]
        public void SetToolCategory(string category)
        {
            SelectedToolCategory = category;
        }

        public async Task LoadClassicToolsAsync()
        {
            try
            {
                var tools = await _classicAppsService.GetClassicToolsAsync();
                _masterClassicTools.Clear();
                _masterClassicTools.AddRange(tools);

                ClassicTools.Clear();
                foreach (var tool in tools)
                {
                    ClassicTools.Add(tool);
                }

                ClassicToolsTotalCount = _masterClassicTools.Count;
                IsPhotoViewerActivated = await _classicAppsService.IsWindowsPhotoViewerActivatedAsync();

                ApplyToolFilters();
            }
            catch (Exception ex)
            {
                _log.Error("Klasik araçlar yüklenirken hata oluştu.", ex, nameof(StoreViewModel));
            }
        }

        public async Task RefreshClassicToolsStateAsync()
        {
            try
            {
                IsPhotoViewerActivated = await _classicAppsService.IsWindowsPhotoViewerActivatedAsync();
                var photoApp = ClassicTools.FirstOrDefault(t => t.Id == "photo_viewer");
                if (photoApp != null)
                {
                    photoApp.IsActivated = IsPhotoViewerActivated;
                }
            }
            catch (Exception ex)
            {
                _log.Error("Fotoğraf görüntüleyici durumu kontrol edilirken hata oluştu.", ex, nameof(StoreViewModel));
            }
        }

        private void ApplyToolFilters()
        {
            var query = _masterClassicTools.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SelectedToolCategory) && SelectedToolCategory != "Tümü")
            {
                query = query.Where(t => string.Equals(t.Category, SelectedToolCategory, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(ToolSearchQuery))
            {
                string q = ToolSearchQuery.Trim().ToLowerInvariant();
                query = query.Where(t =>
                    (t.Title?.ToLowerInvariant().Contains(q) ?? false) ||
                    (t.Description?.ToLowerInvariant().Contains(q) ?? false) ||
                    (t.ExecutablePath?.ToLowerInvariant().Contains(q) ?? false) ||
                    (t.Category?.ToLowerInvariant().Contains(q) ?? false));
            }

            DisplayClassicTools.Clear();
            foreach (var item in query)
            {
                DisplayClassicTools.Add(item);
            }
        }

        [RelayCommand]
        public async Task ActivateWindowsPhotoViewerAsync()
        {
            if (!UacHelper.IsAdministrator())
            {
                ToolOperationMessage = "Klasik Windows Fotoğraf Görüntüleyicisi'ni etkinleştirmek için Yönetici yetkisi gereklidir.";
                ToolOperationSeverity = InfoBarSeverity.Warning;
                IsToolOperationMessageOpen = true;
                return;
            }

            bool ok = await _classicAppsService.ActivateWindowsPhotoViewerAsync();
            if (ok)
            {
                IsPhotoViewerActivated = true;
                var photoApp = ClassicTools.FirstOrDefault(t => t.Id == "photo_viewer");
                if (photoApp != null) photoApp.IsActivated = true;

                ToolOperationMessage = "Klasik Windows Fotoğraf Görüntüleyicisi başarıyla tüm resim formatları (.jpg, .png, .bmp, .gif, .tiff) için sisteme tanımlandı ve varsayılan yapıldı!";
                ToolOperationSeverity = InfoBarSeverity.Success;
                IsToolOperationMessageOpen = true;

                _log.Info("Klasik Windows Fotoğraf Görüntüleyicisi başarıyla etkinleştirildi.", nameof(StoreViewModel));
            }
            else
            {
                ToolOperationMessage = "Klasik Windows Fotoğraf Görüntüleyicisi kayıt defterine tanımlanamadı.";
                ToolOperationSeverity = InfoBarSeverity.Error;
                IsToolOperationMessageOpen = true;
            }
        }

        [RelayCommand]
        public async Task LaunchClassicToolAsync(ClassicAppItem? app)
        {
            if (app == null) return;

            if (app.Id == "photo_viewer")
            {
                await ActivateWindowsPhotoViewerAsync();
                return;
            }

            bool ok = await _classicAppsService.LaunchClassicToolAsync(app.Id);
            if (!ok)
            {
                ToolOperationMessage = $"'{app.Title}' başlatılamadı. Konsolun Windows sürümünüzde mevcut olduğundan emin olun.";
                ToolOperationSeverity = InfoBarSeverity.Error;
                IsToolOperationMessageOpen = true;
            }
            else
            {
                ToolOperationMessage = $"'{app.Title}' başarıyla başlatıldı.";
                ToolOperationSeverity = InfoBarSeverity.Success;
                IsToolOperationMessageOpen = true;
            }
        }

        [RelayCommand]
        public void DismissToolOperationMessage()
        {
            IsToolOperationMessageOpen = false;
        }

        #endregion
    }
}
