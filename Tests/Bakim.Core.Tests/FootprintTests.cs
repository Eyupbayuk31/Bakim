using Bakım.Core.Safety;
using Bakım.Core.Uninstall;
using Xunit;

namespace Bakim.Core.Tests;

public sealed class FootprintTests
{
    private static readonly string Dir = WindowsPath.Normalize(@"C:\Program Files\Foo Corp\Foo")!;
    private static readonly System.Collections.Generic.HashSet<string> Files = new(System.StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Program Files\Foo Corp\Foo\foo.exe",
        @"C:\Program Files\Foo Corp\Foo\bin\updater.exe",
        @"C:\Program Files\Foo Corp\Foobar\foo.exe",
        @"C:\Program Files\Foo Corp\foo.exe",
        @"C:\Windows\System32\rundll32.exe",
    };
    private static bool Exists(string p) => Files.Contains(p);
    private static string NoExpand(string s) => s;

    [Theory]
    [InlineData(@"""C:\Program Files\Foo Corp\Foo\foo.exe"" --tray", true)]
    [InlineData(@"C:\Program Files\Foo Corp\Foo\bin\updater.exe /silent", true)]
    [InlineData(@"C:\Program Files\Foo Corp\Foobar\foo.exe", false)]        // benzer ad, başka klasör
    [InlineData(@"C:\Program Files\Foo Corp\foo.exe", false)]               // üst klasör
    [InlineData(@"""C:\Windows\System32\rundll32.exe"" ""C:\Program Files\Foo Corp\Foo\x.dll"",Run", false)] // yol yalnızca argümanda
    [InlineData(@"MsiExec.exe /X{11111111-2222-3333-4444-555555555555}", false)]
    [InlineData("", false)]
    public void PointsInto(string command, bool expected) =>
        Assert.Equal(expected, FootprintMatch.PointsInto(command, Dir, Exists, NoExpand));

    [Theory]
    [InlineData(@"C:\Program Files\Foo Corp\Foo", true)]
    [InlineData(@"C:\Games\Foo", true)]
    [InlineData(@"C:\Program Files", false)]
    [InlineData(@"C:\Program Files (x86)", false)]
    [InlineData(@"C:\", false)]
    [InlineData(@"D:\Foo", false)]
    [InlineData(@"C:\Users\ali\AppData\Local\Programs", false)] // paylaşılan: birçok uygulama
    [InlineData(@"C:\Users\ali\AppData\Local\Programs\foo", true)]
    [InlineData(@"C:\Users\ali", false)]                          // profil kökü
    public void IsUsableInstallDir(string dir, bool expected) =>
        Assert.Equal(expected, FootprintMatch.IsUsableInstallDir(WindowsPath.Normalize(dir)));

    [Fact]
    public void FirewallRule_FieldsAreParsed()
    {
        var f = FootprintText.ParseFirewallRule(@"v2.30|Action=Allow|Active=TRUE|Dir=In|App=C:\Program Files\Foo\foo.exe|Name=Foo In|app=ignored|");
        Assert.Equal(@"C:\Program Files\Foo\foo.exe", f["App"]);
        Assert.Equal("Foo In", f["name"]);
        Assert.Equal("Allow", f["Action"]);
        Assert.False(f.ContainsKey("v2.30"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(@"\??\C:\Program Files\Foo\drv.sys", @"C:\Program Files\Foo\drv.sys")]
    [InlineData(@"\SystemRoot\System32\drivers\x.sys", @"%SystemRoot%\System32\drivers\x.sys")]
    [InlineData(@"""C:\Program Files\Foo\svc.exe"" -k", @"""C:\Program Files\Foo\svc.exe"" -k")]
    public void ServiceImagePath_IsNormalized(string? raw, string expected) =>
        Assert.Equal(expected, FootprintText.NormalizeServiceImagePath(raw));

    [Fact]
    public void DriverImagePath_PointsIntoInstallDir()
    {
        string image = FootprintText.NormalizeServiceImagePath(@"\??\C:\Program Files\Foo\drv.sys");
        Assert.True(FootprintMatch.PointsInto(image, @"C:\Program Files\Foo", p => p == @"C:\Program Files\Foo\drv.sys"));
    }

    private static readonly string[] Roots = { @"C:\Program Files", @"C:\Program Files (x86)", @"C:\Users\ali\AppData\Local\Programs" };

    [Theory]
    [InlineData(@"""C:\Program Files\Foo\unins000.exe""", @"C:\Program Files\Foo")]
    [InlineData(@"""C:\Program Files (x86)\Foo\uninstall\uninst.exe"" /S", @"C:\Program Files (x86)\Foo")]
    [InlineData(@"""C:\Users\ali\AppData\Local\Programs\bar\Uninstall Bar.exe""", @"C:\Users\ali\AppData\Local\Programs\bar")]
    [InlineData(@"""C:\Program Files\uninst.exe""", null)]                              // kökün kendisi
    [InlineData(@"C:\Windows\SysWOW64\RunDll32.EXE C:\x.dll,Uninstall", null)]           // kabuk / Windows
    [InlineData(@"MsiExec.exe /X{12345678-1234-1234-1234-123456789012}", null)]          // MSI
    [InlineData(@"""C:\ProgramData\Package Cache\{abc}\setup.exe"" /uninstall", null)] // kök dışı
    [InlineData(null, null)]
    public void InferInstallDir_FromUninstaller(string? uninstall, string? expected) =>
        Assert.Equal(expected, FootprintMatch.InferInstallDir(null, uninstall, Roots, _ => false, s => s));

    [Fact]
    public void InferInstallDir_PrefersRegisteredLocation() =>
        Assert.Equal(@"C:\Games\Foo", FootprintMatch.InferInstallDir(@"""C:\Games\Foo\""", @"""C:\Program Files\Bar\unins000.exe""", Roots, _ => false));
}

