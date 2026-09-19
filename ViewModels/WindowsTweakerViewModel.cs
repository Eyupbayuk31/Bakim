using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Helpers;
using Bakım.Models;
using Bakım.Services;
using Wpf.Ui.Controls;

namespace Bakım.ViewModels
{
    public partial class WindowsTweakerViewModel : ObservableObject
    {
        private readonly IBehaviorTweaksService _behaviorService;
        private readonly IBootLogonTweaksService _bootLogonService;
        private readonly IDesktopTaskbarTweaksService _desktopTaskbarService;
        private readonly IContextMenuShortcutsService _contextMenuService;
        private readonly ISystemToolsService _toolsService;
        private readonly IClassicAppsService _classicAppsService;
        private readonly IWindows11TweaksService _win11Service;
        private readonly IAppearanceTweaksService _appearanceService;
        private readonly IAdvancedAppearanceService _advancedAppearanceService;
        private readonly ITweaksSnapshotService _snapshotService;
        private readonly IEdgeTweaksService _edgeService;
        private readonly ISettingsControlPanelTweaksService _settingsCplService;
        private readonly IFileExplorerTweaksService _fileExplorerService;
        private readonly ICollectionView _filteredTweaks;

        public WindowsTweakerViewModel() : this(null, null)
        {
        }

        public WindowsTweakerViewModel(
            IBehaviorTweaksService? behaviorService = null,
            IBootLogonTweaksService? bootLogonService = null,
            IDesktopTaskbarTweaksService? desktopTaskbarService = null,
            IContextMenuShortcutsService? contextMenuService = null,
            ISystemToolsService? toolsService = null,
            IClassicAppsService? classicAppsService = null,
            IWindows11TweaksService? win11Service = null,
            IAppearanceTweaksService? appearanceService = null,
            IAdvancedAppearanceService? advancedAppearanceService = null,
            ITweaksSnapshotService? snapshotService = null,
            IEdgeTweaksService? edgeService = null,
            ISettingsControlPanelTweaksService? settingsCplService = null,
            IFileExplorerTweaksService? fileExplorerService = null)
        {
            _behaviorService = behaviorService ?? new BehaviorTweaksService();
            _bootLogonService = bootLogonService ?? new BootLogonTweaksService();
            _desktopTaskbarService = desktopTaskbarService ?? new DesktopTaskbarTweaksService();
            _contextMenuService = contextMenuService ?? new ContextMenuShortcutsService();
            _toolsService = toolsService ?? new SystemToolsService();
            _classicAppsService = classicAppsService ?? new ClassicAppsService();
            _win11Service = win11Service ?? new Windows11TweaksService();
            _appearanceService = appearanceService ?? new AppearanceTweaksService();
            _advancedAppearanceService = advancedAppearanceService ?? new AdvancedAppearanceService();
            _snapshotService = snapshotService ?? new TweaksSnapshotService();
            _edgeService = edgeService ?? new EdgeTweaksService();
            _settingsCplService = settingsCplService ?? new SettingsControlPanelTweaksService();
            _fileExplorerService = fileExplorerService ?? new FileExplorerTweaksService();

            AllTweaks = new ObservableCollection<SystemTweakItem>();
            ClassicTools = new ObservableCollection<ClassicAppItem>();
            OemInfo = new OemInfoData();
            Metrics = new WindowMetricsData();

            _filteredTweaks = CollectionViewSource.GetDefaultView(AllTweaks);
            _filteredTweaks.Filter = FilterTweakItem;

            IsAdmin = UacHelper.IsAdministrator();

            _ = RefreshAllAsync();
        }

        public ObservableCollection<SystemTweakItem> AllTweaks { get; }
        public ObservableCollection<ClassicAppItem> ClassicTools { get; }
        public ICollectionView FilteredTweaks => _filteredTweaks;

        [ObservableProperty]
        private OemInfoData _oemInfo;

        [ObservableProperty]
        private WindowMetricsData _metrics = new();

        [ObservableProperty]
        private TweakerStats _stats = new();

        [ObservableProperty]
        private string _activeCategory = "All"; // All, Windows11, Behavior, BootLogon, DesktopTaskbar, ContextMenu, Appearance, AdvancedAppearance, FileExplorer, SettingsCpl, Edge, Tools, ClassicApps

        public bool IsTweaksListVisible => !string.IsNullOrWhiteSpace(SearchText) || (ActiveCategory != "Tools" && ActiveCategory != "ClassicApps" && ActiveCategory != "AdvancedAppearance");
        public bool IsToolsTabVisible => string.IsNullOrWhiteSpace(SearchText) && ActiveCategory == "Tools";
        public bool IsClassicAppsTabVisible => string.IsNullOrWhiteSpace(SearchText) && ActiveCategory == "ClassicApps";
        public bool IsAdvancedAppearanceTabVisible => string.IsNullOrWhiteSpace(SearchText) && ActiveCategory == "AdvancedAppearance";

        public string ActiveCategoryDisplayName => ActiveCategory switch
        {
            "Windows11" => "Windows 11",
            "Appearance" => "Görünüm & Tema",
            "AdvancedAppearance" => "Gelişmiş Görünüm",
            "Behavior" => "Sistem Davranışları",
            "BootLogon" => "Açılış & Oturum",
            "DesktopTaskbar" => "Masaüstü & Görev Çubuğu",
            "ContextMenu" => "Sağ Tık Menüsü",
            "FileExplorer" => "Dosya Gezgini",
            "SettingsCpl" => "Ayarlar & Denetim Masası",
            "Edge" => "Microsoft Edge",
            "Tools" => "Sistem Araçları",
            "ClassicApps" => "Klasik Uygulamalar",
            _ => "Tüm İnce Ayarlar"
        };

        [ObservableProperty]
        private bool _isCategoryPickerOpen;

        [ObservableProperty]
        private string _searchText = string.Empty;

        public bool IsSearchActive => !string.IsNullOrWhiteSpace(SearchText);

        public string SearchResultsText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SearchText)) return string.Empty;
                int count = _filteredTweaks.Cast<object>().Count();
                return $"'{SearchText}' araması için {count} ayar bulundu (Tüm kategorilerde)";
            }
        }

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusText = "Windows ince ayarları hazır.";

        [ObservableProperty]
        private bool _hasResultBanner;

        [ObservableProperty]
        private string _operationResultBanner = string.Empty;

        [ObservableProperty]
        private InfoBarSeverity _resultBannerSeverity = InfoBarSeverity.Informational;

        [ObservableProperty]
        private bool _isAdmin;

        [ObservableProperty]
        private bool _hasSnapshot;

        [ObservableProperty]
        private bool _canRestartExplorer;

        // Tools Inputs
        [ObservableProperty]
        private string _trustedInstallerCommandInput = "cmd.exe";

        [ObservableProperty]
        private string _elevatedShortcutTargetPath = string.Empty;

        [ObservableProperty]
        private string _elevatedShortcutName = string.Empty;

        partial void OnActiveCategoryChanged(string value)
        {
            OnPropertyChanged(nameof(IsTweaksListVisible));
            OnPropertyChanged(nameof(IsToolsTabVisible));
            OnPropertyChanged(nameof(IsClassicAppsTabVisible));
            OnPropertyChanged(nameof(IsAdvancedAppearanceTabVisible));
            OnPropertyChanged(nameof(ActiveCategoryDisplayName));
            _filteredTweaks.Refresh();
        }

        partial void OnSearchTextChanged(string value)
        {
            OnPropertyChanged(nameof(IsTweaksListVisible));
            OnPropertyChanged(nameof(IsToolsTabVisible));
            OnPropertyChanged(nameof(IsClassicAppsTabVisible));
            OnPropertyChanged(nameof(IsAdvancedAppearanceTabVisible));
            OnPropertyChanged(nameof(IsSearchActive));
            _filteredTweaks.Refresh();
            OnPropertyChanged(nameof(SearchResultsText));
        }

        [RelayCommand]
        public void SwitchCategory(string category)
        {
            if (string.Equals(category, "PrivacyDebloat", StringComparison.OrdinalIgnoreCase))
            {
                NavigationService.Instance.Navigate("PrivacyDebloat");
                return;
            }
            ActiveCategory = category;
        }

        [RelayCommand]
        public void ToggleCategoryPicker()
        {
            IsCategoryPickerOpen = !IsCategoryPickerOpen;
        }

        [RelayCommand]
        public void SelectCategoryAndClosePicker(string categoryKey)
        {
            IsCategoryPickerOpen = false;
            if (string.Equals(categoryKey, "PrivacyDebloat", StringComparison.OrdinalIgnoreCase))
            {
                NavigationService.Instance.Navigate("PrivacyDebloat");
                return;
            }
            ActiveCategory = categoryKey;
        }

        [RelayCommand]
        public void ClearSearch()
        {
            SearchText = string.Empty;
        }

        private bool FilterTweakItem(object obj)
        {
            if (obj is not SystemTweakItem item) return false;

            // 1. Canlı arama aktifse: TÜM KATEGORİLERDE GLOBAL ARAMA YAP
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string query = SearchText.Trim();
                return item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       item.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       item.Category.Contains(query, StringComparison.OrdinalIgnoreCase);
            }

            // 2. Arama boşsa: Seçili kategoriye göre filtrele
            if (ActiveCategory == "All")
                return true;

            if (ActiveCategory == "Windows11" && item.Category.Contains("Windows 11"))
                return true;

            if (ActiveCategory == "Appearance" && item.Category.Contains("Görünüm"))
                return true;

            if (ActiveCategory == "Behavior" && item.Category.Contains("Davranışlar"))
                return true;

            if (ActiveCategory == "BootLogon" && item.Category.Contains("Açılış"))
                return true;

            if (ActiveCategory == "DesktopTaskbar" && item.Category.Contains("Masaüstü"))
                return true;

            if (ActiveCategory == "ContextMenu" && item.Category.Contains("Sağ Tık"))
                return true;

            if (ActiveCategory == "FileExplorer" && item.Category.Contains("Dosya Gezgini"))
                return true;

            if (ActiveCategory == "SettingsCpl" && item.Category.Contains("Ayarlar"))
                return true;

            if (ActiveCategory == "Edge" && item.Category.Contains("Edge"))
                return true;

            return false;
        }

        [RelayCommand]
        public async Task RefreshAllAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusText = "Tüm ince ayarlar ve araçlar taranıyor...";
            HasResultBanner = false;

            try
            {
                var win11 = await _win11Service.GetWindows11TweaksAsync();
                var behavior = await _behaviorService.GetBehaviorTweaksAsync();
                var bootLogon = await _bootLogonService.GetBootLogonTweaksAsync();
                var desktopTaskbar = await _desktopTaskbarService.GetDesktopTaskbarTweaksAsync();
                var contextMenu = await _contextMenuService.GetContextMenuShortcutsTweaksAsync();
                var appearance = await _appearanceService.GetAppearanceTweaksAsync();
                var fileExplorer = await _fileExplorerService.GetFileExplorerTweaksAsync();
                var settingsCpl = await _settingsCplService.GetSettingsControlPanelTweaksAsync();
                var edge = await _edgeService.GetEdgeTweaksAsync();

                AllTweaks.Clear();
                foreach (var w in win11) AllTweaks.Add(w);
                foreach (var a in appearance) AllTweaks.Add(a);
                foreach (var b in behavior) AllTweaks.Add(b);
                foreach (var bl in bootLogon) AllTweaks.Add(bl);
                foreach (var dt in desktopTaskbar) AllTweaks.Add(dt);
                foreach (var cm in contextMenu) AllTweaks.Add(cm);
                foreach (var fe in fileExplorer) AllTweaks.Add(fe);
                foreach (var sc in settingsCpl) AllTweaks.Add(sc);
                foreach (var ed in edge) AllTweaks.Add(ed);

                // Klasik Araçlar, OEM Bilgisi & WindowMetrics
                var tools = await _classicAppsService.GetClassicToolsAsync();
                ClassicTools.Clear();
                foreach (var t in tools) ClassicTools.Add(t);

                OemInfo = await _toolsService.GetOemInfoAsync();
                Metrics = await _advancedAppearanceService.GetWindowMetricsAsync();

                // Snapshot Guard (V22.0): İlk sistem durumunu tweaks_backup.json dosyasına yedekle
                await _snapshotService.EnsureInitialSnapshotAsync(AllTweaks);
                HasSnapshot = await _snapshotService.HasSnapshotAsync();

                UpdateStats();
                StatusText = $"{AllTweaks.Count} adet ince ayar ve araç başarıyla yüklendi. (Snapshot hazır)";
            }
            catch (Exception ex)
            {
                StatusText = $"Hata: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task<bool> ApplyTweakDirectAsync(SystemTweakItem tweak, bool targetState)
        {
            if (tweak.Category.Contains("Windows 11"))
                return await _win11Service.ApplyTweakAsync(tweak, targetState);
            if (tweak.Category.Contains("Görünüm"))
                return await _appearanceService.ApplyTweakAsync(tweak, targetState);
            if (tweak.Category.Contains("Davranışlar"))
                return await _behaviorService.ApplyTweakAsync(tweak, targetState);
            if (tweak.Category.Contains("Açılış"))
                return await _bootLogonService.ApplyTweakAsync(tweak, targetState);
            if (tweak.Category.Contains("Masaüstü"))
                return await _desktopTaskbarService.ApplyTweakAsync(tweak, targetState);
            if (tweak.Category.Contains("Sağ Tık"))
                return await _contextMenuService.ApplyTweakAsync(tweak, targetState);
            if (tweak.Category.Contains("Dosya Gezgini"))
                return await _fileExplorerService.ApplyTweakAsync(tweak, targetState);
            if (tweak.Category.Contains("Ayarlar"))
                return await _settingsCplService.ApplyTweakAsync(tweak, targetState);
            if (tweak.Category.Contains("Edge"))
                return await _edgeService.ApplyTweakAsync(tweak, targetState);
            return false;
        }

        [RelayCommand]
        public async Task ToggleTweakAsync(SystemTweakItem? tweak)
        {
            if (tweak == null || tweak.IsBusy) return;

            if (tweak.RequiresAdmin && !IsAdmin)
            {
                OperationResultBanner = "Bu ayarı değiştirmek için uygulamayı 'Yönetici Olarak Çalıştır'manız gerekmektedir.";
                ResultBannerSeverity = InfoBarSeverity.Warning;
                HasResultBanner = true;
                tweak.NotifyStateChanged();
                return;
            }

            tweak.IsBusy = true;
            bool targetState = !tweak.IsEnabled;

            // Reversible Engine Guard (V22.0): Değişiklikten önce orijinal durumu snapshot dosyasına kaydet
            await _snapshotService.RecordTweakBeforeChangeAsync(tweak);

            try
            {
                bool success = await ApplyTweakDirectAsync(tweak, targetState);

                if (success)
                {
                    tweak.IsEnabled = targetState;
                    _snapshotService.BroadcastSettingsChange();
                    UpdateStats();

                    // V26.0 Rule of Truth:
                    // IsActive = true  -> "başarıyla uygulandı"
                    // IsActive = false -> "varsayılan duruma getirildi"
                    string status = targetState ? "başarıyla uygulandı" : "varsayılan duruma getirildi";
                    string restartNote = tweak.RequiresRestart ? " (Değişikliğin yansıması için Gezgini Yeniden Başlat butonuna tıklayın)" : "";
                    OperationResultBanner = $"'{tweak.Title}' {status}.{restartNote}";
                    ResultBannerSeverity = InfoBarSeverity.Success;
                    HasResultBanner = true;
                    if (tweak.RequiresRestart)
                    {
                        CanRestartExplorer = true;
                    }
                }
                else
                {
                    tweak.NotifyStateChanged();
                    OperationResultBanner = $"'{tweak.Title}' ayarı uygulanamadı. Yönetici izinlerini kontrol edin.";
                    ResultBannerSeverity = InfoBarSeverity.Error;
                    HasResultBanner = true;
                }
            }
            catch (Exception ex)
            {
                tweak.NotifyStateChanged();
                OperationResultBanner = $"Hata: {ex.Message}";
                ResultBannerSeverity = InfoBarSeverity.Error;
                HasResultBanner = true;
            }
            finally
            {
                tweak.IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task RestoreFromSnapshotAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusText = "İlk durum yedeğinden (tweaks_backup.json) geri yükleniyor...";

            try
            {
                bool ok = await _snapshotService.RestoreFromSnapshotAsync(
                    AllTweaks,
                    async (item, state) =>
                    {
                        bool applied = await ApplyTweakDirectAsync(item, state);
                        if (applied)
                        {
                            item.IsEnabled = state;
                            item.NotifyStateChanged();
                        }
                        return applied;
                    },
                    async (item) =>
                    {
                        if (item.Id == "chkdsk_timeout")
                        {
                            return await _bootLogonService.SetChkdskTimeoutAsync(item.NumericValue);
                        }
                        if (item.Id == "boot_timeout")
                        {
                            return await _bootLogonService.SetBootTimeoutAsync(item.NumericValue);
                        }
                        if (item.Id == "explorer_jumplist_item_count")
                        {
                            return await _fileExplorerService.SetJumpListItemsAsync(item.NumericValue);
                        }
                        return true;
                    });

                UpdateStats();
                OperationResultBanner = ok 
                    ? "Tüm ayarlar kaydedilen ilk durum yedeğindeki (Snapshot) orijinal hallerine döndürüldü!"
                    : "Bazı ayarlar geri yüklenirken hata oluştu. Yönetici izinlerini kontrol edin.";
                ResultBannerSeverity = ok ? InfoBarSeverity.Success : InfoBarSeverity.Error;
                HasResultBanner = true;
            }
            catch (Exception ex)
            {
                OperationResultBanner = $"Geri yükleme hatası: {ex.Message}";
                ResultBannerSeverity = InfoBarSeverity.Error;
                HasResultBanner = true;
            }
            finally
            {
                IsBusy = false;
                StatusText = "Geri yükleme tamamlandı.";
            }
        }

        [RelayCommand]
        public async Task RestartExplorerAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusText = "Windows Gezgini (Explorer.exe) yeniden başlatılıyor...";

            try
            {
                bool ok = await _snapshotService.RestartExplorerAsync();
                if (ok)
                {
                    OperationResultBanner = "Windows Gezgini başarıyla yeniden başlatıldı; görsel ve masaüstü değişiklikleri uygulandı.";
                    ResultBannerSeverity = InfoBarSeverity.Success;
                    HasResultBanner = true;
                    CanRestartExplorer = false;
                }
            }
            finally
            {
                IsBusy = false;
                StatusText = "Hazır.";
            }
        }

        [RelayCommand]
        public async Task SaveNumericTweakAsync(SystemTweakItem? tweak)
        {
            if (tweak == null || tweak.IsBusy) return;

            if (tweak.RequiresAdmin && !IsAdmin)
            {
                OperationResultBanner = "Bu ayarı değiştirmek için Yönetici izinleri gereklidir.";
                ResultBannerSeverity = InfoBarSeverity.Warning;
                HasResultBanner = true;
                return;
            }

            tweak.IsBusy = true;
            try
            {
                if (tweak.Id == "chkdsk_timeout")
                {
                    bool ok = await _bootLogonService.SetChkdskTimeoutAsync(tweak.NumericValue);
                    if (ok)
                    {
                        OperationResultBanner = $"Chkdsk disk denetimi bekleme süresi {tweak.NumericValue} saniye olarak güncellendi.";
                        ResultBannerSeverity = InfoBarSeverity.Success;
                        HasResultBanner = true;
                    }
                }
                else if (tweak.Id == "boot_timeout")
                {
                    bool ok = await _bootLogonService.SetBootTimeoutAsync(tweak.NumericValue);
                    if (ok)
                    {
                        OperationResultBanner = $"Açılış (BCD Boot) menüsü bekleme süresi {tweak.NumericValue} saniye olarak güncellendi.";
                        ResultBannerSeverity = InfoBarSeverity.Success;
                        HasResultBanner = true;
                    }
                }
                else if (tweak.Id == "explorer_jumplist_item_count")
                {
                    bool ok = await _fileExplorerService.SetJumpListItemsAsync(tweak.NumericValue);
                    if (ok)
                    {
                        OperationResultBanner = $"Gezgin Atlama Listesi (Jump List) öğe sayısı {tweak.NumericValue} olarak ayarlandı.";
                        ResultBannerSeverity = InfoBarSeverity.Success;
                        HasResultBanner = true;
                    }
                }
            }
            finally
            {
                tweak.IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ExecuteTweakActionAsync(SystemTweakItem? tweak)
        {
            if (tweak == null) return;

            if (tweak.Id == "classic_volume_mixer_launcher")
            {
                await _desktopTaskbarService.LaunchClassicVolumeMixerAsync();
            }
            else if (tweak.Id == "app_aerolite_theme_launcher")
            {
                await ApplyAeroLiteThemeAsync();
            }
            else if (tweak.Id == "find_spotlight_images")
            {
                int count = await _bootLogonService.ExportSpotlightImagesAsync();
                OperationResultBanner = $"{count} adet kilit ekranı ve Spotlight görseli 'Resimler\\Windows Spotlight' klasörüne aktarıldı ve klasör açıldı.";
                ResultBannerSeverity = InfoBarSeverity.Success;
                HasResultBanner = true;
            }
        }

        [RelayCommand]
        public async Task ApplyAeroLiteThemeAsync()
        {
            bool ok = await _appearanceService.ApplyAeroLiteThemeAsync();
            if (ok)
            {
                OperationResultBanner = "Aero Lite teması başarıyla uygulandı.";
                ResultBannerSeverity = InfoBarSeverity.Success;
                HasResultBanner = true;
            }
            else
            {
                OperationResultBanner = "Aero Lite tema dosyası bulunamadı veya uygulanamadı.";
                ResultBannerSeverity = InfoBarSeverity.Error;
                HasResultBanner = true;
            }
        }

        [RelayCommand]
        public async Task SaveWindowMetricsAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusText = "Gelişmiş görünüm metrikleri kaydediliyor...";

            try
            {
                bool ok = await _advancedAppearanceService.SaveWindowMetricsAsync(Metrics);
                if (ok)
                {
                    OperationResultBanner = "Pencere kenarlığı, kaydırma çubuğu, başlık yüksekliği ve simge aralıkları başarıyla kaydedildi ve DWM yenilendi.";
                    ResultBannerSeverity = InfoBarSeverity.Success;
                    HasResultBanner = true;
                }
                else
                {
                    OperationResultBanner = "Metrikler kaydedilirken bir hata oluştu.";
                    ResultBannerSeverity = InfoBarSeverity.Error;
                    HasResultBanner = true;
                }
            }
            finally
            {
                IsBusy = false;
                StatusText = "Hazır.";
            }
        }

        [RelayCommand]
        public async Task ResetWindowMetricsToDefaultsAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusText = "Tüm metrikler varsayılan değerlere sıfırlanıyor...";

            try
            {
                bool ok = await _advancedAppearanceService.ResetToDefaultsAsync();
                if (ok)
                {
                    Metrics = await _advancedAppearanceService.GetWindowMetricsAsync();
                    OperationResultBanner = "Tüm pencere ve masaüstü metrikleri orijinal Windows fabrika ayarlarına sıfırlandı.";
                    ResultBannerSeverity = InfoBarSeverity.Success;
                    HasResultBanner = true;
                }
            }
            finally
            {
                IsBusy = false;
                StatusText = "Hazır.";
            }
        }

        [RelayCommand]
        public async Task ApplyAllRecommendedAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            HasResultBanner = false;
            StatusText = "Tüm önerilen ince ayarlar uygulanıyor...";

            try
            {
                var win11List = AllTweaks.Where(t => t.Category.Contains("Windows 11")).ToList();
                var appearanceList = AllTweaks.Where(t => t.Category.Contains("Görünüm")).ToList();
                var behaviorList = AllTweaks.Where(t => t.Category.Contains("Davranışlar")).ToList();
                var bootList = AllTweaks.Where(t => t.Category.Contains("Açılış")).ToList();
                var desktopList = AllTweaks.Where(t => t.Category.Contains("Masaüstü")).ToList();
                var contextList = AllTweaks.Where(t => t.Category.Contains("Sağ Tık")).ToList();

                var feList = AllTweaks.Where(t => t.Category.Contains("Dosya Gezgini")).ToList();
                var cplList = AllTweaks.Where(t => t.Category.Contains("Ayarlar")).ToList();
                var edgeList = AllTweaks.Where(t => t.Category.Contains("Edge")).ToList();

                await _win11Service.ApplyAllRecommendedAsync(win11List);
                await _appearanceService.ApplyAllRecommendedAsync(appearanceList);
                await _behaviorService.ApplyAllRecommendedAsync(behaviorList);
                await _bootLogonService.ApplyAllRecommendedAsync(bootList);
                await _desktopTaskbarService.ApplyAllRecommendedAsync(desktopList);
                await _contextMenuService.ApplyAllRecommendedAsync(contextList);
                await _fileExplorerService.ApplyAllRecommendedAsync(feList);
                await _settingsCplService.ApplyAllRecommendedAsync(cplList);
                await _edgeService.ApplyAllRecommendedAsync(edgeList);

                _snapshotService.BroadcastSettingsChange();
                CanRestartExplorer = true;
                UpdateStats();
                OperationResultBanner = "Tüm önerilen tema, görünüm, sistem ve açılış ince ayarları başarıyla uygulandı!";
                ResultBannerSeverity = InfoBarSeverity.Success;
                HasResultBanner = true;
            }
            catch (Exception ex)
            {
                OperationResultBanner = $"Önerilenler uygulanırken hata: {ex.Message}";
                ResultBannerSeverity = InfoBarSeverity.Error;
                HasResultBanner = true;
            }
            finally
            {
                IsBusy = false;
                StatusText = "İşlem tamamlandı.";
            }
        }

        [RelayCommand]
        public async Task RestoreDefaultsAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            HasResultBanner = false;
            StatusText = "Tüm ayarlar Windows varsayılanlarına döndürülüyor...";

            try
            {
                var win11List = AllTweaks.Where(t => t.Category.Contains("Windows 11")).ToList();
                var appearanceList = AllTweaks.Where(t => t.Category.Contains("Görünüm")).ToList();
                var behaviorList = AllTweaks.Where(t => t.Category.Contains("Davranışlar")).ToList();
                var bootList = AllTweaks.Where(t => t.Category.Contains("Açılış")).ToList();
                var desktopList = AllTweaks.Where(t => t.Category.Contains("Masaüstü")).ToList();
                var contextList = AllTweaks.Where(t => t.Category.Contains("Sağ Tık")).ToList();
                var feList = AllTweaks.Where(t => t.Category.Contains("Dosya Gezgini")).ToList();
                var cplList = AllTweaks.Where(t => t.Category.Contains("Ayarlar")).ToList();
                var edgeList = AllTweaks.Where(t => t.Category.Contains("Edge")).ToList();

                await _win11Service.RestoreDefaultsAsync(win11List);
                await _appearanceService.RestoreDefaultsAsync(appearanceList);
                await _behaviorService.RestoreDefaultsAsync(behaviorList);
                await _bootLogonService.RestoreDefaultsAsync(bootList);
                await _desktopTaskbarService.RestoreDefaultsAsync(desktopList);
                await _contextMenuService.RestoreDefaultsAsync(contextList);
                await _fileExplorerService.RestoreDefaultsAsync(feList);
                await _settingsCplService.RestoreDefaultsAsync(cplList);
                await _edgeService.RestoreDefaultsAsync(edgeList);

                _snapshotService.BroadcastSettingsChange();
                CanRestartExplorer = true;
                UpdateStats();
                OperationResultBanner = "Tüm ayarlar başarıyla orijinal Windows fabrika varsayılanlarına döndürüldü.";
                ResultBannerSeverity = InfoBarSeverity.Success;
                HasResultBanner = true;
            }
            catch (Exception ex)
            {
                OperationResultBanner = $"Varsayılana döndürme hatası: {ex.Message}";
                ResultBannerSeverity = InfoBarSeverity.Error;
                HasResultBanner = true;
            }
            finally
            {
                IsBusy = false;
                StatusText = "Varsayılanlara dönüldü.";
            }
        }

        #region Tools & Utilities Commands

        [RelayCommand]
        public async Task ResetCachesAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusText = "Icon, Thumbnail ve Tray önbellekleri temizleniyor...";

            try
            {
                bool ok = await _toolsService.ResetCachesAndRestartExplorerAsync();
                if (ok)
                {
                    OperationResultBanner = "Tüm simge ve küçük resim önbelleği temizlendi; Windows Gezgini (Explorer) başarıyla yeniden başlatıldı.";
                    ResultBannerSeverity = InfoBarSeverity.Success;
                    HasResultBanner = true;
                }
                else
                {
                    OperationResultBanner = "Önbellek temizlenirken bazı dosyalar kilitliydi, Explorer başlatıldı.";
                    ResultBannerSeverity = InfoBarSeverity.Warning;
                    HasResultBanner = true;
                }
            }
            finally
            {
                IsBusy = false;
                StatusText = "Hazır.";
            }
        }

        [RelayCommand]
        public async Task LaunchTrustedInstallerAsync()
        {
            if (!IsAdmin)
            {
                OperationResultBanner = "TrustedInstaller yetkisiyle komut çalıştırmak için uygulama Yönetici olarak çalışmalıdır.";
                ResultBannerSeverity = InfoBarSeverity.Warning;
                HasResultBanner = true;
                return;
            }

            string target = string.IsNullOrWhiteSpace(TrustedInstallerCommandInput) ? "cmd.exe" : TrustedInstallerCommandInput.Trim();
            bool ok = await _toolsService.LaunchAsTrustedInstallerAsync(target);
            if (ok)
            {
                OperationResultBanner = $"'{target}' başarıyla NT AUTHORITY\\TrustedInstaller yetkisiyle başlatıldı!";
                ResultBannerSeverity = InfoBarSeverity.Success;
                HasResultBanner = true;
            }
            else
            {
                OperationResultBanner = "TrustedInstaller süreci başlatılamadı. Servis izinlerini kontrol edin.";
                ResultBannerSeverity = InfoBarSeverity.Error;
                HasResultBanner = true;
            }
        }

        [RelayCommand]
        public async Task CreateElevatedShortcutAsync()
        {
            if (string.IsNullOrWhiteSpace(ElevatedShortcutTargetPath))
            {
                OperationResultBanner = "Lütfen kısayolu oluşturulacak programın dosya yolunu (.exe) girin.";
                ResultBannerSeverity = InfoBarSeverity.Warning;
                HasResultBanner = true;
                return;
            }

            bool ok = await _contextMenuService.CreateElevatedShortcutAsync(ElevatedShortcutTargetPath, ElevatedShortcutName);
            if (ok)
            {
                OperationResultBanner = "UAC'siz (Kullanıcı Hesabı Denetimi uyarısı çıkarmayan) Yönetici Kısayolu Masaüstüne oluşturuldu!";
                ResultBannerSeverity = InfoBarSeverity.Success;
                HasResultBanner = true;
                ElevatedShortcutTargetPath = string.Empty;
                ElevatedShortcutName = string.Empty;
            }
            else
            {
                OperationResultBanner = "Kısayol oluşturulamadı. Hedef dosyanın varlığını ve görev zamanlayıcı izinlerini kontrol edin.";
                ResultBannerSeverity = InfoBarSeverity.Error;
                HasResultBanner = true;
            }
        }

        [RelayCommand]
        public async Task SaveOemInfoAsync()
        {
            if (!IsAdmin)
            {
                OperationResultBanner = "OEM ve Sahip bilgilerini değiştirmek için Yönetici izinleri gereklidir.";
                ResultBannerSeverity = InfoBarSeverity.Warning;
                HasResultBanner = true;
                return;
            }

            OemInfo.IsBusy = true;
            try
            {
                bool ok = await _toolsService.SaveOemInfoAsync(OemInfo);
                if (ok)
                {
                    OperationResultBanner = "OEM Sistem ve Kayıtlı Kullanıcı bilgileri başarıyla güncellendi.";
                    ResultBannerSeverity = InfoBarSeverity.Success;
                    HasResultBanner = true;
                }
                else
                {
                    OperationResultBanner = "OEM bilgileri kaydedilemedi. Yönetici izinlerini kontrol edin.";
                    ResultBannerSeverity = InfoBarSeverity.Error;
                    HasResultBanner = true;
                }
            }
            finally
            {
                OemInfo.IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ResetLocalGroupPolicyAsync()
        {
            if (!IsAdmin)
            {
                OperationResultBanner = "GPO ilkelerini sıfırlamak için Yönetici izinleri gereklidir.";
                ResultBannerSeverity = InfoBarSeverity.Warning;
                HasResultBanner = true;
                return;
            }

            IsBusy = true;
            StatusText = "Yerel Grup İlkeleri (GPO) varsayılanlara sıfırlanıyor...";

            try
            {
                bool ok = await _toolsService.ResetLocalGroupPolicyAsync();
                if (ok)
                {
                    OperationResultBanner = "Yerel Grup İlkeleri (Local Group Policy) ve güvenlik şablonları başarıyla fabrika ayarlarına döndürüldü (gpupdate uygulandı).";
                    ResultBannerSeverity = InfoBarSeverity.Success;
                    HasResultBanner = true;
                }
                else
                {
                    OperationResultBanner = "GPO ilkeleri sıfırlanırken bir hata oluştu.";
                    ResultBannerSeverity = InfoBarSeverity.Error;
                    HasResultBanner = true;
                }
            }
            finally
            {
                IsBusy = false;
                StatusText = "GPO sıfırlama tamamlandı.";
            }
        }

        #endregion

        #region Classic Apps Commands

        [RelayCommand]
        public async Task ActivateWindowsPhotoViewerAsync()
        {
            if (!IsAdmin)
            {
                OperationResultBanner = "Klasik Windows Fotoğraf Görüntüleyicisi'ni etkinleştirmek için Yönetici yetkisi gereklidir.";
                ResultBannerSeverity = InfoBarSeverity.Warning;
                HasResultBanner = true;
                return;
            }

            bool ok = await _classicAppsService.ActivateWindowsPhotoViewerAsync();
            if (ok)
            {
                var photoApp = ClassicTools.FirstOrDefault(t => t.Id == "photo_viewer");
                if (photoApp != null) photoApp.IsActivated = true;

                OperationResultBanner = "Klasik Windows Fotoğraf Görüntüleyicisi başarıyla tüm resim formatları (.jpg, .png vb.) için sisteme kaydedildi!";
                ResultBannerSeverity = InfoBarSeverity.Success;
                HasResultBanner = true;
            }
            else
            {
                OperationResultBanner = "Klasik Windows Fotoğraf Görüntüleyicisi etkinleştirilemedi.";
                ResultBannerSeverity = InfoBarSeverity.Error;
                HasResultBanner = true;
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
                OperationResultBanner = $"'{app.Title}' başlatılamadı. Aracın Windows üzerinde yüklü olduğunu kontrol edin.";
                ResultBannerSeverity = InfoBarSeverity.Error;
                HasResultBanner = true;
            }
        }

        #endregion

        [RelayCommand]
        public void RestartAsAdmin()
        {
            UacHelper.RestartAsAdministrator();
        }

        private void UpdateStats()
        {
            Stats = new TweakerStats
            {
                TotalTweaksCount = AllTweaks.Count,
                ActiveTweaksCount = AllTweaks.Count(t => t.IsEnabled),
                Windows11TweaksCount = AllTweaks.Count(t => t.Category.Contains("Windows 11")),
                BehaviorTweaksCount = AllTweaks.Count(t => t.Category.Contains("Davranışlar")),
                BootLogonTweaksCount = AllTweaks.Count(t => t.Category.Contains("Açılış")),
                DesktopTaskbarTweaksCount = AllTweaks.Count(t => t.Category.Contains("Masaüstü")),
                ContextMenuTweaksCount = AllTweaks.Count(t => t.Category.Contains("Sağ Tık")),
                AppearanceTweaksCount = AllTweaks.Count(t => t.Category.Contains("Görünüm")),
                FileExplorerTweaksCount = AllTweaks.Count(t => t.Category.Contains("Dosya Gezgini")),
                SettingsControlPanelTweaksCount = AllTweaks.Count(t => t.Category.Contains("Ayarlar")),
                EdgeTweaksCount = AllTweaks.Count(t => t.Category.Contains("Edge")),
                AdvancedAppearanceCount = 7,
                ToolsCount = 4,
                ClassicAppsCount = ClassicTools.Count
            };
        }
    }
}
