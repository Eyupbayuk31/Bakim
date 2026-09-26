using System;
using System.Collections.Generic;
using Bakım.Core.Sentinel;
using Bakım.Models;
using Bakım.Services.Sentinel.Detection;
using Bakım.Services.Sentinel.Sensors;
using Xunit;

namespace Bakim.Tests;

/// <summary>Nöbetçi v3 Faz A: kayıt defteri eşleşmesi, sınıflandırıcı adı, bildirim kuralı.</summary>
public sealed class SentinelFazAWindowsTests
{
    [Theory]
    [InlineData(@"HKLM\Software\Microsoft\Windows\CurrentVersion\Run\Foo", true)]
    [InlineData(@"HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce\Foo", true)]
    [InlineData(@"HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run\Foo32", true)]
    [InlineData(@"HKLM\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run\Pol", true)]
    [InlineData(@"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\RuneLite", false)]
    [InlineData(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\RunAsDate", false)]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Services\Runner", false)]
    [InlineData(null, false)]
    public void RunValueKey_IsExactPrefix(string? key, bool expected) =>
        Assert.Equal(expected, RegistryHotspotSensor.IsRunValueKey(key));

    [Fact]
    public void Delta_UninstallKeyStartingWithRun_IsNotStartupEntry()
    {
        var pre = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var post = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [@"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\RuneLite"] = "RuneLite",
            [@"HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run\Updater32"] = @"C:\x\u.exe",
            [@"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Services Helper"] = "Services Helper",
        };

        var (records, services, startups) = RegistryHotspotSensor.ComputeDelta(pre, post);

        Assert.Equal(3, records.Count);
        Assert.Empty(services);
        Assert.Equal(new[] { @"HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run\Updater32" }, startups);
    }

    [Fact]
    public void HotspotSnapshot_Has32BitEntriesOnlyUnderWow6432Node()
    {
        // 32 bit görünüm artık ayrıca okunmuyor: WOW6432Node değerleri 64 bit anahtar adıyla tekrarlanmaz.
        var snapshot = RegistryHotspotSensor.CaptureHotspotSnapshot();
        using var wow = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64)
            .OpenSubKey(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run");
        using var native = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64)
            .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        var nativeNames = new HashSet<string>(native?.GetValueNames() ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (string name in wow?.GetValueNames() ?? Array.Empty<string>())
        {
            Assert.True(snapshot.ContainsKey($@"HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run\{name}"));
            if (!nativeNames.Contains(name))
                Assert.False(snapshot.ContainsKey($@"HKLM\Software\Microsoft\Windows\CurrentVersion\Run\{name}"));
        }
    }

    [Fact]
    public void Classifier_UsesAppName_NotFrameworkName()
    {
        bool isInstaller = InstallerClassifier.ClassifyProcess("vlc-3.0.20-win64.exe", @"C:\Users\u\Downloads\vlc-3.0.20-win64.exe",
            null, "VLC media player", "7-Zip SFX", null, out var kind, out string name, out _, extraScore: 60);

        Assert.True(isInstaller);
        Assert.Equal(SessionKind.Install, kind);
        Assert.Equal("VLC media player", name);
    }

    [Fact]
    public void Notification_RiskyReportWithoutFiles_IsReportable()
    {
        var empty = new SetupDeltaReport();
        Assert.False(SetupRiskPresentation.HasReportableChanges(empty));

        var tempOnly = new SetupDeltaReport { TempFileCount = 42, ModifiedFiles = new List<string> { @"C:\x\cookies" } };
        Assert.False(SetupRiskPresentation.HasReportableChanges(tempOnly));

        var certOnly = new SetupDeltaReport { RiskEvaluated = true, RiskVerdict = RiskVerdict.Dangerous };
        Assert.True(SetupRiskPresentation.HasReportableChanges(certOnly));

        var runOnly = new SetupDeltaReport { AddedStartupEntries = new List<string> { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run\x" } };
        Assert.True(SetupRiskPresentation.HasReportableChanges(runOnly));

        var files = new SetupDeltaReport { CreatedFiles = new List<string> { @"C:\Program Files\A\a.exe" } };
        Assert.True(SetupRiskPresentation.HasReportableChanges(files));
    }
}
