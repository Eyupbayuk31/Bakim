using System;
using Bakım.Core.Health;
using Xunit;

namespace Bakim.Core.Tests;

public class WindowsLifecycleTests
{
    private static readonly DateTime Today = new(2026, 9, 25);

    [Theory]
    [InlineData(26100, SupportState.EndingSoon)]
    [InlineData(26200, SupportState.Supported)]
    [InlineData(22631, SupportState.Ended)]
    [InlineData(19045, SupportState.Ended)]
    [InlineData(27000, SupportState.Supported)]
    [InlineData(18363, SupportState.Unknown)]
    public void States(int build, SupportState expected) => Assert.Equal(expected, WindowsLifecycle.Describe(build, Today).State);

    [Fact]
    public void EndingSoon_SaysDays()
    {
        var info = WindowsLifecycle.Describe(26100, Today);
        Assert.Contains("18 gün", info.Text);
        Assert.Equal("Windows 11 24H2", info.Release);
    }
}
