using Bakım.Core.GameMode;
using Bakım.Core.Text;
using Xunit;

namespace Bakim.Core.Tests;

public sealed class GameModeCoreTests
{
    [Fact]
    public void Parse_TrimsExe_DeDuplicates_RejectsPaths()
    {
        var list = ProcessNameList.Parse(" OneDrive.exe, Teams ;teams,, \n cs2 , C:\\x\\a.exe, \"Discord\" ");
        Assert.Equal(new[] { "OneDrive", "Teams", "cs2", "Discord" }, list);
    }

    [Fact]
    public void AddTo_AddsOnlyNewNames_AndReturnsThem()
    {
        var target = new List<string> { "OneDrive" };
        var added = ProcessNameList.AddTo(target, "onedrive.exe, Teams");
        Assert.Equal(new[] { "Teams" }, added);
        Assert.Equal(new[] { "OneDrive", "Teams" }, target);
    }

    [Fact]
    public void Format_RoundTripsThroughParse()
    {
        var text = ProcessNameList.Format(new[] { "OneDrive", "Teams.exe", "teams" });
        Assert.Equal("OneDrive, Teams", text);
        Assert.Equal(new[] { "OneDrive", "Teams" }, ProcessNameList.Parse(text));
    }

    [Fact]
    public void Plan_ReflectsProfile_NotStaticText()
    {
        var profile = new GameModeProfile("Keep", TrimMemory: false, SuspendApps: Array.Empty<string>(), AutoStart: false, AutoStartGames: Array.Empty<string>());
        var steps = GameModePlan.Build(profile);

        Assert.Equal(GameModeStepState.Skipped, steps.Single(s => s.Id == "power").State);
        Assert.Equal(GameModeStepState.Skipped, steps.Single(s => s.Id == "memory").State);
        Assert.Equal(GameModeStepState.Skipped, steps.Single(s => s.Id == "suspend").State);
        // Bakım'ın arka plan işleri her zaman duraklar.
        Assert.Equal(1, GameModePlan.ActiveStepCount(steps));
    }

    [Fact]
    public void Plan_FullProfile_ListsEveryStep()
    {
        var profile = new GameModeProfile("HighPerformance", true, new[] { "OneDrive", "Teams" }, true, new[] { "cs2" });
        var steps = GameModePlan.Build(profile);

        Assert.Equal(4, GameModePlan.ActiveStepCount(steps));
        var suspend = steps.Single(s => s.Id == "suspend");
        Assert.Equal("2 uygulama askıya alınır", suspend.Title);
        Assert.Equal("OneDrive · Teams", suspend.Detail);
    }

    [Fact]
    public void Plan_WithSession_ShowsWhatActuallyHappened()
    {
        var profile = new GameModeProfile("Ultimate", true, new[] { "OneDrive", "Teams" }, false, Array.Empty<string>());
        var session = new GameModeSessionInfo(DateTime.UtcNow, "cs2", "Yüksek Performans", false, 0, new[] { "OneDrive" }, 3);
        var steps = GameModePlan.Build(profile, session);

        Assert.All(steps, s => Assert.Equal(GameModeStepState.Applied, s.State));
        Assert.Equal("Güç planı: Yüksek Performans", steps.Single(s => s.Id == "power").Title);
        Assert.Equal("1 uygulama askıda", steps.Single(s => s.Id == "suspend").Title);
        Assert.True(session.IsAutomatic);
    }

    [Fact]
    public void Plan_WithSession_PowerPlanFailure_IsReported()
    {
        var profile = new GameModeProfile("HighPerformance", false, Array.Empty<string>(), false, Array.Empty<string>());
        var session = new GameModeSessionInfo(DateTime.UtcNow, null, null, true, 0, Array.Empty<string>(), 0);
        Assert.Equal(GameModeStepState.Failed, GameModePlan.Build(profile, session).Single(s => s.Id == "power").State);
        Assert.False(session.IsAutomatic);
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(247, "04:07")]
    [InlineData(3847, "1:04:07")]
    public void Clock_FormatsElapsedTime(int seconds, string expected) =>
        Assert.Equal(expected, DurationText.Clock(TimeSpan.FromSeconds(seconds)));
}
