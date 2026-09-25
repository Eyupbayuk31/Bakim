using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bakım.Core.Sentinel;
using Xunit;

namespace Bakim.Core.Tests;

/// <summary>Nöbetçi v2 (Faz 7): çatı tanıma, MOTW, gürültü, sistem durumu farkı, risk motoru.</summary>
public class SentinelV2Tests
{
    private static byte[] Pe(params string[] markers)
    {
        var bytes = new List<byte> { (byte)'M', (byte)'Z' };
        bytes.AddRange(new byte[64]);
        foreach (var m in markers) bytes.AddRange(Encoding.ASCII.GetBytes(m));
        return bytes.ToArray();
    }

    [Theory]
    [InlineData("Inno Setup Setup Data (6.2.0)", InstallerFramework.InnoSetup)]
    [InlineData("xxNullsoftInstyy", InstallerFramework.Nsis)]
    [InlineData(".wixburn", InstallerFramework.WixBurn)]
    [InlineData("Advanced Installer 20", InstallerFramework.AdvancedInstaller)]
    [InlineData("plain program", InstallerFramework.Unknown)]
    public void Fingerprint_FromHead(string marker, InstallerFramework expected) =>
        Assert.Equal(expected, InstallerFingerprint.Detect(Pe(marker), ReadOnlySpan<byte>.Empty));

    [Fact]
    public void Fingerprint_FromTailOverlay()
    {
        byte[] tail = Encoding.ASCII.GetBytes("....NullsoftInst....");
        Assert.Equal(InstallerFramework.Nsis, InstallerFingerprint.Detect(Pe("nothing"), tail));
    }

    [Fact]
    public void Fingerprint_RequiresPe_ExceptMsi()
    {
        byte[] notPe = Encoding.ASCII.GetBytes("Inno Setup Setup Data");
        Assert.Equal(InstallerFramework.Unknown, InstallerFingerprint.Detect(notPe, ReadOnlySpan<byte>.Empty));
        byte[] ole = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0 };
        Assert.Equal(InstallerFramework.Msi, InstallerFingerprint.Detect(ole, ReadOnlySpan<byte>.Empty, "a.msi"));
        Assert.False(InstallerFingerprint.IsPortableExecutable(ole));
        Assert.True(InstallerFingerprint.IsPortableExecutable(Pe()));
    }

    [Fact]
    public void Motw_IsParsed()
    {
        var motw = MarkOfTheWeb.Parse("[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://www.7-zip.org/download.html\r\nHostUrl=https://www.7-zip.org/a/7z2408-x64.exe\r\n");
        Assert.NotNull(motw);
        Assert.True(motw!.IsFromInternet);
        Assert.Equal("www.7-zip.org", motw.SiteName);
        Assert.Null(MarkOfTheWeb.Parse("[ZoneTransfer]\r\n"));
        Assert.False(MarkOfTheWeb.Parse("ZoneId=1")!.IsFromInternet);
    }

    [Theory]
    [InlineData(@"C:\Users\a\AppData\Local\Google\Chrome\User Data\Default\Cache\f_001", true)]
    [InlineData(@"C:\Users\a\AppData\Local\Microsoft\Windows\INetCache\x", true)]
    [InlineData(@"C:\Windows\Prefetch\SETUP.EXE-1.pf", true)]
    [InlineData(@"C:\Users\a\AppData\Local\Bakim\logs\x.log", true)]
    [InlineData(@"C:\Program Files\7-Zip\7z.exe", false)]
    [InlineData(@"C:\Users\a\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\7-Zip\7-Zip.lnk", false)]
    [InlineData(null, true)]
    public void Noise_Paths(string? path, bool noise) => Assert.Equal(noise, SentinelNoise.IsNoisePath(path));

    [Fact]
    public void Snapshot_Diff_AddedModifiedRemoved_AndUnreadableAreas()
    {
        var pre = new Dictionary<string, string>
        {
            [SystemStateSnapshot.MakeKey(SystemArea.EnvironmentPath, @"HKCU\Environment\Path")] = @"C:\a",
            [SystemStateSnapshot.MakeKey(SystemArea.Hosts, "0.0.0.0 old.example")] = "",
        };
        var post = new Dictionary<string, string>
        {
            [SystemStateSnapshot.MakeKey(SystemArea.EnvironmentPath, @"HKCU\Environment\Path")] = @"C:\a;C:\b",
            [SystemStateSnapshot.MakeKey(SystemArea.RootCertificate, "ABC")] = "CN=Evil Root",
            [SystemStateSnapshot.MakeKey(SystemArea.DefenderExclusion, @"C:\x")] = "0",
        };
        var preAreas = SystemStateSnapshot.AreasOf(pre.Keys, new[] { SystemArea.RootCertificate });
        var postAreas = SystemStateSnapshot.AreasOf(post.Keys, new[] { SystemArea.Hosts });

        var diff = SystemStateSnapshot.Diff(pre, post, preAreas, postAreas);

        Assert.Contains(diff, c => c.Area == SystemArea.EnvironmentPath && c.Kind == ChangeKind.Modified);
        Assert.Contains(diff, c => c.Area == SystemArea.RootCertificate && c.Kind == ChangeKind.Added);
        Assert.Contains(diff, c => c.Area == SystemArea.Hosts && c.Kind == ChangeKind.Removed);
        // Önceden okunamayan alan (Defender, yönetici gerektirir) "eklendi" sayılmaz.
        Assert.DoesNotContain(diff, c => c.Area == SystemArea.DefenderExclusion);
    }

    [Fact]
    public void Risk_CleanInstall()
    {
        var result = SetupRiskEngine.Evaluate(new SetupRiskInput { InstallerSigned = true });
        Assert.Equal(RiskVerdict.Clean, result.Verdict);
        Assert.Equal(0, result.Score);
    }

    [Fact]
    public void Risk_SignedAppWithRunEntry_IsInfo()
    {
        var input = new SetupRiskInput { InstallerSigned = true };
        input.Startup.Add(new StartupAddition("Foo", @"""C:\Program Files\Foo\foo.exe"" --tray", @"C:\Program Files\Foo\foo.exe", true));
        var result = SetupRiskEngine.Evaluate(input);
        Assert.Equal(RiskVerdict.Info, result.Verdict);
        Assert.Single(result.Top(3));
    }

    [Fact]
    public void Risk_RootCertAndDefenderExclusion_IsDangerous()
    {
        var input = new SetupRiskInput { InstallerSigned = false, InstallerFromInternet = true };
        input.SystemChanges.Add(new SystemChange(SystemArea.RootCertificate, ChangeKind.Added, "ABC", null, "CN=Evil Root"));
        input.SystemChanges.Add(new SystemChange(SystemArea.DefenderExclusion, ChangeKind.Added, @"C:\ProgramData\x", null, "0"));
        var result = SetupRiskEngine.Evaluate(input);
        Assert.Equal(RiskVerdict.Dangerous, result.Verdict);
        Assert.Equal(RiskSeverity.Critical, result.Findings[0].Severity);
        Assert.Contains(result.Findings, f => f.Technique == "T1553.004");
    }

    [Fact]
    public void Risk_TempRunEntry_AndBundle_AndInboundRule()
    {
        var input = new SetupRiskInput { InstallerSigned = false };
        input.Startup.Add(new StartupAddition("upd", @"C:\Users\a\AppData\Local\Temp\upd.exe", @"C:\Users\a\AppData\Local\Temp\upd.exe", null));
        input.NewPrograms.AddRange(new[] { "Foo", "Toolbar X", "Cleaner Y" });
        input.SystemChanges.Add(new SystemChange(SystemArea.FirewallRule, ChangeKind.Added, "{1}", null,
            @"v2.30|Action=Allow|Active=TRUE|Dir=In|Protocol=6|LPort=8080|App=C:\Foo\foo.exe|Name=Foo|"));
        var result = SetupRiskEngine.Evaluate(input);
        Assert.Equal(20 + 8 + 20, result.Score);
        Assert.Equal(RiskVerdict.Suspicious, result.Verdict);
        Assert.Contains(result.Findings, f => f.Title.Contains("3 program"));
        Assert.Contains(result.Findings, f => f.Detail.Contains("port 8080"));
    }

    [Fact]
    public void Risk_UnsignedExeInSystem32_AndHiddenPe()
    {
        var input = new SetupRiskInput { WindowsDirectory = @"C:\Windows" };
        input.Executables.Add(new ExecutableDrop(@"C:\Windows\System32\svch0st.exe", false, false));
        input.Executables.Add(new ExecutableDrop(@"C:\Program Files\Foo\data.dat", null, true));
        input.Executables.Add(new ExecutableDrop(@"C:\Program Files\Foo\foo.exe", false, false)); // olağan: bulgu yok
        var result = SetupRiskEngine.Evaluate(input);
        Assert.Equal(2, result.Findings.Count);
        Assert.Equal(RiskSeverity.Critical, result.Findings[0].Severity);
    }

    [Theory]
    [InlineData(@"C:\Users\a\AppData\Local\Temp\x.exe", true)]
    [InlineData(@"""C:\ProgramData\updater.exe"" /silent", true)]
    [InlineData(@"C:\ProgramData\Foo\updater.exe", false)]
    [InlineData(@"C:\Users\a\AppData\Roaming\x.exe", true)]
    [InlineData(@"C:\Users\a\AppData\Roaming\Foo\x.exe", false)]
    [InlineData(@"C:\Program Files\Foo\foo.exe", false)]
    public void SuspiciousLocations(string path, bool expected) =>
        Assert.Equal(expected, SetupRiskEngine.IsSuspiciousLocation(path));

    [Fact]
    public void PathChange_ListsAdditions()
    {
        var input = new SetupRiskInput();
        input.SystemChanges.Add(new SystemChange(SystemArea.EnvironmentPath, ChangeKind.Modified, "HKCU\\Environment\\Path", @"C:\a", @"C:\a;C:\Tools\bin"));
        var f = SetupRiskEngine.Evaluate(input).Findings.Single();
        Assert.Contains(@"C:\Tools\bin", f.Detail);
    }
}

public class SetupTraceTests
{
    [Theory]
    [InlineData(@"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\7-Zip", "HKLM|7-ZIP")]
    [InlineData(@"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\7-Zip [32]", "HKLM|7-ZIP")]
    [InlineData(@"HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{ABC}\DisplayName", "HKLM|{ABC}")]
    [InlineData(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Foo", "HKCU|FOO")]
    [InlineData(@"HKLM\Software\Foo", null)]
    [InlineData(null, null)]
    public void Identity(string? key, string? expected) => Assert.Equal(expected, SetupTrace.UninstallIdentity(key));

    [Fact]
    public void TopLevelFolders()
    {
        var top = SetupTrace.TopLevelCreatedFolders(new[]
        {
            @"C:\Program Files\Foo", @"C:\Program Files\Foo\bin", @"C:\Program Files\Foo\bin\x",
            @"C:\Users\a\AppData\Roaming\Foo\", @"C:\Users\a\AppData\Roaming\Foo\cache"
        });
        Assert.Equal(new[] { @"C:\Program Files\Foo", @"C:\Users\a\AppData\Roaming\Foo" }, top);
    }
}

public class SentinelNoiseFilterTests
{
    [Theory]
    [InlineData(@"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan", @"%systemroot%\system32\usoclient.exe StartScan", true)]
    [InlineData(@"\Microsoft\Windows\Defrag\ScheduledDefrag", "", true)]
    [InlineData(@"\GoogleUpdateTaskMachineUA", @"C:\Program Files (x86)\Google\Update\GoogleUpdate.exe /ua", false)]
    [InlineData(@"\Microsoft\Windows\Evil", @"C:\ProgramData\x.exe", false)]
    public void WindowsTaskNoise(string key, string action, bool noise) =>
        Assert.Equal(noise, SystemStateSnapshot.IsLikelyWindowsNoise(new SystemChange(SystemArea.ScheduledTask, ChangeKind.Added, key, null, action)));

    [Fact]
    public void Bundle_IgnoresRuntimes()
    {
        var input = new SetupRiskInput();
        input.NewPrograms.AddRange(new[] { "Cool Game", "Microsoft Visual C++ 2015-2022 Redistributable (x64)", "DirectX 9 Runtime" });
        Assert.DoesNotContain(SetupRiskEngine.Evaluate(input).Findings, f => f.Title.Contains("program yükledi"));

        input.NewPrograms.Add("Some Toolbar");
        Assert.Contains(SetupRiskEngine.Evaluate(input).Findings, f => f.Title.Contains("2 program"));
    }
}

public class FindingTargetTests
{
    [Fact]
    public void RegistryValue_WithView()
    {
        Assert.True(FindingTarget.TryParseValue(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\notepad.exe\Debugger [32]", out var p));
        Assert.Equal(Microsoft.Win32.RegistryHive.LocalMachine, p.Hive);
        Assert.Equal(Microsoft.Win32.RegistryView.Registry32, p.View);
        Assert.Equal("Debugger", p.ValueName);
        Assert.EndsWith(@"notepad.exe", p.SubKey);

        Assert.True(FindingTarget.TryParseValue(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Updater", out var run));
        Assert.Equal(Microsoft.Win32.RegistryHive.CurrentUser, run.Hive);
        Assert.Equal("Updater", run.ValueName);

        Assert.False(FindingTarget.TryParseValue(@"HKU\x\y", out _));
        Assert.False(FindingTarget.TryParseValue("HKLM", out _));
    }

    [Fact]
    public void Certificate_AndDefender()
    {
        Assert.True(FindingTarget.TryParseCertificate(@"HKLM\SOFTWARE\Microsoft\SystemCertificates\Root\Certificates\0123456789ABCDEF0123456789ABCDEF01234567", out var thumb, out bool machine));
        Assert.True(machine);
        Assert.Equal(40, thumb.Length);
        Assert.False(FindingTarget.TryParseCertificate(@"HKCU\x\Root\Certificates\nothex", out _, out _));

        Assert.True(FindingTarget.TryParseDefenderExclusion(@"Paths: C:\ProgramData\x", out var parameter, out var value));
        Assert.Equal("ExclusionPath", parameter);
        Assert.Equal(@"C:\ProgramData\x", value);
    }

    [Fact]
    public void Findings_CarryActions()
    {
        var input = new SetupRiskInput();
        input.Startup.Add(new StartupAddition("upd", "x.exe", null, false, @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run\upd"));
        input.Services.Add(new ServiceAddition("svc", @"C:\x\svc.exe", false, true, false));
        input.SystemChanges.Add(new SystemChange(SystemArea.RootCertificate, ChangeKind.Added,
            @"HKLM\SOFTWARE\Policies\Microsoft\SystemCertificates\Root\Certificates\0123456789ABCDEF0123456789ABCDEF01234567", null, "CN=x"));
        var findings = SetupRiskEngine.Evaluate(input).Findings;
        Assert.Contains(findings, f => f.Action == FindingAction.RemoveStartupValue && f.Target!.EndsWith(@"\upd"));
        Assert.Contains(findings, f => f.Action == FindingAction.DisableService && f.Target == "svc");
        // Grup İlkesi sertifikası elle kaldırılamaz: eylem sunulmaz.
        Assert.Contains(findings, f => f.Title.Contains("sertifika") && f.Action == FindingAction.None);
    }
}
