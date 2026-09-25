using Bakım.Core.Uninstall;
using Xunit;

namespace Bakim.Core.Tests;

public class UninstallCommandParserTests
{
    private static readonly HashSet<string> Existing = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Program Files (x86)\Foo Bar\uninst.exe",
        @"C:\Program Files\Notepad++\uninstall.exe",
        @"C:\Program Files\VS\unins000.exe",
        @"C:\Program Files\NoExt\remove",
    };

    private static bool Exists(string p) => Existing.Contains(p);
    private static string Identity(string s) => s;

    private static ParsedUninstallCommand P(string cmd) => UninstallCommandParser.Parse(cmd, Exists, Identity)!;

    [Theory]
    [InlineData("MsiExec.exe /I{23170F69-40C1-2702-2201-000001000000}", "/X{23170F69-40C1-2702-2201-000001000000}")]
    [InlineData("MsiExec.exe /X{23170f69-40c1-2702-2201-000001000000}", "/X{23170F69-40C1-2702-2201-000001000000}")]
    [InlineData(@"""C:\Windows\System32\msiexec.exe"" /i {23170F69-40C1-2702-2201-000001000000} REBOOT=R", "/X{23170F69-40C1-2702-2201-000001000000}")]
    public void Msi_IsRebuiltFromProductCode(string cmd, string expectedArgs)
    {
        var p = P(cmd);
        Assert.Equal("msiexec.exe", p.FileName);
        Assert.Equal(expectedArgs, p.Arguments);
        Assert.True(p.IsMsi);
    }

    [Fact]
    public void UnquotedPathWithSpaces_IsResolvedByProbing()
    {
        var p = P(@"C:\Program Files (x86)\Foo Bar\uninst.exe /S");
        Assert.Equal(@"C:\Program Files (x86)\Foo Bar\uninst.exe", p.FileName);
        Assert.Equal("/S", p.Arguments);
    }

    [Fact]
    public void UnquotedPath_NotOnDisk_FallsBackToExeBoundary()
    {
        var p = P(@"C:\Program Files\Gone App\uninstall.exe --mode silent");
        Assert.Equal(@"C:\Program Files\Gone App\uninstall.exe", p.FileName);
        Assert.Equal("--mode silent", p.Arguments);
    }

    [Fact]
    public void QuotedPath_IsSplitAtClosingQuote()
    {
        var p = P(@"""C:\Users\x\AppData\Local\Discord\Update.exe"" --uninstall");
        Assert.Equal(@"C:\Users\x\AppData\Local\Discord\Update.exe", p.FileName);
        Assert.Equal("--uninstall", p.Arguments);
    }

    [Fact]
    public void ExtensionlessFile_IsFound()
    {
        var p = P(@"C:\Program Files\NoExt\remove /quiet");
        Assert.Equal(@"C:\Program Files\NoExt\remove", p.FileName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_ReturnsNull(string cmd)
    {
        Assert.Null(UninstallCommandParser.Parse(cmd, Exists, Identity));
    }

    [Theory]
    [InlineData(@"""C:\Program Files\VS\unins000.exe""", null, InstallerFamily.InnoSetup)]
    [InlineData(@"""C:\Program Files\Notepad++\uninstall.exe""", null, InstallerFamily.Nsis)]
    [InlineData(@"""C:\Users\x\AppData\Local\Discord\Update.exe"" --uninstall", null, InstallerFamily.Squirrel)]
    [InlineData(@"""C:\ProgramData\Package Cache\{A}\setup.exe"" /uninstall", null, InstallerFamily.WixBurn)]
    [InlineData(@"""C:\Program Files (x86)\Steam\steam.exe"" steam://uninstall/730", "Steam App 730", InstallerFamily.Steam)]
    [InlineData("MsiExec.exe /I{23170F69-40C1-2702-2201-000001000000}", null, InstallerFamily.Msi)]
    [InlineData(@"""C:\Program Files\Foo\foo.exe"" -remove", null, InstallerFamily.GenericExe)]
    public void DetectFamily_RecognisesInstallers(string cmd, string? keyName, InstallerFamily expected)
    {
        var p = P(cmd);
        Assert.Equal(expected, UninstallCommandParser.DetectFamily(p, keyName, Exists));
    }

    [Fact]
    public void SilentArguments_PerFamily()
    {
        var msi = P("MsiExec.exe /I{23170F69-40C1-2702-2201-000001000000}");
        Assert.Equal("/X{23170F69-40C1-2702-2201-000001000000} /qn /norestart",
            UninstallCommandParser.BuildSilentArguments(msi, InstallerFamily.Msi));

        var inno = P(@"""C:\Program Files\VS\unins000.exe""");
        Assert.Equal("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
            UninstallCommandParser.BuildSilentArguments(inno, InstallerFamily.InnoSetup));

        var nsis = P(@"""C:\Program Files\Notepad++\uninstall.exe""");
        Assert.Equal(@"/S _?=C:\Program Files\Notepad++",
            UninstallCommandParser.BuildSilentArguments(nsis, InstallerFamily.Nsis));

        var generic = P(@"""C:\Program Files\Foo\foo.exe"" -remove");
        Assert.Null(UninstallCommandParser.BuildSilentArguments(generic, InstallerFamily.GenericExe));
        Assert.Null(UninstallCommandParser.BuildSilentArguments(generic, InstallerFamily.InstallShield));
    }

    [Fact]
    public void ExistingSwitches_AreNotDuplicated()
    {
        var inno = P(@"""C:\Program Files\VS\unins000.exe"" /verysilent");
        Assert.Equal("/verysilent /SUPPRESSMSGBOXES /NORESTART",
            UninstallCommandParser.BuildSilentArguments(inno, InstallerFamily.InnoSetup));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1605, true)]
    [InlineData(3010, true)]
    [InlineData(1602, false)]
    [InlineData(1603, false)]
    public void MsiExitCodes(int code, bool success)
    {
        Assert.Equal(success, UninstallCommandParser.IsMsiSuccess(code));
    }
}
