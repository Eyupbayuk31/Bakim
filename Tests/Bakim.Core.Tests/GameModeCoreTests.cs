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
        var profile = new GameModeProfile("Keep", TrimMemory: false, SuspendApps: Array.Empty<string>(), AutoStart: false, AutoStartGames: Array.Empty<string>(),
            Prioritize: false, WindowsGameMode: false);
        var steps = GameModePlan.Build(profile);

        Assert.Equal(GameModeStepState.Skipped, steps.Single(s => s.Id == "power").State);
        Assert.Equal(GameModeStepState.Skipped, steps.Single(s => s.Id == "memory").State);
        Assert.Equal(GameModeStepState.Skipped, steps.Single(s => s.Id == "suspend").State);
        Assert.Equal(GameModeStepState.Skipped, steps.Single(s => s.Id == "priority").State);
        Assert.Equal(GameModeStepState.Skipped, steps.Single(s => s.Id == "wingame").State);
        // Bakım'ın arka plan işleri her zaman duraklar.
        Assert.Equal(1, GameModePlan.ActiveStepCount(steps));
    }

    [Fact]
    public void Plan_FullProfile_ListsEveryStep()
    {
        var profile = new GameModeProfile("HighPerformance", true, new[] { "OneDrive", "Teams" }, true, new[] { "cs2" });
        var steps = GameModePlan.Build(profile);

        Assert.Equal(6, GameModePlan.ActiveStepCount(steps));
        var suspend = steps.Single(s => s.Id == "suspend");
        Assert.Equal("2 uygulama askıya alınır", suspend.Title);
        Assert.Equal("OneDrive · Teams", suspend.Detail);
    }

    [Fact]
    public void Plan_WithSession_ShowsWhatActuallyHappened()
    {
        var profile = new GameModeProfile("Ultimate", true, new[] { "OneDrive", "Teams" }, false, Array.Empty<string>());
        var session = new GameModeSessionInfo(DateTime.UtcNow, "cs2", "Yüksek Performans", false, 0, new[] { "OneDrive" }, 3,
            PrioritizedGame: "cs2", LoweredCount: 2, WindowsGameModeTurnedOn: true);
        var steps = GameModePlan.Build(profile, session);

        Assert.All(steps, s => Assert.Equal(GameModeStepState.Applied, s.State));
        Assert.Equal("cs2 önceliği yükseltildi", steps.Single(s => s.Id == "priority").Title);
        Assert.Equal("Windows Oyun Modu açıldı", steps.Single(s => s.Id == "wingame").Title);
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

public sealed class GameModeRulesTests
{
    [Theory]
    [InlineData(84u, false)]
    [InlineData(85u, true)]
    [InlineData(97u, true)]
    public void Trim_OnlyUnderMemoryPressure(uint load, bool expected) => Assert.Equal(expected, GameModeRules.ShouldTrim(load));

    [Fact]
    public void SelectBackground_NeverTouchesGameForegroundOrSelf()
    {
        var running = new[]
        {
            new RunningProcess(100, "Discord"), new RunningProcess(101, "chrome"), new RunningProcess(102, "chrome"),
            new RunningProcess(200, "VALORANT-Win64-Shipping"), new RunningProcess(300, "Bakim"), new RunningProcess(4, "System"),
            new RunningProcess(400, "notepad")
        };
        var picked = GameModeRules.SelectBackground(running, new[] { "discord", "chrome", "VALORANT-Win64-Shipping", "Bakim", "System" },
            gameName: "VALORANT-Win64-Shipping", foregroundPid: 101, selfPid: 300);
        Assert.Equal(new[] { 100, 102 }, picked.Select(p => p.Pid));
    }

    [Fact]
    public void BackgroundCpu_SubtractsGame_AndClamps()
    {
        // 6 çekirdek, 10 sn: kapasite 60 sn. Sistem 15 sn meşgul, oyun 9 sn → arka plan 6 sn = %10.
        Assert.Equal(10, GameModeRules.BackgroundCpuPercent(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(10), 6), 3);
        Assert.Equal(0, GameModeRules.BackgroundCpuPercent(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), 6));
        Assert.Equal(0, GameModeRules.BackgroundCpuPercent(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.Zero, 6));
        Assert.Equal("%12 → %4", GameModeRules.CpuChangeText(12.3, 4.4));
        Assert.Equal(string.Empty, GameModeRules.CpuChangeText(null, 4));
    }

    [Fact]
    public void CpuAverager_Averages()
    {
        var a = new CpuAverager();
        Assert.Null(a.Average);
        a.Add(10); a.Add(20);
        Assert.Equal(15, a.Average);
    }

    [Fact]
    public void Riot_ParsesInstallPath()
    {
        string yaml = "product_install_full_path: \"C:/Riot Games/VALORANT/live\"\nproduct_install_root: \"C:/Riot Games\"\n";
        Assert.Equal(@"C:\Riot Games\VALORANT\live", RiotGames.ParseInstallPath(yaml));
        Assert.Null(RiotGames.ParseInstallPath("product_install_root: \"C:/Riot Games\""));
        Assert.Contains(RiotGames.Known, p => p.Id == "valorant.live" && p.RelativeExe.EndsWith("VALORANT-Win64-Shipping.exe"));
    }
}
