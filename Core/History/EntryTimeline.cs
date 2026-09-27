using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Bakım.Core.History
{
    public enum TimelineEventKind
    {
        FirstSeen,
        Added,
        Changed,
        Disabled,
        Enabled,
        Removed,
        Returned,
        Provenance,
        Decision
    }

    public sealed record TimelineEvent(DateTime AtUtc, TimelineEventKind Kind, string Text);

    /// <summary>Zaman çizelgesi girdisi olarak tek bir tarama: zaman, tetikleyici ve girdiler.</summary>
    public sealed record TimelineSnapshot(DateTime TakenAtUtc, string Trigger, IReadOnlyList<PersistenceEntry> Entries);

    /// <summary>
    /// Girdi başına zaman çizelgesi (ANL Faz 7): aynı ayrıştırıcı sürümüyle alınmış taramalardan ilk görülüş,
    /// değişiklikler, kapatma/açma, kaldırılma ve geri dönüş olayları çıkarılır.
    /// </summary>
    public static class EntryTimeline
    {
        public static IReadOnlyList<TimelineEvent> Build(string key, IEnumerable<TimelineSnapshot> snapshots)
        {
            var events = new List<TimelineEvent>();
            PersistenceEntry? previous = null;
            bool seen = false;
            bool first = true;

            foreach (var snap in snapshots.OrderBy(s => s.TakenAtUtc))
            {
                var current = snap.Entries.FirstOrDefault(e => string.Equals(KeyFor(e), key, StringComparison.Ordinal));
                if (current != null && previous == null)
                {
                    if (!seen)
                        events.Add(new TimelineEvent(snap.TakenAtUtc, first ? TimelineEventKind.FirstSeen : TimelineEventKind.Added,
                            first ? "Kayıtlı ilk taramada vardı" : "Eklendi (önceki taramada yoktu)"));
                    else
                        events.Add(new TimelineEvent(snap.TakenAtUtc, TimelineEventKind.Returned, "Yeniden ortaya çıktı"));
                    seen = true;
                }
                else if (current == null && previous != null)
                {
                    events.Add(new TimelineEvent(snap.TakenAtUtc, TimelineEventKind.Removed, "Artık yok (kaldırıldı)"));
                }
                else if (current != null && previous != null)
                {
                    var fields = SnapshotDiff.ChangedFields(previous, current);
                    if (fields.Contains(nameof(PersistenceEntry.IsEnabled)))
                        events.Add(new TimelineEvent(snap.TakenAtUtc,
                            current.IsEnabled ? TimelineEventKind.Enabled : TimelineEventKind.Disabled,
                            current.IsEnabled ? "Etkinleştirildi" : "Devre dışı bırakıldı"));
                    var others = fields.Where(f => f != nameof(PersistenceEntry.IsEnabled)).ToList();
                    if (others.Count > 0)
                        events.Add(new TimelineEvent(snap.TakenAtUtc, TimelineEventKind.Changed,
                            "Değişti: " + string.Join(", ", others.Select(SnapshotDiff.FieldDisplay)) + Detail(previous, current, others)));
                }

                previous = current;
                first = false;
            }
            return events;
        }

        private static string KeyFor(PersistenceEntry e) =>
            string.IsNullOrEmpty(e.Key) ? SnapshotDiff.KeyOf(e.Category, e.Location, e.Name) : e.Key;

        /// <summary>Yol değiştiyse eski → yeni yol; en sık sorulan soru "nereye taşındı?".</summary>
        private static string Detail(PersistenceEntry before, PersistenceEntry after, IReadOnlyList<string> fields) =>
            fields.Contains(nameof(PersistenceEntry.FilePath)) ? $" ({before.FilePath} → {after.FilePath})" : string.Empty;

        /// <summary>Olayları tarih sırasıyla, her satır "27.09.2026 14:02 · metin" biçiminde yazar.</summary>
        public static string Format(IEnumerable<TimelineEvent> events, TimeZoneInfo? zone = null)
        {
            zone ??= TimeZoneInfo.Local;
            var culture = CultureInfo.GetCultureInfo("tr-TR");
            return string.Join(Environment.NewLine, events
                .OrderBy(e => e.AtUtc)
                .Select(e => $"{TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(e.AtUtc, DateTimeKind.Utc), zone).ToString("dd.MM.yyyy HH:mm", culture)} · {e.Text}"));
        }
    }
}
