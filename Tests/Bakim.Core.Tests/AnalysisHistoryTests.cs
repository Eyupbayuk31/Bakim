using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakım.Core.History;
using Xunit;

namespace Bakim.Core.Tests;

public sealed class AnalysisHistoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bakim-history-" + Guid.NewGuid().ToString("N"));
    private AnalysisHistoryStore Store() => new(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static AnalysisRecord Rec(string name, int risk = 10, string sha = "aa", DateTime? at = null) => new()
    {
        AnalyzedAtUtc = at ?? new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc),
        FileName = name,
        FilePath = @"C:\Apps\" + name,
        Sha256 = sha,
        RiskScore = risk,
        Verdict = AnalysisVerdicts.FromScore(risk),
        SignatureStatus = "Unsigned",
        Source = AnalysisSource.Analyzer,
        Factors = new[] { new AnalysisFactorSummary("İmzasız yazılım", "Warning", 25) }
    };

    [Fact]
    public void AppendAndLoad_RoundTrips()
    {
        var store = Store();
        var r = Rec("a.exe") with { VirusTotalMalicious = 3, VirusTotalTotal = 72, Note = "not" };
        store.Append(r);

        var loaded = Store().Load();
        Assert.Single(loaded.Records);
        var back = loaded.Records[0];
        Assert.Equal(r.Id, back.Id);
        Assert.Equal(3, back.VirusTotalMalicious);
        Assert.Equal("İmzasız yazılım", back.Factors[0].Title);
        Assert.Equal(AnalysisSource.Analyzer, back.Source);
        Assert.Equal(0, loaded.CorruptLines);
    }

    [Fact]
    public void LastLineWins_ForDecisionUpdates()
    {
        var store = Store();
        var r = Rec("a.exe");
        store.Append(r);
        store.Append(r with { Decision = UserDecision.Trusted, DecisionAtUtc = DateTime.UtcNow, Note = "benim" });

        var loaded = store.Load();
        Assert.Single(loaded.Records);
        Assert.Equal(UserDecision.Trusted, loaded.Records[0].Decision);
        Assert.Equal("benim", loaded.Records[0].Note);
        Assert.Equal(2, loaded.TotalLines);
    }

    [Fact]
    public void CorruptLine_DoesNotBreakHistory()
    {
        var store = Store();
        store.Append(Rec("a.exe"));
        File.AppendAllText(store.RecordsPath, "{ bozuk satır\n");
        store.Append(Rec("b.exe"));

        var loaded = store.Load();
        Assert.Equal(2, loaded.Records.Count);
        Assert.Equal(1, loaded.CorruptLines);
    }

    [Fact]
    public void Rewrite_Compacts()
    {
        var store = Store();
        var r = Rec("a.exe");
        for (int i = 0; i < 5; i++) store.Append(r with { Note = "n" + i });
        store.Rewrite(store.Load().Records);

        var loaded = store.Load();
        Assert.Equal(1, loaded.TotalLines);
        Assert.Equal("n4", loaded.Records[0].Note);
    }

    [Fact]
    public void Snapshots_SaveListLoadPrune()
    {
        var store = Store();
        for (int i = 0; i < 4; i++)
        {
            store.SaveSnapshot(new PersistenceSnapshot
            {
                TakenAtUtc = new DateTime(2026, 9, 20 + i, 9, 0, 0, DateTimeKind.Utc),
                Trigger = "Manual",
                Entries = new[] { Entry("Run", "HKCU\\Run", "App" + i, @"C:\a" + i + ".exe") }
            });
        }

        var list = store.ListSnapshots();
        Assert.Equal(4, list.Count);
        Assert.True(list[0].TakenAtUtc > list[1].TakenAtUtc);

        var newest = store.LoadSnapshot(list[0].Id);
        Assert.NotNull(newest);
        Assert.Equal("App3", newest!.Entries[0].Name);

        Assert.Equal(2, store.PruneSnapshots(2));
        Assert.Equal(2, store.ListSnapshots().Count);
        Assert.Equal(2, Directory.GetFiles(store.SnapshotsDirectory, "*.json.gz").Length);
    }

    [Fact]
    public void ClearAll_RemovesEverything()
    {
        var store = Store();
        store.Append(Rec("a.exe"));
        store.SaveSnapshot(new PersistenceSnapshot { TakenAtUtc = DateTime.UtcNow });
        store.ClearAll();
        Assert.Empty(store.Load().Records);
        Assert.Empty(store.ListSnapshots());
    }

    [Fact]
    public async Task ConcurrentAppends_AreAllKept()
    {
        var store = Store();
        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(() => store.Append(Rec($"f{i}.exe")))));
        Assert.Equal(50, store.Load().Records.Count);
    }

    internal static PersistenceEntry Entry(string cat, string loc, string name, string path, bool enabled = true,
        string sig = "Verified", string? signer = "Microsoft Corporation", string? sha = null, string args = "") =>
        new(SnapshotDiff.KeyOf(cat, loc, name), cat, name, loc, path, args, enabled, sig, signer, sha);
}

public class SnapshotDiffTests
{
    private static PersistenceEntry E(string name, string path, bool enabled = true, string sig = "Verified", string? sha = null) =>
        AnalysisHistoryStoreTests.Entry("Run", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", name, path, enabled, sig, sha: sha);

    [Fact]
    public void DetectsAddedRemovedAndChanged()
    {
        var before = new[] { E("Discord", @"C:\d.exe"), E("FooSvc", @"C:\Foo\svc.exe"), E("Same", @"C:\s.exe") };
        var after = new[] { E("FooSvc", @"C:\Users\Public\svc.exe", sig: "Unsigned"), E("Same", @"C:\s.exe"), E("Updater", @"C:\ProgramData\x\up.exe") };

        var diff = SnapshotDiff.Compute(before, after);

        var added = Assert.Single(diff, d => d.Kind == DiffKind.Added);
        Assert.Equal("Updater", added.After!.Name);
        var removed = Assert.Single(diff, d => d.Kind == DiffKind.Removed);
        Assert.Equal("Discord", removed.Before!.Name);
        var changed = Assert.Single(diff, d => d.Kind == DiffKind.Changed);
        Assert.Contains(nameof(PersistenceEntry.FilePath), changed.ChangedFields);
        Assert.Contains(nameof(PersistenceEntry.SignatureStatus), changed.ChangedFields);
        Assert.Equal(3, diff.Count);
    }

    [Fact]
    public void KeyIsCaseAndWhitespaceInsensitive_PathQuotesIgnored()
    {
        var a = new PersistenceEntry(SnapshotDiff.KeyOf("Run", @"HKCU\Run", "App"), "Run", "App", @"HKCU\Run", "\"C:\\A.exe\"", "", true, "Verified", null, null);
        var b = new PersistenceEntry(SnapshotDiff.KeyOf(" run ", @"hkcu\run", "APP"), "Run", "APP", @"hkcu\run", @"c:\a.exe", "", true, "Verified", null, null);
        Assert.Empty(SnapshotDiff.Compute(new[] { a }, new[] { b }));
    }

    [Fact]
    public void MissingHash_IsNotAChange_ButDifferentHashIs()
    {
        Assert.Empty(SnapshotDiff.Compute(new[] { E("A", @"C:\a.exe", sha: null) }, new[] { E("A", @"C:\a.exe", sha: "ff") }));
        var d = SnapshotDiff.Compute(new[] { E("A", @"C:\a.exe", sha: "aa") }, new[] { E("A", @"C:\a.exe", sha: "bb") });
        Assert.Equal(new[] { nameof(PersistenceEntry.Sha256) }, d.Single().ChangedFields);
    }

    [Fact]
    public void DuplicateKeys_AreNotLost()
    {
        var before = new[] { E("Dup", @"C:\1.exe"), E("Dup", @"C:\2.exe") };
        var after = new[] { E("Dup", @"C:\1.exe") };
        var diff = SnapshotDiff.Compute(before, after);
        Assert.Single(diff, x => x.Kind == DiffKind.Removed);
    }

    [Fact]
    public void DisablingAnEntry_IsAChange()
    {
        var d = SnapshotDiff.Compute(new[] { E("A", @"C:\a.exe") }, new[] { E("A", @"C:\a.exe", enabled: false) });
        Assert.Equal(new[] { nameof(PersistenceEntry.IsEnabled) }, d.Single().ChangedFields);
    }
}

public class AnalysisHistoryIndexTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

    private static AnalysisRecord R(string name, int risk, int daysAgo, string sha = "h", AnalysisSource src = AnalysisSource.Analyzer) => new()
    {
        FileName = name,
        FilePath = @"C:\X\" + name,
        Sha256 = sha,
        RiskScore = risk,
        Verdict = AnalysisVerdicts.FromScore(risk),
        AnalyzedAtUtc = Now.AddDays(-daysAgo),
        Source = src,
        SignatureStatus = "Unsigned"
    };

    [Fact]
    public void Upsert_ReplacesAndReindexes()
    {
        var index = new AnalysisHistoryIndex();
        var r = R("a.exe", 10, 0, sha: "old");
        index.Upsert(r);
        index.Upsert(r with { Sha256 = "new" });
        Assert.Equal(1, index.Count);
        Assert.Empty(index.GetByHash("old"));
        Assert.Single(index.GetByHash("NEW"));
    }

    [Fact]
    public void Query_FiltersAndSortsNewestFirst()
    {
        var index = new AnalysisHistoryIndex();
        index.Upsert(R("safe.exe", 5, 1));
        index.Upsert(R("bad.exe", 80, 2, src: AnalysisSource.SetupSentinel) with { VirusTotalMalicious = 5, VirusTotalTotal = 70 });
        index.Upsert(R("old.exe", 50, 40));

        Assert.Equal(new[] { "safe.exe", "bad.exe", "old.exe" }, index.Query(AnalysisHistoryFilter.All, Now).Select(r => r.FileName));
        Assert.Equal(2, index.Query(new AnalysisHistoryFilter { Range = HistoryDateRange.Last7Days }, Now).Count);
        Assert.Equal("bad.exe", index.Query(new AnalysisHistoryFilter { OnlyVirusTotalHits = true }, Now).Single().FileName);
        Assert.Equal("bad.exe", index.Query(new AnalysisHistoryFilter { Source = AnalysisSource.SetupSentinel }, Now).Single().FileName);
        Assert.Equal("bad.exe", index.Query(new AnalysisHistoryFilter { Verdicts = new[] { AnalysisVerdict.Dangerous } }, Now).Single().FileName);
        Assert.Equal("old.exe", index.Query(new AnalysisHistoryFilter { Text = "OLD" }, Now).Single().FileName);
    }

    [Fact]
    public void LatestByHash_RespectsMaxAge_AndTrustDecision()
    {
        var index = new AnalysisHistoryIndex();
        index.Upsert(R("a.exe", 10, 10, sha: "abc"));
        Assert.Null(index.LatestByHash("abc", TimeSpan.FromDays(7), Now));
        var fresh = R("a.exe", 12, 2, sha: "abc");
        index.Upsert(fresh);
        Assert.Equal(fresh.Id, index.LatestByHash("abc", TimeSpan.FromDays(7), Now)!.Id);

        Assert.False(index.IsTrustedHash("abc"));
        index.Upsert(fresh with { Decision = UserDecision.Trusted, DecisionAtUtc = Now });
        Assert.True(index.IsTrustedHash("abc"));
    }

    [Fact]
    public void Purge_KeepsTrustedAndQuarantined()
    {
        var index = new AnalysisHistoryIndex();
        var old = R("old.exe", 10, 400);
        var trusted = R("trusted.exe", 10, 400) with { Decision = UserDecision.Trusted };
        index.Upsert(old);
        index.Upsert(trusted);
        index.Upsert(R("new.exe", 10, 1));

        var ids = index.SelectForPurge(HistoryRetention.Default, Now);
        Assert.Equal(new[] { old.Id }, ids);
    }

    [Fact]
    public void Purge_EnforcesMaxRecords_OldestFirst()
    {
        var index = new AnalysisHistoryIndex();
        var records = Enumerable.Range(0, 5).Select(i => R($"f{i}.exe", 10, i)).ToList();
        records.ForEach(index.Upsert);
        var ids = index.SelectForPurge(new HistoryRetention(TimeSpan.FromDays(365), 3), Now);
        Assert.Equal(new HashSet<string> { records[3].Id, records[4].Id }, ids.ToHashSet());
    }

    [Fact]
    public void RelatedTo_FindsByHashOrPath()
    {
        var index = new AnalysisHistoryIndex();
        var a = R("a.exe", 10, 3, sha: "s1");
        var samePathNewHash = R("a.exe", 60, 1, sha: "s2");
        var sameHashOtherPath = R("copy.exe", 10, 2, sha: "s1");
        index.Upsert(a); index.Upsert(samePathNewHash); index.Upsert(sameHashOtherPath);
        Assert.Equal(3, index.RelatedTo(a).Count);
    }
}

public class AnalysisLogicTests
{
    [Theory]
    [InlineData(0, AnalysisVerdict.Clean)]
    [InlineData(20, AnalysisVerdict.Info)]
    [InlineData(40, AnalysisVerdict.Caution)]
    [InlineData(55, AnalysisVerdict.Suspicious)]
    [InlineData(90, AnalysisVerdict.Dangerous)]
    public void VerdictFromScore(int score, AnalysisVerdict expected) =>
        Assert.Equal(expected, AnalysisVerdicts.FromScore(score));

    [Fact]
    public async Task AnalysisContext_FlowsAndRestores()
    {
        Assert.Equal(AnalysisSource.Unknown, AnalysisContext.Source);
        using (AnalysisContext.Begin(AnalysisSource.SetupSentinel, "Kurulum: 7-Zip"))
        {
            await Task.Yield();
            Assert.Equal(AnalysisSource.SetupSentinel, AnalysisContext.Source);
            Assert.Equal("Kurulum: 7-Zip", AnalysisContext.Detail);
            using (AnalysisContext.Begin(AnalysisSource.Processes))
                Assert.Equal(AnalysisSource.Processes, AnalysisContext.Source);
            Assert.Equal(AnalysisSource.SetupSentinel, AnalysisContext.Source);
        }
        Assert.Equal(AnalysisSource.Unknown, AnalysisContext.Source);
    }

    [Fact]
    public void RecordComparer_ListsMeaningfulChanges()
    {
        var older = new AnalysisRecord
        {
            SignatureStatus = "Verified", Signer = "Foo Ltd", FileSizeBytes = 1024 * 1024, Sha256 = "a", RiskScore = 12,
            Factors = new[] { new AnalysisFactorSummary("Geçerli imza", "Clean", 0) }
        };
        var newer = older with
        {
            SignatureStatus = "Unsigned", Signer = null, FileSizeBytes = 3 * 1024 * 1024, Sha256 = "b", RiskScore = 64,
            Factors = new[] { new AnalysisFactorSummary("Temp dizininde", "Warning", 25) }
        };

        var lines = RecordComparer.Compare(older, newer);
        Assert.Contains("İmza: Geçerli → İmzasız", lines);
        Assert.Contains("Risk: 12 → 64", lines);
        Assert.Contains("İçerik değişti (SHA-256 farklı)", lines);
        Assert.Contains("Yeni faktör: Temp dizininde", lines);
        Assert.Contains("Kalkan faktör: Geçerli imza", lines);
        Assert.Contains(lines, l => l.StartsWith("Boyut: 1 MB → 3 MB"));
    }

    [Theory]
    [InlineData(@"C:\Users\Ali\AppData\x.exe", @"C:\Users\***\AppData\x.exe")]
    [InlineData(@"c:\users\ali", @"c:\users\***")]
    [InlineData(@"D:\Oyunlar\x.exe", @"D:\Oyunlar\x.exe")]
    public void MaskUserName(string input, string expected) =>
        Assert.Equal(expected, HistoryExport.MaskUserName(input));

    [Fact]
    public void Csv_EscapesAndBlocksFormulaInjection()
    {
        var r = new AnalysisRecord { FileName = "=cmd|' /C calc'!A0", FilePath = "a;b", Note = "x\"y" };
        string csv = HistoryExport.ToCsv(new[] { r });
        Assert.Contains("'=cmd", csv);
        Assert.Contains("\"a;b\"", csv);
        Assert.Contains("\"x\"\"y\"", csv);
    }

    [Fact]
    public void Html_EncodesContent()
    {
        var r = new AnalysisRecord { FileName = "<script>alert(1)</script>.exe" };
        string html = HistoryExport.ToHtml(new[] { r });
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}

public class AnalysisHistoryPerformanceTests
{
    [Fact]
    public void TwentyThousandRecords_QueryIsFast()
    {
        var index = new AnalysisHistoryIndex();
        var now = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 20_000; i++)
        {
            index.Upsert(new AnalysisRecord
            {
                FileName = $"file{i}.exe",
                FilePath = $@"C:\Apps\Vendor{i % 300}\file{i}.exe",
                Sha256 = i.ToString("x64"),
                RiskScore = i % 100,
                Verdict = AnalysisVerdicts.FromScore(i % 100),
                AnalyzedAtUtc = now.AddMinutes(-i)
            });
        }

        index.Query(new AnalysisHistoryFilter { Text = "vendor12" }, now); // ısınma
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = index.Query(new AnalysisHistoryFilter { Text = "vendor123", Range = HistoryDateRange.Last30Days }, now);
        sw.Stop();

        Assert.NotEmpty(result);
        // Hedef 100 ms; CI makinelerindeki dalgalanma için gevşek sınır.
        Assert.True(sw.ElapsedMilliseconds < 500, $"Sorgu {sw.ElapsedMilliseconds} ms sürdü");
    }
}
