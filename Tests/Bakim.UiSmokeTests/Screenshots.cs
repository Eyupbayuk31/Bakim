using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Bakım.Services;
using Bakım.ViewModels;

namespace Bakim.UiSmokeTests;

/// <summary>
/// GÖRSEL DOĞRULAMA
/// ================
/// Gerçek kabuk penceresini (DI, görünüm modelleri, temalar) ekran dışında açar, her kenar
/// çubuğu sayfasına gider ve pencere içeriğini PNG'ye çizer: 2 tema × 2 genişlik.
/// CI'da yapıt olarak yüklenir; tasarım değişiklikleri kullanıcı ekran görüntüsü
/// beklemeden incelenebilir.
///
///   dotnet run --project Tests/Bakim.UiSmokeTests -c Release -- --screenshots çıktı_klasörü
/// </summary>
internal static class Screenshots
{
    private static readonly (AppThemeKind Theme, string Name)[] Themes =
    {
        (AppThemeKind.MicaDark, "koyu"),
        (AppThemeKind.FluentLight, "acik"),
    };

    private static readonly int[] Widths = { 1280, 1600 };

    /// <summary>Veri gerektiren görünümler: çekimden önce örnek durum hazırlanır (yalnızca tek sayfa kipinde ağır işler).</summary>
    public static string? PrepareFolder { get; set; }

    private static void Prepare(MainViewModel vm, string key)
    {
        if (key.Equals("Storage", StringComparison.OrdinalIgnoreCase) && PrepareFolder != null)
        {
            var storage = vm.Storage;
            storage.DiskMapTarget = PrepareFolder;
            storage.ScanDiskMapCommand.Execute(null);
            for (int i = 0; i < 120 && (storage.IsDiskMapScanning || !storage.HasDiskMap); i++) Pump(500);
        }
        else if (key.Equals("Analyzer", StringComparison.OrdinalIgnoreCase) && PrepareFolder == "scan")
        {
            var analyzer = vm.Analyzer;
            for (int i = 0; i < 60 && (analyzer.IsScanning || analyzer.Items.Count == 0); i++) Pump(500);
            var visible = analyzer.FilteredItems.Cast<Bakım.Models.PersistenceItem>().ToList();
            foreach (var item in visible.Skip(1).Take(2)) item.IsChecked = true;
            analyzer.UpdateCheckedCommand.Execute(null);
            analyzer.SelectedItem = visible.FirstOrDefault();
            Pump(1000);
        }
        else if (key.Equals("Analyzer", StringComparison.OrdinalIgnoreCase) && PrepareFolder == "inpage")
        {
            var analyzer = vm.Analyzer;
            for (int i = 0; i < 60 && (analyzer.IsScanning || analyzer.Items.Count == 0); i++) Pump(500);
            // Düzenek gerçek geçmiş deposunu kullanır: çekim için yapılan analiz kullanıcının geçmişinde kalmasın.
            var history = Bakım.App.GetService<Bakım.Services.History.IAnalysisHistoryService>();
            var before = history.Query(Bakım.Core.History.AnalysisHistoryFilter.All).Select(r => r.Id).ToHashSet();
            var target = analyzer.FilteredItems.Cast<Bakım.Models.PersistenceItem>().FirstOrDefault(i => i.HasValidFile);
            if (target != null) analyzer.AnalyzeThreatCommand.Execute(target);
            for (int i = 0; i < 80 && !analyzer.HasInPageAnalysis; i++) Pump(500);
            var created = history.Query(Bakım.Core.History.AnalysisHistoryFilter.All).Select(r => r.Id).Where(id => !before.Contains(id)).ToList();
            if (created.Count > 0) history.RemoveAsync(created).GetAwaiter().GetResult();
            Pump(1500);
        }
        else if (key.Equals("Analyzer", StringComparison.OrdinalIgnoreCase) && PrepareFolder == "history")
        {
            vm.Analyzer.SwitchTabCommand.Execute("History");
            Pump(1500);
            if (vm.Analyzer.History.Rows.Count > 0) vm.Analyzer.History.Selected = vm.Analyzer.History.Rows[0];
        }
    }

    public static int Run(string outDir, string? onlyKey = null)
    {
        Directory.CreateDirectory(outDir);
        int saved = 0, failed = 0;
        string current = "başlangıç";

        // Bekçi: bir sayfa kalıcı diyalog açar ya da donarsa iş 25 dk boyunca sessizce beklemesin.
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromMinutes(8));
            Console.WriteLine($"ZAMAN ASIMI | son adim: {current}; {saved} goruntu kaydedildi");
            Console.Out.Flush();
            Environment.Exit(2);
        }) { IsBackground = true };
        watchdog.Start();

        Bakım.App.SkipStartupForTesting = true;
        // Run() çağrılmadığı için WPF bağlamı kurulmaz; await sonrası kod arayüz iş parçacığına dönsün.
        System.Threading.SynchronizationContext.SetSynchronizationContext(
            new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        var app = new Bakım.App();
        app.InitializeComponent();

        // Tek bir sayfanın hatası tüm görüntü setini düşürmesin: günlüğe yaz, devam et.
        var errors = new List<string>();
        app.DispatcherUnhandledException += (_, e) =>
        {
            errors.Add(e.Exception.GetType().Name + ": " + e.Exception.Message);
            Console.WriteLine($"  UYARI      | {e.Exception}");
            e.Handled = true;
        };

        var collection = new ServiceCollection();
        Bakım.App.ConfigureServices(collection, NullLogService.Instance, new AppSettingsService(NullLogService.Instance));
        var provider = collection.BuildServiceProvider();
        Bakım.App.SetServicesForTesting(provider);

        var main = provider.GetRequiredService<Bakım.MainWindow>();
        var vm = (MainViewModel)main.DataContext!;

        main.WindowStartupLocation = WindowStartupLocation.Manual;
        main.Left = -32000;
        main.Top = 0;
        main.ShowActivated = false;
        main.ShowInTaskbar = false;
        main.Width = Widths[0];
        main.Height = 900;
        current = "pencere gosteriliyor";
        Console.WriteLine("  PENCERE    | gosteriliyor");
        try
        {
            main.Show();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  UYARI      | Pencere gösterilirken: {ex}");
            failed++;
        }

        var keys = NavCatalog.AllItems(NavCatalog.Build()).Select(i => i.Key)
            .Append(NavCatalog.BuildSettingsItem().Key)
            .Where(k => onlyKey == null || k.Contains(onlyKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var themes = onlyKey == null ? Themes : Themes.Take(1).ToArray();
        // Tek sayfa kipinde pencerenin kendi açılış gezintisi (son açık sayfa) önce bitsin; yoksa onu ezer.
        if (onlyKey != null) Pump(3000);
        var widths = onlyKey == null ? Widths : Widths.Take(1).ToArray();

        foreach (var (theme, themeName) in themes)
        {
            ThemeService.Shared.ApplyTheme(theme, persist: false);
            ThemeService.Shared.ApplyBackdrop(false);

            foreach (int width in widths)
            {
                main.Width = width;
                Pump(300);

                foreach (string key in keys)
                {
                    current = $"{themeName}-{width}-{key}";
                    Console.WriteLine($"  GIDILIYOR  | {current}");
                    try
                    {
                        vm.Navigate(key);
                        Prepare(vm, key);
                        // Loaded + ilk veri yüklemesi + animasyonlar; tek sayfa çekiminde tarama bitsin diye daha uzun.
                        Pump(onlyKey == null ? 1600 : 12000);
                        string path = Path.Combine(outDir, $"{themeName}-{width}-{key}.png");
                        Save(main, path);
                        saved++;
                        Console.WriteLine($"  KAYDEDILDI | {Path.GetFileName(path)}");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Console.WriteLine($"  HATA       | {themeName}-{width}-{key}: {ex}");
                    }
                }
            }
        }

        Console.WriteLine($"SONUC: {saved} goruntu, {failed} hata, {errors.Count} dagitici hatasi");
        // Arka plan servisleri (telemetri, tepsi) süreci açık tutabilir.
        Environment.Exit(failed == 0 ? 0 : 1);
        return 0;
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Save(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        root.UpdateLayout();
        int w = Math.Max(1, (int)Math.Ceiling(root.ActualWidth));
        int h = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));

        // Mica ekran görüntüsüne çizilmez: pencere zeminini elle doldur.
        // Pencerenin kaynak zinciri uygulama kaynaklarına da çıkar; Application.Current bu koşucuda null olabilir.
        // Zemin fırçası Mica için yarı saydamdır (alfa 0'a kadar); görüntüde beyaz çıkmasın diye opak boyanır.
        Brush background = window.TryFindResource("ApplicationBackgroundBrush") is SolidColorBrush solid
            ? new SolidColorBrush(Color.FromRgb(solid.Color.R, solid.Color.G, solid.Color.B))
            : Brushes.Black;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(background, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(root)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, w, h)
            }, null, new Rect(0, 0, w, h));
        }

        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
