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
    public interface IResidualScannerEngine
    {
        Task<List<LeftoverItem>> ScanResidualsAsync(InstalledAppItem app, IProgress<string>? progress = null);
        Task<int> CleanResidualsAsync(IEnumerable<LeftoverItem> leftovers, IProgress<string>? progress = null);
        Task<List<LeftoverItem>> ScanHeuristicResidualsAsync(string targetPathOrExe, string appNameHint);
        Task<List<ResidualItem>> ScanResidualItemsAsync(InstalledAppItem app, IProgress<string>? progress = null);
        Task<int> CleanResidualItemsAsync(IEnumerable<ResidualItem> items, IProgress<string>? progress = null);
    }

    public class ResidualScannerEngine : IResidualScannerEngine
    {
        private static readonly string[] GenericBlacklistTokens = new[]
        {
            "microsoft", "windows", "corporation", "inc", "ltd", "the", "app", "application",
            "software", "installer", "setup", "update", "updater", "service", "system", "tool",
            "tools", "common", "shared", "temp", "cache", "data", "bin", "lib", "help", "doc",
            "support", "x86", "x64", "net", "framework", "runtime", "client", "desktop"
        };

        private static readonly string[] ProtectedSystemNames = new[]
        {
            "windows", "system32", "syswow64", "winsxs", "drivers", "boot",
            "system volume information", "$recycle.bin", "recovery", "msocache",
            "microsoft", "windows nt", "windows defender", "internet explorer"
        };

        #region Stage 1 & 2: Complete Residual Scan

        public async Task<List<LeftoverItem>> ScanResidualsAsync(InstalledAppItem app, IProgress<string>? progress = null)
        {
            return await Task.Run(() =>
            {
                var leftovers = new List<LeftoverItem>();
                var searchTokens = ExtractSearchTokens(app);

                if (searchTokens.Count == 0 && string.IsNullOrWhiteSpace(app.InstallLocation))
                {
                    return leftovers;
                }

                progress?.Report("Aşama 1: Dosya sistemi ve dizin kalıntıları taranıyor...");

                // 1. InstallLocation (Eğer dizin mevcutsa doğrudan %100 güvenilirlikle ekle)
                if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
                {
                    if (!IsProtectedDirectory(app.InstallLocation))
                    {
                        long size = CalculateFolderSizeSafe(app.InstallLocation);
                        leftovers.Add(new LeftoverItem
                        {
                            Path = app.InstallLocation,
                            ItemType = LeftoverType.Folder,
                            SizeBytes = size,
                            FormattedSize = FormatBytes(size),
                            Description = "Program Kurulum Ana Dizini Kalıntısı",
                            ConfidenceScore = 100,
                            IsSelected = true
                        });
                    }
                }

                // 2. Dosya Sistemi Arama Dizinleri
                var candidateRoots = new List<string>
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow"),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), // ProgramData
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Path.GetTempPath(),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), // All Users Start Menu
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs)        // Current User Start Menu
                };

                foreach (string root in candidateRoots)
                {
                    if (!Directory.Exists(root)) continue;

                    try
                    {
                        var dirInfo = new DirectoryInfo(root);
                        foreach (var subDir in dirInfo.GetDirectories())
                        {
                            if (IsProtectedDirectory(subDir.FullName)) continue;

                            string dirName = subDir.Name.ToLowerInvariant();
                            int confidence = CalculateMatchConfidence(dirName, searchTokens, app);

                            if (confidence >= 75)
                            {
                                if (!leftovers.Any(l => l.Path.Equals(subDir.FullName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    long size = CalculateFolderSizeSafe(subDir.FullName);
                                    leftovers.Add(new LeftoverItem
                                    {
                                        Path = subDir.FullName,
                                        ItemType = LeftoverType.Folder,
                                        SizeBytes = size,
                                        FormattedSize = FormatBytes(size),
                                        Description = $"{dirInfo.Name} Yetim Uygulama Klasörü",
                                        ConfidenceScore = confidence,
                                        IsSelected = true
                                    });
                                }
                            }
                        }

                        // Kısayollar (.lnk) taraması
                        if (root.Contains("Programs", StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (var file in dirInfo.GetFiles("*.lnk", SearchOption.AllDirectories))
                            {
                                string fname = Path.GetFileNameWithoutExtension(file.Name).ToLowerInvariant();
                                if (searchTokens.Any(t => fname.Contains(t)))
                                {
                                    if (!leftovers.Any(l => l.Path.Equals(file.FullName, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        leftovers.Add(new LeftoverItem
                                        {
                                            Path = file.FullName,
                                            ItemType = LeftoverType.File,
                                            SizeBytes = file.Length,
                                            FormattedSize = FormatBytes(file.Length),
                                            Description = "Başlat Menüsü Yetim Kısayolu",
                                            ConfidenceScore = 100,
                                            IsSelected = true
                                        });
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                progress?.Report("Aşama 2: Kayıt Defteri (Registry) derin kalıntı taraması yapılıyor...");

                // 3. Kayıt Defteri Taraması
                ScanRegistryHive(RegistryHive.CurrentUser, @"Software", searchTokens, app, leftovers);
                ScanRegistryHive(RegistryHive.LocalMachine, @"Software", searchTokens, app, leftovers);
                ScanRegistryHive(RegistryHive.LocalMachine, @"Software\WOW6432Node", searchTokens, app, leftovers);
                ScanRegistryFileExts(searchTokens, app, leftovers);

                // Yetim Uninstall Kaydı
                if (!string.IsNullOrWhiteSpace(app.RegistryKeyPath))
                {
                    if (!leftovers.Any(l => l.Path.Equals(app.RegistryKeyPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        leftovers.Add(new LeftoverItem
                        {
                            Path = app.RegistryKeyPath,
                            ItemType = LeftoverType.RegistryKey,
                            SizeBytes = 1024,
                            FormattedSize = "1 KB",
                            Description = "Windows Uninstall Kayıt Defteri Anahtarı",
                            ConfidenceScore = 100,
                            IsSelected = true
                        });
                    }
                }

                return leftovers;
            });
        }

        private void ScanRegistryHive(RegistryHive hive, string basePath, List<string> searchTokens, InstalledAppItem app, List<LeftoverItem> results)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var targetKey = baseKey.OpenSubKey(basePath);
                if (targetKey == null) return;

                foreach (string name in targetKey.GetSubKeyNames())
                {
                    string nameLower = name.ToLowerInvariant();
                    if (GenericBlacklistTokens.Contains(nameLower)) continue;

                    int confidence = CalculateMatchConfidence(nameLower, searchTokens, app);
                    if (confidence >= 75)
                    {
                        string fullPath = $@"{hive}\{basePath}\{name}";
                        if (!results.Any(r => r.Path.Equals(fullPath, StringComparison.OrdinalIgnoreCase)))
                        {
                            results.Add(new LeftoverItem
                            {
                                Path = fullPath,
                                ItemType = LeftoverType.RegistryKey,
                                SizeBytes = 2048,
                                FormattedSize = "2 KB",
                                Description = "Yetim Yazılım Kayıt Defteri Anahtarı",
                                ConfidenceScore = confidence,
                                IsSelected = true
                            });
                        }
                    }
                }
            }
            catch { }
        }

        private void ScanRegistryFileExts(List<string> searchTokens, InstalledAppItem app, List<LeftoverItem> results)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                using var extsKey = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts");
                if (extsKey == null) return;

                foreach (string ext in extsKey.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = extsKey.OpenSubKey($@"{ext}\OpenWithProgids");
                        if (sub == null) continue;

                        foreach (string valName in sub.GetValueNames())
                        {
                            string valLower = valName.ToLowerInvariant();
                            if (searchTokens.Any(t => valLower.Contains(t)))
                            {
                                string fullPath = $@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\OpenWithProgids\{valName}";
                                if (!results.Any(r => r.Path.Equals(fullPath, StringComparison.OrdinalIgnoreCase)))
                                {
                                    results.Add(new LeftoverItem
                                    {
                                        Path = fullPath,
                                        ItemType = LeftoverType.RegistryKey,
                                        SizeBytes = 512,
                                        FormattedSize = "512 B",
                                        Description = $"Dosya İlişkilendirme Kaydı ({ext})",
                                        ConfidenceScore = 90,
                                        IsSelected = true
                                    });
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        #endregion

        #region Heuristic Force Scan

        public async Task<List<LeftoverItem>> ScanHeuristicResidualsAsync(string targetPathOrExe, string appNameHint)
        {
            return await Task.Run(() =>
            {
                var leftovers = new List<LeftoverItem>();
                string cleanHint = CleanToken(appNameHint);
                var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(cleanHint))
                {
                    foreach (var p in cleanHint.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (p.Length >= 3 && !GenericBlacklistTokens.Contains(p.ToLowerInvariant()))
                            tokens.Add(p.ToLowerInvariant());
                    }
                }

                // If targetPathOrExe is a folder
                if (Directory.Exists(targetPathOrExe))
                {
                    long size = CalculateFolderSizeSafe(targetPathOrExe);
                    leftovers.Add(new LeftoverItem
                    {
                        Path = targetPathOrExe,
                        ItemType = LeftoverType.Folder,
                        SizeBytes = size,
                        FormattedSize = FormatBytes(size),
                        Description = "Zorla Kaldırılacak Uygulama Ana Dizini",
                        ConfidenceScore = 100,
                        IsSelected = true
                    });
                }
                // If it's an executable file
                else if (File.Exists(targetPathOrExe))
                {
                    string parentDir = Path.GetDirectoryName(targetPathOrExe) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(parentDir) && !IsProtectedDirectory(parentDir))
                    {
                        long size = CalculateFolderSizeSafe(parentDir);
                        leftovers.Add(new LeftoverItem
                        {
                            Path = parentDir,
                            ItemType = LeftoverType.Folder,
                            SizeBytes = size,
                            FormattedSize = FormatBytes(size),
                            Description = "Zorla Kaldırılacak Uygulama Klasörü",
                            ConfidenceScore = 100,
                            IsSelected = true
                        });
                    }
                }

                // Scan AppData for tokens
                if (tokens.Count > 0)
                {
                    var fakeApp = new InstalledAppItem
                    {
                        DisplayName = appNameHint,
                        Publisher = string.Empty,
                        InstallLocation = Directory.Exists(targetPathOrExe) ? targetPathOrExe : (Path.GetDirectoryName(targetPathOrExe) ?? string.Empty)
                    };

                    var subScan = ScanResidualsAsync(fakeApp).GetAwaiter().GetResult();
                    foreach (var s in subScan)
                    {
                        if (!leftovers.Any(l => l.Path.Equals(s.Path, StringComparison.OrdinalIgnoreCase)))
                        {
                            leftovers.Add(s);
                        }
                    }
                }

                return leftovers;
            });
        }

        #endregion

        #region Leftover Cleaner

        public async Task<int> CleanResidualsAsync(IEnumerable<LeftoverItem> leftovers, IProgress<string>? progress = null)
        {
            return await Task.Run(() =>
            {
                int cleanedCount = 0;
                var list = leftovers.ToList();

                for (int i = 0; i < list.Count; i++)
                {
                    var item = list[i];
                    progress?.Report($"Siliniyor ({i + 1}/{list.Count}): {Path.GetFileName(item.Path)}");

                    try
                    {
                        if (item.ItemType == LeftoverType.Folder && Directory.Exists(item.Path))
                        {
                            if (!IsProtectedDirectory(item.Path))
                            {
                                RemoveReadOnlyAttributesRecursive(item.Path);
                                Directory.Delete(item.Path, recursive: true);
                                item.IsDeleted = true;
                                cleanedCount++;
                            }
                        }
                        else if (item.ItemType == LeftoverType.File && File.Exists(item.Path))
                        {
                            File.SetAttributes(item.Path, FileAttributes.Normal);
                            File.Delete(item.Path);
                            item.IsDeleted = true;
                            cleanedCount++;
                        }
                        else if (item.ItemType == LeftoverType.RegistryKey)
                        {
                            if (DeleteRegistryKeySafe(item.Path))
                            {
                                item.IsDeleted = true;
                                cleanedCount++;
                            }
                        }
                    }
                    catch { }
                }

                return cleanedCount;
            });
        }

        private static void RemoveReadOnlyAttributesRecursive(string dirPath)
        {
            try
            {
                var dir = new DirectoryInfo(dirPath);
                dir.Attributes &= ~FileAttributes.ReadOnly;
                foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try { file.Attributes &= ~FileAttributes.ReadOnly; } catch { }
                }
            }
            catch { }
        }

        private bool DeleteRegistryKeySafe(string fullPath)
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

        #endregion

        #region Helpers & Match Scoring

        private static int CalculateMatchConfidence(string targetName, List<string> tokens, InstalledAppItem app)
        {
            string cleanApp = CleanToken(app.DisplayName).ToLowerInvariant();
            string cleanPub = CleanToken(app.Publisher).ToLowerInvariant();

            // Exact app name match
            if (targetName.Equals(cleanApp, StringComparison.OrdinalIgnoreCase)) return 100;
            if (cleanApp.Length >= 4 && targetName.Contains(cleanApp, StringComparison.OrdinalIgnoreCase)) return 100;

            // Publisher + App token combination
            bool matchesPub = !string.IsNullOrWhiteSpace(cleanPub) && targetName.Contains(cleanPub, StringComparison.OrdinalIgnoreCase);
            bool matchesAnyToken = tokens.Any(t => targetName.Contains(t, StringComparison.OrdinalIgnoreCase));

            if (matchesPub && matchesAnyToken) return 100;
            if (matchesAnyToken) return 85;

            return 0;
        }

        private static List<string> ExtractSearchTokens(InstalledAppItem app)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string cleanName = CleanToken(app.DisplayName);
            foreach (var part in cleanName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string pLower = part.ToLowerInvariant();
                if (pLower.Length >= 3 && !GenericBlacklistTokens.Contains(pLower))
                {
                    tokens.Add(pLower);
                }
            }

            if (!string.IsNullOrWhiteSpace(app.Publisher) &&
                !app.Publisher.Contains("Bilinmeyen", StringComparison.OrdinalIgnoreCase))
            {
                string cleanPub = CleanToken(app.Publisher);
                foreach (var part in cleanPub.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    string pLower = part.ToLowerInvariant();
                    if (pLower.Length >= 4 && !GenericBlacklistTokens.Contains(pLower))
                    {
                        tokens.Add(pLower);
                    }
                }
            }

            return tokens.ToList();
        }

        private static string CleanToken(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            char[] chars = input.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = ' ';
            }
            return new string(chars).Trim();
        }

        private static bool IsProtectedDirectory(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return true;

            string normalized = fullPath.TrimEnd('\\', '/').ToLowerInvariant();
            string name = Path.GetFileName(normalized);

            if (ProtectedSystemNames.Contains(name)) return true;

            // Ensure we are not deleting C:\ or root drives
            if (Path.GetPathRoot(fullPath)?.TrimEnd('\\', '/').Equals(normalized, StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            return false;
        }

        private static long CalculateFolderSizeSafe(string folderPath)
        {
            try
            {
                var di = new DirectoryInfo(folderPath);
                return di.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
            }
            catch
            {
                return 0;
            }
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

        #region ResidualItem Extended API (V42.0)

        public async Task<List<ResidualItem>> ScanResidualItemsAsync(InstalledAppItem app, IProgress<string>? progress = null)
        {
            var leftovers = await ScanResidualsAsync(app, progress);
            return leftovers.Select(l => new ResidualItem
            {
                Path = l.Path,
                Type = l.ItemType switch
                {
                    LeftoverType.Folder => ResidualType.Folder,
                    LeftoverType.File => ResidualType.File,
                    LeftoverType.RegistryKey => ResidualType.RegistryKey,
                    _ => ResidualType.Folder
                },
                SizeInBytes = l.SizeBytes,
                Description = l.Description,
                ConfidenceScore = l.ConfidenceScore,
                IsSafeToDelete = l.ConfidenceScore >= 80,
                IsSelected = true
            }).ToList();
        }

        public async Task<int> CleanResidualItemsAsync(IEnumerable<ResidualItem> items, IProgress<string>? progress = null)
        {
            var mapped = items.Select(r => new LeftoverItem
            {
                Path = r.Path,
                ItemType = r.Type switch
                {
                    ResidualType.Folder => LeftoverType.Folder,
                    ResidualType.File => LeftoverType.File,
                    ResidualType.RegistryKey => LeftoverType.RegistryKey,
                    _ => LeftoverType.Folder
                },
                SizeBytes = r.SizeInBytes,
                Description = r.Description,
                ConfidenceScore = r.ConfidenceScore,
                IsSelected = r.IsSelected
            }).ToList();

            int count = await CleanResidualsAsync(mapped, progress);

            foreach (var r in items)
            {
                var m = mapped.FirstOrDefault(x => x.Path.Equals(r.Path, StringComparison.OrdinalIgnoreCase));
                if (m != null && m.IsDeleted)
                {
                    r.IsDeleted = true;
                }
            }

            return count;
        }

        public static async Task<List<ResidualItem>> ScanResidualsStaticAsync(string appName, string? publisher = null, string? installLocation = null)
        {
            var engine = new ResidualScannerEngine();
            var app = new InstalledAppItem
            {
                DisplayName = appName,
                Publisher = publisher ?? string.Empty,
                InstallLocation = installLocation ?? string.Empty
            };
            return await engine.ScanResidualItemsAsync(app);
        }

        #endregion
    }
}
