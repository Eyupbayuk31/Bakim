using Bakım.Core.Safety;
using Bakım.Core.Security;
using Bakım.Core.Startup;
using Bakım.Core.Telemetry;
using Bakım.Core.Text;
using Microsoft.Win32;
using Xunit;

namespace Bakim.Core.Tests;

public class RegistryPathTests
{
    [Theory]
    [InlineData(@"HKCU\Software\Foo", RegistryHive.CurrentUser, @"Software\Foo")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Foo", RegistryHive.CurrentUser, @"Software\Foo")]
    [InlineData(@"CurrentUser\Software\Foo", RegistryHive.CurrentUser, @"Software\Foo")]
    [InlineData(@"HKLM\Software\Foo", RegistryHive.LocalMachine, @"Software\Foo")]
    [InlineData(@"LocalMachine\Software\Foo", RegistryHive.LocalMachine, @"Software\Foo")]
    [InlineData(@"HKEY_LOCAL_MACHINE\Software\Foo\", RegistryHive.LocalMachine, @"Software\Foo")]
    [InlineData(@"HKCR\.txt", RegistryHive.ClassesRoot, ".txt")]
    public void Parse_ResolvesHive(string text, RegistryHive hive, string sub)
    {
        Assert.True(RegistryPath.TryParse(text, RegistryView.Registry64, out var p));
        Assert.Equal(hive, p.Hive);
        Assert.Equal(sub, p.SubKey);
    }

    [Fact]
    public void Hkcu_IsNeverTreatedAsLocalMachine()
    {
        // Eski ayrıştırıcı "HKCU" içinde "Current" aramadığı için HKLM'e düşüyordu.
        var p = RegistryPath.Parse(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.txt");
        Assert.Equal(RegistryHive.CurrentUser, p.Hive);
    }

    [Fact]
    public void ViewSuffix_RoundTrips()
    {
        var p = RegistryPath.Parse(@"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Foo [32]");
        Assert.Equal(RegistryView.Registry32, p.View);
        Assert.Equal(@"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Foo [32]", p.ToDisplay());
        Assert.Equal(@"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\Foo", p.ToRegExe());
        Assert.Equal("/reg:32", p.RegExeViewSwitch);
    }

    [Theory]
    [InlineData("Garbage\\Foo")]
    [InlineData("")]
    public void Parse_RejectsUnknownHive(string text)
    {
        Assert.False(RegistryPath.TryParse(text, RegistryView.Registry64, out _));
    }
}

public class RegistrySafetyGuardTests
{
    [Theory]
    [InlineData(@"HKLM\Software", false)]
    [InlineData(@"HKLM\Software\Microsoft", false)]
    [InlineData(@"HKCU\Software\Classes", false)]
    [InlineData(@"HKLM\Software\Microsoft\Windows\CurrentVersion\Run", false)]
    [InlineData(@"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall", false)]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager", false)]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Services", false)]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Services\FooSvc", true)]
    [InlineData(@"HKLM\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Notify", false)]
    [InlineData(@"HKCU\Software\Google", false)]           // yayıncı kökü: açık izin gerekir
    [InlineData(@"HKCU\Software\Google\Drive", true)]
    [InlineData(@"HKLM\Software\WOW6432Node\VideoLAN", false)]
    [InlineData(@"HKLM\Software\WOW6432Node\VideoLAN\VLC", true)]
    [InlineData(@"HKCR\.txt", false)]                      // uzantı anahtarı paylaşılır
    [InlineData(@"HKCU\Software\Classes\.pdf", false)]
    [InlineData(@"HKCR\VLC.mp4", true)]
    [InlineData(@"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Foo", true)]
    public void CheckKeyDeletion(string key, bool allowed)
    {
        Assert.Equal(allowed, RegistrySafetyGuard.CheckKeyDeletion(RegistryPath.Parse(key)).IsAllowed);
    }

    [Fact]
    public void VendorRoot_AllowedOnlyWithExplicitFlag()
    {
        var p = RegistryPath.Parse(@"HKCU\Software\SomeVendor");
        Assert.False(RegistrySafetyGuard.CheckKeyDeletion(p).IsAllowed);
        Assert.True(RegistrySafetyGuard.CheckKeyDeletion(p, allowVendorRoot: true).IsAllowed);
    }

    [Fact]
    public void ValueDeletion_InRunKey_IsAllowed_ButNotInWinlogon()
    {
        var run = RegistryPath.Parse(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run").WithValue("Foo");
        var winlogon = RegistryPath.Parse(@"HKLM\Software\Microsoft\Windows NT\CurrentVersion\Winlogon").WithValue("Shell");
        Assert.True(RegistrySafetyGuard.CheckValueDeletion(run).IsAllowed);
        Assert.False(RegistrySafetyGuard.CheckValueDeletion(winlogon).IsAllowed);
    }
}

public class CriticalProcessPolicyTests
{
    [Theory]
    [InlineData("winlogon")]
    [InlineData("csrss.exe")]
    [InlineData("DWM")]
    [InlineData("svchost")]
    [InlineData("explorer")]
    public void CriticalNames_AreProtected(string name)
    {
        Assert.True(CriticalProcessPolicy.IsProtected(1234, name, null, @"C:\Windows", currentProcessId: 1));
    }

    [Fact]
    public void AnythingUnderWindows_IsProtected()
    {
        Assert.True(CriticalProcessPolicy.IsProtected(1234, "notepad", @"C:\Windows\System32\notepad.exe", @"C:\Windows", 1));
    }

    [Fact]
    public void RegularApp_IsNotProtected_AndSelfIs()
    {
        Assert.False(CriticalProcessPolicy.IsProtected(1234, "chrome", @"C:\Program Files\Google\Chrome\chrome.exe", @"C:\Windows", 1));
        Assert.True(CriticalProcessPolicy.IsProtected(1234, "chrome", null, @"C:\Windows", 1234));
        Assert.True(CriticalProcessPolicy.IsProtected(4, "System", null, @"C:\Windows", 1));
    }
}

public class TrustedPublishersTests
{
    [Theory]
    [InlineData("Microsoft Corporation", true)]
    [InlineData("microsoft windows", true)]
    [InlineData("Advanced Micro Devices, Inc.", true)]
    [InlineData("Hamdi Yazılım", false)]     // eskiden "AMD" alt dizesiyle güvenilir sayılıyordu
    [InlineData("Intellisoft Ltd", false)]   // "Intel"
    [InlineData("Applet Games", false)]      // "Apple"
    [InlineData("Not Microsoft Corporation", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsTrusted_RequiresExactName(string? signer, bool expected)
    {
        Assert.Equal(expected, TrustedPublishers.IsTrusted(signer));
    }
}

public class StartupApprovedPathsTests
{
    [Fact]
    public void Wow64RunEntries_UseRun32()
    {
        Assert.EndsWith(@"StartupApproved\Run32", StartupApprovedPaths.ForRunKey(@"HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"));
        Assert.EndsWith(@"StartupApproved\Run", StartupApprovedPaths.ForRunKey(@"HKLM\Software\Microsoft\Windows\CurrentVersion\Run"));
        Assert.DoesNotContain("WOW6432Node", StartupApprovedPaths.ForRunKey(@"HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"));
    }

    [Fact]
    public void Value_EncodesStateAndTimestamp()
    {
        var when = new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);
        byte[] disabled = StartupApprovedPaths.BuildValue(false, when);
        Assert.Equal(12, disabled.Length);
        Assert.Equal(0x03, disabled[0]);
        Assert.Equal(when.ToFileTimeUtc(), BitConverter.ToInt64(disabled, 4));
        Assert.False(StartupApprovedPaths.IsEnabled(disabled));

        byte[] enabled = StartupApprovedPaths.BuildValue(true, existing: disabled);
        Assert.Equal(0x02, enabled[0]);
        Assert.Equal(0L, BitConverter.ToInt64(enabled, 4));
        Assert.True(StartupApprovedPaths.IsEnabled(enabled));
        Assert.True(StartupApprovedPaths.IsEnabled(null));
    }
}

public class ByteFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(-5, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1,5 KB")]
    [InlineData(10 * 1024 * 1024 + 300 * 1024, "10,3 MB")]
    [InlineData(250L * 1024 * 1024 * 1024, "250 GB")]
    public void Format_UsesTurkishDecimals(long bytes, string expected)
    {
        Assert.Equal(expected, ByteFormatter.Format(bytes));
    }
}

public class SensorReadingTests
{
    [Fact]
    public void Display_DistinguishesMeasuredEstimatedAndMissing()
    {
        Assert.Equal("—", SensorReading.Unavailable("°C", "Sensör yok").Display);
        Assert.Equal("62 °C", SensorReading.Measured(62.4, "°C", "ACPI").Display);
        Assert.Equal("≈62 °C", new SensorReading(62, SensorQuality.Estimated, "°C", "tahmin").Display);
    }
}

public class RegistryValuePathTests
{
    [Fact]
    public void ValuePath_RoundTrips()
    {
        var p = RegistryPath.Parse(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.mp4\OpenWithProgids").WithValue("VLC.mp4");
        string text = p.ToString();
        Assert.True(RegistryPath.TryParseWithValue(text, RegistryView.Registry64, out var back));
        Assert.Equal(RegistryHive.CurrentUser, back.Hive);
        Assert.Equal("VLC.mp4", back.ValueName);
        Assert.Equal(p.SubKey, back.SubKey);
    }

    [Fact]
    public void KeyWithoutValue_ParsesAsKey()
    {
        Assert.True(RegistryPath.TryParseWithValue(@"HKLM\Software\Foo [32]", RegistryView.Registry64, out var p));
        Assert.Null(p.ValueName);
        Assert.Equal(RegistryView.Registry32, p.View);
    }
}

public class MemoryResultTextTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1024)]
    [InlineData(15L * 1024 * 1024)]
    public void SmallOrNegative_IsNotReportedAsGain(long bytes)
    {
        Assert.False(MemoryResultText.IsSignificant(bytes));
        Assert.Equal("—", MemoryResultText.Badge(bytes));
        Assert.DoesNotContain("MB", MemoryResultText.Describe(bytes));
    }

    [Fact]
    public void RealGain_IsReportedAsApproximate()
    {
        long bytes = 420L * 1024 * 1024;
        Assert.True(MemoryResultText.IsSignificant(bytes));
        Assert.StartsWith("≈420 MB", MemoryResultText.Describe(bytes));
        Assert.Equal("+420 MB", MemoryResultText.Badge(bytes));
    }
}
