using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Core.Activity;
using Bakım.Core.Safety;
using Bakım.Helpers;
using Bakım.Services.Activity;

namespace Bakım.Services.Analyzer
{
    /// <summary>Karantinadaki dosyanın kaydı (karantina klasöründe manifest.json).</summary>
    public sealed record QuarantineManifest(
        string Id,
        string OriginalPath,
        string StoredFileName,
        string? Sha256,
        long SizeBytes,
        DateTime QuarantinedAtUtc,
        string Reason);

    public sealed record QuarantineOutcome(bool Success, string Message, QuarantineManifest? Manifest = null);

    public interface IQuarantineService
    {
        /// <summary>
        /// Dosyayı karantinaya alır: çalıştırma izni kaldırılmış karantina klasörüne taşınır ve işlem
        /// Etkinlik Merkezi'ne geri alınabilir olarak yazılır. Korumalı konumlar (Windows …) reddedilir.
        /// </summary>
        Task<QuarantineOutcome> QuarantineAsync(string path, string reason, CancellationToken ct = default);

        Task<QuarantineOutcome> RestoreAsync(string quarantineDirectory, CancellationToken ct = default);
    }

    /// <summary>
    /// Karantina (ANL Faz 3). "Sil" yerine varsayılan güvenli eylem: dosya çalışamaz, ama yanlış
    /// alarmsa tek tıkla eski yerine döner. Klasör: %ProgramData%\Bakim\Quarantine (yönetici) ya da
    /// %LocalAppData%\Bakim\Quarantine.
    /// </summary>
    public sealed class QuarantineService : IQuarantineService
    {
        public const string ManifestFile = "manifest.json";
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        private readonly IActivityService? _activity;
        private readonly PathSafetyGuard _guard;

        public QuarantineService(IActivityService? activity = null, PathSafetyGuard? guard = null, string? root = null)
        {
            _activity = activity;
            _guard = guard ?? PathSafetyGuard.Default;
            Root = root ?? DefaultRoot();
        }

        public string Root { get; }

        private static string DefaultRoot() => Path.Combine(
            Environment.GetFolderPath(UacHelper.IsAdministrator() ? Environment.SpecialFolder.CommonApplicationData : Environment.SpecialFolder.LocalApplicationData),
            "Bakim", "Quarantine");

        public Task<QuarantineOutcome> QuarantineAsync(string path, string reason, CancellationToken ct = default) => Task.Run(() =>
        {
            if (!File.Exists(path)) return new QuarantineOutcome(false, "Dosya bulunamadı.");
            var check = _guard.CheckDeletion(path, isDirectory: false, allowOutsideKnownRoots: true);
            if (!check.IsAllowed) return new QuarantineOutcome(false, $"Korumalı konum, karantinaya alınmadı: {check.Reason}");

            string id = Guid.NewGuid().ToString("N");
            string dir = Path.Combine(Root, id);
            string stored = Path.GetFileName(path) + ".quarantine";
            string target = Path.Combine(dir, stored);
            try
            {
                var info = new FileInfo(path);
                string? sha = info.Length <= 512L * 1024 * 1024 ? Sha256(path) : null;
                Directory.CreateDirectory(dir);
                File.Move(path, target);
                DenyExecute(target, deny: true);

                var manifest = new QuarantineManifest(id, path, stored, sha, info.Length, DateTime.UtcNow, reason);
                File.WriteAllText(Path.Combine(dir, ManifestFile), JsonSerializer.Serialize(manifest, Json));
                Record(manifest, dir);
                AppLog.Info($"Karantinaya alındı: {path} → {target}", nameof(QuarantineService));
                return new QuarantineOutcome(true, "Dosya karantinaya alındı. Etkinlik Merkezi'nden tek tıkla geri yükleyebilirsiniz.", manifest);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Taşıma yarım kaldıysa dosyayı yerine koy.
                if (!File.Exists(path) && File.Exists(target))
                {
                    try { File.Move(target, path); }
                    catch (Exception undo) when (undo is IOException or UnauthorizedAccessException)
                    {
                        AppLog.Error($"Karantina geri alınamadı; dosya şurada: {target}", undo, nameof(QuarantineService));
                    }
                }
                AppLog.Warning($"Karantinaya alınamadı: {path}", ex, nameof(QuarantineService));
                return new QuarantineOutcome(false, ex is IOException
                    ? "Dosya kullanımda olabilir; önce ilgili süreci durdurun."
                    : "Bu dosyayı taşımak için yönetici yetkisi gerekiyor.");
            }
        }, ct);

        public Task<QuarantineOutcome> RestoreAsync(string quarantineDirectory, CancellationToken ct = default) => Task.Run(() =>
        {
            string manifestPath = Path.Combine(quarantineDirectory, ManifestFile);
            if (!File.Exists(manifestPath)) return new QuarantineOutcome(false, "Karantina kaydı bulunamadı.");
            var manifest = JsonSerializer.Deserialize<QuarantineManifest>(File.ReadAllText(manifestPath));
            if (manifest == null) return new QuarantineOutcome(false, "Karantina kaydı okunamadı.");

            string stored = Path.Combine(quarantineDirectory, manifest.StoredFileName);
            if (!File.Exists(stored)) return new QuarantineOutcome(false, "Karantinadaki dosya bulunamadı.", manifest);
            if (File.Exists(manifest.OriginalPath)) return new QuarantineOutcome(false, "Özgün konumda aynı adlı bir dosya var; geri yükleme durduruldu.", manifest);
            try
            {
                // Çalıştırma yasağı dosyayla birlikte taşınır: önce kaldırılır.
                DenyExecute(stored, deny: false);
                Directory.CreateDirectory(Path.GetDirectoryName(manifest.OriginalPath)!);
                File.Move(stored, manifest.OriginalPath);
                Directory.Delete(quarantineDirectory, recursive: true);
                AppLog.Info($"Karantinadan geri yüklendi: {manifest.OriginalPath}", nameof(QuarantineService));
                return new QuarantineOutcome(true, "Dosya özgün konumuna geri yüklendi.", manifest);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warning($"Karantinadan geri yüklenemedi: {manifest.OriginalPath}", ex, nameof(QuarantineService));
                return new QuarantineOutcome(false, $"Geri yüklenemedi: {ex.Message}", manifest);
            }
        }, ct);

        /// <summary>Dosyada herkes için "çalıştırma" iznini açıkça yasaklar (ya da yasağı kaldırır).</summary>
        private static void DenyExecute(string path, bool deny)
        {
            var file = new FileInfo(path);
            var security = file.GetAccessControl();
            var rule = new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.ExecuteFile, AccessControlType.Deny);
            if (deny) security.AddAccessRule(rule);
            else security.RemoveAccessRuleAll(rule);
            file.SetAccessControl(security);
        }

        private static string Sha256(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private void Record(QuarantineManifest manifest, string dir)
        {
            var activity = _activity ?? App.TryGetService<IActivityService>();
            if (activity == null) return;
            try
            {
                activity.Record(new ActivityEntry
                {
                    Kind = ActivityKind.Quarantine,
                    Module = "Analizör",
                    Title = $"'{Path.GetFileName(manifest.OriginalPath)}' karantinaya alındı",
                    Summary = manifest.Reason,
                    Outcome = ActivityOutcome.Succeeded,
                    Undo = UndoState.Undoable,
                    UndoHandler = UndoHandlers.QuarantineRestore,
                    PayloadPath = dir,
                    DeepLink = "Analyzer",
                    Items = new List<ActivityItem> { new(manifest.OriginalPath, "Karantina", "Taşındı") }
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                AppLog.Warning("Karantina Etkinlik Merkezi'ne yazılamadı.", ex, nameof(QuarantineService));
            }
        }
    }

    /// <summary>Etkinlik Merkezi'nden "Geri al": dosyayı karantinadan özgün yerine taşır.</summary>
    public sealed class QuarantineRestoreUndoHandler : IUndoHandler
    {
        public string Key => UndoHandlers.QuarantineRestore;

        public bool HasPayload(ActivityEntry entry) =>
            !string.IsNullOrEmpty(entry.PayloadPath) && File.Exists(Path.Combine(entry.PayloadPath, QuarantineService.ManifestFile));

        public async Task<UndoResult> UndoAsync(ActivityEntry entry, CancellationToken ct)
        {
            var service = App.TryGetService<IQuarantineService>() ?? new QuarantineService();
            var outcome = await service.RestoreAsync(entry.PayloadPath ?? string.Empty, ct).ConfigureAwait(false);
            return outcome.Success
                ? new UndoResult(true, 1, 0, outcome.Message, new[] { new ActivityItem(outcome.Manifest!.OriginalPath, "Dosya", "Geri yüklendi") })
                : UndoResult.Fail(outcome.Message);
        }
    }
}
