using System;
using System.Collections.Generic;
using System.Linq;
using Bakım.Core.Uninstall;
using Xunit;

namespace Bakim.Core.Tests;

public class UninstallWatchRulesTests
{
    private static UninstallSnapshotEntry E(string key, string name, string? dir = null, bool system = false) => new(key, name, dir, system);

    [Fact]
    public void ExternalUninstall_IsReported()
    {
        var before = new[] { E("K1", "7-Zip 24.08 (x64)", @"C:\Program Files\7-Zip\"), E("K2", "Discord") };
        var after = new[] { E("K2", "Discord") };
        var removed = UninstallWatchRules.Removed(before, after);
        Assert.Equal("K1", Assert.Single(removed).KeyPath);
    }

    [Fact]
    public void MsiUpgrade_WithNewProductCode_IsNotUninstall()
    {
        var before = new[] { E("{OLD}", "Everything 1.4.1.1032 (x64)") };
        var after = new[] { E("{NEW}", "Everything 1.4.1.1035 (x64)") };
        Assert.Empty(UninstallWatchRules.Removed(before, after));
    }

    [Fact]
    public void Reinstall_SameFolderDifferentName_IsNotUninstall()
    {
        var before = new[] { E("A", "Foo Suite", @"C:\Program Files\Foo") };
        var after = new[] { E("B", "Foo Suite Pro", @"C:\Program Files\Foo\") };
        Assert.Empty(UninstallWatchRules.Removed(before, after));
    }

    [Fact]
    public void SystemComponents_AndBakimInitiated_AreSkipped()
    {
        var now = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        var before = new[] { E("S", "Microsoft VC++ Runtime", system: true), E("B", "Bakımla kaldırılan"), E("OLD", "Eski işaret") };
        var marks = new Dictionary<string, DateTime> { ["B"] = now.AddMinutes(-3), ["OLD"] = now.AddHours(-5) };
        var removed = UninstallWatchRules.Removed(before, Array.Empty<UninstallSnapshotEntry>(), marks, now);
        Assert.Equal(new[] { "OLD" }, removed.Select(r => r.KeyPath));
    }

    [Theory]
    [InlineData("Everything 1.4.1.1032 (x64)", "everything")]
    [InlineData("Notepad++ (64-bit x64)", "notepad++")]
    [InlineData("Microsoft Visual Studio Code (User)", "microsoft visual studio code (user)")]
    [InlineData("Git version 2.46.0", "git version")]
    [InlineData("VLC media player v3.0.21 x64", "vlc media player")]
    public void NameStem_DropsVersionAndArch(string name, string stem) => Assert.Equal(stem, UninstallWatchRules.NameStem(name));

    [Fact]
    public void Fingerprint_IsOrderAndCaseIndependent() =>
        Assert.Equal(UninstallWatchRules.Fingerprint(new[] { "B", "a" }), UninstallWatchRules.Fingerprint(new[] { "A", "b" }));
}
