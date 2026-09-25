using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    /// <summary>
    /// Kurulu program listesi. (v3.21: bu servis yalnızca LİSTELEME yapar. Eski
    /// ScanLeftoversAsync / CleanLeftoversAsync / LaunchStandardUninstallAsync hiçbir
    /// yerden çağrılmıyordu ve güvenlik kontrolü içermiyordu; kaldırıldı. Kalıntı
    /// taraması ResidualScannerEngine'de, kaldırma DeepUninstallerService'tedir.)
    /// </summary>
    public interface IUninstallerService
    {
        Task<List<InstalledAppItem>> GetInstalledAppsAsync();
        void OpenInstallLocation(InstalledAppItem app);
    }

    public class UninstallerService : IUninstallerService
    {
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

                // Tekilleştirme: HKCU'nun 32/64 görünümleri aynı anahtarı gösterir ve bazı
                // programlar iki görünüme de yazar. Ad + yayıncı + sürüm birlikte aynıysa
                // aynı kayıttır; yalnızca ada göre gruplamak x86/x64 sürümlerini yutuyordu.
                var distinctApps = apps
                    .GroupBy(a => (a.DisplayName.Trim().ToLowerInvariant(), a.Publisher.Trim().ToLowerInvariant(), a.DisplayVersion.Trim()))
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

                        // Boyut kayıt defterinde yoksa klasör ölçümü sonraya bırakılır (liste hemen görünür).
                        bool sizePending = sizeBytes == 0 && !string.IsNullOrWhiteSpace(installLocation);

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

                        // Yayıncı kaldırmayı kapatmış (NoRemove=1): Windows da "Kaldır" düğmesini gizler.
                        bool noRemove = appKey.GetValue("NoRemove") is int nr && nr == 1;

                        list.Add(new InstalledAppItem
                        {
                            DisplayName = displayName,
                            Publisher = publisher,
                            DisplayVersion = version,
                            InstallDate = formattedDate,
                            EstimatedSizeBytes = sizeBytes,
                            FormattedSize = sizePending ? "—" : Bakım.Core.Text.ByteFormatter.Format(sizeBytes),
                            SizePending = sizePending,
                            UninstallString = uninstallString,
                            QuietUninstallString = quietUninstallString,
                            InstallLocation = installLocation,
                            DisplayIconPath = displayIcon,
                            // Görünüm (32/64) bilgisiyle kalıcı biçim: "HKLM\...\Uninstall\X [32]".
                            // Eskiden "LocalMachine\..." yazılıyor ve 32 bit kayıtlar 64 bit
                            // görünümde aranıyordu (yanlış anahtar silinebiliyordu).
                            RegistryKeyPath = new Bakım.Core.Safety.RegistryPath(
                                hive, hive == RegistryHive.CurrentUser ? RegistryView.Registry64 : view,
                                $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{subKeyName}").ToDisplay(),
                            Is64Bit = is64Bit,
                            IsSystemComponent = isSystemComponent,
                            NoRemove = noRemove
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }

        #endregion

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


        #region Helpers

        /// <summary>
        /// Kurulum klasörünün boyutu; bağlantı noktalarına inilmez, erişilemeyen alt klasörler atlanır,
        /// iptal edilebilir. Klasör yoksa ya da korumalı ise 0.
        /// </summary>
        public static long MeasureInstallFolder(string folderPath, System.Threading.CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return 0;
            if (!Bakım.Core.Safety.PathSafetyGuard.Default.CheckDeletion(folderPath, isDirectory: true, allowOutsideKnownRoots: true).IsAllowed)
                return 0; // "C:\Program Files" gibi yanlış InstallLocation kökleri sayılmaz

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            long total = 0;
            try
            {
                foreach (var file in new DirectoryInfo(folderPath).EnumerateFiles("*", options))
                {
                    ct.ThrowIfCancellationRequested();
                    total += file.Length;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return total;
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



        #endregion
    }
}
