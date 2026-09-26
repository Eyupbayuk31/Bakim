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
        Task<List<SetupDeltaReport>> LoadSavedReportsAsync();
        List<SetupDeltaReport> LoadSavedReports();

        event Action<WatchedSetupSession>? SetupDetected;
        event Action<SetupDeltaReport>? SetupFinished;
    }

    public sealed class SetupSentinelService : ISetupSentinelService
    {
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

        /// <summary>Kurulumun geçici klasörleri: buradan çalışan süreç kurulan uygulama sayılmaz (A10).</summary>
        private static readonly string[] TempRoots = new[]
            {
                Path.GetTempPath(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
            }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();

        /// <summary>Finalize'da boyutu tek tek okunan en fazla dosya; fazlası ortalamayla tahmin edilir.</summary>
        private const int MaxSizedFiles = 20_000;

        // NTFS USN günlüğü Faz C'de oturum imleciyle yeniden bağlanacak (NÖB v3 A2): eski bağlantı
        // saatler önceki kayıtları okuyor ve yolu uyduruyordu.
        private readonly WmiProcessSensor? _processEvents;
        private bool _isTraceActive;
        private long _eventSequence;

        public SentinelProtectionStatus ProtectionStatus { get; private set; }

        public SetupSentinelService(
            IAppSettingsService settingsService,
            ILogService log,
            ISessionStore? sessionStore = null)
        {
            _settingsService = settingsService;
            _log = log;
            _sessionStore = sessionStore ?? new SessionStore();

            try
            {
                _processEvents = new WmiProcessSensor();
                _processEvents.ProcessStarted += OnProcessStartedEvent;
                _isTraceActive = _processEvents.Start();
            }
            catch (Exception ex)
            {
                _log.Debug($"WMI süreç izleyicisi başlatılamadı: {ex.Message}", nameof(SetupSentinelService));
            }

            bool isAdmin = UacHelper.IsAdministrator();
            ProtectionStatus = SentinelProtectionPolicy.Evaluate(isAdmin, isUsnAvailable: false, _isTraceActive);

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
                            ScanNewProcesses();
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
        private void ScanNewProcesses()
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
            var byPid = new Dictionary<int, NativeProcess.ProcessEntry>();
            foreach (var entry in snapshot) byPid[entry.ProcessId] = entry;

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
                    if (EvaluateCandidate(pid, entry, seen, byPid)) break;
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
        private bool EvaluateCandidate(int pid, NativeProcess.ProcessEntry entry, SeenProcess seen,
            IReadOnlyDictionary<int, NativeProcess.ProcessEntry> byPid)
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
                // Oyun başlatıcısı, güncelleyici ya da Windows hizmeti başlattıysa kullanıcının kurulumu değildir (A8).
                if (!_settingsService.Current.SentinelWatchBackgroundInstalls &&
                    SetupSessionPolicy.FindBackgroundAncestor(pid, p => LiveParentOf(p, byPid)) is { } launcher)
                {
                    _log.Info($"Arka plan kurulumu atlandı: {detectedAppName} (PID: {pid}, başlatan: {launcher}).", nameof(SetupSentinelService));
                    return false;
                }

                StartSession(pid, seen.CreationTicks, processName, detectedAppName, exePath ?? processName);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Sürecin hâlâ yaşayan ebeveyni. Ebeveyn PID'i sonradan başka bir sürece geçmişse
        /// (ebeveyn çocuktan sonra oluşmuş) zincir kopar.
        /// </summary>
        private static (int Pid, string ExeName)? LiveParentOf(int pid, IReadOnlyDictionary<int, NativeProcess.ProcessEntry> byPid)
        {
            if (!byPid.TryGetValue(pid, out var child) || !byPid.TryGetValue(child.ParentProcessId, out var parent)) return null;
            var childCreated = ProcessInfoReader.GetProcessCreationTimeUtc(pid);
            var parentCreated = ProcessInfoReader.GetProcessCreationTimeUtc(parent.ProcessId);
            if (childCreated.HasValue && parentCreated.HasValue && parentCreated.Value > childCreated.Value) return null;
            return (parent.ProcessId, parent.ExeName);
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

        private void StartSession(int rootPid, long creationTicks, string procName, string appName, string exePath)
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
            session.TrackedProcessIds.TryAdd(rootPid, 0);
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

            // Olay iş parçacığında disk erişimi yok (H-16): boyut ve varlık finalize'da okunur.
            string ext = Path.GetExtension(fullPath).ToLowerInvariant();
            session.CapturedFileEvents.Add(new SetupFileEvent
            {
                Sequence = Interlocked.Increment(ref _eventSequence),
                FilePath = fullPath,
                ChangeType = changeType,
                OldFilePath = oldPath,
                Timestamp = DateTime.UtcNow,
                IsExecutable = ExecutableExtensions.Contains(ext),
                Extension = ext
            });
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
            //    birkaç geçişte kapanır (torunlar aynı turda eklenir). Ağaçtan ayrılan uygulamanın
            //    çocukları eklenmez.
            string rootName = session.ProcessName.ToLowerInvariant();
            bool added;
            do
            {
                added = false;
                foreach (var entry in snapshot)
                {
                    if (session.TrackedProcessIds.ContainsKey(entry.ProcessId) || session.DetachedProcessIds.ContainsKey(entry.ProcessId)) continue;
                    string name = Path.GetFileNameWithoutExtension(entry.ExeName).ToLowerInvariant();
                    bool childOfTracked = session.TrackedProcessIds.ContainsKey(entry.ParentProcessId) &&
                                          !session.DetachedProcessIds.ContainsKey(entry.ParentProcessId);

                    // msiexec: yalnızca ebeveyni ağaçtaysa (arka plandaki "msiexec /V" hizmeti eklenmez).
                    bool include = name == "msiexec"
                        ? childOfTracked
                        : (childOfTracked || (rootName.Length > 0 && name.Contains(rootName))) && !InstallerClassifier.ExcludedProcessNames.Contains(name);
                    if (!include) continue;

                    long ticks = ProcessInfoReader.GetProcessCreationTimeUtc(entry.ProcessId)?.Ticks ?? 0;
                    session.TrackedProcesses[(entry.ProcessId, ticks)] = true;
                    session.TrackedProcessIds.TryAdd(entry.ProcessId, 0);
                    added = true;
                }
            }
            while (added);

            // 2. İzlenen süreçlerden hayatta olanlar (PID yeniden kullanımına karşı oluşturma zamanı).
            var alivePids = new HashSet<int>(snapshot.Select(e => e.ProcessId));
            var alive = new List<int>();
            foreach (var (pid, ticks) in session.TrackedProcesses.Keys)
            {
                if (!alivePids.Contains(pid) || session.DetachedProcessIds.ContainsKey(pid)) continue;
                var created = ProcessInfoReader.GetProcessCreationTimeUtc(pid);
                if (ticks == 0 || (created.HasValue && Math.Abs(created.Value.Ticks - ticks) < TimeSpan.FromSeconds(5).Ticks))
                    alive.Add(pid);
            }

            // 3. Kök kurulum çıktıysa, kurulumun sonunda başlattığı uygulama oturumu açık tutmaz (A10).
            if (alive.Count > 0 && !alive.Contains(session.RootProcessId))
                DetachLaunchedApps(session, alive, snapshot);
            bool anyAlive = alive.Any(pid => !session.DetachedProcessIds.ContainsKey(pid));

            // 4. Süreçler kapandıysa ve süren bir MSI işlemi yoksa oturumu sonlandır.
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

        /// <summary>
        /// Ağaçta kalan süreçlerden kurulumun yazdığı bir exe'den çalışanları (ve onların çocuklarını)
        /// ağaçtan ayırır. Eskiden "kurulum bitince uygulamayı başlat" seçeneği oturumu uygulama
        /// kapanana dek açık tutuyor, o sürede sistemde yazılan her şey kuruluma atfediliyordu.
        /// </summary>
        private void DetachLaunchedApps(WatchedSetupSession session, List<int> alive, IReadOnlyList<NativeProcess.ProcessEntry> snapshot)
        {
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ev in session.CapturedFileEvents)
            {
                if (ev.ChangeType != "Deleted" && ev.IsExecutable) written.Add(ev.FilePath);
            }
            if (written.Count == 0) return;

            var parentOf = new Dictionary<int, int>();
            foreach (var entry in snapshot) parentOf[entry.ProcessId] = entry.ParentProcessId;

            bool changed;
            do
            {
                changed = false;
                foreach (int pid in alive)
                {
                    if (session.DetachedProcessIds.ContainsKey(pid)) continue;
                    bool parentDetached = parentOf.TryGetValue(pid, out int parent) && session.DetachedProcessIds.ContainsKey(parent);
                    string? image = parentDetached ? null : ProcessInfoReader.GetProcessExecutablePath(pid);
                    if (!parentDetached && !SetupSessionPolicy.IsLaunchedInstalledApp(image, written, TempRoots)) continue;

                    session.DetachedProcessIds.TryAdd(pid, 0);
                    changed = true;
                    _log.Info($"Kurulan uygulama başlatıldı, izlemeden ayrıldı: {image ?? "PID " + pid} ({session.AppName}).", nameof(SetupSentinelService));
                }
            }
            while (changed);
        }

        public async Task<SetupDeltaReport?> FinalizeActiveSessionAsync()
        {
            if (!await _finalizeLock.WaitAsync(100)) return null;

            try
            {
                WatchedSetupSession? session = _activeSession;
                if (session == null || !session.IsActive) return null;

                session.EndTime = DateTime.UtcNow;
                _log.Info($"Kurulum süreçleri sonlandı. {session.AppName} için delta hesaplanıyor...", nameof(SetupSentinelService));

                // Son yazmaların diske oturması için beklenir; bu sürede gelen olaylar da kaydedilir (A6).
                // Eskiden oturum beklemeden kapatıldığı için bu olaylar atılıyordu.
                await Task.Delay(1500);
                session.IsActive = false;
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

                // Dosya farkı: geçici dosyalar, yeniden adlandırmalar, silinip yeniden yazılanlar (A5).
                var delta = await Task.Run(() => BuildFileDelta(session));
                report.CreatedFiles = delta.CreatedFiles;
                report.ModifiedFiles = delta.ModifiedFiles;
                report.DeletedFiles = delta.DeletedFiles;
                report.RenamedFiles = delta.RenamedFiles;
                report.AddedFolders = delta.CreatedFolders;
                report.AddedFiles = report.CreatedFiles; // Geriye dönük uyumluluk
                report.TempFileCount = delta.TempItemCount;
                report.AddedExecutables = report.CreatedFiles
                    .Where(f => ExecutableExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

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

                // Boyut, dosyalar diske oturduktan sonra okunur (A7): Created anında çoğu dosya 0 bayttır.
                long totalBytes = await Task.Run(() => SumFileSizes(report.CreatedFiles));
                report.TotalSizeBytes = totalBytes;
                report.FormattedSize = FormatBytes(totalBytes);

                // Nöbetçi v2: kurulum dosyası, sistem alanları ve risk kararı (NÖB 2–4).
                await EvaluateRiskAsync(session, report);

                // Uygulama adı: oturumda oluşan Uninstall kaydı en güçlü kanıttır (A9).
                if (SetupAppName.FromNewPrograms(report.NewPrograms, report.AppName, report.InstallerPath) is { } registeredName &&
                    !registeredName.Equals(report.AppName, StringComparison.OrdinalIgnoreCase))
                {
                    _log.Debug($"Kurulum adı Uninstall kaydından alındı: \"{report.AppName}\" → \"{registeredName}\".", nameof(SetupSentinelService));
                    report.AppName = registeredName;
                }

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

        public async Task<List<SetupDeltaReport>> LoadSavedReportsAsync()
        {
            return await _sessionStore.LoadAllReportsAsync().ConfigureAwait(false);
        }

        public List<SetupDeltaReport> LoadSavedReports()
        {
            try
            {
                return Task.Run(async () => await _sessionStore.LoadAllReportsAsync().ConfigureAwait(false))
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _log.Warning("LoadSavedReports senkron çağrıda hata.", ex, nameof(SetupSentinelService));
                return new List<SetupDeltaReport>();
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            double mb = bytes / (1024.0 * 1024.0);
            if (mb < 1.0) return $"{bytes / 1024.0:F1} KB";
            if (mb >= 1024.0) return $"{mb / 1024.0:F2} GB";
            return $"{mb:F1} MB";
        }

        /// <summary>
        /// Boyut toplamı. İlk <see cref="MaxSizedFiles"/> dosya tek tek okunur; fazlası ortalamayla
        /// tahmin edilir (büyük oyun kurulumlarında finalize dakikalar sürmesin).
        /// </summary>
        private static long SumFileSizes(IReadOnlyList<string> files)
        {
            long total = 0;
            int measured = 0;
            foreach (string file in files.Take(MaxSizedFiles))
            {
                try
                {
                    total += new FileInfo(file).Length;
                    measured++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    // Dosya bu arada silindi ya da okunamıyor.
                }
            }
            if (files.Count > MaxSizedFiles && measured > 0)
                total += total / measured * (files.Count - MaxSizedFiles);
            return total;
        }

        private static SetupFileDelta BuildFileDelta(WatchedSetupSession session)
        {
            var events = session.CapturedFileEvents.Select(e => new FileChangeEvent(e.Sequence, e.ChangeType switch
            {
                "Created" => FileChangeKind.Created,
                "Deleted" => FileChangeKind.Deleted,
                "Renamed" => FileChangeKind.Renamed,
                _ => FileChangeKind.Changed
            }, e.FilePath, e.OldFilePath));

            // Kurulum süreci başlamadan önce oluşmuş bir dosyanın üstüne taşınan öğe "değişen" sayılır.
            // (Silinip 15 sn içinde aynı adla yeniden oluşan dosya NTFS'te eski oluşturma zamanını alır.)
            var processStart = session.RootProcessCreationTicks > 0
                ? new DateTime(session.RootProcessCreationTicks, DateTimeKind.Utc)
                : session.StartTime;
            var threshold = processStart - TimeSpan.FromSeconds(2);

            return SetupDeltaBuilder.Build(events, ProbePath, path =>
            {
                try { return File.GetCreationTimeUtc(path) < threshold; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return true; }
            });
        }

        private static PathState ProbePath(string path)
        {
            try
            {
                if (File.Exists(path)) return PathState.File;
                if (Directory.Exists(path)) return PathState.Directory;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Erişilemeyen yol: yok sayılır.
            }
            return PathState.Missing;
        }

        private void OnProcessStartedEvent(int pid, string name, int parentPid)
        {
            var session = _activeSession;
            if (session == null || !session.IsActive) return;
            if (!session.TrackedProcessIds.ContainsKey(parentPid) || session.DetachedProcessIds.ContainsKey(parentPid)) return;
            if (!session.TrackedProcessIds.TryAdd(pid, 0)) return;

            long creationTicks = ProcessInfoReader.GetProcessCreationTimeUtc(pid)?.Ticks ?? DateTime.UtcNow.Ticks;
            session.TrackedProcesses[(pid, creationTicks)] = true;
            _log.Debug($"Çocuk süreç anında bağlandı: {name} (PID: {pid}, Ebeveyn: {parentPid})", nameof(SetupSentinelService));
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Task? worker;
            lock (_lock) worker = _workerTask;
            Stop();
            _processEvents?.Dispose();

            // Döngü (ya da süren bir finalize) bitmeden kilit atılmaz (H-22).
            bool finished = true;
            try { finished = worker?.Wait(TimeSpan.FromSeconds(2)) ?? true; }
            catch (AggregateException) { }
            if (finished) _finalizeLock.Dispose();
        }
    }
}
