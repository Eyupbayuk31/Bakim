using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakım.Core.History
{
    /// <summary>Saklama kuralı: varsayılan 365 gün ve 20.000 kayıt. Güvenilen/karantinadaki kayıtlar korunur.</summary>
    public sealed record HistoryRetention(TimeSpan MaxAge, int MaxRecords)
    {
        public static HistoryRetention Default { get; } = new(TimeSpan.FromDays(365), 20_000);
    }

    /// <summary>
    /// Bellek içi geçmiş indeksi: kimlik, SHA-256 ve yol sözlükleri. Arama ve filtreleme
    /// 20.000 kayıtta milisaniyeler içinde biter (disk erişimi yok).
    /// </summary>
    public sealed class AnalysisHistoryIndex
    {
        private readonly Dictionary<string, AnalysisRecord> _byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _byHash = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _byPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new();

        public int Count
        {
            get { lock (_gate) return _byId.Count; }
        }

        public IReadOnlyList<AnalysisRecord> All()
        {
            lock (_gate) return _byId.Values.ToList();
        }

        public void Clear()
        {
            lock (_gate)
            {
                _byId.Clear();
                _byHash.Clear();
                _byPath.Clear();
            }
        }

        public void Upsert(AnalysisRecord record)
        {
            lock (_gate)
            {
                if (_byId.TryGetValue(record.Id, out var old)) Unlink(old);
                _byId[record.Id] = record;
                Link(_byHash, record.Sha256, record.Id);
                Link(_byPath, PathKey(record.FilePath), record.Id);
            }
        }

        public bool Remove(string id)
        {
            lock (_gate)
            {
                if (!_byId.Remove(id, out var old)) return false;
                Unlink(old);
                return true;
            }
        }

        public AnalysisRecord? Get(string id)
        {
            lock (_gate) return _byId.TryGetValue(id, out var r) ? r : null;
        }

        public IReadOnlyList<AnalysisRecord> GetByHash(string? sha256) => Lookup(_byHash, sha256);

        public IReadOnlyList<AnalysisRecord> GetByPath(string? path) => Lookup(_byPath, PathKey(path));

        /// <summary>Aynı dosyanın (önce hash, yoksa yol) tüm kayıtları, yeniden eskiye.</summary>
        public IReadOnlyList<AnalysisRecord> RelatedTo(AnalysisRecord record)
        {
            var byHash = GetByHash(record.Sha256);
            var byPath = GetByPath(record.FilePath);
            return byHash.Concat(byPath)
                         .GroupBy(r => r.Id).Select(g => g.First())
                         .OrderByDescending(r => r.AnalyzedAtUtc)
                         .ToList();
        }

        /// <summary>Hash itibar önbelleği: aynı SHA-256'nın <paramref name="maxAge"/> içindeki en yeni kaydı.</summary>
        public AnalysisRecord? LatestByHash(string? sha256, TimeSpan maxAge, DateTime nowUtc) =>
            GetByHash(sha256).FirstOrDefault(r => nowUtc - r.AnalyzedAtUtc <= maxAge);

        /// <summary>Bu hash için kullanıcı "Güvendi" dediyse true (en yeni karar geçerlidir).</summary>
        public bool IsTrustedHash(string? sha256)
        {
            var decided = GetByHash(sha256).Where(r => r.Decision != UserDecision.None)
                                           .OrderByDescending(r => r.DecisionAtUtc ?? r.AnalyzedAtUtc)
                                           .FirstOrDefault();
            return decided?.Decision == UserDecision.Trusted;
        }

        public IReadOnlyList<AnalysisRecord> Query(AnalysisHistoryFilter filter, DateTime nowUtc)
        {
            DateTime? from = filter.Range switch
            {
                HistoryDateRange.Today => nowUtc.ToLocalTime().Date.ToUniversalTime(),
                HistoryDateRange.Last7Days => nowUtc.AddDays(-7),
                HistoryDateRange.Last30Days => nowUtc.AddDays(-30),
                _ => null
            };
            string? text = string.IsNullOrWhiteSpace(filter.Text) ? null : filter.Text.Trim();

            IEnumerable<AnalysisRecord> q;
            lock (_gate) q = _byId.Values.ToList();

            if (from.HasValue) q = q.Where(r => r.AnalyzedAtUtc >= from.Value);
            if (filter.Verdicts is { Count: > 0 } verdicts) q = q.Where(r => verdicts.Contains(r.Verdict));
            if (filter.Source.HasValue) q = q.Where(r => r.Source == filter.Source.Value);
            if (!string.IsNullOrEmpty(filter.SignatureStatus))
                q = q.Where(r => string.Equals(r.SignatureStatus, filter.SignatureStatus, StringComparison.OrdinalIgnoreCase));
            if (filter.OnlyVirusTotalHits) q = q.Where(r => r.HasVirusTotalHit);
            if (filter.OnlyUndecided) q = q.Where(r => r.Decision == UserDecision.None);
            if (text != null) q = q.Where(r => Matches(r, text));

            return q.OrderByDescending(r => r.AnalyzedAtUtc).Take(Math.Max(1, filter.Limit)).ToList();
        }

        /// <summary>Saklama kuralına göre silinecek kayıtların kimlikleri (korunanlar hariç).</summary>
        public IReadOnlyList<string> SelectForPurge(HistoryRetention retention, DateTime nowUtc)
        {
            List<AnalysisRecord> all;
            lock (_gate) all = _byId.Values.ToList();

            var purge = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in all)
            {
                if (!r.IsKeptForever && nowUtc - r.AnalyzedAtUtc > retention.MaxAge) purge.Add(r.Id);
            }

            var remaining = all.Where(r => !purge.Contains(r.Id)).OrderByDescending(r => r.AnalyzedAtUtc).ToList();
            int over = remaining.Count - retention.MaxRecords;
            if (over > 0)
            {
                foreach (var r in remaining.Where(r => !r.IsKeptForever).Reverse().Take(over)) purge.Add(r.Id);
            }
            return purge.ToList();
        }

        private static bool Matches(AnalysisRecord r, string text) =>
            r.FileName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            r.FilePath.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            r.Sha256.StartsWith(text, StringComparison.OrdinalIgnoreCase) ||
            (r.Signer?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (r.Note?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (r.SourceDetail?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);

        private IReadOnlyList<AnalysisRecord> Lookup(Dictionary<string, HashSet<string>> map, string? key)
        {
            if (string.IsNullOrEmpty(key)) return Array.Empty<AnalysisRecord>();
            lock (_gate)
            {
                if (!map.TryGetValue(key, out var ids)) return Array.Empty<AnalysisRecord>();
                return ids.Select(id => _byId[id]).OrderByDescending(r => r.AnalyzedAtUtc).ToList();
            }
        }

        private void Unlink(AnalysisRecord record)
        {
            UnlinkFrom(_byHash, record.Sha256, record.Id);
            UnlinkFrom(_byPath, PathKey(record.FilePath), record.Id);
        }

        private static void Link(Dictionary<string, HashSet<string>> map, string? key, string id)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!map.TryGetValue(key, out var set)) map[key] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(id);
        }

        private static void UnlinkFrom(Dictionary<string, HashSet<string>> map, string? key, string id)
        {
            if (string.IsNullOrEmpty(key) || !map.TryGetValue(key, out var set)) return;
            set.Remove(id);
            if (set.Count == 0) map.Remove(key);
        }

        private static string PathKey(string? path) => (path ?? string.Empty).Trim().Trim('"').Replace('/', '\\');
    }
}
