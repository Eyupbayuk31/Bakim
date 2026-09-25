using Bakım.Core.Safety;
using Xunit;

namespace Bakim.Core.Tests;

public class PathSafetyGuardTests
{
    // Gerçek bir Windows kurulumunu taklit eden sabit klasör kümesi:
    // testler Linux'ta da aynı sonucu verir.
    private static readonly KnownFolderSet Folders = new()
    {
        ExactProtected = Norm(
            @"C:\Program Files", @"C:\Program Files (x86)", @"C:\Program Files\Common Files",
            @"C:\ProgramData", @"C:\Users", @"C:\Users\ali", @"C:\Users\Public",
            @"C:\Users\ali\Desktop", @"C:\Users\ali\Documents", @"C:\Users\ali\Downloads",
            @"C:\Users\ali\AppData", @"C:\Users\ali\AppData\Local", @"C:\Users\ali\AppData\Roaming",
            @"C:\Users\ali\AppData\LocalLow", @"C:\Users\ali\AppData\Local\Programs",
            @"C:\Users\ali\AppData\Local\Temp",
            @"C:\Users\ali\AppData\Roaming\Microsoft\Windows\Start Menu\Programs",
            @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs"),
        TreeProtected = Norm(
            @"C:\Windows", @"C:\Program Files\WindowsApps", @"C:\ProgramData\Microsoft",
            @"C:\ProgramData\Package Cache", @"C:\Users\ali\AppData\Local\Microsoft\Windows",
            @"C:\Users\ali\AppData\Roaming\Microsoft\Windows", @"C:\Program Files\Bakim",
            @"C:\Users\ali\AppData\Roaming\Bakım"),
        TreeExceptions = Norm(
            @"C:\Users\ali\AppData\Roaming\Microsoft\Windows\Start Menu\Programs",
            @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs"),
        DeletableRoots = Norm(
            @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData",
            @"C:\Users\ali\AppData\Local", @"C:\Users\ali\AppData\Roaming", @"C:\Users\ali\AppData\LocalLow",
            @"C:\Users\ali\AppData\Local\Programs", @"C:\Users\ali\AppData\Local\Temp",
            @"C:\Users\ali\Documents", @"C:\Users\ali\Desktop", @"C:\Users\ali\Downloads", @"C:\Users\ali",
            @"C:\Users\ali\AppData\Roaming\Microsoft\Windows\Start Menu\Programs",
            @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs"),
    };

    private static IReadOnlyList<string> Norm(params string[] p) => p.Select(x => WindowsPath.Normalize(x)!).ToList();

    private static readonly PathSafetyGuard Guard = new(Folders);

    [Theory]
    [InlineData(@"C:\", true, PathVerdict.ProtectedExact)]
    [InlineData(@"D:\", true, PathVerdict.ProtectedExact)]
    [InlineData(@"C:\Windows", true, PathVerdict.ProtectedTree)]
    [InlineData(@"C:\Windows\System32\foo.dll", false, PathVerdict.ProtectedTree)]
    [InlineData(@"C:\Windows\Installer\abc.msi", false, PathVerdict.ProtectedTree)]
    [InlineData(@"C:\Program Files", true, PathVerdict.ProtectedExact)]
    [InlineData(@"C:\Program Files (x86)", true, PathVerdict.ProtectedExact)]
    [InlineData(@"C:\Program Files\Foo", true, PathVerdict.Allowed)]
    [InlineData(@"C:\Program Files\WindowsApps\X", true, PathVerdict.ProtectedTree)]
    [InlineData(@"C:\Program Files\Common Files", true, PathVerdict.ProtectedExact)]
    [InlineData(@"C:\Users\ali\Downloads", true, PathVerdict.ProtectedExact)]
    [InlineData(@"C:\Users\ali\Downloads\app.exe", false, PathVerdict.Allowed)]
    [InlineData(@"C:\Users\ali\Desktop", true, PathVerdict.ProtectedExact)]
    [InlineData(@"C:\Users\ali\AppData\Local", true, PathVerdict.ProtectedExact)]
    [InlineData(@"C:\Users\ali\AppData\Local\Programs", true, PathVerdict.ProtectedExact)]
    [InlineData(@"C:\Users\ali\AppData\Local\Programs\Microsoft VS Code", true, PathVerdict.Allowed)]
    [InlineData(@"C:\Users\ali\AppData\Local\Microsoft\Windows\X", true, PathVerdict.ProtectedTree)]
    [InlineData(@"C:\Users\ali\AppData\Roaming\Bakım\x.json", false, PathVerdict.ProtectedTree)]
    [InlineData(@"C:\ProgramData\Microsoft\Windows Defender", true, PathVerdict.ProtectedTree)]
    [InlineData(@"C:\ProgramData\Package Cache\{guid}", true, PathVerdict.ProtectedTree)]
    [InlineData(@"C:\Users", true, PathVerdict.ProtectedExact)]
    [InlineData(@"C:\Users\ali", true, PathVerdict.ProtectedExact)]
    [InlineData(@"C:\System Volume Information\x", false, PathVerdict.ProtectedTree)]
    [InlineData(@"D:\$Recycle.Bin\S-1-5", true, PathVerdict.ProtectedTree)]
    [InlineData(@"C:\pagefile.sys", false, PathVerdict.TooShallow)]
    [InlineData(@"\\server\share\x", true, PathVerdict.Invalid)]
    [InlineData(@"..\x", true, PathVerdict.Invalid)]
    [InlineData(@"C:\Program Files\..\Windows\System32", true, PathVerdict.ProtectedTree)]
    public void CheckDeletion_ReturnsExpectedVerdict(string path, bool isDirectory, PathVerdict expected)
    {
        Assert.Equal(expected, Guard.CheckDeletion(path, isDirectory).Verdict);
    }

    [Fact]
    public void StartMenuShortcut_IsDeletable_EvenInsideProtectedTree()
    {
        var file = Guard.CheckDeletion(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Foo\Foo.lnk", isDirectory: false);
        var dir = Guard.CheckDeletion(@"C:\Users\ali\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Foo", isDirectory: true);
        var root = Guard.CheckDeletion(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs", isDirectory: true);

        Assert.True(file.IsAllowed);
        Assert.True(dir.IsAllowed);
        Assert.False(root.IsAllowed);
    }

    [Fact]
    public void FolderOutsideKnownRoots_RequiresExplicitPermission()
    {
        Assert.Equal(PathVerdict.OutsideKnownRoots, Guard.CheckDeletion(@"D:\Oyunlar\X", true).Verdict);
        Assert.True(Guard.CheckDeletion(@"D:\Oyunlar\X", true, allowOutsideKnownRoots: true).IsAllowed);

        // Sürücü köküne 1 seviye yakın klasör izinle bile silinmez (ör. bir oyun kütüphanesi).
        Assert.Equal(PathVerdict.TooShallow, Guard.CheckDeletion(@"D:\Oyunlar", true, allowOutsideKnownRoots: true).Verdict);
    }

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Users\ali\Downloads")]
    [InlineData(@"C:\Users\ali\Desktop")]
    [InlineData(@"C:\")]
    public void KillScope_RejectsSystemAndUserRoots(string dir)
    {
        Assert.False(Guard.CheckKillScope(dir).IsAllowed);
    }

    [Fact]
    public void KillScope_AllowsApplicationFolder()
    {
        Assert.True(Guard.CheckKillScope(@"C:\Program Files\Notepad++").IsAllowed);
        Assert.True(Guard.CheckKillScope(@"D:\Games\SomeGame").IsAllowed);
    }

    [Fact]
    public void IsUnder_DoesNotConfusePrefixes()
    {
        Assert.False(WindowsPath.IsUnderOrEqual(@"C:\AppData\x", @"C:\App"));
        Assert.True(WindowsPath.IsUnderOrEqual(@"C:\App\x", @"C:\App"));
        Assert.True(WindowsPath.IsUnderOrEqual(@"C:\App", @"C:\App"));
        Assert.False(WindowsPath.IsStrictlyUnder(@"C:\App", @"C:\App"));
    }

    [Theory]
    [InlineData(@"c:/program files//foo/", @"C:\program files\foo")]
    [InlineData(@"C:\A\.\B\..\C", @"C:\A\C")]
    [InlineData(@"C:\", "C:")]
    [InlineData(@"""C:\Program Files\X""", @"C:\Program Files\X")]
    public void Normalize_ProducesCanonicalForm(string input, string expected)
    {
        Assert.Equal(expected, WindowsPath.Normalize(input));
    }
}
