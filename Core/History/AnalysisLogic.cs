using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Bakım.Core.Text;

namespace Bakım.Core.History
{
    /// <summary>
    /// Analizin kaynağını çağrı zinciri boyunca taşır. Kaydeden dekoratör, analizi başlatan
    /// modülü bilmeden doğru kaynak etiketini buradan okur:
    /// <code>using (AnalysisContext.Begin(AnalysisSource.SetupSentinel, "Kurulum: 7-Zip")) await analyzer.AnalyzeFileAsync(p);</code>
    /// </summary>
    public static class AnalysisContext
    {
        private static readonly AsyncLocal<(AnalysisSource Source, string? Detail)?> Current = new();

        public static AnalysisSource Source => Current.Value?.Source ?? AnalysisSource.Unknown;
        public static string? Detail => Current.Value?.Detail;

        public static IDisposable Begin(AnalysisSource source, string? detail = null)
        {
            var previous = Current.Value;
            Current.Value = (source, detail);
            return new Restore(previous);
        }

        private sealed class Restore : IDisposable
        {
            private readonly (AnalysisSource, string?)? _previous;
            private bool _done;
            public Restore((AnalysisSource, string?)? previous) => _previous = previous;

            public void Dispose()
            {
                if (_done) return;
                _done = true;
                Current.Value = _previous;
            }
        }
    }

    public static class AnalysisVerdicts
    {
        /// <summary>Risk puanından karar: 0–15 Temiz, –25 Bilgi, –45 Dikkat, –60 Şüpheli, üstü Tehlikeli.</summary>
        public static AnalysisVerdict FromScore(int riskScore, bool fileMissing = false)
        {
            if (fileMissing) return AnalysisVerdict.Missing;
            return riskScore switch
            {
                <= 15 => AnalysisVerdict.Clean,
                <= 25 => AnalysisVerdict.Info,
                <= 45 => AnalysisVerdict.Caution,
                <= 60 => AnalysisVerdict.Suspicious,
                _ => AnalysisVerdict.Dangerous
            };
        }

        public static string Display(AnalysisVerdict verdict) => verdict switch
        {
            AnalysisVerdict.Clean => "Temiz",
            AnalysisVerdict.Info => "Bilgi",
            AnalysisVerdict.Caution => "Dikkat",
            AnalysisVerdict.Suspicious => "Şüpheli",
            AnalysisVerdict.Dangerous => "Tehlikeli",
            AnalysisVerdict.Missing => "Dosya yok",
            _ => verdict.ToString()
        };

        public static string Display(AnalysisSource source) => source switch
        {
            AnalysisSource.Analyzer => "Analizör",
            AnalysisSource.ContextMenu => "Sağ tık",
            AnalysisSource.DragDrop => "Sürükle-bırak",
            AnalysisSource.SetupSentinel => "Nöbetçi",
            AnalysisSource.Uninstaller => "Kaldırıcı",
            AnalysisSource.Processes => "Süreçler",
            AnalysisSource.NetworkMonitor => "Ağ İzleyici",
            AnalysisSource.Startup => "Başlangıç",
            AnalysisSource.Scheduled => "Zamanlanmış",
            _ => "Bilinmiyor"
        };

        public static string Display(UserDecision decision) => decision switch
        {
            UserDecision.Trusted => "Güvenildi",
            UserDecision.Quarantined => "Karantina",
            UserDecision.Deleted => "Silindi",
            UserDecision.Disabled => "Devre dışı",
            UserDecision.Ignored => "Yok sayıldı",
            _ => string.Empty
        };
    }

    /// <summary>İki kalıcılık taraması arasındaki fark (eklenen, kaldırılan, değişen).</summary>
    public static class SnapshotDiff
    {
        /// <summary>Kararlı kimlik: kategori|konum|ad, küçük harf ve boşlukları kırpılmış.</summary>
        public static string KeyOf(string category, string location, string name) =>
            string.Join('|', Norm(category), Norm(location), Norm(name));

        private static string Norm(string? s) => (s ?? string.Empty).Trim().ToLowerInvariant();

        public static IReadOnlyList<SnapshotDiffItem> Compute(IEnumerable<PersistenceEntry> before, IEnumerable<PersistenceEntry> after)
        {
            var older = Index(before);
            var newer = Index(after);
            var result = new List<SnapshotDiffItem>();

            foreach (var (key, entry) in newer)
            {
                if (!older.TryGetValue(key, out var previous))
                {
                    result.Add(new SnapshotDiffItem(DiffKind.Added, null, entry, Array.Empty<string>()));
                    continue;
                }

                var changed = ChangedFields(previous, entry);
                if (changed.Count > 0) result.Add(new SnapshotDiffItem(DiffKind.Changed, previous, entry, changed));
            }

            foreach (var (key, entry) in older)
            {
                if (!newer.ContainsKey(key))
                    result.Add(new SnapshotDiffItem(DiffKind.Removed, entry, null, Array.Empty<string>()));
            }

            return result
                .OrderBy(d => d.Kind)
                .ThenBy(d => d.Current.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Current.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static IReadOnlyList<string> ChangedFields(PersistenceEntry a, PersistenceEntry b)
        {
            var fields = new List<string>();
            if (!SamePath(a.FilePath, b.FilePath)) fields.Add(nameof(PersistenceEntry.FilePath));
            if (!string.Equals(a.Arguments?.Trim(), b.Arguments?.Trim(), StringComparison.Ordinal)) fields.Add(nameof(PersistenceEntry.Arguments));
            if (a.IsEnabled != b.IsEnabled) fields.Add(nameof(PersistenceEntry.IsEnabled));
            if (!string.Equals(a.SignatureStatus, b.SignatureStatus, StringComparison.OrdinalIgnoreCase)) fields.Add(nameof(PersistenceEntry.SignatureStatus));
            if (!string.Equals(a.Signer ?? "", b.Signer ?? "", StringComparison.Ordinal)) fields.Add(nameof(PersistenceEntry.Signer));
            // Hash yalnızca iki tarafta da hesaplanmışsa karşılaştırılır (hesaplanmamış = bilinmiyor).
            if (!string.IsNullOrEmpty(a.Sha256) && !string.IsNullOrEmpty(b.Sha256) &&
                !string.Equals(a.Sha256, b.Sha256, StringComparison.OrdinalIgnoreCase)) fields.Add(nameof(PersistenceEntry.Sha256));
            return fields;
        }

        private static bool SamePath(string? a, string? b) =>
            string.Equals((a ?? "").Trim().Trim('"'), (b ?? "").Trim().Trim('"'), StringComparison.OrdinalIgnoreCase);

        /// <summary>Aynı anahtar birden çok kez geçerse (#2, #3…) ile ayrılır; hiçbir girdi kaybolmaz.</summary>
        private static Dictionary<string, PersistenceEntry> Index(IEnumerable<PersistenceEntry> entries)
        {
            var map = new Dictionary<string, PersistenceEntry>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                string key = string.IsNullOrEmpty(entry.Key) ? KeyOf(entry.Category, entry.Location, entry.Name) : entry.Key;
                string unique = key;
                for (int n = 2; map.ContainsKey(unique); n++) unique = $"{key}#{n}";
                map[unique] = entry;
            }
            return map;
        }

        /// <summary>"Yol", "İmza" gibi kullanıcıya gösterilecek alan adı.</summary>
        public static string FieldDisplay(string field) => field switch
        {
            nameof(PersistenceEntry.FilePath) => "Yol",
            nameof(PersistenceEntry.Arguments) => "Parametreler",
            nameof(PersistenceEntry.IsEnabled) => "Durum",
            nameof(PersistenceEntry.SignatureStatus) => "İmza",
            nameof(PersistenceEntry.Signer) => "İmzalayan",
            nameof(PersistenceEntry.Sha256) => "SHA-256",
            _ => field
        };
    }

    /// <summary>Aynı dosyanın iki analizi arasındaki fark ("Farkı göster").</summary>
    public static class RecordComparer
    {
        public static IReadOnlyList<string> Compare(AnalysisRecord older, AnalysisRecord newer)
        {
            var lines = new List<string>();

            if (!string.Equals(older.SignatureStatus, newer.SignatureStatus, StringComparison.OrdinalIgnoreCase))
                lines.Add($"İmza: {SignatureDisplay(older.SignatureStatus)} → {SignatureDisplay(newer.SignatureStatus)}");
            if (!string.Equals(older.Signer ?? "", newer.Signer ?? "", StringComparison.Ordinal))
                lines.Add($"İmzalayan: {Or(older.Signer)} → {Or(newer.Signer)}");
            if (older.FileSizeBytes != newer.FileSizeBytes)
                lines.Add($"Boyut: {ByteFormatter.Format(older.FileSizeBytes)} → {ByteFormatter.Format(newer.FileSizeBytes)}");
            if (!string.IsNullOrEmpty(older.Sha256) && !string.IsNullOrEmpty(newer.Sha256) &&
                !string.Equals(older.Sha256, newer.Sha256, StringComparison.OrdinalIgnoreCase))
                lines.Add("İçerik değişti (SHA-256 farklı)");
            if (!string.Equals(older.FileVersion ?? "", newer.FileVersion ?? "", StringComparison.Ordinal))
                lines.Add($"Sürüm: {Or(older.FileVersion)} → {Or(newer.FileVersion)}");
            if (older.RiskScore != newer.RiskScore)
                lines.Add($"Risk: {older.RiskScore} → {newer.RiskScore}");
            if (older.VirusTotalMalicious != newer.VirusTotalMalicious && newer.VirusTotalMalicious.HasValue)
                lines.Add($"VirusTotal: {VtText(older)} → {VtText(newer)}");

            var before = new HashSet<string>(older.Factors.Select(f => f.Title), StringComparer.Ordinal);
            var after = new HashSet<string>(newer.Factors.Select(f => f.Title), StringComparer.Ordinal);
            foreach (var added in newer.Factors.Where(f => !before.Contains(f.Title)))
                lines.Add($"Yeni faktör: {added.Title}");
            foreach (var removed in older.Factors.Where(f => !after.Contains(f.Title)))
                lines.Add($"Kalkan faktör: {removed.Title}");

            return lines;
        }

        public static string SignatureDisplay(string? status) => status switch
        {
            "Verified" => "Geçerli",
            "Unsigned" => "İmzasız",
            "InvalidOrTampered" => "Geçersiz",
            null or "" => "—",
            _ => status
        };

        private static string VtText(AnalysisRecord r) =>
            r.VirusTotalMalicious.HasValue && r.VirusTotalTotal.HasValue
                ? string.Create(CultureInfo.InvariantCulture, $"{r.VirusTotalMalicious}/{r.VirusTotalTotal}")
                : "—";

        private static string Or(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;
    }
}
