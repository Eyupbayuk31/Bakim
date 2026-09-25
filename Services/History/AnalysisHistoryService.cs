using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakım.Core.History;
using Bakım.Models;

namespace Bakım.Services.History
{
    /// <summary>Analizör Geçmişi (§6): her dosya analizini ve her kalıcılık taramasını kalıcı olarak saklar.</summary>
    public interface IAnalysisHistoryService
    {
        int Count { get; }

        Task<AnalysisRecord?> RecordAsync(ThreatAnalysisResult result, AnalysisSource source, string? sourceDetail = null);
        Task SetDecisionAsync(string recordId, UserDecision decision, string? note = null);
        Task SetNoteAsync(string recordId, string? note);

        AnalysisRecord? Get(string recordId);
        IReadOnlyList<AnalysisRecord> Query(AnalysisHistoryFilter filter);
        IReadOnlyList<AnalysisRecord> GetRelated(AnalysisRecord record);
        IReadOnlyList<AnalysisRecord> GetByHash(string sha256);
        IReadOnlyList<AnalysisRecord> GetByPath(string path);
        /// <summary>Hash itibar önbelleği: aynı SHA-256'nın <paramref name="maxAge"/> içindeki en yeni kaydı.</summary>
        AnalysisRecord? GetLatestByHash(string sha256, TimeSpan maxAge);
        bool IsTrustedHash(string sha256);

        Task<SnapshotHeader> SaveSnapshotAsync(IEnumerable<PersistenceItem> items, string trigger);
        IReadOnlyList<SnapshotHeader> ListSnapshots();
        IReadOnlyList<SnapshotDiffItem> Diff(string olderSnapshotId, string newerSnapshotId);
        /// <summary>Son iki taramanın farkı; iki tarama yoksa boş.</summary>
        IReadOnlyList<SnapshotDiffItem> DiffLatest();

        Task<int> RemoveAsync(IEnumerable<string> recordIds);
        Task<int> PurgeAsync(HistoryRetention retention);
        Task ClearAllAsync();
        Task<string> ExportAsync(string path, ExportFormat format, AnalysisHistoryFilter filter, bool maskUserName);

        event Action<AnalysisRecord>? RecordAdded;
        event Action? HistoryChanged;
    }

    public sealed class AnalysisHistoryService : IAnalysisHistoryService
    {
        public const int MaxSnapshots = 50;

        private readonly AnalysisHistoryStore _store;
        private readonly AnalysisHistoryIndex _index = new();
        private readonly ILogService _log;
        private readonly object _loadGate = new();
        private bool _loaded;
        private int _appendsSinceMaintenance;

        public AnalysisHistoryService(ILogService log) : this(log, new AnalysisHistoryStore(AnalysisHistoryStore.DefaultDirectory())) { }

        public AnalysisHistoryService(ILogService log, AnalysisHistoryStore store)
        {
            _log = log;
            _store = store;
        }

        public event Action<AnalysisRecord>? RecordAdded;
        public event Action? HistoryChanged;

        public int Count
        {
            get { EnsureLoaded(); return _index.Count; }
        }

        private void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_loadGate)
            {
                if (_loaded) return;
                try
                {
                    var result = _store.Load();
                    foreach (var r in result.Records) _index.Upsert(r);
                    if (result.CorruptLines > 0)
                        _log.Warning($"Analizör geçmişinde {result.CorruptLines} bozuk satır atlandı.", null, nameof(AnalysisHistoryService));

                    if (_store.NeedsCompaction(result.Records.Count, result.TotalLines))
                        _store.Rewrite(_index.All());
                }
                catch (Exception ex)
                {
                    _log.Error("Analizör geçmişi yüklenemedi; boş geçmişle devam ediliyor.", ex, nameof(AnalysisHistoryService));
                }
                _loaded = true;
            }
        }

        public Task<AnalysisRecord?> RecordAsync(ThreatAnalysisResult result, AnalysisSource source, string? sourceDetail = null)
        {
            return Task.Run<AnalysisRecord?>(() =>
            {
                if (string.IsNullOrWhiteSpace(result.FilePath)) return null;
                EnsureLoaded();

                var record = ToRecord(result, source, sourceDetail);

                // Kullanıcı bu hash'e daha önce "Güvendi" dediyse karar yeni kayda taşınır.
                if (!string.IsNullOrEmpty(record.Sha256) && _index.IsTrustedHash(record.Sha256))
                    record = record with { Decision = UserDecision.Trusted, DecisionAtUtc = DateTime.UtcNow };

                try
                {
                    _store.Append(record);
                }
                catch (Exception ex)
                {
                    _log.Warning("Analiz kaydı diske yazılamadı.", ex, nameof(AnalysisHistoryService));
                    return null;
                }

                _index.Upsert(record);
                MaintainOccasionally();
                RecordAdded?.Invoke(record);
                return record;
            });
        }

        public Task SetDecisionAsync(string recordId, UserDecision decision, string? note = null) =>
            UpdateAsync(recordId, r => r with
            {
                Decision = decision,
                DecisionAtUtc = decision == UserDecision.None ? null : DateTime.UtcNow,
                Note = note ?? r.Note
            });

        public Task SetNoteAsync(string recordId, string? note) =>
            UpdateAsync(recordId, r => r with { Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() });

        private Task UpdateAsync(string recordId, Func<AnalysisRecord, AnalysisRecord> change)
        {
            return Task.Run(() =>
            {
                EnsureLoaded();
                var current = _index.Get(recordId);
                if (current == null) return;
                var updated = change(current);
                _store.Append(updated); // son satır geçerli
                _index.Upsert(updated);
                HistoryChanged?.Invoke();
            });
        }

        public AnalysisRecord? Get(string recordId)
        {
            EnsureLoaded();
            return _index.Get(recordId);
        }

        public IReadOnlyList<AnalysisRecord> Query(AnalysisHistoryFilter filter)
        {
            EnsureLoaded();
            return _index.Query(filter, DateTime.UtcNow);
        }

        public IReadOnlyList<AnalysisRecord> GetRelated(AnalysisRecord record)
        {
            EnsureLoaded();
            return _index.RelatedTo(record);
        }

        public IReadOnlyList<AnalysisRecord> GetByHash(string sha256)
        {
            EnsureLoaded();
            return _index.GetByHash(sha256);
        }

        public IReadOnlyList<AnalysisRecord> GetByPath(string path)
        {
            EnsureLoaded();
            return _index.GetByPath(path);
        }

        public AnalysisRecord? GetLatestByHash(string sha256, TimeSpan maxAge)
        {
            EnsureLoaded();
            return _index.LatestByHash(sha256, maxAge, DateTime.UtcNow);
        }

        public bool IsTrustedHash(string sha256)
        {
            EnsureLoaded();
            return _index.IsTrustedHash(sha256);
        }

        public Task<SnapshotHeader> SaveSnapshotAsync(IEnumerable<PersistenceItem> items, string trigger)
        {
            var entries = items.Select(ToEntry).ToList();
            return Task.Run(() =>
            {
                var header = _store.SaveSnapshot(new PersistenceSnapshot
                {
                    TakenAtUtc = DateTime.UtcNow,
                    Trigger = trigger,
                    Entries = entries
                });
                try { _store.PruneSnapshots(MaxSnapshots); }
                catch (Exception ex) { _log.Warning("Eski kalıcılık anlık görüntüleri silinemedi.", ex, nameof(AnalysisHistoryService)); }
                HistoryChanged?.Invoke();
                return header;
            });
        }

        public IReadOnlyList<SnapshotHeader> ListSnapshots() => _store.ListSnapshots();

        public IReadOnlyList<SnapshotDiffItem> Diff(string olderSnapshotId, string newerSnapshotId)
        {
            var older = _store.LoadSnapshot(olderSnapshotId);
            var newer = _store.LoadSnapshot(newerSnapshotId);
            if (older == null || newer == null) return Array.Empty<SnapshotDiffItem>();
            return SnapshotDiff.Compute(older.Entries, newer.Entries);
        }

        public IReadOnlyList<SnapshotDiffItem> DiffLatest()
        {
            var list = _store.ListSnapshots();
            return list.Count < 2 ? Array.Empty<SnapshotDiffItem>() : Diff(list[1].Id, list[0].Id);
        }

        public Task<int> RemoveAsync(IEnumerable<string> recordIds)
        {
            var ids = recordIds.ToList();
            return Task.Run(() =>
            {
                EnsureLoaded();
                int removed = ids.Count(id => _index.Remove(id));
                if (removed > 0)
                {
                    _store.Rewrite(_index.All());
                    HistoryChanged?.Invoke();
                }
                return removed;
            });
        }

        public Task<int> PurgeAsync(HistoryRetention retention) =>
            Task.Run(() =>
            {
                EnsureLoaded();
                return PurgeCore(retention);
            });

        private int PurgeCore(HistoryRetention retention)
        {
            var ids = _index.SelectForPurge(retention, DateTime.UtcNow);
            foreach (var id in ids) _index.Remove(id);
            if (ids.Count > 0)
            {
                _store.Rewrite(_index.All());
                HistoryChanged?.Invoke();
            }
            return ids.Count;
        }

        public Task ClearAllAsync() =>
            Task.Run(() =>
            {
                EnsureLoaded();
                _store.ClearAll();
                _index.Clear();
                HistoryChanged?.Invoke();
            });

        public Task<string> ExportAsync(string path, ExportFormat format, AnalysisHistoryFilter filter, bool maskUserName) =>
            Task.Run(() =>
            {
                var records = Query(filter with { Limit = int.MaxValue });
                string content = HistoryExport.Render(records, format, maskUserName);
                // CSV Excel'de Türkçe karakterlerin doğru görünmesi için BOM'lu UTF-8.
                File.WriteAllText(path, content, new UTF8Encoding(format == ExportFormat.Csv));
                return path;
            });

        /// <summary>Her 200 eklemede bir: saklama kuralı ve dosya sıkıştırma.</summary>
        private void MaintainOccasionally()
        {
            if (++_appendsSinceMaintenance < 200) return;
            _appendsSinceMaintenance = 0;
            try
            {
                if (PurgeCore(HistoryRetention.Default) == 0 && _store.RecordsFileSize > AnalysisHistoryStore.CompactionThresholdBytes)
                    _store.Rewrite(_index.All());
            }
            catch (Exception ex)
            {
                _log.Warning("Analizör geçmişi bakımı yapılamadı.", ex, nameof(AnalysisHistoryService));
            }
        }

        #region Eşleme

        public static AnalysisRecord ToRecord(ThreatAnalysisResult r, AnalysisSource source, string? detail)
        {
            DateTime? lastWrite = null;
            bool missing = false;
            try
            {
                if (File.Exists(r.FilePath)) lastWrite = File.GetLastWriteTimeUtc(r.FilePath);
                else missing = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

            string signature = r.IsSigned
                ? "Verified"
                : r.DigitalSignatureText.Contains("GEÇERSİZ", StringComparison.OrdinalIgnoreCase) || r.DigitalSignatureText.Contains("Bozulmuş", StringComparison.OrdinalIgnoreCase)
                    ? "InvalidOrTampered"
                    : "Unsigned";

            bool vtChecked = r.VirusTotalMalicious >= 0 && r.VirusTotalTotal > 0;

            return new AnalysisRecord
            {
                AnalyzedAtUtc = DateTime.UtcNow,
                Source = source,
                SourceDetail = detail,
                FilePath = r.FilePath,
                FileName = string.IsNullOrEmpty(r.FileName) ? Path.GetFileName(r.FilePath) : r.FileName,
                FileSizeBytes = r.FileSizeBytes,
                Sha256 = r.Sha256 ?? string.Empty,
                Md5 = NullIfEmpty(r.Md5),
                Sha1 = NullIfEmpty(r.Sha1),
                FileLastWriteUtc = lastWrite,
                SignatureStatus = signature,
                Signer = NullIfEmpty(r.SignerName),
                IsCatalogSigned = r.IsCatalogSigned,
                CompanyName = NullIfEmpty(r.CompanyName),
                ProductName = NullIfEmpty(r.ProductName),
                FileVersion = NullIfEmpty(r.FileVersion),
                RiskScore = r.RiskScore,
                Verdict = AnalysisVerdicts.FromScore(r.RiskScore, missing),
                Factors = r.Factors.Select(f => new AnalysisFactorSummary(f.Title, f.Severity.ToString(), f.ScoreImpact)).ToList(),
                Recommendation = NullIfEmpty(r.Recommendation),
                VirusTotalMalicious = vtChecked ? r.VirusTotalMalicious : null,
                VirusTotalTotal = vtChecked ? r.VirusTotalTotal : null,
                VirusTotalCheckedAtUtc = vtChecked ? DateTime.UtcNow : null,
                MotwHostUrl = r.HasMarkOfTheWeb ? NullIfEmpty(r.ZoneSourceUrl) : null,
                PersistenceLocation = r.OriginAutorunItem == null ? null : NullIfEmpty(r.OriginAutorunItem.LocationSource)
            };
        }

        public static PersistenceEntry ToEntry(PersistenceItem item)
        {
            string category = item.Category.ToString();
            return new PersistenceEntry(
                SnapshotDiff.KeyOf(category, item.LocationSource, item.Name),
                category,
                item.Name,
                item.LocationSource,
                item.FilePath,
                item.Arguments,
                item.IsEnabled,
                item.Signature.ToString(),
                NullIfEmpty(item.SignatureSignerName),
                NullIfEmpty(item.Sha256Hash));
        }

        private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

        #endregion
    }
}
