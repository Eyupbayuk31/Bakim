using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Bakım.Services
{
    /// <summary>
    /// Askıya alınan süreçlerin defteri (MASTER_PLAN §5.4, §5.5 "geri dönüş garantisi").
    /// Askıya alınan her süreç diske yazılır; Bakım kapanırken hepsi devam ettirilir. Bakım
    /// çöker ya da kapatılamazsa bir sonraki açılışta defterdeki süreçler (hâlâ aynı süreçse —
    /// başlama zamanı eşleşmeli) devam ettirilir. Askıda unutulmuş süreç bırakılmaz.
    /// </summary>
    public static class SuspendedProcessLedger
    {
        public sealed record Entry(int Pid, long StartTimeTicks, string Name, DateTime SuspendedAtUtc);

        private static readonly object Gate = new();
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bakim", "suspended-processes.json");

        public static void Add(int pid, long startTimeTicks, string name)
        {
            lock (Gate)
            {
                var list = Read().Where(e => e.Pid != pid).ToList();
                list.Add(new Entry(pid, startTimeTicks, name, DateTime.UtcNow));
                Write(list);
            }
        }

        public static void Remove(int pid)
        {
            lock (Gate)
            {
                var list = Read();
                if (list.RemoveAll(e => e.Pid == pid) > 0) Write(list);
            }
        }

        public static IReadOnlyList<Entry> Entries
        {
            get { lock (Gate) return Read(); }
        }

        /// <summary>Defterdeki tüm süreçleri devam ettirir; devam ettirilen sayısını döndürür.</summary>
        public static int ResumeAll(Func<int, bool> resume, Func<int, long?> startTimeOf)
        {
            List<Entry> entries;
            lock (Gate) entries = Read();
            int resumed = 0;
            foreach (var e in entries)
            {
                // PID yeniden kullanılmış olabilir: yalnızca başlama zamanı aynıysa aynı süreçtir.
                var start = startTimeOf(e.Pid);
                if (start == null || start.Value != e.StartTimeTicks) continue;
                try
                {
                    if (resume(e.Pid)) resumed++;
                }
                catch (Exception ex)
                {
                    AppLog.Warning($"Askıdaki süreç devam ettirilemedi: {e.Name} ({e.Pid})", ex, nameof(SuspendedProcessLedger));
                }
            }
            lock (Gate) Write(new List<Entry>());
            if (resumed > 0) AppLog.Info($"{resumed} askıdaki süreç devam ettirildi.", nameof(SuspendedProcessLedger));
            return resumed;
        }

        private static List<Entry> Read()
        {
            try
            {
                if (!File.Exists(FilePath)) return new List<Entry>();
                return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) ?? new List<Entry>();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                AppLog.Warning("Askıya alınan süreç defteri okunamadı.", ex, nameof(SuspendedProcessLedger));
                return new List<Entry>();
            }
        }

        private static void Write(List<Entry> list)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (list.Count == 0)
                {
                    if (File.Exists(FilePath)) File.Delete(FilePath);
                    return;
                }
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(list));
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warning("Askıya alınan süreç defteri yazılamadı.", ex, nameof(SuspendedProcessLedger));
            }
        }
    }
}
