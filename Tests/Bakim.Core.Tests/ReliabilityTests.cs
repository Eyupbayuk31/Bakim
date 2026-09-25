using System;
using Bakım.Core.Health;
using Xunit;

namespace Bakim.Core.Tests;

public class ReliabilityTests
{
    [Fact]
    public void Daily_TakesMinimumPerDay_AndWindow()
    {
        var today = new DateTime(2026, 9, 25);
        var samples = new[]
        {
            (today.AddHours(1), 9.5), (today.AddHours(5), 7.2),
            (today.AddDays(-1).AddHours(3), 10.0),
            (today.AddDays(-40), 3.0),          // pencere dışı
            (today.AddDays(-2), 0.0),           // geçersiz
        };
        var days = ReliabilityTimeline.Daily(samples, 30, today);
        Assert.Equal(2, days.Count);
        Assert.Equal(7.2, days[^1].Index);
        Assert.Contains("7,2/10", ReliabilityTimeline.Describe(days));
    }

    [Fact]
    public void Empty_IsExplained() =>
        Assert.Contains("verisi yok", ReliabilityTimeline.Describe(Array.Empty<ReliabilityDay>()));
}
