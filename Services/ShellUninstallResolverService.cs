using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bakım.Core.Safety;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IShellUninstallResolverService
    {
        Task<InstalledAppItem?> ResolveTargetAppAsync(string rawPath);
    }

    public class ShellUninstallResolverService : IShellUninstallResolverService
    {
        private readonly IDeepUninstallerService _deepUninstaller;

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        public ShellUninstallResolverService(IDeepUninstallerService deepUninstaller)
        {
            _deepUninstaller = deepUninstaller;
        }

        public async Task<InstalledAppItem?> ResolveTargetAppAsync(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) return null;

            return await Task.Run(async () =>
            {
                string cleanedPath = rawPath.Trim().Trim('\"');
                if (!File.Exists(cleanedPath) && !Directory.Exists(cleanedPath))
                {
                    return null;
                }

                string targetExePath = cleanedPath;
                string shortcutName = Path.GetFileNameWithoutExtension(cleanedPath);
                string targetDir = string.Empty;
                string extension = Path.GetExtension(cleanedPath).ToLowerInvariant();

                // S-6: Yolu olmayan hedefler — MSI "advertised" kısayolu, .msi paketi, Steam .url —
                // Uninstall anahtar adıyla (ürün kodu / "Steam App N") birebir eşleştirilir.
                string? exactKeyName = extension switch
                {
                    ".lnk" => Helpers.MsiInterop.TryGetShortcutProductCode(cleanedPath),
                    ".msi" => Helpers.MsiInterop.TryGetPackageProductCode(cleanedPath),
                    ".url" => Helpers.MsiInterop.SteamUninstallKeyName(Helpers.MsiInterop.TryReadUrlShortcut(cleanedPath)),
                    _ => null
                };
                if (exactKeyName != null || extension is ".msi" or ".url")
                {
                    var apps = await _deepUninstaller.GetInstalledAppsAsync();
                    if (exactKeyName != null)
                    {
                        var byKey = apps.FirstOrDefault(a => UninstallKeyName(a).Equals(exactKeyName, StringComparison.OrdinalIgnoreCase));
                        if (byKey != null) return byKey;
                    }

                    // .msi kurulu değilse ya da .url (Steam dışı) ise: yalnızca ad eşleşmesi; tahmini
                    // "taşınabilir uygulama" üretilmez (hedef bir dosya klasörü değil).
                    if (extension is ".msi" or ".url")
                        return FindMatchingApp(apps, string.Empty, string.Empty, shortcutName);
                }

                // 1. Resolve .lnk shortcut if needed
                if (cleanedPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    string? resolvedTarget = ResolveShortcutTarget(cleanedPath);
                    if (!string.IsNullOrWhiteSpace(resolvedTarget) && (File.Exists(resolvedTarget) || Directory.Exists(resolvedTarget)))
                    {
                        targetExePath = resolvedTarget;
                    }
                }

                // Klasör hedefi: ana exe'yi seç (kaldırıcı/kurulum/güncelleyici exe'leri değil).
                if (Directory.Exists(targetExePath))
                {
                    targetDir = targetExePath;
                    targetExePath = PickMainExecutable(targetDir) ?? targetDir;
                }
                else if (File.Exists(targetExePath))
                {
                    targetDir = Path.GetDirectoryName(targetExePath) ?? string.Empty;
                }

                // Korumalı sistem hedefleri (C:\Windows\explorer.exe, C:\Program Files kökü,
                // Bakım'ın kendisi) asla kaldırma hedefi olmaz. Eskiden bunlar için
                // InstallLocation = C:\Windows sentezlenip sihirbaz Windows süreçlerini
                // kapatmaya çalışabiliyordu.
                var dirCheck = PathSafetyGuard.Default.CheckDeletion(targetDir, isDirectory: true, allowOutsideKnownRoots: true);
                if (dirCheck.Verdict == PathVerdict.ProtectedTree ||
                    (dirCheck.Verdict == PathVerdict.ProtectedExact && Directory.Exists(cleanedPath) && !File.Exists(cleanedPath)))
                {
                    AppLog.Warning($"Sağ tık kaldırma hedefi korumalı olduğu için reddedildi: {cleanedPath} ({dirCheck.Reason})", null, nameof(ShellUninstallResolverService));
                    return null;
                }

                // 2. Fetch all installed applications from system registry
                var installedApps = await _deepUninstaller.GetInstalledAppsAsync();

                // 3. Search for a matching installed application
                InstalledAppItem? matched = FindMatchingApp(installedApps, targetExePath, targetDir, shortcutName);

                if (matched != null)
                {
                    if (matched.IconSource == null)
                    {
                        matched.IconSource = ExtractIconSafe(targetExePath);
                    }
                    return matched;
                }

                // 4. Heuristic App Synthesis for portable or unlisted apps
                return SynthesizeHeuristicApp(targetExePath, targetDir, shortcutName);
            });
        }

        /// <summary>Uninstall alt anahtarının adı ("{GUID}", "Steam App 730"); " [32]" görünüm eki hariç.</summary>
        private static string UninstallKeyName(InstalledAppItem app) =>
            RegistryPath.TryParse(app.RegistryKeyPath, Microsoft.Win32.RegistryView.Registry64, out var path)
                ? Path.GetFileName(path.SubKey.TrimEnd('\\'))
                : Path.GetFileName(app.RegistryKeyPath.TrimEnd('\\'));

        private static InstalledAppItem? FindMatchingApp(
            System.Collections.Generic.List<InstalledAppItem> apps,
            string targetExePath,
            string targetDir,
            string shortcutName)
        {
            string normExe = targetExePath.TrimEnd('\\').ToLowerInvariant();
            string normDir = targetDir.TrimEnd('\\').ToLowerInvariant();
            string normShortcut = shortcutName.Trim().ToLowerInvariant();

            // A. Hedef klasör bir programın kurulum klasörüne eşit ya da onun ALTINDA.
            //    En derin (en özgül) kurulum klasörü seçilir.
            if (!string.IsNullOrWhiteSpace(normDir))
            {
                var matchByLoc = apps
                    .Where(a => !string.IsNullOrWhiteSpace(a.InstallLocation))
                    .Select(a => (App: a, Loc: a.InstallLocation.TrimEnd('\\').ToLowerInvariant()))
                    .Where(x => x.Loc.Length > 3 && (normDir == x.Loc || normDir.StartsWith(x.Loc + "\\")))
                    .OrderByDescending(x => x.Loc.Length)
                    .Select(x => x.App)
                    .FirstOrDefault();

                if (matchByLoc != null) return matchByLoc;

                // Hedef, kurulum klasörünün ÜSTÜ ise (ör. "C:\Program Files\VideoLAN"
                // → "…\VideoLAN\VLC") yalnızca TEK bir program eşleşiyorsa kabul edilir.
                // Eskiden "C:\Program Files"a sağ tıklamak ilk bulunan programı seçiyordu.
                var children = apps
                    .Where(a => !string.IsNullOrWhiteSpace(a.InstallLocation) &&
                                a.InstallLocation.TrimEnd('\\').ToLowerInvariant().StartsWith(normDir + "\\"))
                    .ToList();
                if (children.Count == 1 && PathSafetyGuard.Default.CheckDeletion(targetDir, isDirectory: true).IsAllowed)
                    return children[0];
            }

            // B. Match on DisplayIconPath
            if (!string.IsNullOrWhiteSpace(normExe))
            {
                var matchByIcon = apps.FirstOrDefault(a =>
                {
                    if (string.IsNullOrWhiteSpace(a.DisplayIconPath)) return false;
                    string iconPath = a.DisplayIconPath.Trim('\"');
                    int commaIdx = iconPath.IndexOf(',');
                    if (commaIdx > 0) iconPath = iconPath.Substring(0, commaIdx).Trim();
                    return iconPath.Equals(normExe, StringComparison.OrdinalIgnoreCase);
                });

                if (matchByIcon != null) return matchByIcon;
            }

            // C. Match on UninstallString containing the target directory
            if (!string.IsNullOrWhiteSpace(normDir))
            {
                var matchByUninst = apps.FirstOrDefault(a =>
                    (!string.IsNullOrWhiteSpace(a.UninstallString) && a.UninstallString.ToLowerInvariant().Contains(normDir)) ||
                    (!string.IsNullOrWhiteSpace(a.QuietUninstallString) && a.QuietUninstallString.ToLowerInvariant().Contains(normDir)));

                if (matchByUninst != null) return matchByUninst;
            }

            // D. Kısayol adı ↔ program adı. Önce birebir eşitlik; önek eşleşmeleri yalnızca
            //    TEK aday varsa kabul edilir ("Microsoft Edge" kısayolu "Microsoft Edge WebView2
            //    Runtime" ile eşleşmemeli).
            if (!string.IsNullOrWhiteSpace(normShortcut) && normShortcut.Length >= 3)
            {
                var exact = apps.FirstOrDefault(a => a.DisplayName.Equals(normShortcut, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;

                var prefix = apps.Where(a =>
                    a.DisplayName.ToLowerInvariant().StartsWith(normShortcut + " ") ||
                    normShortcut.StartsWith(a.DisplayName.ToLowerInvariant() + " ")).ToList();
                if (prefix.Count == 1) return prefix[0];
            }

            return null;
        }

        private static InstalledAppItem SynthesizeHeuristicApp(string targetExePath, string targetDir, string fallbackName)
        {
            string displayName = fallbackName;
            string publisher = "Bilinmeyen Yayıncı";
            string version = "1.0.0";
            long sizeBytes = 0;

            if (File.Exists(targetExePath))
            {
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(targetExePath);
                    if (!string.IsNullOrWhiteSpace(vi.FileDescription))
                    {
                        displayName = vi.FileDescription.Trim();
                    }
                    if (!string.IsNullOrWhiteSpace(vi.CompanyName))
                    {
                        publisher = vi.CompanyName.Trim();
                    }
                    if (!string.IsNullOrWhiteSpace(vi.FileVersion))
                    {
                        version = vi.FileVersion.Trim();
                    }
                }
                catch { }
            }

            if (Directory.Exists(targetDir))
            {
                sizeBytes = CalculateFolderSizeSafe(targetDir);
            }

            // Scan directory for common uninstaller binaries
            string uninstallerCmd = string.Empty;
            if (Directory.Exists(targetDir))
            {
                string[] uninstCandidates = new[]
                {
                    "unins000.exe", "unins001.exe", "uninstall.exe", "uninst.exe"
                };

                foreach (var candidate in uninstCandidates)
                {
                    string candidatePath = Path.Combine(targetDir, candidate);
                    if (File.Exists(candidatePath))
                    {
                        uninstallerCmd = $"\"{candidatePath}\"";
                        break;
                    }
                }
            }

            // Taşınabilir program: klasör yalnızca güvenli kapsamdaysa (ör. "İndirilenler\\Araç")
            // kurulum klasörü sayılır. İndirilenler/Masaüstü gibi kullanıcı klasörleri ASLA
            // program klasörü sayılmaz; aksi halde klasörün tamamı silinmeye önerilirdi.
            string safeInstallLocation = PathSafetyGuard.Default.CheckDeletion(targetDir, isDirectory: true).IsAllowed
                ? targetDir
                : string.Empty;

            return new InstalledAppItem
            {
                DisplayName = displayName,
                Publisher = publisher,
                DisplayVersion = version,
                InstallLocation = safeInstallLocation,
                DisplayIconPath = targetExePath,
                UninstallString = uninstallerCmd,
                EstimatedSizeBytes = sizeBytes,
                FormattedSize = FormatBytes(sizeBytes),
                IconSource = ExtractIconSafe(targetExePath),
                InstallerKind = InstallerType.GenericExe,
                IsSystemComponent = false
            };
        }

        private static readonly string[] NonMainExePrefixes =
        {
            "unins", "uninst", "setup", "install", "update", "updater", "crash", "helper", "service", "elevate", "vc_redist", "dotnet"
        };

        /// <summary>
        /// Klasördeki "ana" exe: kaldırıcı/kurulum/güncelleyici değil, adı klasöre en çok
        /// benzeyen; eşitlikte en büyük dosya. (Eskiden ilk bulunan exe seçiliyordu — bu
        /// unins000.exe bile olabiliyordu.)
        /// </summary>
        private static string? PickMainExecutable(string dir)
        {
            try
            {
                string folderName = Path.GetFileName(dir.TrimEnd('\\')).ToLowerInvariant();
                return Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly)
                    .Select(f => new FileInfo(f))
                    .Where(f => !NonMainExePrefixes.Any(p => f.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(f => folderName.Contains(Path.GetFileNameWithoutExtension(f.Name).ToLowerInvariant()) ||
                                            Path.GetFileNameWithoutExtension(f.Name).ToLowerInvariant().Contains(folderName))
                    .ThenByDescending(f => f.Length)
                    .Select(f => f.FullName)
                    .FirstOrDefault();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static string? ResolveShortcutTarget(string shortcutPath) => Bakım.Helpers.ShellLink.ResolveTarget(shortcutPath);

        private static ImageSource? ExtractIconSafe(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;

            try
            {
                using var ico = Icon.ExtractAssociatedIcon(filePath);
                if (ico != null)
                {
                    using var bmp = ico.ToBitmap();
                    var hBmp = bmp.GetHbitmap();
                    try
                    {
                        var wpfBmp = Imaging.CreateBitmapSourceFromHBitmap(
                            hBmp,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        wpfBmp.Freeze();
                        return wpfBmp;
                    }
                    finally
                    {
                        DeleteObject(hBmp);
                    }
                }
            }
            catch { }

            return null;
        }

        private static long CalculateFolderSizeSafe(string dirPath)
        {
            try
            {
                var dir = new DirectoryInfo(dirPath);
                return dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
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
    }
}
