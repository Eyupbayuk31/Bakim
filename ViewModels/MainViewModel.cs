using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;
using Bakım.Helpers;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly ICommandPaletteService _paletteService;
        private readonly IThemeService _themeService;

        public MainViewModel() : this(null, null, null)
        {
        }

        public MainViewModel(
            ISystemCleanService? cleanService,
            ISystemInfoService? infoService,
            IStartupService? startupService,
            INetworkMonitorService? networkService = null,
            IServiceManagerService? serviceManager = null,
            IPrivacyDebloatService? privacyService = null,
            ICrashAnalyzerService? crashService = null,
            IUninstallerService? uninstallerService = null,
            IBehaviorTweaksService? behaviorService = null,
            IBootLogonTweaksService? bootLogonService = null,
            IDesktopTaskbarTweaksService? desktopTaskbarService = null,
            IContextMenuShortcutsService? contextMenuService = null,
            ISystemToolsService? toolsService = null,
            IClassicAppsService? classicAppsService = null,
            IWindows11TweaksService? win11TweaksService = null,
            IAppearanceTweaksService? appearanceService = null,
            IAdvancedAppearanceService? advancedAppearanceService = null,
            ITweaksSnapshotService? snapshotService = null,
            IEdgeTweaksService? edgeService = null,
            ISettingsControlPanelTweaksService? settingsCplService = null,
            IFileExplorerTweaksService? fileExplorerService = null,
            ITelemetryService? telemetryService = null,
            ICommandPaletteService? paletteService = null,
            IThemeService? themeService = null)
        {
            var cleanSvc = cleanService ?? new SystemCleanService();
            var infoSvc = infoService ?? new SystemInfoService();
            var startupSvc = startupService ?? new StartupService();
            var netSvc = networkService ?? new NetworkMonitorService();
            var servSvc = serviceManager ?? new ServiceManagerService();
            var privSvc = privacyService ?? new PrivacyDebloatService();
            var crashSvc = crashService ?? new CrashAnalyzerService();
            var uninstSvc = uninstallerService ?? new UninstallerService();
            var behaviorSvc = behaviorService ?? new BehaviorTweaksService();
            var bootLogonSvc = bootLogonService ?? new BootLogonTweaksService();
            var dtSvc = desktopTaskbarService ?? new DesktopTaskbarTweaksService();
            var cmSvc = contextMenuService ?? new ContextMenuShortcutsService();
            var toolsSvc = toolsService ?? new SystemToolsService();
            var classicSvc = classicAppsService ?? new ClassicAppsService();
            var win11Svc = win11TweaksService ?? new Windows11TweaksService();
            var appearSvc = appearanceService ?? new AppearanceTweaksService();
            var advAppearSvc = advancedAppearanceService ?? new AdvancedAppearanceService();
            var snapSvc = snapshotService ?? new TweaksSnapshotService();
            var edgeSvc = edgeService ?? new EdgeTweaksService();
            var setCplSvc = settingsCplService ?? new SettingsControlPanelTweaksService();
            var feSvc = fileExplorerService ?? new FileExplorerTweaksService();
            var teleSvc = telemetryService ?? new TelemetryService();

            _paletteService = paletteService ?? new CommandPaletteService();
            _themeService = themeService ?? new ThemeService();

            Dashboard = new DashboardViewModel(cleanSvc, infoSvc, teleSvc);
            Cleaner = new CleanerViewModel(cleanSvc);
            Optimizer = new OptimizerViewModel(cleanSvc, infoSvc);
            Startup = new StartupViewModel(startupSvc);
            SystemInfo = new SystemInfoViewModel(infoSvc);
            NetworkMonitor = new NetworkMonitorViewModel(netSvc);
            ServiceManager = new ServiceManagerViewModel(servSvc);
            PrivacyDebloat = new PrivacyDebloatViewModel(privSvc);
            CrashAnalyzer = new CrashAnalyzerViewModel(crashSvc);
            Uninstaller = new UninstallerViewModel(uninstSvc);
            WindowsTweaker = new WindowsTweakerViewModel(
                behaviorSvc, bootLogonSvc, dtSvc, cmSvc, toolsSvc,
                classicSvc, win11Svc, appearSvc, advAppearSvc, snapSvc,
                edgeSvc, setCplSvc, feSvc);
            Settings = new SettingsViewModel();
            TweakerCategories = new TweakerCategoriesViewModel();

            // Alt viewmodel navigasyon olayları
            Dashboard.NavigateToCleanerRequested += () => Navigate("Cleaner");
            Dashboard.NavigateToOptimizerRequested += () => Navigate("Optimizer");

            NavigationService.Instance.NavigationRequested += target => Navigate(target);
            NavigationService.Instance.TweakerCategoryRequested += cat => NavigateToTweakerCategory(cat);

            IsAdmin = UacHelper.IsAdministrator();
            CurrentView = Dashboard;
            CurrentNavKey = "Dashboard";

            FilteredCommands = new ObservableCollection<CommandPaletteItem>(_paletteService.GetAllCommands().Take(12));
        }

        public DashboardViewModel Dashboard { get; }
        public CleanerViewModel Cleaner { get; }
        public OptimizerViewModel Optimizer { get; }
        public StartupViewModel Startup { get; }
        public SystemInfoViewModel SystemInfo { get; }
        public NetworkMonitorViewModel NetworkMonitor { get; }
        public ServiceManagerViewModel ServiceManager { get; }
        public PrivacyDebloatViewModel PrivacyDebloat { get; }
        public CrashAnalyzerViewModel CrashAnalyzer { get; }
        public UninstallerViewModel Uninstaller { get; }
        public WindowsTweakerViewModel WindowsTweaker { get; }
        public SettingsViewModel Settings { get; }
        public TweakerCategoriesViewModel TweakerCategories { get; }

        public ObservableCollection<ToastNotificationItem> ActiveToasts { get; } = new();

        #region Navigation & Layout Properties

        [ObservableProperty]
        private object _currentView;

        [ObservableProperty]
        private string _currentNavKey = "Dashboard";

        [ObservableProperty]
        private bool _isSidebarExpanded = true;

        [ObservableProperty]
        private bool _isTweakerMenuExpanded = false;

        [ObservableProperty]
        private string _currentTweakerCategory = "All";

        public bool CanShowTweakerSubMenu => IsSidebarExpanded && IsTweakerMenuExpanded;

        partial void OnIsSidebarExpandedChanged(bool value)
        {
            OnPropertyChanged(nameof(CanShowTweakerSubMenu));
        }

        partial void OnIsTweakerMenuExpandedChanged(bool value)
        {
            OnPropertyChanged(nameof(CanShowTweakerSubMenu));
        }

        [ObservableProperty]
        private bool _isAdmin;

        // Dynamic Sidebar Badges
        [ObservableProperty]
        private string _tweaksBadge = "14 Aktif";

        [ObservableProperty]
        private string _uninstallerBadge = "Programlar";

        [ObservableProperty]
        private string _ramBadge = "%58";

        public event Action? LogoutRequested;

        #endregion

        #region Command Palette (Raycast / Spotlight Ctrl+K)

        [ObservableProperty]
        private bool _isCommandPaletteOpen;

        [ObservableProperty]
        private string _commandPaletteQuery = string.Empty;

        [ObservableProperty]
        private ObservableCollection<CommandPaletteItem> _filteredCommands = new();

        [ObservableProperty]
        private CommandPaletteItem? _selectedCommand;

        partial void OnCommandPaletteQueryChanged(string value)
        {
            var results = _paletteService.Search(value);
            FilteredCommands.Clear();
            foreach (var r in results) FilteredCommands.Add(r);
            if (FilteredCommands.Count > 0)
                SelectedCommand = FilteredCommands[0];
        }

        [RelayCommand]
        public void OpenCommandPalette()
        {
            CommandPaletteQuery = string.Empty;
            FilteredCommands.Clear();
            foreach (var r in _paletteService.GetAllCommands().Take(12)) FilteredCommands.Add(r);
            if (FilteredCommands.Count > 0) SelectedCommand = FilteredCommands[0];
            IsCommandPaletteOpen = true;
        }

        [RelayCommand]
        public void CloseCommandPalette()
        {
            IsCommandPaletteOpen = false;
        }

        [RelayCommand]
        public void ExecuteCommand(CommandPaletteItem? item)
        {
            var target = item ?? SelectedCommand ?? FilteredCommands.FirstOrDefault();
            if (target == null) return;

            CloseCommandPalette();

            switch (target.ActionKind)
            {
                case CommandActionKind.Navigate:
                    if (target.TargetParameter.Contains(':'))
                    {
                        var parts = target.TargetParameter.Split(':');
                        if (parts.Length == 2 && parts[0] == "WindowsTweaker")
                        {
                            NavigateToTweakerCategory(parts[1]);
                        }
                    }
                    else
                    {
                        Navigate(target.TargetParameter);
                    }
                    ShowToast("Sayfa Açıldı", $"{target.Title} modülüne geçiş yapıldı.", InfoBarSeverity.Informational, target.IconName);
                    break;

                case CommandActionKind.QuickAction:
                    ExecuteQuickAction(target.TargetParameter, target.Title);
                    break;
            }
        }

        private void ExecuteQuickAction(string parameter, string title)
        {
            switch (parameter)
            {
                case "QuickBoost":
                    _ = Dashboard.OneClickBoostCommand.ExecuteAsync(null);
                    ShowToast("Sistem Hızlandırıldı", "RAM ve geçici önbellek başarıyla temizlendi!", InfoBarSeverity.Success, "TopSpeed24");
                    break;

                case "RestartExplorer":
                    _ = WindowsTweaker.RestartExplorerCommand.ExecuteAsync(null);
                    ShowToast("Gezgin Yeniden Başlatıldı", "Windows Gezgini süreci tazelendi.", InfoBarSeverity.Success, "ArrowClockwise24");
                    break;

                case "CreateRestorePoint":
                    _ = PrivacyDebloat.CreateRestorePointCommand.ExecuteAsync(null);
                    ShowToast("Geri Yükleme Noktası", "Windows Sistem Koruması yedeği alındı.", InfoBarSeverity.Success, "History24");
                    break;

                case "ElevateAdmin":
                    RestartAsAdmin();
                    break;

                case "ToggleTheme":
                    ToggleTheme();
                    break;

                case "Theme:Amoled":
                    _themeService.ApplyTheme(AppThemeKind.AmoledBlack);
                    ShowToast("Tema Güncellendi", "AMOLED Pure Black teması uygulandı.", InfoBarSeverity.Success, "DarkTheme24");
                    break;

                case "Theme:Cyberpunk":
                    _themeService.ApplyTheme(AppThemeKind.CyberpunkPurple);
                    ShowToast("Tema Güncellendi", "Cyberpunk Neon Mor teması uygulandı.", InfoBarSeverity.Success, "Color24");
                    break;

                default:
                    ShowToast("İşlem Tamamlandı", $"{title} başarıyla icra edildi.", InfoBarSeverity.Success, "CheckmarkCircle24");
                    break;
            }
        }

        #endregion

        #region Floating Toast Notification System

        public void ShowToast(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Success, string iconName = "CheckmarkCircle24")
        {
            var toast = new ToastNotificationItem
            {
                Title = title,
                Message = message,
                Severity = severity,
                IconName = iconName
            };

            ActiveToasts.Add(toast);

            // 3.5 saniye sonra otomatik kaldır
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                ActiveToasts.Remove(toast);
            };
            timer.Start();
        }

        [RelayCommand]
        public void DismissToast(ToastNotificationItem? toast)
        {
            if (toast != null) ActiveToasts.Remove(toast);
        }

        #endregion

        #region Standard Navigation Commands

        [RelayCommand]
        public void ToggleSidebar()
        {
            IsSidebarExpanded = !IsSidebarExpanded;
        }

        [RelayCommand]
        public void ToggleTweakerMenu()
        {
            IsTweakerMenuExpanded = !IsTweakerMenuExpanded;
        }

        [RelayCommand]
        public void NavigateToTweakerCategory(string categoryKey)
        {
            CurrentTweakerCategory = categoryKey;
            TweakerCategories.SetSelectedCategorySilent(categoryKey);

            if (string.Equals(categoryKey, "PrivacyDebloat", StringComparison.OrdinalIgnoreCase))
            {
                Navigate("PrivacyDebloat");
            }
            else
            {
                WindowsTweaker.SwitchCategory(categoryKey);
                Navigate("Tweaker");
            }

            if (IsSidebarExpanded)
            {
                IsTweakerMenuExpanded = true;
            }
        }

        [RelayCommand]
        public void Navigate(string target)
        {
            CurrentNavKey = target;
            if (target == "Tweaker" || target == "WindowsTweaker" || target == "PrivacyDebloat")
            {
                if (IsSidebarExpanded)
                {
                    IsTweakerMenuExpanded = true;
                }
            }

            CurrentView = target switch
            {
                "Dashboard" => Dashboard,
                "Cleaner" => Cleaner,
                "Optimizer" => Optimizer,
                "Startup" => Startup,
                "SystemInfo" => SystemInfo,
                "Network" => NetworkMonitor,
                "ServiceManager" => ServiceManager,
                "PrivacyDebloat" => PrivacyDebloat,
                "CrashAnalyzer" => CrashAnalyzer,
                "Uninstaller" => Uninstaller,
                "Tweaker" => WindowsTweaker,
                "WindowsTweaker" => WindowsTweaker,
                "Settings" => Settings,
                _ => Dashboard
            };
        }

        [RelayCommand]
        public void RestartAsAdmin()
        {
            UacHelper.RestartAsAdministrator();
        }

        [RelayCommand]
        public void ToggleTheme()
        {
            _themeService.ToggleNextTheme();
            ShowToast("Tema Değiştirildi", $"Yeni tema aktif: {_themeService.CurrentTheme}", InfoBarSeverity.Informational, "DarkTheme24");
        }

        [RelayCommand]
        public void Logout()
        {
            LogoutRequested?.Invoke();
        }

        #endregion
    }
}
