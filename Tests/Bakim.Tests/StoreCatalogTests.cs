using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Models;
using Bakım.Services;
using Bakım.ViewModels;
using Wpf.Ui.Controls;
using Xunit;

namespace Bakim.Tests;

/// <summary>
/// Mağaza kataloğu elle yazılmış ~150 girişten oluşur: yinelenen kimlik, var olmayan hazır paket hedefi ya da
/// başka programları da "kurulu" sayan kısa algılama anahtarı derleyicinin yakalayamayacağı hatalardır.
/// </summary>
public class StoreCatalogTests
{
    private static readonly List<StoreAppItem> Catalog = new StoreService(NullLogService.Instance).GetCatalog();

    [Fact]
    public void Ids_AreUnique()
    {
        var dup = Catalog.GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(dup);
    }

    [Fact]
    public void WingetIds_AreUnique()
    {
        var ids = Catalog.SelectMany(a => a.BundleWingetIds?.Length > 0 ? a.BundleWingetIds : new[] { a.WingetId })
            .Where(id => !string.IsNullOrWhiteSpace(id));
        var dup = ids.GroupBy(id => id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(dup);
    }

    [Fact]
    public void EveryWingetApp_HasPackageId()
    {
        var missing = Catalog.Where(a => a.InstallerType == StoreInstallerType.Winget
                                         && string.IsNullOrWhiteSpace(a.WingetId)
                                         && !(a.BundleWingetIds?.Length > 0))
            .Select(a => a.Id).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryCategory_HasApps_AndDisplayNameIsConsistent()
    {
        foreach (var cat in Enum.GetValues<StoreCategory>().Where(c => c != StoreCategory.All))
        {
            var apps = Catalog.Where(a => a.Category == cat).ToList();
            Assert.True(apps.Count > 0, $"{cat} kategorisi boş");
            Assert.Single(apps.Select(a => a.CategoryDisplayName).Distinct());
        }
    }

    [Fact]
    public void DetectKeywords_AreSpecificEnough()
    {
        // "Git", "Zen", "itch" gibi kısa anahtarlar başka programların adında da geçer ve yanlış "Kurulu" gösterir.
        var weak = Catalog
            .SelectMany(a => (a.RegistryDetectKeyword ?? string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .DefaultIfEmpty(string.Empty)
                .Where(k => k.StartsWith('=') ? k.Length < 3 : k.Length < 4)
                .Select(k => $"{a.Id}: '{k}'"))
            .ToList();
        Assert.Empty(weak);
    }

    [Theory]
    [InlineData("Git", "=Git", true)]
    [InlineData("GitHub Desktop", "=Git", false)]
    [InlineData("Digital Editions", "=Git", false)]
    [InlineData("Eclipse Temurin JDK with Hotspot 17.0.20+8 (x64)", "Eclipse Temurin JDK with Hotspot 17", true)]
    [InlineData("Eclipse Temurin JRE with Hotspot 21.0.8+9 (x64)", "Eclipse Temurin JDK with Hotspot 17", false)]
    public void DetectKeyword_Matching(string displayName, string keyword, bool expected) =>
        Assert.Equal(expected, StoreService.MatchesDetectKeyword(displayName, keyword));

    [Fact]
    public void Icons_AreValidSymbols()
    {
        var bad = Catalog.Where(a => !Enum.TryParse<SymbolRegular>(a.IconSymbol, out _)).Select(a => a.Id).ToList();
        Assert.Empty(bad);
    }

    [Fact]
    public void Presets_TargetOnlyCatalogApps()
    {
        var vm = new StoreViewModel(new CatalogOnlyStore(), NullLogService.Instance);
        var ids = new HashSet<string>(Catalog.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
        Assert.True(vm.Presets.Count >= 8);
        foreach (var preset in vm.Presets)
        {
            var missing = preset.TargetIds.Where(id => !ids.Contains(id)).ToList();
            Assert.True(missing.Count == 0, $"{preset.Id}: {string.Join(", ", missing)}");
            Assert.Equal(preset.TargetIds.Count, preset.IncludedApps.Count);
        }
    }

    private sealed class CatalogOnlyStore : IStoreService
    {
        public List<StoreAppItem> GetCatalog() => new StoreService(NullLogService.Instance).GetCatalog();
        public Task CheckInstalledStatusesAsync(IEnumerable<StoreAppItem> items) => Task.CompletedTask;
        public Task<bool> InstallAppAsync(StoreAppItem app, Action<int, string>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> IsWingetAvailableAsync() => Task.FromResult(false);
        public Task<(IReadOnlyList<Bakım.Core.Store.WingetUpgrade> Upgrades, string? Error)> GetUpgradesAsync(CancellationToken ct = default)
            => Task.FromResult<(IReadOnlyList<Bakım.Core.Store.WingetUpgrade>, string?)>((Array.Empty<Bakım.Core.Store.WingetUpgrade>(), null));
        public Task<(bool Success, string Message)> UpgradeAsync(Bakım.Core.Store.WingetUpgrade package, CancellationToken ct = default)
            => Task.FromResult((false, string.Empty));
    }
}
