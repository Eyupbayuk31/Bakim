using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Bakım.Core.Safety;
using Bakım.Core.Uninstall;
using Bakım.Models;
using Bakım.Services.Safety;
using Bakım.Services.Uninstall;

namespace Bakım.Services
{
    public class BatchUninstallResult
    {
        public int SuccessCount => Removed.Count;
        public int FailedCount => Failed.Count;
        public int SkippedCount => Skipped.Count;
        public long TotalCleanedBytes { get; set; }
        public string FormattedCleanedSize { get; set; } = "0 B";

        /// <summary>Gerçekten kaldırıldığı doğrulanan programlar.</summary>
        public List<InstalledAppItem> Removed { get; } = new();

        /// <summary>Kaldırıcı çalıştı ama program hâlâ kurulu, ya da başlatılamadı.</summary>
        public List<(InstalledAppItem App, string Reason)> Failed { get; } = new();

        /// <summary>Sessiz kaldırma desteklenmediği için atlananlar.</summary>
        public List<(InstalledAppItem App, string Reason)> Skipped { get; } = new();

        public string? RestorePointMessage { get; set; }
    }

    public interface IDeepUninstallerService
    {
        Task<List<InstalledAppItem>> GetInstalledAppsAsync();

        /// <summary>Resmi kaldırıcıyı çalıştırır, bitmesini bekler ve sonucu doğrular.</summary>
        Task<UninstallRunResult> RunUninstallAsync(InstalledAppItem app, bool silent, IProgress<string>? progress = null, CancellationToken ct = default);

        bool IsStillInstalled(InstalledAppItem app);
        bool SupportsSilentUninstall(InstalledAppItem app);

        /// <summary>Uyumluluk: kaldırma doğrulandıysa true.</summary>
        Task<bool> LaunchUninstallAsync(InstalledAppItem app, bool silent = false);

        /// <summary>Uyumluluk: geri yükleme noktası gerçekten oluşturulduysa true.</summary>
        Task<bool> CreateRestorePointAsync(string appName);
        Task<RestorePointResult> CreateRestorePointDetailedAsync(string description);

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
        private readonly IUninstallerService _installedApps;
        private readonly ISafeProcessService _safeProcess;
        private readonly ISafeRegistryService _safeRegistry;
        private readonly IRestorePointService _restorePoints;
        private readonly ILogService _log;
        private readonly UninstallRunner _runner;

        public DeepUninstallerService(
            IResidualScannerEngine residualScanner,
            IUninstallerService installedApps,
            ISafeProcessService safeProcess,
            ISafeRegistryService safeRegistry,
            IRestorePointService restorePoints,
            ILogService log)
        {
            _residualScanner = residualScanner;
            _installedApps = installedApps;
            _safeProcess = safeProcess;
            _safeRegistry = safeRegistry;
            _restorePoints = restorePoints;
            _log = log;
            _runner = new UninstallRunner(log);
        }

        #region Listeleme

        public async Task<List<InstalledAppItem>> GetInstalledAppsAsync()
        {
            var apps = await _installedApps.GetInstalledAppsAsync();
            foreach (var app in apps)
            {
                app.InstallerKind = MapFamily(UninstallRunner.DetectFamily(app));
            }
            return apps;
        }

        private static InstallerType MapFamily(InstallerFamily family) => family switch
        {
            InstallerFamily.Msi => InstallerType.Msi,
            InstallerFamily.InnoSetup => InstallerType.InnoSetup,
            InstallerFamily.Nsis => InstallerType.Nsis,
            InstallerFamily.InstallShield => InstallerType.InstallShield,
            InstallerFamily.WixBurn => InstallerType.WixBurn,
            InstallerFamily.Squirrel => InstallerType.Squirrel,
            InstallerFamily.Steam => InstallerType.Steam,
            _ => InstallerType.GenericExe
        };

        #endregion

        #region Kaldırma

        public Task<UninstallRunResult> RunUninstallAsync(InstalledAppItem app, bool silent, IProgress<string>? progress = null, CancellationToken ct = default) =>
            _runner.RunAsync(app, silent, progress, ct);

        public bool IsStillInstalled(InstalledAppItem app) => UninstallRunner.IsStillInstalled(app);

        public bool SupportsSilentUninstall(InstalledAppItem app) => UninstallRunner.SupportsSilent(app);

        public async Task<bool> LaunchUninstallAsync(InstalledAppItem app, bool silent = false)
        {
            var result = await RunUninstallAsync(app, silent);
            return result.IsRemoved;
        }

        public async Task<bool> CreateRestorePointAsync(string appName)
        {
            var result = await CreateRestorePointDetailedAsync(appName);
            return result.Created;
        }

        public Task<RestorePointResult> CreateRestorePointDetailedAsync(string description) =>
            _restorePoints.CreateAsync(description, RestorePointKind.ApplicationUninstall);

        #endregion

        #region Toplu sessiz kaldırma

        public async Task<BatchUninstallResult> ExecuteBatchSilentUninstallAsync(
            IEnumerable<InstalledAppItem> apps,
            bool autoClean,
            IProgress<BatchUninstallProgress>? progress = null)
        {
            var appList = apps.ToList();
            var result = new BatchUninstallResult();

            // Eskiden HER uygulama için ayrı nokta açılıyordu (her biri UAC + 15 sn; Windows
            // 24 saatte bir noktaya izin verdiği için ilkinden sonrası sessizce başarısızdı).
            var rp = await CreateRestorePointDetailedAsync($"Toplu kaldırma ({appList.Count} program)");
            result.RestorePointMessage = rp.Message;

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

                if (!SupportsSilentUninstall(app))
                {
                    result.Skipped.Add((app, "Sessiz kaldırma desteklenmiyor; tek tek kaldırın."));
                    continue;
                }

                var run = await RunUninstallAsync(app, silent: true);
                if (run.IsRemoved)
                {
                    result.Removed.Add(app);
                    if (autoClean)
                    {
                        result.TotalCleanedBytes += await ExecuteAutoCleanResidualsAsync(app);
                    }
                }
                else if (run.Outcome == UninstallOutcome.NotSupported)
                {
                    result.Skipped.Add((app, run.Detail));
                }
                else
                {
                    result.Failed.Add((app, run.Detail));
                }
            }

            result.FormattedCleanedSize = Bakım.Core.Text.ByteFormatter.Format(result.TotalCleanedBytes);

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

        #region Otomatik temizlik ve zorla kaldırma

        /// <summary>
        /// Onaysız otomatik temizlik YALNIZCA kesin kanıtlı öğelere uygulanır: kaldırma
        /// doğrulandıktan sonra hâlâ duran kurulum klasörü ve yetim Uninstall kaydı.
        /// İsim benzerliğinden gelen hiçbir öğe onaysız silinmez.
        /// </summary>
        public async Task<long> ExecuteAutoCleanResidualsAsync(InstalledAppItem app, IProgress<string>? progress = null)
        {
            if (IsStillInstalled(app))
            {
                _log.Warning($"Otomatik temizlik atlandı: {app.DisplayName} hâlâ kurulu görünüyor.", null, nameof(DeepUninstallerService));
                return 0;
            }

            progress?.Report($"{app.DisplayName} için kesin kalıntılar taranıyor...");
            var leftovers = await _residualScanner.ScanResidualsAsync(app, ResidualScanOptions.Confirmed);
            var certain = leftovers.Where(l => l.ConfidenceScore >= (int)Core.Text.MatchConfidence.Certain).ToList();
            if (certain.Count == 0) return 0;

            var report = await _residualScanner.CleanResidualsDetailedAsync(certain, $"Otomatik temizlik: {app.DisplayName}", progress);
            return report.BytesFreed;
        }

        /// <summary>
        /// Zorla kaldırma: resmi kaldırıcı atlanır. Yalnızca güvenli kapsamdaki süreçler
        /// kapatılır ve yalnızca yüksek/kesin güvenli kalıntılar silinir; isim tabanlı
        /// zayıf eşleşmeler kullanıcı incelemesine bırakılır.
        /// </summary>
        public async Task<int> ExecuteForceUninstallAsync(InstalledAppItem app, IProgress<string>? progress = null)
        {
            progress?.Report($"{app.DisplayName} ile ilişkili süreçler kontrol ediliyor...");
            string? folder = !string.IsNullOrWhiteSpace(app.InstallLocation) ? app.InstallLocation : null;
            if (folder != null)
            {
                var processes = _safeProcess.FindProcessesUnder(folder, out string? refusal);
                if (refusal != null)
                    _log.Info($"Zorla kaldırma: süreç kapatma atlandı ({refusal})", nameof(DeepUninstallerService));
                else if (processes.Count > 0)
                    await _safeProcess.TerminateAsync(processes);
            }

            progress?.Report("Kalıntılar taranıyor...");
            string target = folder ?? CleanIconPath(app.DisplayIconPath);
            var leftovers = await _residualScanner.ScanHeuristicResidualsAsync(target, app.DisplayName);
            var safe = leftovers.Where(l => l.ConfidenceScore >= (int)Core.Text.MatchConfidence.High).ToList();

            progress?.Report("Kalıntılar kaldırılıyor...");
            var report = await _residualScanner.CleanResidualsDetailedAsync(safe, $"Zorla kaldırma: {app.DisplayName}", progress);
            int cleaned = report.SucceededCount;

            if (!string.IsNullOrWhiteSpace(app.RegistryKeyPath) &&
                RegistryPath.TryParse(app.RegistryKeyPath, RegistryView.Registry64, out var key))
            {
                var r = await _safeRegistry.DeleteKeyAsync(key, report.JournalId);
                if (r.Succeeded) cleaned++;
            }

            return cleaned;
        }

        private static string CleanIconPath(string? displayIcon)
        {
            if (string.IsNullOrWhiteSpace(displayIcon)) return string.Empty;
            string path = displayIcon.Trim().Trim('"');
            int comma = path.LastIndexOf(',');
            if (comma > 2) path = path[..comma].Trim().Trim('"');
            return path;
        }

        public void OpenInstallLocation(InstalledAppItem app)
        {
            _installedApps.OpenInstallLocation(app);
        }

        #endregion
    }
}
