using System.IO;
using System.Linq;
using Bakım.Core.Cleaning;
using Xunit;

namespace Bakim.Core.Tests;

public sealed class BrowserProfilesTests
{
    [Theory]
    [InlineData("Default", true)]
    [InlineData("Profile 1", true)]
    [InlineData("Profile 12", true)]
    [InlineData("Guest Profile", true)]
    [InlineData("System Profile", false)]
    [InlineData("Crashpad", false)]
    [InlineData("Profile", false)]
    [InlineData("Profile X", false)]
    [InlineData("ShaderCache", false)]
    public void RecognisesProfiles(string name, bool expected) =>
        Assert.Equal(expected, BrowserProfiles.IsChromiumProfileDirectory(name));

    [Fact]
    public void CacheDirectories_CoverEveryProfile_AndOnlyCacheFolders()
    {
        var dirs = BrowserProfiles.CacheDirectories("UD", new[] { "Default", "Profile 2", "Crashpad", "Profile 1" });
        Assert.Equal(9, dirs.Count);
        Assert.Contains(Path.Combine("UD", "Profile 2", "Code Cache"), dirs);
        Assert.DoesNotContain(dirs, d => d.Contains("Crashpad"));
        Assert.All(dirs, d => Assert.Contains(Path.GetFileName(d), BrowserProfiles.CacheSubdirectories));
        // Çerez / geçmiş / oturum klasörleri asla hedef değil
        Assert.DoesNotContain(dirs, d => d.EndsWith("Cookies") || d.EndsWith("History") || d.EndsWith("Sessions"));
    }
}
