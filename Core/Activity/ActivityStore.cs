using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bakım.Core.Activity
{
    /// <summary>
    /// Etkinlik Merkezi'nin disk katmanı (%LocalAppData%\Bakim\History\Activity):
    ///   • activity.jsonl — yalnızca ekleme; aynı Id ile yeni satır günceller, okurken son satır geçerli.
    ///     Bozuk satır tüm geçmişi bozmaz: atlanır ve sayılır.
    ///   • journals/{id}/ — geri alma verisi (eski kayıt değerleri, hizmet yapılandırması …).
    /// </summary>
    public sealed class ActivityStore
    {
        public const int MaxEntries = 5000;
        public static readonly TimeSpan Retention = TimeSpan.FromDays(365);

        public static readonly JsonSerializerOptions Json = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly object _gate = new();

        public ActivityStore(string directory)
        {
            Directory = directory;
        }

        public string Directory { get; }
        public string EntriesPath => Path.Combine(Directory, "activity.jsonl");
        public string JournalsDirectory => Path.Combine(Directory, "journals");

        public static string DefaultDirectory() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bakim", "History", "Activity");

        /// <summary>Kaydın geri alma verisi klasörü; oluşturur.</summary>
        public string CreateJournal(string id)
        {
            string dir = Path.Combine(JournalsDirectory, id);
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }

        public sealed record LoadResult(IReadOnlyList<ActivityEntry> Entries, int CorruptLines, int TotalLines);

        /// <summary>Kayıtlar, yeniden eskiye.</summary>
        public LoadResult Load()
        {
            lock (_gate)
            {
                if (!File.Exists(EntriesPath)) return new LoadResult(Array.Empty<ActivityEntry>(), 0, 0);

                var byId = new Dictionary<string, ActivityEntry>(StringComparer.Ordinal);
                int corrupt = 0, total = 0;
                using var stream = new FileStream(EntriesPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    total++;
                    try
                    {
                        var entry = JsonSerializer.Deserialize<ActivityEntry>(line, Json);
                        if (entry == null || string.IsNullOrEmpty(entry.Id)) { corrupt++; continue; }
                        byId[entry.Id] = entry;
                    }
                    catch (JsonException)
                    {
                        corrupt++;
                    }
                }
                return new LoadResult(byId.Values.OrderByDescending(e => e.AtUtc).ToList(), corrupt, total);
            }
        }

        public void Append(ActivityEntry entry)
        {
            if (entry.Items.Count > ActivityEntry.MaxInlineItems)
                entry = entry with { Items = entry.Items.Take(ActivityEntry.MaxInlineItems).ToList() };

            string line = JsonSerializer.Serialize(entry, Json) + "\n";
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                using var stream = new FileStream(EntriesPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                var bytes = Encoding.UTF8.GetBytes(line);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
        }

        /// <summary>Dosyayı yalnızca verilen kayıtlarla yeniden yazar; artık journal klasörlerini siler.</summary>
        public void Rewrite(IEnumerable<ActivityEntry> entries)
        {
            var list = entries.OrderBy(e => e.AtUtc).ToList();
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                string temp = EntriesPath + ".tmp";
                using (var writer = new StreamWriter(temp, append: false, new UTF8Encoding(false)))
                {
                    foreach (var e in list) writer.Write(JsonSerializer.Serialize(e, Json) + "\n");
                }
                File.Move(temp, EntriesPath, overwrite: true);

                if (!System.IO.Directory.Exists(JournalsDirectory)) return;
                var live = new HashSet<string>(list.Select(e => e.Id), StringComparer.Ordinal);
                foreach (var dir in System.IO.Directory.GetDirectories(JournalsDirectory))
                {
                    if (live.Contains(Path.GetFileName(dir))) continue;
                    try { System.IO.Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
        }

        /// <summary>
        /// Saklama: 365 günden eski ve <see cref="MaxEntries"/> üstündeki kayıtları atar; dosyada
        /// çok sayıda eski satır birikmişse sıkıştırır. Atılan kayıt sayısını döndürür.
        /// </summary>
        public int Maintain(DateTime nowUtc)
        {
            var loaded = Load();
            var keep = ActivityQuery.ApplyRetention(loaded.Entries, nowUtc, Retention, MaxEntries);
            int dropped = loaded.Entries.Count - keep.Count;
            bool bloated = loaded.TotalLines > 200 && loaded.TotalLines > loaded.Entries.Count * 2;
            if (dropped > 0 || bloated || loaded.CorruptLines > 0) Rewrite(keep);
            return dropped;
        }

        public void ClearAll()
        {
            lock (_gate)
            {
                if (File.Exists(EntriesPath)) File.Delete(EntriesPath);
                if (System.IO.Directory.Exists(JournalsDirectory)) System.IO.Directory.Delete(JournalsDirectory, recursive: true);
            }
        }
    }
}
