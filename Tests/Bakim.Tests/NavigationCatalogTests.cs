using System;
using System.Linq;
using Bakım.ViewModels;
using Xunit;

namespace Bakim.Tests;

/// <summary>Kenar çubuğu (IA v2, §2.1): her anahtar çözülür, her sayfaya kenar çubuğundan ulaşılır.</summary>
public sealed class NavigationCatalogTests
{
    [Fact]
    public void EveryNavKey_ResolvesToADistinctModule()
    {
        var items = NavCatalog.AllItems(NavCatalog.Build()).Append(NavCatalog.BuildSettingsItem()).ToList();
        foreach (var item in items)
            Assert.True(AppModuleRegistry.TryResolve(item.Key, out _), $"Çözülemeyen anahtar: {item.Key}");
        Assert.Equal(items.Count, items.Select(i => i.Module).Distinct().Count());
    }

    [Fact]
    public void EveryModule_IsReachableFromSidebar()
    {
        var reachable = NavCatalog.AllItems(NavCatalog.Build()).Select(i => i.Module)
            .Append(NavCatalog.BuildSettingsItem().Module).ToHashSet();
        foreach (AppModule module in Enum.GetValues<AppModule>())
        {
            // Gizlilik, Windows Ayarları'nın alt kategorisidir (§2.2).
            if (module == AppModule.PrivacyDebloat) continue;
            Assert.Contains(module, reachable);
        }
    }

    [Fact]
    public void Groups_HaveTitles_AndOnlyFirstIsMarkedFirst()
    {
        var groups = NavCatalog.Build();
        Assert.All(groups, g => Assert.False(string.IsNullOrWhiteSpace(g.Title)));
        Assert.True(groups[0].IsFirst);
        Assert.All(groups.Skip(1), g => Assert.False(g.IsFirst));
    }
}
