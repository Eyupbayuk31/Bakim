using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;
using Bakım.Helpers;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    /// <summary>
    /// Kabuk (shell) ViewModel'i: navigasyon, komut paleti, bildirimler.
    ///
    /// v3.1'e kadar bu sınıf yapıcı metodunda 22 servisi ve 14 alt ViewModel'i
    /// elle örnekliyordu; DI konteyneri kayıtlıydı ama hiç kullanılmıyordu.
    /// Artık her şey konteynerden gelir ve alt modüller İLK GEZİNMEDE oluşturulur.
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        private readonly IServiceProvider _services;
        private readonly ICommandPaletteService _paletteService;
        private readonly IThemeService _themeService;
        private readonly IGameModeService _gameModeService;
        private readonly ITelemetryService _telemetryService;
        private readonly DispatcherTimer _miniTelemetryTimer;
        private readonly ILogService _log;

        // Alt modüller ilk erişimde oluşturulur: açılışta 14 ViewModel birden
        // ayağa kalkmaz, 14 servis grafiği çözülmez.
        private readonly Lazy<DashboardViewModel> _dashboard;
        private readonly Lazy<CleanerViewModel> _cleaner;
        private readonly Lazy<OptimizerViewModel> _optimizer;
        private readonly Lazy<StartupViewModel> _startup;
        private readonly Lazy<SystemInfoViewModel> _systemInfo;
        private readonly Lazy<NetworkMonitorViewModel> _networkMonitor;
        private readonly Lazy<ServiceManagerViewModel> _serviceManager;
        private readonly Lazy<PrivacyDebloatViewModel> _privacyDebloat;
        private readonly Lazy<CrashAnalyzerViewModel> _crashAnalyzer;
        private readonly Lazy<UninstallerViewModel> _uninstaller;
        private readonly Lazy<AnalyzerViewModel> _analyzer;
        private readonly Lazy<WindowsTweakerViewModel> _windowsTweaker;
        private readonly Lazy<SettingsViewModel> _settings;
        private readonly Lazy<TweakerCategoriesViewModel> _tweakerCategories;
        private readonly Lazy<StoreViewModel> _store;

        /// <summary>Şu an etkin olan modül; gezinirken devre dışı bırakılır.</summary>
        private IModuleViewModel? _activeModule;

        public MainViewModel(
            IServiceProvider services,
            ICommandPaletteService paletteService,
            IThemeService themeService,
            INavigationService navigationService,
            IBackgroundMaintenanceService maintenanceService,
            IGameModeService gameModeService,
            ITelemetryService telemetryService,
            ILogService log)
        {
            _services = services;
            _paletteService = paletteService;
            _themeService = themeService;
            _gameModeService = gameModeService;
            _telemetryService = telemetryService;
            _log = log;

            _isGameModeActive = _gameModeService.IsGameModeActive;
            _gameModeService.GameModeChanged += active => OnUiThread(() => IsGameModeActive = active);

            _miniTelemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _miniTelemetryTimer.Tick += async (_, _) => await UpdateMiniTelemetryAsync();
            _miniTelemetryTimer.Start();
            _ = UpdateMiniTelemetryAsync();

            _dashboard = Lazy(() =>
            {
                var vm = _services.GetRequiredService<DashboardViewModel>();
                vm.NavigateToCleanerRequested += () => Navigate("Cleaner");
                vm.NavigateToOptimizerRequested += () => Navigate("Optimizer");
                return vm;
            });

            _cleaner = Lazy(_services.GetRequiredService<CleanerViewModel>);
            _optimizer = Lazy(_services.GetRequiredService<OptimizerViewModel>);
            _startup = Lazy(_services.GetRequiredService<StartupViewModel>);
            _systemInfo = Lazy(_services.GetRequiredService<SystemInfoViewModel>);
            _networkMonitor = Lazy(_services.GetRequiredService<NetworkMonitorViewModel>);
            _serviceManager = Lazy(_services.GetRequiredService<ServiceManagerViewModel>);
            _privacyDebloat = Lazy(_services.GetRequiredService<PrivacyDebloatViewModel>);
            _crashAnalyzer = Lazy(_services.GetRequiredService<CrashAnalyzerViewModel>);
            _uninstaller = Lazy(_services.GetRequiredService<UninstallerViewModel>);
            _analyzer = Lazy(_services.GetRequiredService<AnalyzerViewModel>);
            _windowsTweaker = Lazy(_services.GetRequiredService<WindowsTweakerViewModel>);
            _settings = Lazy(_services.GetRequiredService<SettingsViewModel>);
            _tweakerCategories = Lazy(_services.GetRequiredService<TweakerCategoriesViewModel>);
            _store = Lazy(_services.GetRequiredService<StoreViewModel>);

            navigationService.NavigationRequested += target => Navigate(target);
            navigationService.TweakerCategoryRequested += NavigateToTweakerCategory;

            SubscribeToBackgroundMaintenance(maintenanceService);

            IsAdmin = UacHelper.IsAdministrator();
            FilteredCommands = new ObservableCollection<CommandPaletteItem>(
                _paletteService.GetAllCommands().Take(12));

            // Açılış modülü
            _currentView = Dashboard;
            _ = ActivateAsync(Dashboard);

            _ = CheckForUpdatesOnStartupAsync();
        }

        private static Lazy<T> Lazy<T>(Func<T> factory) =>
            new(factory, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        #region Alt Modüller (lazy)

        public DashboardViewModel Dashboard => _dashboard.Value;
        public CleanerViewModel Cleaner => _cleaner.Value;
        public OptimizerViewModel Optimizer => _optimizer.Value;
        public StartupViewModel Startup => _startup.Value;
        public SystemInfoViewModel SystemInfo => _systemInfo.Value;
        public NetworkMonitorViewModel NetworkMonitor => _networkMonitor.Value;
        public ServiceManagerViewModel ServiceManager => _serviceManager.Value;
        public PrivacyDebloatViewModel PrivacyDebloat => _privacyDebloat.Value;
        public CrashAnalyzerViewModel CrashAnalyzer => _crashAnalyzer.Value;
        public UninstallerViewModel Uninstaller => _uninstaller.Value;
        public AnalyzerViewModel Analyzer => _analyzer.Value;
        public WindowsTweakerViewModel WindowsTweaker => _windowsTweaker.Value;
        public SettingsViewModel Settings => _settings.Value;
        public TweakerCategoriesViewModel TweakerCategories => _tweakerCategories.Value;
        public StoreViewModel Store => _store.Value;

        #endregion

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
            => OnPropertyChanged(nameof(CanShowTweakerSubMenu));

        partial void OnIsTweakerMenuExpandedChanged(bool value)
            => OnPropertyChanged(nameof(CanShowTweakerSubMenu));

        [ObservableProperty]
        private bool _isAdmin;

        [ObservableProperty]
        private string _tweaksBadge = "14 Aktif";

        [ObservableProperty]
        private string _uninstallerBadge = "Programlar";

        [ObservableProperty]
        private string _ramBadge = "%58";

        [ObservableProperty]
        private bool _isGameModeActive;

        [ObservableProperty]
        private string _miniCpuText = "%0";

        [ObservableProperty]
        private string _miniRamText = "%0";

        public string CurrentUserName => Environment.UserName;

        public event Action? LogoutRequested;

        private async Task UpdateMiniTelemetryAsync()
        {
            try
            {
                var sample = await _telemetryService.SampleMetricsAsync();
                MiniCpuText = $"%{sample.CpuUsagePercentage}";
                MiniRamText = $"%{sample.RamUsagePercentage}";
            }
            catch { }
        }

        [RelayCommand]
        public async Task ToggleGameModeAsync()
        {
            long freed = await _gameModeService.ToggleGameModeAsync();
            IsGameModeActive = _gameModeService.IsGameModeActive;
            if (IsGameModeActive)
            {
                ShowToast("Oyun Modu Açık", _gameModeService.LastActionSummary, InfoBarSeverity.Success, "TopSpeed24");
            }
            else
            {
                ShowToast("Oyun Modu Kapatıldı", _gameModeService.LastActionSummary, InfoBarSeverity.Informational, "CheckmarkCircle24");
            }
        }

        [RelayCommand]
        public async Task FastRamBoostAsync()
        {
            try
            {
                var cleanService = _services.GetService<ISystemCleanService>();
                if (cleanService != null)
                {
                    long freed = await cleanService.AutoTrimWorkingSetsAsync();
                    bool significant = Bakım.Core.Text.MemoryResultText.IsSignificant(freed);
                    ShowToast("Bellek İşlemi", Bakım.Core.Text.MemoryResultText.Describe(freed),
                        significant ? InfoBarSeverity.Success : InfoBarSeverity.Informational, "TopSpeed24");
                    await UpdateMiniTelemetryAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Error("Hızlı RAM temizliği başarısız.", ex, nameof(MainViewModel));
            }
        }

        [RelayCommand]
        public async Task RefreshActiveModuleAsync()
        {
            if (_activeModule != null)
            {
                try
                {
                    await _activeModule.OnActivatedAsync();
                    ShowToast("Yenilendi", "Aktif modül verileri güncellendi.", InfoBarSeverity.Informational, "ArrowClockwise24");
                }
                catch (Exception ex)
                {
                    _log.Error("Modül yenilenirken hata oluştu.", ex, nameof(MainViewModel));
                }
            }
            await UpdateMiniTelemetryAsync();
        }

        #endregion

        #region Arka Plan Bakım Bildirimleri

        private void SubscribeToBackgroundMaintenance(IBackgroundMaintenanceService maintenance)
        {
            maintenance.HighRamDetected += percent => OnUiThread(() =>
                ShowToast(
                    "Yüksek Bellek Kullanımı",
                    $"RAM kullanımı %{percent} seviyesinde. Tek tıkla hızlandırmayı deneyebilirsiniz.",
                    InfoBarSeverity.Warning,
                    "Warning24"));

            maintenance.AutoRamCleanCompleted += freedBytes => OnUiThread(() =>
                ShowToast(
                    "Otomatik RAM Temizliği",
                    $"{CleanCategory.FormatBytes(freedBytes)} bellek geri kazanıldı.",
                    InfoBarSeverity.Success,
                    "TopSpeed24"));
        }

        private static void OnUiThread(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            if (dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        }

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
        public void CloseCommandPalette() => IsCommandPaletteOpen = false;

        [RelayCommand]
        public void SelectNextCommand()
        {
            if (FilteredCommands.Count == 0) return;
            int idx = SelectedCommand != null ? FilteredCommands.IndexOf(SelectedCommand) : -1;
            SelectedCommand = idx < FilteredCommands.Count - 1
                ? FilteredCommands[idx + 1]
                : FilteredCommands[0];
        }

        [RelayCommand]
        public void SelectPreviousCommand()
        {
            if (FilteredCommands.Count == 0) return;
            int idx = SelectedCommand != null ? FilteredCommands.IndexOf(SelectedCommand) : -1;
            SelectedCommand = idx > 0
                ? FilteredCommands[idx - 1]
                : FilteredCommands[FilteredCommands.Count - 1];
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
                        else if (parts.Length == 2 && parts[0] == "Store")
                        {
                            Navigate("Store");
                            if (parts[1] == "ClassicTools")
                            {
                                Store.SwitchToClassicTools();
                            }
                            else if (parts[1] == "Presets")
                            {
                                Store.SwitchToPresets();
                            }
                        }
                    }
                    else
                    {
                        Navigate(target.TargetParameter);
                    }
                    ShowToast("Sayfa Açıldı", $"{target.Title} modülüne geçiş yapıldı.",
                        InfoBarSeverity.Informational, target.IconName);
                    break;

                case CommandActionKind.QuickAction:
                    ExecuteQuickAction(target.TargetParameter, target.Title);
                    break;
            }
        }

        /// <summary>
        /// Komut paleti hızlı eylemleri. Bildirim işlem BİTTİKTEN sonra ve gerçek sonuçla
        /// gösterilir (eskiden işlem başlarken "başarıyla temizlendi" deniyordu).
        /// </summary>
        private async void ExecuteQuickAction(string parameter, string title)
        {
            try
            {
                await ExecuteQuickActionCoreAsync(parameter, title);
            }
            catch (Exception ex)
            {
                _log.Error($"Hızlı eylem başarısız: {parameter}", ex, nameof(MainViewModel));
                ShowToast("İşlem Başarısız", ex.Message, InfoBarSeverity.Error, "ErrorCircle24");
            }
        }

        private async Task ExecuteQuickActionCoreAsync(string parameter, string title)
        {
            switch (parameter)
            {
                case "QuickBoost":
                    await Dashboard.OneClickBoostCommand.ExecuteAsync(null);
                    ShowToast("Bellek İşlemi", Dashboard.OptimizationResultMessage, InfoBarSeverity.Informational, "TopSpeed24");
                    break;

                case "RestartExplorer":
                    await WindowsTweaker.RestartExplorerCommand.ExecuteAsync(null);
                    ShowToast("Gezgin Yeniden Başlatıldı", "Windows Gezgini yeniden başlatıldı.", InfoBarSeverity.Success, "ArrowClockwise24");
                    break;

                case "CreateRestorePoint":
                    await PrivacyDebloat.CreateRestorePointCommand.ExecuteAsync(null);
                    ShowToast("Geri Yükleme Noktası", PrivacyDebloat.LastRestorePointResult, InfoBarSeverity.Informational, "History24");
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

                case "Theme:Light":
                    _themeService.ApplyTheme(AppThemeKind.FluentLight);
                    ShowToast("Tema Güncellendi", "Fluent Açık teması uygulandı.", InfoBarSeverity.Success, "WeatherSunny24");
                    break;

                default:
                    _log.Warning($"Tanınmayan hızlı eylem: {parameter}", null, nameof(MainViewModel));
                    ShowToast("Bilinmeyen Eylem", $"'{title}' için tanımlı bir işlem yok.", InfoBarSeverity.Warning, "Warning24");
                    break;
            }
        }

        #endregion

        #region Floating Toast Notification System

        /// <summary>Aynı anda gösterilecek en fazla bildirim; fazlası en eskiyi düşürür.</summary>
        private const int MaxVisibleToasts = 4;

        public void ShowToast(string title, string message,
            InfoBarSeverity severity = InfoBarSeverity.Success,
            string iconName = "CheckmarkCircle24")
        {
            var toast = new ToastNotificationItem
            {
                Title = title,
                Message = message,
                Severity = severity,
                IconName = iconName
            };

            // Bildirim yağmurunda ekranın dolmasını engelle
            while (ActiveToasts.Count >= MaxVisibleToasts)
            {
                ActiveToasts.RemoveAt(0);
            }

            ActiveToasts.Add(toast);

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

        #region Navigation

        [RelayCommand]
        public void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

        [RelayCommand]
        public void ToggleTweakerMenu() => IsTweakerMenuExpanded = !IsTweakerMenuExpanded;

        /// <summary>
        /// Yeniden giriş koruması: bir alt ViewModel kurulurken navigasyon
        /// olayı yayınlarsa bu metot tekrar çağrılır ve henüz kurulmakta olan
        /// Lazy&lt;T&gt; örneğine erişmeye çalışır. .NET bunu deadlock riski
        /// sayıp InvalidOperationException fırlatır. Dıştaki çağrı zaten
        /// doğru kategoriye gideceği için içteki çağrı güvenle yok sayılır.
        /// </summary>
        private bool _isSwitchingTweakerCategory;

        [RelayCommand]
        public void NavigateToTweakerCategory(string categoryKey)
        {
            if (_isSwitchingTweakerCategory) return;
            _isSwitchingTweakerCategory = true;

            try
            {
                CurrentTweakerCategory = categoryKey;
                TweakerCategories.SetSelectedCategorySilent(categoryKey);

                WindowsTweaker.SwitchCategory(categoryKey);
                Navigate("Tweaker");

                if (IsSidebarExpanded) IsTweakerMenuExpanded = true;
            }
            catch (Exception ex)
            {
                _log.Error($"Tweaker kategorisine geçiş sırasında hata oluştu: {categoryKey}", ex, nameof(MainViewModel));
                Navigate("Tweaker");
            }
            finally
            {
                _isSwitchingTweakerCategory = false;
            }
        }

        [RelayCommand]
        public void Navigate(string target)
        {
            if (string.Equals(target, "PrivacyDebloat", StringComparison.OrdinalIgnoreCase))
            {
                WindowsTweaker.SwitchCategory("PrivacyDebloat");
                target = "Tweaker";
            }

            if (!AppModuleRegistry.TryResolve(target, out var module))
            {
                // Sessizce Panoya düşmek yerine sorunu görünür kıl.
                _log.Warning(
                    $"Bilinmeyen navigasyon anahtarı: '{target}'. Panoya dönülüyor.",
                    null, nameof(MainViewModel));
                module = AppModule.Dashboard;
            }

            CurrentNavKey = target;

            if (module is AppModule.WindowsTweaker or AppModule.PrivacyDebloat && IsSidebarExpanded)
            {
                IsTweakerMenuExpanded = true;
            }

            object view = module switch
            {
                AppModule.Dashboard => Dashboard,
                AppModule.Cleaner => Cleaner,
                AppModule.Optimizer => Optimizer,
                AppModule.Startup => Startup,
                AppModule.SystemInfo => SystemInfo,
                AppModule.NetworkMonitor => NetworkMonitor,
                AppModule.ServiceManager => ServiceManager,
                AppModule.PrivacyDebloat => PrivacyDebloat,
                AppModule.CrashAnalyzer => CrashAnalyzer,
                AppModule.Uninstaller => Uninstaller,
                AppModule.Analyzer => Analyzer,
                AppModule.WindowsTweaker => WindowsTweaker,
                AppModule.Settings => Settings,
                AppModule.Store => Store,
                _ => Dashboard
            };

            if (ReferenceEquals(view, CurrentView)) return;

            CurrentView = view;
            _ = SwitchActiveModuleAsync(view);
        }

        /// <summary>Eski modülü durdurur, yenisini başlatır.</summary>
        private async Task SwitchActiveModuleAsync(object view)
        {
            var previous = _activeModule;
            _activeModule = view as IModuleViewModel;

            if (previous != null && !ReferenceEquals(previous, _activeModule))
            {
                try
                {
                    await previous.OnDeactivatedAsync();
                }
                catch (Exception ex)
                {
                    _log.Error("Modül devre dışı bırakılırken hata.", ex, nameof(MainViewModel));
                }
            }

            if (_activeModule != null)
            {
                await ActivateAsync(_activeModule);
            }
        }

        private async Task ActivateAsync(object view)
        {
            if (view is not IModuleViewModel module) return;

            _activeModule = module;
            try
            {
                await module.OnActivatedAsync();
            }
            catch (Exception ex)
            {
                _log.Error("Modül etkinleştirilirken hata.", ex, nameof(MainViewModel));
            }
        }

        [RelayCommand]
        public void RestartAsAdmin() => UacHelper.RestartAsAdministrator();

        [RelayCommand]
        public void ToggleTheme()
        {
            _themeService.ToggleNextTheme();
            ShowToast("Tema Değiştirildi",
                $"Yeni tema aktif: {_themeService.CurrentDefinition.DisplayName}",
                InfoBarSeverity.Informational, "DarkTheme24");
        }

        [RelayCommand]
        public void Logout() => LogoutRequested?.Invoke();

        private async Task CheckForUpdatesOnStartupAsync()
        {
            try
            {
                await Task.Delay(4000);
                var update = await AutoUpdateService.CheckForUpdatesAsync();

                if (update.IsUpdateAvailable && !string.IsNullOrEmpty(update.DownloadUrl))
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        var dialog = new Views.Dialogs.UpdateDialogView(update)
                        {
                            Owner = System.Windows.Application.Current.MainWindow
                        };
                        dialog.ShowDialog();
                    });
                }
            }
            catch (Exception ex)
            {
                // Başlangıçta kullanıcıyı rahatsız etme, ama sessizce de kaybetme
                _log.Warning("Açılış güncelleme denetimi başarısız.", ex, nameof(MainViewModel));
            }
        }

        #endregion
    }
}
