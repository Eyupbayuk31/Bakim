using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Bakım.Services.Safety
{
    public sealed record JournalOperation(DateTime AtUtc, string Target, string Action, string Outcome, string? BackupFile, string? Detail);

    /// <summary>
    /// Yıkıcı işlemlerin geri alma günlüğü.
    ///
    /// Her işlem grubu (ör. bir kaldırma sihirbazı oturumu) bir klasör alır:
    ///   %LocalAppData%\Bakim\History\Journals\{id}\
    ///     journal.json   → yapılan işlemler
    ///     0001.reg ...   → silinmeden önce alınan kayıt defteri yedekleri (reg.exe export)
    /// Etkinlik Merkezi (Faz 3) bu klasörleri okuyup "Geri al" sunar.
    /// </summary>
    public static class UndoJournal
    {
        public static string RootDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bakim", "History", "Journals");

        private static readonly object Gate = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        /// <summary>Yeni bir günlük oluşturur ve kimliğini döndürür.</summary>
        public static string Create(string title)
        {
            string id = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..24];
            string dir = GetDirectory(id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "title.txt"), title);
            return id;
        }

        public static string GetDirectory(string journalId) => Path.Combine(RootDirectory, journalId);

        /// <summary>Bir sonraki yedek dosyasının yolu (0001.reg, 0002.reg …).</summary>
        public static string NextBackupPath(string journalId, string extension)
        {
            string dir = GetDirectory(journalId);
            Directory.CreateDirectory(dir);
            lock (Gate)
            {
                int n = Directory.GetFiles(dir, "*" + extension).Length + 1;
                return Path.Combine(dir, $"{n:D4}{extension}");
            }
        }

        public static void Append(string journalId, JournalOperation op)
        {
            string file = Path.Combine(GetDirectory(journalId), "journal.json");
            lock (Gate)
            {
                var list = Read(journalId).ToList();
                list.Add(op);
                Directory.CreateDirectory(GetDirectory(journalId));
                string tmp = file + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(list, JsonOptions));
                File.Move(tmp, file, overwrite: true);
            }
        }

        public static IReadOnlyList<JournalOperation> Read(string journalId)
        {
            string file = Path.Combine(GetDirectory(journalId), "journal.json");
            if (!File.Exists(file)) return Array.Empty<JournalOperation>();
            try
            {
                return JsonSerializer.Deserialize<List<JournalOperation>>(File.ReadAllText(file)) ?? new List<JournalOperation>();
            }
            catch (JsonException ex)
            {
                AppLog.Warning($"Geri alma günlüğü okunamadı: {file}", ex, nameof(UndoJournal));
                return Array.Empty<JournalOperation>();
            }
        }

        /// <summary>
        /// Günlükteki tüm .reg yedeklerini ters sırayla geri yükler (reg.exe import).
        /// Başarıyla geri yüklenen dosya sayısını döndürür.
        /// </summary>
        public static async Task<(int Restored, int Failed)> RestoreRegistryAsync(string journalId)
        {
            string dir = GetDirectory(journalId);
            if (!Directory.Exists(dir)) return (0, 0);

            int ok = 0, failed = 0;
            foreach (string reg in Directory.GetFiles(dir, "*.reg").OrderByDescending(f => f, StringComparer.Ordinal))
            {
                bool success = await RunRegAsync("import", reg);
                if (success) ok++; else failed++;
                Append(journalId, new JournalOperation(DateTime.UtcNow, reg, "RegistryRestore", success ? "Restored" : "Failed", reg, null));
            }
            return (ok, failed);
        }

        /// <summary>reg.exe çalıştırır. Argümanlar ArgumentList ile verilir (tırnak/enjeksiyon güvenli).</summary>
        internal static async Task<bool> RunRegAsync(params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "reg.exe"),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (string a in args) psi.ArgumentList.Add(a);

                using var p = Process.Start(psi);
                if (p == null) return false;

                var waitTask = p.WaitForExitAsync();
                if (await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(60))) != waitTask)
                {
                    try { p.Kill(); } catch (InvalidOperationException) { }
                    AppLog.Warning($"reg.exe zaman aşımına uğradı: {string.Join(' ', args)}", null, nameof(UndoJournal));
                    return false;
                }

                if (p.ExitCode != 0)
                {
                    string err = await p.StandardError.ReadToEndAsync();
                    AppLog.Warning($"reg.exe {args[0]} başarısız ({p.ExitCode}): {err.Trim()}", null, nameof(UndoJournal));
                }
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                AppLog.Error($"reg.exe çalıştırılamadı: {string.Join(' ', args)}", ex, nameof(UndoJournal));
                return false;
            }
        }
    }
}
