using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakım.Models;
using Bakım.Services.Safety;

namespace Bakım.Services.Sentinel.Actions
{
    public class RollbackResult
    {
        public int DeletedFilesCount { get; set; }
        public int DeletedFoldersCount { get; set; }
        public int ProtectedModifiedFilesCount { get; set; }
        public int BlockedCount { get; set; }
        public List<string> Errors { get; } = new();
    }

    /// <summary>
    /// Kurulum sonrası geri alma (revert) işlemlerini yöneten güvenli motor.
    ///
    /// Kurallar:
    ///   • Yalnızca oturum sırasında YENİ OLUŞTURULAN ("Created") dosyalara dokunulur;
    ///     önceden var olan ya da değiştirilen hiçbir dosya silinmez.
    ///   • Her silme <see cref="ISafeDeleteService"/> üzerinden yapılır: yol önce
    ///     PathSafetyGuard'dan geçer (ör. C:\Windows altına bırakılmış bir sürücü
    ///     dosyası silinmez), dosya Geri Dönüşüm Kutusu'na taşınır.
    ///   • Geri Dönüşüm Kutusu başarısız olursa dosya KALICI SİLİNMEZ; hata raporlanır.
    ///     (v3.20.0'daki ilk sürüm bu durumda sessizce File.Delete yapıyordu.)
    ///   • Klasörler yalnızca boşsa kaldırılır.
    /// </summary>
    public static class RollbackPlanner
    {
        /// <summary>Senkron sarmalayıcı (testler ve eski çağrılar için).</summary>
        public static RollbackResult ExecuteSafeRollback(SetupDeltaReport report, ISafeDeleteService? safeDelete = null) =>
            ExecuteSafeRollbackAsync(report, safeDelete).GetAwaiter().GetResult();

        public static async Task<RollbackResult> ExecuteSafeRollbackAsync(SetupDeltaReport report, ISafeDeleteService? safeDelete = null)
        {
            safeDelete ??= App.TryGetService<ISafeDeleteService>() ?? new SafeDeleteService(NullLogService.Instance);
            var result = new RollbackResult();
            string journal = UndoJournal.Create($"Kurulum geri alma: {report.AppName}");

            // 1. Korunacak dosyalar: değiştirilenler ve silinenler asla hedef olamaz.
            var protectedFiles = new HashSet<string>(report.ModifiedFiles, StringComparer.OrdinalIgnoreCase);
            protectedFiles.UnionWith(report.DeletedFiles);
            result.ProtectedModifiedFilesCount = report.ModifiedFiles.Count;

            // 2. Hedef: yalnızca CreatedFiles. Eski raporlarda CreatedFiles yoksa
            //    AddedFiles'tan değiştirilenler çıkarılarak kullanılır.
            var targetFiles = report.CreatedFiles.Count > 0
                ? report.CreatedFiles
                : report.AddedFiles.Where(f => !protectedFiles.Contains(f)).ToList();

            foreach (var file in targetFiles.Where(f => !protectedFiles.Contains(f)))
            {
                if (!File.Exists(file)) continue;

                var r = await safeDelete.DeletePathAsync(file, isDirectory: false, new DeletePolicy(JournalId: journal));
                if (r.Succeeded)
                {
                    result.DeletedFilesCount++;
                }
                else if (r.Outcome == DeleteOutcome.Blocked)
                {
                    result.BlockedCount++;
                    result.Errors.Add($"Korumalı konum, silinmedi: {file} ({r.Message})");
                }
                else if (r.Outcome != DeleteOutcome.NotFound)
                {
                    result.Errors.Add($"Dosya silinemedi: {file} ({r.Message})");
                }
            }

            // 3. Yalnızca yeni eklenmiş ve şu an BOŞ olan klasörler (en derinden başlayarak).
            foreach (var folder in report.AddedFolders.OrderByDescending(f => f.Length))
            {
                try
                {
                    if (!Directory.Exists(folder) || Directory.EnumerateFileSystemEntries(folder).Any()) continue;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                var r = await safeDelete.DeletePathAsync(folder, isDirectory: true, new DeletePolicy(JournalId: journal));
                if (r.Succeeded)
                {
                    result.DeletedFoldersCount++;
                }
                else if (r.Outcome == DeleteOutcome.Blocked)
                {
                    result.BlockedCount++;
                }
                else if (r.Outcome != DeleteOutcome.NotFound)
                {
                    result.Errors.Add($"Klasör silinemedi: {folder} ({r.Message})");
                }
            }

            return result;
        }
    }
}
