using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Core.Safety;
using Bakım.Helpers;

namespace Bakım.Services.Safety
{
    public enum DeleteOutcome
    {
        Deleted,
        Recycled,
        ScheduledForReboot,
        Blocked,
        NotFound,
        AccessDenied,
        InUse,
        Failed
    }

    public sealed record OperationResult(string Target, DeleteOutcome Outcome, string Message, long BytesFreed)
    {
        public bool Succeeded => Outcome is DeleteOutcome.Deleted or DeleteOutcome.Recycled or DeleteOutcome.ScheduledForReboot;
    }

    /// <param name="Permanent">true: kalıcı sil; false: Geri Dönüşüm Kutusu (varsayılan).</param>
    /// <param name="AllowOutsideKnownRoots">Kesin kanıtla belirlenmiş hedefler için (bkz. PathSafetyGuard).</param>
    /// <param name="JournalId">Geri alma günlüğü; null ise günlüğe yazılmaz.</param>
    public sealed record DeletePolicy(bool Permanent = false, bool AllowOutsideKnownRoots = false, string? JournalId = null);

    public interface ISafeDeleteService
    {
        Task<OperationResult> DeletePathAsync(string path, bool isDirectory, DeletePolicy policy, CancellationToken ct = default);
        OperationResult ScheduleDeleteOnReboot(string path, bool isDirectory);
    }

    /// <summary>
    /// Dosya ve klasör silmenin TEK yolu. Her çağrı önce <see cref="PathSafetyGuard"/>
    /// onayından geçer; varsayılan olarak Geri Dönüşüm Kutusu kullanılır.
    /// </summary>
    public sealed class SafeDeleteService : ISafeDeleteService
    {
        private readonly PathSafetyGuard _guard;
        private readonly ILogService _log;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileExW(string existing, string? newName, uint flags);
        private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

        public SafeDeleteService(ILogService log) : this(PathSafetyGuard.Default, log) { }

        public SafeDeleteService(PathSafetyGuard guard, ILogService log)
        {
            _guard = guard;
            _log = log;
        }

        public Task<OperationResult> DeletePathAsync(string path, bool isDirectory, DeletePolicy policy, CancellationToken ct = default)
        {
            return Task.Run(() => DeleteCore(path, isDirectory, policy, ct), ct);
        }

        private OperationResult DeleteCore(string path, bool isDirectory, DeletePolicy policy, CancellationToken ct)
        {
            var check = _guard.CheckDeletion(path, isDirectory, policy.AllowOutsideKnownRoots);
            if (!check.IsAllowed)
            {
                _log.Warning($"Silme engellendi ({check.Verdict}): {path} — {check.Reason}", null, nameof(SafeDeleteService));
                return Record(policy, new OperationResult(path, DeleteOutcome.Blocked, check.Reason, 0));
            }

            bool exists = isDirectory ? Directory.Exists(path) : File.Exists(path);
            if (!exists) return Record(policy, new OperationResult(path, DeleteOutcome.NotFound, "Zaten yok.", 0));

            ct.ThrowIfCancellationRequested();

            try
            {
                var attributes = File.GetAttributes(path);
                bool isReparse = (attributes & FileAttributes.ReparsePoint) != 0;

                // Bağlantı (junction/symlink): yalnızca bağlantının kendisi kaldırılır,
                // hedefin içine asla inilmez.
                if (isReparse)
                {
                    if (isDirectory) Directory.Delete(path, recursive: false); else File.Delete(path);
                    return Record(policy, new OperationResult(path, DeleteOutcome.Deleted, "Bağlantı kaldırıldı (hedef korunur).", 0));
                }

                long size = isDirectory ? SafeFolderSize(path, ct) : new FileInfo(path).Length;

                if (!policy.Permanent)
                {
                    int code = RecycleBin.Send(path);
                    bool gone = isDirectory ? !Directory.Exists(path) : !File.Exists(path);
                    if (code == 0 && gone)
                        return Record(policy, new OperationResult(path, DeleteOutcome.Recycled, "Geri Dönüşüm Kutusu'na taşındı.", size));

                    // SHFileOperation eski DE_* kodları döndürür: 0x78 erişim reddi, 32 paylaşım ihlali.
                    var outcome = code switch
                    {
                        0 or 32 => DeleteOutcome.InUse,
                        5 or 0x78 => DeleteOutcome.AccessDenied,
                        _ => DeleteOutcome.Failed
                    };
                    string reason = outcome switch
                    {
                        DeleteOutcome.InUse => "Bazı öğeler taşınamadı (kullanımda olabilir).",
                        DeleteOutcome.AccessDenied => "Erişim reddedildi (yönetici gerekebilir).",
                        _ => $"Geri Dönüşüm Kutusu'na taşınamadı (kod {code})."
                    };
                    return Record(policy, new OperationResult(path, outcome, reason, 0));
                }

                if (isDirectory)
                {
                    ClearReadOnly(path);
                    Directory.Delete(path, recursive: true);
                }
                else
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
                return Record(policy, new OperationResult(path, DeleteOutcome.Deleted, "Kalıcı olarak silindi.", size));
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.Warning($"Silme yetkisi yok: {path}", ex, nameof(SafeDeleteService));
                return Record(policy, new OperationResult(path, DeleteOutcome.AccessDenied, "Erişim reddedildi (yönetici gerekebilir).", 0));
            }
            catch (IOException ex)
            {
                _log.Warning($"Silinemedi (kullanımda olabilir): {path}", ex, nameof(SafeDeleteService));
                return Record(policy, new OperationResult(path, DeleteOutcome.InUse, "Dosya kullanımda.", 0));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"Beklenmeyen silme hatası: {path}", ex, nameof(SafeDeleteService));
                return Record(policy, new OperationResult(path, DeleteOutcome.Failed, ex.Message, 0));
            }
        }

        public OperationResult ScheduleDeleteOnReboot(string path, bool isDirectory)
        {
            var check = _guard.CheckDeletion(path, isDirectory);
            if (!check.IsAllowed)
                return new OperationResult(path, DeleteOutcome.Blocked, check.Reason, 0);

            // Not: MoveFileEx yalnızca boş klasörleri ve dosyaları siler; yönetici gerektirir.
            bool ok = MoveFileExW(path, null, MOVEFILE_DELAY_UNTIL_REBOOT);
            return ok
                ? new OperationResult(path, DeleteOutcome.ScheduledForReboot, "Yeniden başlatmada silinecek.", 0)
                : new OperationResult(path, DeleteOutcome.Failed, $"Zamanlanamadı (kod {Marshal.GetLastWin32Error()}; yönetici gerekebilir).", 0);
        }

        private static OperationResult Record(DeletePolicy policy, OperationResult result)
        {
            if (!string.IsNullOrEmpty(policy.JournalId))
            {
                UndoJournal.Append(policy.JournalId, new JournalOperation(
                    DateTime.UtcNow, result.Target, "Delete", result.Outcome.ToString(), null, result.Message));
            }
            return result;
        }

        private static long SafeFolderSize(string path, CancellationToken ct)
        {
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                long total = 0;
                foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", options))
                {
                    ct.ThrowIfCancellationRequested();
                    total += f.Length;
                }
                return total;
            }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }

        private static void ClearReadOnly(string path)
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", options).Where(f => f.IsReadOnly))
            {
                try { f.IsReadOnly = false; }
                catch (UnauthorizedAccessException) { /* silme adımı zaten raporlayacak */ }
            }
        }
    }
}
