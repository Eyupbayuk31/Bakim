using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Core.Sentinel;
using Bakım.Helpers;
using Bakım.Models;
using Bakım.Services.Sentinel.Actions;
using Bakım.Services.Sentinel.Detection;
using Bakım.Services.Sentinel.Sensors;
using Bakım.Services.Sentinel.Storage;

namespace Bakım.Services
{
    public interface ISetupSentinelService : IDisposable
    {
        bool IsEnabled { get; set; }
        bool IsMonitoringActiveSession { get; }
        WatchedSetupSession? ActiveSession { get; }
        IReadOnlyList<SetupDeltaReport> RecentReports { get; }
        SentinelProtectionStatus ProtectionStatus { get; }

        void Start();
        void Stop();
        Task<SetupDeltaReport?> FinalizeActiveSessionAsync();
        Task<bool> SaveReportProfileAsync(SetupDeltaReport report);
        List<SetupDeltaReport> LoadSavedReports();
        Task<int> RevertReportAsync(SetupDeltaReport report);

        event Action<WatchedSetupSession>? SetupDetected;
        event Action<SetupDeltaReport>? SetupFinished;
    }

    public sealed class SetupSentinelService : ISetupSentinelService
    {
        private readonly IInstallerMonitorService _installerMonitorService;
        private readonly IAppSettingsService _settingsService;
        private readonly ILogService _log;
        private readonly ISessionStore _sessionStore;

        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly List<SetupDeltaReport> _recentReports = new();
        private readonly ConcurrentDictionary<(int Pid, long CreationTicks), bool> _handledProcesses = new();
        private readonly SemaphoreSlim _finalizeLock = new(1, 1);
        private readonly object _lock = new();

        private CancellationTokenSource? _workerCts;
        private Task? _workerTask;
        private WatchedSetupSession? _activeSession;
        private Dictionary<string, string>? _preInstallHotspot;

        /// <summary>
        /// Boşta iken periyodik alınan kayıt defteri taban görüntüsü (P0-3). Kurulum ancak
        /// başladıktan sonra fark edildiği için o anda alınan görüntü, kurulumun ilk saniyelerde
        /// yazdığı Run/hizmet/Uninstall kayıtlarını "zaten vardı" sayıyordu.
        /// </summary>
        private Dictionary<string, string>? _baselineHotspot;
        private DateTime _baselineTakenUtc = DateTime.MinValue;

        /// <summary>
        /// Sistem durumu tabanı (NÖB 2.1): IFEO, sertifikalar, güvenlik duvarı, görevler …
        /// Kayıt defteri tabanından pahalı olduğu için daha seyrek alınır.
        /// </summary>
        private SystemStateCapture? _baselineSystem;
        private SystemStateCapture? _preSystem;
        private static readonly TimeSpan SystemBaselineInterval = TimeSpan.FromMinutes(3);

        /// <summary>Tanınan süreçler (yalnız nöbetçi döngüsü erişir): tur başına tek süreç görüntüsü.</summary>
        private readonly Dictionary<int, SeenProcess> _seenProcesses = new();
        private readonly Dictionary<string, (string? Description, string? Product)> _versionCache = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Yeni bir süreç bu süre boyunca yeniden değerlendirilir (pencere başlığı geç gelebilir).</summary>
        private static readonly TimeSpan CandidateWindow = TimeSpan.FromSeconds(15);

        private sealed class SeenProcess
        {
            public string ExeName = string.Empty;
            public int ParentPid;
            public long CreationTicks;
            public DateTime FirstSeenUtc;
            public bool Final;
        }
        private static readonly TimeSpan BaselineInterval = TimeSpan.FromSeconds(60);
        private bool _isDisposed;
        private bool _isEnabled;

        private static readonly string[] ExecutableExtensions = new[]
        {
            ".exe", ".dll", ".sys", ".bat", ".cmd", ".ps1", ".vbs", ".msi"
        };

        private readonly UsnJournalSensor? _usnSensor;
        private readonly KernelTraceSensor? _kernelTrace;
        private bool _isUsnActive;
        private bool _isTraceActive;

        public SentinelProtectionStatus ProtectionStatus { get; private set; }

        public SetupSentinelService(
            IInstallerMonitorService installerMonitorService,
            IAppSettingsService settingsService,
            ILogService log,
            ISessionStore? sessionStore = null)
        {
            _installerMonitorService = installerMonitorService;
            _settingsService = settingsService;
            _log = log;
            _sessionStore = sessionStore ?? new SessionStore();

            try
            {
                _usnSensor = new UsnJournalSensor();
                _isUsnActive = _usnSensor.InitializeDrive('C');
            }
            catch (Exception ex)
            {
                _log.Debug($"USN başlatılamadı: {ex.Message}", nameof(SetupSentinelService));
            }

            try
            {
                _kernelTrace = new KernelTraceSensor();
                _kernelTrace.ProcessStarted += OnKernelProcessStarted;
                _isTraceActive = _kernelTrace.Start();
            }
            catch (Exception ex)
            {
                _log.Debug($"Kernel izleyici başlatılamadı: {ex.Message}", nameof(SetupSentinelService));
            }

            bool isAdmin = UacHelper.IsAdministrator();
            ProtectionStatus = SentinelProtectionPolicy.Evaluate(isAdmin, _isUsnActive, _isTraceActive);

            _isEnabled = _settingsService.Current.IsSentinelSetupGuardEnabled;

            if (_isEnabled)
            {
                Start();
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                _settingsService.Update(s => s.IsSentinelSetupGuardEnabled = value);

                if (_isEnabled)
                {
                    Start();
                    _log.Info("Sentinel Kurulum Nöbetçisi başlatıldı.", nameof(SetupSentinelService));
                }
                else
                {
                    Stop();
                    _log.Info("Sentinel Kurulum Nöbetçisi durduruldu.", nameof(SetupSentinelService));
                }
            }
        }

        public bool IsMonitoringActiveSession => _activeSession != null && _activeSession.IsActive;
        public WatchedSetupSession? ActiveSession => _activeSession;
        public IReadOnlyList<SetupDeltaReport> RecentReports
        {
            get
            {
                lock (_lock)
                {
                    return _recentReports.ToList();
                }
            }
        }

        public event Action<WatchedSetupSession>? SetupDetected;
        public event Action<SetupDeltaReport>? SetupFinished;

        public void Start()
        {
            if (_isDisposed) return;

            lock (_lock)
            {
                if (_workerTask != null && !_workerTask.IsCompleted) return;

                _workerCts = new CancellationTokenSource();
                _workerTask = Task.Run(() => PollingLoopAsync(_workerCts.Token));
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _workerCts?.Cancel();
                _workerCts?.Dispose();
                _workerCts = null;
                _workerTask = null;
                StopFileSystemWatchers();
            }
        }

        private async Task PollingLoopAsync(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1500));

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (await timer.WaitForNextTickAsync(ct))
                    {
                        if (!_isEnabled || _isDisposed) continue;

                        if (IsMonitoringActiveSession)
                        {
                            await CheckActiveSessionProcessesAsync();
                        }
                        else
                        {
                            await ScanNewProcessesAsync();
                            RefreshBaselineIfDue();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.Error("Kurulum nöbetçi döngüsünde hata.", ex, nameof(SetupSentinelService));
                }
            }
        }

        /// <summary>
        /// Yeni süreçleri tarar. Tur başına TEK süreç görüntüsü alınır (eskiden her süreç için ayrı
        /// Toolhelp görüntüsü ve FileVersionInfo okunuyordu: 1,5 sn'de bir yüzlerce çağrı).
        /// Bir süreç yalnızca ilk <see cref="CandidateWindow"/> boyunca değerlendirilir.
        /// </summary>
        private async Task ScanNewProcessesAsync()
        {
            IReadOnlyList<NativeProcess.ProcessEntry> snapshot;
            try
            {
                snapshot = NativeProcess.Snapshot();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                _log.Debug($"Süreç görüntüsü alınamadı: {ex.Message}", nameof(SetupSentinelService));
                return;
            }

            int currentPid = Environment.ProcessId;
            var now = DateTime.UtcNow;
            var alive = new HashSet<int>();

            foreach (var entry in snapshot)
            {
                int pid = entry.ProcessId;
                if (pid == currentPid || pid <= 4) continue;
                alive.Add(pid);

                // PID yeniden kullanımı: aynı PID'de başka bir exe ya da ebeveyn → yeni süreç.
                if (_seenProcesses.TryGetValue(pid, out var seen) &&
                    (!string.Equals(seen.ExeName, entry.ExeName, StringComparison.OrdinalIgnoreCase) || seen.ParentPid != entry.ParentProcessId))
                {
                    seen = null;
                }
                if (seen == null)
                {
                    seen = new SeenProcess
                    {
                        ExeName = entry.ExeName,
                        ParentPid = entry.ParentProcessId,
                        CreationTicks = ProcessInfoReader.GetProcessCreationTimeUtc(pid)?.Ticks ?? 0,
                        FirstSeenUtc = now
                    };
                    _seenProcesses[pid] = seen;
                }
                if (seen.Final) continue;

                var key = (pid, seen.CreationTicks);
                if (_handledProcesses.ContainsKey(key)) { seen.Final = true; continue; }

                // Bakım'ın başlattığı kaldırıcı ağacı: kurulum oturumu açılmaz.
                if (Sentinel.Detection.SentinelSuppression.IsSuppressed(pid, entry.ParentProcessId))
                {
                    _handledProcesses[key] = true;
                    seen.Final = true;
                    continue;
                }

                try
                {
                    if (await EvaluateCandidateAsync(pid, entry, seen)) break;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
                {
                    // Süreç bu arada kapandı ya da erişilemiyor.
                    seen.Final = true;
                }

                if (now - seen.FirstSeenUtc > CandidateWindow) seen.Final = true;
            }

            foreach (int gone in _seenProcesses.Keys.Where(p => !alive.Contains(p)).ToList())
                _seenProcesses.Remove(gone);
            if (_versionCache.Count > 1024) _versionCache.Clear();
            if (_handledProcesses.Count > 4096) _handledProcesses.Clear();
        }

        private static readonly string[] InstalledRoots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            }
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p.TrimEnd('\\') + "\\")
            .ToArray();

        private static bool IsInstalledLocation(string exePath) =>
            InstalledRoots.Any(root => exePath.StartsWith(root, StringComparison.OrdinalIgnoreCase));

        /// <summary>Süreci sınıflandırır; kurulumsa oturum açar ve true döner.</summary>
        private async Task<bool> EvaluateCandidateAsync(int pid, NativeProcess.ProcessEntry entry, SeenProcess seen)
        {
            string processName = Path.GetFileNameWithoutExtension(entry.ExeName);
            string? exePath = ProcessInfoReader.GetProcessExecutablePath(pid);

            // Zaten kurulu konumdan çalışan süreç kurulum değildir (msiexec hariç): pencere başlığı ve
            // sürüm bilgisi hiç okunmaz. Sistemdeki süreçlerin çoğu burada elenir.
            if (exePath != null && IsInstalledLocation(exePath) && !processName.Equals("msiexec", StringComparison.OrdinalIgnoreCase))
            {
                seen.Final = true;
                return false;
            }

            string? title = null;
            try
            {
                using var proc = Process.GetProcessById(pid);
                title = proc.MainWindowTitle;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                seen.Final = true; // kapandı
                return false;
            }

            string? desc = null, prod = null;
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                if (!_versionCache.TryGetValue(exePath, out var version) && File.Exists(exePath))
                {
                    try
                    {
                        var fvi = FileVersionInfo.GetVersionInfo(exePath);
                        version = (fvi.FileDescription, fvi.ProductName);
                    }
                    catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
                    {
                        version = (null, null);
                    }
                    _versionCache[exePath] = version;
                }
                (desc, prod) = version;
            }

            string? commandLine = processName.Equals("msiexec", StringComparison.OrdinalIgnoreCase)
                ? ProcessInfoReader.TryGetCommandLine(pid)
                : null;

            bool isTarget = InstallerClassifier.ClassifyProcess(processName, exePath, title, desc, prod, commandLine,
                out SessionKind kind, out string detectedAppName, out int confidenceScore);

            // Sınırdaki aday: kurulum çatısı izi ve indirme kaynağı puana eklenir (NÖB 1.2, 1.3).
            if (!isTarget && kind == SessionKind.Unknown && confidenceScore >= 20 && !string.IsNullOrWhiteSpace(exePath))
            {
                int extra = InstallerInspector.ExtraScore(exePath);
                if (extra > 0)
                {
                    isTarget = InstallerClassifier.ClassifyProcess(processName, exePath, title, desc, prod, commandLine,
                        out kind, out detectedAppName, out confidenceScore, extra);
                }
            }

            if (!isTarget) return false;

            var key = (pid, seen.CreationTicks);
            _handledProcesses[key] = true;
            seen.Final = true;

            if (kind == SessionKind.Uninstall)
            {
                _log.Info($"Kaldırma süreci saptandı (oturum açılmadı): {detectedAppName} (PID: {pid})", nameof(SetupSentinelService));
                return false;
            }

            if (kind == SessionKind.Install && confidenceScore >= 50)
            {
                await StartSessionAsync(pid, seen.CreationTicks, processName, detectedAppName, exePath ?? processName);
                return true;
            }
            return false;
        }

        private void RefreshBaselineIfDue()
        {
            if (IsMonitoringActiveSession) return;
            if (DateTime.UtcNow - _baselineTakenUtc >= BaselineInterval)
            {
                try
                {
                    _baselineHotspot = RegistryHotspotSensor.CaptureHotspotSnapshot();
                    _baselineTakenUtc = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    _log.Debug($"Nöbetçi taban görüntüsü alınamadı: {ex.Message}", nameof(SetupSentinelService));
                }
            }
            if (_baselineSystem == null || DateTime.UtcNow - _baselineSystem.TakenUtc >= SystemBaselineInterval)
            {
                _baselineSystem = SystemStateSensor.Capture();
            }
        }

        private async Task StartSessionAsync(int rootPid, long creationTicks, string procName, string appName, string exePath)
        {
            var session = new WatchedSetupSession
            {
                RootProcessId = rootPid,
                RootProcessCreationTicks = creationTicks,
                ProcessName = procName,
                AppName = appName,
                InstallerPath = exePath,
                Kind = SessionKind.Install,
                StartTime = DateTime.UtcNow,
                IsActive = true
            };

            session.TrackedProcesses[(rootPid, creationTicks)] = true;
            session.TrackedProcessIds.Add(rootPid);
            _activeSession = session;

            _log.Info($"Kurulum tespit edildi: {appName} (PID: {rootPid}). Değişiklikler arka planda izleniyor...", nameof(SetupSentinelService));

            // 1. Sensörleri Başlat (FSW 64 KB arabellek)
            StartFileSystemWatchers(session);

            // 2. Pre-Snapshot: Registry Hotspot ve Run/Services Değerleri (P0-3, P0-4)
            try
            {
                // Kurulum sürecinden ÖNCE alınmış taban varsa o kullanılır; yoksa şimdi alınır.
                var processStartUtc = creationTicks > 0 ? new DateTime(creationTicks, DateTimeKind.Utc) : DateTime.UtcNow;
                bool baselineIsBefore = _baselineHotspot != null && _baselineTakenUtc <= processStartUtc;
                _preInstallHotspot = baselineIsBefore ? _baselineHotspot : RegistryHotspotSensor.CaptureHotspotSnapshot();
                if (baselineIsBefore)
                    _log.Debug($"Kurulum öncesi taban görüntüsü kullanıldı ({(processStartUtc - _baselineTakenUtc).TotalSeconds:F0} sn önce).", nameof(SetupSentinelService));
                _preSystem = _baselineSystem != null && _baselineSystem.TakenUtc <= processStartUtc
                    ? _baselineSystem
                    : SystemStateSensor.Capture();
                session.InstallerInfoTask = Task.Run(() => InstallerInspector.Inspect(exePath));
                session.PreSnapshot = await _installerMonitorService.TakePreInstallSnapshotAsync(appName);
            }
            catch (Exception ex)
            {
                _log.Error("Kurulum öncesi snapshot alınamadı.", ex, nameof(SetupSentinelService));
            }

            SetupDetected?.Invoke(session);
        }

        private void StartFileSystemWatchers(WatchedSetupSession session)
        {
            StopFileSystemWatchers();

            var targetDirs = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            };

            foreach (var dir in targetDirs)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;

                    var fsw = new FileSystemWatcher(dir)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        InternalBufferSize = 65536, // P0-10: 64 KB arabellek
                        EnableRaisingEvents = true
                    };

                    fsw.Created += (s, e) => RecordFileEvent(session, e.FullPath, "Created");
                    fsw.Changed += (s, e) => RecordFileEvent(session, e.FullPath, "Changed");
                    fsw.Deleted += (s, e) => RecordFileEvent(session, e.FullPath, "Deleted");
                    fsw.Renamed += (s, e) => RecordFileEvent(session, e.FullPath, "Renamed", e.OldFullPath);
                    fsw.Error += (s, e) =>
                    {
                        session.IsPossiblyIncomplete = true;
                        _log.Warning($"Dosya izleyici arabelleği taştı ({dir}). Rapor eksik olabilir.", null, nameof(SetupSentinelService));
                    };

                    _watchers.Add(fsw);
                }
                catch { }
            }
        }

        private void StopFileSystemWatchers()
        {
            foreach (var w in _watchers)
            {
                try
                {
                    w.EnableRaisingEvents = false;
                    w.Dispose();
                }
                catch { }
            }
            _watchers.Clear();
        }

        private void RecordFileEvent(WatchedSetupSession session, string fullPath, string changeType, string? oldPath = null)
        {
            if (!session.IsActive) return;

            // Gürültü Filtresi (P0-10): Tarayıcı önbellekleri, Bakım logları ve çöp kutusu
            if (IsNoisePath(fullPath)) return;

            try
            {
                string ext = Path.GetExtension(fullPath).ToLowerInvariant();
                bool isExe = ExecutableExtensions.Contains(ext);

                long size = 0;
                if (changeType != "Deleted" && File.Exists(fullPath))
                {
                    try { size = new FileInfo(fullPath).Length; } catch { }
                }

                session.CapturedFileEvents.Add(new SetupFileEvent
                {
                    FilePath = fullPath,
                    ChangeType = changeType,
                    OldFilePath = oldPath,
                    SizeBytes = size,
                    Timestamp = DateTime.UtcNow,
                    IsExecutable = isExe,
                    Extension = ext
                });
            }
            catch { }
        }

        private static bool IsNoisePath(string path) => SentinelNoise.IsNoisePath(path);

        private async Task CheckActiveSessionProcessesAsync()
        {
            var session = _activeSession;
            if (session == null || !session.IsActive) return;

            IReadOnlyList<NativeProcess.ProcessEntry> snapshot;
            try
            {
                snapshot = NativeProcess.Snapshot();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                _log.Debug($"Süreç görüntüsü alınamadı: {ex.Message}", nameof(SetupSentinelService));
                return;
            }

            // 1. Yeni alt süreçler: ebeveyni ağaçta olanlar (P0-8). Tek görüntü, ebeveyn zinciri
            //    birkaç geçişte kapanır (torunlar aynı turda eklenir).
            string rootName = session.ProcessName.ToLowerInvariant();
            bool added;
            do
            {
                added = false;
                foreach (var entry in snapshot)
                {
                    if (session.TrackedProcessIds.Contains(entry.ProcessId)) continue;
                    string name = Path.GetFileNameWithoutExtension(entry.ExeName).ToLowerInvariant();
                    bool childOfTracked = session.TrackedProcessIds.Contains(entry.ParentProcessId);

                    // msiexec: yalnızca ebeveyni ağaçtaysa (arka plandaki "msiexec /V" hizmeti eklenmez).
                    bool include = name == "msiexec"
                        ? childOfTracked
                        : (childOfTracked || (rootName.Length > 0 && name.Contains(rootName))) && !InstallerClassifier.ExcludedProcessNames.Contains(name);
                    if (!include) continue;

                    long ticks = ProcessInfoReader.GetProcessCreationTimeUtc(entry.ProcessId)?.Ticks ?? 0;
                    session.TrackedProcesses[(entry.ProcessId, ticks)] = true;
                    session.TrackedProcessIds.Add(entry.ProcessId);
                    added = true;
                }
            }
            while (added);

            // 2. İzlenen süreçlerden hayatta olan var mı? (PID yeniden kullanımına karşı oluşturma zamanı)
            var alivePids = new HashSet<int>(snapshot.Select(e => e.ProcessId));
            bool anyAlive = false;
            foreach (var (pid, ticks) in session.TrackedProcesses.Keys)
            {
                if (!alivePids.Contains(pid)) continue;
                var created = ProcessInfoReader.GetProcessCreationTimeUtc(pid);
                if (ticks == 0 || (created.HasValue && Math.Abs(created.Value.Ticks - ticks) < TimeSpan.FromSeconds(5).Ticks))
                {
                    anyAlive = true;
                    break;
                }
            }

            // 3. Süreçler kapandıysa ve süren bir MSI işlemi yoksa oturumu sonlandır.
            if (!anyAlive)
            {
                var (hasMsi, isCompleted, _) = MsiEventLogWatcher.QueryLatestMsiStatus(session.StartTime);
                // MSI bitişi olay günlüğüne hiç yazılmazsa oturum sonsuza dek açık kalmasın.
                if (hasMsi && !isCompleted && DateTime.UtcNow - session.StartTime < TimeSpan.FromHours(2))
                {
                    // MSI işlemi henüz olay günlüğünde bitiş bildirmedi, kısa süre daha bekle
                    return;
                }

                await FinalizeActiveSessionAsync();
            }
        }

        public async Task<SetupDeltaReport?> FinalizeActiveSessionAsync()
        {
            if (!await _finalizeLock.WaitAsync(100)) return null;

            try
            {
                WatchedSetupSession? session = _activeSession;
                if (session == null || !session.IsActive) return null;

                session.IsActive = false;
                session.EndTime = DateTime.UtcNow;

                _log.Info($"Kurulum süreçleri sonlandı. {session.AppName} için delta hesaplanıyor...", nameof(SetupSentinelService));

                // Dosya sistemi yazmalarının diske oturması için 1.5 saniye bekle
                await Task.Delay(1500);
                StopFileSystemWatchers();

                var report = new SetupDeltaReport
                {
                    SessionId = session.SessionId,
                    AppName = session.AppName,
                    InstallerPath = session.InstallerPath,
                    InstallTime = session.StartTime,
                    Duration = (session.EndTime ?? DateTime.UtcNow) - session.StartTime,
                    IsPossiblyIncomplete = session.IsPossiblyIncomplete
                };

                // P0-1: Dosyaları Created, Modified, Deleted olarak net ayrıştır
                var uniqueCreated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var uniqueModified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var uniqueDeleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var uniqueFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long totalBytes = 0;

                foreach (var ev in session.CapturedFileEvents)
                {
                    if (ev.ChangeType == "Deleted")
                    {
                        uniqueDeleted.Add(ev.FilePath);
                        uniqueCreated.Remove(ev.FilePath);
                    }
                    else if (ev.ChangeType == "Created")
                    {
                        if (File.Exists(ev.FilePath))
                        {
                            uniqueCreated.Add(ev.FilePath);
                            totalBytes += ev.SizeBytes;
                        }
                        else if (Directory.Exists(ev.FilePath))
                        {
                            uniqueFolders.Add(ev.FilePath);
                        }
                    }
                    else if (ev.ChangeType == "Changed")
                    {
                        if (!uniqueCreated.Contains(ev.FilePath) && File.Exists(ev.FilePath))
                        {
                            uniqueModified.Add(ev.FilePath);
                        }
                    }
                }

                report.CreatedFiles = uniqueCreated.ToList();
                report.ModifiedFiles = uniqueModified.ToList();
                report.DeletedFiles = uniqueDeleted.ToList();
                report.AddedFiles = report.CreatedFiles; // Geriye döküm uyumluluk
                report.AddedFolders = uniqueFolders.ToList();

                foreach (var f in report.CreatedFiles)
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ExecutableExtensions.Contains(ext))
                    {
                        report.AddedExecutables.Add(f);
                    }
                }

                // USN Journal Değişiklikleri (Tam Koruma Modu / NÖB Faz 8)
                if (_usnSensor != null && _isUsnActive)
                {
                    try
                    {
                        var usnRecords = _usnSensor.PollChanges('C');
                        foreach (var record in usnRecords)
                        {
                            if (string.IsNullOrWhiteSpace(record.FileName)) continue;
                            if (record.IsFileCreated && !uniqueCreated.Any(f => f.EndsWith(record.FileName, StringComparison.OrdinalIgnoreCase)))
                            {
                                string candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), record.FileName);
                                if (File.Exists(candidate))
                                {
                                    uniqueCreated.Add(candidate);
                                    if (ExecutableExtensions.Contains(Path.GetExtension(candidate).ToLowerInvariant()))
                                    {
                                        report.AddedExecutables.Add(candidate);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Debug($"USN günlüğü okunamadı: {ex.Message}", nameof(SetupSentinelService));
                    }
                }

                // Registry Delta: Hotspot Sensörü ile Run ve Services Değerleri (P0-4, P0-5)
                try
                {
                    var postHotspot = RegistryHotspotSensor.CaptureHotspotSnapshot();
                    if (_preInstallHotspot != null)
                    {
                        var (records, services, startups) = RegistryHotspotSensor.ComputeDelta(_preInstallHotspot, postHotspot);
                        report.AddedRegistryRecords = records;
                        report.AddedServices = services;
                        report.AddedStartupEntries = startups;
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("Registry delta hesaplamasında hata.", ex, nameof(SetupSentinelService));
                }

                report.TotalSizeBytes = totalBytes;
                report.FormattedSize = FormatBytes(totalBytes);

                // Nöbetçi v2: kurulum dosyası, sistem alanları ve risk kararı (NÖB 2–4).
                await EvaluateRiskAsync(session, report);

                // Raporu Tekil SessionStore'a Kaydet (P0-6)
                await _sessionStore.SaveReportAsync(report);
                RecordActivity(report);

                lock (_lock)
                {
                    _recentReports.Insert(0, report);
                    if (_recentReports.Count > 20)
                    {
                        _recentReports.RemoveAt(_recentReports.Count - 1);
                    }
                }

                _log.Info($"Kurulum tamamlandı: {report.AppName} - +{report.CreatedFiles.Count} dosya, +{report.AddedExecutables.Count} yürütülebilir, +{report.AddedStartupEntries.Count} başlangıç girdisi.", nameof(SetupSentinelService));

                SetupFinished?.Invoke(report);
                return report;
            }
            finally
            {
                _activeSession = null;
                _preInstallHotspot = null;
                _preSystem = null;
                _baselineTakenUtc = DateTime.MinValue; // kurulum bitti: taban hemen yenilensin
                _finalizeLock.Release();
            }
        }

        private static readonly HashSet<string> MasqueradeExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".txt", ".dat", ".log", ".pdf", ".doc", ".docx",
            ".xls", ".xlsx", ".mp3", ".mp4", ".ini", ".cfg", ".json", ".xml", ".db"
        };

        /// <summary>
        /// Kurulum dosyası bilgisi, sistem alanı farkı, yeni programlar ve risk kararı. Hiçbir adım
        /// raporu bozmaz: okunamayan parça rapora girmez, karar eldeki kanıtla verilir.
        /// </summary>
        private async Task EvaluateRiskAsync(WatchedSetupSession session, SetupDeltaReport report)
        {
            try
            {
                if (session.InstallerInfoTask != null &&
                    await Task.WhenAny(session.InstallerInfoTask, Task.Delay(TimeSpan.FromSeconds(10))) == session.InstallerInfoTask)
                    report.Installer = await session.InstallerInfoTask;
            }
            catch (Exception ex)
            {
                _log.Debug($"Kurulum dosyası incelenemedi: {ex.Message}", nameof(SetupSentinelService));
            }

            try
            {
                var pre = _preSystem;
                if (pre != null)
                {
                    var post = SystemStateSensor.Capture();
                    report.SystemChanges = SystemStateSnapshot.Diff(pre.Values, post.Values, pre.ReadableAreas, post.ReadableAreas)
                        .Where(c => !SentinelNoise.IsNoiseRegistryKey(c.Key) && !SystemStateSnapshot.IsLikelyWindowsNoise(c))
                        .Take(500)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                _log.Warning("Sistem alanı farkı hesaplanamadı.", ex, nameof(SetupSentinelService));
            }

            // Yeni Uninstall kayıtları (görüntüdeki değer DisplayName'dir).
            report.NewPrograms = report.AddedRegistryRecords
                .Where(r => r.ChangeKind is "KeyAdded" or "ValueAdded" &&
                            r.KeyPath.Contains(@"\Uninstall\", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(r.ValueData))
                .Select(r => r.ValueData.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var input = new SetupRiskInput
            {
                InstallerSigned = report.Installer?.Signed,
                InstallerFromInternet = report.Installer?.FromInternet == true,
                WindowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            };
            input.SystemChanges.AddRange(report.SystemChanges);
            input.NewPrograms.AddRange(report.NewPrograms);

            foreach (var record in report.AddedRegistryRecords.Where(r => r.ChangeKind == "ValueAdded" && r.IsAutorunOrService &&
                                                                           !r.KeyPath.Contains(@"\Services\", StringComparison.OrdinalIgnoreCase)))
            {
                string name = record.KeyPath[(record.KeyPath.LastIndexOf('\\') + 1)..];
                string? target = Core.Uninstall.UninstallCommandParser.Parse(record.ValueData, File.Exists)?.FileName;
                input.Startup.Add(new StartupAddition(name, record.ValueData, target, InstallerInspector.IsSigned(target), record.KeyPath));
            }

            foreach (string serviceKey in report.AddedServices)
            {
                string name = serviceKey[(serviceKey.LastIndexOf('\\') + 1)..];
                var (imagePath, isDriver, autoStart) = ReadServiceFacts(name);
                string normalized = Core.Uninstall.FootprintText.NormalizeServiceImagePath(imagePath);
                string? target = Core.Uninstall.UninstallCommandParser.Parse(normalized, File.Exists)?.FileName;
                input.Services.Add(new ServiceAddition(name, imagePath, isDriver, autoStart, InstallerInspector.IsSigned(target)));
            }

            string windows = input.WindowsDirectory.TrimEnd('\\') + "\\";
            foreach (string exe in report.AddedExecutables.Where(e => e.StartsWith(windows, StringComparison.OrdinalIgnoreCase)).Take(50))
                input.Executables.Add(new ExecutableDrop(exe, InstallerInspector.IsSigned(exe), false));
            foreach (string file in report.CreatedFiles.Where(f => MasqueradeExtensions.Contains(Path.GetExtension(f))).Take(500))
            {
                if (InstallerInspector.LooksLikeHiddenPe(file)) input.Executables.Add(new ExecutableDrop(file, null, true));
            }

            var risk = SetupRiskEngine.Evaluate(input);
            report.RiskEvaluated = true;
            report.RiskScore = risk.Score;
            report.RiskVerdict = risk.Verdict;
            report.RiskFindings = risk.Findings.ToList();

            var top = risk.Top(1).FirstOrDefault();
            report.QuickRiskSummary = SetupRiskEngine.VerdictLabel(risk.Verdict) +
                                      (top != null ? $" · {top.Title}" : risk.Verdict == RiskVerdict.Clean ? " · kalıcılık ya da sistem değişikliği yok" : "");
            report.RiskBadgeBrush = risk.Verdict >= RiskVerdict.Caution ? "SystemFillColorCautionBrush" : "AccentTextFillColorPrimaryBrush";
        }

        private static (string ImagePath, bool IsDriver, bool AutoStart) ReadServiceFacts(string name)
        {
            try
            {
                using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
                using var key = root.OpenSubKey($@"{RegistryHotspotSensor.ServicesSubKeyPath}\{name}");
                if (key == null) return (string.Empty, false, false);
                string image = key.GetValue("ImagePath", string.Empty, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty;
                bool driver = key.GetValue("Type") is int type && (type & 0x3) != 0;
                bool auto = key.GetValue("Start") is int start && start <= 2;
                return (image, driver, auto);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return (string.Empty, false, false);
            }
        }

        /// <summary>Kurulum oturumunu Etkinlik Merkezi'ne yazar (ayrıntılı rapor Kaldırıcı'dadır).</summary>
        private static void RecordActivity(SetupDeltaReport report)
        {
            var activity = App.TryGetService<Activity.IActivityService>();
            if (activity == null) return;

            string verb = report.Kind switch
            {
                SessionKind.Uninstall => "kaldırıldı",
                SessionKind.Update => "güncellendi",
                _ => "kuruldu"
            };
            string summary = $"+{report.CreatedFiles.Count:N0} dosya · {report.FormattedSize}" +
                             (report.AddedStartupEntries.Count > 0 ? $" · {report.AddedStartupEntries.Count} başlangıç girdisi" : "") +
                             (report.AddedServices.Count > 0 ? $" · {report.AddedServices.Count} hizmet" : "") +
                             (string.IsNullOrWhiteSpace(report.QuickRiskSummary) ? "" : $" · {report.QuickRiskSummary}");
            var items = report.AddedExecutables.Select(e => new Core.Activity.ActivityItem(e, "Yürütülebilir", "Eklendi"))
                .Concat(report.AddedStartupEntries.Select(e => new Core.Activity.ActivityItem(e, "Başlangıç", "Eklendi")))
                .Concat(report.AddedServices.Select(e => new Core.Activity.ActivityItem(e, "Hizmet", "Eklendi")));

            Activity.ActivityRecording.RecordSimple(activity, Core.Activity.ActivityKind.SetupSession, "Kurulum Nöbetçisi",
                $"\"{report.AppName}\" {verb}", summary,
                report.IsPossiblyIncomplete ? Core.Activity.ActivityOutcome.PartiallySucceeded : Core.Activity.ActivityOutcome.Succeeded,
                items, deepLink: "Sentinel");
        }

        public async Task<bool> SaveReportProfileAsync(SetupDeltaReport report)
        {
            return await _sessionStore.SaveReportAsync(report);
        }

        public List<SetupDeltaReport> LoadSavedReports()
        {
            return _sessionStore.LoadAllReportsAsync().GetAwaiter().GetResult();
        }

        public async Task<int> RevertReportAsync(SetupDeltaReport report)
        {
            var result = await _sessionStore.RollbackReportAsync(report);
            return result.DeletedFilesCount + result.DeletedFoldersCount;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            double mb = bytes / (1024.0 * 1024.0);
            if (mb < 1.0) return $"{bytes / 1024.0:F1} KB";
            if (mb >= 1024.0) return $"{mb / 1024.0:F2} GB";
            return $"{mb:F1} MB";
        }

        private void OnKernelProcessStarted(int pid, string name, int parentPid)
        {
            var session = _activeSession;
            if (session != null && session.IsActive)
            {
                lock (_lock)
                {
                    if (session.TrackedProcessIds.Contains(parentPid) && !session.TrackedProcessIds.Contains(pid))
                    {
                        session.TrackedProcessIds.Add(pid);
                        long creationTicks = ProcessInfoReader.GetProcessCreationTimeUtc(pid)?.Ticks ?? DateTime.UtcNow.Ticks;
                        session.TrackedProcesses[(pid, creationTicks)] = true;
                        _log.Debug($"[Tam Koruma] Çocuk süreç bağlandı: {name} (PID: {pid}, Ebeveyn: {parentPid})", nameof(SetupSentinelService));
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();
            _kernelTrace?.Dispose();
            _usnSensor?.Dispose();
            _finalizeLock.Dispose();
        }
    }
}
