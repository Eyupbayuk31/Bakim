using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Bakım.Controls;
using Bakım.Models;
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
        RunDialogChecks();

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
            _ = mainVm.Analyzer;
            _ = mainVm.WindowsTweaker;
            _ = mainVm.TweakerCategories;
            _ = mainVm.Settings;
            _ = mainVm.Store;
            Console.WriteLine("  GECTI  | MainViewModel 15 alt modulu (TweakerCategories ve Store dahil) basariyla cozuldu");

            main.Close();

            RunTweakerNavigationChecks(provider);
            RunStoreTabChecks(provider);

            // NOT: provider bilerek dispose EDILMEZ — sonraki diyalog kontrolu
            // App.Services uzerinden ayni konteyneri kullanir.
        }
        catch (Exception ex)
        {
            Failures.Add("konteyner");
            Console.WriteLine($"  COKTU  | {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"           ic istisna: {ex.InnerException.Message}");
        }
    }

    /// <summary>
    /// Diyalog pencerelerinin gerçekten yüklendiğini ve TÜM SEKMELERİNİN
    /// render olduğunu doğrular. Sekmeler Visibility ile gizlendiği için
    /// yalnızca açılışta görüneni test etmek yetmez — gizli sekmedeki bozuk
    /// bir binding ya da eksik kaynak fark edilmeden kalırdı.
    /// </summary>
    private static void RunDialogChecks()
    {
        Console.WriteLine();
        Console.WriteLine("DIYALOG PENCERELERI");
        Console.WriteLine("===================");

        try
        {
            var sample = BuildSampleAnalysis();

            var dialog = new Bakım.Views.Dialogs.ThreatAnalysisDialog(
                sample,
                Bakım.App.GetService<IFileThreatAnalyzerService>(),
                null,
                Bakım.App.GetService<IVirusTotalCheckService>());

            dialog.ShowActivated = false;
            dialog.ShowInTaskbar = false;

            // Dört sekmenin her birini etkinleştirip düzen geçişini zorla
            string[] tabNames = { "Genel Bakis", "PE & Kalkanlar", "API Cagrilari", "Kimlik & Hash" };

            for (int i = 0; i < 4; i++)
            {
                int before = ResourceErrors.Count;

                dialog.ViewModel.SelectedTabIndex = i;

                dialog.Measure(new Size(940, 780));
                dialog.Arrange(new Rect(0, 0, 940, 780));
                dialog.UpdateLayout();

                // Dar pencere: eylem cubugu tasmiyor mu
                dialog.Measure(new Size(820, 640));
                dialog.Arrange(new Rect(0, 0, 820, 640));
                dialog.UpdateLayout();

                int added = ResourceErrors.Count - before;
                if (added == 0)
                {
                    Console.WriteLine($"  GECTI  | ThreatAnalysisDialog / sekme {i} ({tabNames[i]})");
                }
                else
                {
                    Console.WriteLine($"  KALDI  | sekme {i} ({tabNames[i]}) — {added} kaynak hatasi");
                    Failures.Add($"dialog-tab-{i}");
                }
            }

            dialog.Close();
        }
        catch (Exception ex)
        {
            Failures.Add("ThreatAnalysisDialog");
            Console.WriteLine($"  COKTU  | ThreatAnalysisDialog -> {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"           ic istisna: {ex.InnerException.Message}");
        }
    }

    /// <summary>Dört sekmenin de dolu görüneceği sentetik analiz sonucu.</summary>
    private static ThreatAnalysisResult BuildSampleAnalysis()
    {
        return new ThreatAnalysisResult
        {
            FileName = "ornek.exe",
            FilePath = @"C:\Program Files\Ornek\ornek.exe",
            FileSizeFormatted = "5,51 MB",
            FileSizeBytes = 5_777_408,
            Sha256 = new string('a', 64),
            Md5 = new string('b', 32),
            Sha1 = new string('c', 40),
            ImpHash = new string('d', 32),
            EntropyScore = 6.82,
            EntropyText = "Normal Entropi (6,82 / 8.0)",
            RiskScore = 45,
            IsSigned = true,
            SignerName = "Ornek Yazilim A.S.",
            DigitalSignatureText = "Geçerli (Ornek Yazilim A.S.)",
            IsCatalogSigned = true,
            SignatureCatalogPath = @"C:\Windows\System32\CatRoot\ornek.cat",
            CertificateExpiry = DateTime.Now.AddYears(1),
            ProductName = "Ornek Urun",
            FileVersion = "1.2.3.4",
            HasMarkOfTheWeb = true,
            ZoneSourceUrl = "https://ornek.example/indir/ornek.exe",
            VirusTotalSummary = "3/70 Supheli",
            VirusTotalMalicious = 3,
            VirusTotalTotal = 70,
            IsActiveProcess = true,
            ActiveProcessId = 8152,
            ActiveProcessMemory = "128 MB",
            Recommendation = "Dosya imzali ancak internetten indirilmis. Kaynagini dogrulayin.",
            PeHeader = new PeHeaderInfo
            {
                IsPeFile = true,
                MachineArchitecture = "x64 (AMD64)",
                Subsystem = "Windows GUI",
                CompileTimeUtc = "2024-03-12 14:22:01 UTC",
                EntryPointRva = 0x1234,
                ImageBase = 0x140000000,
                SectionCount = 3,
                Is64Bit = true
            },
            Mitigations = new ExploitMitigationMatrix
            {
                HasAslr = true, HasDep = true, HasCfg = false,
                HasHighEntropyVa = true, HasSafeSeh = false, IsDotNet = false
            },
            Sections =
            {
                new PeSectionItem { Name = ".text", VirtualAddress = 0x1000, VirtualSize = 0x5000,
                                    RawSize = 0x5000, Entropy = 6.1, IsExecutable = true },
                new PeSectionItem { Name = ".rdata", VirtualAddress = 0x7000, VirtualSize = 0x2000,
                                    RawSize = 0x2000, Entropy = 4.9 },
                new PeSectionItem { Name = ".packed", VirtualAddress = 0x9000, VirtualSize = 0x9000,
                                    RawSize = 0x9000, Entropy = 7.8, IsExecutable = true,
                                    IsWritable = true, IsSuspiciousPacker = true },
            },
            ImportedDlls =
            {
                new ImportedDllGroup
                {
                    DllName = "kernel32.dll",
                    Functions =
                    {
                        new ImportedApiFunction { Name = "VirtualAllocEx", IsSuspicious = true,
                            Category = "Bellek Enjeksiyonu", Description = "Baska surecte bellek ayirir." },
                        new ImportedApiFunction { Name = "CreateFileW", Description = "Dosya acar." },
                    }
                },
                new ImportedDllGroup
                {
                    DllName = "ws2_32.dll",
                    Functions =
                    {
                        new ImportedApiFunction { Name = "connect", IsSuspicious = true,
                            Category = "Ağ / C2 İletişimi", Description = "Uzak sunucuya baglanir." },
                    }
                },
            },
            Factors =
            {
                new ThreatFactor { Title = "Güvenilir Yayıncı İmzası", Severity = ThreatSeverity.Clean,
                                   Description = "Dosya meşru bir üretici tarafından imzalanmış.", ScoreImpact = 0 },
                new ThreatFactor { Title = "İnternetten İndirilmiş", Severity = ThreatSeverity.Warning,
                                   Description = "Mark-of-the-Web işareti taşıyor.", ScoreImpact = 15 },
                new ThreatFactor { Title = "Yüksek Entropili Bölüm", Severity = ThreatSeverity.Critical,
                                   Description = ".packed bölümü sıkıştırılmış/şifrelenmiş olabilir.", ScoreImpact = 30 },
                new ThreatFactor { Title = "Bilgi", Severity = ThreatSeverity.Info,
                                   Description = "Ek bilgi satırı.", ScoreImpact = 0 },
            }
        };
    }

    /// <summary>
    /// Tweaker kategori gezinmesi. Günlükte "Tweaker kategorisine geçiş
    /// sırasında hata oluştu: All" satırı görüldü; hata try/catch içinde
    /// yutuluyordu. Bu kontrol her kategori anahtarını gerçek konteyner
    /// üzerinden deneyip istisnayı görünür kılar.
    /// </summary>
    private static void RunTweakerNavigationChecks(IServiceProvider provider)
    {
        Console.WriteLine();
        Console.WriteLine("TWEAKER KATEGORI GEZINMESI");
        Console.WriteLine("==========================");

        string[] keys =
        {
            "All", "Appearance", "Behavior", "BootLogon",
            "ContextMenu", "DesktopTaskbar", "Edge", "FileExplorer",
            "SettingsCpl", "Tools", "AdvancedAppearance", "PrivacyDebloat",
        };

        try
        {
            var main = provider.GetRequiredService<Bakım.ViewModels.MainViewModel>();

            foreach (var key in keys)
            {
                try
                {
                    main.NavigateToTweakerCategory(key);
                    Console.WriteLine($"  GECTI  | {key}");
                }
                catch (Exception ex)
                {
                    Failures.Add($"tweaker:{key}");
                    Console.WriteLine($"  COKTU  | {key} -> {ex.GetType().Name}: {ex.Message}");
                    if (ex.InnerException != null)
                        Console.WriteLine($"           ic: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Failures.Add("tweaker-nav");
            Console.WriteLine($"  COKTU  | MainViewModel cozulemedi -> {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"           ic: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        }
    }

    private static void RunStoreTabChecks(IServiceProvider provider)
    {
        Console.WriteLine();
        Console.WriteLine("STORE SEKME GEZINMESI");
        Console.WriteLine("=====================");

        try
        {
            var store = provider.GetRequiredService<Bakım.ViewModels.StoreViewModel>();
            store.SwitchToCatalog();
            if (!store.IsCatalogTab) throw new InvalidOperationException("Catalog tab secilemedi.");
            Console.WriteLine("  GECTI  | Uygulama Kataloğu (Tab 0)");

            store.SwitchToPresets();
            if (!store.IsPresetsTab) throw new InvalidOperationException("Presets tab secilemedi.");
            Console.WriteLine("  GECTI  | Hazır Paketler (Tab 1)");

            store.SwitchToClassicTools();
            if (!store.IsClassicToolsTab) throw new InvalidOperationException("ClassicTools tab secilemedi.");
            Console.WriteLine("  GECTI  | Windows Konsolları & Klasik Araçlar (Tab 2)");
        }
        catch (Exception ex)
        {
            Failures.Add("store:tabs");
            Console.WriteLine($"  COKTU  | Store sekmeleri -> {ex.GetType().Name}: {ex.Message}");
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
            ("AnalyzerView",  () => new AnalyzerView()),
            ("WindowsTweakerModuleView", () => new WindowsTweakerModuleView()),
            ("SettingsModuleView",       () => new SettingsModuleView()),
            ("StoreModuleView",          () => new StoreModuleView()),

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
