using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Bakım.Controls;
using Bakım.Services;
using Bakım.Views.Modules;

// XAML DUMAN TESTİ
// ================
// Eksik bir kaynak anahtarı WPF'te İSTİSNA FIRLATMAZ — öğe sessizce görünmez olur.
// Derleme yeşil olsa bile arayüz bozulmuş olabilir. Bu test:
//   1. Üretimdeki kaynak grafiğinin birebir aynısını yükler
//   2. Her görünümü oluşturup düzen geçişini zorlar (şablonlar uygulanır)
//   3. WPF'in kendi izleme kanalından kaynak çözümleme hatalarını toplar
//   4. Her temanın eksiksiz uygulandığını doğrular

namespace Bakim.UiSmokeTests;

internal static class Program
{
    /// <summary>
    /// pack://application:,,,/ kök URI'si GİRİŞ derlemesini işaret eder.
    /// Duman testi ayrı bir exe olduğu için varsayılan olarak bu test projesine
    /// bakar ve Assets/app.ico'yu bulamaz.
    ///
    /// ResourceAssembly ilk OKUNDUĞUNDA kilitlenir, bu yüzden atama Main'den de
    /// önce — modül başlatıcıda — yapılmalıdır. Üretimde giriş derlemesi zaten
    /// Bakım olduğundan bu ayar gerekmez; yalnızca koşucuyu üretimle hizalar.
    /// </summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void PinResourceAssembly()
    {
        var target = typeof(Bakım.App).Assembly;
        try
        {
            // Bakım derlemesi yüklenirken WPF bunu kendisi ayarlamış olabilir;
            // o durumda zaten doğru değerdedir ve tekrar atama istisna fırlatır.
            if (!ReferenceEquals(Application.ResourceAssembly, target))
            {
                Application.ResourceAssembly = target;
            }
        }
        catch (InvalidOperationException)
        {
            // Kilitlenmiş; aşağıdaki doğrulama gerçek değeri bildirir.
        }
    }

    private static readonly List<string> ResourceErrors = new();
    private static readonly List<string> Failures = new();

    [STAThread]
    private static int Main()
    {
        Console.WriteLine($"Kaynak derlemesi: {Application.ResourceAssembly?.GetName().Name ?? "(yok)"}");
        Console.WriteLine();

        var listener = new CollectingListener();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.ResourceDictionarySource.Listeners.Add(listener);
        PresentationTraceSources.ResourceDictionarySource.Switch.Level = SourceLevels.Warning;
        PresentationTraceSources.MarkupSource.Listeners.Add(listener);
        PresentationTraceSources.MarkupSource.Switch.Level = SourceLevels.Warning;

        // App.InitializeComponent() App.xaml'i yükler: WPF-UI sözlükleri, token
        // sözlükleri ve dönüştürücüler. Run() çağrılmadığı için OnStartup çalışmaz —
        // servisler ayağa kalkmaz, pencere açılmaz, kullanıcı ayarları değişmez.
        var app = new Bakım.App();
        app.InitializeComponent();

        ThemeService.Shared.ApplyTheme(AppThemeKind.MicaDark, persist: false);

        RunViewChecks();
        RunThemeChecks(app);
        RunContainerChecks();

        Console.WriteLine();

        if (ResourceErrors.Count > 0)
        {
            Console.WriteLine($"KAYNAK HATALARI ({ResourceErrors.Count}):");
            foreach (var e in ResourceErrors.Distinct().Take(40))
                Console.WriteLine($"  {e}");
            Console.WriteLine();
        }

        bool clean = ResourceErrors.Count == 0 && Failures.Count == 0;
        Console.WriteLine(clean
            ? "SONUC: TUM KONTROLLER GECTI"
            : $"SONUC: {Failures.Count} basarisizlik, {ResourceErrors.Count} kaynak hatasi");

        return clean ? 0 : 1;
    }

    /// <summary>
    /// Gerçek DI konteynerini kurup kabuk penceresini çözer.
    /// Bu, uygulamanın açılış yolunun tamamını (konteyner -> MainViewModel ->
    /// lazy modüller -> pencere) test altına alan tek kontroldür.
    /// </summary>
    private static void RunContainerChecks()
    {
        Console.WriteLine();
        Console.WriteLine("BAGIMLILIK KONTEYNERI");
        Console.WriteLine("=====================");

        try
        {
            var collection = new ServiceCollection();
            Bakım.App.ConfigureServices(collection, NullLogService.Instance, new AppSettingsService(NullLogService.Instance));

            var provider = collection.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

            Console.WriteLine("  GECTI  | Konteyner dogrulanarak kuruldu");

            // Uygulamanin gercek acilis yolu: konteyner pencereyi uretebilmeli.
            Bakım.App.SetServicesForTesting(provider);

            var login = provider.GetRequiredService<Bakım.LoginWindow>();
            Console.WriteLine($"  GECTI  | LoginWindow cozuldu (DataContext: {login.DataContext?.GetType().Name})");
            login.Close();

            var main = provider.GetRequiredService<Bakım.MainWindow>();
            Console.WriteLine($"  GECTI  | MainWindow cozuldu (DataContext: {main.DataContext?.GetType().Name})");

            var mainVm = (Bakım.ViewModels.MainViewModel)main.DataContext!;
            _ = mainVm.Dashboard;
            _ = mainVm.Cleaner;
            _ = mainVm.Optimizer;
            _ = mainVm.Startup;
            _ = mainVm.SystemInfo;
            _ = mainVm.NetworkMonitor;
            _ = mainVm.ServiceManager;
            _ = mainVm.PrivacyDebloat;
            _ = mainVm.CrashAnalyzer;
            _ = mainVm.Uninstaller;
            _ = mainVm.Autoruns;
            _ = mainVm.WindowsTweaker;
            _ = mainVm.TweakerCategories;
            _ = mainVm.Settings;
            Console.WriteLine("  GECTI  | MainViewModel 14 alt modulu (TweakerCategories dahil) basariyla cozuldu");

            main.Close();

            provider.Dispose();
        }
        catch (Exception ex)
        {
            Failures.Add("konteyner");
            Console.WriteLine($"  COKTU  | {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"           ic istisna: {ex.InnerException.Message}");
        }
    }

    private static void RunViewChecks()
    {
        var views = new (string Name, Func<FrameworkElement> Create)[]
        {
            ("DashboardModuleView",      () => new DashboardModuleView()),
            ("CleanerModuleView",        () => new CleanerModuleView()),
            ("OptimizerModuleView",      () => new OptimizerModuleView()),
            ("StartupModuleView",        () => new StartupModuleView()),
            ("SystemInfoModuleView",     () => new SystemInfoModuleView()),
            ("NetworkMonitorModuleView", () => new NetworkMonitorModuleView()),
            ("ServiceManagerModuleView", () => new ServiceManagerModuleView()),
            ("PrivacyDebloatModuleView", () => new PrivacyDebloatModuleView()),
            ("CrashAnalyzerModuleView",  () => new CrashAnalyzerModuleView()),
            ("UninstallerModuleView",    () => new UninstallerModuleView()),
            ("AutorunsPersistenceView",  () => new AutorunsPersistenceView()),
            ("WindowsTweakerModuleView", () => new WindowsTweakerModuleView()),
            ("SettingsModuleView",       () => new SettingsModuleView()),

            ("Controls/SectionHeader",   () => new SectionHeader { Title = "Başlık", Description = "Açıklama", Icon = "Shield24" }),
            ("Controls/MetricChip",      () => new MetricChip { Icon = "TopSpeed24", Label = "RAM:", Value = "%58", Intent = Intent.Accent }),
            ("Controls/StatCard",        () => new StatCard { Icon = "HardDrive24", Label = "Disk", Value = "%72", Caption = "340 GB / 476 GB", Intent = Intent.Caution }),
            ("Controls/StatusBadge",     () => new StatusBadge { Text = "Güvenli", Intent = Intent.Success }),
            ("Controls/EmptyState",      () => new EmptyState { Icon = "Search24", Title = "Sonuç yok", Description = "Tarama yapılmadı." }),
        };

        Console.WriteLine("GORUNUM YUKLEME");
        Console.WriteLine("===============");

        foreach (var (name, create) in views)
        {
            int before = ResourceErrors.Count;
            try
            {
                var host = new Window
                {
                    Width = 1280,
                    Height = 840,
                    ShowActivated = false,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    Content = create()
                };

                // Düzen geçişi: şablonlar uygulanır, tüm kaynaklar çözümlenir
                host.Measure(new Size(1280, 840));
                host.Arrange(new Rect(0, 0, 1280, 840));
                host.UpdateLayout();

                // Dar pencere: responsive düzenin çökmediğini doğrula
                host.Measure(new Size(1020, 680));
                host.Arrange(new Rect(0, 0, 1020, 680));
                host.UpdateLayout();

                host.Content = null;
                host.Close();

                int added = ResourceErrors.Count - before;
                if (added == 0)
                {
                    Console.WriteLine($"  GECTI  | {name}");
                }
                else
                {
                    Console.WriteLine($"  KALDI  | {name}  ({added} kaynak hatasi)");
                    Failures.Add(name);
                }
            }
            catch (Exception ex)
            {
                Failures.Add(name);
                Console.WriteLine($"  COKTU  | {name}  -> {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static void RunThemeChecks(Application app)
    {
        // Tema değiştirmek Application.Resources üzerine yeniden yazar.
        // Her temanın gerçekten yazıldığını ve hata üretmediğini kanıtla.
        string[] probes =
        {
            "ApplicationBackgroundBrush", "CardBackgroundFillColorDefaultBrush",
            "TextFillColorPrimaryBrush", "AccentTextFillColorPrimaryBrush",
            "SystemFillColorCriticalBrush", "Brush.Critical.Subtle",
            "Brush.Overlay.Scrim", "Color.Accent.Transparent", "Color.Shadow",
        };

        Console.WriteLine();
        Console.WriteLine("TEMA GECISI");
        Console.WriteLine("===========");

        foreach (AppThemeKind kind in Enum.GetValues<AppThemeKind>())
        {
            int before = ResourceErrors.Count;
            try
            {
                ThemeService.Shared.ApplyTheme(kind, persist: false);

                var def = ThemeService.GetDefinition(kind);
                var missing = probes.Where(k => app.Resources[k] == null).ToList();
                var bg = (System.Windows.Media.SolidColorBrush)app.Resources["ApplicationBackgroundBrush"]!;
                bool matches = bg.Color == def.WindowBackground;

                if (missing.Count == 0 && matches && ResourceErrors.Count == before)
                {
                    Console.WriteLine($"  GECTI  | {def.DisplayName,-26} arkaplan {bg.Color}");
                }
                else
                {
                    Console.WriteLine($"  KALDI  | {def.DisplayName}  eksik=[{string.Join(",", missing)}] eslesme={matches}");
                    Failures.Add($"tema:{kind}");
                }
            }
            catch (Exception ex)
            {
                Failures.Add($"tema:{kind}");
                Console.WriteLine($"  COKTU  | {kind} -> {ex.Message}");
            }
        }
    }

    private sealed class CollectingListener : TraceListener
    {
        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            if (message.Contains("Cannot find resource", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                ResourceErrors.Add(message.Trim());
            }
        }
    }
}
