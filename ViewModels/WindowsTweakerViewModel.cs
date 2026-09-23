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

        public WindowsTweakerViewModel(
            IBehaviorTweaksService behaviorService,
            IBootLogonTweaksService bootLogonService,
            IDesktopTaskbarTweaksService desktopTaskbarService,
            IContextMenuShortcutsService contextMenuService,
            ISystemToolsService toolsService,
            IClassicAppsService classicAppsService,
            IWindows11TweaksService win11Service,
            IAppearanceTweaksService appearanceService,
            IAdvancedAppearanceService advancedAppearanceService,
            ITweaksSnapshotService snapshotService,
            IEdgeTweaksService edgeService,
            ISettingsControlPanelTweaksService settingsCplService,
            IFileExplorerTweaksService fileExplorerService)
        {
            _behaviorService = behaviorService;
            _bootLogonService = bootLogonService;
            _desktopTaskbarService = desktopTaskbarService;
            _contextMenuService = contextMenuService;
            _toolsService = toolsService;
            _classicAppsService = classicAppsService;
            _win11Service = win11Service;
            _appearanceService = appearanceService;
            _advancedAppearanceService = advancedAppearanceService;
            _snapshotService = snapshotService;
            _edgeService = edgeService;
            _settingsCplService = settingsCplService;
            _fileExplorerService = fileExplorerService;

            AllTweaks = new ObservableCollection<SystemTweakItem>();
            ClassicTools = new ObservableCollection<ClassicAppItem>();
            OemInfo = new OemInfoData();
            Metrics = new WindowMetricsData();

            InitializeCategories();

            _filteredTweaks = CollectionViewSource.GetDefaultView(AllTweaks);
            _filteredTweaks.Filter = FilterTweakItem;

            IsAdmin = UacHelper.IsAdministrator();

            _ = RefreshAllAsync();
        }

        public ObservableCollection<SystemTweakItem> AllTweaks { get; }
        public ObservableCollection<ClassicAppItem> ClassicTools { get; }
        public ObservableCollection<TweakerCategoryModel> CategoryList { get; } = new();
        public ICollectionView FilteredTweaks => _filteredTweaks;

        [ObservableProperty]
        private OemInfoData _oemInfo;

        [ObservableProperty]
        private WindowMetricsData _metrics = new();

        [ObservableProperty]
        private TweakerStats _stats = new();

        [ObservableProperty]
        private string _activeCategory = "All"; // All, Windows11, Behavior, BootLogon, DesktopTaskbar, ContextMenu, Appearance, AdvancedAppearance, FileExplorer, SettingsCpl, Edge, Tools, ClassicApps

        partial void OnActiveCategoryChanged(string value)
        {
            foreach (var cat in CategoryList)
            {
                cat.IsSelected = cat.Key == value;
            }
            OnPropertyChanged(nameof(ActiveCategoryDisplayName));
            OnPropertyChanged(nameof(IsTweaksListVisible));
            OnPropertyChanged(nameof(IsToolsTabVisible));
            OnPropertyChanged(nameof(IsClassicAppsTabVisible));
            OnPropertyChanged(nameof(IsAdvancedAppearanceTabVisible));
            _filteredTweaks.Refresh();
        }

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
            UpdateCategoryCounts();
        }

        private void InitializeCategories()
        {
            CategoryList.Clear();
            CategoryList.Add(new TweakerCategoryModel
            {
                Key = "All",
                DisplayName = "Tüm İnce Ayarlar",
                IconSymbol = "AppsList24",
                ShortDescription = "Sistemdeki tüm Windows ince ayarları ve optimizasyon seçenekleri.",
                BenefitSummary = "Tüm kategorilerdeki ayarları tek bir liste üzerinden inceleyip yönetebilirsiniz.",
                IsSelected = true
            });
            CategoryList.Add(new TweakerCategoryModel
            {
                Key = "Windows11",
                DisplayName = "Windows 11",
                IconSymbol = "Desktop24",
                ShortDescription = "Windows 11'e özgü modern arayüz ve özellik ayarları.",
                BenefitSummary = "Yeni başlat menüsü, görev çubuğu ve modern deneyimleri sadeleştirip hızlandırır."
            });
            CategoryList.Add(new TweakerCategoryModel
            {
                Key = "Appearance",
                DisplayName = "Görünüm & Tema",
                IconSymbol = "Color24",
                ShortDescription = "Pencereler, animasyonlar ve tema özelleştirmeleri.",
                BenefitSummary = "Gereksiz görsel efektleri ve animasyonları kapatıp arayüz tepkisini hızlandırır."
            });
            CategoryList.Add(new TweakerCategoryModel
            {
                Key = "AdvancedAppearance",
                DisplayName = "Gelişmiş Görünüm",
                IconSymbol = "SlideGrid24",
                ShortDescription = "Pencere kenarlık boyutları, başlık yükseklikleri ve simge aralıkları.",
                BenefitSummary = "Masaüstü ve pencere metriklerini piksel hassasiyetiyle ekranınıza göre optimize eder."
            });
            CategoryList.Add(new TweakerCategoryModel
            {
                Key = "Behavior",
                DisplayName = "Sistem Davranışları",
                IconSymbol = "Settings24",
                ShortDescription = "Hata bildirimleri, bekleme süreleri ve arka plan davranışları.",
                BenefitSummary = "Sistemin donma ve yanıt vermeme sürelerini düşürür, arka plan yükünü hafifletir."
            });
            CategoryList.Add(new TweakerCategoryModel
            {
                Key = "BootLogon",
                DisplayName = "Açılış & Oturum",
                IconSymbol = "Power24",
                ShortDescription = "Bilgisayar açılışı, kilit ekranı ve oturum kontrolleri.",
                BenefitSummary = "Açılış hızını artırır, kilit ekranındaki gereksiz bekleme sürelerini kaldırır."
            });
            CategoryList.Add(new TweakerCategoryModel
            {
                Key = "DesktopTaskbar",
                DisplayName = "Masaüstü & Görev Çubuğu",
                IconSymbol = "Grid24",
                ShortDescription = "Görev çubuğu öğeleri, bildirim alanı ve masaüstü kısayolları.",
                BenefitSummary = "Çalışma alanınızı temiz tutar ve sık kullanılan işlevlere erişimi kolaylaştırır."
            });
            CategoryList.Add(new TweakerCategoryModel
            {
                Key = "ContextMenu",
                DisplayName = "Sağ Tık Menüsü",
                IconSymbol = "CursorHover24",
                ShortDescription = "Masaüstü ve dosya sağ tık (içerik) menüsü seçenekleri.",
                BenefitSummary = "Sağ tık menüsünü hızlandırır, kalabalığı azaltır ve pratik yönetim araçları ekler."
            });
            CategoryList.Add(new TweakerCategoryModel
            {
                Key = "FileExplorer",
                DisplayName = "Dosya Gezgini",
                IconSymbol = "Folder24",
                ShortDescription = "Dosya uzantıları, gizli öğeler ve gezgin gezinti bölmesi.",
                BenefitSummary = "Dosyaları daha hızlı bulmanızı ve tam sistem hakimiyetiyle yönetmenizi sağlar."
            });
            CategoryList.Add(new TweakerCategoryModel
            {
                Key = "SettingsCpl",
                DisplayName = "Ayarlar & Denetim Masası",
                IconSymbol = "Wrench24",
                ShortDescription = "Windows ayarlar sayfası ve klasik denetim masası denetimleri.",
                BenefitSummary = "Gereksiz ayar sayfalarını gizler ve kritik yönetim panellerine doğrudan erişim verir."
            });
            CategoryList.Add(new TweakerCategoryModel
            {
                Key = "Edge",
                DisplayName = "Microsoft Edge",
                IconSymbol = "Globe24",
                ShortDescription = "Edge tarayıcı başlangıç, telemetri ve yan panel ayarları.",
                BenefitSummary = "Arka planda Edge'in sistem kaynaklarını ve RAM tüketimini engeller."
            });
            CategoryList.Add(new TweakerCategoryModel
            {
                Key = "Tools",
                DisplayName = "Sistem Araçları",
                IconSymbol = "Wrench24",
                ShortDescription = "Önbellek temizleme, TrustedInstaller terminali ve OEM bilgileri.",
                BenefitSummary = "Simge önbelleği bozulmalarını onarır ve derin sistem müdahalelerini kolaylaştırır."
            });
            CategoryList.Add(new TweakerCategoryModel
            {
                Key = "ClassicApps",
                DisplayName = "Klasik Uygulamalar",
                IconSymbol = "WindowApps24",
                ShortDescription = "Eski Windows Fotoğraf Görüntüleyici ve klasik sistem konsolları.",
                BenefitSummary = "Hızlı ve kararlı klasik Windows yardımcı araçlarını tek tıkla geri getirir."
            });
            CategoryList.Add(new TweakerCategoryModel
            {
                Key = "PrivacyDebloat",
                DisplayName = "Gizlilik & Debloat",
                IconSymbol = "Shield24",
                ShortDescription = "Telemetri, veri toplama ve yerleşik gereksiz uygulamaların kaldırılması.",
                BenefitSummary = "Arka plandaki telemetriyi keserek tam gizlilik ve performans modülü sayfasına yönlendirir."
            });
        }

        private void UpdateCategoryCounts()
        {
            foreach (var cat in CategoryList)
            {
                switch (cat.Key)
                {
                    case "All":
                        cat.TotalCount = AllTweaks.Count;
                        cat.ActiveCount = AllTweaks.Count(t => t.IsEnabled);
                        break;
                    case "Windows11":
                        cat.TotalCount = AllTweaks.Count(t => t.Category.Contains("Windows 11"));
                        cat.ActiveCount = AllTweaks.Count(t => t.Category.Contains("Windows 11") && t.IsEnabled);
                        break;
                    case "Appearance":
                        cat.TotalCount = AllTweaks.Count(t => t.Category.Contains("Görünüm"));
                        cat.ActiveCount = AllTweaks.Count(t => t.Category.Contains("Görünüm") && t.IsEnabled);
                        break;
                    case "AdvancedAppearance":
                        cat.TotalCount = 7;
                        cat.ActiveCount = 7;
                        break;
                    case "Behavior":
                        cat.TotalCount = AllTweaks.Count(t => t.Category.Contains("Davranışlar"));
                        cat.ActiveCount = AllTweaks.Count(t => t.Category.Contains("Davranışlar") && t.IsEnabled);
                        break;
                    case "BootLogon":
                        cat.TotalCount = AllTweaks.Count(t => t.Category.Contains("Açılış"));
                        cat.ActiveCount = AllTweaks.Count(t => t.Category.Contains("Açılış") && t.IsEnabled);
                        break;
                    case "DesktopTaskbar":
                        cat.TotalCount = AllTweaks.Count(t => t.Category.Contains("Masaüstü"));
                        cat.ActiveCount = AllTweaks.Count(t => t.Category.Contains("Masaüstü") && t.IsEnabled);
                        break;
                    case "ContextMenu":
                        cat.TotalCount = AllTweaks.Count(t => t.Category.Contains("Sağ Tık"));
                        cat.ActiveCount = AllTweaks.Count(t => t.Category.Contains("Sağ Tık") && t.IsEnabled);
                        break;
                    case "FileExplorer":
                        cat.TotalCount = AllTweaks.Count(t => t.Category.Contains("Dosya Gezgini"));
                        cat.ActiveCount = AllTweaks.Count(t => t.Category.Contains("Dosya Gezgini") && t.IsEnabled);
                        break;
                    case "SettingsCpl":
                        cat.TotalCount = AllTweaks.Count(t => t.Category.Contains("Ayarlar"));
                        cat.ActiveCount = AllTweaks.Count(t => t.Category.Contains("Ayarlar") && t.IsEnabled);
                        break;
                    case "Edge":
                        cat.TotalCount = AllTweaks.Count(t => t.Category.Contains("Edge"));
                        cat.ActiveCount = AllTweaks.Count(t => t.Category.Contains("Edge") && t.IsEnabled);
                        break;
                    case "Tools":
                        cat.TotalCount = 4;
                        cat.ActiveCount = 4;
                        break;
                    case "ClassicApps":
                        cat.TotalCount = ClassicTools.Count;
                        cat.ActiveCount = ClassicTools.Count(c => c.IsActivated);
                        break;
                    case "PrivacyDebloat":
                        cat.TotalCount = 0;
                        cat.ActiveCount = 0;
                        break;
                }
            }
        }
    }
}
