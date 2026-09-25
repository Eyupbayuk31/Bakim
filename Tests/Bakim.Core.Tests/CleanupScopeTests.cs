using System;
using Bakım.Core.Cleaning;
using Xunit;

namespace Bakim.Core.Tests;

public class CleanupScopeTests
{
    private static CleanupScope Scope() => new(
        new[]
        {
            @"C:\Users\Ali\AppData\Local\Temp",
            @"C:\Windows\Temp",
            @"C:\Users\Ali\AppData\Roaming\Telegram Desktop\tdata\user_data\cache",
            @"C:\Users\Ali\AppData\Local\Mozilla\Firefox\Profiles\abc.default\cache2",
            @"C:\Users",   // çok geniş: yok sayılmalı
            @"C:\",        // çok geniş: yok sayılmalı
            null
        },
        new[] { @"C:\Windows\System32", @"C:\Windows\SysWOW64" });

    [Theory]
    [InlineData(@"C:\Users\Ali\AppData\Local\Temp\setup.log", true)]
    [InlineData(@"C:\Users\Ali\AppData\Local\Temp\sub\x.tmp", true)]
    [InlineData(@"C:\Windows\Temp\a.tmp", true)]
    [InlineData(@"D:\Projeler\temp\tez.docx", false)]
    [InlineData(@"C:\Users\Ali\Documents\cache\notlar.txt", false)]
    [InlineData(@"C:\Users\Ali\AppData\Roaming\Telegram Desktop\tdata\key_datas", false)]
    [InlineData(@"C:\Users\Ali\AppData\Roaming\Telegram Desktop\tdata\user_data\cache\0\ab", true)]
    [InlineData(@"C:\Users\Ali\AppData\Local\Mozilla\Firefox\Profiles\abc.default\cache2\entries\1", true)]
    [InlineData(@"C:\Users\Ali\AppData\Local\Mozilla\Firefox\Profiles\abc.default\places.sqlite", false)]
    [InlineData(@"C:\Users\Ali\AppData\Local\Temp", false)]                 // kökün kendisi
    [InlineData(@"C:\Users\Ali\AppData\Local\Temp\..\important.db", false)] // üst klasöre kaçış
    [InlineData(@"C:\Users\Ali\Desktop\x.txt", false)]                       // "C:\Users" kök sayılmaz
    public void OnlyFilesUnderCategoryRoots_AreInScope(string path, bool expected)
    {
        Assert.Equal(expected, Scope().Contains(path));
    }

    [Fact]
    public void ForbiddenTrees_WinEvenIfARootCoversThem()
    {
        var scope = new CleanupScope(new[] { @"C:\Windows\Logs", @"C:\Windows\System32\LogFiles" },
                                     new[] { @"C:\Windows\System32" });
        Assert.True(scope.Contains(@"C:\Windows\Logs\CBS\CBS.log"));
        Assert.False(scope.Contains(@"C:\Windows\System32\LogFiles\x.log"));
    }

    [Fact]
    public void IntermediateDirectories_ExcludeRootAndFile()
    {
        var dirs = CleanupScope.IntermediateDirectories(@"C:\T\a\b\f.tmp", @"C:\T");
        Assert.Equal(new[] { @"C:\T\a\b", @"C:\T\a" }, dirs);
    }

    [Fact]
    public void RecentFiles_AreNotOldEnough()
    {
        var now = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(CleanupScope.IsOldEnough(now.AddHours(-2), now, TimeSpan.FromHours(24)));
        Assert.True(CleanupScope.IsOldEnough(now.AddDays(-2), now, TimeSpan.FromHours(24)));
        Assert.True(CleanupScope.IsOldEnough(now, now, TimeSpan.Zero));
    }
}
