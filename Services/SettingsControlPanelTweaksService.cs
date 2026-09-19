using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public interface ISettingsControlPanelTweaksService
    {
        Task<List<SystemTweakItem>> GetSettingsControlPanelTweaksAsync();
        Task<bool> ApplyTweakAsync(SystemTweakItem tweak, bool enable);
        Task<bool> ApplyAllRecommendedAsync(List<SystemTweakItem> tweaks);
        Task<bool> RestoreDefaultsAsync(List<SystemTweakItem> tweaks);
    }

    public class SettingsControlPanelTweaksService : ISettingsControlPanelTweaksService
    {
        public async Task<List<SystemTweakItem>> GetSettingsControlPanelTweaksAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<SystemTweakItem>
                {
                    // 1. Add Classic User Accounts
                    new()
                    {
                        Id = "cpl_classic_user_accounts",
                        Category = "Ayarlar & Denetim Masası",
                        Title = "Denetim Masasına Klasik Kullanıcı Hesapları Simgesini Ekle",
                        Description = "Gelişmiş yerel kullanıcı ve grup yönetimi (nusrmgr.cpl) penceresini doğrudan Denetim Masası simgeleri arasına ekler.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "PersonAccounts24",
                        IsEnabled = CheckKeyExists(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ControlPanel\NameSpace\{7A8CAE00-C21A-4F2E-8BCE-82370E0A5FA3}")
                    },

                    // 2. Add Personalization
                    new()
                    {
                        Id = "cpl_classic_personalization",
                        Category = "Ayarlar & Denetim Masası",
                        Title = "Denetim Masasına Klasik Kişiselleştirme Menüsünü Ekle",
                        Description = "Eski Windows Kişiselleştirme penceresini (Masaüstü Arka Planı, Renkler, Sesler, Ekran Koruyucu) Denetim Masasına geri getirir.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "Color24",
                        IsEnabled = CheckKeyExists(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ControlPanel\NameSpace\{ED834ED6-4B5A-4bfe-8F11-A626DCB6A921}")
                    },

                    // 3. Add Windows Update
                    new()
                    {
                        Id = "cpl_windows_update",
                        Category = "Ayarlar & Denetim Masası",
                        Title = "Denetim Masasına Windows Update Kısayolunu Ekle",
                        Description = "Denetim Masası ana ekranına doğrudan modern Windows Update sayfasına bağlanan simge ekler.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "ArrowClockwise24",
                        IsEnabled = CheckKeyExists(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ControlPanel\NameSpace\{36eef7db-88ad-4e81-ad49-0e313f0c35f8}")
                    },

                    // 4. Disable Online & Video Tips in Settings
                    new()
                    {
                        Id = "settings_disable_online_tips",
                        Category = "Ayarlar & Denetim Masası",
                        Title = "Ayarlar Uygulamasındaki Çevrimiçi İpuçlarını ve Tanıtımları Kapat",
                        Description = "Windows Ayarlar uygulamasının sağ sütununda veya altında çıkan öneri, ipucu ve video yönlendirmelerini kaldırır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "Lightbulb24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableSoftLanding", 1)
                    },

                    // 5. Hide Pages from Settings
                    new()
                    {
                        Id = "settings_hide_specific_pages",
                        Category = "Ayarlar & Denetim Masası",
                        Title = "Ayarlar Uygulamasında Gereksiz Telemetri Sayfalarını Gizle",
                        Description = "Ayarlar uygulamasında gizlilik tehdidi oluşturan tanılama ve reklam sayfalarını (Feedback, DiagnosticData, WindowsInsider) gizler.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "EyeHide24",
                        IsEnabled = CheckRegistryString(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "SettingsPageVisibility", "hide:feedback;diagnostics;windowsinsider")
                    },

                    // 6. Insider Page
                    new()
                    {
                        Id = "settings_disable_insider_page",
                        Category = "Ayarlar & Denetim Masası",
                        Title = "Windows Insider Programı Sayfasını Ayarlar'dan Tamamen Gizle",
                        Description = "Ayarlar > Güncelleştirme altındaki 'Windows Insider Programı' katılma sekmesini kaldırarak kararsız beta sürümlerine geçilmesini önler.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "ShieldDismiss24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DisableWindowsInsiderPicker", 1)
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
                        case "cpl_classic_user_accounts":
                            if (enable)
                            {
                                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ControlPanel\NameSpace\{7A8CAE00-C21A-4F2E-8BCE-82370E0A5FA3}", true);
                                key?.SetValue("", "Klasik Kullanıcı Hesapları");
                            }
                            else
                            {
                                DeleteRegistryKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ControlPanel\NameSpace\{7A8CAE00-C21A-4F2E-8BCE-82370E0A5FA3}");
                            }
                            break;

                        case "cpl_classic_personalization":
                            if (enable)
                            {
                                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ControlPanel\NameSpace\{ED834ED6-4B5A-4bfe-8F11-A626DCB6A921}", true);
                                key?.SetValue("", "Klasik Kişiselleştirme");
                            }
                            else
                            {
                                DeleteRegistryKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ControlPanel\NameSpace\{ED834ED6-4B5A-4bfe-8F11-A626DCB6A921}");
                            }
                            break;

                        case "cpl_windows_update":
                            if (enable)
                            {
                                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ControlPanel\NameSpace\{36eef7db-88ad-4e81-ad49-0e313f0c35f8}", true);
                                key?.SetValue("", "Windows Update");
                            }
                            else
                            {
                                DeleteRegistryKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ControlPanel\NameSpace\{36eef7db-88ad-4e81-ad49-0e313f0c35f8}");
                            }
                            break;

                        case "settings_disable_online_tips":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableSoftLanding", 1);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableSoftLanding");
                            }
                            break;

                        case "settings_hide_specific_pages":
                            if (enable)
                            {
                                SetRegistryString(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "SettingsPageVisibility", "hide:feedback;diagnostics;windowsinsider");
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "SettingsPageVisibility");
                            }
                            break;

                        case "settings_disable_insider_page":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DisableWindowsInsiderPicker", 1);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DisableWindowsInsiderPicker");
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

        public async Task<bool> ApplyAllRecommendedAsync(List<SystemTweakItem> tweaks)
        {
            bool allOk = true;
            foreach (var tweak in tweaks)
            {
                if (tweak.IsRecommended && !tweak.IsEnabled)
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
                if (tweak.IsEnabled)
                {
                    bool ok = await ApplyTweakAsync(tweak, false);
                    if (!ok) allOk = false;
                }
            }
            return allOk;
        }

        #region Helpers

        private static bool CheckKeyExists(RegistryKey root, string subKey)
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

        private static bool CheckRegistryString(RegistryKey root, string subKey, string valueName, string expectedValue)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key == null) return false;
                var val = key.GetValue(valueName)?.ToString();
                return string.Equals(val, expectedValue, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void SetRegistryDword(RegistryKey root, string subKey, string valueName, int value)
        {
            try
            {
                using var key = root.CreateSubKey(subKey, true);
                key?.SetValue(valueName, value, RegistryValueKind.DWord);
            }
            catch { }
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
