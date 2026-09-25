using System;
using System.Linq;
using Bakım.Core.Startup;
using Xunit;

namespace Bakim.Core.Tests;

public class BootPerformanceTests
{
    private const string Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    private static string Boot(string time, int boot) =>
        $@"<Event xmlns=""{Ns}""><System><EventID>100</EventID><TimeCreated SystemTime=""{time}""/></System>
           <EventData><Data Name=""BootTime"">{boot}</Data><Data Name=""MainPathBootTime"">{boot / 2}</Data><Data Name=""BootPostBootTime"">{boot / 2}</Data></EventData></Event>";

    private static string App(string time, string name, int degradation) =>
        $@"<Event xmlns=""{Ns}""><System><EventID>101</EventID><TimeCreated SystemTime=""{time}""/></System>
           <EventData><Data Name=""Name"">{name}</Data><Data Name=""FriendlyName"">Friendly</Data><Data Name=""TotalTime"">{degradation + 500}</Data><Data Name=""DegradationTime"">{degradation}</Data></EventData></Event>";

    [Fact]
    public void ParsesBootEvent()
    {
        var b = BootEventParser.ParseBoot(Boot("2026-09-20T07:30:00.1234567Z", 34567));
        Assert.NotNull(b);
        Assert.Equal(34567, b!.BootMs);
        Assert.Equal(DateTimeKind.Utc, b.TimeUtc.Kind);
        Assert.Equal(7, b.TimeUtc.Hour);
        Assert.Null(BootEventParser.ParseBoot("<bozuk"));
        Assert.Null(BootEventParser.ParseBoot(null));
    }

    [Fact]
    public void AggregatesDegradationByFileName()
    {
        var events = new[]
        {
            BootEventParser.ParseDegradation(App("2026-09-20T07:30:00Z", "Discord.exe", 3000))!,
            BootEventParser.ParseDegradation(App("2026-09-21T07:30:00Z", "discord.exe", 1000))!,
            BootEventParser.ParseDegradation(App("2026-09-21T07:30:00Z", "tiny.exe", 120))!,
        };
        var map = BootEventParser.Aggregate(events);
        var discord = map["discord.exe"];
        Assert.Equal(2, discord.Count);
        Assert.Equal(2000, discord.AvgDegradationMs);
        Assert.Equal(3, discord.Level);
        Assert.Equal(1, map["tiny.exe"].Level);
        Assert.Equal("discord.exe", BootEventParser.KeyOf(@"""C:\Users\a\AppData\Local\Discord\Discord.exe"""));
    }

    [Fact]
    public void FormatsAndTrends()
    {
        Assert.Equal("34,6 sn", BootEventParser.FormatSeconds(34567).Replace('\u00A0', ' '));
        Assert.Equal("9,5 sn", BootEventParser.FormatSeconds(9500));
        var boots = new[] { 20000, 30000, 30000, 30000 }
            .Select((ms, i) => new BootRecord(DateTime.UtcNow.AddDays(-i), ms, 0, 0)).ToList();
        Assert.Equal(-10000, BootEventParser.TrendMs(boots));
        Assert.Null(BootEventParser.TrendMs(boots.Take(2).ToList()));
    }
}
