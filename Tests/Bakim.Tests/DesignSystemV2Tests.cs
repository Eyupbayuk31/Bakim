using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Bakım.Services;
using Xunit;

namespace Bakim.Tests;

/// <summary>Tasarım sistemi v2 (§3.1): açılış paleti ThemeService ile aynı; durum metni okunur.</summary>
public sealed class DesignSystemV2Tests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Bakım.csproj"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı");
        }
    }

    [Fact]
    public void BootstrapPalette_MatchesThemeService_ForMicaDark()
    {
        string xaml = File.ReadAllText(Path.Combine(RepoRoot, "Themes", "Tokens", "Palette.Bootstrap.xaml"));
        var defined = Regex.Matches(xaml, "x:Key=\"(?<k>[^\"]+)\" Color=\"(?<c>#[0-9A-Fa-f]+)\"")
            .ToDictionary(m => m.Groups["k"].Value, m => (Color)ColorConverter.ConvertFromString(m.Groups["c"].Value)!);

        foreach (var (key, color) in ThemeService.SemanticV2(ThemeService.GetDefinition(AppThemeKind.MicaDark)))
        {
            Assert.True(defined.ContainsKey(key), $"Açılış paletinde eksik: {key}");
            Assert.Equal(color, defined[key]);
        }
    }

    [Theory]
    [InlineData(AppThemeKind.MicaDark)]
    [InlineData(AppThemeKind.AmoledBlack)]
    [InlineData(AppThemeKind.CyberpunkPurple)]
    [InlineData(AppThemeKind.FluentLight)]
    public void StatusText_IsReadable_OnRaisedSurface(AppThemeKind kind)
    {
        var def = ThemeService.GetDefinition(kind);
        var keys = ThemeService.SemanticV2(def).ToDictionary(p => p.Key, p => p.Color);
        foreach (var name in new[] { "Success", "Caution", "Critical", "Info" })
        {
            double ratio = Contrast(keys[$"Status.{name}.Text"], keys["Surface.Raised"]);
            Assert.True(ratio >= 4.5, $"{def.DisplayName}: Status.{name}.Text kart üzerinde {ratio:F2}:1");
        }
    }

    [Fact]
    public void HighContrast_UsesSystemColors()
    {
        var def = ThemeService.GetDefinition(AppThemeKind.HighContrast);
        Assert.Equal(System.Windows.SystemColors.WindowTextColor, def.TextPrimary);
        Assert.Equal(System.Windows.SystemColors.WindowColor, def.WindowBackground);
    }

    private static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double Ch(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }
        double la = L(a), lb = L(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
