using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Bakım.Models;
using Bakım.Core.Security;
using Bakım.Helpers;

namespace Bakım.Services
{
    /// <summary>Yönetici kısayolu oluşturma sonucu.</summary>
    public sealed record ElevatedShortcutResult(bool Created, string Message)
    {
        public static ElevatedShortcutResult Fail(string message) => new(false, message);
    }

    public interface IContextMenuShortcutsService
    {
        Task<List<SystemTweakItem>> GetContextMenuShortcutsTweaksAsync();
        Task<bool> ApplyTweakAsync(SystemTweakItem tweak, bool enable);
        Task<ElevatedShortcutResult> CreateElevatedShortcutAsync(string targetExePath, string shortcutName);
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
                using var writes = WriteScope.Begin();
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

                    if (!writes.Succeeded)
                    {
                        tweak.LastError = writes.Describe();
                        return false;
                    }

                    tweak.LastError = null;
                    tweak.IsEnabled = enable;
                    return true;
                }
                catch (Exception ex)
                {
                    tweak.LastError = ex.Message;
                    return false;
                }
            });
        }

        /// <summary>
        /// UAC sormadan yönetici olarak açılan kısayol: en yüksek yetkili bir zamanlanmış görev
        /// ve onu tetikleyen bir .lnk (S-11).
        ///
        /// Güvenlik: görev UAC'yi atladığı için hedefi standart bir kullanıcı değiştirebiliyorsa bu
        /// bir yetki yükseltme açığı olur. Bu yüzden hedef yalnızca Program Files / Windows altında
        /// olabilir ve dosyanın, üst klasörlerinin sahibi ve yazma izni olanlar yalnızca SYSTEM,
        /// Administrators ve TrustedInstaller olmalıdır. Eskiden her exe kabul ediliyor ve
        /// masaüstüne bir .vbs yazılıyordu (VBScript Windows'tan kaldırılıyor).
        /// </summary>
        public async Task<ElevatedShortcutResult> CreateElevatedShortcutAsync(string targetExePath, string shortcutName)
        {
            return await Task.Run(() =>
            {
                string target;
                try
                {
                    target = Path.GetFullPath((targetExePath ?? string.Empty).Trim().Trim('"'));
                }
                catch
                {
                    return ElevatedShortcutResult.Fail("Geçersiz dosya yolu.");
                }

                if (!File.Exists(target))
                    return ElevatedShortcutResult.Fail("Dosya bulunamadı.");
                if (!string.Equals(Path.GetExtension(target), ".exe", StringComparison.OrdinalIgnoreCase))
                    return ElevatedShortcutResult.Fail("Yalnızca .exe dosyaları için kısayol oluşturulabilir.");

                string? refusal = CheckElevationTarget(target);
                if (refusal != null) return ElevatedShortcutResult.Fail(refusal);

                string cleanName = string.IsNullOrWhiteSpace(shortcutName)
                    ? Path.GetFileNameWithoutExtension(target)
                    : shortcutName.Trim();
                foreach (char c in Path.GetInvalidFileNameChars()) cleanName = cleanName.Replace(c, '_');
                cleanName = cleanName.Trim(' ', '.');
                if (cleanName.Length == 0) cleanName = Path.GetFileNameWithoutExtension(target);

                string taskName = $"Bakim_Elevated_{cleanName}";

                var create = ProcessRunner.Run("schtasks.exe", new[]
                {
                    "/create", "/tn", taskName, "/tr", $"\"{target}\"", "/sc", "ONCE", "/st", "00:00", "/rl", "HIGHEST", "/f"
                }, TimeSpan.FromSeconds(30));
                if (!create.Succeeded)
                {
                    string reason = create.Describe();
                    if (!UacHelper.IsAdministrator()) reason = "Bakım'ı yönetici olarak çalıştırın (" + reason + ")";
                    return ElevatedShortcutResult.Fail($"Zamanlanmış görev oluşturulamadı: {reason}");
                }

                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string linkPath = Path.Combine(desktop, $"{cleanName} (Yönetici).lnk");
                try
                {
                    ShellLink.Create(linkPath,
                        Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                        $"/run /tn \"{taskName}\"",
                        target,
                        $"{cleanName} programını yönetici olarak başlatır (Bakım).",
                        ShellLink.ShowMinimizedNoActive);
                }
                catch (Exception ex)
                {
                    ProcessRunner.Run("schtasks.exe", new[] { "/delete", "/tn", taskName, "/f" }, TimeSpan.FromSeconds(15));
                    return ElevatedShortcutResult.Fail($"Kısayol dosyası yazılamadı: {ex.Message}");
                }

                return new ElevatedShortcutResult(true, $"'{Path.GetFileName(linkPath)}' masaüstüne oluşturuldu.");
            });
        }

        /// <summary>Hedef güvenliyse null, değilse kullanıcıya gösterilecek ret nedeni.</summary>
        private static string? CheckElevationTarget(string target)
        {
            string? root = ElevationTargetPolicy.FindAllowedRoot(target, new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            });
            if (root == null)
            {
                return "Güvenlik nedeniyle yalnızca Program Files ya da Windows klasöründeki programlar için " +
                       "UAC'siz kısayol oluşturulabilir. Kullanıcı klasörlerindeki bir programı herhangi bir " +
                       "zararlı değiştirip yönetici yetkisi kazanabilir.";
            }

            foreach (string path in ElevationTargetPolicy.ChainToRoot(target, root))
            {
                try
                {
                    if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                        return $"'{path}' bir sembolik bağlantı/bağlantı noktası; bu tür yollar desteklenmez.";
                }
                catch (Exception ex)
                {
                    return $"'{path}' okunamadı: {ex.Message}";
                }

                var snapshot = FileSecurityReader.TryRead(path);
                if (snapshot == null)
                    return $"'{path}' için izinler okunamadı; güvenli olduğu doğrulanamadı.";

                string? writer = ElevationTargetPolicy.FindUntrustedWriter(snapshot);
                if (writer != null)
                {
                    return $"'{path}' konumunu {FileSecurityReader.DescribeSid(writer)} değiştirebiliyor. " +
                           "Bu programa UAC'siz yönetici kısayolu vermek, yönetici yetkisini o hesaptaki her " +
                           "programa açmak anlamına gelir; bu yüzden kısayol oluşturulmadı.";
                }
            }
            return null;
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

        private static bool SetRegistryString(RegistryKey root, string subKey, string valueName, string value) =>
            VerifiedRegistry.SetString(root, subKey, valueName, value);

        private static bool DeleteRegistryValue(RegistryKey root, string subKey, string valueName) =>
            VerifiedRegistry.DeleteValue(root, subKey, valueName);

        private static bool DeleteRegistryKey(RegistryKey root, string subKey) =>
            VerifiedRegistry.DeleteKeyTree(root, subKey);

        #endregion
    }
}
