using System;
using System.IO;
using System.Threading.Tasks;
using Bakım.Core.Safety;
using Microsoft.Win32;

namespace Bakım.Services.Safety
{
    public interface ISafeRegistryService
    {
        /// <summary>Anahtarı (alt anahtarlarıyla) yedekleyip siler. Yedek alınamazsa SİLMEZ.</summary>
        Task<OperationResult> DeleteKeyAsync(RegistryPath key, string journalId, bool allowVendorRoot = false);

        /// <summary>Tek bir değeri, bulunduğu anahtarı yedekleyerek siler.</summary>
        Task<OperationResult> DeleteValueAsync(RegistryPath keyWithValue, string journalId);

        bool KeyExists(RegistryPath key);
    }

    /// <summary>
    /// Kayıt defterinde silmenin TEK yolu.
    ///
    /// Eski kod silmeden önce yalnızca "[-Anahtar]" satırları içeren bir dosya
    /// yazıp buna "yedek" diyordu — o dosya anahtarı geri getirmez, SİLER.
    /// Burada yedek reg.exe export ile alınır (tüm alt anahtarlar ve değerler)
    /// ve reg.exe import ile birebir geri yüklenebilir.
    /// </summary>
    public sealed class SafeRegistryService : ISafeRegistryService
    {
        private readonly ILogService _log;

        public SafeRegistryService(ILogService log)
        {
            _log = log;
        }

        public bool KeyExists(RegistryPath key)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(key.Hive, key.View);
                using var k = root.OpenSubKey(key.SubKey);
                return k != null;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return false;
            }
        }

        public async Task<OperationResult> DeleteKeyAsync(RegistryPath key, string journalId, bool allowVendorRoot = false)
        {
            string display = key.ToDisplay();
            var check = RegistrySafetyGuard.CheckKeyDeletion(key, allowVendorRoot);
            if (!check.IsAllowed)
            {
                _log.Warning($"Kayıt defteri silme engellendi: {display} — {check.Reason}", null, nameof(SafeRegistryService));
                return Log(journalId, new OperationResult(display, DeleteOutcome.Blocked, check.Reason, 0), null);
            }

            if (!KeyExists(key))
                return Log(journalId, new OperationResult(display, DeleteOutcome.NotFound, "Anahtar zaten yok.", 0), null);

            string backup = UndoJournal.NextBackupPath(journalId, ".reg");
            bool exported = await UndoJournal.RunRegAsync("export", key.ToRegExe(), backup, "/y", key.RegExeViewSwitch);
            if (!exported || !File.Exists(backup))
            {
                return Log(journalId, new OperationResult(display, DeleteOutcome.Failed,
                    "Yedek alınamadığı için anahtar silinmedi.", 0), null);
            }

            try
            {
                using var root = RegistryKey.OpenBaseKey(key.Hive, key.View);
                root.DeleteSubKeyTree(key.SubKey, throwOnMissingSubKey: false);
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.Warning($"Kayıt defteri anahtarı silinemedi (yetki): {display}", ex, nameof(SafeRegistryService));
                return Log(journalId, new OperationResult(display, DeleteOutcome.AccessDenied, "Erişim reddedildi (yönetici gerekebilir).", 0), backup);
            }
            catch (System.Security.SecurityException ex)
            {
                _log.Warning($"Kayıt defteri anahtarı silinemedi (güvenlik): {display}", ex, nameof(SafeRegistryService));
                return Log(journalId, new OperationResult(display, DeleteOutcome.AccessDenied, "Erişim reddedildi.", 0), backup);
            }
            catch (Exception ex)
            {
                _log.Error($"Kayıt defteri anahtarı silinemedi: {display}", ex, nameof(SafeRegistryService));
                return Log(journalId, new OperationResult(display, DeleteOutcome.Failed, ex.Message, 0), backup);
            }

            bool gone = !KeyExists(key);
            return Log(journalId, gone
                ? new OperationResult(display, DeleteOutcome.Deleted, "Silindi (yedeklendi).", 0)
                : new OperationResult(display, DeleteOutcome.Failed, "Silme sonrası anahtar hâlâ duruyor.", 0), backup);
        }

        public async Task<OperationResult> DeleteValueAsync(RegistryPath keyWithValue, string journalId)
        {
            string display = keyWithValue.ToString();
            var check = RegistrySafetyGuard.CheckValueDeletion(keyWithValue);
            if (!check.IsAllowed)
                return Log(journalId, new OperationResult(display, DeleteOutcome.Blocked, check.Reason, 0), null);

            try
            {
                using var root = RegistryKey.OpenBaseKey(keyWithValue.Hive, keyWithValue.View);
                using var probe = root.OpenSubKey(keyWithValue.SubKey);
                if (probe == null || probe.GetValue(keyWithValue.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) == null)
                    return Log(journalId, new OperationResult(display, DeleteOutcome.NotFound, "Değer zaten yok.", 0), null);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return Log(journalId, new OperationResult(display, DeleteOutcome.AccessDenied, "Anahtar okunamadı.", 0), null);
            }

            // Değerin bulunduğu anahtar bütünüyle yedeklenir; import değeri geri getirir.
            string backup = UndoJournal.NextBackupPath(journalId, ".reg");
            var keyOnly = keyWithValue with { ValueName = null };
            bool exported = await UndoJournal.RunRegAsync("export", keyOnly.ToRegExe(), backup, "/y", keyOnly.RegExeViewSwitch);
            if (!exported)
                return Log(journalId, new OperationResult(display, DeleteOutcome.Failed, "Yedek alınamadığı için değer silinmedi.", 0), null);

            try
            {
                using var root = RegistryKey.OpenBaseKey(keyWithValue.Hive, keyWithValue.View);
                using var key = root.OpenSubKey(keyWithValue.SubKey, writable: true);
                if (key == null)
                    return Log(journalId, new OperationResult(display, DeleteOutcome.NotFound, "Anahtar kayboldu.", 0), backup);

                // Etkin bir yakalama kapsamı varsa değer düzeyinde geri alma için özgün değer.
                Helpers.RegistryCapture.Track(key, keyWithValue.ValueName!);
                key.DeleteValue(keyWithValue.ValueName!, throwOnMissingValue: false);
                return Log(journalId, new OperationResult(display, DeleteOutcome.Deleted, "Değer silindi (yedeklendi).", 0), backup);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                return Log(journalId, new OperationResult(display, DeleteOutcome.AccessDenied, "Erişim reddedildi (yönetici gerekebilir).", 0), backup);
            }
            catch (Exception ex)
            {
                _log.Error($"Kayıt defteri değeri silinemedi: {display}", ex, nameof(SafeRegistryService));
                return Log(journalId, new OperationResult(display, DeleteOutcome.Failed, ex.Message, 0), backup);
            }
        }

        private static OperationResult Log(string journalId, OperationResult result, string? backup)
        {
            UndoJournal.Append(journalId, new JournalOperation(DateTime.UtcNow, result.Target, "RegistryDelete", result.Outcome.ToString(), backup, result.Message));
            return result;
        }
    }
}
