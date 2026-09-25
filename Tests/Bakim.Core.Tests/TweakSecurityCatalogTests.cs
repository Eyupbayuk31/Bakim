using Bakım.Core.Security;
using Xunit;

namespace Bakim.Core.Tests;

public class TweakSecurityCatalogTests
{
    [Theory]
    [InlineData("disable_smartscreen")]
    [InlineData("disable_windows_update")]
    [InlineData("disable_zone_identifier")]
    public void KnownSecurityReducers_AreHigh(string id)
    {
        var note = TweakSecurityCatalog.For(id);
        Assert.Equal(SecurityImpact.High, note.Impact);
        Assert.False(string.IsNullOrWhiteSpace(note.Warning));
    }

    [Fact]
    public void UnknownTweak_HasNoImpact()
    {
        Assert.Equal(SecurityImpact.None, TweakSecurityCatalog.For("show_seconds_taskbar").Impact);
        Assert.Equal(SecurityImpact.None, TweakSecurityCatalog.For(null).Impact);
    }

    [Fact]
    public void EveryEntry_HasAWarning()
    {
        foreach (var id in TweakSecurityCatalog.AllIds)
            Assert.False(string.IsNullOrWhiteSpace(TweakSecurityCatalog.For(id).Warning), id);
    }
}
