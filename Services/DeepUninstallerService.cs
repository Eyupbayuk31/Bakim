using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public class BatchUninstallResult
    {
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public long TotalCleanedBytes { get; set; }
        public string FormattedCleanedSize { get; set; } = "0 MB";
    }

    public interface IDeepUninstallerService
    {
        Task<List<InstalledAppItem>> GetInstalledAppsAsync();
        Task<bool> LaunchUninstallAsync(InstalledAppItem app, bool silent = false);
        Task<bool> CreateRestorePointAsync(string appName);
        Task<BatchUninstallResult> ExecuteBatchSilentUninstallAsync(
            IEnumerable<InstalledAppItem> apps,
            bool autoClean,
            IProgress<BatchUninstallProgress>? progress = null);
        Task<int> ExecuteForceUninstallAsync(InstalledAppItem app, IProgress<string>? progress = null);
        Task<long> ExecuteAutoCleanResidualsAsync(InstalledAppItem app, IProgress<string>? progress = null);
        void OpenInstallLocation(InstalledAppItem app);
    }

    public class DeepUninstallerService : IDeepUninstallerService
    {
        private readonly IResidualScannerEngine _residualScanner;
        private readonly IUninstallerService _legacyUninstaller;

        public DeepUninstallerService(IResidualScannerEngine? residualScanner = null, IUninstallerService? legacyUninstaller = null)
        {
            _residualScanner = residualScanner ?? new ResidualScannerEngine();
            _legacyUninstaller = legacyUninstaller ?? new UninstallerService();
        }

        #region App Discovery

        public async Task<List<InstalledAppItem>> GetInstalledAppsAsync()
        {
            var apps = await _legacyUninstaller.GetInstalledAppsAsync();

            // Detect installer types for each app
            foreach (var app in apps)
            {
                app.InstallerKind = DetectInstallerKind(app);
            }

            return apps;
        }

        private static InstallerType DetectInstallerKind(InstalledAppItem app)
        {
            string cmd = (!string.IsNullOrWhiteSpace(app.UninstallString) ? app.UninstallString : app.QuietUninstallString).ToLowerInvariant();

            if (cmd.Contains("msiexec")) return InstallerType.Msi;
            if (cmd.Contains("unins000") || cmd.Contains("inno")) return InstallerType.InnoSetup;
            if (cmd.Contains("uninstall.exe") || cmd.Contains("uninst.exe") || cmd.Contains("nsis")) return InstallerType.Nsis;
            if (cmd.Contains("installshield") || cmd.Contains("setup.exe -uninst")) return InstallerType.InstallShield;

            return InstallerType.GenericExe;
        }

        #endregion

        #region Restore Point

        public async Task<bool> CreateRestorePointAsync(string appName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string safeName = Regex.Replace(appName, @"[^a-zA-Z0-9_\-]", "_");
                    if (safeName.Length > 40) safeName = safeName.Substring(0, 40);

                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Checkpoint-Computer -Description 'Bakim_Uninstaller_{safeName}' -RestorePointType 'APPLICATION_UNINSTALL'\"",
                        CreateNoWindow = true,
                        UseShellExecute = true,
                        Verb = "runas"
                    };

                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(15000);
                    return proc?.ExitCode == 0;
                }
                catch
                {
                    // If System Restore is disabled or permissions denied, fail gracefully
                    return false;
                }
            });
        }

        #endregion

        #region Launch Uninstall (Standard / Silent)

        public async Task<bool> LaunchUninstallAsync(InstalledAppItem app, bool silent = false)
        {
            return await Task.Run(async () =>
            {
                string rawCommand = silent && !string.IsNullOrWhiteSpace(app.QuietUninstallString)
                    ? app.QuietUninstallString
                    : app.UninstallString;

                if (string.IsNullOrWhiteSpace(rawCommand))
                {
                    rawCommand = app.QuietUninstallString;
                }

                if (string.IsNullOrWhiteSpace(rawCommand)) return false;

                try
                {
                    var (fileName, arguments) = ParseCommandAndArguments(rawCommand);

                    // If silent mode is requested, augment arguments based on installer type
                    if (silent)
                    {
                        arguments = BuildSilentArguments(fileName, arguments, app.InstallerKind);
                    }
                    else if (fileName.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
                    {
                        // Standard MSI uninstall requires /X
                        arguments = Regex.Replace(arguments, @"/I(?=\s|\b|$)", "/X", RegexOptions.IgnoreCase);
                    }

                    var psi = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        await proc.WaitForExitAsync();
                        return true;
                    }
                }
                catch { }

                return false;
            });
        }

        private static (string fileName, string arguments) ParseCommandAndArguments(string command)
        {
            string cmd = command.Trim();
            string fileName;
            string arguments = string.Empty;

            if (cmd.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
            {
                fileName = "msiexec.exe";
                int idx = cmd.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase);
                arguments = cmd.Substring(idx + 7).Trim();
                if (arguments.StartsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    arguments = arguments.Substring(4).Trim();
                }
            }
            else if (cmd.StartsWith("\""))
            {
                int closingQuote = cmd.IndexOf('\"', 1);
                if (closingQuote > 0)
                {
                    fileName = cmd.Substring(1, closingQuote - 1);
                    arguments = cmd.Substring(closingQuote + 1).Trim();
                }
                else
                {
                    fileName = cmd.Trim('\"');
                }
            }
            else
            {
                int spaceIdx = cmd.IndexOf(' ');
                if (spaceIdx > 0)
                {
                    fileName = cmd.Substring(0, spaceIdx);
                    arguments = cmd.Substring(spaceIdx + 1).Trim();
                }
                else
                {
                    fileName = cmd;
                }
            }

            return (fileName, arguments);
        }

        private static string BuildSilentArguments(string fileName, string currentArgs, InstallerType kind)
        {
            switch (kind)
            {
                case InstallerType.Msi:
                    string msiArgs = Regex.Replace(currentArgs, @"/I(?=\s|\b|$)", "/X", RegexOptions.IgnoreCase);
                    if (!msiArgs.Contains("/x", StringComparison.OrdinalIgnoreCase)) msiArgs = "/X " + msiArgs;
                    if (!msiArgs.Contains("/qn", StringComparison.OrdinalIgnoreCase)) msiArgs += " /qn";
                    if (!msiArgs.Contains("/norestart", StringComparison.OrdinalIgnoreCase)) msiArgs += " /norestart";
                    return msiArgs;

                case InstallerType.InnoSetup:
                    string inno = currentArgs;
                    if (!inno.Contains("/VERYSILENT", StringComparison.OrdinalIgnoreCase)) inno += " /VERYSILENT";
                    if (!inno.Contains("/SUPPRESSMSGBOXES", StringComparison.OrdinalIgnoreCase)) inno += " /SUPPRESSMSGBOXES";
                    if (!inno.Contains("/NORESTART", StringComparison.OrdinalIgnoreCase)) inno += " /NORESTART";
                    return inno.Trim();

                case InstallerType.Nsis:
                    string nsis = currentArgs;
                    if (!nsis.Contains("/S", StringComparison.Ordinal)) nsis += " /S";
                    return nsis.Trim();

                case InstallerType.InstallShield:
                    string iss = currentArgs;
                    if (!iss.Contains("-s", StringComparison.OrdinalIgnoreCase)) iss += " -s";
                    return iss.Trim();

                default:
                    return currentArgs;
            }
        }

        #endregion

        #region Batch Silent Uninstallation

        public async Task<BatchUninstallResult> ExecuteBatchSilentUninstallAsync(
            IEnumerable<InstalledAppItem> apps,
            bool autoClean,
            IProgress<BatchUninstallProgress>? progress = null)
        {
            var appList = apps.ToList();
            var result = new BatchUninstallResult();

            for (int i = 0; i < appList.Count; i++)
            {
                var app = appList[i];
                progress?.Report(new BatchUninstallProgress
                {
                    CurrentIndex = i + 1,
                    TotalCount = appList.Count,
                    CurrentAppName = app.DisplayName,
                    TotalCleanedBytes = result.TotalCleanedBytes,
                    IsCompleted = false
                });

                // 1. Restore Point
                await CreateRestorePointAsync(app.DisplayName);

                // 2. Launch Silent Uninstall
                bool success = await LaunchUninstallAsync(app, silent: true);
                if (success)
                {
                    result.SuccessCount++;

                    // 3. Auto Clean Leftovers
                    if (autoClean)
                    {
                        long cleaned = await ExecuteAutoCleanResidualsAsync(app);
                        result.TotalCleanedBytes += cleaned;
                    }
                }
                else
                {
                    result.FailedCount++;
                }
            }

            result.FormattedCleanedSize = FormatBytes(result.TotalCleanedBytes);

            progress?.Report(new BatchUninstallProgress
            {
                CurrentIndex = appList.Count,
                TotalCount = appList.Count,
                CurrentAppName = "Tamamlandı",
                TotalCleanedBytes = result.TotalCleanedBytes,
                IsCompleted = true
            });

            return result;
        }

        #endregion

        #region Auto-Clean & Force Uninstall

        public async Task<long> ExecuteAutoCleanResidualsAsync(InstalledAppItem app, IProgress<string>? progress = null)
        {
            progress?.Report($"{app.DisplayName} için otomatik kalıntı taraması yapılıyor...");
            var leftovers = await _residualScanner.ScanResidualsAsync(app);

            // Clean items with 100% confidence automatically
            var safeLeftovers = leftovers.Where(l => l.ConfidenceScore >= 100).ToList();
            if (safeLeftovers.Count == 0) return 0;

            long totalBytes = safeLeftovers.Sum(l => l.SizeBytes);
            await _residualScanner.CleanResidualsAsync(safeLeftovers, progress);

            return totalBytes;
        }

        public async Task<int> ExecuteForceUninstallAsync(InstalledAppItem app, IProgress<string>? progress = null)
        {
            return await Task.Run(async () =>
            {
                progress?.Report($"{app.DisplayName} ilişkili süreçler sonlandırılıyor...");

                // 1. Kill running processes related to this app
                KillProcessesForApp(app);

                // 2. Scan heuristic leftovers
                progress?.Report("Gelişmiş sezgisel kalıntı taraması yapılıyor...");
                var targetPath = !string.IsNullOrWhiteSpace(app.InstallLocation) ? app.InstallLocation : app.DisplayIconPath;
                var leftovers = await _residualScanner.ScanHeuristicResidualsAsync(targetPath, app.DisplayName);

                // 3. Clean leftovers
                progress?.Report("Kalıntılar zorla sökülüyor...");
                int cleaned = await _residualScanner.CleanResidualsAsync(leftovers);

                // 4. Clean uninstall registry entry if still present
                if (!string.IsNullOrWhiteSpace(app.RegistryKeyPath))
                {
                    DeleteRegistryKey(app.RegistryKeyPath);
                    cleaned++;
                }

                return cleaned;
            });
        }

        private static void KillProcessesForApp(InstalledAppItem app)
        {
            try
            {
                var processes = Process.GetProcesses();
                foreach (var p in processes)
                {
                    try
                    {
                        string path = p.MainModule?.FileName ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(app.InstallLocation) &&
                            path.StartsWith(app.InstallLocation.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                        {
                            p.Kill();
                            p.WaitForExit(3000);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static bool DeleteRegistryKey(string fullPath)
        {
            try
            {
                int slashIndex = fullPath.IndexOf('\\');
                if (slashIndex <= 0) return false;

                string hiveStr = fullPath.Substring(0, slashIndex);
                string subPath = fullPath.Substring(slashIndex + 1);

                RegistryHive hive = hiveStr.Contains("Current", StringComparison.OrdinalIgnoreCase)
                    ? RegistryHive.CurrentUser
                    : RegistryHive.LocalMachine;

                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                baseKey.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void OpenInstallLocation(InstalledAppItem app)
        {
            _legacyUninstaller.OpenInstallLocation(app);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        #endregion
    }
}
