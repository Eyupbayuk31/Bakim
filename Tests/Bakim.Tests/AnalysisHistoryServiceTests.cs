using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakım.Core.History;
using Bakım.Models;
using Bakım.Services;
using Bakım.Services.History;
using Bakım.Services.Safety;
using Xunit;

namespace Bakim.Tests;

/// <summary>Analizör Geçmişi servisi ve kaydeden dekoratör (§6.4).</summary>
public sealed class AnalysisHistoryServiceTests : System.IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bakim_hist_" + System.Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private AnalysisHistoryService CreateService() =>
        new(NullLogService.Instance, new AnalysisHistoryStore(_dir));

    private static ThreatAnalysisResult Result(string path, int risk = 30, string sha = "abc") => new()
    {
        FilePath = path,
        FileName = Path.GetFileName(path),
        Sha256 = sha,
        RiskScore = risk,
        IsSigned = false,
        DigitalSignatureText = "İmzasız",
        Factors = { new ThreatFactor { Title = "İmzasız yazılım", Severity = ThreatSeverity.Warning, ScoreImpact = 25 } }
    };

    private sealed class FakeAnalyzer : IFileThreatAnalyzerService
    {
        public Task<ThreatAnalysisResult> AnalyzeFileAsync(string filePath, string? commandArgs = null, PersistenceItem? autorunItem = null) =>
            Task.FromResult(Result(filePath));
        public Task<OperationResult> KillProcessAsync(int processId) =>
            Task.FromResult(new OperationResult("", DeleteOutcome.Deleted, "", 0));
        public Task<OperationResult> RemoveFileAsync(string filePath) =>
            Task.FromResult(new OperationResult(filePath, DeleteOutcome.Recycled, "", 0));
    }

    [Fact]
    public async Task Record_PersistsAcrossInstances()
    {
        Directory.CreateDirectory(_dir);
        string file = Path.Combine(_dir, "x.exe");
        File.WriteAllBytes(file, new byte[] { 0x4D, 0x5A });

        var service = CreateService();
        var rec = await service.RecordAsync(Result(file), AnalysisSource.Analyzer, "Dosya İncele");
        Assert.NotNull(rec);
        Assert.Equal(AnalysisVerdict.Caution, rec!.Verdict);
        Assert.NotNull(rec.FileLastWriteUtc);
        Assert.Equal("Unsigned", rec.SignatureStatus);

        var reopened = CreateService();
        Assert.Equal(1, reopened.Count);
        Assert.Equal("Dosya İncele", reopened.Query(AnalysisHistoryFilter.All).Single().SourceDetail);
    }

    [Fact]
    public async Task MissingFile_IsRecordedAsMissing()
    {
        var rec = await CreateService().RecordAsync(Result(Path.Combine(_dir, "yok.exe")), AnalysisSource.Analyzer);
        Assert.Equal(AnalysisVerdict.Missing, rec!.Verdict);
    }

    [Fact]
    public async Task Decorator_RecordsWithContextSource()
    {
        var history = CreateService();
        var analyzer = new RecordingFileThreatAnalyzer(new FakeAnalyzer(), history, NullLogService.Instance);

        using (AnalysisContext.Begin(AnalysisSource.SetupSentinel, "Kurulum: 7-Zip"))
            await analyzer.AnalyzeFileAsync(@"C:\Program Files\7-Zip\7zFM.exe");

        var record = history.Query(AnalysisHistoryFilter.All).Single();
        Assert.Equal(AnalysisSource.SetupSentinel, record.Source);
        Assert.Equal("Kurulum: 7-Zip", record.SourceDetail);
    }

    [Fact]
    public async Task Decorator_MarksDeletedDecision_OnRemove()
    {
        var history = CreateService();
        var analyzer = new RecordingFileThreatAnalyzer(new FakeAnalyzer(), history, NullLogService.Instance);
        await analyzer.AnalyzeFileAsync(@"C:\Temp\bad.exe");
        await analyzer.RemoveFileAsync(@"C:\Temp\bad.exe");
        Assert.Equal(UserDecision.Deleted, history.Query(AnalysisHistoryFilter.All).Single().Decision);
    }

    [Fact]
    public async Task TrustDecision_CarriesToNewAnalysesOfSameHash()
    {
        var history = CreateService();
        var first = await history.RecordAsync(Result(@"C:\a\tool.exe", sha: "same"), AnalysisSource.Analyzer);
        await history.SetDecisionAsync(first!.Id, UserDecision.Trusted);
        var second = await history.RecordAsync(Result(@"D:\copy\tool.exe", sha: "same"), AnalysisSource.Startup);
        Assert.Equal(UserDecision.Trusted, second!.Decision);
        Assert.True(history.IsTrustedHash("same"));
    }

    [Fact]
    public async Task Snapshots_DiffLatest()
    {
        var history = CreateService();
        var a = new PersistenceItem { Name = "Discord", Category = PersistenceCategory.RegistryRun, LocationSource = @"HKCU\Run", FilePath = @"C:\d.exe" };
        var b = new PersistenceItem { Name = "Updater", Category = PersistenceCategory.ScheduledTask, LocationSource = @"Task: \Up", FilePath = @"C:\u.exe" };

        await history.SaveSnapshotAsync(new[] { a }, "Manual");
        await Task.Delay(1100); // dosya adı saniye çözünürlüğünde
        await history.SaveSnapshotAsync(new[] { a, b }, "Manual");

        var diff = history.DiffLatest();
        var added = Assert.Single(diff);
        Assert.Equal(DiffKind.Added, added.Kind);
        Assert.Equal("Updater", added.After!.Name);
    }
}
