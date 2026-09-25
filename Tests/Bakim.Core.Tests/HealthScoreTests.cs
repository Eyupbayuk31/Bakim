using System;
using System.Linq;
using Bakım.Core.Health;
using Xunit;

namespace Bakim.Core.Tests;

public sealed class HealthScoreTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

    private static HealthSignals Healthy() => new()
    {
        NowUtc = Now,
        SystemDriveUsedPercent = 50,
        SystemDriveFreeBytes = 200L * 1024 * 1024 * 1024,
        EnabledStartupCount = 5,
        BsodLast7Days = 0,
        CriticalEventsLast7Days = 0,
        DefenderRealtimeOn = true,
        RebootPending = false,
        LastCleanUtc = Now.AddDays(-2)
    };

    [Fact]
    public void HealthySystem_Scores100_WithNoIssues()
    {
        var r = HealthScore.Evaluate(Healthy());
        Assert.Equal(100, r.Score);
        Assert.Empty(r.Issues);
        Assert.Equal(HealthLevel.Good, r.Level);
        Assert.Equal(6, r.Components.Count);
    }

    [Fact]
    public void UnknownSignals_DoNotDeduct()
    {
        var r = HealthScore.Evaluate(new HealthSignals { NowUtc = Now, ActivityKnown = false });
        Assert.Equal(100, r.Score);
        Assert.All(r.Components, c => Assert.Equal(HealthLevel.Unknown, c.Level));
    }

    [Theory]
    [InlineData(79, 0)]
    [InlineData(80, 8)]
    [InlineData(90, 20)]
    [InlineData(97, 30)]
    public void DiskFullness_Deducts(int used, int deduction)
    {
        var r = HealthScore.Evaluate(Healthy() with { SystemDriveUsedPercent = used });
        Assert.Equal(deduction, r.Components.Single(c => c.Key == "disk").Deduction);
        Assert.Equal(100 - deduction, r.Score);
    }

    [Fact]
    public void Bsod_IsAProblem_AndLinksToCrashAnalyzer()
    {
        var c = HealthScore.Evaluate(Healthy() with { BsodLast7Days = 2, CriticalEventsLast7Days = 4 })
            .Components.Single(c => c.Key == "stability");
        Assert.Equal(HealthLevel.Problem, c.Level);
        Assert.Equal(20, c.Deduction);
        Assert.Equal("CrashAnalyzer", c.DeepLink);
        Assert.Contains("2 mavi ekran", c.Detail);
    }

    [Fact]
    public void DefenderOff_IsLargestIssue()
    {
        var r = HealthScore.Evaluate(Healthy() with { DefenderRealtimeOn = false, EnabledStartupCount = 15 });
        Assert.Equal("defender", r.Issues[0].Key);
        Assert.Equal(100 - 25 - 6, r.Score);
    }

    [Fact]
    public void NoCleanupYet_OrOld_IsAttention()
    {
        Assert.Equal(5, HealthScore.Evaluate(Healthy() with { LastCleanUtc = null }).Components.Single(c => c.Key == "cleanup").Deduction);
        Assert.Equal(5, HealthScore.Evaluate(Healthy() with { LastCleanUtc = Now.AddDays(-45) }).Components.Single(c => c.Key == "cleanup").Deduction);
        Assert.Equal(0, HealthScore.Evaluate(Healthy() with { LastCleanUtc = Now.AddDays(-10) }).Components.Single(c => c.Key == "cleanup").Deduction);
    }

    [Fact]
    public void Score_NeverBelowZero()
    {
        var r = HealthScore.Evaluate(Healthy() with
        {
            SystemDriveUsedPercent = 99, EnabledStartupCount = 40, BsodLast7Days = 9,
            DefenderRealtimeOn = false, RebootPending = true, LastCleanUtc = null
        });
        Assert.Equal(HealthLevel.Problem, r.Level);
        Assert.InRange(r.Score, 0, 100);
        Assert.Equal("Sorunlar var", r.Title);
    }
}
