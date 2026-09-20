using System.Windows.Media;
using Bakım.Services;
using Xunit;
using Xunit.Abstractions;

namespace Bakim.Tests;

/// <summary>
/// Tema paletlerinin WCAG 2.1 kontrast denetimi.
///
/// Tasarım sistemi "metin/yüzey çiftleri AA (4.5:1) hedefler" diye belgelenmiştir.
/// Bu iddia test edilmezse palet zamanla sessizce bozulur — özellikle yeni tema
/// eklenirken. Aşağıdaki testler her temayı otomatik denetler, yani gelecekte
/// eklenen bir tema da aynı çıtayı geçmek zorundadır.
/// </summary>
public class ThemeContrastTests
{
    private const double AaNormalText = 4.5;
    private const double AaLargeText = 3.0;

    private readonly ITestOutputHelper _output;

    public ThemeContrastTests(ITestOutputHelper output) => _output = output;

    /// <summary>WCAG 2.1 bağıl parlaklık (relative luminance).</summary>
    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    public static TheoryData<AppThemeKind> AllThemes()
    {
        var data = new TheoryData<AppThemeKind>();
        foreach (AppThemeKind kind in Enum.GetValues<AppThemeKind>())
            data.Add(kind);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllThemes))]
    public void PrimaryText_MeetsAa_OnAllSurfaces(AppThemeKind kind)
    {
        var t = ThemeService.GetDefinition(kind);

        var surfaces = new (string Name, Color Color)[]
        {
            ("pencere", t.WindowBackground),
            ("kart", t.CardBackground),
            ("kart-alt", t.CardBackgroundAlt),
            ("denetim", t.ControlFill),
        };

        foreach (var (name, surface) in surfaces)
        {
            double ratio = Contrast(t.TextPrimary, surface);
            _output.WriteLine($"{t.DisplayName} | birincil metin / {name}: {ratio:F2}:1");
            Assert.True(ratio >= AaNormalText,
                $"{t.DisplayName}: birincil metin '{name}' yuzeyinde {ratio:F2}:1 " +
                $"(AA icin {AaNormalText}:1 gerekli)");
        }
    }

    [Theory]
    [MemberData(nameof(AllThemes))]
    public void SecondaryText_MeetsAa_OnCardSurface(AppThemeKind kind)
    {
        var t = ThemeService.GetDefinition(kind);

        double ratio = Contrast(t.TextSecondary, t.CardBackground);
        _output.WriteLine($"{t.DisplayName} | ikincil metin / kart: {ratio:F2}:1");

        Assert.True(ratio >= AaNormalText,
            $"{t.DisplayName}: ikincil metin kart uzerinde {ratio:F2}:1 (AA icin {AaNormalText}:1)");
    }

    [Theory]
    [MemberData(nameof(AllThemes))]
    public void TertiaryText_MeetsAa_OnCardSurface(AppThemeKind kind)
    {
        var t = ThemeService.GetDefinition(kind);

        double ratio = Contrast(t.TextTertiary, t.CardBackground);
        _output.WriteLine($"{t.DisplayName} | ucuncul metin / kart: {ratio:F2}:1");

        Assert.True(ratio >= AaNormalText,
            $"{t.DisplayName}: ucuncul metin kart uzerinde {ratio:F2}:1 (AA icin {AaNormalText}:1)");
    }

    [Theory]
    [MemberData(nameof(AllThemes))]
    public void IntentColors_AreDistinguishable_OnCardSurface(AppThemeKind kind)
    {
        var t = ThemeService.GetDefinition(kind);

        // Anlam renkleri çoğunlukla ikon ve kalın rozet metni olarak kullanılır
        // (büyük/kalın metin sayılır) — AA büyük metin eşiği 3:1.
        var intents = new (string Name, Color Color)[]
        {
            ("vurgu", t.Accent),
            ("basari", t.Success),
            ("uyari", t.Caution),
            ("kritik", t.Critical),
        };

        foreach (var (name, color) in intents)
        {
            double ratio = Contrast(color, t.CardBackground);
            _output.WriteLine($"{t.DisplayName} | {name} / kart: {ratio:F2}:1");
            Assert.True(ratio >= AaLargeText,
                $"{t.DisplayName}: '{name}' rengi kart uzerinde {ratio:F2}:1 " +
                $"(buyuk metin/ikon icin {AaLargeText}:1 gerekli)");
        }
    }

    [Theory]
    [MemberData(nameof(AllThemes))]
    public void SurfaceAndBorder_AreVisuallySeparable(AppThemeKind kind)
    {
        var t = ThemeService.GetDefinition(kind);

        // Kenarlık, kartı zeminden ayırabilmeli. WCAG 1.4.11 (grafik nesneler) 3:1 ister;
        // ince ayırıcılar için pratik alt sınır olarak 1.3:1 uyguluyoruz.
        double ratio = Contrast(t.CardStroke, t.CardBackground);
        _output.WriteLine($"{t.DisplayName} | kenarlik / kart: {ratio:F2}:1");

        Assert.True(ratio >= 1.3,
            $"{t.DisplayName}: kenarlik kart zemininden ayirt edilemiyor ({ratio:F2}:1)");
    }

    [Fact]
    public void EveryTheme_HasDistinctWindowBackground()
    {
        var seen = new Dictionary<Color, AppThemeKind>();

        foreach (AppThemeKind kind in Enum.GetValues<AppThemeKind>())
        {
            var bg = ThemeService.GetDefinition(kind).WindowBackground;
            Assert.False(seen.ContainsKey(bg),
                $"{kind} ile {(seen.TryGetValue(bg, out var other) ? other : kind)} ayni arkaplani kullaniyor");
            seen[bg] = kind;
        }
    }

    [Fact]
    public void LightTheme_IsActuallyLight_AndDarkThemesAreDark()
    {
        foreach (AppThemeKind kind in Enum.GetValues<AppThemeKind>())
        {
            var t = ThemeService.GetDefinition(kind);
            double bg = Luminance(t.WindowBackground);

            if (t.IsDark)
                Assert.True(bg < 0.2, $"{t.DisplayName} koyu isaretli ama arkaplan parlakligi {bg:F3}");
            else
                Assert.True(bg > 0.5, $"{t.DisplayName} acik isaretli ama arkaplan parlakligi {bg:F3}");
        }
    }
}
