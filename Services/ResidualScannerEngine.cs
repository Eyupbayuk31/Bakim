using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Bakım.Core.Safety;
using Bakım.Core.Text;
using Bakım.Models;
using Bakım.Services.Safety;

namespace Bakım.Services
{
    /// <summary>
    /// Kalıntı taramasının bağlamı.
    /// </summary>
    /// <param name="UninstallConfirmed">
    /// Resmi kaldırıcının programı gerçekten kaldırdığı doğrulandı mı? Doğrulanmadıysa
    /// kurulum klasörü ve Uninstall kaydı ASLA kalıntı olarak önerilmez: program hâlâ
    /// kurulu olabilir (kullanıcı kaldırıcıda "İptal"e basmış olabilir).
    /// </param>
    public sealed record ResidualScanOptions(bool UninstallConfirmed)
    {
        /// <summary>Kaldırma doğrulanmadı: yalnızca isim tabanlı, seçilmemiş adaylar.</summary>
        public static ResidualScanOptions Unconfirmed { get; } = new(false);

        /// <summary>Kaldırma doğrulandı: kurulum klasörü ve Uninstall kaydı kesin kalıntıdır.</summary>
        public static ResidualScanOptions Confirmed { get; } = new(true);
    }

    /// <summary>Temizlik sonucu: geri alma günlüğü ve öğe bazında sonuçlar.</summary>
    public sealed record ResidualCleanReport(string JournalId, IReadOnlyList<OperationResult> Results)
    {
        public int SucceededCount => Results.Count(r => r.Succeeded);
        public int FailedCount => Results.Count(r => !r.Succeeded && r.Outcome != DeleteOutcome.NotFound);
        public long BytesFreed => Results.Where(r => r.Succeeded).Sum(r => r.BytesFreed);
        public bool HasRegistryBackup => Directory.Exists(UndoJournal.GetDirectory(JournalId)) &&
                                         Directory.EnumerateFiles(UndoJournal.GetDirectory(JournalId), "*.reg").Any();
    }

    public interface IResidualScannerEngine
    {
        /// <summary>Kaldırma doğrulanmamış varsayılır (güvenli varsayılan).</summary>
        Task<List<LeftoverItem>> ScanResidualsAsync(InstalledAppItem app, IProgress<string>? progress = null);
        Task<List<LeftoverItem>> ScanResidualsAsync(InstalledAppItem app, ResidualScanOptions options, IProgress<string>? progress = null);
        Task<int> CleanResidualsAsync(IEnumerable<LeftoverItem> leftovers, IProgress<string>? progress = null);
        Task<ResidualCleanReport> CleanResidualsDetailedAsync(IEnumerable<LeftoverItem> leftovers, string title, IProgress<string>? progress = null);
        Task<List<LeftoverItem>> ScanHeuristicResidualsAsync(string targetPathOrExe, string appNameHint);
        Task<List<ResidualItem>> ScanResidualItemsAsync(InstalledAppItem app, IProgress<string>? progress = null);
        Task<List<ResidualItem>> ScanResidualItemsAsync(InstalledAppItem app, ResidualScanOptions options, IProgress<string>? progress = null);
        Task<int> CleanResidualItemsAsync(IEnumerable<ResidualItem> items, IProgress<string>? progress = null);
        Task<ResidualCleanReport> CleanResidualItemsDetailedAsync(IEnumerable<ResidualItem> items, string title, IProgress<string>? progress = null);
    }

    /// <summary>
    /// Kaldırma sonrası kalıntı tarayıcısı.
    ///
    /// v3.21'de yeniden yazıldı. Eski motorun bulguları (bkz. docs/UNINSTALLER_V2_PLAN.md):
    ///   • tek kelimelik ALT DİZE eşleşmesi %85 güven alıyor ve "%100 Güvenli" gösteriliyordu,
    ///   • yayıncı + herhangi bir kelime %100 sayılıp toplu modda onaysız siliniyordu
    ///     ("Google Drive" → %LocalAppData%\Google, Chrome profili dahil),
    ///   • korunan klasör kontrolü yalnızca klasör ADINA bakıyordu (Program Files, İndirilenler korunmuyordu),
    ///   • kurulum klasörü, program hâlâ kuruluyken bile %100 kalıntı sayılıyordu,
    ///   • "HKCU\..." yolları HKLM sanılıyor, değerler anahtar gibi siliniyordu.
    /// </summary>
    public class ResidualScannerEngine : IResidualScannerEngine
    {
        private readonly IUninstallerService _installedApps;
        private readonly ISafeDeleteService _safeDelete;
        private readonly ISafeRegistryService _safeRegistry;
        private readonly PathSafetyGuard _guard;
        private readonly ILogService _log;

        private const int Certain = (int)MatchConfidence.Certain;
        private const int High = (int)MatchConfidence.High;

        public ResidualScannerEngine(
            IUninstallerService installedApps,
            ISafeDeleteService safeDelete,
            ISafeRegistryService safeRegistry,
            ILogService log)
            : this(installedApps, safeDelete, safeRegistry, PathSafetyGuard.Default, log)
        {
        }

        public ResidualScannerEngine(
            IUninstallerService installedApps,
            ISafeDeleteService safeDelete,
            ISafeRegistryService safeRegistry,
            PathSafetyGuard guard,
            ILogService log)
        {
            _installedApps = installedApps;
            _safeDelete = safeDelete;
            _safeRegistry = safeRegistry;
            _guard = guard;
            _log = log;
        }

        #region Tarama

        public Task<List<LeftoverItem>> ScanResidualsAsync(InstalledAppItem app, IProgress<string>? progress = null) =>
            ScanResidualsAsync(app, ResidualScanOptions.Unconfirmed, progress);

        public async Task<List<LeftoverItem>> ScanResidualsAsync(InstalledAppItem app, ResidualScanOptions options, IProgress<string>? progress = null)
        {
            // Diğer kurulu programların klasörleri: bunların içine, üstüne ya da
            // kendisine denk gelen hiçbir aday önerilmez.
            var otherLocations = await GetOtherInstallLocationsAsync(app);

            return await Task.Run(() =>
            {
                var results = new List<LeftoverItem>();
                var matcher = new NameMatcher(app.DisplayName, app.Publisher);

                progress?.Report("Aşama 1: Dosya sistemi kalıntıları taranıyor...");
                AddInstallLocation(app, options, otherLocations, results);
                if (matcher.HasIdentity)
                {
                    ScanFolders(matcher, otherLocations, results);
                    ScanStartMenuShortcuts(matcher, results);
                }

                progress?.Report("Aşama 2: Kayıt defteri kalıntıları taranıyor...");
                if (matcher.HasIdentity)
                {
                    ScanRegistryRoot(RegistryHive.CurrentUser, RegistryView.Registry64, matcher, results);
                    ScanRegistryRoot(RegistryHive.LocalMachine, RegistryView.Registry64, matcher, results);
                    ScanRegistryRoot(RegistryHive.LocalMachine, RegistryView.Registry32, matcher, results);
                    ScanFileAssociationValues(matcher, results);
                }
                AddUninstallKey(app, options, results);

                return results
                    .GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(x => x.ConfidenceScore).First())
                    .OrderByDescending(r => r.ConfidenceScore)
                    .ThenBy(r => r.ItemType)
                    .ToList();
            });
        }

        private async Task<List<string>> GetOtherInstallLocationsAsync(InstalledAppItem target)
        {
            try
            {
                var apps = await _installedApps.GetInstalledAppsAsync();
                return apps
                    .Where(a => !string.Equals(a.RegistryKeyPath, target.RegistryKeyPath, StringComparison.OrdinalIgnoreCase) ||
                                string.IsNullOrEmpty(target.RegistryKeyPath))
                    .Where(a => !string.Equals(a.DisplayName, target.DisplayName, StringComparison.OrdinalIgnoreCase))
                    .Select(a => WindowsPath.Normalize(a.InstallLocation))
                    .Where(p => p != null && WindowsPath.Depth(p) >= 1)
                    .Select(p => p!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                _log.Warning("Kurulu program listesi alınamadı; kalıntı taraması daha temkinli yapılacak.", ex, nameof(ResidualScannerEngine));
                return new List<string>();
            }
        }

        /// <summary>Aday klasör başka bir kurulu programın alanına dokunuyor mu?</summary>
        private static bool OverlapsOtherApp(string folder, IReadOnlyList<string> otherLocations)
        {
            string? n = WindowsPath.Normalize(folder);
            if (n == null) return true;
            foreach (string other in otherLocations)
            {
                if (WindowsPath.IsUnderOrEqual(n, other) || WindowsPath.IsStrictlyUnder(other, n)) return true;
            }
            return false;
        }

        private void AddInstallLocation(InstalledAppItem app, ResidualScanOptions options, IReadOnlyList<string> otherLocations, List<LeftoverItem> results)
        {
            if (!options.UninstallConfirmed) return;
            if (string.IsNullOrWhiteSpace(app.InstallLocation) || !Directory.Exists(app.InstallLocation)) return;

            var check = _guard.CheckDeletion(app.InstallLocation, isDirectory: true, allowOutsideKnownRoots: true);
            if (!check.IsAllowed)
            {
                _log.Info($"Kurulum klasörü korumalı olduğu için önerilmedi: {app.InstallLocation} ({check.Reason})", nameof(ResidualScannerEngine));
                return;
            }
            if (OverlapsOtherApp(app.InstallLocation, otherLocations))
            {
                _log.Info($"Kurulum klasörü başka bir programla paylaşıldığı için önerilmedi: {app.InstallLocation}", nameof(ResidualScannerEngine));
                return;
            }

            long size = SafeFolderSize(app.InstallLocation);
            results.Add(new LeftoverItem
            {
                Path = check.NormalizedPath,
                ItemType = LeftoverType.Folder,
                SizeBytes = size,
                FormattedSize = ByteFormatter.Format(size),
                Description = "Programın kurulum klasörü",
                EvidenceText = "Uninstall kaydındaki kurulum konumu; kaldırma doğrulandıktan sonra hâlâ duruyor.",
                ConfidenceScore = Certain,
                AllowOutsideKnownRoots = true,
                IsSelected = true
            });
        }

        private void AddUninstallKey(InstalledAppItem app, ResidualScanOptions options, List<LeftoverItem> results)
        {
            if (!options.UninstallConfirmed || string.IsNullOrWhiteSpace(app.RegistryKeyPath)) return;
            if (!RegistryPath.TryParse(app.RegistryKeyPath, RegistryView.Registry64, out var key)) return;
            if (!_safeRegistry.KeyExists(key)) return;

            results.Add(new LeftoverItem
            {
                Path = key.ToDisplay(),
                ItemType = LeftoverType.RegistryKey,
                FormattedSize = "Kayıt anahtarı",
                Description = "Yetim Uninstall kaydı",
                EvidenceText = "Kaldırıcı çalıştıktan sonra program listesindeki kayıt silinmemiş.",
                ConfidenceScore = Certain,
                IsSelected = true
            });
        }

        private static IEnumerable<(string Root, string Label)> CandidateRoots()
        {
            static string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
            string local = F(Environment.SpecialFolder.LocalApplicationData);
            string profile = F(Environment.SpecialFolder.UserProfile);

            yield return (local, "AppData\\Local");
            yield return (Path.Combine(local, "Programs"), "AppData\\Local\\Programs");
            yield return (F(Environment.SpecialFolder.ApplicationData), "AppData\\Roaming");
            yield return (Path.Combine(profile, "AppData", "LocalLow"), "AppData\\LocalLow");
            yield return (F(Environment.SpecialFolder.CommonApplicationData), "ProgramData");
            yield return (F(Environment.SpecialFolder.ProgramFiles), "Program Files");
            yield return (F(Environment.SpecialFolder.ProgramFilesX86), "Program Files (x86)");
            yield return (F(Environment.SpecialFolder.MyDocuments), "Belgeler");
        }

        private void ScanFolders(NameMatcher matcher, IReadOnlyList<string> otherLocations, List<LeftoverItem> results)
        {
            foreach (var (root, label) in CandidateRoots())
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                foreach (var dir in SafeGetDirectories(root))
                {
                    var match = matcher.Match(dir.Name);

                    if (match.Kind == MatchKind.PublisherRoot)
                    {
                        // Yayıncı klasörü (ör. %LocalAppData%\Google): kendisi asla aday olmaz,
                        // yalnızca içindeki uygulama klasörüne bakılır.
                        foreach (var child in SafeGetDirectories(dir.FullName))
                        {
                            var childMatch = matcher.Match(child.Name, dir.Name);
                            if (childMatch.IsCandidate)
                                AddFolderCandidate(child.FullName, $"{label}\\{dir.Name}", childMatch, otherLocations, results);
                        }
                        continue;
                    }

                    if (match.IsCandidate)
                        AddFolderCandidate(dir.FullName, label, match, otherLocations, results);
                }
            }
        }

        /// <summary>
        /// Bu adları taşıyan alt klasörler kullanıcı verisine işaret eder (tarayıcı profili,
        /// oyun kayıtları, belgeler). Böyle bir klasör asla otomatik seçilmez.
        /// </summary>
        private static readonly string[] UserDataMarkers =
        {
            "User Data", "Profiles", "Profile", "Saves", "Save", "SaveGames", "Saved Games",
            "Documents", "Backups", "Backup", "Projects", "Library"
        };

        private void AddFolderCandidate(string path, string label, NameMatch match, IReadOnlyList<string> otherLocations, List<LeftoverItem> results)
        {
            var check = _guard.CheckDeletion(path, isDirectory: true);
            if (!check.IsAllowed) return;
            if (OverlapsOtherApp(path, otherLocations)) return;

            int confidence = (int)match.Confidence;
            string evidence = match.Reason;

            bool inDocuments = label.StartsWith("Belgeler", StringComparison.OrdinalIgnoreCase);
            string? marker = UserDataMarkers.FirstOrDefault(m => Directory.Exists(Path.Combine(path, m)));
            if (inDocuments || marker != null)
            {
                confidence = Math.Min(confidence, (int)MatchConfidence.Medium);
                evidence += inDocuments
                    ? " Belgeler klasöründe: kişisel dosyalar içerebilir, lütfen inceleyin."
                    : $" Kullanıcı verisi içeriyor ('{marker}'): profil ya da kayıtlar silinebilir, lütfen inceleyin.";
            }

            long size = SafeFolderSize(path);
            results.Add(new LeftoverItem
            {
                Path = check.NormalizedPath,
                ItemType = LeftoverType.Folder,
                SizeBytes = size,
                FormattedSize = ByteFormatter.Format(size),
                Description = $"{label} klasörü",
                EvidenceText = evidence,
                ConfidenceScore = confidence,
                IsSelected = confidence >= High
            });
        }

        private void ScanStartMenuShortcuts(NameMatcher matcher, List<LeftoverItem> results)
        {
            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                         Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                         Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                         Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                     })
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                IEnumerable<FileInfo> shortcuts;
                try
                {
                    var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 3 };
                    shortcuts = new DirectoryInfo(root).EnumerateFiles("*.lnk", options).ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var file in shortcuts)
                {
                    var match = matcher.Match(Path.GetFileNameWithoutExtension(file.Name));
                    if (match.Confidence < MatchConfidence.High) continue;
                    if (!_guard.CheckDeletion(file.FullName, isDirectory: false).IsAllowed) continue;

                    results.Add(new LeftoverItem
                    {
                        Path = file.FullName,
                        ItemType = LeftoverType.File,
                        SizeBytes = file.Length,
                        FormattedSize = ByteFormatter.Format(file.Length),
                        Description = "Yetim kısayol",
                        EvidenceText = match.Reason,
                        ConfidenceScore = (int)match.Confidence,
                        IsSelected = true
                    });
                }
            }
        }

        private void ScanRegistryRoot(RegistryHive hive, RegistryView view, NameMatcher matcher, List<LeftoverItem> results)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var software = baseKey.OpenSubKey("Software");
                if (software == null) return;

                foreach (string name in software.GetSubKeyNames())
                {
                    var match = matcher.Match(name);

                    if (match.Kind == MatchKind.PublisherRoot)
                    {
                        using var vendor = software.OpenSubKey(name);
                        if (vendor == null) continue;
                        foreach (string child in vendor.GetSubKeyNames())
                        {
                            var childMatch = matcher.Match(child, name);
                            if (childMatch.IsCandidate)
                                AddRegistryCandidate(new RegistryPath(hive, view, $@"Software\{name}\{child}"), childMatch, results);
                        }
                        continue;
                    }

                    if (match.IsCandidate)
                        AddRegistryCandidate(new RegistryPath(hive, view, $@"Software\{name}"), match, results);
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                _log.Debug($"Kayıt defteri kökü okunamadı: {hive} {view} — {ex.Message}", nameof(ResidualScannerEngine));
            }
        }

        private static void AddRegistryCandidate(RegistryPath key, NameMatch match, List<LeftoverItem> results)
        {
            // Yayıncı kökü ("Software\Vendor") ancak açık izinle silinebilir; burada
            // aday olarak eklenen tek seviyeli anahtarlar uygulamanın kendi adını taşır.
            bool vendorRootLike = key.SubKeyDepth == 2;
            var check = RegistrySafetyGuard.CheckKeyDeletion(key, allowVendorRoot: vendorRootLike);
            if (!check.IsAllowed) return;

            results.Add(new LeftoverItem
            {
                Path = key.ToDisplay(),
                ItemType = LeftoverType.RegistryKey,
                FormattedSize = "Kayıt anahtarı",
                Description = "Uygulama ayar anahtarı",
                EvidenceText = match.Reason,
                ConfidenceScore = (int)match.Confidence,
                IsSelected = (int)match.Confidence >= High
            });
        }

        /// <summary>
        /// Explorer\FileExts\.ext\OpenWithProgids altındaki DEĞERLER (anahtar değil).
        /// ProgId genellikle "Uygulama.uzantı" biçimindedir; ilk bölüm eşleştirilir.
        /// </summary>
        private static void ScanFileAssociationValues(NameMatcher matcher, List<LeftoverItem> results)
        {
            const string fileExts = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                using var exts = baseKey.OpenSubKey(fileExts);
                if (exts == null) return;

                foreach (string ext in exts.GetSubKeyNames())
                {
                    using var progIds = exts.OpenSubKey($@"{ext}\OpenWithProgids");
                    if (progIds == null) continue;

                    foreach (string progId in progIds.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(progId)) continue;
                        string appPart = progId.Split('.')[0];
                        var match = matcher.Match(appPart);
                        if (match.Confidence < MatchConfidence.High) continue;

                        var valuePath = new RegistryPath(RegistryHive.CurrentUser, RegistryView.Registry64, $@"{fileExts}\{ext}\OpenWithProgids", progId);
                        results.Add(new LeftoverItem
                        {
                            Path = valuePath.ToString(),
                            ItemType = LeftoverType.RegistryValue,
                            FormattedSize = "Kayıt değeri",
                            Description = $"Dosya ilişkilendirmesi ({ext})",
                            EvidenceText = $"'{progId}' ilişkilendirmesi: {match.Reason}",
                            ConfidenceScore = (int)MatchConfidence.Medium,
                            IsSelected = false
                        });
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // İlişkilendirmeler okunamadıysa tarama eksik kalır; kritik değil.
            }
        }

        #endregion

        #region Zorla kaldırma taraması

        /// <summary>
        /// Uninstall kaydı olmayan (taşınabilir/bozuk) programlar için tarama.
        /// Eski sürüm hedef exe'nin ÜST klasörünü %100 güvenle ekliyordu; İndirilenler'deki
        /// bir exe için İndirilenler'in tamamı silinecekler listesine giriyordu. Artık
        /// klasör yalnızca <see cref="PathSafetyGuard"/> izin verirse ve "Yüksek" güvenle eklenir.
        /// </summary>
        public async Task<List<LeftoverItem>> ScanHeuristicResidualsAsync(string targetPathOrExe, string appNameHint)
        {
            string? folder = Directory.Exists(targetPathOrExe)
                ? targetPathOrExe
                : (File.Exists(targetPathOrExe) ? Path.GetDirectoryName(targetPathOrExe) : null);

            var fakeApp = new InstalledAppItem { DisplayName = appNameHint, Publisher = string.Empty, InstallLocation = folder ?? string.Empty };
            var results = await ScanResidualsAsync(fakeApp, ResidualScanOptions.Unconfirmed);

            if (!string.IsNullOrEmpty(folder))
            {
                var check = _guard.CheckDeletion(folder, isDirectory: true);
                if (check.IsAllowed && !results.Any(r => r.Path.Equals(check.NormalizedPath, StringComparison.OrdinalIgnoreCase)))
                {
                    long size = SafeFolderSize(folder);
                    results.Insert(0, new LeftoverItem
                    {
                        Path = check.NormalizedPath,
                        ItemType = LeftoverType.Folder,
                        SizeBytes = size,
                        FormattedSize = ByteFormatter.Format(size),
                        Description = "Programın klasörü",
                        EvidenceText = "Seçilen programın bulunduğu klasör.",
                        ConfidenceScore = High,
                        IsSelected = true
                    });
                }
            }

            return results;
        }

        #endregion

        #region Temizlik

        public async Task<int> CleanResidualsAsync(IEnumerable<LeftoverItem> leftovers, IProgress<string>? progress = null)
        {
            var report = await CleanResidualsDetailedAsync(leftovers, "Kalıntı temizliği", progress);
            return report.SucceededCount;
        }

        public async Task<ResidualCleanReport> CleanResidualsDetailedAsync(IEnumerable<LeftoverItem> leftovers, string title, IProgress<string>? progress = null)
        {
            var list = leftovers.ToList();
            string journal = UndoJournal.Create(title);
            var results = new List<OperationResult>();

            // Önce kayıt defteri (yedek alınır), sonra dosyalar, en son klasörler (derinden sığa).
            var ordered = list
                .OrderBy(l => l.ItemType switch
                {
                    LeftoverType.RegistryValue => 0,
                    LeftoverType.RegistryKey => 1,
                    LeftoverType.File => 2,
                    _ => 3
                })
                .ThenByDescending(l => l.Path.Length)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                var item = ordered[i];
                progress?.Report($"Temizleniyor ({i + 1}/{ordered.Count}): {item.Path}");

                OperationResult result = item.ItemType switch
                {
                    LeftoverType.RegistryKey => RegistryPath.TryParse(item.Path, RegistryView.Registry64, out var key)
                        ? await _safeRegistry.DeleteKeyAsync(key, journal)
                        : new OperationResult(item.Path, DeleteOutcome.Failed, "Kayıt defteri yolu çözümlenemedi.", 0),

                    LeftoverType.RegistryValue => RegistryPath.TryParseWithValue(item.Path, RegistryView.Registry64, out var value) && value.ValueName != null
                        ? await _safeRegistry.DeleteValueAsync(value, journal)
                        : new OperationResult(item.Path, DeleteOutcome.Failed, "Kayıt defteri değeri çözümlenemedi.", 0),

                    LeftoverType.File => await _safeDelete.DeletePathAsync(item.Path, isDirectory: false, new DeletePolicy(JournalId: journal)),

                    _ => await _safeDelete.DeletePathAsync(item.Path, isDirectory: true,
                        new DeletePolicy(AllowOutsideKnownRoots: item.AllowOutsideKnownRoots && item.ConfidenceScore >= Certain, JournalId: journal)),
                };

                item.IsDeleted = result.Succeeded || result.Outcome == DeleteOutcome.NotFound;
                if (result.Succeeded && result.BytesFreed == 0 && item.SizeBytes > 0)
                    result = result with { BytesFreed = item.SizeBytes };
                results.Add(result);
            }

            return new ResidualCleanReport(journal, results);
        }

        #endregion

        #region ResidualItem API

        public Task<List<ResidualItem>> ScanResidualItemsAsync(InstalledAppItem app, IProgress<string>? progress = null) =>
            ScanResidualItemsAsync(app, ResidualScanOptions.Unconfirmed, progress);

        public async Task<List<ResidualItem>> ScanResidualItemsAsync(InstalledAppItem app, ResidualScanOptions options, IProgress<string>? progress = null)
        {
            var leftovers = await ScanResidualsAsync(app, options, progress);
            return leftovers.Select(ToResidual).ToList();
        }

        public async Task<int> CleanResidualItemsAsync(IEnumerable<ResidualItem> items, IProgress<string>? progress = null)
        {
            var report = await CleanResidualItemsDetailedAsync(items, "Kalıntı temizliği", progress);
            return report.SucceededCount;
        }

        public async Task<ResidualCleanReport> CleanResidualItemsDetailedAsync(IEnumerable<ResidualItem> items, string title, IProgress<string>? progress = null)
        {
            var source = items.ToList();
            var mapped = source.Select(ToLeftover).ToList();
            var report = await CleanResidualsDetailedAsync(mapped, title, progress);

            for (int i = 0; i < source.Count; i++)
            {
                if (mapped[i].IsDeleted) source[i].IsDeleted = true;
            }
            return report;
        }

        private static ResidualItem ToResidual(LeftoverItem l) => new()
        {
            Path = l.Path,
            Type = l.ItemType switch
            {
                LeftoverType.File => ResidualType.File,
                LeftoverType.RegistryKey => ResidualType.RegistryKey,
                LeftoverType.RegistryValue => ResidualType.RegistryValue,
                _ => ResidualType.Folder
            },
            SizeInBytes = l.SizeBytes,
            Description = l.Description,
            EvidenceText = l.EvidenceText,
            ConfidenceScore = l.ConfidenceScore,
            AllowOutsideKnownRoots = l.AllowOutsideKnownRoots,
            IsSelected = l.IsSelected
        };

        private static LeftoverItem ToLeftover(ResidualItem r) => new()
        {
            Path = r.Path,
            ItemType = r.Type switch
            {
                ResidualType.File => LeftoverType.File,
                ResidualType.RegistryKey => LeftoverType.RegistryKey,
                ResidualType.RegistryValue => LeftoverType.RegistryValue,
                _ => LeftoverType.Folder
            },
            SizeBytes = r.SizeInBytes,
            Description = r.Description,
            EvidenceText = r.EvidenceText,
            ConfidenceScore = r.ConfidenceScore,
            AllowOutsideKnownRoots = r.AllowOutsideKnownRoots,
            IsSelected = r.IsSelected
        };

        #endregion

        #region Yardımcılar

        private static IEnumerable<DirectoryInfo> SafeGetDirectories(string path)
        {
            try
            {
                return new DirectoryInfo(path).GetDirectories()
                    .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return Array.Empty<DirectoryInfo>();
            }
        }

        private static long SafeFolderSize(string path)
        {
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                return new DirectoryInfo(path).EnumerateFiles("*", options).Sum(f => f.Length);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return 0;
            }
        }

        #endregion
    }
}
