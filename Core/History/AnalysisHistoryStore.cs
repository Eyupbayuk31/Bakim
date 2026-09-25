using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bakım.Core.History
{
    /// <summary>
    /// Analizör Geçmişi'nin disk katmanı (%LocalAppData%\Bakim\History\Analyzer):
    ///   • records.jsonl — yalnızca ekleme; güncelleme aynı Id ile yeni satır, okurken son satır geçerli.
    ///     Bozuk satır tüm geçmişi bozmaz: atlanır ve sayılır.
    ///   • snapshots/{yyyyMMdd_HHmmss}_{id}.json.gz + snapshots/index.jsonl (başlıklar).
    /// Sıkıştırma ve yeniden yazma önce geçici dosyaya yazılır, sonra yerine taşınır.
    /// </summary>
    public sealed class AnalysisHistoryStore
    {
        public const long CompactionThresholdBytes = 5L * 1024 * 1024;

        public static readonly JsonSerializerOptions Json = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        private readonly object _gate = new();

        public AnalysisHistoryStore(string directory)
        {
            Directory = directory;
        }

        public string Directory { get; }
        public string RecordsPath => Path.Combine(Directory, "records.jsonl");
        public string SnapshotsDirectory => Path.Combine(Directory, "snapshots");
        private string SnapshotIndexPath => Path.Combine(SnapshotsDirectory, "index.jsonl");

        public static string DefaultDirectory() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bakim", "History", "Analyzer");

        #region Kayıtlar

        public sealed record LoadResult(IReadOnlyList<AnalysisRecord> Records, int CorruptLines, int TotalLines);

        public LoadResult Load()
        {
            lock (_gate)
            {
                var byId = new Dictionary<string, AnalysisRecord>(StringComparer.Ordinal);
                int corrupt = 0, total = 0;
                if (!File.Exists(RecordsPath)) return new LoadResult(Array.Empty<AnalysisRecord>(), 0, 0);

                using var stream = new FileStream(RecordsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    total++;
                    try
                    {
                        var record = JsonSerializer.Deserialize<AnalysisRecord>(line, Json);
                        if (record == null || string.IsNullOrEmpty(record.Id)) { corrupt++; continue; }
                        byId[record.Id] = record; // son satır geçerli
                    }
                    catch (JsonException)
                    {
                        corrupt++;
                    }
                }
                return new LoadResult(byId.Values.OrderBy(r => r.AnalyzedAtUtc).ToList(), corrupt, total);
            }
        }

        public void Append(AnalysisRecord record)
        {
            string line = JsonSerializer.Serialize(record, Json) + "\n";
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                using var stream = new FileStream(RecordsPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                var bytes = Encoding.UTF8.GetBytes(line);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
        }

        /// <summary>Dosyayı yalnızca verilen kayıtlarla yeniden yazar (sıkıştırma, silme, saklama).</summary>
        public void Rewrite(IEnumerable<AnalysisRecord> records)
        {
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                string temp = RecordsPath + ".tmp";
                using (var writer = new StreamWriter(temp, append: false, new UTF8Encoding(false)))
                {
                    foreach (var r in records.OrderBy(r => r.AnalyzedAtUtc))
                        writer.Write(JsonSerializer.Serialize(r, Json) + "\n");
                }
                File.Move(temp, RecordsPath, overwrite: true);
            }
        }

        public long RecordsFileSize
        {
            get
            {
                try { return File.Exists(RecordsPath) ? new FileInfo(RecordsPath).Length : 0; }
                catch (IOException) { return 0; }
            }
        }

        public bool NeedsCompaction(int liveRecords, int totalLines) =>
            RecordsFileSize > CompactionThresholdBytes || (totalLines > 200 && totalLines > liveRecords * 2);

        #endregion

        #region Anlık görüntüler

        public SnapshotHeader SaveSnapshot(PersistenceSnapshot snapshot)
        {
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(SnapshotsDirectory);
                string fileName = snapshot.TakenAtUtc.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + snapshot.Id + ".json.gz";
                string path = Path.Combine(SnapshotsDirectory, fileName);

                using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
                {
                    JsonSerializer.Serialize(gzip, snapshot, Json);
                }

                var header = new SnapshotHeader(snapshot.Id, snapshot.TakenAtUtc, snapshot.Trigger, snapshot.Entries.Count, fileName);
                File.AppendAllText(SnapshotIndexPath, JsonSerializer.Serialize(header, Json) + "\n", new UTF8Encoding(false));
                return header;
            }
        }

        /// <summary>Başlıklar, yeniden eskiye. Dosyası silinmiş başlıklar atlanır.</summary>
        public IReadOnlyList<SnapshotHeader> ListSnapshots()
        {
            lock (_gate)
            {
                if (!File.Exists(SnapshotIndexPath)) return Array.Empty<SnapshotHeader>();
                var headers = new Dictionary<string, SnapshotHeader>(StringComparer.Ordinal);
                foreach (var line in File.ReadAllLines(SnapshotIndexPath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var h = JsonSerializer.Deserialize<SnapshotHeader>(line, Json);
                        if (h != null && File.Exists(Path.Combine(SnapshotsDirectory, h.FileName))) headers[h.Id] = h;
                    }
                    catch (JsonException) { }
                }
                return headers.Values.OrderByDescending(h => h.TakenAtUtc).ToList();
            }
        }

        public PersistenceSnapshot? LoadSnapshot(string id)
        {
            var header = ListSnapshots().FirstOrDefault(h => h.Id == id);
            if (header == null) return null;
            lock (_gate)
            {
                try
                {
                    using var file = new FileStream(Path.Combine(SnapshotsDirectory, header.FileName), FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var gzip = new GZipStream(file, CompressionMode.Decompress);
                    return JsonSerializer.Deserialize<PersistenceSnapshot>(gzip, Json);
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
                {
                    return null;
                }
            }
        }

        /// <summary>En yeni <paramref name="keep"/> görüntü dışındakileri siler; silinen sayısını döndürür.</summary>
        public int PruneSnapshots(int keep)
        {
            var all = ListSnapshots();
            var remove = all.Skip(Math.Max(0, keep)).ToList();
            if (remove.Count == 0) return 0;

            lock (_gate)
            {
                foreach (var h in remove)
                {
                    try { File.Delete(Path.Combine(SnapshotsDirectory, h.FileName)); } catch (IOException) { }
                }
                var kept = all.Take(Math.Max(0, keep)).OrderBy(h => h.TakenAtUtc)
                              .Select(h => JsonSerializer.Serialize(h, Json));
                string temp = SnapshotIndexPath + ".tmp";
                File.WriteAllText(temp, string.Join("\n", kept) + "\n", new UTF8Encoding(false));
                File.Move(temp, SnapshotIndexPath, overwrite: true);
            }
            return remove.Count;
        }

        #endregion

        /// <summary>Tüm geçmişi (kayıtlar + anlık görüntüler) siler.</summary>
        public void ClearAll()
        {
            lock (_gate)
            {
                if (File.Exists(RecordsPath)) File.Delete(RecordsPath);
                if (System.IO.Directory.Exists(SnapshotsDirectory)) System.IO.Directory.Delete(SnapshotsDirectory, recursive: true);
            }
        }
    }
}
