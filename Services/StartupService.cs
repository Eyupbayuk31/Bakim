using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IStartupService
    {
        Task<List<StartupProgramItem>> GetStartupProgramsAsync();
        Task<bool> SetStartupProgramStateAsync(StartupProgramItem item, bool enable);
        Task<bool> DeleteStartupProgramAsync(StartupProgramItem item);
        Task<bool> AddNewStartupProgramAsync(string name, string executablePath);
        void OpenFileLocation(string rawFilePath);
    }

    public class StartupService : IStartupService
    {
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        private static readonly ConcurrentDictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

        public async Task<List<StartupProgramItem>> GetStartupProgramsAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<StartupProgramItem>();

                // 1. HKCU Run Registry Key
                ReadRegistryKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", true, "Kayıt Defteri (HKCU)", list);

                // 2. HKLM Run Registry Key (64-Bit)
                ReadRegistryKey(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", false, "Kayıt Defteri (HKLM)", list);

                // 3. HKLM WOW6432Node Run Key (32-Bit apps on 64-Bit Windows)
                ReadRegistryKey(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", false, "32-Bit Kayıt Defteri (WOW64)", list);

                // 4. User Startup Folder
                ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Kullanıcı Başlangıç Klasörü", true, list);

                // 5. Common Startup Folder (All Users)
                ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Ortak Başlangıç Klasörü", false, list);

                // Sıralama: En yüksek açılış etkisinden düşüğe ve isme göre
                return list.OrderByDescending(p => p.ImpactLevel).ThenBy(p => p.Name).ToList();
            });
        }

        public async Task<bool> SetStartupProgramStateAsync(StartupProgramItem item, bool enable)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 1. Başlangıç Klasörü Kontrolü
                    if (item.LocationType.Contains("Klasör") || item.RegistryPath.Contains("Klasör"))
                    {
                        string currentPath = item.FilePath;
                        if (enable)
                        {
                            if (currentPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                            {
                                string targetPath = currentPath.Substring(0, currentPath.Length - ".disabled".Length);
                                File.Move(currentPath, targetPath);
                                item.FilePath = targetPath;
                            }
                        }
                        else
                        {
                            if (!currentPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) && File.Exists(currentPath))
                            {
                                string targetPath = currentPath + ".disabled";
                                File.Move(currentPath, targetPath);
                                item.FilePath = targetPath;
                            }
                        }

                        item.IsEnabled = enable;
                        item.StatusText = enable ? "Etkin" : "Devre Dışı";
                        return true;
                    }

                    // 2. Kayıt Defteri (StartupApproved\Run)
                    RegistryKey root = item.IsCurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
                    string approvedKeyPath = item.RegistryPath.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase)
                        ? @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
                        : @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

                    using var approvedKey = root.OpenSubKey(approvedKeyPath, true) ?? root.CreateSubKey(approvedKeyPath);
                    if (approvedKey != null)
                    {
                        byte[] existing = approvedKey.GetValue(item.Name) as byte[] ?? new byte[12];
                        if (existing.Length < 12)
                        {
                            Array.Resize(ref existing, 12);
                        }

                        // Windows standardı: 02 = Enabled, 03 = Disabled
                        existing[0] = enable ? (byte)0x02 : (byte)0x03;
                        approvedKey.SetValue(item.Name, existing, RegistryValueKind.Binary);

                        item.IsEnabled = enable;
                        item.StatusText = enable ? "Etkin" : "Devre Dışı";
                        return true;
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (Exception) { }

                return false;
            });
        }

        public async Task<bool> DeleteStartupProgramAsync(StartupProgramItem item)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 1. Başlangıç klasörü ise doğrudan kısayolu sil
                    if (item.LocationType.Contains("Klasör") || item.RegistryPath.Contains("Klasör"))
                    {
                        if (File.Exists(item.FilePath))
                        {
                            File.Delete(item.FilePath);
                            return true;
                        }
                        if (File.Exists(item.FilePath + ".disabled"))
                        {
                            File.Delete(item.FilePath + ".disabled");
                            return true;
                        }
                        return false;
                    }

                    // 2. Kayıt Defteri ise Run anahtarından sil
                    RegistryKey root = item.IsCurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
                    string subKeyPath = item.RegistryPath.Replace($"{root.Name}\\", "");

                    using (var key = root.OpenSubKey(subKeyPath, true))
                    {
                        key?.DeleteValue(item.Name, false);
                    }

                    // Onay anahtarından da temizle
                    string approvedKeyPath = item.RegistryPath.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase)
                        ? @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
                        : @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

                    using (var approvedKey = root.OpenSubKey(approvedKeyPath, true))
                    {
                        approvedKey?.DeleteValue(item.Name, false);
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> AddNewStartupProgramAsync(string name, string executablePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                        return false;

                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    if (key == null) return false;

                    key.SetValue(name, $"\"{executablePath}\"");

                    string approvedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
                    using var approvedKey = Registry.CurrentUser.OpenSubKey(approvedKeyPath, true) ?? Registry.CurrentUser.CreateSubKey(approvedKeyPath);
                    approvedKey?.SetValue(name, new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public void OpenFileLocation(string rawFilePath)
        {
            try
            {
                string cleanPath = CleanExecutablePath(rawFilePath);
                if (File.Exists(cleanPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{cleanPath}\"",
                        UseShellExecute = true
                    });
                    return;
                }

                string? dir = Path.GetDirectoryName(cleanPath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{dir}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch { }
        }

        private static void ReadStartupFolder(string folderPath, string locationType, bool isCurrentUser, List<StartupProgramItem> list)
        {
            try
            {
                if (!Directory.Exists(folderPath)) return;

                var dir = new DirectoryInfo(folderPath);
                foreach (var file in dir.EnumerateFiles())
                {
                    if (file.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool isDisabled = file.Extension.Equals(".disabled", StringComparison.OrdinalIgnoreCase);
                    string realName = isDisabled
                        ? Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file.Name))
                        : Path.GetFileNameWithoutExtension(file.Name);

                    string cleanTarget = CleanExecutablePath(file.FullName);
                    bool exists = File.Exists(cleanTarget) || File.Exists(file.FullName);

                    var (level, text, brush, delay) = CalculateImpact(realName, file.FullName);
                    var icon = GetFileIcon(cleanTarget);
                    var publisher = GetPublisher(cleanTarget);

                    list.Add(new StartupProgramItem
                    {
                        Name = realName,
                        FilePath = file.FullName,
                        CleanExePath = cleanTarget,
                        RegistryPath = locationType,
                        LocationType = locationType,
                        Publisher = publisher,
                        IsCurrentUser = isCurrentUser,
                        IsEnabled = !isDisabled,
                        FileExists = exists,
                        IconSource = icon,
                        StatusText = isDisabled ? "Devre Dışı" : "Etkin",
                        ImpactLevel = level,
                        ImpactText = text,
                        ImpactBadgeBrush = brush,
                        EstimatedDelayText = delay
                    });
                }
            }
            catch { }
        }

        private static void ReadRegistryKey(RegistryKey root, string subKey, bool isCurrentUser, string locationType, List<StartupProgramItem> list)
        {
            try
            {
                using var key = root.OpenSubKey(subKey, false);
                if (key == null) return;

                string approvedKeyPath = subKey.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase)
                    ? @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
                    : @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

                using var approvedKey = root.OpenSubKey(approvedKeyPath, false);

                foreach (var valueName in key.GetValueNames())
                {
                    if (string.IsNullOrWhiteSpace(valueName)) continue;

                    string command = key.GetValue(valueName)?.ToString() ?? string.Empty;
                    bool isEnabled = true;

                    if (approvedKey != null)
                    {
                        var binVal = approvedKey.GetValue(valueName) as byte[];
                        if (binVal != null && binVal.Length > 0)
                        {
                            isEnabled = (binVal[0] == 0x02);
                        }
                    }

                    string cleanPath = CleanExecutablePath(command);
                    bool exists = File.Exists(cleanPath);

                    var (level, text, brush, delay) = CalculateImpact(valueName, command);
                    var icon = GetFileIcon(cleanPath);
                    var publisher = GetPublisher(cleanPath);

                    list.Add(new StartupProgramItem
                    {
                        Name = valueName,
                        FilePath = command,
                        CleanExePath = cleanPath,
                        RegistryPath = $"{root.Name}\\{subKey}",
                        LocationType = locationType,
                        Publisher = publisher,
                        IsCurrentUser = isCurrentUser,
                        IsEnabled = isEnabled,
                        FileExists = exists,
                        IconSource = icon,
                        StatusText = isEnabled ? "Etkin" : "Devre Dışı",
                        ImpactLevel = level,
                        ImpactText = text,
                        ImpactBadgeBrush = brush,
                        EstimatedDelayText = delay
                    });
                }
            }
            catch { }
        }

        private static ImageSource? GetFileIcon(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            if (_iconCache.TryGetValue(filePath, out var cached))
                return cached;

            try
            {
                using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
                if (sysIcon != null)
                {
                    using var bitmap = sysIcon.ToBitmap();
                    var hBitmap = bitmap.GetHbitmap();
                    try
                    {
                        var wpfBmp = Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        wpfBmp.Freeze();
                        _iconCache[filePath] = wpfBmp;
                        return wpfBmp;
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
            }
            catch { }

            _iconCache[filePath] = null;
            return null;
        }

        private static string GetPublisher(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return "Bilinmeyen Yayıncı";

            try
            {
                var vi = FileVersionInfo.GetVersionInfo(filePath);
                if (!string.IsNullOrWhiteSpace(vi.CompanyName))
                    return vi.CompanyName.Trim();
                if (!string.IsNullOrWhiteSpace(vi.ProductName))
                    return vi.ProductName.Trim();
            }
            catch { }

            return "Bilinmeyen Yayıncı";
        }

        private static (int level, string text, string brush, string delay) CalculateImpact(string name, string filePath)
        {
            string combined = $"{name} {filePath}".ToLowerInvariant();

            // Yüksek Etki: Ağır istemciler, oyun başlatıcıları, tarayıcılar, bulut senkronizasyonları
            string[] highImpactKeywords =
            {
                "discord", "spotify", "steam", "epic", "teams", "slack", "chrome",
                "firefox", "edge", "onedrive", "adobe", "creative cloud", "dropbox",
                "zoom", "torrent", "skype", "viber", "riot", "battle.net", "origin",
                "overwolf", "blitz", "medal", "curseforge"
            };

            foreach (var kw in highImpactKeywords)
            {
                if (combined.Contains(kw))
                {
                    return (3, "Yüksek Etki (>1000ms)", "SystemFillColorCriticalBrush", "~1.5 sn");
                }
            }

            // Orta Etki: Donanım yardımcı araçları, ses/grafik panelleri, koruma servisleri
            string[] mediumImpactKeywords =
            {
                "realtek", "nvidia", "amd", "intel", "logitech", "razer", "corsair",
                "security", "defender", "antivirus", "service", "host", "audio", "sound",
                "rtkaud", "noisesuppression"
            };

            foreach (var kw in mediumImpactKeywords)
            {
                if (combined.Contains(kw))
                {
                    return (2, "Orta Etki (300-1000ms)", "SystemFillColorCautionBrush", "~0.6 sn");
                }
            }

            // Düşük Etki
            return (1, "Düşük Etki (<300ms)", "SystemFillColorSuccessBrush", "~0.2 sn");
        }

        private static string CleanExecutablePath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string s = raw.Trim();

            if (s.StartsWith("\""))
            {
                int endQuote = s.IndexOf('"', 1);
                if (endQuote > 1)
                {
                    return s.Substring(1, endQuote - 1);
                }
            }

            int exeIdx = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx > 0)
            {
                return s.Substring(0, exeIdx + 4).Trim('\"');
            }

            int lnkIdx = s.IndexOf(".lnk", StringComparison.OrdinalIgnoreCase);
            if (lnkIdx > 0)
            {
                return s.Substring(0, lnkIdx + 4).Trim('\"');
            }

            return s.Split(' ')[0].Trim('\"');
        }
    }
}
