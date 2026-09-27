using System;
using System.Collections.Generic;
using Bakım.Core.Analyzer;
using Xunit;

namespace Bakim.Core.Tests;

/// <summary>Analizör v3 Faz 4: canlı izleme farkı, bildirim kararı, Nöbetçi kaynağı.</summary>
public class AnalyzerFaz4Tests
{
    [Fact]
    public void Watch_Added_IncludesHijackedCommand()
    {
        string k1 = PersistenceWatchRules.Key("RegistryRun", @"HKCU\…\Run → A", "A", @"C:\a.exe");
        var before = new HashSet<string> { k1 };
        var after = new[]
        {
            ("RegistryRun", @"HKCU\…\Run → A", "A", @"C:\a.exe"),
            ("RegistryRun", @"HKCU\…\Run → B", "B", @"C:\b.exe"),                 // yeni
            ("RegistryRun", @"HKCU\…\Run → A", "A", @"C:\Users\u\Temp\evil.exe"),  // aynı ad, hedef değişti
        };
        var added = PersistenceWatchRules.Added(before, after, x => PersistenceWatchRules.Key(x.Item1, x.Item2, x.Item3, x.Item4));
        Assert.Equal(2, added.Count);
        Assert.Contains(added, x => x.Item3 == "B");
        Assert.Contains(added, x => x.Item4.Contains("evil"));
    }

    [Theory]
    [InlineData(40, false, false, true)]
    [InlineData(20, false, false, false)]
    [InlineData(90, true, false, false)]   // doğrulanmış Microsoft: Windows güncellemesi
    [InlineData(40, false, true, false)]   // Oyun Modu: yalnız Tehlikeli
    [InlineData(80, false, true, true)]
    public void Watch_ShouldNotify(int score, bool microsoft, bool gameMode, bool expected) =>
        Assert.Equal(expected, PersistenceWatchRules.ShouldNotify(score, microsoft, gameMode));

    [Fact]
    public void Provenance_MatchesRunServiceAndTask_NewestWins()
    {
        var old = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var recent = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);
        var index = ProvenanceIndex.Build(new[]
        {
            new SetupTrail("Eski Uygulama", old, new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Updater" }),
            new SetupTrail("Notepad++", recent, new[]
            {
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run\Updater",
                @"HKLM\SYSTEM\CurrentControlSet\Services\NppSvc",
                @"\NotepadPlusPlus\Update"
            })
        });

        var run = index.Find(ProvenanceIndex.CandidateKeys(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "Updater", null, null));
        Assert.Equal("Notepad++", run?.App);
        Assert.Equal(recent, run?.When);

        Assert.Equal("Notepad++", index.Find(ProvenanceIndex.CandidateKeys(null, null, "nppsvc", null))?.App);
        Assert.Equal("Notepad++", index.Find(ProvenanceIndex.CandidateKeys(null, null, null, @"\NotepadPlusPlus\Update\"))?.App);
        Assert.Null(index.Find(ProvenanceIndex.CandidateKeys(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "Other", null, null)));
    }
}
