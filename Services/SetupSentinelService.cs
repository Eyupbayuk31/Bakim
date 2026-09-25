using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Bakım.Models;

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

        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly List<SetupDeltaReport> _recentReports = new();
        private readonly HashSet<int> _alreadyHandledProcessIds = new();
        private readonly object _lock = new();

        private Timer? _pollingTimer;
        private WatchedSetupSession? _activeSession;
        private bool _isDisposed;
        private bool _isEnabled;

        private static readonly string StorageDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Bakım", "InstallationLogs");

        private static readonly string[] ExecutableExtensions = new[]
        {
            ".exe", ".dll", ".sys", ".bat", ".cmd", ".ps1", ".vbs", ".msi"
        };

        private static readonly string[] InstallerKeywords = new[]
        {
            "setup", "install", "installer", "kurulum", "kurucu", "msiexec", "unins", "vcredist", "dxsetup"
        };

        private static readonly HashSet<string> ExcludedProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "idle", "system", "explorer", "svchost", "taskmgr", "devenv", "code",
            "jusched", "jucheck", "javaupdate", "googleupdate", "microsoftedgeupdate",
            "onedrive", "onedrivestandaloneupdater", "discord", "spotify", "steam",
            "epicgameslauncher", "riotclientservices", "bakim"
        };

        public SetupSentinelService(
            IInstallerMonitorService installerMonitorService,
            IAppSettingsService settingsService,
            ILogService log)
        {
            _installerMonitorService = installerMonitorService;
            _settingsService = settingsService;
            _log = log;

            _isEnabled = _settingsService.Current.IsSentinelSetupGuardEnabled;

            try
            {
                if (!Directory.Exists(StorageDirectory))
                {
                    Directory.CreateDirectory(StorageDirectory);
                }
            }
            catch { }

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
                _pollingTimer?.Dispose();
                _pollingTimer = new Timer(OnPollingTick, null, 1000, 1500);
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _pollingTimer?.Dispose();
                _pollingTimer = null;
                StopFileSystemWatchers();
            }
        }

        private async void OnPollingTick(object? state)
        {
            if (!_isEnabled || _isDisposed) return;

            try
            {
                if (IsMonitoringActiveSession)
                {
                    CheckActiveSessionProcesses();
                    return;
                }

                // Scan running processes for newly launched installers
                var processes = Process.GetProcesses();
                int currentPid = Environment.ProcessId;

                foreach (var proc in processes)
                {
                    try
                    {
                        if (proc.Id == currentPid || proc.Id <= 4) continue;
                        if (_alreadyHandledProcessIds.Contains(proc.Id)) continue;

                        if (IsInstallerProcess(proc, out string detectedAppName, out string executablePath))
                        {
                            _alreadyHandledProcessIds.Add(proc.Id);
                            await StartSessionAsync(proc.Id, proc.ProcessName, detectedAppName, executablePath);
                            break;
                        }
                    }
                    catch { }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error("Kurulum nöbetçi taramasında hata.", ex, nameof(SetupSentinelService));
            }
        }

        private bool IsInstallerProcess(Process proc, out string appName, out string path)
        {
            appName = string.Empty;
            path = string.Empty;

            try
            {
                string procName = proc.ProcessName.ToLowerInvariant();

                // 1. Skip system & known background / updater daemon processes
                if (ExcludedProcessNames.Contains(procName) || procName.StartsWith("service", StringComparison.OrdinalIgnoreCase))
                    return false;

                // 2. Try inspect main module or executable path
                string? exePath = null;
                try { exePath = proc.MainModule?.FileName; } catch { }

                if (string.IsNullOrWhiteSpace(exePath))
                {
                    if (procName is "msiexec" or "setup" or "installer" or "kurulum")
                    {
                        appName = proc.MainWindowTitle.Length > 2 ? proc.MainWindowTitle : proc.ProcessName;
                        path = proc.ProcessName;
                        return true;
                    }
                    return false;
                }

                path = exePath;

                // 3. Reject binaries located in Program Files, Program Files (x86), or Windows directories
                // Software running from these directories is ALREADY installed software (unless it is msiexec.exe)
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

                bool isAlreadyInstalledDir =
                    (!string.IsNullOrEmpty(programFiles) && exePath.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(programFilesX86) && exePath.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(windowsDir) && exePath.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase));

                if (isAlreadyInstalledDir && !string.Equals(procName, "msiexec", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // 4. Check file name, proc name, title, and directory
                string fileName = Path.GetFileName(exePath).ToLowerInvariant();
                string dir = Path.GetDirectoryName(exePath)?.ToLowerInvariant() ?? string.Empty;

                bool nameMatch = InstallerKeywords.Any(k => fileName.Contains(k));
                bool procMatch = InstallerKeywords.Any(k => procName.Contains(k));
                bool tempOrDownloads = dir.Contains("temp") || dir.Contains("downloads") || dir.Contains("indirilenler") || dir.Contains("desktop") || dir.Contains("masaüstü");

                string? title = null;
                try { title = proc.MainWindowTitle; } catch { }

                bool titleMatch = !string.IsNullOrWhiteSpace(title) &&
                                  InstallerKeywords.Any(k => title.ToLowerInvariant().Contains(k));

                // Inspect FileVersionInfo
                bool fviMatch = false;
                string? prodName = null;
                try
                {
                    var fvi = proc.MainModule?.FileVersionInfo;
                    if (fvi != null)
                    {
                        prodName = fvi.ProductName;
                        string desc = (fvi.FileDescription ?? string.Empty).ToLowerInvariant();
                        string prod = (fvi.ProductName ?? string.Empty).ToLowerInvariant();
                        if (InstallerKeywords.Any(k => desc.Contains(k) || prod.Contains(k)))
                        {
                            fviMatch = true;
                        }
                    }
                }
                catch { }

                if (nameMatch || procMatch || titleMatch || fviMatch || (procMatch && tempOrDownloads))
                {
                    appName = !string.IsNullOrWhiteSpace(prodName) && prodName.Length > 2
                        ? prodName
                        : (!string.IsNullOrWhiteSpace(title) && title.Length > 2 ? title : Path.GetFileNameWithoutExtension(exePath));

                    return true;
                }
            }
            catch { }

            return false;
        }

        private async Task StartSessionAsync(int rootPid, string procName, string appName, string exePath)
        {
            var session = new WatchedSetupSession
            {
                RootProcessId = rootPid,
                ProcessName = procName,
                AppName = appName,
                InstallerPath = exePath,
                StartTime = DateTime.UtcNow,
                IsActive = true
            };

            session.TrackedProcessIds.Add(rootPid);
            _activeSession = session;

            _log.Info($"Kurulum tespit edildi: {appName} (PID: {rootPid}). Değişiklikler izleniyor...", nameof(SetupSentinelService));

            // Start File System Watchers
            StartFileSystemWatchers(session);

            // Pre-install snapshot
            try
            {
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
                        EnableRaisingEvents = true
                    };

                    fsw.Created += (s, e) => RecordFileEvent(session, e.FullPath, "Created");
                    fsw.Changed += (s, e) => RecordFileEvent(session, e.FullPath, "Changed");
                    fsw.Renamed += (s, e) => RecordFileEvent(session, e.FullPath, "Renamed");

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

        private void RecordFileEvent(WatchedSetupSession session, string fullPath, string changeType)
        {
            if (!session.IsActive) return;

            try
            {
                string ext = Path.GetExtension(fullPath).ToLowerInvariant();
                bool isExe = ExecutableExtensions.Contains(ext);

                long size = 0;
                if (File.Exists(fullPath))
                {
                    try { size = new FileInfo(fullPath).Length; } catch { }
                }

                session.CapturedFileEvents.Add(new SetupFileEvent
                {
                    FilePath = fullPath,
                    ChangeType = changeType,
                    SizeBytes = size,
                    Timestamp = DateTime.UtcNow,
                    IsExecutable = isExe,
                    Extension = ext
                });
            }
            catch { }
        }

        private void CheckActiveSessionProcesses()
        {
            if (_activeSession == null || !_activeSession.IsActive) return;

            // Discover any child processes spawned
            try
            {
                var processes = Process.GetProcesses();
                foreach (var p in processes)
                {
                    try
                    {
                        if (!_activeSession.TrackedProcessIds.Contains(p.Id))
                        {
                            string pName = p.ProcessName.ToLowerInvariant();
                            if (!ExcludedProcessNames.Contains(pName) &&
                                (InstallerKeywords.Any(k => pName.Contains(k)) ||
                                 pName.Contains(_activeSession.ProcessName.ToLowerInvariant())))
                            {
                                _activeSession.TrackedProcessIds.Add(p.Id);
                            }
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }

            // Check if any tracked process is still alive
            bool anyAlive = false;
            foreach (var pid in _activeSession.TrackedProcessIds)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    if (!p.HasExited)
                    {
                        anyAlive = true;
                        break;
                    }
                }
                catch
                {
                    // Process already exited
                }
            }

            if (!anyAlive)
            {
                _ = FinalizeActiveSessionAsync();
            }
        }

        public async Task<SetupDeltaReport?> FinalizeActiveSessionAsync()
        {
            WatchedSetupSession? session = _activeSession;
            if (session == null || !session.IsActive) return null;

            session.IsActive = false;
            session.EndTime = DateTime.UtcNow;

            _log.Info($"Kurulum süreçleri sonlandı. {session.AppName} için delta hesaplanıyor...", nameof(SetupSentinelService));

            // Wait 2 seconds for file system writes to settle
            await Task.Delay(2000);
            StopFileSystemWatchers();

            var report = new SetupDeltaReport
            {
                SessionId = session.SessionId,
                AppName = session.AppName,
                InstallerPath = session.InstallerPath,
                InstallTime = session.StartTime,
                Duration = (session.EndTime ?? DateTime.UtcNow) - session.StartTime
            };

            try
            {
                // Process captured file system events
                var uniqueFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var uniqueFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long totalBytes = 0;

                foreach (var ev in session.CapturedFileEvents)
                {
                    if (File.Exists(ev.FilePath))
                    {
                        if (uniqueFiles.Add(ev.FilePath))
                        {
                            report.AddedFiles.Add(ev.FilePath);
                            if (ev.IsExecutable)
                            {
                                report.AddedExecutables.Add(ev.FilePath);
                            }
                            totalBytes += ev.SizeBytes;
                        }
                    }
                    else if (Directory.Exists(ev.FilePath))
                    {
                        if (uniqueFolders.Add(ev.FilePath))
                        {
                            report.AddedFolders.Add(ev.FilePath);
                        }
                    }
                }

                // If pre-snapshot was captured, compute registry delta
                if (session.PreSnapshot != null)
                {
                    try
                    {
                        var delta = await _installerMonitorService.TakePostInstallSnapshotAndSaveDeltaAsync(session.AppName, session.PreSnapshot);

                        foreach (var f in delta.AddedFiles)
                        {
                            if (uniqueFiles.Add(f))
                            {
                                report.AddedFiles.Add(f);
                                string ext = Path.GetExtension(f).ToLowerInvariant();
                                if (ExecutableExtensions.Contains(ext))
                                {
                                    report.AddedExecutables.Add(f);
                                }
                            }
                        }

                        foreach (var fol in delta.AddedFolders)
                        {
                            if (uniqueFolders.Add(fol))
                            {
                                report.AddedFolders.Add(fol);
                            }
                        }

                        foreach (var reg in delta.AddedRegistryKeys)
                        {
                            bool isAuto = reg.Contains(@"\Run", StringComparison.OrdinalIgnoreCase) ||
                                          reg.Contains(@"\Services", StringComparison.OrdinalIgnoreCase);

                            report.AddedRegistryRecords.Add(new SetupRegistryRecord
                            {
                                Hive = reg.StartsWith("HKEY_LOCAL_MACHINE") || reg.StartsWith("HKLM") ? "HKLM" : "HKCU",
                                KeyPath = reg,
                                IsAutorunOrService = isAuto
                            });

                            if (reg.Contains(@"\Services\", StringComparison.OrdinalIgnoreCase))
                            {
                                report.AddedServices.Add(reg);
                            }
                            else if (reg.Contains(@"\Run", StringComparison.OrdinalIgnoreCase))
                            {
                                report.AddedStartupEntries.Add(reg);
                            }
                        }

                        if (delta.TotalSizeBytes > totalBytes)
                        {
                            totalBytes = delta.TotalSizeBytes;
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Error("Registry delta hesaplamasında hata.", ex, nameof(SetupSentinelService));
                    }
                }

                report.TotalSizeBytes = totalBytes;
                report.FormattedSize = FormatBytes(totalBytes);

                // Save report profile
                await SaveReportProfileAsync(report);

                lock (_lock)
                {
                    _recentReports.Insert(0, report);
                    if (_recentReports.Count > 20)
                    {
                        _recentReports.RemoveAt(_recentReports.Count - 1);
                    }
                }

                _log.Info($"Kurulum tamamlandı: {report.AppName} - +{report.AddedFiles.Count} dosya ({report.FormattedSize}), +{report.AddedRegistryRecords.Count} registry anahtarı.", nameof(SetupSentinelService));

                SetupFinished?.Invoke(report);
            }
            catch (Exception ex)
            {
                _log.Error("Kurulum raporu oluşturulamadı.", ex, nameof(SetupSentinelService));
            }
            finally
            {
                _activeSession = null;
            }

            return report;
        }

        public async Task<bool> SaveReportProfileAsync(SetupDeltaReport report)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string safeName = SanitizeFileName(report.AppName);
                    string filePath = Path.Combine(StorageDirectory, $"{safeName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(report, options);
                    File.WriteAllText(filePath, json);
                    report.IsProfileSaved = true;
                    return true;
                }
                catch (Exception ex)
                {
                    _log.Error("Kurulum profili diske kaydedilemedi.", ex, nameof(SetupSentinelService));
                    return false;
                }
            });
        }

        public List<SetupDeltaReport> LoadSavedReports()
        {
            var reports = new List<SetupDeltaReport>();
            try
            {
                if (!Directory.Exists(StorageDirectory)) return reports;

                foreach (var file in Directory.GetFiles(StorageDirectory, "*.json").OrderByDescending(File.GetCreationTimeUtc))
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var report = JsonSerializer.Deserialize<SetupDeltaReport>(json);
                        if (report != null)
                        {
                            reports.Add(report);
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return reports;
        }

        public async Task<int> RevertReportAsync(SetupDeltaReport report)
        {
            return await Task.Run(() =>
            {
                int deletedCount = 0;

                // 1. Delete added files
                foreach (var file in report.AddedFiles)
                {
                    try
                    {
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                            deletedCount++;
                        }
                    }
                    catch { }
                }

                // 2. Delete added empty folders
                foreach (var folder in report.AddedFolders.OrderByDescending(f => f.Length))
                {
                    try
                    {
                        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                        {
                            Directory.Delete(folder);
                            deletedCount++;
                        }
                    }
                    catch { }
                }

                return deletedCount;
            });
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();
        }
    }
}
