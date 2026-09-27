using System;
using System.Collections.Generic;
using Bakım.Core.Analyzer;
using Xunit;

namespace Bakim.Core.Tests;

/// <summary>
/// Analizör v3 Faz 8: gerçek dünyadaki kalıcılık komut satırı desenlerinden sentetik derlem. Kişisel
/// yollar ve kimlikler içermez; desenler Run, görev, hizmet ve Winlogon girdilerinde sık görülenlerdir.
/// </summary>
public class CommandLineCorpusTests
{
    private static readonly HashSet<string> Disk = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Windows\explorer.exe",
        @"C:\Windows\System32\svchost.exe",
        @"C:\Windows\System32\rundll32.exe",
        @"C:\Windows\System32\shell32.dll",
        @"C:\Windows\System32\cmd.exe",
        @"C:\Windows\System32\msiexec.exe",
        @"C:\Windows\System32\sc.exe",
        @"C:\Windows\System32\userinit.exe",
        @"C:\Windows\System32\SecurityHealthSystray.exe",
        @"C:\Windows\System32\drivers\tcpip.sys",
        @"C:\Windows\System32\DriverStore\FileRepository\nv.inf_amd64_x\nvlddmkm.sys",
        @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
        @"C:\Windows\System32\wscript.exe",
        @"C:\Windows\SysWOW64\OneDriveSetup.exe",
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Google\GoogleUpdater\131.0.6727.0\updater.exe",
        @"C:\Program Files (x86)\Microsoft\EdgeUpdate\MicrosoftEdgeUpdate.exe",
        @"C:\Program Files (x86)\Steam\steam.exe",
        @"C:\Program Files\NVIDIA Corporation\NvContainer\nvcontainer.exe",
        @"C:\Program Files\Windows Defender\MsMpEng.exe",
        @"C:\Program Files\Common Files\Microsoft Shared\ClickToRun\OfficeClickToRun.exe",
        @"C:\Users\u\AppData\Local\Discord\Update.exe",
        @"C:\Users\u\AppData\Local\Microsoft\OneDrive\OneDrive.exe",
        @"C:\Users\u\AppData\Local\Programs\Microsoft VS Code\Code.exe",
        @"C:\Users\u\AppData\Roaming\Spotify\Spotify.exe",
        @"C:\PROGRA~1\7-Zip\7zFM.exe",
    };

    private static readonly ResolverEnvironment Env = new()
    {
        FileExists = Disk.Contains,
        Expand = s => s
            .Replace("%windir%", @"C:\Windows", StringComparison.OrdinalIgnoreCase)
            .Replace("%SystemRoot%", @"C:\Windows", StringComparison.OrdinalIgnoreCase)
            .Replace("%ProgramFiles(x86)%", @"C:\Program Files (x86)", StringComparison.OrdinalIgnoreCase)
            .Replace("%ProgramFiles%", @"C:\Program Files", StringComparison.OrdinalIgnoreCase)
            .Replace("%LOCALAPPDATA%", @"C:\Users\u\AppData\Local", StringComparison.OrdinalIgnoreCase),
        WindowsDirectory = @"C:\Windows",
        SearchDirectories = new[] { @"C:\Windows\System32\WindowsPowerShell\v1.0" }
    };

    [Theory]
    // Run girdileri
    [InlineData(@"""C:\Users\u\AppData\Local\Microsoft\OneDrive\OneDrive.exe"" /background", @"C:\Users\u\AppData\Local\Microsoft\OneDrive\OneDrive.exe", "/background")]
    [InlineData(@"C:\Users\u\AppData\Local\Discord\Update.exe --processStart Discord.exe", @"C:\Users\u\AppData\Local\Discord\Update.exe", "--processStart Discord.exe")]
    [InlineData(@"C:\Users\u\AppData\Roaming\Spotify\Spotify.exe --autostart --minimized", @"C:\Users\u\AppData\Roaming\Spotify\Spotify.exe", "--autostart --minimized")]
    [InlineData(@"""C:\Program Files (x86)\Steam\steam.exe"" -silent", @"C:\Program Files (x86)\Steam\steam.exe", "-silent")]
    [InlineData(@"%windir%\system32\SecurityHealthSystray.exe", @"C:\Windows\System32\SecurityHealthSystray.exe", "")]
    [InlineData(@"""%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe"" --startup", @"C:\Users\u\AppData\Local\Programs\Microsoft VS Code\Code.exe", "--startup")]
    [InlineData(@"C:\PROGRA~1\7-Zip\7zFM.exe", @"C:\PROGRA~1\7-Zip\7zFM.exe", "")]
    // Görevler
    [InlineData(@"""C:\Program Files (x86)\Google\GoogleUpdater\131.0.6727.0\updater.exe"" --wake --system", @"C:\Program Files (x86)\Google\GoogleUpdater\131.0.6727.0\updater.exe", "--wake --system")]
    [InlineData(@"C:\Program Files (x86)\Microsoft\EdgeUpdate\MicrosoftEdgeUpdate.exe /c", @"C:\Program Files (x86)\Microsoft\EdgeUpdate\MicrosoftEdgeUpdate.exe", "/c")]
    [InlineData(@"%ProgramFiles%\Common Files\Microsoft Shared\ClickToRun\OfficeClickToRun.exe /frequentupdate SCHEDULEDTASK", @"C:\Program Files\Common Files\Microsoft Shared\ClickToRun\OfficeClickToRun.exe", "/frequentupdate SCHEDULEDTASK")]
    [InlineData(@"%windir%\SysWOW64\OneDriveSetup.exe /update /restart", @"C:\Windows\SysWOW64\OneDriveSetup.exe", "/update /restart")]
    // Hizmet ImagePath
    [InlineData(@"C:\Windows\system32\svchost.exe -k netsvcs -p", @"C:\Windows\System32\svchost.exe", "-k netsvcs -p")]
    [InlineData(@"%SystemRoot%\System32\svchost.exe -k LocalServiceNetworkRestricted -p", @"C:\Windows\System32\svchost.exe", "-k LocalServiceNetworkRestricted -p")]
    [InlineData(@"""C:\Program Files\NVIDIA Corporation\NvContainer\nvcontainer.exe"" -s NvContainerLocalSystem", @"C:\Program Files\NVIDIA Corporation\NvContainer\nvcontainer.exe", "-s NvContainerLocalSystem")]
    [InlineData(@"""C:\Program Files\Windows Defender\MsMpEng.exe""", @"C:\Program Files\Windows Defender\MsMpEng.exe", "")]
    [InlineData(@"C:\Windows\System32\msiexec.exe /V", @"C:\Windows\System32\msiexec.exe", "/V")]
    // Sürücüler
    [InlineData(@"System32\drivers\tcpip.sys", @"C:\Windows\System32\drivers\tcpip.sys", "")]
    [InlineData(@"\SystemRoot\System32\drivers\tcpip.sys", @"C:\Windows\System32\drivers\tcpip.sys", "")]
    [InlineData(@"\SystemRoot\System32\DriverStore\FileRepository\nv.inf_amd64_x\nvlddmkm.sys", @"C:\Windows\System32\DriverStore\FileRepository\nv.inf_amd64_x\nvlddmkm.sys", "")]
    [InlineData(@"\??\C:\Windows\System32\drivers\tcpip.sys", @"C:\Windows\System32\drivers\tcpip.sys", "")]
    // Winlogon
    [InlineData("explorer.exe", @"C:\Windows\explorer.exe", "")]
    [InlineData(@"C:\Windows\system32\userinit.exe,", @"C:\Windows\System32\userinit.exe", "")]
    public void Resolves(string raw, string expectedFile, string expectedArgs)
    {
        var r = CommandLineResolver.Resolve(raw, Env);
        Assert.Equal(TargetResolution.Resolved, r.Resolution);
        Assert.Equal(expectedFile, r.FilePath, ignoreCase: true);
        Assert.Equal(expectedArgs, r.Arguments);
    }

    [Theory]
    [InlineData(@"C:\Windows\system32\rundll32.exe C:\Windows\system32\shell32.dll,Control_RunDLL", "rundll32")]
    [InlineData(@"powershell.exe -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\ProgramData\x\run.ps1", "powershell")]
    [InlineData(@"wscript.exe ""C:\Users\u\AppData\Roaming\x.vbs""", "wscript")]
    [InlineData(@"%windir%\system32\cmd.exe /c start chrome", "cmd")]
    [InlineData(@"C:\Windows\system32\sc.exe start wuauserv", null)]
    public void DetectsWrappers(string raw, string? host)
    {
        var r = CommandLineResolver.Resolve(raw, Env);
        Assert.Equal(host, r.HostName);
    }

    [Theory]
    [InlineData(@"C:\Program Files\Gone App\app.exe --tray", @"C:\Program Files\Gone App\app.exe")]
    [InlineData(@"""C:\Users\u\Desktop\removed.exe"" -x", @"C:\Users\u\Desktop\removed.exe")]
    public void Orphans_KeepMeaningfulPath(string raw, string expected)
    {
        var r = CommandLineResolver.Resolve(raw, Env);
        Assert.Equal(TargetResolution.NotFound, r.Resolution);
        Assert.Equal(expected, r.FilePath);
    }
}
