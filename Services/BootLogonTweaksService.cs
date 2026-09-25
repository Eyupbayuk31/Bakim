using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Bakım.Models;
using Bakım.Helpers;

namespace Bakım.Services
{
    public interface IBootLogonTweaksService
    {
        Task<List<SystemTweakItem>> GetBootLogonTweaksAsync();
        Task<bool> ApplyTweakAsync(SystemTweakItem tweak, bool enable);
        Task<bool> SetChkdskTimeoutAsync(int seconds);
        Task<bool> SetBootTimeoutAsync(int seconds);
        Task<int> ExportSpotlightImagesAsync();
        Task<bool> ApplyAllRecommendedAsync(List<SystemTweakItem> tweaks);
        Task<bool> RestoreDefaultsAsync(List<SystemTweakItem> tweaks);
    }

    public class BootLogonTweaksService : IBootLogonTweaksService
    {
        public async Task<List<SystemTweakItem>> GetBootLogonTweaksAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<SystemTweakItem>
                {
                    // 1. Auto Repair at Boot
                    new()
                    {
                        Id = "auto_repair_boot",
                        Category = "Açılış & Oturum (Boot & Logon)",
                        Title = "Açılışta Otomatik Onarım Döngüsünü Devre Dışı Bırak",
                        Description = "Açılış hatalarında Windows'un saatler süren başarısız 'Otomatik Onarım Hazırlanıyor' döngüsüne girmesini engeller.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IconSymbol = "ArrowSyncDismiss24",
                        IsEnabled = CheckAutoRepairDisabled()
                    },

                    // 2. Boot Options (BCD Timeout)
                    new()
                    {
                        Id = "boot_timeout",
                        Category = "Açılış & Oturum (Boot & Logon)",
                        Title = "Açılış (BCD Boot) Menüsü Bekleme Süresi",
                        Description = "Çoklu işletim sistemi veya güvenli mod seçim menüsünün ekranda kaç saniye bekleyeceğini yapılandırır.",
                        Type = TweakType.Numeric,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "Timer24",
                        NumericValue = GetBootTimeout(),
                        MinNumericValue = 0,
                        MaxNumericValue = 120,
                        NumericUnit = "saniye",
                        IsEnabled = true
                    },

                    // 3. Chkdsk Timeout at Boot
                    new()
                    {
                        Id = "chkdsk_timeout",
                        Category = "Açılış & Oturum (Boot & Logon)",
                        Title = "Açılışta Chkdsk Disk Denetimi Geri Sayım Süresi",
                        Description = "Sistem anormal kapandığında açılışta beliren disk denetleme geri sayım bekleme süresini saniye cinsinden ayarlar.",
                        Type = TweakType.Numeric,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "Timer24",
                        NumericValue = GetChkdskTimeout(),
                        MinNumericValue = 0,
                        MaxNumericValue = 60,
                        NumericUnit = "saniye",
                        IsEnabled = true
                    },

                    // 4. Default Lock Screen Background
                    new()
                    {
                        Id = "lock_screen_background",
                        Category = "Açılış & Oturum (Boot & Logon)",
                        Title = "Kilit Ekranı İçin Sabit Varsayılan Görsel Zorla",
                        Description = "Kilit ekranının dinamik reklam/haber resimleriyle değişmesini engelleyerek sabit Windows manzara görselini sabitler.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "Image24",
                        IsEnabled = CheckRegistryStringExists(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Personalization", "LockScreenImage")
                    },

                    // 9. Enable NumLock on Logon Screen
                    new()
                    {
                        Id = "enable_numlock_logon",
                        Category = "Açılış & Oturum (Boot & Logon)",
                        Title = "Açılışta NumLock Sayısal Tuşlarını Otomatik Etkinleştir",
                        Description = "Windows açıldığında ve oturum ekranı geldiğinde klavyedeki NumPad sayı tuşlarının her zaman açık (ON) gelmesini sağlar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "Keyboard24",
                        IsEnabled = CheckNumLockEnabled()
                    },

                    // 11. Find Lock Screen Images
                    new()
                    {
                        Id = "find_spotlight_images",
                        Category = "Açılış & Oturum (Boot & Logon)",
                        Title = "Kilit Ekranı ve Spotlight Görsellerini Dışa Aktar",
                        Description = "Windows'un arka planda indirdiği yüksek kaliteli kilit ekranı ve Spotlight duvar kağıtlarını Resimler klasörüne .jpg olarak çıkartır.",
                        Type = TweakType.Action,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "FolderOpen24",
                        IsEnabled = true
                    },

                    // 17. Sign-in Message
                    new()
                    {
                        Id = "legal_notice_message",
                        Category = "Açılış & Oturum (Boot & Logon)",
                        Title = "Giriş Öncesi Özel Karşılama ve Güvenlik Bildirimi Göster",
                        Description = "Oturum açma ekranından hemen önce ekrana kurumsal karşılama veya güvenlik/yasal uyarı diyalog kutusu getirir.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "AlertUrgent24",
                        IsEnabled = CheckLegalNoticeEnabled()
                    },

                };

                list.InsertRange(0, Bakım.Services.Tweaks.TweakEngine.ItemsFor("Açılış & Oturum (Boot & Logon)"));
                return list;
            });
        }

        public async Task<bool> ApplyTweakAsync(SystemTweakItem tweak, bool enable)
        {
            // Veri tabanlı ayarlar (Assets/tweaks/*.json) tek motordan uygulanır (MASTER_PLAN §5.16).
            if (Bakım.Services.Tweaks.TweakEngine.Handles(tweak.Id))
                return await Bakım.Services.Tweaks.TweakEngine.ApplyAsync(tweak, enable);

            return await Task.Run(() =>
            {
                using var writes = WriteScope.Begin();
                try
                {
                    switch (tweak.Id)
                    {
                        // 1. Auto Repair at Boot
                        case "auto_repair_boot":
                            RunBcdedit("/set", "{default}", "recoveryenabled", enable ? "No" : "Yes");
                            break;

                        // 4. Default Lock Screen Background
                        case "lock_screen_background":
                            if (enable)
                            {
                                SetRegistryString(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Personalization", "LockScreenImage", @"C:\Windows\Web\Screen\img100.jpg");
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Personalization", "LockScreenImage");
                            }
                            break;

                        // 9. Enable NumLock on Logon Screen
                        case "enable_numlock_logon":
                            string numVal = enable ? "2" : "2147483648";
                            SetRegistryString(Registry.Users, @".DEFAULT\Control Panel\Keyboard", "InitialKeyboardIndicators", numVal);
                            SetRegistryString(Registry.CurrentUser, @"Control Panel\Keyboard", "InitialKeyboardIndicators", numVal);
                            break;

                        // 11. Find Lock Screen Images (Action)
                        case "find_spotlight_images":
                            _ = ExportSpotlightImagesAsync();
                            return true;

                        // 17. Sign-in Message
                        case "legal_notice_message":
                            if (enable)
                            {
                                SetRegistryString(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "legalnoticecaption", "Bakım Suite - Sistem Koruması");
                                SetRegistryString(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "legalnoticetext", "Bu bilgisayar yetkili kullanıcı erişimine tahsis edilmiştir. Güvenli oturum açma protokolü devrededir.");
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "legalnoticecaption");
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "legalnoticetext");
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

        public async Task<bool> SetChkdskTimeoutAsync(int seconds)
        {
            return await Task.Run(() =>
            {
                try
                {
                    int clamped = Math.Clamp(seconds, 0, 60);
                    return SetRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager", "AutoChkTimeOut", clamped);
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> SetBootTimeoutAsync(int seconds)
        {
            return await Task.Run(() =>
            {
                try
                {
                    int clamped = Math.Clamp(seconds, 0, 120);
                    return RunBcdedit("/timeout", clamped.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<int> ExportSpotlightImagesAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string assetsPath = Path.Combine(localAppData, @"Packages\Microsoft.Windows.ContentDeliveryManager_cw5n1h2txyewy\LocalState\Assets");
                    string targetFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Windows Spotlight");

                    if (!Directory.Exists(targetFolder))
                    {
                        Directory.CreateDirectory(targetFolder);
                    }

                    int count = 0;
                    if (Directory.Exists(assetsPath))
                    {
                        foreach (var file in Directory.GetFiles(assetsPath))
                        {
                            var fi = new FileInfo(file);
                            // 100 KB'den büyük dosyalar tam çözünürlüklü duvar kağıtlarıdır (küçük simgeleri filtreler)
                            if (fi.Length > 100 * 1024)
                            {
                                string destFile = Path.Combine(targetFolder, $"{fi.Name}.jpg");
                                if (!File.Exists(destFile))
                                {
                                    File.Copy(file, destFile, true);
                                    count++;
                                }
                            }
                        }
                    }

                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = targetFolder,
                            UseShellExecute = true
                        });
                    }
                    catch { }

                    return count;
                }
                catch
                {
                    return 0;
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
                else if (tweak.Type == TweakType.Numeric && tweak.Id == "chkdsk_timeout")
                {
                    await SetChkdskTimeoutAsync(8);
                    tweak.NumericValue = 8;
                }
                else if (tweak.Type == TweakType.Numeric && tweak.Id == "boot_timeout")
                {
                    await SetBootTimeoutAsync(30);
                    tweak.NumericValue = 30;
                }
            }
            return allOk;
        }

        #region Helpers

        private static bool CheckRegistryDword(RegistryKey root, string subKey, string valueName, int expectedValue)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key == null) return false;
                var val = key.GetValue(valueName);
                if (val == null) return false;
                return Convert.ToInt32(val) == expectedValue;
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckRegistryStringExists(RegistryKey root, string subKey, string valueName)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key == null) return false;
                var val = key.GetValue(valueName)?.ToString();
                return !string.IsNullOrWhiteSpace(val);
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckLegalNoticeEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
                if (key == null) return false;
                var text = key.GetValue("legalnoticetext")?.ToString();
                return !string.IsNullOrWhiteSpace(text);
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckNumLockEnabled()
        {
            try
            {
                using var key = Registry.Users.OpenSubKey(@".DEFAULT\Control Panel\Keyboard");
                if (key == null) return false;
                var val = key.GetValue("InitialKeyboardIndicators")?.ToString();
                return val == "2" || val == "2147483650";
            }
            catch
            {
                return false;
            }
        }

        private static int GetChkdskTimeout()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
                if (key == null) return 8;
                var val = key.GetValue("AutoChkTimeOut");
                if (val == null) return 8;
                return Convert.ToInt32(val);
            }
            catch
            {
                return 8;
            }
        }

        private static int GetBootTimeout()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "bcdedit.exe",
                    Arguments = "/enum {bootmgr}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return 30;
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                var match = Regex.Match(output, @"timeout\s+(\d+)", RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int timeout))
                {
                    return timeout;
                }
                return 30;
            }
            catch
            {
                return 30;
            }
        }

        private static bool CheckAutoRepairDisabled()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "bcdedit.exe",
                    Arguments = "/enum {default}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                return output.Contains("recoveryenabled") && output.Contains("No", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool RunBcdedit(params string[] arguments) =>
            ProcessRunner.RunReported("bcdedit " + string.Join(' ', arguments), "bcdedit.exe", arguments, TimeSpan.FromSeconds(20));

        private static bool SetRegistryDword(RegistryKey root, string subKey, string valueName, int value) =>
            VerifiedRegistry.SetDword(root, subKey, valueName, value);

        private static bool SetRegistryString(RegistryKey root, string subKey, string valueName, string value) =>
            VerifiedRegistry.SetString(root, subKey, valueName, value);

        private static bool DeleteRegistryValue(RegistryKey root, string subKey, string valueName) =>
            VerifiedRegistry.DeleteValue(root, subKey, valueName);

        #endregion
    }
}
