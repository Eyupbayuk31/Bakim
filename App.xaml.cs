using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Bakım.Models;
using Bakım.Services;
using Bakım.ViewModels;
using Bakım.Views.Dialogs;

namespace Bakım
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// Koruyucu Kod & Hata Yakalama: UI Thread ve arka plan task istisnalarını güvenle yönetir.
    /// Microsoft.Extensions.DependencyInjection ile küresel IoC konteyneri sağlar.
    /// </summary>
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = null!;

        public static T GetService<T>() where T : class
        {
            return Services.GetRequiredService<T>();
        }

        /// <summary>
        /// Konteyner henüz kurulmamışsa (tasarım zamanı, erken başlangıç) null döner.
        /// XAML tarafından örneklenen ViewModel'lerin paylaşılan servislere ulaşması içindir.
        /// </summary>
        public static T? TryGetService<T>() where T : class
        {
            try
            {
                return Services?.GetService<T>();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Yalnızca test içindir: duman testi gerçek konteyneri kurup kabuk
        /// pencerelerini çözebilsin diye sağlayıcıyı enjekte eder.
        /// Üretim yolunda Services OnStartup içinde atanır.
        /// </summary>
        public static void SetServicesForTesting(IServiceProvider provider) => Services = provider;

        private FileLogService? _logService;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Başlangıç kayıt / onarım parametresi (Tek seferlik UAC işçisi)
            if (e.Args != null && e.Args.Length > 0)
            {
                string firstArg = e.Args[0].Trim().ToLowerInvariant();
                if (firstArg == "--register-autostart")
                {
                    bool ok = AdminElevationService.RegisterTaskSchedulerInternal();
                    Shutdown(ok ? 0 : 1);
                    return;
                }
                if (firstArg == "--unregister-autostart")
                {
                    bool ok = AdminElevationService.UnregisterTaskSchedulerInternal();
                    Shutdown(ok ? 0 : 1);
                    return;
                }
                if (firstArg == "--repair-autostart")
                {
                    bool ok = AdminElevationService.RepairAutostartInternal();
                    Shutdown(ok ? 0 : 1);
                    return;
                }
                if (firstArg == "--register-contextmenu")
                {
                    var cms = new ShellContextMenuService();
                    bool ok = cms.RegisterContextMenu();
                    Shutdown(ok ? 0 : 1);
                    return;
                }
                if (firstArg == "--unregister-contextmenu")
                {
                    var cms = new ShellContextMenuService();
                    bool ok = cms.UnregisterContextMenu();
                    Shutdown(ok ? 0 : 1);
                    return;
                }
            }

            // 0. Günlükleme: her şeyden önce ayağa kalkmalı ki başlangıç hataları da kaydedilsin.
            _logService = new FileLogService();
            AppLog.Initialize(_logService);

            var settingsService = new AppSettingsService(_logService);

            _logService.MinimumLevel = settingsService.Current.VerboseLogging
                ? LogLevel.Debug
                : LogLevel.Info;

            _logService.Info(
                $"Bakım başlatılıyor — sürüm {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}, " +
                $"OS {Environment.OSVersion.VersionString}, yönetici: {Helpers.UacHelper.IsAdministrator()}",
                "Startup");

            // 1. DI Konteyner Yapılandırması
            var services = new ServiceCollection();
            ConfigureServices(services, _logService, settingsService);

            // ValidateOnBuild: bir servisin bağımlılığı kayıtlı değilse hata AÇILIŞTA
            // ve tüm eksikleri listeleyerek fırlar. Eskiden eksik kayıt sessizce
            // `?? new X()` yedeğine düşüyor, yanlış (tekil olmayan) örnek kullanılıyordu.
            Services = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

            // 2. UI Thread Beklenmeyen Hata Koruması
            DispatcherUnhandledException += App_DispatcherUnhandledException;

            // 3. Arka Plan İş Parçacıkları (Task) Hata Koruması
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                AppLog.Error("Gözlenmemiş arka plan görev istisnası.", args.Exception, "TaskScheduler");
                args.SetObserved();
            };

            // 4. AppDomain Genel İstisna Yakalayıcı
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    AppLog.Error($"Yakalanmamış alan istisnası (ölümcül: {args.IsTerminating}).", ex, "AppDomain");
                }
            };

            // 5. Tema: kayıtlı tercihi geri yükle (yoksa Mica Koyu)
            ThemeService.Shared.Attach(settingsService, _logService);
            ThemeService.Shared.RestorePersistedTheme();
            Helpers.MotionPolicy.Apply(settingsService.Current.ReduceMotion);

            // 6. Hedef Kaldırma Parametresi Denetimi (--uninstall-target "<path>")
            //    Sağ tık → "Bakım ile Kaldır" yalnızca sihirbazı açan ikinci bir süreçtir. Arka plan
            //    bakımı, Kurulum Nöbetçisi ve sağ tık kaydı bu moda GİRMEDEN önce dönülür (H-16):
            //    eskiden ana uygulama zaten açıkken ikinci bir nöbetçi ve RAM temizleyici başlıyordu.
            string? uninstallTarget = null;
            if (e.Args != null)
            {
                for (int i = 0; i < e.Args.Length; i++)
                {
                    if (e.Args[i].Equals("--uninstall-target", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
                    {
                        uninstallTarget = e.Args[i + 1];
                        break;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(uninstallTarget))
            {
                _logService.Info($"Hedef kaldırma parametresi saptandı: {uninstallTarget}", "Startup");
                _isUninstallTargetMode = true;

                // Bakım zaten açıksa sihirbaz orada açılır (KAL C2); bu süreç hemen kapanır.
                if (SingleInstanceService.IsPrimaryRunning() &&
                    new SingleInstanceService().Send(new IpcRequest(IpcRequestKind.UninstallTarget, uninstallTarget, DateTime.UtcNow), TimeSpan.FromSeconds(3)))
                {
                    _logService.Info("Kaldırma isteği açık Bakım örneğine iletildi.", "Startup");
                    Shutdown(0);
                    return;
                }

                LaunchUninstallTargetMode(uninstallTarget);
                return;
            }

            bool isSilentStart = e.Args != null && Array.Exists(e.Args, arg =>
                arg.Equals("--autostart", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--tray", StringComparison.OrdinalIgnoreCase));

            // Tek örnek: Bakım zaten açıksa pencereyi öne getir ve çık (ikinci nöbetçi/tepsi açılmaz).
            _singleInstance = new SingleInstanceService();
            if (!_singleInstance.TryBecomePrimary())
            {
                bool forwarded = isSilentStart ||
                                 _singleInstance.Send(new IpcRequest(IpcRequestKind.Activate, null, DateTime.UtcNow), TimeSpan.FromSeconds(3));
                if (forwarded)
                {
                    _logService.Info("Bakım zaten çalışıyor; açık örnek öne getirildi, bu süreç kapanıyor.", "Startup");
                    _isSecondaryInstance = true;
                    Shutdown(0);
                    return;
                }
                _logService.Warning("Açık Bakım örneği yanıt vermedi; yeni örnek başlatılıyor.", null, "Startup");
            }

            // 7. Arka plan bakım motoru (otomatik RAM temizliği, yüksek RAM uyarısı)
            GetService<IBackgroundMaintenanceService>().Start();

            // Önceki oturum Oyun Modu açıkken çöktüyse güç planını geri yükle.
            GetService<IGameModeService>().RecoverInterruptedSession();
            GetService<IGameModeService>().StartAutoTrigger();
            // Önceki oturum çöktüyse askıda kalmış süreçleri devam ettir (§5.4).
            SystemCleanService.ResumeAllSuspended();

            // Sentinel Kurulum Nöbetçisi (otomatik kurulum yakalama & analizör taraması)
            var sentinelService = GetService<ISetupSentinelService>();
            sentinelService.SetupDetected += OnSetupDetected;
            sentinelService.SetupFinished += OnSetupFinished;

            // Windows Gezgini Sağ Tık Menüsü ("Bakım ile Kaldır") Varsayılan Entegrasyonu
            if (settingsService.Current.EnableShellContextMenu)
            {
                try
                {
                    var shellContextMenu = GetService<IShellContextMenuService>();
                    if (!shellContextMenu.IsContextMenuRegistered())
                    {
                        shellContextMenu.RegisterContextMenu();
                        _logService.Info("Windows Gezgini sağ tık menüsü varsayılan olarak etkinleştirildi.", "Startup");
                    }
                }
                catch (Exception ex)
                {
                    _logService.Error("Sağ tık menüsü başlangıçta otomatik kaydedilemedi.", ex, "Startup");
                }
            }

            // 8. İlk pencere konteynerden çözülür.
            //    StartupUri kullanılmıyor: o yol pencereyi WPF'in kendisi üretir ve
            //    DI'ı tamamen atlar; ViewModel'ler de XAML'den örneklenmek zorunda kalırdı.
            var mainWindow = GetService<MainWindow>();
            MainWindow = mainWindow;

            if (isSilentStart)
            {
                mainWindow.WindowState = WindowState.Minimized;
                mainWindow.Hide();
                _logService.Info("Sessiz başlangıç parametresi saptandı (--autostart / --tray). Pencere tepsiye gizleniyor.", "Startup");
            }
            else
            {
                mainWindow.Show();
            }

            _singleInstance.Listen(request => Dispatcher.BeginInvoke(() => HandleInstanceRequest(request)));

            _logService.Info("Başlangıç tamamlandı.", "Startup");
        }

        private SingleInstanceService? _singleInstance;
        private static bool _isSecondaryInstance;

        /// <summary>İkinci açılıştan gelen istek (UI iş parçacığında).</summary>
        private async void HandleInstanceRequest(IpcRequest request)
        {
            try
            {
                TryGetService<ITrayIconService>()?.RestoreWindow();
                if (request.Kind != IpcRequestKind.UninstallTarget || string.IsNullOrWhiteSpace(request.Target)) return;

                AppLog.Info($"Sağ tık kaldırma isteği alındı: {request.Target}", "SingleInstance");
                var targetApp = await GetService<IShellUninstallResolverService>().ResolveTargetAppAsync(request.Target);
                if (targetApp == null)
                {
                    MessageBox.Show(
                        $"Bu hedef için kaldırılabilecek bir program bulunamadı ya da hedef korumalı bir Windows bileşeni:\n\n{request.Target}",
                        "Kaldırıcı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var wizardVm = new ViewModels.DeepUninstallWizardViewModel(targetApp,
                    GetService<IDeepUninstallerService>(), GetService<IResidualScannerEngine>());
                var wizardWindow = new Views.Windows.DeepUninstallWizardWindow(wizardVm);
                if (MainWindow?.IsVisible == true) wizardWindow.Owner = MainWindow;
                wizardWindow.Show();
                wizardWindow.Activate();
            }
            catch (Exception ex)
            {
                AppLog.Error("Tek örnek isteği işlenemedi.", ex, "SingleInstance");
            }
        }

        private async void LaunchUninstallTargetMode(string path)
        {
            try
            {
                var resolver = GetService<IShellUninstallResolverService>();
                var targetApp = await resolver.ResolveTargetAppAsync(path);

                if (targetApp == null)
                {
                    MessageBox.Show(
                        $"Bu hedef için kaldırılabilecek bir program bulunamadı ya da hedef korumalı bir Windows bileşeni:\n\n{path}",
                        "Kaldırıcı Hatası",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    Shutdown(1);
                    return;
                }

                var deepUninstaller = GetService<IDeepUninstallerService>();
                var residualScanner = GetService<IResidualScannerEngine>();

                var wizardVm = new ViewModels.DeepUninstallWizardViewModel(targetApp, deepUninstaller, residualScanner);
                var wizardWindow = new Views.Windows.DeepUninstallWizardWindow(wizardVm);

                MainWindow = wizardWindow;
                wizardWindow.Closed += (s, ev) => Shutdown(0);
                wizardWindow.Show();
            }
            catch (Exception ex)
            {
                AppLog.Error("Hedef kaldırma sihirbazı başlatılırken hata oluştu.", ex, "Startup");
                MessageBox.Show($"Kaldırma sihirbazı başlatılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        /// <summary>Sağ tık kaldırma sihirbazı süreci: arka plan servisleri hiç başlatılmaz.</summary>
        private static bool _isUninstallTargetMode;

        protected override void OnExit(ExitEventArgs e)
        {
            if (_isUninstallTargetMode || _isSecondaryInstance)
            {
                // Bu süreçte nöbetçi/bakım/çıkış temizliği yok; TryGetService onları kapanırken
                // oluşturup (nöbetçi yapıcıda başlar) çalıştırırdı.
                AppLog.Info($"{(_isSecondaryInstance ? "İkinci örnek" : "Kaldırma sihirbazı")} kapanıyor (çıkış kodu {e.ApplicationExitCode}).", "Shutdown");
                _logService?.Dispose();
                base.OnExit(e);
                return;
            }

            try
            {
                Bakım.MainWindow.IsExplicitExit = true;
                AppLog.Info($"Uygulama kapanıyor (çıkış kodu {e.ApplicationExitCode}).", "Shutdown");

                TryGetService<IBackgroundMaintenanceService>()?.Stop();
                TryGetService<ISetupSentinelService>()?.Stop();

                // Askıya alınmış süreç bırakılmaz (§5.4).
                SystemCleanService.ResumeAllSuspended();
                TryGetService<ITrayIconService>()?.Detach();
                _singleInstance?.Dispose();

                // Çıkışta otomatik temizlik tercihi
                var settings = TryGetService<IAppSettingsService>();
                if (settings?.Current.AutoCleanOnExit == true)
                {
                    TryGetService<IExitCleanupService>()?.RunExitCleanup();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Kapanış işlemleri sırasında hata.", ex, "Shutdown");
            }
            finally
            {
                _logService?.Dispose();
                base.OnExit(e);
            }
        }

        private SetupDetectedFlyoutWindow? _sentinelFlyoutWindow;

        private void OnSetupDetected(WatchedSetupSession session)
        {
            // Sessiz arka plan izleme: Kurulum başlarken kullanıcıyı rahatsız edecek açılır pencere gösterilmez.
            // Sistem arka planda dosya ve kayıt defteri olaylarını sessizce takip eder.
            AppLog.Info($"Sentinel: Kurulum başlatıldı ve arka planda sessizce izleniyor: {session.AppName} (PID: {session.RootProcessId})", "SetupSentinel");
        }

        private void OnSetupFinished(SetupDeltaReport report)
        {
            // İptal edilmiş kurulumlar veya yeni dosya/yürütülebilir üretmeyen süreçler için pencere açıp kullanıcıyı rahatsız etme.
            if (report.AddedFiles.Count == 0 && report.AddedExecutables.Count == 0)
            {
                AppLog.Info($"Sentinel: {report.AppName} kurulumunda yeni dosya değişikliği saptanmadığından bildirim gösterilmedi.", "SetupSentinel");
                return;
            }

            // Bildirim seviyesi (NÖB 6.2) ve Oyun Modu sırasında sessizlik (NÖB 6.1): rapor yine kaydedilir.
            int level = TryGetService<IAppSettingsService>()?.Current.SentinelNotifyLevel ?? 0;
            bool risky = report.RiskEvaluated && report.RiskVerdict >= Core.Sentinel.RiskVerdict.Caution;
            if (level >= 2 || (level == 1 && !risky))
            {
                AppLog.Info($"Sentinel: {report.AppName} raporu kaydedildi; bildirim ayarı gereği gösterilmedi.", "SetupSentinel");
                return;
            }
            if (TryGetService<IGameModeService>()?.IsGameModeActive == true && !risky)
            {
                AppLog.Info($"Sentinel: Oyun Modu açık; {report.AppName} bildirimi gösterilmedi (rapor Kurulum Nöbetçisi sayfasında).", "SetupSentinel");
                return;
            }

            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (_sentinelFlyoutWindow == null || !_sentinelFlyoutWindow.IsLoaded)
                    {
                        _sentinelFlyoutWindow = new SetupDetectedFlyoutWindow();
                        _sentinelFlyoutWindow.Closed += (s, e) => _sentinelFlyoutWindow = null;
                    }

                    _sentinelFlyoutWindow.SetFinishedReport(report);
                    _sentinelFlyoutWindow.Show();
                }
                catch (Exception ex)
                {
                    AppLog.Error("Kurulum tamamlandı bildirim penceresi açılamadı.", ex, "App");
                }
            });
        }

        /// <summary>
        /// Konteyner yapılandırması. Test projesi de bu metodu çağırarak
        /// gerçek kayıt grafiğini doğrular — kopyalanmış bir liste üzerinde değil.
        /// </summary>
        public static void ConfigureServices(
            IServiceCollection services,
            ILogService logService,
            IAppSettingsService settingsService)
        {
            // Çapraz Kesit Servisleri (önceden örneklenmiş tekil nesneler)
            services.AddSingleton(logService);
            services.AddSingleton(settingsService);

            // Tema: statik Shared ile aynı örnek kaydedilir, böylece iki farklı
            // tema durumu oluşması yapısal olarak imkânsız hale gelir.
            services.AddSingleton<IThemeService>(_ => ThemeService.Shared);

            // Güvenlik çekirdeği: silme, kayıt defteri, süreç ve geri yükleme noktası
            // işlemlerinin TEK yolu. Yıkıcı işlem yapan her servis bunları kullanır.
            services.AddSingleton(_ => Bakım.Core.Safety.PathSafetyGuard.Default);
            services.AddSingleton<Bakım.Services.Safety.ISafeDeleteService, Bakım.Services.Safety.SafeDeleteService>();
            services.AddSingleton<Bakım.Services.Safety.ISafeRegistryService, Bakım.Services.Safety.SafeRegistryService>();
            services.AddSingleton<Bakım.Services.Safety.ISafeProcessService, Bakım.Services.Safety.SafeProcessService>();
            services.AddSingleton<Bakım.Services.Safety.IRestorePointService, Bakım.Services.Safety.RestorePointService>();

            // Backend Sistem Servisleri (Singleton)
            services.AddSingleton<ISystemCleanService, SystemCleanService>();
            services.AddSingleton<ISystemInfoService, SystemInfoService>();
            services.AddSingleton<IStartupService, StartupService>();
            services.AddSingleton<INetworkMonitorService, NetworkMonitorService>();
            services.AddSingleton<IServiceManagerService, ServiceManagerService>();
            services.AddSingleton<IPrivacyDebloatService, PrivacyDebloatService>();
            services.AddSingleton<ICrashAnalyzerService, CrashAnalyzerService>();
            services.AddSingleton<IUninstallerService, UninstallerService>();
            services.AddSingleton<IResidualScannerEngine, ResidualScannerEngine>();
            services.AddSingleton<Services.Uninstall.IFootprintCollector, Services.Uninstall.FootprintCollector>();
            services.AddSingleton<IInstallerMonitorService, InstallerMonitorService>();
            services.AddSingleton<IHunterService, HunterService>();
            services.AddSingleton<IDeepUninstallerService, DeepUninstallerService>();
            services.AddSingleton<IBehaviorTweaksService, BehaviorTweaksService>();
            services.AddSingleton<IBootLogonTweaksService, BootLogonTweaksService>();
            services.AddSingleton<IDesktopTaskbarTweaksService, DesktopTaskbarTweaksService>();
            services.AddSingleton<IContextMenuShortcutsService, ContextMenuShortcutsService>();
            services.AddSingleton<IShellContextMenuService, ShellContextMenuService>();
            services.AddSingleton<IShellUninstallResolverService, ShellUninstallResolverService>();
            services.AddSingleton<ISystemToolsService, SystemToolsService>();
            services.AddSingleton<IClassicAppsService, ClassicAppsService>();
            services.AddSingleton<IWindows11TweaksService, Windows11TweaksService>();
            services.AddSingleton<IAppearanceTweaksService, AppearanceTweaksService>();
            services.AddSingleton<IAdvancedAppearanceService, AdvancedAppearanceService>();
            services.AddSingleton<ITweaksSnapshotService, TweaksSnapshotService>();
            services.AddSingleton<IEdgeTweaksService, EdgeTweaksService>();
            services.AddSingleton<ISettingsControlPanelTweaksService, SettingsControlPanelTweaksService>();
            services.AddSingleton<IFileExplorerTweaksService, FileExplorerTweaksService>();
            services.AddSingleton<IAuthService, AuthService>();
            services.AddSingleton<IVirusTotalCheckService, VirusTotalCheckService>();
            services.AddSingleton<IAutorunsScannerEngine, AutorunsScannerEngine>();
            services.AddSingleton<Bakım.Services.Sentinel.Storage.ISessionStore, Bakım.Services.Sentinel.Storage.SessionStore>();
            services.AddSingleton<Bakım.Services.Sentinel.IWindowsSandboxService, Bakım.Services.Sentinel.WindowsSandboxService>();
            services.AddSingleton<ISetupSentinelService, SetupSentinelService>();

            // v3.1'de eklenen kayıtlar: bu servisler daha önce hiç kayıtlı değildi,
            // bu yüzden ViewModel'ler onları elle `new` ile üretmek zorunda kalıyordu.
            services.AddSingleton<ITelemetryService, TelemetryService>();
            services.AddSingleton<ICommandPaletteService, CommandPaletteService>();
            // Analizör Geçmişi (§6): analiz servisi kaydeden bir dekoratörle sarılır; hangi modül
            // analiz ederse etsin sonuç geçmişe yazılır.
            services.AddSingleton<Bakım.Services.History.IAnalysisHistoryService, Bakım.Services.History.AnalysisHistoryService>();
            services.AddSingleton<FileThreatAnalyzerService>();
            services.AddSingleton<IFileThreatAnalyzerService>(sp => new Bakım.Services.History.RecordingFileThreatAnalyzer(
                sp.GetRequiredService<FileThreatAnalyzerService>(),
                sp.GetRequiredService<Bakım.Services.History.IAnalysisHistoryService>(),
                sp.GetRequiredService<ILogService>()));
            // Etkinlik Merkezi (§7): her değişikliğin kaydı ve geri alma işleyicileri.
            services.AddSingleton<Bakım.Services.Activity.IUndoHandler>(_ => new Bakım.Services.Activity.RegistryValuesUndoHandler());
            services.AddSingleton<Bakım.Services.Activity.IUndoHandler>(_ => new Bakım.Services.Activity.RegistryValuesUndoHandler(Bakım.Core.Activity.UndoHandlers.StartupApproved));
            services.AddSingleton<Bakım.Services.Activity.IUndoHandler, Bakım.Services.Activity.RegImportUndoHandler>();
            services.AddSingleton<Bakım.Services.Activity.IUndoHandler, Bakım.Services.Activity.ServiceConfigUndoHandler>();
            services.AddSingleton<Bakım.Services.Activity.IUndoHandler, Bakım.Services.Activity.FirewallRuleUndoHandler>();
            services.AddSingleton<Bakım.Services.Activity.IUndoHandler, Bakım.Services.Activity.RecycleBinUndoHandler>();
            services.AddSingleton<Bakım.Services.Activity.IUndoHandler, Bakım.Services.Sentinel.Actions.RootCertificateUndoHandler>();
            services.AddSingleton<Bakım.Services.Activity.IActivityService>(sp =>
                new Bakım.Services.Activity.ActivityService(sp.GetServices<Bakım.Services.Activity.IUndoHandler>()));
            services.AddSingleton<IHealthSignalsService, HealthSignalsService>();
            services.AddSingleton<IDiskMapService, DiskMapService>();
            services.AddSingleton<IQuickMaintenanceService, QuickMaintenanceService>();
            services.AddSingleton<IStoreService, StoreService>();
            services.AddSingleton<IDuplicateFinderService, DuplicateFinderService>();
            services.AddSingleton<INavigationService>(_ => NavigationService.Instance);

            // Otomasyon & Yaşam Döngüsü Servisleri
            services.AddSingleton<IBackgroundMaintenanceService, BackgroundMaintenanceService>();
            services.AddSingleton<IGameModeService, GameModeService>();
            services.AddSingleton<IGameLibraryService, GameLibraryService>();
            services.AddSingleton<IExitCleanupService, ExitCleanupService>();
            services.AddSingleton<ITrayIconService, TrayIconService>();

            // ViewModels (Transient)
            services.AddTransient<MainViewModel>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<CleanerViewModel>();
            services.AddTransient<OptimizerViewModel>();
            services.AddTransient<StartupViewModel>();
            services.AddTransient<SystemInfoViewModel>();
            services.AddTransient<NetworkMonitorViewModel>();
            services.AddTransient<ServiceManagerViewModel>();
            services.AddTransient<PrivacyDebloatViewModel>();
            services.AddTransient<CrashAnalyzerViewModel>();
            services.AddTransient<UninstallerViewModel>();
            // Tekil: Kurulum Nöbetçisi "Analizörle tara" dediğinde ekrandaki örneğe dosya eklemeli.
            // Transient iken her çağrı görünmeyen yeni bir örnek (ve yeni bir tam tarama) üretiyordu.
            services.AddSingleton<AnalyzerViewModel>();
            services.AddSingleton<AnalyzerHistoryViewModel>();
            services.AddSingleton<ActivityCenterViewModel>();
            // Faz 4 IA: yeni sayfalar. Depolama tekil: Sistem Bilgisi'nden tarama başlatılabilir.
            services.AddSingleton<StorageViewModel>();
            services.AddTransient<WindowsToolsViewModel>();
            services.AddSingleton<GameModeViewModel>();
            services.AddSingleton<SentinelViewModel>();
            services.AddTransient<WindowsTweakerViewModel>();
            services.AddTransient<TweakerCategoriesViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<StoreViewModel>();
            services.AddTransient<LoginViewModel>();

            // Windows
            services.AddTransient<MainWindow>();
            services.AddTransient<LoginWindow>();
        }

        private static DateTime _lastExceptionTime = DateTime.MinValue;
        private static string _lastExceptionMessage = string.Empty;

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Çöküşü engelle
            e.Handled = true;

            // Kök Neden Tespiti ve Kullanıcı Bilgilendirmesi
            string rootCause = e.Exception.InnerException?.Message ?? e.Exception.Message;
            var now = DateTime.UtcNow;

            // Geçici WPF DWM / HwndTarget pencere ayarı / konumu güncelleme istisnaları (ERROR_NOT_ENOUGH_QUOTA vb.):
            // e.Handled = true zaten sürecin çökmesini engeller ve WPF sonraki karede toparlar.
            // Bu geçici iç WPF çizim hataları için kullanıcıya engelleyici modal MessageBox açılmamalıdır.
            string stackTrace = e.Exception.StackTrace ?? string.Empty;
            if (e.Exception is System.ComponentModel.Win32Exception ||
                e.Exception.InnerException is System.ComponentModel.Win32Exception ||
                stackTrace.Contains("HwndTarget.UpdateWindowSettings") ||
                stackTrace.Contains("HwndTarget.UpdateWindowPos"))
            {
                AppLog.Warning($"Geçici arayüz pencere render uyarısı bastırıldı: {rootCause}", e.Exception, "Dispatcher");
                return;
            }

            AppLog.Error("Yakalanmamış arayüz istisnası.", e.Exception, "Dispatcher");

            // 2 saniye throttling / de-duplication: Aynı hatanın üst üste popup açmasını engelle
            if (string.Equals(_lastExceptionMessage, rootCause, StringComparison.Ordinal) &&
                (now - _lastExceptionTime).TotalSeconds < 2.0)
            {
                return;
            }

            _lastExceptionTime = now;
            _lastExceptionMessage = rootCause;

            MessageBox.Show(
                $"Beklenmeyen bir hata oluştu fakat uygulama güvenle çalışmaya devam ediyor:\n\n{rootCause}\n\n" +
                $"Teknik ayrıntılar günlüğe kaydedildi:\n{AppLog.Current.LogDirectory}",
                "Bakım Sistemi - Koruyucu Hata Yakalama",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
