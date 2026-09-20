using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Bakım.Services;
using Bakım.ViewModels;

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

            // 6. Arka plan bakım motoru (otomatik RAM temizliği, yüksek RAM uyarısı)
            GetService<IBackgroundMaintenanceService>().Start();

            // 7. İlk pencere konteynerden çözülür.
            //    StartupUri kullanılmıyor: o yol pencereyi WPF'in kendisi üretir ve
            //    DI'ı tamamen atlar; ViewModel'ler de XAML'den örneklenmek zorunda kalırdı.
            var mainWindow = GetService<MainWindow>();
            MainWindow = mainWindow;

            bool isSilentStart = false;
            foreach (var arg in e.Args)
            {
                if (arg.Equals("--autostart", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--tray", StringComparison.OrdinalIgnoreCase))
                {
                    isSilentStart = true;
                    break;
                }
            }

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

            _logService.Info("Başlangıç tamamlandı.", "Startup");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                Bakım.MainWindow.IsExplicitExit = true;
                AppLog.Info($"Uygulama kapanıyor (çıkış kodu {e.ApplicationExitCode}).", "Shutdown");

                TryGetService<IBackgroundMaintenanceService>()?.Stop();
                TryGetService<ITrayIconService>()?.Detach();

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
            services.AddSingleton<IInstallerMonitorService, InstallerMonitorService>();
            services.AddSingleton<IHunterService, HunterService>();
            services.AddSingleton<IDeepUninstallerService, DeepUninstallerService>();
            services.AddSingleton<IBehaviorTweaksService, BehaviorTweaksService>();
            services.AddSingleton<IBootLogonTweaksService, BootLogonTweaksService>();
            services.AddSingleton<IDesktopTaskbarTweaksService, DesktopTaskbarTweaksService>();
            services.AddSingleton<IContextMenuShortcutsService, ContextMenuShortcutsService>();
            services.AddSingleton<ISystemToolsService, SystemToolsService>();
            services.AddSingleton<IClassicAppsService, ClassicAppsService>();
            services.AddSingleton<IWindows11TweaksService, Windows11TweaksService>();
            services.AddSingleton<IAppearanceTweaksService, AppearanceTweaksService>();
            services.AddSingleton<IAdvancedAppearanceService, AdvancedAppearanceService>();
            services.AddSingleton<ITweaksSnapshotService, TweaksSnapshotService>();
            services.AddSingleton<IEdgeTweaksService, EdgeTweaksService>();
            services.AddSingleton<ISettingsControlPanelTweaksService, SettingsControlPanelTweaksService>();
            services.AddSingleton<IFileExplorerTweaksService, FileExplorerTweaksService>();
            services.AddSingleton<IGitHubUpdateService, GitHubUpdateService>();
            services.AddSingleton<IAuthService, AuthService>();
            services.AddSingleton<IVirusTotalCheckService, VirusTotalCheckService>();
            services.AddSingleton<IAutorunsScannerEngine, AutorunsScannerEngine>();

            // v3.1'de eklenen kayıtlar: bu servisler daha önce hiç kayıtlı değildi,
            // bu yüzden ViewModel'ler onları elle `new` ile üretmek zorunda kalıyordu.
            services.AddSingleton<ITelemetryService, TelemetryService>();
            services.AddSingleton<ICommandPaletteService, CommandPaletteService>();
            services.AddSingleton<IFileThreatAnalyzerService, FileThreatAnalyzerService>();
            services.AddSingleton<INavigationService>(_ => NavigationService.Instance);

            // Otomasyon & Yaşam Döngüsü Servisleri
            services.AddSingleton<IBackgroundMaintenanceService, BackgroundMaintenanceService>();
            services.AddSingleton<IGameModeService, GameModeService>();
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
            services.AddTransient<AutorunsViewModel>();
            services.AddTransient<WindowsTweakerViewModel>();
            services.AddTransient<TweakerCategoriesViewModel>();
            services.AddTransient<SettingsViewModel>();
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
