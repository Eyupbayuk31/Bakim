using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Bakım.Core.Activity
{
    public sealed record ActivityFilter(
        string? Text = null,
        ActivityKind? Kind = null,
        ActivityOutcome? Outcome = null,
        bool UndoableOnly = false,
        DateTime? FromUtc = null,
        string? Module = null);

    /// <summary>Etkinlik listesinin saf mantığı: filtre, gün grupları, saklama, metin.</summary>
    public static class ActivityQuery
    {
        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

        public static IEnumerable<ActivityEntry> Filter(IEnumerable<ActivityEntry> entries, ActivityFilter f)
        {
            foreach (var e in entries)
            {
                if (f.Kind is { } kind && e.Kind != kind) continue;
                if (f.Outcome is { } outcome && e.Outcome != outcome) continue;
                if (f.UndoableOnly && !e.CanUndo) continue;
                if (f.FromUtc is { } from && e.AtUtc < from) continue;
                if (!string.IsNullOrEmpty(f.Module) && !string.Equals(e.Module, f.Module, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(f.Text) && !Matches(e, f.Text.Trim())) continue;
                yield return e;
            }
        }

        private static bool Matches(ActivityEntry e, string text)
        {
            bool Has(string? s) => s != null && Tr.CompareInfo.IndexOf(s, text, CompareOptions.IgnoreCase) >= 0;
            return Has(e.Title) || Has(e.Summary) || Has(e.Module) || e.Items.Any(i => Has(i.Target));
        }

        /// <summary>Yerel saate göre gün başlığı: "Bugün", "Dün", "12 Eylül 2026 Cuma".</summary>
        public static string DayLabel(DateTime atLocal, DateTime nowLocal)
        {
            var day = atLocal.Date;
            if (day == nowLocal.Date) return "Bugün";
            if (day == nowLocal.Date.AddDays(-1)) return "Dün";
            return day.ToString(day.Year == nowLocal.Year ? "d MMMM dddd" : "d MMMM yyyy", Tr);
        }

        public static IReadOnlyList<ActivityEntry> ApplyRetention(IEnumerable<ActivityEntry> entries, DateTime nowUtc, TimeSpan retention, int maxEntries)
        {
            var cutoff = nowUtc - retention;
            return entries
                .Where(e => e.AtUtc >= cutoff)
                .OrderByDescending(e => e.AtUtc)
                .Take(Math.Max(0, maxEntries))
                .ToList();
        }

        /// <summary>Geri alma sonrası özgün kaydın yeni hali.</summary>
        public static ActivityEntry MarkUndone(ActivityEntry original, UndoResult result, DateTime nowUtc) => original with
        {
            Undo = result.Success ? UndoState.Undone : UndoState.UndoFailed,
            UndoMessage = result.Message,
            UndoneAtUtc = nowUtc
        };

        /// <summary>Geri alma işleminin kendi kaydı (Kind = Restore).</summary>
        public static ActivityEntry RestoreEntry(ActivityEntry original, UndoResult result, DateTime nowUtc) => new()
        {
            AtUtc = nowUtc,
            Kind = ActivityKind.Restore,
            Module = original.Module,
            Title = $"Geri alındı: {original.Title}",
            Summary = result.Message,
            Outcome = result.Outcome,
            Undo = UndoState.NotUndoable,
            RelatedId = original.Id,
            DeepLink = original.DeepLink,
            Items = result.Items ?? Array.Empty<ActivityItem>()
        };

        public static string KindLabel(ActivityKind kind) => kind switch
        {
            ActivityKind.Clean => "Temizlik",
            ActivityKind.Uninstall => "Kaldırma",
            ActivityKind.SetupSession => "Kurulum",
            ActivityKind.Tweak => "İnce ayar",
            ActivityKind.StartupChange => "Başlangıç",
            ActivityKind.ServiceChange => "Hizmet",
            ActivityKind.FirewallRule => "Güvenlik duvarı",
            ActivityKind.Quarantine => "Karantina",
            ActivityKind.Restore => "Geri alma",
            ActivityKind.GameModeSession => "Oyun Modu",
            ActivityKind.StoreInstall => "Mağaza kurulumu",
            ActivityKind.StoreUpdate => "Mağaza güncellemesi",
            ActivityKind.Analysis => "Analiz",
            ActivityKind.PersistenceScan => "Kalıcılık taraması",
            ActivityKind.ScheduledJob => "Zamanlanmış iş",
            ActivityKind.AppUpdate => "Bakım güncellemesi",
            _ => "Diğer"
        };

        public static string OutcomeLabel(ActivityOutcome outcome) => outcome switch
        {
            ActivityOutcome.Succeeded => "Başarılı",
            ActivityOutcome.PartiallySucceeded => "Kısmen başarılı",
            ActivityOutcome.Failed => "Başarısız",
            ActivityOutcome.Cancelled => "İptal edildi",
            _ => outcome.ToString()
        };

        public static string UndoLabel(UndoState state) => state switch
        {
            UndoState.Undoable => "Geri alınabilir",
            UndoState.Undone => "Geri alındı",
            UndoState.UndoFailed => "Geri alma başarısız",
            UndoState.Expired => "Yedek artık yok",
            _ => ""
        };

        /// <summary>Sayılardan sonuç: hepsi tamam → Başarılı, hiçbiri → Başarısız, arası → Kısmen.</summary>
        public static ActivityOutcome OutcomeFromCounts(int succeeded, int failed) =>
            failed == 0 ? ActivityOutcome.Succeeded
            : succeeded == 0 ? ActivityOutcome.Failed
            : ActivityOutcome.PartiallySucceeded;

        /// <summary>CSV dışa aktarma (Excel uyumlu, ';' ayraçlı).</summary>
        public static string ToCsv(IEnumerable<ActivityEntry> entries)
        {
            static string Q(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Zaman;Modül;Tür;Başlık;Özet;Sonuç;Geri alma");
            foreach (var e in entries)
            {
                sb.Append(Q(e.AtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(';')
                  .Append(Q(e.Module)).Append(';')
                  .Append(Q(KindLabel(e.Kind))).Append(';')
                  .Append(Q(e.Title)).Append(';')
                  .Append(Q(e.Summary)).Append(';')
                  .Append(Q(OutcomeLabel(e.Outcome))).Append(';')
                  .Append(Q(UndoLabel(e.Undo))).AppendLine();
            }
            return sb.ToString();
        }
    }
}
