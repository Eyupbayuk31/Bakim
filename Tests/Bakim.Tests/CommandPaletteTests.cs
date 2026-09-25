using System.Linq;
using Bakım.Models;
using Bakım.Services;
using Bakım.ViewModels;
using Xunit;

namespace Bakim.Tests;

/// <summary>Komut paleti (Faz 8, §5.19): Türkçe duyarsız arama, derin bağlantılar, ince ayar komutları.</summary>
public sealed class CommandPaletteTests
{
    private readonly CommandPaletteService _palette = new();

    [Fact]
    public void TurkishInsensitiveSearch_FindsDeepLink()
    {
        var results = _palette.Search("kaldirma gecmisi");
        Assert.Equal("Activity?kind=Uninstall", results.First().TargetParameter);
    }

    [Fact]
    public void TweakCommands_AreSearchable()
    {
        var hit = _palette.Search("bu bilgisayar").FirstOrDefault(c => c.Title.StartsWith("Ayar:"));
        Assert.NotNull(hit);
        Assert.StartsWith("WindowsTweaker:", hit!.TargetParameter);
    }

    [Fact]
    public void EveryNavigationTarget_Resolves()
    {
        foreach (var c in _palette.GetAllCommands().Where(c => c.ActionKind == CommandActionKind.Navigate && !c.TargetParameter.Contains(':')))
        {
            string key = c.TargetParameter.Split('?')[0];
            Assert.True(AppModuleRegistry.TryResolve(key, out _), $"Çözülemeyen hedef: {c.TargetParameter}");
        }
    }
}
