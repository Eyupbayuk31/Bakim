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

                // 1. Resolve .lnk shortcut if needed
                if (cleanedPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    string? resolvedTarget = ResolveShortcutTarget(cleanedPath);
                    if (!string.IsNullOrWhiteSpace(resolvedTarget) && (File.Exists(resolvedTarget) || Directory.Exists(resolvedTarget)))
                    {
                        targetExePath = resolvedTarget;
                    }
                }

                // If target is a directory
                if (Directory.Exists(targetExePath))
                {
                    targetDir = targetExePath;
                    // Try to find a main executable in the folder
                    var exeFiles = Directory.GetFiles(targetDir, "*.exe", SearchOption.TopDirectoryOnly);
                    if (exeFiles.Length > 0)
                    {
                        targetExePath = exeFiles[0];
                    }
                }
                else if (File.Exists(targetExePath))
                {
                    targetDir = Path.GetDirectoryName(targetExePath) ?? string.Empty;
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

        private static InstalledAppItem? FindMatchingApp(
            System.Collections.Generic.List<InstalledAppItem> apps,
            string targetExePath,
            string targetDir,
            string shortcutName)
        {
            string normExe = targetExePath.TrimEnd('\\').ToLowerInvariant();
            string normDir = targetDir.TrimEnd('\\').ToLowerInvariant();
            string normShortcut = shortcutName.Trim().ToLowerInvariant();

            // A. Exact or prefix match on InstallLocation
            if (!string.IsNullOrWhiteSpace(normDir))
            {
                var matchByLoc = apps.FirstOrDefault(a =>
                    !string.IsNullOrWhiteSpace(a.InstallLocation) &&
                    (normDir.Equals(a.InstallLocation.TrimEnd('\\').ToLowerInvariant()) ||
                     normDir.StartsWith(a.InstallLocation.TrimEnd('\\').ToLowerInvariant() + "\\") ||
                     a.InstallLocation.TrimEnd('\\').ToLowerInvariant().StartsWith(normDir + "\\")));

                if (matchByLoc != null) return matchByLoc;
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

            // D. Match by Shortcut Name against DisplayName
            if (!string.IsNullOrWhiteSpace(normShortcut) && normShortcut.Length >= 3)
            {
                var matchByName = apps.FirstOrDefault(a =>
                    a.DisplayName.Equals(normShortcut, StringComparison.OrdinalIgnoreCase) ||
                    a.DisplayName.ToLowerInvariant().StartsWith(normShortcut + " ") ||
                    normShortcut.StartsWith(a.DisplayName.ToLowerInvariant() + " "));

                if (matchByName != null) return matchByName;
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
                    "unins000.exe", "unins001.exe", "uninstall.exe", "uninst.exe", "setup.exe"
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

            return new InstalledAppItem
            {
                DisplayName = displayName,
                Publisher = publisher,
                DisplayVersion = version,
                InstallLocation = targetDir,
                DisplayIconPath = targetExePath,
                UninstallString = uninstallerCmd,
                EstimatedSizeBytes = sizeBytes,
                FormattedSize = FormatBytes(sizeBytes),
                IconSource = ExtractIconSafe(targetExePath),
                InstallerKind = InstallerType.GenericExe,
                IsSystemComponent = false
            };
        }

        private static string? ResolveShortcutTarget(string shortcutPath)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic shell = Activator.CreateInstance(shellType)!;
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    return shortcut.TargetPath;
                }
            }
            catch { }
            return null;
        }

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
