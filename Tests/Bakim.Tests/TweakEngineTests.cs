using System.Linq;
using System.Threading.Tasks;
using Bakım.Core.Tweaks;
using Bakım.Services;
using Bakım.Services.Tweaks;
using Xunit;

namespace Bakim.Tests;

/// <summary>Veri tabanlı ince ayarlar (Faz 8, §5.16): gömülü katalog yüklenir, servislerle çakışmaz.</summary>
public sealed class TweakEngineTests
{
    [Fact]
    public void EmbeddedCatalog_LoadsAndIsValid()
    {
        Assert.True(TweakEngine.Definitions.Count >= 50);
        Assert.Empty(TweakCatalog.Validate(TweakEngine.Definitions));
        Assert.True(TweakEngine.Handles("explorer_launch_to_this_pc"));
    }

    [Fact]
    public async Task ServiceLists_HaveUniqueIds_AndCatalogItemsExplainChanges()
    {
        var explorer = await new FileExplorerTweaksService().GetFileExplorerTweaksAsync();
        Assert.Equal(explorer.Count, explorer.Select(t => t.Id).Distinct().Count());
        var catalogItem = explorer.Single(t => t.Id == "explorer_launch_to_this_pc");
        Assert.True(catalogItem.HasChangeList);
        Assert.Contains(catalogItem.ChangesWhenOn, c => c.Contains("LaunchTo"));
        // Karmaşık (sayısal) ayar hâlâ serviste.
        Assert.Contains(explorer, t => t.Id == "explorer_jumplist_item_count");
    }

    [Fact]
    public void AdminRequirement_IsDerived()
    {
        var policy = TweakEngine.Find("explorer_disable_search_history");
        Assert.NotNull(policy);
        Assert.True(policy!.RequiresAdmin); // HKCU\Software\Policies kullanıcıya salt okunur
    }
}
