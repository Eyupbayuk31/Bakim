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

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 0. DI Konteyner Yapılandırması
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            // 1. UI Thread Beklenmeyen Hata Koruması
            DispatcherUnhandledException += App_DispatcherUnhandledException;

            // 2. Arka Plan İş Parçacıkları (Task) Hata Koruması
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                args.SetObserved();
            };

            // 3. AppDomain Genel İstisna Yakalayıcı
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CRITICAL] Unhandled Domain Exception: {ex.Message}");
                }
            };

            // Koyu Slate Fluent temasını başlangıçta kararlı şekilde uygula
            ThemeManager.ApplyTheme(true);
        }

        private static void ConfigureServices(IServiceCollection services)
        {
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
            services.AddTransient<WindowsTweakerViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<LoginViewModel>();

            // Windows
            services.AddTransient<MainWindow>();
            services.AddTransient<LoginWindow>();
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Kök Neden Tespiti ve Kullanıcı Bilgilendirmesi
            string rootCause = e.Exception.InnerException?.Message ?? e.Exception.Message;
            MessageBox.Show(
                $"Beklenmeyen bir hata oluştu fakat uygulama güvenle çalışmaya devam ediyor:\n\n{rootCause}",
                "Bakım Sistemi - Koruyucu Hata Yakalama",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            // Çöküşü engelle
            e.Handled = true;
        }
    }
}
