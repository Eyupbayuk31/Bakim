using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private static readonly TimeSpan BaselineInterval = TimeSpan.FromSeconds(60);
        private bool _isDisposed;
        private bool _isEnabled;

        private static readonly string[] ExecutableExtensions = new[]
        {
            ".exe", ".dll", ".sys", ".bat", ".cmd", ".ps1", ".vbs", ".msi"
        };

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

        private async Task ScanNewProcessesAsync()
        {
            int currentPid = Environment.ProcessId;
            var processes = Process.GetProcesses();

            try
            {
                foreach (var proc in processes)
                {
                    try
                    {
                        if (proc.Id == currentPid || proc.Id <= 4) continue;

                        ProcessInfoReader.TryGetProcessDetails(
                            proc.Id,
                            out string? exePath,
                            out int? parentPid,
                            out DateTime? creationTime);

                        long creationTicks = creationTime?.Ticks ?? 0;
                        var key = (proc.Id, creationTicks);

                        if (_handledProcesses.ContainsKey(key)) continue;

                        string? title = null;
                        try { title = proc.MainWindowTitle; } catch { }

                        string? desc = null;
                        string? prod = null;
                        if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                        {
                            try
                            {
                                var fvi = FileVersionInfo.GetVersionInfo(exePath);
                                desc = fvi.FileDescription;
                                prod = fvi.ProductName;
                            }
                            catch { }
                        }

                        bool isTarget = InstallerClassifier.ClassifyProcess(
                            proc.ProcessName,
                            exePath,
                            title,
                            desc,
                            prod,
                            proc.ProcessName.Equals("msiexec", StringComparison.OrdinalIgnoreCase)
                                ? ProcessInfoReader.TryGetCommandLine(proc.Id)
                                : null,
                            out SessionKind kind,
                            out string detectedAppName,
                            out int confidenceScore);

                        if (isTarget)
                        {
                            _handledProcesses[key] = true;

                            if (kind == SessionKind.Uninstall)
                            {
                                _log.Info($"Kaldırma süreci saptandı (oturum açılmadı): {detectedAppName} (PID: {proc.Id})", nameof(SetupSentinelService));
                                continue;
                            }

                            if (kind == SessionKind.Install && confidenceScore >= 50)
                            {
                                await StartSessionAsync(proc.Id, creationTicks, proc.ProcessName, detectedAppName, exePath ?? proc.ProcessName);
                                break;
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            finally
            {
                // Her turda (≈2 sn) tüm süreçler için tanıtıcı açılır; serbest bırakılmazsa birikir.
                foreach (var disposable in processes) disposable.Dispose();
            }
        }

        private void RefreshBaselineIfDue()
        {
            if (IsMonitoringActiveSession || DateTime.UtcNow - _baselineTakenUtc < BaselineInterval) return;
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

        private static bool IsNoisePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            string lower = path.ToLowerInvariant();

            return lower.Contains(@"\appdata\local\google\chrome\user data\") ||
                   lower.Contains(@"\appdata\local\microsoft\edge\user data\") ||
                   lower.Contains(@"\appdata\local\mozilla\firefox\") ||
                   lower.Contains(@"\appdata\roaming\bakım\") ||
                   lower.Contains(@"\appdata\local\bakım\") ||
                   lower.Contains(@"\thumbcache_") ||
                   lower.Contains(@"\windows\prefetch\") ||
                   lower.Contains(@"\$recycle.bin\");
        }

        private async Task CheckActiveSessionProcessesAsync()
        {
            if (_activeSession == null || !_activeSession.IsActive) return;

            // 1. Yeni alt süreçleri (Child Processes) ebeveyn ağacı ile tespit et (P0-8)
            try
            {
                var processes = Process.GetProcesses();
                try
                {
                    foreach (var p in processes)
                    {
                        try
                        {
                            ProcessInfoReader.TryGetProcessDetails(p.Id, out string? path, out int? parentPid, out DateTime? creationTime);
                            long creationTicks = creationTime?.Ticks ?? 0;
                            var pKey = (p.Id, creationTicks);

                            if (!_activeSession.TrackedProcesses.ContainsKey(pKey))
                            {
                                string pName = p.ProcessName.ToLowerInvariant();

                                // Ebeveyn PID bizim ağacımızda mı?
                                bool isChildOfTracked = parentPid.HasValue &&
                                    (_activeSession.TrackedProcessIds.Contains(parentPid.Value) ||
                                     _activeSession.TrackedProcesses.Keys.Any(k => k.Pid == parentPid.Value));

                                // msiexec özel durumu (P0-8): yalnızca ebeveyn ağaçtaysa ekle (msiexec /V arka plan servisini hariç tut)
                                if (pName == "msiexec")
                                {
                                    if (isChildOfTracked)
                                    {
                                        _activeSession.TrackedProcesses[pKey] = true;
                                        _activeSession.TrackedProcessIds.Add(p.Id);
                                    }
                                }
                                else if (isChildOfTracked || pName.Contains(_activeSession.ProcessName.ToLowerInvariant()))
                                {
                                    if (!InstallerClassifier.ExcludedProcessNames.Contains(pName))
                                    {
                                        _activeSession.TrackedProcesses[pKey] = true;
                                        _activeSession.TrackedProcessIds.Add(p.Id);
                                    }
                                }
                            }
                        }
                        catch { }
                        finally { p.Dispose(); }
                    }
                }
                finally
                {
                    // Her turda (≈2 sn) tüm süreçler için tanıtıcı açılır; serbest bırakılmazsa birikir.
                    foreach (var disposable in processes) disposable.Dispose();
                }
            }
            catch { }

            // 2. Takip edilen süreçlerden hayatta olan var mı?
            bool anyAlive = false;
            foreach (var key in _activeSession.TrackedProcesses.Keys.ToList())
            {
                try
                {
                    using var p = Process.GetProcessById(key.Pid);
                    if (!p.HasExited)
                    {
                        var created = ProcessInfoReader.GetProcessCreationTimeUtc(key.Pid);
                        if (created.HasValue && Math.Abs((created.Value.Ticks - key.CreationTimeTicks)) < TimeSpan.FromSeconds(5).Ticks)
                        {
                            anyAlive = true;
                            break;
                        }
                    }
                }
                catch
                {
                    // Süreç kapandı
                }
            }

            // 3. Eğer süreçler kapandıysa ve aktif bir MSI işlemi yoksa oturumu sonlandır
            if (!anyAlive)
            {
                var (hasMsi, isCompleted, _) = MsiEventLogWatcher.QueryLatestMsiStatus(_activeSession.StartTime);
                if (hasMsi && !isCompleted)
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

                // Hızlı Risk Özeti (Faz 0 Hızlı Kazanım)
                GenerateQuickRiskSummary(report);

                // Raporu Tekil SessionStore'a Kaydet (P0-6)
                await _sessionStore.SaveReportAsync(report);

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
                _baselineTakenUtc = DateTime.MinValue; // kurulum bitti: taban hemen yenilensin
                _finalizeLock.Release();
            }
        }

        private static void GenerateQuickRiskSummary(SetupDeltaReport report)
        {
            int exeCount = report.AddedExecutables.Count;
            int startupCount = report.AddedStartupEntries.Count;
            int svcCount = report.AddedServices.Count;

            if (startupCount > 0 || svcCount > 0)
            {
                var parts = new List<string>();
                if (exeCount > 0) parts.Add($"{exeCount} yürütülebilir");
                if (startupCount > 0) parts.Add($"{startupCount} başlangıç kaydı");
                if (svcCount > 0) parts.Add($"{svcCount} yeni servis");

                report.QuickRiskSummary = string.Join(" • ", parts);
                report.RiskBadgeBrush = "SystemFillColorCautionBrush";
            }
            else if (exeCount > 0)
            {
                report.QuickRiskSummary = $"{exeCount} yürütülebilir dosya • Başlangıç kaydı saptanmadı";
                report.RiskBadgeBrush = "AccentTextFillColorPrimaryBrush";
            }
            else
            {
                report.QuickRiskSummary = "Temiz kurulum • Yürütülebilir tehdit saptanmadı";
                report.RiskBadgeBrush = "AccentTextFillColorPrimaryBrush";
            }
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

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();
            _finalizeLock.Dispose();
        }
    }
}
