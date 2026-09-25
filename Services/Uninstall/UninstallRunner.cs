using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Bakım.Core.Safety;
using Bakım.Core.Uninstall;
using Bakım.Helpers;
using Bakım.Models;

namespace Bakım.Services.Uninstall
{
    public enum UninstallOutcome
    {
        /// <summary>Program kaldırıldı (Uninstall kaydı yok).</summary>
        Removed,
        /// <summary>Kaldırıldı ama yeniden başlatma gerekiyor.</summary>
        RebootRequired,
        /// <summary>Kaldırıcı bitti ama program hâlâ kurulu (hata ya da kullanıcı iptali).</summary>
        StillInstalled,
        /// <summary>Kullanıcı iptal etti.</summary>
        Cancelled,
        /// <summary>Kaldırıcı başlatılamadı.</summary>
        Failed,
        /// <summary>Kaldırıcı zaman aşımında bitmedi ya da kullanıcı beklemeyi bıraktı.</summary>
        TimedOut,
        /// <summary>Sessiz kaldırma bu kaldırıcı türünde desteklenmiyor.</summary>
        NotSupported
    }

    public sealed record UninstallRunResult(UninstallOutcome Outcome, int? ExitCode, TimeSpan Duration, string Detail)
    {
        public bool IsRemoved => Outcome is UninstallOutcome.Removed or UninstallOutcome.RebootRequired;
    }

    /// <summary>
    /// Resmi kaldırıcıyı çalıştırır, GERÇEKTEN bitmesini bekler ve sonucu doğrular.
    ///
    /// Neden gerekli: Inno Setup (unins000.exe) ve NSIS kaldırıcıları kendilerini
    /// %TEMP%'e kopyalayıp asıl süreç hemen çıkar. Eski kod yalnızca ilk sürecin
    /// bitişini bekliyor, kaldırma sürerken kalıntı taramasına geçiyor ve kullanıcı
    /// kaldırıcıda "İptal"e bassa bile programın klasörünü "kalıntı" olarak sunuyordu.
    ///
    /// Bitiş koşulu: (izlenen süreç ağacında canlı süreç kalmadı) YA DA (Uninstall kaydı
    /// silindi ve 30 sn geçti — Steam gibi sürekli açık kalan istemciler için).
    /// Doğrulama: Uninstall kaydı hâlâ var mı?
    /// </summary>
    public sealed class UninstallRunner
    {
        private static readonly TimeSpan SilentTimeout = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan KeyGoneGrace = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

        private readonly ILogService _log;

        public UninstallRunner(ILogService log)
        {
            _log = log;
        }

        /// <summary>Program hâlâ kurulu mu? (Uninstall kaydı ya da — kayıt yoksa — kurulum klasörü)</summary>
        public static bool IsStillInstalled(InstalledAppItem app)
        {
            if (!string.IsNullOrWhiteSpace(app.RegistryKeyPath) &&
                RegistryPath.TryParse(app.RegistryKeyPath, RegistryView.Registry64, out var key))
            {
                try
                {
                    using var root = RegistryKey.OpenBaseKey(key.Hive, key.View);
                    using var k = root.OpenSubKey(key.SubKey);
                    return k != null;
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    return true; // okunamıyorsa kurulu varsay (temkinli)
                }
            }

            return !string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation);
        }

        public static ParsedUninstallCommand? ParseCommand(InstalledAppItem app, bool silent)
        {
            string? raw = silent && !string.IsNullOrWhiteSpace(app.QuietUninstallString)
                ? app.QuietUninstallString
                : (!string.IsNullOrWhiteSpace(app.UninstallString) ? app.UninstallString : app.QuietUninstallString);
            return UninstallCommandParser.Parse(raw, File.Exists);
        }

        public static InstallerFamily DetectFamily(InstalledAppItem app)
        {
            var parsed = ParseCommand(app, silent: false);
            if (parsed == null) return InstallerFamily.Unknown;
            string? keyName = app.RegistryKeyPath?.Split('\\').LastOrDefault()?.Replace(" [32]", string.Empty);
            return UninstallCommandParser.DetectFamily(parsed, keyName, File.Exists);
        }

        /// <summary>Sessiz kaldırma mümkün mü (QuietUninstallString ya da bilinen kaldırıcı ailesi)?</summary>
        public static bool SupportsSilent(InstalledAppItem app)
        {
            if (!string.IsNullOrWhiteSpace(app.QuietUninstallString)) return true;
            var parsed = ParseCommand(app, silent: false);
            return parsed != null && UninstallCommandParser.BuildSilentArguments(parsed, DetectFamily(app)) != null;
        }

        public async Task<UninstallRunResult> RunAsync(InstalledAppItem app, bool silent, IProgress<string>? progress, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            var parsed = ParseCommand(app, silent);
            if (parsed == null)
                return new UninstallRunResult(UninstallOutcome.Failed, null, sw.Elapsed, "Bu program için kaldırma komutu bulunamadı.");

            string arguments = parsed.Arguments;
            if (silent && string.IsNullOrWhiteSpace(app.QuietUninstallString))
            {
                string? silentArgs = UninstallCommandParser.BuildSilentArguments(parsed, DetectFamily(app));
                if (silentArgs == null)
                    return new UninstallRunResult(UninstallOutcome.NotSupported, null, sw.Elapsed,
                        "Bu kaldırıcı sessiz modu desteklemiyor; arayüzlü kaldırma gerekiyor.");
                arguments = silentArgs;
            }

            progress?.Report("Resmi kaldırıcı başlatılıyor...");
            _log.Info($"Kaldırıcı başlatılıyor: \"{parsed.FileName}\" {arguments} (sessiz: {silent})", nameof(UninstallRunner));

            Process? root;
            JobObject? job = null;
            try
            {
                (root, job) = Start(parsed.FileName, arguments);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return new UninstallRunResult(UninstallOutcome.Cancelled, null, sw.Elapsed, "Yönetici izni (UAC) reddedildi.");
            }
            catch (Exception ex)
            {
                _log.Error($"Kaldırıcı başlatılamadı: {parsed.FileName}", ex, nameof(UninstallRunner));
                return new UninstallRunResult(UninstallOutcome.Failed, null, sw.Elapsed, "Kaldırıcı başlatılamadı: " + ex.Message);
            }

            // Nöbetçi bu kaldırıcıyı ve alt süreçlerini "yeni kurulum" saymasın (KAL D5).
            using var suppression = root != null ? Sentinel.Detection.SentinelSuppression.SuppressProcessTree(root.Id) : null;
            using (job)
            using (root)
            {
                var tracked = new HashSet<int>();
                if (root != null) tracked.Add(root.Id);

                DateTime? keyGoneAt = null;
                bool timedOut = false;

                progress?.Report(silent
                    ? "Program sessizce kaldırılıyor..."
                    : "Resmi kaldırıcı açık: lütfen kaldırma adımlarını tamamlayın.");

                while (true)
                {
                    if (ct.IsCancellationRequested) { timedOut = true; break; }
                    if (silent && sw.Elapsed > SilentTimeout) { timedOut = true; break; }

                    bool anyAlive = job?.ActiveProcessCount is int active
                        ? active > 0
                        : TrackDescendants(tracked);

                    if (!anyAlive) break;

                    if (!IsStillInstalled(app))
                    {
                        keyGoneAt ??= DateTime.UtcNow;
                        if (DateTime.UtcNow - keyGoneAt.Value > KeyGoneGrace) break;
                    }

                    try { await Task.Delay(PollInterval, ct); }
                    catch (OperationCanceledException) { timedOut = true; break; }
                }

                int? exitCode = null;
                try { if (root != null && root.HasExited) exitCode = root.ExitCode; }
                catch (InvalidOperationException) { }

                // Kaldırıcılar kaydı işlem sonunda siler; kısa bir süre daha kontrol et.
                progress?.Report("Kaldırma sonucu doğrulanıyor...");
                bool stillInstalled = true;
                for (int i = 0; i < 10; i++)
                {
                    stillInstalled = IsStillInstalled(app);
                    if (!stillInstalled) break;
                    try { await Task.Delay(1000, CancellationToken.None); } catch (OperationCanceledException) { }
                }

                var result = Evaluate(parsed, silent, stillInstalled, timedOut, exitCode, sw.Elapsed);
                _log.Info($"Kaldırma sonucu: {app.DisplayName} → {result.Outcome} (çıkış kodu {exitCode?.ToString() ?? "-"}, {sw.Elapsed.TotalSeconds:0} sn)", nameof(UninstallRunner));
                return result;
            }
        }

        private static UninstallRunResult Evaluate(ParsedUninstallCommand parsed, bool silent, bool stillInstalled, bool timedOut, int? exitCode, TimeSpan elapsed)
        {
            if (!stillInstalled)
            {
                bool reboot = exitCode is 3010 or 1641;
                return new UninstallRunResult(reboot ? UninstallOutcome.RebootRequired : UninstallOutcome.Removed, exitCode, elapsed,
                    reboot ? "Program kaldırıldı; işlemi tamamlamak için yeniden başlatma gerekiyor." : "Program kaldırıldı.");
            }

            if (timedOut)
                return new UninstallRunResult(UninstallOutcome.TimedOut, exitCode, elapsed, "Kaldırıcı beklenen sürede bitmedi; program hâlâ kurulu görünüyor.");

            if (exitCode.HasValue && UninstallCommandParser.IsUserCancel(exitCode.Value))
                return new UninstallRunResult(UninstallOutcome.Cancelled, exitCode, elapsed, "Kaldırma iptal edildi.");

            if (!silent)
                return new UninstallRunResult(UninstallOutcome.Cancelled, exitCode, elapsed,
                    "Kaldırma tamamlanmadı ya da iptal edildi; program hâlâ kurulu.");

            return new UninstallRunResult(UninstallOutcome.StillInstalled, exitCode, elapsed,
                parsed.IsMsi && exitCode.HasValue
                    ? $"Windows Installer kaldırmayı tamamlayamadı (kod {exitCode})."
                    : $"Kaldırıcı bitti ama program hâlâ kurulu{(exitCode.HasValue ? $" (kod {exitCode})" : string.Empty)}.");
        }

        /// <summary>
        /// Bakım yöneticiyse süreç doğrudan başlatılıp Job Object'e atanır (tüm çocuklar
        /// kesin izlenir). Değilse ya da kaldırıcı yönetici istiyorsa ShellExecute ile
        /// başlatılır ve torunlar süreç anlık görüntüsüyle izlenir.
        /// </summary>
        private (Process? Root, JobObject? Job) Start(string fileName, string arguments)
        {
            if (UacHelper.IsAdministrator())
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        WorkingDirectory = SafeWorkingDirectory(fileName)
                    };
                    var p = Process.Start(psi);
                    if (p != null)
                    {
                        var job = new JobObject();
                        if (job.TryAssign(p.Handle)) return (p, job);
                        job.Dispose();
                        return (p, null);
                    }
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 740)
                {
                    // ERROR_ELEVATION_REQUIRED: aşağıdaki ShellExecute yoluna düş.
                }
            }

            var shell = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                WorkingDirectory = SafeWorkingDirectory(fileName)
            };
            return (Process.Start(shell), null);
        }

        private static string SafeWorkingDirectory(string fileName)
        {
            try
            {
                string? dir = Path.GetDirectoryName(fileName);
                return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : Environment.SystemDirectory;
            }
            catch (ArgumentException)
            {
                return Environment.SystemDirectory;
            }
        }

        /// <summary>
        /// İzlenen süreçlerin çocuklarını ekler ve biri hâlâ yaşıyor mu döndürür.
        /// Inno'nun %TEMP% kopyası gibi ebeveyni çıkmış torunlar da yakalanır: ebeveyn
        /// yaşarken bir kez görülen her PID izlenmeye devam eder.
        /// </summary>
        private bool TrackDescendants(HashSet<int> tracked)
        {
            if (tracked.Count == 0) return false;

            IReadOnlyList<NativeProcess.ProcessEntry> snapshot;
            try
            {
                snapshot = NativeProcess.Snapshot();
            }
            catch (Win32Exception ex)
            {
                _log.Debug($"Süreç anlık görüntüsü alınamadı: {ex.Message}", nameof(UninstallRunner));
                return false;
            }

            bool added;
            do
            {
                added = false;
                foreach (var e in snapshot)
                {
                    if (!tracked.Contains(e.ProcessId) && tracked.Contains(e.ParentProcessId))
                    {
                        tracked.Add(e.ProcessId);
                        added = true;
                    }
                }
            }
            while (added);

            var alive = new HashSet<int>(snapshot.Select(e => e.ProcessId));
            return tracked.Any(alive.Contains);
        }
    }
}
