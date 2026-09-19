using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IContextMenuShortcutsService
    {
        Task<List<SystemTweakItem>> GetContextMenuShortcutsTweaksAsync();
        Task<bool> ApplyTweakAsync(SystemTweakItem tweak, bool enable);
        Task<bool> CreateElevatedShortcutAsync(string targetExePath, string shortcutName);
        Task<bool> ApplyAllRecommendedAsync(List<SystemTweakItem> tweaks);
        Task<bool> RestoreDefaultsAsync(List<SystemTweakItem> tweaks);
    }

    public class ContextMenuShortcutsService : IContextMenuShortcutsService
    {
        public async Task<List<SystemTweakItem>> GetContextMenuShortcutsTweaksAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<SystemTweakItem>
                {
                    new()
                    {
                        Id = "disable_shortcut_suffix",
                        Category = "Sağ Tık & Kısayollar",
                        Title = "Yeni Kısayollardaki '- Kısayol' Metin Ekini Kaldır",
                        Description = "Masaüstünde veya klasörlerde yeni bir kısayol oluşturulduğunda dosya adının sonuna otomatik eklenen '- Kısayol' yazısını engeller.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "Cut24",
                        IsEnabled = CheckShortcutSuffixDisabled()
                    },
                    new()
                    {
                        Id = "remove_shortcut_arrow",
                        Category = "Sağ Tık & Kısayollar",
                        Title = "Kısayol Simgelerinin Üzerindeki Ok İşaretini Kaldır",
                        Description = "Kısayol ikonlarının sol alt köşesinde beliren küçük kıvrımlı ok simgesini kaldırarak temiz ve şeffaf bir görünüm sağlar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IconSymbol = "ArrowCurveDownLeft24",
                        IsEnabled = CheckShortcutArrowRemoved()
                    },
                    new()
                    {
                        Id = "context_install_cab",
                        Category = "Sağ Tık & Kısayollar",
                        Title = "CAB Dosyaları İçin Sağ Tık Menüsüne 'Yükle' (Install) Ekle",
                        Description = "Windows güncelleme ve sürücü paketleri olan .cab dosyalarına sağ tıklandığında DISM ile doğrudan kurulum yapma seçeneği ekler.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "Apps24",
                        IsEnabled = CheckRegistryKeyExists(Registry.ClassesRoot, @"CABFolder\Shell\Install")
                    },
                    new()
                    {
                        Id = "context_powershell_admin",
                        Category = "Sağ Tık & Kısayollar",
                        Title = "PowerShell (.ps1) Dosyalarına 'Yönetici Olarak Çalıştır' Ekle",
                        Description = ".ps1 PowerShell betiklerine sağ tıklandığında ExecutionPolicy kısıtlamasına takılmadan Yönetici yetkisiyle doğrudan başlatır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "ShieldCheckmark24",
                        IsEnabled = CheckRegistryKeyExists(Registry.ClassesRoot, @"Microsoft.PowerShellScript.1\Shell\runas\command")
                    },
                    new()
                    {
                        Id = "context_take_ownership",
                        Category = "Sağ Tık & Kısayollar",
                        Title = "Dosya ve Klasörler İçin 'Sahipliğini Al (Take Ownership)' Ekle",
                        Description = "Erişimi kilitli veya korumalı dosya/klasörlere sağ tıklandığında tek tıkla NTFS sahipliğini ve Tam Yetkiyi (Full Control) yöneticilere devreder.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "Key24",
                        IsEnabled = CheckRegistryKeyExists(Registry.ClassesRoot, @"*\shell\runas_takeownership")
                    }
                };

                return list;
            });
        }

        public async Task<bool> ApplyTweakAsync(SystemTweakItem tweak, bool enable)
        {
            return await Task.Run(() =>
            {
                try
                {
                    switch (tweak.Id)
                    {
                        case "disable_shortcut_suffix":
                            if (enable)
                            {
                                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer", true);
                                key?.SetValue("link", new byte[] { 0, 0, 0, 0 }, RegistryValueKind.Binary);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "link");
                            }
                            break;

                        case "remove_shortcut_arrow":
                            if (enable)
                            {
                                SetRegistryString(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons", "29", @"%SystemRoot%\System32\shell32.dll,-50");
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons", "29");
                            }
                            break;

                        case "context_install_cab":
                            if (enable)
                            {
                                SetRegistryString(Registry.ClassesRoot, @"CABFolder\Shell\Install", "", "Yükle (Install)");
                                SetRegistryString(Registry.ClassesRoot, @"CABFolder\Shell\Install", "HasLUAShield", "");
                                SetRegistryString(Registry.ClassesRoot, @"CABFolder\Shell\Install\command", "", "dism.exe /online /add-package /packagepath:\"%1\"");
                            }
                            else
                            {
                                DeleteRegistryKey(Registry.ClassesRoot, @"CABFolder\Shell\Install");
                            }
                            break;

                        case "context_powershell_admin":
                            if (enable)
                            {
                                SetRegistryString(Registry.ClassesRoot, @"Microsoft.PowerShellScript.1\Shell\runas", "", "PowerShell ile Yönetici Olarak Çalıştır");
                                SetRegistryString(Registry.ClassesRoot, @"Microsoft.PowerShellScript.1\Shell\runas", "HasLUAShield", "");
                                SetRegistryString(Registry.ClassesRoot, @"Microsoft.PowerShellScript.1\Shell\runas\command", "", "powershell.exe \"-Command\" \"if((Get-ExecutionPolicy ) -ne 'AllSigned') { Set-ExecutionPolicy -Scope Process Bypass }; & '%1'\"");
                            }
                            else
                            {
                                DeleteRegistryKey(Registry.ClassesRoot, @"Microsoft.PowerShellScript.1\Shell\runas");
                            }
                            break;

                        case "context_take_ownership":
                            if (enable)
                            {
                                // Dosyalar için Take Ownership
                                SetRegistryString(Registry.ClassesRoot, @"*\shell\runas_takeownership", "", "Sahipliğini Al (Take Ownership)");
                                SetRegistryString(Registry.ClassesRoot, @"*\shell\runas_takeownership", "NoWorkingDirectory", "");
                                SetRegistryString(Registry.ClassesRoot, @"*\shell\runas_takeownership", "HasLUAShield", "");
                                SetRegistryString(Registry.ClassesRoot, @"*\shell\runas_takeownership\command", "", "cmd.exe /c takeown /f \"%1\" && icacls \"%1\" /grant administrators:F");

                                // Klasörler için Take Ownership
                                SetRegistryString(Registry.ClassesRoot, @"Directory\shell\runas_takeownership", "", "Sahipliğini Al (Take Ownership)");
                                SetRegistryString(Registry.ClassesRoot, @"Directory\shell\runas_takeownership", "NoWorkingDirectory", "");
                                SetRegistryString(Registry.ClassesRoot, @"Directory\shell\runas_takeownership", "HasLUAShield", "");
                                SetRegistryString(Registry.ClassesRoot, @"Directory\shell\runas_takeownership\command", "", "cmd.exe /c takeown /f \"%1\" /r /d y && icacls \"%1\" /grant administrators:F /t");
                            }
                            else
                            {
                                DeleteRegistryKey(Registry.ClassesRoot, @"*\shell\runas_takeownership");
                                DeleteRegistryKey(Registry.ClassesRoot, @"Directory\shell\runas_takeownership");
                            }
                            break;

                        default:
                            return false;
                    }

                    tweak.IsEnabled = enable;
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> CreateElevatedShortcutAsync(string targetExePath, string shortcutName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(targetExePath) || !File.Exists(targetExePath))
                        return false;

                    string cleanName = string.IsNullOrWhiteSpace(shortcutName)
                        ? Path.GetFileNameWithoutExtension(targetExePath)
                        : shortcutName.Trim();

                    // Özel geçersiz karakterleri temizle
                    foreach (char c in Path.GetInvalidFileNameChars())
                    {
                        cleanName = cleanName.Replace(c, '_');
                    }

                    string taskName = $"Bakim_Elevated_{cleanName}";

                    // 1. schtasks ile en yüksek yetkili (Highest) görev oluştur
                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/create /tn \"{taskName}\" /tr \"\\\"{targetExePath}\\\"\" /sc ONCE /st 00:00 /rl HIGHEST /f",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };

                    using (var proc = Process.Start(psi))
                    {
                        proc?.WaitForExit();
                        if (proc?.ExitCode != 0) return false;
                    }

                    // 2. Masaüstünde schtasks'ı tetikleyen bir VBS / Shortcut oluştur
                    string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    string vbsPath = Path.Combine(desktopPath, $"{cleanName} (Yönetici).vbs");

                    string vbsContent = $"Set WshShell = CreateObject(\"WScript.Shell\"){Environment.NewLine}" +
                                        $"WshShell.Run \"schtasks /run /tn \"\"{taskName}\"\"\", 0, False";

                    File.WriteAllText(vbsPath, vbsContent);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> ApplyAllRecommendedAsync(List<SystemTweakItem> tweaks)
        {
            bool allOk = true;
            foreach (var tweak in tweaks)
            {
                if (tweak.IsRecommended && tweak.Type == TweakType.Toggle && !tweak.IsEnabled)
                {
                    bool ok = await ApplyTweakAsync(tweak, true);
                    if (!ok) allOk = false;
                }
            }
            return allOk;
        }

        public async Task<bool> RestoreDefaultsAsync(List<SystemTweakItem> tweaks)
        {
            bool allOk = true;
            foreach (var tweak in tweaks)
            {
                if (tweak.Type == TweakType.Toggle && tweak.IsEnabled)
                {
                    bool ok = await ApplyTweakAsync(tweak, false);
                    if (!ok) allOk = false;
                }
            }
            return allOk;
        }

        #region Helpers

        private static bool CheckShortcutSuffixDisabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer");
                if (key == null) return false;
                var val = key.GetValue("link");
                if (val is byte[] bytes && bytes.Length >= 4)
                {
                    return bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckShortcutArrowRemoved()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons");
                if (key == null) return false;
                var val = key.GetValue("29")?.ToString();
                return !string.IsNullOrWhiteSpace(val);
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckRegistryKeyExists(RegistryKey root, string subKey)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        private static void SetRegistryString(RegistryKey root, string subKey, string valueName, string value)
        {
            try
            {
                using var key = root.CreateSubKey(subKey, true);
                key?.SetValue(valueName, value, RegistryValueKind.String);
            }
            catch { }
        }

        private static void DeleteRegistryValue(RegistryKey root, string subKey, string valueName)
        {
            try
            {
                using var key = root.OpenSubKey(subKey, true);
                key?.DeleteValue(valueName, false);
            }
            catch { }
        }

        private static void DeleteRegistryKey(RegistryKey root, string subKey)
        {
            try
            {
                root.DeleteSubKeyTree(subKey, false);
            }
            catch { }
        }

        #endregion
    }
}
