using System;
using System.Collections.Generic;
using System.Linq;
using Bakım.Core.History;
using Xunit;

namespace Bakim.Core.Tests;

/// <summary>Analizör v3 Faz 7: kararlı anahtar ve girdi başına zaman çizelgesi.</summary>
public class AnalyzerFaz7Tests
{
    [Fact]
    public void StableKey_Service_IgnoresStartTypeAndDisplayName()
    {
        string auto = SnapshotDiff.StableKey("WindowsService", "Hizmet: Foo (Otomatik)", "Foo Service", null, "Foo", null);
        string disabled = SnapshotDiff.StableKey("WindowsService", "Hizmet: Foo (Devre dışı)", "Foo Hizmeti", null, "foo", null);
        Assert.Equal(auto, disabled);
    }

    [Fact]
    public void StableKey_Task_UsesTaskPath()
    {
        string a = SnapshotDiff.StableKey("ScheduledTask", @"Görev: \Vendor\Update", "Update", @"\Vendor\Update", null, null);
        string b = SnapshotDiff.StableKey("ScheduledTask", @"Görev: \Vendor\Update", "Güncelleme", @"\Vendor\Update", null, null);
        Assert.Equal(a, b);
        Assert.NotEqual(a, SnapshotDiff.StableKey("ScheduledTask", @"Görev: \Other\Update", "Update", @"\Other\Update", null, null));
    }

    [Fact]
    public void StableKey_Run_DistinguishesHives_AndFallsBackToLegacyKey()
    {
        string hkcu = SnapshotDiff.StableKey("RegistryRun", @"HKCU\…\Run → App", "App", null, null, @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run");
        string hklm = SnapshotDiff.StableKey("RegistryRun", @"HKLM\…\Run → App", "App", null, null, @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run");
        Assert.NotEqual(hkcu, hklm);
        Assert.Equal(SnapshotDiff.KeyOf("StartupFolder", "Başlangıç", "x.lnk"),
            SnapshotDiff.StableKey("StartupFolder", "Başlangıç", "x.lnk", null, null, null));
    }

    [Fact]
    public void Diff_ServiceDisabled_IsChangeNotRemoveAdd()
    {
        var before = new[] { Svc("Hizmet: Foo (Otomatik)", enabled: true) };
        var after = new[] { Svc("Hizmet: Foo (Devre dışı)", enabled: false) };
        var diff = SnapshotDiff.Compute(before, after);
        var item = Assert.Single(diff);
        Assert.Equal(DiffKind.Changed, item.Kind);
        Assert.Contains(nameof(PersistenceEntry.IsEnabled), item.ChangedFields);
    }

    private static readonly DateTime T0 = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
    private const string OldPath = @"C:\Program Files\Foo\foo.exe";
    private const string NewPath = @"C:\Foo2\foo.exe";

    private static PersistenceEntry Svc(string location, bool enabled = true, string path = OldPath) =>
        new(SnapshotDiff.StableKey("WindowsService", location, "Foo", null, "Foo", null), "WindowsService", "Foo", location, path, "", enabled, "Valid", "Foo Ltd", null);

    private static PersistenceEntry Other(string name) =>
        new(SnapshotDiff.KeyOf("RegistryRun", "HKCU", name), "RegistryRun", name, "HKCU", @"C:\x\" + name + ".exe", "", true, "Valid", null, null);

    private static TimelineSnapshot Snap(int day, params PersistenceEntry[] entries) => new(T0.AddDays(day), "Manual", entries);

    private static string Key => Svc("x").Key;

    [Fact]
    public void Timeline_FullLifecycle()
    {
        var events = EntryTimeline.Build(Key, new[]
        {
            Snap(3, Svc("a", enabled: false, path: NewPath)), // sıra karışık verilir; tarihe göre sıralanmalı
            Snap(0, Other("x")),
            Snap(1, Svc("a"), Other("x")),
            Snap(2, Svc("a", path: NewPath)),
            Snap(4),
            Snap(5, Svc("a", enabled: false, path: NewPath)),
        });

        Assert.Equal(new[]
        {
            TimelineEventKind.Added,
            TimelineEventKind.Changed,
            TimelineEventKind.Disabled,
            TimelineEventKind.Removed,
            TimelineEventKind.Returned
        }, events.Select(e => e.Kind));
        Assert.Equal(T0.AddDays(1), events[0].AtUtc);
        Assert.Contains(NewPath, events[1].Text);
        Assert.Contains("Yol", events[1].Text);
    }

    [Fact]
    public void Timeline_PresentInFirstSnapshot_IsFirstSeen()
    {
        var events = EntryTimeline.Build(Key, new[] { Snap(0, Svc("a")), Snap(1, Svc("a")) });
        var only = Assert.Single(events);
        Assert.Equal(TimelineEventKind.FirstSeen, only.Kind);
    }

    [Fact]
    public void Timeline_NeverSeen_IsEmpty()
    {
        Assert.Empty(EntryTimeline.Build(Key, new[] { Snap(0, Other("x")) }));
        Assert.Empty(EntryTimeline.Build(Key, Array.Empty<TimelineSnapshot>()));
    }

    [Fact]
    public void Timeline_Format_SortsAndUsesGivenZone()
    {
        var events = new List<TimelineEvent>
        {
            new(T0.AddHours(2), TimelineEventKind.Decision, "Karar: Güvenilir"),
            new(T0, TimelineEventKind.Added, "Eklendi")
        };
        string text = EntryTimeline.Format(events, TimeZoneInfo.Utc);
        Assert.Equal($"01.09.2026 10:00 · Eklendi{Environment.NewLine}01.09.2026 12:00 · Karar: Güvenilir", text);
    }

    [Fact]
    public void ParserVersion_BumpedForStableKeys()
    {
        Assert.Equal(3, PersistenceSnapshot.CurrentParserVersion);
    }
}
