using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakım.Models;
using Bakım.Services;
using Xunit;

namespace Bakim.Tests;

/// <summary>
/// Analizör v3 Faz 0 (Windows): gerçek tarama ve derin analiz. Ağa çıkmaz; VirusTotal sahtedir
/// (kullanıcının anahtarı ve kotası kullanılmaz).
/// </summary>
public sealed class AnalyzerFaz0WindowsTests
{
    private sealed class FakeVirusTotal : IVirusTotalCheckService
    {
        public (int Malicious, int Suspicious, int Total)? Verdict { get; init; }
        private readonly VirusTotalCheckService _hasher = new(new AppSettingsService(new NullLogService()));

        public string ApiKey { get; set; } = string.Empty;
        public bool HasApiKey => Verdict != null;
        public string ComputeSha256(string filePath) => _hasher.ComputeSha256(filePath);
        public Task<(int malicious, int total, string message)> CheckHashAsync(string sha256Hash, string? apiKey = null) =>
            Task.FromResult(Verdict is { } v ? (v.Malicious, v.Total, $"{v.Malicious}/{v.Total} zararlı") : (-1, -1, "API Anahtarı Gerekli"));
        public int LastSuspicious(string sha256) => Verdict?.Suspicious ?? 0;
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

    private static string Win(params string[] parts) =>
        Path.Combine(new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows) }.Concat(parts).ToArray());

    [Theory]
    [InlineData("explorer.exe")]
    [InlineData("System32", "notepad.exe")]
    [InlineData("System32", "svchost.exe")]
    [InlineData("System32", "taskmgr.exe")]
    public async Task DeepAnalysis_CleanWindowsBinaries_AreNotFlagged(params string[] path)
    {
        string file = Win(path);
        if (!File.Exists(file)) return;
        var result = await new FileThreatAnalyzerService(new FakeVirusTotal()).AnalyzeFileAsync(file);
        Assert.True(result.RiskScore <= 10, $"{file}: {result.RiskScore} — {string.Join(", ", result.Factors.Where(f => f.ScoreImpact > 0).Select(f => f.Title))}");
        Assert.DoesNotContain("kalıcı olarak", result.Recommendation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeepAnalysis_VirusTotalConsensus_OverridesValidSignature()
    {
        string explorer = Win("explorer.exe");
        if (!File.Exists(explorer)) return;
        var vt = new FakeVirusTotal { Verdict = (60, 2, 72) };
        var result = await new FileThreatAnalyzerService(vt).AnalyzeFileAsync(explorer);
        Assert.True(result.IsSigned);
        Assert.True(result.RiskScore >= 75, $"Puan {result.RiskScore}");
        Assert.StartsWith("YÜKSEK RİSK", result.Recommendation);
    }

    private static async Task<List<PersistenceItem>> ScanAsync()
    {
        var items = new List<PersistenceItem>();
        await foreach (var item in new AutorunsScannerEngine(new FakeVirusTotal()).ScanAllAsync()) items.Add(item);
        return items;
    }

    [Fact]
    public async Task Scanner_ResolvesPaths_AndProtectsDriversAndHidesOnlyVerifiedMicrosoft()
    {
        var items = await ScanAsync();
        Assert.NotEmpty(items);

        // Sürücüler ve açılış/sistem hizmetleri hiçbir koşulda değiştirilemez.
        Assert.DoesNotContain(items, i => i.Category == PersistenceCategory.Driver && (i.CanToggle || i.CanDelete));
        Assert.DoesNotContain(items, i => i.ServiceStart is 0 or 1 && i.CanToggle);
        Assert.DoesNotContain(items, i => i.ServiceName != null && Bakım.Core.ServiceControl.CriticalServicePolicy.IsCritical(i.ServiceName) && i.CanToggle);

        // "Microsoft'u gizle" yalnızca doğrulanmış imzaya dayanır.
        Assert.DoesNotContain(items, i => i.IsMicrosoft && i.Signature != SignatureStatus.Verified);

        // Winlogon'un standart değerleri (explorer.exe, userinit.exe,) yetim sayılmaz.
        var winlogon = items.Where(i => i.Name.StartsWith("Winlogon:", StringComparison.Ordinal)).ToList();
        Assert.All(winlogon, i => Assert.True(i.HasValidFile, $"{i.Name}: '{i.FilePath}' (ham: '{i.RawCommand}')"));

        // Hizmet ve sürücü yolları çözülür (\SystemRoot, System32\drivers …).
        var services = items.Where(i => i.Category is PersistenceCategory.WindowsService or PersistenceCategory.Driver).ToList();
        int unresolved = services.Count(i => !i.HasValidFile);
        Assert.True(unresolved <= Math.Max(3, services.Count / 20), $"{unresolved}/{services.Count} hizmet çözülemedi: " +
            string.Join(" | ", services.Where(i => !i.HasValidFile).Take(5).Select(i => i.RawCommand)));

        // Görevler COM'dan okunur: yol kesilmez ("C:\Program" gibi sahte yollar yok).
        Assert.DoesNotContain(items, i => i.Category == PersistenceCategory.ScheduledTask && i.FilePath.Equals(@"C:\Program", StringComparison.OrdinalIgnoreCase));
        Assert.All(items.Where(i => i.Category == PersistenceCategory.ScheduledTask), i => Assert.StartsWith(@"\", i.TaskPath));
    }

    [Fact]
    public async Task Scanner_Faz1_Categories_AreReadOnly_AndResolved()
    {
        var items = await ScanAsync();

        // BootExecute "autocheck autochk *": program autochk.exe'dir ve her Windows'ta vardır.
        var boot = items.FirstOrDefault(i => i.Name.StartsWith("BootExecute: autocheck", StringComparison.OrdinalIgnoreCase));
        if (boot != null) Assert.True(boot.HasValidFile, boot.FilePath);

        // LSA paketleri (msv1_0 …) System32 altında çözülür.
        Assert.All(items.Where(i => i.Name.Equals("LSA: msv1_0", StringComparison.OrdinalIgnoreCase)), i => Assert.True(i.HasValidFile));

        foreach (var i in items.Where(i => i.Category is PersistenceCategory.SystemHook or PersistenceCategory.BrowserExtension or PersistenceCategory.OfficeAddin))
        {
            Assert.False(i.CanToggle);
            Assert.False(i.CanDelete);
        }

        // Eklentilere imza puanı uygulanmaz.
        Assert.All(items.Where(i => i.Category == PersistenceCategory.BrowserExtension), i => Assert.False(i.SignatureApplicable));

        // Aynı Office eklentisi iki kez listelenmez.
        var office = items.Where(i => i.Category == PersistenceCategory.OfficeAddin).Select(i => i.LocationSource).ToList();
        Assert.Equal(office.Count, office.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void RiskScore_MissingFile_IsCleanupNotThreat()
    {
        var orphan = new PersistenceItem { Signature = SignatureStatus.Unsigned, FilePath = @"C:\Users\u\Desktop\gone.exe", HasValidFile = false, Category = PersistenceCategory.RegistryRun };
        Assert.InRange(orphan.RiskScore, 5, 20);

        var noFile = new PersistenceItem { Signature = SignatureStatus.Unsigned, FilePath = string.Empty, SignatureApplicable = false, Category = PersistenceCategory.OfficeAddin };
        Assert.Equal(0, noFile.RiskScore);
    }

    [Fact]
    public async Task Engine_RefusesPolicyViolations_WithoutTouchingSystem()
    {
        var engine = new AutorunsScannerEngine(new FakeVirusTotal());
        var driver = new PersistenceItem { Name = "disk", Category = PersistenceCategory.Driver, ServiceName = "disk", ServiceStart = 0, ServiceType = 1 };
        Assert.False(await engine.ToggleItemAsync(driver, false));
        Assert.False(await engine.DeleteItemAsync(driver));

        var critical = new PersistenceItem { Name = "RPC", Category = PersistenceCategory.WindowsService, ServiceName = "RpcSs", ServiceStart = 2, ServiceType = 0x20 };
        Assert.False(critical.CanToggle);
        Assert.False(await engine.ToggleItemAsync(critical, false));

        var windowsTask = new PersistenceItem { Name = "Defrag", Category = PersistenceCategory.ScheduledTask, TaskPath = @"\Microsoft\Windows\Defrag\ScheduledDefrag" };
        Assert.False(await engine.DeleteItemAsync(windowsTask));
    }

    [Fact]
    public void RiskScore_UsesVirusTotalFloor_AndDeepScore()
    {
        var signed = new PersistenceItem { Signature = SignatureStatus.Verified, FilePath = Win("explorer.exe") };
        Assert.Equal(0, signed.RiskScore);
        signed.VirusTotalPositives = 60;
        signed.TotalScanners = 72;
        Assert.True(signed.RiskScore >= 75);

        var deep = new PersistenceItem { Signature = SignatureStatus.Unsigned, FilePath = Win("explorer.exe"), DeepRiskScore = 5 };
        Assert.Equal(5, deep.RiskScore);
    }
}
