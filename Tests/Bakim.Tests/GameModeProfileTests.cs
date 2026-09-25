using Bakım.Services;
using Xunit;

namespace Bakim.Tests;

public sealed class GameModeProfileTests
{
    [Fact]
    public void ParseProcessList_TrimsExe_DeDuplicates_AndIgnoresBlanks()
    {
        var list = GameModeService.ParseProcessList(" OneDrive.exe, Teams ;teams,, \n cs2 ");
        Assert.Equal(new[] { "OneDrive", "Teams", "cs2" }, list);
    }

    [Fact]
    public void ParseProcessList_NullOrEmpty_IsEmpty()
    {
        Assert.Empty(GameModeService.ParseProcessList(null));
        Assert.Empty(GameModeService.ParseProcessList("  ,  ; "));
    }
}
