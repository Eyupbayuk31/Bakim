using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IStartupService
    {
        Task<List<StartupProgramItem>> GetStartupProgramsAsync();
        Task<bool> SetStartupProgramStateAsync(StartupProgramItem item, bool enable);
        void OpenFileLocation(string rawFilePath);
    }

    public class StartupService : IStartupService
    {
        public async Task<List<StartupProgramItem>> GetStartupProgramsAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<StartupProgramItem>();

                // 1. HKCU Run Registry Key
                ReadRegistryKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", true, list);

                // 2. HKLM Run Registry Key
                ReadRegistryKey(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", false, list);

                // 3. User Startup Folder (.lnk / executable shortcuts)
                ReadStartupFolder(list);

                // Açılış Etkisi Sıralaması: En yüksek etkiden düşüğe doğru sırala
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
                    if (item.RegistryPath.Contains("Klasör") || item.RegistryPath.Contains("Startup Folder"))
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
                    string approvedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

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
                catch (UnauthorizedAccessException)
                {
                    // Yetki yetersiz (UAC)
                }
                catch (Exception)
                {
                    // Savunmacı programlama
                }

                return false;
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
            catch (Exception)
            {
                // Explorer açılamazsa sessizce geç
            }
        }

        private static void ReadStartupFolder(List<StartupProgramItem> list)
        {
            try
            {
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(startupFolder))
                {
                    var dir = new DirectoryInfo(startupFolder);
                    foreach (var file in dir.EnumerateFiles())
                    {
                        bool isDisabled = file.Extension.Equals(".disabled", StringComparison.OrdinalIgnoreCase);
                        string realName = isDisabled 
                            ? Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file.Name)) 
                            : Path.GetFileNameWithoutExtension(file.Name);

                        var (level, text, brush) = CalculateImpact(realName, file.FullName);

                        list.Add(new StartupProgramItem
                        {
                            Name = realName,
                            FilePath = file.FullName,
                            RegistryPath = "Başlangıç Klasörü",
                            IsCurrentUser = true,
                            IsEnabled = !isDisabled,
                            StatusText = isDisabled ? "Devre Dışı" : "Etkin",
                            ImpactLevel = level,
                            ImpactText = text,
                            ImpactBadgeBrush = brush
                        });
                    }
                }
            }
            catch (Exception) { }
        }

        private static void ReadRegistryKey(RegistryKey root, string subKey, bool isCurrentUser, List<StartupProgramItem> list)
        {
            try
            {
                using var key = root.OpenSubKey(subKey, false);
                if (key == null) return;

                // StartupApproved key üzerinden aktiflik durumunu oku
                string approvedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
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
                            // 0x02 = Etkin, 0x03 veya farklıysa = Devre Dışı
                            isEnabled = (binVal[0] == 0x02);
                        }
                    }

                    var (level, text, brush) = CalculateImpact(valueName, command);

                    list.Add(new StartupProgramItem
                    {
                        Name = valueName,
                        FilePath = command,
                        RegistryPath = $"{root.Name}\\{subKey}",
                        IsCurrentUser = isCurrentUser,
                        IsEnabled = isEnabled,
                        StatusText = isEnabled ? "Etkin" : "Devre Dışı",
                        ImpactLevel = level,
                        ImpactText = text,
                        ImpactBadgeBrush = brush
                    });
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Sysinternals / Windows Task Manager kurallarına göre başlangıç etkisini hesaplar.
        /// </summary>
        private static (int level, string text, string brush) CalculateImpact(string name, string filePath)
        {
            string combined = $"{name} {filePath}".ToLowerInvariant();

            // Yüksek Etki: Ağır istemciler, oyun başlatıcıları, tarayıcılar, bulut senkronizasyonları
            string[] highImpactKeywords =
            {
                "discord", "spotify", "steam", "epic", "teams", "slack", "chrome",
                "firefox", "edge", "onedrive", "adobe", "creative cloud", "dropbox",
                "zoom", "torrent", "skype", "viber", "riot", "battle.net", "origin"
            };

            foreach (var kw in highImpactKeywords)
            {
                if (combined.Contains(kw))
                {
                    return (3, "Yüksek Etki (>1000ms)", "SystemFillColorCriticalBrush");
                }
            }

            // Orta Etki: Donanım yardımcı araçları, ses/grafik panelleri, koruma servisleri
            string[] mediumImpactKeywords =
            {
                "realtek", "nvidia", "amd", "intel", "logitech", "razer", "corsair",
                "security", "defender", "antivirus", "service", "host", "audio", "sound"
            };

            foreach (var kw in mediumImpactKeywords)
            {
                if (combined.Contains(kw))
                {
                    return (2, "Orta Etki (300-1000ms)", "SystemFillColorCautionBrush");
                }
            }

            // Düşük Etki
            return (1, "Düşük Etki (<300ms)", "SystemFillColorSuccessBrush");
        }

        private static string CleanExecutablePath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string s = raw.Trim();

            // Tırnak içindeyse tırnakları ayıkla
            if (s.StartsWith("\""))
            {
                int endQuote = s.IndexOf('"', 1);
                if (endQuote > 1)
                {
                    return s.Substring(1, endQuote - 1);
                }
            }

            // .exe'den sonrasındaki parametreleri temizle
            int exeIdx = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx > 0)
            {
                return s.Substring(0, exeIdx + 4).Trim('\"');
            }

            return s.Split(' ')[0].Trim('\"');
        }
    }
}
