using System;
using System.Collections.Generic;
using System.Linq;
using Bakım.Core.Sentinel;
using Xunit;

namespace Bakim.Core.Tests;

/// <summary>Nöbetçi v3 Faz A: dosya farkı, uygulama adı, oturum kuralları, koruma rozeti.</summary>
public class SentinelFazATests
{
    private const string Pf = @"C:\Program Files\App";
    private const string Tmp = @"C:\Users\u\AppData\Local\Temp\is-ABC.tmp";

    private static long _seq;

    private static FileChangeEvent Ev(FileChangeKind kind, string path, string? old = null) => new(++_seq, kind, path, old);

    /// <summary>Disk durumu sözlükten okunur; sözlükte olmayan yol yoktur.</summary>
    private static Func<string, PathState> Disk(params (string Path, PathState State)[] entries)
    {
        var map = entries.ToDictionary(e => e.Path, e => e.State, StringComparer.OrdinalIgnoreCase);
        return p => map.TryGetValue(p, out var s) ? s : PathState.Missing;
    }

    // ---- A5: dosya farkı ----

    [Fact]
    public void Delta_CreatedThenDeleted_IsTemp_NotDeleted()
    {
        var delta = SetupDeltaBuilder.Build(new[]
        {
            Ev(FileChangeKind.Created, Tmp + @"\a.dll"),
            Ev(FileChangeKind.Created, Tmp + @"\b.dll"),
            Ev(FileChangeKind.Deleted, Tmp + @"\a.dll"),
            Ev(FileChangeKind.Deleted, Tmp + @"\b.dll"),
        }, Disk());

        Assert.Empty(delta.DeletedFiles);
        Assert.Empty(delta.CreatedFiles);
        Assert.Equal(2, delta.TempItemCount);
    }

    [Fact]
    public void Delta_PreexistingFileDeleted_IsDeleted()
    {
        var delta = SetupDeltaBuilder.Build(new[] { Ev(FileChangeKind.Deleted, Pf + @"\old.dll") }, Disk());
        Assert.Equal(new[] { Pf + @"\old.dll" }, delta.DeletedFiles);
        Assert.Equal(0, delta.TempItemCount);
    }

    [Fact]
    public void Delta_DeletedThenRecreated_IsModified_NotCreated()
    {
        string file = Pf + @"\app.exe";
        var delta = SetupDeltaBuilder.Build(new[]
        {
            Ev(FileChangeKind.Deleted, file),
            Ev(FileChangeKind.Created, file),
        }, Disk((file, PathState.File)));

        Assert.Empty(delta.CreatedFiles);
        Assert.Empty(delta.DeletedFiles);
        Assert.Equal(new[] { file }, delta.ModifiedFiles);
    }

    [Fact]
    public void Delta_TempNameRenamedToFinal_IsCreatedUnderFinalName()
    {
        string tmp = Pf + @"\app.exe.tmp123";
        string final = Pf + @"\app.exe";
        var delta = SetupDeltaBuilder.Build(new[]
        {
            Ev(FileChangeKind.Created, tmp),
            Ev(FileChangeKind.Changed, tmp),
            Ev(FileChangeKind.Renamed, final, tmp),
        }, Disk((final, PathState.File)), _ => false);

        Assert.Equal(new[] { final }, delta.CreatedFiles);
        Assert.Empty(delta.RenamedFiles);
        Assert.Empty(delta.ModifiedFiles);
    }

    [Fact]
    public void Delta_RenameOntoPreexistingFile_IsModified_NeverCreated()
    {
        string tmp = Pf + @"\app.exe.new";
        string final = Pf + @"\app.exe";
        var delta = SetupDeltaBuilder.Build(new[]
        {
            Ev(FileChangeKind.Created, tmp),
            Ev(FileChangeKind.Renamed, final, tmp),
        }, Disk((final, PathState.File)), path => path == final);

        Assert.Empty(delta.CreatedFiles);
        Assert.Equal(new[] { final }, delta.ModifiedFiles);
    }

    [Fact]
    public void Delta_DeleteThenRenameOntoTarget_IsModified()
    {
        string tmp = Pf + @"\x.tmp";
        string final = Pf + @"\x.dll";
        var delta = SetupDeltaBuilder.Build(new[]
        {
            Ev(FileChangeKind.Created, tmp),
            Ev(FileChangeKind.Deleted, final),
            Ev(FileChangeKind.Renamed, final, tmp),
        }, Disk((final, PathState.File)), _ => false);

        Assert.Empty(delta.CreatedFiles);
        Assert.Empty(delta.DeletedFiles);
        Assert.Equal(new[] { final }, delta.ModifiedFiles);
    }

    [Fact]
    public void Delta_DirectoryRename_MovesChildrenToNewPath()
    {
        string stage = @"C:\Program Files\~stage";
        string final = @"C:\Program Files\Tool";
        var delta = SetupDeltaBuilder.Build(new[]
        {
            Ev(FileChangeKind.Created, stage),
            Ev(FileChangeKind.Created, stage + @"\tool.exe"),
            Ev(FileChangeKind.Created, stage + @"\lib\core.dll"),
            Ev(FileChangeKind.Renamed, final, stage),
        }, Disk((final, PathState.Directory), (final + @"\tool.exe", PathState.File), (final + @"\lib\core.dll", PathState.File)), _ => false);

        Assert.Equal(new[] { final }, delta.CreatedFolders);
        Assert.Equal(new[] { final + @"\lib\core.dll", final + @"\tool.exe" }, delta.CreatedFiles);
    }

    [Fact]
    public void Delta_PreexistingFileRenamed_IsReportedAsMove_NotCreatedOrDeleted()
    {
        string before = @"C:\Users\u\Desktop\notes.txt";
        string after = @"C:\Users\u\Desktop\notes-old.txt";
        var delta = SetupDeltaBuilder.Build(new[] { Ev(FileChangeKind.Renamed, after, before) }, Disk((after, PathState.File)));

        Assert.Empty(delta.CreatedFiles);
        Assert.Empty(delta.DeletedFiles);
        Assert.Equal(new[] { $"{before} → {after}" }, delta.RenamedFiles);
    }

    [Fact]
    public void Delta_ChangedDirectoryAndMissingFiles_AreDropped()
    {
        var delta = SetupDeltaBuilder.Build(new[]
        {
            Ev(FileChangeKind.Changed, @"C:\Program Files"),
            Ev(FileChangeKind.Created, Pf + @"\gone.txt"),
            Ev(FileChangeKind.Changed, Pf + @"\settings.ini"),
        }, Disk((@"C:\Program Files", PathState.Directory), (Pf + @"\settings.ini", PathState.File)));

        Assert.Empty(delta.CreatedFiles);
        Assert.Equal(new[] { Pf + @"\settings.ini" }, delta.ModifiedFiles);
    }

    [Fact]
    public void Delta_OrderIsBySequence_NotEnumerationOrder()
    {
        string file = Pf + @"\late.dll";
        var created = new FileChangeEvent(10, FileChangeKind.Created, file);
        var deleted = new FileChangeEvent(20, FileChangeKind.Deleted, file);
        var delta = SetupDeltaBuilder.Build(new[] { deleted, created }, Disk());

        Assert.Empty(delta.DeletedFiles);
        Assert.Equal(1, delta.TempItemCount);
    }

    // ---- A9: uygulama adı ----

    [Theory]
    [InlineData("7-Zip SFX", true)]
    [InlineData("Setup/Uninstall", true)]
    [InlineData("Windows Installer - Unicode", true)]
    [InlineData("Inno Setup", true)]
    [InlineData("WinRAR self-extracting archive", true)]
    [InlineData("Setup", true)]
    [InlineData("  ", true)]
    [InlineData("VLC media player", false)]
    [InlineData("7-Zip", false)]
    [InlineData("Notepad++", false)]
    public void AppName_Generic(string name, bool generic) => Assert.Equal(generic, SetupAppName.IsGeneric(name));

    [Fact]
    public void AppName_SkipsFrameworkNames()
    {
        Assert.Equal("VLC media player", SetupAppName.Resolve("7-Zip SFX", "VLC media player", null, @"C:\Downloads\vlc.exe", "vlc"));
        Assert.Equal("DirectX 9 SDK Install",
            SetupAppName.Resolve("7-Zip SFX", "7z Setup SFX", null, @"C:\ProgramData\Riot Games\DirectX_9_SDK_Install.exe", "DirectX_9_SDK_Install"));
        Assert.Equal("Setup - Foo", SetupAppName.Resolve(null, "Setup/Uninstall", "Setup - Foo", @"C:\d\setup.exe", "setup"));
        Assert.Equal("setup", SetupAppName.Resolve(null, null, null, @"C:\d\setup.exe", "setup"));
    }

    [Fact]
    public void AppName_FromNewPrograms()
    {
        Assert.Null(SetupAppName.FromNewPrograms(Array.Empty<string>(), "x", null));
        Assert.Equal("Notepad++ (64-bit x64)", SetupAppName.FromNewPrograms(new[] { "Notepad++ (64-bit x64)" }, "npp.8.6.Installer.x64", null));
        // Paket kurulum: adla ortak kelimesi olan seçilir.
        Assert.Equal("VLC media player",
            SetupAppName.FromNewPrograms(new[] { "McAfee WebAdvisor", "VLC media player" }, "vlc-3.0.20-win64", @"C:\d\vlc-3.0.20-win64.exe"));
        // Ortak kelime yoksa mevcut ad korunur.
        Assert.Null(SetupAppName.FromNewPrograms(new[] { "League of Legends", "Teamfight Tactics" }, "DirectX 9 SDK Install", null));
    }

    // ---- A8: arka plan başlatıcıları ----

    private static Func<int, (int, string)?> Tree(params (int Pid, int Parent, string Name)[] procs)
    {
        var map = procs.ToDictionary(p => p.Pid);
        return pid => map.TryGetValue(pid, out var p) && map.TryGetValue(p.Parent, out var parent) ? (parent.Pid, parent.Name) : null;
    }

    [Fact]
    public void Background_LauncherAncestor_IsFound()
    {
        var parentOf = Tree((100, 10, "DirectX_9_SDK_Install.exe"), (10, 5, "RiotClientServices.exe"), (5, 1, "explorer.exe"));
        Assert.Equal("RiotClientServices.exe", SetupSessionPolicy.FindBackgroundAncestor(100, parentOf));
    }

    [Fact]
    public void Background_UserLaunchedInstaller_IsNotSuppressed()
    {
        var fromExplorer = Tree((100, 10, "setup.exe"), (10, 5, "explorer.exe"));
        var fromBrowser = Tree((100, 10, "vlc.exe"), (10, 5, "msedge.exe"), (5, 1, "explorer.exe"));
        Assert.Null(SetupSessionPolicy.FindBackgroundAncestor(100, fromExplorer));
        Assert.Null(SetupSessionPolicy.FindBackgroundAncestor(100, fromBrowser));
    }

    [Fact]
    public void Background_ServiceHost_CountsOnlyAsDirectParent()
    {
        // Windows Update: svchost doğrudan ebeveyn.
        Assert.Equal("svchost.exe", SetupSessionPolicy.FindBackgroundAncestor(100, Tree((100, 10, "msiexec.exe"), (10, 1, "svchost.exe"))));
        // Mağaza uygulaması terminal svchost altından başlar: terminalden elle başlatılan kurulum susturulmaz.
        var terminal = Tree((100, 20, "setup.exe"), (20, 30, "pwsh.exe"), (30, 40, "WindowsTerminal.exe"), (40, 1, "svchost.exe"));
        Assert.Null(SetupSessionPolicy.FindBackgroundAncestor(100, terminal));
    }

    // ---- A10: kurulan uygulama başlatıldı ----

    [Fact]
    public void LaunchedApp_IsInstalledExe_OutsideTemp()
    {
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Users\u\AppData\Local\Programs\Microsoft VS Code\Code.exe",
            @"C:\Users\u\AppData\Local\Temp\is-ABC.tmp\VSCodeUserSetup.tmp",
            @"C:\Users\u\AppData\Local\Temp\helper\worker.exe",
            @"C:\Program Files\Foo\unins000.exe",
        };
        string[] temp = { @"C:\Users\u\AppData\Local\Temp\" };

        Assert.True(SetupSessionPolicy.IsLaunchedInstalledApp(@"C:\Users\u\AppData\Local\Programs\Microsoft VS Code\code.exe", written, temp));
        // Geçici klasördeki ikinci aşama kurulum oturumu açık tutmaya devam eder.
        Assert.False(SetupSessionPolicy.IsLaunchedInstalledApp(@"C:\Users\u\AppData\Local\Temp\helper\worker.exe", written, temp));
        // Kurulum aracına benzeyen ad.
        Assert.False(SetupSessionPolicy.IsLaunchedInstalledApp(@"C:\Program Files\Foo\unins000.exe", written, temp));
        // Oturumda yazılmamış exe (önceden kurulu bir uygulama).
        Assert.False(SetupSessionPolicy.IsLaunchedInstalledApp(@"C:\Windows\notepad.exe", written, temp));
        Assert.False(SetupSessionPolicy.IsLaunchedInstalledApp(null, written, temp));
    }

    // ---- A2: koruma rozeti ----

    [Fact]
    public void Protection_WmiOnly_IsNotTamKoruma()
    {
        var status = SentinelProtectionPolicy.Evaluate(isAdmin: true, isUsnAvailable: false, isTraceAvailable: true);
        Assert.Equal(SentinelProtectionMode.Gelismis, status.Mode);
        Assert.DoesNotContain("USN", status.ActiveSensorsSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Kernel", status.ActiveSensorsSummary, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(SentinelProtectionMode.TemelMod, SentinelProtectionPolicy.Evaluate(false, false, false).Mode);
        Assert.Equal(SentinelProtectionMode.TemelMod, SentinelProtectionPolicy.Evaluate(false, true, true).Mode);
        Assert.Equal(SentinelProtectionMode.TamKoruma, SentinelProtectionPolicy.Evaluate(true, true, false).Mode);
    }
}
