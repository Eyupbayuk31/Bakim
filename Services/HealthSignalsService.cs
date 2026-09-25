using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Core.Health;
using Microsoft.Win32;

namespace Bakım.Services
{
    /// <summary>
    /// Kontrol Paneli sağlık puanının kalıcı sinyallerini toplar (MASTER_PLAN §5.1).
    /// Pahalı sorgular (olay günlüğü, WMI) 5 dakika önbelleklenir; okunamayan sinyal null döner.
    /// </summary>
    public interface IHealthSignalsService
    {
        Task<HealthSignals> GetAsync(bool forceRefresh = false, CancellationToken ct = default);
    }

    public sealed class HealthSignalsService : IHealthSignalsService
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        private readonly IStartupService _startup;
        private readonly ICrashAnalyzerService _crashes;
        private readonly Activity.IActivityService _activity;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private HealthSignals? _cached;
        private DateTime _cachedAtUtc = DateTime.MinValue;

        public HealthSignalsService(IStartupService startup, ICrashAnalyzerService crashes, Activity.IActivityService activity)
        {
            _startup = startup;
            _crashes = crashes;
            _activity = activity;
        }

        public async Task<HealthSignals> GetAsync(bool forceRefresh = false, CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var now = DateTime.UtcNow;
                if (!forceRefresh && _cached != null && now - _cachedAtUtc < CacheDuration)
                    return WithLiveParts(_cached, now);

                var (bsod, critical) = await ReadStabilityAsync().ConfigureAwait(false);
                var signals = new HealthSignals
                {
                    NowUtc = now,
                    EnabledStartupCount = await ReadStartupCountAsync().ConfigureAwait(false),
                    BsodLast7Days = bsod,
                    CriticalEventsLast7Days = critical,
                    DefenderRealtimeOn = await Task.Run(ReadDefenderRealtime, ct).ConfigureAwait(false),
                    RebootPending = ReadRebootPending()
                };
                _cached = signals;
                _cachedAtUtc = now;
                return WithLiveParts(signals, now);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Ucuz ve hızlı değişen sinyaller her çağrıda tazelenir: disk doluluğu, son temizlik.</summary>
        private HealthSignals WithLiveParts(HealthSignals s, DateTime now)
        {
            string drive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            int? used = null;
            long? free = null;
            try
            {
                var info = new DriveInfo(drive);
                if (info.IsReady && info.TotalSize > 0)
                {
                    free = info.AvailableFreeSpace;
                    used = (int)Math.Round(100.0 * (info.TotalSize - info.TotalFreeSpace) / info.TotalSize);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                AppLog.Debug("Sistem sürücüsü okunamadı: " + ex.Message, nameof(HealthSignalsService));
            }

            DateTime? lastClean = null;
            bool activityKnown = true;
            try
            {
                lastClean = _activity.Entries
                    .Where(e => e.Kind == Core.Activity.ActivityKind.Clean && e.Outcome != Core.Activity.ActivityOutcome.Failed)
                    .Select(e => (DateTime?)e.AtUtc)
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                activityKnown = false;
                AppLog.Debug("Etkinlik kaydı okunamadı: " + ex.Message, nameof(HealthSignalsService));
            }

            return s with
            {
                NowUtc = now,
                SystemDrive = drive.TrimEnd('\\'),
                SystemDriveUsedPercent = used,
                SystemDriveFreeBytes = free,
                LastCleanUtc = lastClean,
                ActivityKnown = activityKnown
            };
        }

        private async Task<int?> ReadStartupCountAsync()
        {
            try
            {
                var items = await _startup.GetStartupProgramsAsync().ConfigureAwait(false);
                return items.Count(i => i.IsEnabled);
            }
            catch (Exception ex)
            {
                AppLog.Debug("Başlangıç listesi okunamadı: " + ex.Message, nameof(HealthSignalsService));
                return null;
            }
        }

        private async Task<(int? Bsod, int? Critical)> ReadStabilityAsync()
        {
            int? bsod = null, critical = null;
            var since = DateTime.Now.AddDays(-7);
            try
            {
                var dumps = await _crashes.GetMinidumpCrashesAsync().ConfigureAwait(false);
                bsod = dumps.Count(d => d.CrashTime >= since);
            }
            catch (Exception ex)
            {
                AppLog.Debug("Minidump listesi okunamadı: " + ex.Message, nameof(HealthSignalsService));
            }
            try
            {
                var events = await _crashes.GetCriticalEventsAsync(7).ConfigureAwait(false);
                // Olaylar modülüyle aynı tanım: Kritik düzey, Kernel-Power (41), beklenmeyen kapanma (6008).
                critical = events.Count(e => e.TimeGenerated >= since &&
                                             (e.Level == "Kritik" || e.EventId == 41 || e.EventId == 6008));
            }
            catch (Exception ex)
            {
                AppLog.Debug("Olay günlüğü okunamadı: " + ex.Message, nameof(HealthSignalsService));
            }
            return (bsod, critical);
        }

        /// <summary>Defender gerçek zamanlı koruma (MSFT_MpComputerStatus). Başka AV varsa ya da okunamazsa null.</summary>
        private static bool? ReadDefenderRealtime()
        {
            try
            {
                var scope = new ManagementScope(@"root\Microsoft\Windows\Defender");
                using var searcher = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT RealTimeProtectionEnabled, AMServiceEnabled FROM MSFT_MpComputerStatus"));
                using var results = searcher.Get();
                foreach (ManagementObject mo in results)
                {
                    using (mo)
                    {
                        if (mo["AMServiceEnabled"] is bool service && !service) return null; // Defender pasif: başka AV
                        if (mo["RealTimeProtectionEnabled"] is bool rtp) return rtp;
                    }
                }
            }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                AppLog.Debug("Defender durumu okunamadı: " + ex.Message, nameof(HealthSignalsService));
            }
            return null;
        }

        private static bool? ReadRebootPending()
        {
            try
            {
                using var wu = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
                using var cbs = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
                return wu != null || cbs != null;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return null;
            }
        }
    }
}
