using System.IO;
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

    public static int Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        int saved = 0, failed = 0;

        var app = new Bakım.App();
        app.InitializeComponent();

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
        main.Show();

        var keys = NavCatalog.AllItems(NavCatalog.Build()).Select(i => i.Key)
            .Append(NavCatalog.BuildSettingsItem().Key)
            .ToList();

        foreach (var (theme, themeName) in Themes)
        {
            ThemeService.Shared.ApplyTheme(theme, persist: false);
            ThemeService.Shared.ApplyBackdrop(false);

            foreach (int width in Widths)
            {
                main.Width = width;
                Pump(300);

                foreach (string key in keys)
                {
                    try
                    {
                        vm.Navigate(key);
                        Pump(1600); // Loaded + ilk veri yüklemesi + animasyonlar
                        string path = Path.Combine(outDir, $"{themeName}-{width}-{key}.png");
                        Save(main, path);
                        saved++;
                        Console.WriteLine($"  KAYDEDILDI | {Path.GetFileName(path)}");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Console.WriteLine($"  HATA       | {themeName}-{width}-{key}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }

        Console.WriteLine($"SONUC: {saved} goruntu, {failed} hata");
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
        var background = Application.Current.TryFindResource("ApplicationBackgroundBrush") as Brush ?? Brushes.Black;
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
