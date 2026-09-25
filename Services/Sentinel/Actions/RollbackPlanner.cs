using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bakım.Models;
using Microsoft.VisualBasic.FileIO;

namespace Bakım.Services.Sentinel.Actions
{
    public class RollbackResult
    {
        public int DeletedFilesCount { get; set; }
        public int DeletedFoldersCount { get; set; }
        public int ProtectedModifiedFilesCount { get; set; }
        public List<string> Errors { get; } = new();
    }

    /// <summary>
    /// Kurulum sonras geri alma (revert) ilemlerini yneten gvenli motor.
    /// Yalnzca oturum srasnda YEN RETLEN ("Created") dosyalar Geri Dnm
    /// Kutusuna gnderir. nceden var olan veya deitirilen hibir dosyay ASLA silmez.
    /// </summary>
    public static class RollbackPlanner
    {
        /// <summary>
        /// Raporlanan deiiklikleri gvenli ekilde geri alr.
        /// </summary>
        public static RollbackResult ExecuteSafeRollback(SetupDeltaReport report)
        {
            var result = new RollbackResult();

            // 1. Korunacak dosyalar belirle (ModifiedFiles asla silinemez!)
            var protectedFiles = new HashSet<string>(report.ModifiedFiles, StringComparer.OrdinalIgnoreCase);
            result.ProtectedModifiedFilesCount = protectedFiles.Count;

            // 2. Yalnzca CreatedFiles listesindeki dosyalara ilem yap
            // Geriye dnk uyumluluk: CreatedFiles bo ise AddedFiles kullan ama ModifiedFiles' hari tut
            var targetFiles = report.CreatedFiles.Count > 0
                ? report.CreatedFiles
                : report.AddedFiles.Where(f => !protectedFiles.Contains(f)).ToList();

            foreach (var file in targetFiles)
            {
                try
                {
                    // Ek koruma: dosya deitirilenler listesindeyse atla
                    if (protectedFiles.Contains(file)) continue;

                    if (File.Exists(file))
                    {
                        try
                        {
                            // Kalc silme yerine Geri Dnm Kutusu (Recycle Bin) kullanlr
                            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                                file,
                                UIOption.OnlyErrorDialogs,
                                RecycleOption.SendToRecycleBin);
                            result.DeletedFilesCount++;
                        }
                        catch
                        {
                            // Geri Dnm Kutusu desteklenmiyorsa veya hata aldysa dorudan gvenli sil
                            File.Delete(file);
                            result.DeletedFilesCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Dosya silinemedi: {file} ({ex.Message})");
                }
            }

            // 3. Yalnzca yeni eklenmi ve u an bo olan klasrleri sil (en derinden balayarak)
            foreach (var folder in report.AddedFolders.OrderByDescending(f => f.Length))
            {
                try
                {
                    if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                    {
                        Directory.Delete(folder);
                        result.DeletedFoldersCount++;
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Klasr silinemedi: {folder} ({ex.Message})");
                }
            }

            return result;
        }
    }
}
