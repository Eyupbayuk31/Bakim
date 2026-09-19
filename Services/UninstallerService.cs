using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IUninstallerService
    {
        Task<List<InstalledAppItem>> GetInstalledAppsAsync();
        Task<bool> LaunchStandardUninstallAsync(InstalledAppItem app);
        Task<List<LeftoverItem>> ScanLeftoversAsync(InstalledAppItem app);
        Task<int> CleanLeftoversAsync(List<LeftoverItem> selectedLeftovers);
        void OpenInstallLocation(InstalledAppItem app);
    }

    public class UninstallerService : IUninstallerService
    {
        private static readonly string[] GenericKeywords = new[]
        {
            "microsoft", "windows", "corporation", "inc", "ltd", "the", "app", "application",
            "software", "installer", "setup", "update", "updater", "service", "system", "tool", "tools"
        };

        #region App Discovery (Registry Scanning)

        public async Task<List<InstalledAppItem>> GetInstalledAppsAsync()
        {
            return await Task.Run(() =>
            {
                var apps = new List<InstalledAppItem>();

                // 1. HKLM 64-bit
                ScanRegistryKey(RegistryHive.LocalMachine, RegistryView.Registry64, apps, true);

                // 2. HKLM 32-bit (WOW6432Node)
                ScanRegistryKey(RegistryHive.LocalMachine, RegistryView.Registry32, apps, false);

                // 3. HKCU 64-bit
                ScanRegistryKey(RegistryHive.CurrentUser, RegistryView.Registry64, apps, true);

                // 4. HKCU 32-bit
                ScanRegistryKey(RegistryHive.CurrentUser, RegistryView.Registry32, apps, false);

                // Deduplicate & filter
                var distinctApps = apps
                    .GroupBy(a => a.DisplayName.Trim().ToLowerInvariant())
                    .Select(g => g.OrderByDescending(a => a.EstimatedSizeBytes).First())
                    .OrderByDescending(a => a.EstimatedSizeBytes)
                    .ThenBy(a => a.DisplayName)
                    .ToList();

                return distinctApps;
            });
        }

        private void ScanRegistryKey(RegistryHive hive, RegistryView view, List<InstalledAppItem> list, bool is64Bit)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstallKey = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstallKey == null) return;

                foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                {
                    try
                    {
                        using var appKey = uninstallKey.OpenSubKey(subKeyName);
                        if (appKey == null) continue;

                        string displayName = appKey.GetValue("DisplayName")?.ToString()?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(displayName)) continue;

                        // Güncelleme veya yama kontrolü (ParentKeyName varsa ana uygulamanın yamasıdır)
                        if (appKey.GetValue("ParentKeyName") != null) continue;

                        string publisher = appKey.GetValue("Publisher")?.ToString()?.Trim() ?? "Bilinmeyen Yayıncı";
                        string version = appKey.GetValue("DisplayVersion")?.ToString()?.Trim() ?? "1.0";
                        string uninstallString = appKey.GetValue("UninstallString")?.ToString()?.Trim() ?? string.Empty;
                        string quietUninstallString = appKey.GetValue("QuietUninstallString")?.ToString()?.Trim() ?? string.Empty;
                        string installLocation = appKey.GetValue("InstallLocation")?.ToString()?.Trim() ?? string.Empty;
                        string displayIcon = appKey.GetValue("DisplayIcon")?.ToString()?.Trim() ?? string.Empty;

                        // Kurulum Tarihi
                        string rawDate = appKey.GetValue("InstallDate")?.ToString()?.Trim() ?? string.Empty;
                        string formattedDate = FormatInstallDate(rawDate);

                        // Boyut Hesaplama (EstimatedSize KB cinsindendir)
                        long sizeBytes = 0;
                        object? rawSize = appKey.GetValue("EstimatedSize");
                        if (rawSize is int intSize) sizeBytes = (long)intSize * 1024;
                        else if (rawSize is long longSize) sizeBytes = longSize * 1024;
                        else if (rawSize != null && long.TryParse(rawSize.ToString(), out long parsedSize))
                        {
                            sizeBytes = parsedSize * 1024;
                        }

                        // Eğer EstimatedSize 0 ise ve InstallLocation varsa, klasör boyutunu hesaplamaya çalış
                        if (sizeBytes == 0 && !string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
                        {
                            sizeBytes = CalculateFolderSizeSafe(installLocation);
                        }

                        // Sistem Bileşeni Kontrolü
                        bool isSystemComponent = false;
                        object? sysCompVal = appKey.GetValue("SystemComponent");
                        if (sysCompVal is int sc && sc == 1) isSystemComponent = true;

                        if (displayName.Contains("Visual C++", StringComparison.OrdinalIgnoreCase) ||
                            displayName.Contains(".NET", StringComparison.OrdinalIgnoreCase) ||
                            displayName.Contains("Windows Driver Package", StringComparison.OrdinalIgnoreCase) ||
                            displayName.Contains("DirectX", StringComparison.OrdinalIgnoreCase) ||
                            displayName.Contains("Windows Software Development Kit", StringComparison.OrdinalIgnoreCase))
                        {
                            isSystemComponent = true;
                        }

                        list.Add(new InstalledAppItem
                        {
                            DisplayName = displayName,
                            Publisher = publisher,
                            DisplayVersion = version,
                            InstallDate = formattedDate,
                            EstimatedSizeBytes = sizeBytes,
                            FormattedSize = FormatBytes(sizeBytes),
                            UninstallString = uninstallString,
                            QuietUninstallString = quietUninstallString,
                            InstallLocation = installLocation,
                            DisplayIconPath = displayIcon,
                            RegistryKeyPath = $@"{hive}\Software\Microsoft\Windows\CurrentVersion\Uninstall\{subKeyName}",
                            Is64Bit = is64Bit,
                            IsSystemComponent = isSystemComponent
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }

        #endregion

        #region Stage 1: Standard Uninstallation

        public async Task<bool> LaunchStandardUninstallAsync(InstalledAppItem app)
        {
            return await Task.Run(async () =>
            {
                string command = !string.IsNullOrWhiteSpace(app.QuietUninstallString)
                    ? app.QuietUninstallString
                    : app.UninstallString;

                if (string.IsNullOrWhiteSpace(command)) return false;

                try
                {
                    string fileName;
                    string arguments = string.Empty;

                    // MsiExec /I or /X command
                    if (command.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
                    {
                        fileName = "msiexec.exe";
                        int idx = command.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase);
                        arguments = command.Substring(idx + 7).Trim();
                        // /I yerine /X (Uninstall) kullan
                        arguments = RegexReplaceInsensitive(arguments, "/I", "/X");
                    }
                    else if (command.StartsWith("\""))
                    {
                        int closingQuote = command.IndexOf('\"', 1);
                        if (closingQuote > 0)
                        {
                            fileName = command.Substring(1, closingQuote - 1);
                            arguments = command.Substring(closingQuote + 1).Trim();
                        }
                        else
                        {
                            fileName = command.Trim('\"');
                        }
                    }
                    else
                    {
                        int spaceIdx = command.IndexOf(' ');
                        if (spaceIdx > 0)
                        {
                            fileName = command.Substring(0, spaceIdx);
                            arguments = command.Substring(spaceIdx + 1).Trim();
                        }
                        else
                        {
                            fileName = command;
                        }
                    }

                    var psi = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = true
                    };

                    using var process = Process.Start(psi);
                    if (process != null)
                    {
                        await process.WaitForExitAsync();
                        return true;
                    }
                }
                catch { }

                return false;
            });
        }

        #endregion

        #region Stage 2: Deep Leftover Scan Engine

        public async Task<List<LeftoverItem>> ScanLeftoversAsync(InstalledAppItem app)
        {
            return await Task.Run(() =>
            {
                var leftovers = new List<LeftoverItem>();
                var searchTokens = ExtractSearchTokens(app);

                if (searchTokens.Count == 0) return leftovers;

                // 1. Dosya Sistemi Kalıntı Taraması
                var candidateDirs = new List<string>
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow"),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), // ProgramData
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Path.GetTempPath()
                };

                // InstallLocation ekle (Eğer hala duruyorsa)
                if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
                {
                    long folderSize = CalculateFolderSizeSafe(app.InstallLocation);
                    leftovers.Add(new LeftoverItem
                    {
                        Path = app.InstallLocation,
                        ItemType = LeftoverType.Folder,
                        SizeBytes = folderSize,
                        FormattedSize = FormatBytes(folderSize),
                        Description = "Program Kurulum Dizini Kalıntısı"
                    });
                }

                // Dizinlerde arama yap
                foreach (string root in candidateDirs)
                {
                    if (!Directory.Exists(root)) continue;

                    try
                    {
                        var dirInfo = new DirectoryInfo(root);
                        foreach (var subDir in dirInfo.GetDirectories())
                        {
                            if (IsProtectedSystemDirectory(subDir.FullName)) continue;

                            string dirName = subDir.Name.ToLowerInvariant();
                            bool isMatch = searchTokens.Any(token => dirName.Contains(token));

                            if (isMatch)
                            {
                                // Eğer daha önce eklenmediyse ekle
                                if (!leftovers.Any(l => l.Path.Equals(subDir.FullName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    long size = CalculateFolderSizeSafe(subDir.FullName);
                                    leftovers.Add(new LeftoverItem
                                    {
                                        Path = subDir.FullName,
                                        ItemType = LeftoverType.Folder,
                                        SizeBytes = size,
                                        FormattedSize = FormatBytes(size),
                                        Description = $"{Path.GetFileName(root)} Dizini Kalıntısı"
                                    });
                                }
                            }
                        }
                    }
                    catch { }
                }

                // 2. Kayıt Defteri (Registry) Kalıntı Taraması
                ScanRegistryLeftovers(RegistryHive.CurrentUser, @"Software", searchTokens, leftovers);
                ScanRegistryLeftovers(RegistryHive.LocalMachine, @"Software", searchTokens, leftovers);
                ScanRegistryLeftovers(RegistryHive.LocalMachine, @"Software\WOW6432Node", searchTokens, leftovers);

                // Yetim Uninstall Anahtarı kontrolü
                if (!string.IsNullOrWhiteSpace(app.RegistryKeyPath))
                {
                    leftovers.Add(new LeftoverItem
                    {
                        Path = app.RegistryKeyPath,
                        ItemType = LeftoverType.RegistryKey,
                        SizeBytes = 1024,
                        FormattedSize = "1 KB",
                        Description = "Yetim Kayıt Defteri Kurulum Anahtarı"
                    });
                }

                return leftovers;
            });
        }

        private void ScanRegistryLeftovers(RegistryHive hive, string subPath, List<string> tokens, List<LeftoverItem> list)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var targetKey = baseKey.OpenSubKey(subPath);
                if (targetKey == null) return;

                foreach (string name in targetKey.GetSubKeyNames())
                {
                    string nameLower = name.ToLowerInvariant();
                    if (GenericKeywords.Contains(nameLower)) continue;

                    if (tokens.Any(t => nameLower.Contains(t)))
                    {
                        string fullPath = $@"{hive}\{subPath}\{name}";
                        if (!list.Any(l => l.Path.Equals(fullPath, StringComparison.OrdinalIgnoreCase)))
                        {
                            list.Add(new LeftoverItem
                            {
                                Path = fullPath,
                                ItemType = LeftoverType.RegistryKey,
                                SizeBytes = 2048,
                                FormattedSize = "2 KB",
                                Description = "Yetim Yazılım Kayıt Defteri Anahtarı"
                            });
                        }
                    }
                }
            }
            catch { }
        }

        private List<string> ExtractSearchTokens(InstalledAppItem app)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // App Name Tokenizing
            string cleanName = CleanStringForTokens(app.DisplayName);
            foreach (var part in cleanName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Length >= 3 && !GenericKeywords.Contains(part.ToLowerInvariant()))
                {
                    tokens.Add(part.ToLowerInvariant());
                }
            }

            // Publisher Tokenizing
            if (!string.IsNullOrWhiteSpace(app.Publisher) &&
                !app.Publisher.Contains("Bilinmeyen", StringComparison.OrdinalIgnoreCase))
            {
                string cleanPub = CleanStringForTokens(app.Publisher);
                foreach (var part in cleanPub.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (part.Length >= 4 && !GenericKeywords.Contains(part.ToLowerInvariant()))
                    {
                        tokens.Add(part.ToLowerInvariant());
                    }
                }
            }

            return tokens.ToList();
        }

        private static string CleanStringForTokens(string input)
        {
            char[] chars = input.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = ' ';
            }
            return new string(chars);
        }

        private static bool IsProtectedSystemDirectory(string fullPath)
        {
            string name = Path.GetFileName(fullPath).ToLowerInvariant();
            if (name == "windows" || name == "system32" || name == "syswow64" ||
                name == "winsxs" || name == "drivers" || name == "common files" ||
                name == "microsoft" || name == "windows nt" || name == "windows defender")
            {
                return true;
            }
            return false;
        }

        #endregion

        #region Leftover Cleaning

        public async Task<int> CleanLeftoversAsync(List<LeftoverItem> selectedLeftovers)
        {
            return await Task.Run(() =>
            {
                int cleanedCount = 0;

                foreach (var item in selectedLeftovers)
                {
                    try
                    {
                        if (item.ItemType == LeftoverType.Folder && Directory.Exists(item.Path))
                        {
                            if (!IsProtectedSystemDirectory(item.Path))
                            {
                                Directory.Delete(item.Path, recursive: true);
                                item.IsDeleted = true;
                                cleanedCount++;
                            }
                        }
                        else if (item.ItemType == LeftoverType.File && File.Exists(item.Path))
                        {
                            File.Delete(item.Path);
                            item.IsDeleted = true;
                            cleanedCount++;
                        }
                        else if (item.ItemType == LeftoverType.RegistryKey)
                        {
                            bool ok = DeleteRegistryKeySafe(item.Path);
                            if (ok)
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

        private bool DeleteRegistryKeySafe(string fullPath)
        {
            try
            {
                // Format: HKEY_LOCAL_MACHINE\Software\... veya LocalMachine\...
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
            try
            {
                if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
                {
                    Process.Start("explorer.exe", $"\"{app.InstallLocation}\"");
                }
            }
            catch { }
        }

        #endregion

        #region Helpers

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

        private static string FormatInstallDate(string rawDate)
        {
            if (rawDate.Length == 8 && int.TryParse(rawDate, out _))
            {
                string y = rawDate.Substring(0, 4);
                string m = rawDate.Substring(4, 2);
                string d = rawDate.Substring(6, 2);
                return $"{d}.{m}.{y}";
            }
            return !string.IsNullOrWhiteSpace(rawDate) ? rawDate : "Bilinmiyor";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        private static string RegexReplaceInsensitive(string input, string pattern, string replacement)
        {
            return System.Text.RegularExpressions.Regex.Replace(
                input, pattern, replacement, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        #endregion
    }
}
