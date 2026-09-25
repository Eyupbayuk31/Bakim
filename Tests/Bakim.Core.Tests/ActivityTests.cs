using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bakım.Core.Activity;
using Xunit;

namespace Bakim.Core.Tests;

public sealed class ActivityStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bakim-activity-" + Guid.NewGuid().ToString("N"));
    private ActivityStore Store() => new(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static ActivityEntry Entry(string title, DateTime? at = null, ActivityKind kind = ActivityKind.Tweak, UndoState undo = UndoState.Undoable) => new()
    {
        AtUtc = at ?? new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc),
        Kind = kind,
        Module = "Windows Ayarları",
        Title = title,
        Summary = "özet",
        Outcome = ActivityOutcome.Succeeded,
        Undo = undo,
        UndoHandler = undo == UndoState.Undoable ? UndoHandlers.RegistryValues : null,
        Items = new[] { new ActivityItem(@"HKCU\Software\X|Y", "Set", "OK") }
    };

    [Fact]
    public void Append_ThenLoad_RoundTripsAllFields()
    {
        var e = Entry("Gizlilik ayarı") with { PayloadPath = @"C:\x", DeepLink = "Tweaker", RelatedId = "r1" };
        Store().Append(e);

        var loaded = Store().Load().Entries.Single();
        Assert.Equal(e.Id, loaded.Id);
        Assert.Equal(e.Kind, loaded.Kind);
        Assert.Equal(e.Undo, loaded.Undo);
        Assert.Equal(e.UndoHandler, loaded.UndoHandler);
        Assert.Equal(e.PayloadPath, loaded.PayloadPath);
        Assert.Equal(e.DeepLink, loaded.DeepLink);
        Assert.Equal(e.RelatedId, loaded.RelatedId);
        Assert.Single(loaded.Items);
        Assert.True(loaded.CanUndo);
    }

    [Fact]
    public void Enums_AreStoredAsNames()
    {
        Store().Append(Entry("a"));
        string text = File.ReadAllText(Store().EntriesPath);
        Assert.Contains("\"Tweak\"", text);
        Assert.Contains("\"Undoable\"", text);
    }

    [Fact]
    public void SameId_LastLineWins()
    {
        var e = Entry("a");
        var store = Store();
        store.Append(e);
        store.Append(e with { Undo = UndoState.Undone, UndoMessage = "3 değer geri yüklendi" });

        var loaded = store.Load();
        Assert.Equal(2, loaded.TotalLines);
        var only = Assert.Single(loaded.Entries);
        Assert.Equal(UndoState.Undone, only.Undo);
        Assert.False(only.CanUndo);
    }

    [Fact]
    public void CorruptLine_IsSkippedAndCounted()
    {
        var store = Store();
        store.Append(Entry("a"));
        File.AppendAllText(store.EntriesPath, "{bozuk\n");
        store.Append(Entry("b"));

        var loaded = store.Load();
        Assert.Equal(2, loaded.Entries.Count);
        Assert.Equal(1, loaded.CorruptLines);
    }

    [Fact]
    public void Load_ReturnsNewestFirst()
    {
        var store = Store();
        store.Append(Entry("eski", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        store.Append(Entry("yeni", new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(new[] { "yeni", "eski" }, store.Load().Entries.Select(e => e.Title));
    }

    [Fact]
    public void Append_TruncatesInlineItems()
    {
        var items = Enumerable.Range(0, 500).Select(i => new ActivityItem("t" + i, "Delete", "OK")).ToList();
        Store().Append(Entry("çok") with { Items = items });
        Assert.Equal(ActivityEntry.MaxInlineItems, Store().Load().Entries.Single().Items.Count);
    }

    [Fact]
    public void Maintain_DropsExpired_AndRemovesOrphanJournals()
    {
        var now = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        var store = Store();
        var old = Entry("eski", now.AddDays(-400));
        var fresh = Entry("yeni", now.AddDays(-1));
        store.Append(old);
        store.Append(fresh);
        string oldJournal = store.CreateJournal(old.Id);
        string freshJournal = store.CreateJournal(fresh.Id);

        int dropped = store.Maintain(now);

        Assert.Equal(1, dropped);
        Assert.Equal("yeni", store.Load().Entries.Single().Title);
        Assert.False(Directory.Exists(oldJournal));
        Assert.True(Directory.Exists(freshJournal));
    }

    [Fact]
    public void Maintain_CompactsBloatedFile()
    {
        var store = Store();
        var e = Entry("a");
        for (int i = 0; i < 250; i++) store.Append(e with { UndoMessage = i.ToString() });

        store.Maintain(e.AtUtc);

        var loaded = store.Load();
        Assert.Equal(1, loaded.TotalLines);
        Assert.Equal("249", loaded.Entries.Single().UndoMessage);
    }

    [Fact]
    public void Load_MissingFile_IsEmpty()
    {
        var loaded = Store().Load();
        Assert.Empty(loaded.Entries);
        Assert.Equal(0, loaded.TotalLines);
    }
}

public sealed class ActivityQueryTests
{
    private static ActivityEntry E(string title, ActivityKind kind, ActivityOutcome outcome = ActivityOutcome.Succeeded,
        UndoState undo = UndoState.NotUndoable, DateTime? at = null, string module = "Temizleyici") => new()
    {
        Title = title,
        Kind = kind,
        Outcome = outcome,
        Undo = undo,
        UndoHandler = undo == UndoState.Undoable ? UndoHandlers.RegistryRegImport : null,
        AtUtc = at ?? new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc),
        Module = module,
        Items = new[] { new ActivityItem(@"C:\Temp\foo.log", "Delete", "OK") }
    };

    private static readonly ActivityEntry[] Sample =
    {
        E("Tarayıcı önbellekleri temizlendi", ActivityKind.Clean),
        E("\"Foo Toolbar\" kaldırıldı", ActivityKind.Uninstall, undo: UndoState.Undoable, module: "Kaldırıcı"),
        E("Hizmet devre dışı", ActivityKind.ServiceChange, ActivityOutcome.Failed, module: "Hizmetler"),
    };

    [Fact]
    public void Filter_ByKind() =>
        Assert.Equal("\"Foo Toolbar\" kaldırıldı", ActivityQuery.Filter(Sample, new ActivityFilter(Kind: ActivityKind.Uninstall)).Single().Title);

    [Fact]
    public void Filter_ByOutcome() =>
        Assert.Equal(ActivityKind.ServiceChange, ActivityQuery.Filter(Sample, new ActivityFilter(Outcome: ActivityOutcome.Failed)).Single().Kind);

    [Fact]
    public void Filter_UndoableOnly() =>
        Assert.Equal(ActivityKind.Uninstall, ActivityQuery.Filter(Sample, new ActivityFilter(UndoableOnly: true)).Single().Kind);

    [Theory]
    [InlineData("TARAYICI", ActivityKind.Clean)]      // Türkçe büyük/küçük harf
    [InlineData("hizmetler", ActivityKind.ServiceChange)] // modül adı
    public void Filter_ByText(string text, ActivityKind expected) =>
        Assert.Equal(expected, ActivityQuery.Filter(Sample, new ActivityFilter(Text: text)).Single().Kind);

    [Fact]
    public void Filter_ByItemTarget() =>
        Assert.Equal(3, ActivityQuery.Filter(Sample, new ActivityFilter(Text: "foo.log")).Count());

    [Fact]
    public void Filter_ByModule() =>
        Assert.Single(ActivityQuery.Filter(Sample, new ActivityFilter(Module: "kaldırıcı")));

    [Fact]
    public void Filter_FromDate()
    {
        var list = new[] { E("eski", ActivityKind.Clean, at: new DateTime(2026, 1, 1)), E("yeni", ActivityKind.Clean, at: new DateTime(2026, 9, 1)) };
        Assert.Equal("yeni", ActivityQuery.Filter(list, new ActivityFilter(FromUtc: new DateTime(2026, 6, 1))).Single().Title);
    }

    [Fact]
    public void DayLabel_TodayYesterdayAndDate()
    {
        var now = new DateTime(2026, 9, 20, 15, 0, 0);
        Assert.Equal("Bugün", ActivityQuery.DayLabel(now.AddHours(-3), now));
        Assert.Equal("Dün", ActivityQuery.DayLabel(now.AddDays(-1), now));
        Assert.StartsWith("12 Eylül", ActivityQuery.DayLabel(new DateTime(2026, 9, 12), now));
        Assert.Contains("2025", ActivityQuery.DayLabel(new DateTime(2025, 9, 12), now));
    }

    [Fact]
    public void Retention_KeepsNewestWithinLimit()
    {
        var now = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        var list = Enumerable.Range(0, 10).Select(i => E("e" + i, ActivityKind.Clean, at: now.AddDays(-i))).ToList();
        var kept = ActivityQuery.ApplyRetention(list, now, TimeSpan.FromDays(5), 3);
        Assert.Equal(new[] { "e0", "e1", "e2" }, kept.Select(e => e.Title));
    }

    [Fact]
    public void MarkUndone_Success_And_Failure()
    {
        var original = Sample[1];
        var ok = ActivityQuery.MarkUndone(original, new UndoResult(true, 3, 0, "3 yedek geri yüklendi"), DateTime.UtcNow);
        Assert.Equal(UndoState.Undone, ok.Undo);
        Assert.Equal(original.Id, ok.Id);
        Assert.False(ok.CanUndo);

        var failed = ActivityQuery.MarkUndone(original, UndoResult.Fail("yedek yok"), DateTime.UtcNow);
        Assert.Equal(UndoState.UndoFailed, failed.Undo);
        Assert.Equal("yedek yok", failed.UndoMessage);
    }

    [Fact]
    public void RestoreEntry_IsLinkedAndNotUndoable()
    {
        var original = Sample[1];
        var restore = ActivityQuery.RestoreEntry(original, new UndoResult(true, 2, 1, "2 geri yüklendi, 1 başarısız"), DateTime.UtcNow);
        Assert.Equal(ActivityKind.Restore, restore.Kind);
        Assert.Equal(original.Id, restore.RelatedId);
        Assert.NotEqual(original.Id, restore.Id);
        Assert.Equal(UndoState.NotUndoable, restore.Undo);
        Assert.Equal(ActivityOutcome.PartiallySucceeded, restore.Outcome);
    }

    [Theory]
    [InlineData(true, 3, 0, ActivityOutcome.Succeeded)]
    [InlineData(true, 3, 1, ActivityOutcome.PartiallySucceeded)]
    [InlineData(false, 1, 2, ActivityOutcome.PartiallySucceeded)]
    [InlineData(false, 0, 2, ActivityOutcome.Failed)]
    public void UndoResult_Outcome(bool success, int restored, int failed, ActivityOutcome expected) =>
        Assert.Equal(expected, new UndoResult(success, restored, failed, "").Outcome);

    [Theory]
    [InlineData(5, 0, ActivityOutcome.Succeeded)]
    [InlineData(5, 1, ActivityOutcome.PartiallySucceeded)]
    [InlineData(0, 1, ActivityOutcome.Failed)]
    public void OutcomeFromCounts(int ok, int failed, ActivityOutcome expected) =>
        Assert.Equal(expected, ActivityQuery.OutcomeFromCounts(ok, failed));

    [Fact]
    public void AllKinds_HaveTurkishLabels()
    {
        foreach (ActivityKind k in Enum.GetValues<ActivityKind>())
            Assert.False(string.IsNullOrWhiteSpace(ActivityQuery.KindLabel(k)));
        Assert.NotEqual("Diğer", ActivityQuery.KindLabel(ActivityKind.Clean));
    }

    [Fact]
    public void Csv_EscapesQuotesAndHasHeader()
    {
        string csv = ActivityQuery.ToCsv(Sample);
        var lines = csv.TrimEnd().Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.StartsWith("Zaman;", lines[0]);
        Assert.Contains("\"\"\"Foo Toolbar\"\" kaldırıldı\"", csv);
    }
}
