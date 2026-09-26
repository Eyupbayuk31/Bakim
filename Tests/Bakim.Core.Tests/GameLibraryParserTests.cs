using Bakım.Core.GameMode;
using Xunit;

namespace Bakim.Core.Tests;

public sealed class GameLibraryParserTests
{
    private const string LibraryFolders = """
        "libraryfolders"
        {
            "0"
            {
                "path"		"C:\\Program Files (x86)\\Steam"
                "label"		""
                "apps" { "228980" "1" "730" "2" }
            }
            "1"
            {
                "path"		"D:\\SteamLibrary"
            }
        }
        """;

    [Fact]
    public void LibraryFolders_AreParsedAndUnescaped()
    {
        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" },
            GameLibraryParser.ParseSteamLibraryFolders(LibraryFolders));
    }

    [Fact]
    public void AppManifest_ReadsIdNameAndInstallDir()
    {
        var m = GameLibraryParser.ParseSteamAppManifest("""
            "AppState"
            {
                "appid"		"730"
                "Universe"		"1"
                "name"		"Counter-Strike 2"
                "installdir"		"Counter-Strike Global Offensive"
                "UserConfig" { "name" "ignored" }
            }
            """);
        Assert.Equal(new SteamAppManifest("730", "Counter-Strike 2", "Counter-Strike Global Offensive"), m);
        Assert.False(GameLibraryParser.IsSteamTool(m!));
    }

    [Fact]
    public void SteamTools_AreNotGames()
    {
        Assert.True(GameLibraryParser.IsSteamTool(new SteamAppManifest("228980", "Steamworks Common Redistributables", "Steamworks Shared")));
        Assert.True(GameLibraryParser.IsSteamTool(new SteamAppManifest("1", "Proton 9.0", "Proton 9.0")));
    }

    [Fact]
    public void EpicManifest_ParsesGames_SkipsIncompleteAndNonApps()
    {
        var m = GameLibraryParser.ParseEpicManifest("""
            { "DisplayName": "Fortnite", "InstallLocation": "C:\\Games\\Fortnite",
              "LaunchExecutable": "FortniteGame/Binaries/Win64/FortniteLauncher.exe", "bIsApplication": true }
            """);
        Assert.Equal("Fortnite", m!.DisplayName);
        Assert.Null(GameLibraryParser.ParseEpicManifest("""{ "DisplayName": "X", "InstallLocation": "C:\\X", "LaunchExecutable": "x.exe", "bIsIncompleteInstall": true }"""));
        Assert.Null(GameLibraryParser.ParseEpicManifest("not json"));
    }

    [Fact]
    public void PickGameProcess_PrefersMatchingName_OverLargerHelpers()
    {
        var exes = new[]
        {
            (@"game\bin\win64\cs2.exe", 2_000_000L),
            (@"game\bin\win64\crashhandler.exe", 9_000_000L),
            (@"unins000.exe", 1_000_000L),
        };
        Assert.Equal("cs2", GameLibraryParser.PickGameProcess(exes, "Counter-Strike 2", "Counter-Strike Global Offensive")
                            ?? GameLibraryParser.PickGameProcess(exes, "cs2", "cs2"));
    }

    [Fact]
    public void PickGameProcess_PrefersUnrealShippingBinary_AndSkipsAntiCheatLauncher()
    {
        var exes = new[]
        {
            (@"start_protected_game.exe", 5_000_000L),
            (@"Game.exe", 300_000L),
            (@"Game\Binaries\Win64\Game-Win64-Shipping.exe", 90_000_000L),
        };
        Assert.Equal("Game-Win64-Shipping", GameLibraryParser.PickGameProcess(exes, "Some Game", "SomeGame"));
    }

    [Theory]
    [InlineData("ReachGame.exe", false)]
    [InlineData("Dispatcher.exe", false)]
    [InlineData("EACLauncher.exe", true)]
    [InlineData("UnityCrashHandler64.exe", true)]
    [InlineData(@"bin\unins000.exe", true)]
    public void NonGameExecutable_Detection(string file, bool expected) =>
        Assert.Equal(expected, GameLibraryParser.IsNonGameExecutable(file));

    [Fact]
    public void PickGameProcess_NoCandidates_IsNull() =>
        Assert.Null(GameLibraryParser.PickGameProcess(new[] { (@"unins000.exe", 1L) }, "X", "X"));
}
