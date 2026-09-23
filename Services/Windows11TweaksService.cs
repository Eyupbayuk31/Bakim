using System.Diagnostics;
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IWindows11TweaksService
    {
        Task<List<SystemTweakItem>> GetWindows11TweaksAsync();
        Task<bool> ApplyTweakAsync(SystemTweakItem item, bool enable);
        Task<bool> ApplyAllRecommendedAsync(IEnumerable<SystemTweakItem> items);
        Task<bool> RestoreDefaultsAsync(IEnumerable<SystemTweakItem> items);
    }

    public class Windows11TweaksService : IWindows11TweaksService
    {
        private const string CategoryName = "Windows 11";

        public async Task<List<SystemTweakItem>> GetWindows11TweaksAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<SystemTweakItem>
                {
                    new()
                    {
                        Id = "win11_classic_context_menu",
                        Category = CategoryName,
                        Title = "Klasik Tam Sağ Tık Menüsü (Classic Full Context Menus)",
                        Description = "Windows 11'in 'Daha fazla seçenek göster' kısıtlamasını kaldırarak Windows 10 tarzı tam sağ tık menüsünü doğrudan açar.",
                        IconSymbol = "WindowApps24",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IsEnabled = IsClassicContextMenuEnabled()
                    },
                    new()
                    {
                        Id = "win11_classic_taskbar",
                        Category = CategoryName,
                        Title = "Klasik Görev Çubuğu Hizalaması (Classic Taskbar)",
                        Description = "Görev çubuğu simgelerini Windows 10 stili sol köşeye taşır ve klasik hizalama düzenine geçirir.",
                        IconSymbol = "DockRow24",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IsEnabled = IsClassicTaskbarEnabled()
                    },
                    new()
                    {
                        Id = "win11_colorize_taskbar",
                        Category = CategoryName,
                        Title = "Görev Çubuğunu Renklendir (Colorize Taskbar)",
                        Description = "Windows 11 görev çubuğunda ve Başlat menüsünde sistem tema vurgu rengini (Accent Color) zorla görünür kılar.",
                        IconSymbol = "Color24",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IsEnabled = IsColorizeTaskbarEnabled()
                    },
                    new()
                    {
                        Id = "win11_disable_background_apps",
                        Category = CategoryName,
                        Title = "Arka Plan Uygulamalarını Kapat (Disable Background Apps)",
                        Description = "Windows 11 ayarlarından kaldırılan arka plan UWP uygulama çalışmasını tamamen engelleyerek RAM ve pil tasarrufu sağlar.",
                        IconSymbol = "HeartPulse24",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IsEnabled = IsBackgroundAppsDisabled()
                    },
                    new()
                    {
                        Id = "win11_disable_copilot",
                        Category = CategoryName,
                        Title = "Windows Copilot'u Kapat (Disable Copilot)",
                        Description = "Windows 11'e entegre gelen Copilot yapay zeka asistanını ve görev çubuğundaki Copilot simgesini tamamen kapatır.",
                        IconSymbol = "Bot24",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IsEnabled = IsCopilotDisabled()
                    },
                    new()
                    {
                        Id = "win11_disable_recommended_start",
                        Category = CategoryName,
                        Title = "Başlat Menüsü Önerilenleri Kapat (Disable Recommended in Start)",
                        Description = "Başlat menüsündeki 'Önerilenler' (en son kullanılan dosyalar ve son yüklenen uygulamalar) alanını temizleyip devre dışı bırakır.",
                        IconSymbol = "AppsListDetail24",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IsEnabled = IsRecommendedInStartDisabled()
                    },
                    new()
                    {
                        Id = "win11_enable_ribbon",
                        Category = CategoryName,
                        Title = "Klasik Dosya Gezgini Şerit Menüsü (Enable Ribbon)",
                        Description = "Windows 11 Dosya Gezgini'ndeki modern sadeleştirilmiş komut çubuğu yerine klasik Windows 10 Ribbon şerit menüsünü geri getirir.",
                        IconSymbol = "Folder24",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IsEnabled = IsRibbonEnabled()
                    },
                    new()
                    {
                        Id = "win11_enable_stickers",
                        Category = CategoryName,
                        Title = "Masaüstü Arka Plan Çıkartmaları (Enable Stickers)",
                        Description = "Windows 11'de masaüstü duvar kağıdı üzerine çıkartma (Stickers) ekleme özelliğini aktif eder.",
                        IconSymbol = "Emoji24",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IsEnabled = IsStickersEnabled()
                    },
                    new()
                    {
                        Id = "win11_remove_spotlight_icon",
                        Category = CategoryName,
                        Title = "Windows Işığı Masaüstü Simgesini Kaldır (Remove Spotlight Icon)",
                        Description = "Windows Spotlight (Öne Çıkanlar) duvar kağıdı etkinken masaüstünde beliren 'Bu resim hakkında bilgi edinin' simgesini kaldırır.",
                        IconSymbol = "Camera24",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IsEnabled = IsSpotlightIconRemoved()
                    }
                };

                return list;
            });
        }

        public async Task<bool> ApplyTweakAsync(SystemTweakItem item, bool enable)
        {
            return await Task.Run(() =>
            {
                try
                {
                    return item.Id switch
                    {
                        "win11_classic_context_menu" => SetClassicContextMenu(enable),
                        "win11_classic_taskbar" => SetClassicTaskbar(enable),
                        "win11_colorize_taskbar" => SetColorizeTaskbar(enable),
                        "win11_disable_background_apps" => SetDisableBackgroundApps(enable),
                        "win11_disable_copilot" => SetDisableCopilot(enable),
                        "win11_disable_recommended_start" => SetDisableRecommendedInStart(enable),
                        "win11_enable_ribbon" => SetEnableRibbon(enable),
                        "win11_enable_stickers" => SetEnableStickers(enable),
                        "win11_remove_spotlight_icon" => SetRemoveSpotlightIcon(enable),
                        _ => false
                    };
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> ApplyAllRecommendedAsync(IEnumerable<SystemTweakItem> items)
        {
            return await Task.Run(() =>
            {
                bool allSuccess = true;
                foreach (var item in items.Where(i => i.IsRecommended))
                {
                    try
                    {
                        bool success = item.Id switch
                        {
                            "win11_classic_context_menu" => SetClassicContextMenu(true),
                            "win11_disable_background_apps" => SetDisableBackgroundApps(true),
                            "win11_disable_copilot" => SetDisableCopilot(true),
                            "win11_disable_recommended_start" => SetDisableRecommendedInStart(true),
                            "win11_remove_spotlight_icon" => SetRemoveSpotlightIcon(true),
                            _ => true
                        };

                        if (success)
                        {
                            item.IsEnabled = true;
                        }
                        else
                        {
                            allSuccess = false;
                        }
                    }
                    catch
                    {
                        allSuccess = false;
                    }
                }
                return allSuccess;
            });
        }

        public async Task<bool> RestoreDefaultsAsync(IEnumerable<SystemTweakItem> items)
        {
            return await Task.Run(() =>
            {
                bool allSuccess = true;
                foreach (var item in items)
                {
                    try
                    {
                        bool success = item.Id switch
                        {
                            "win11_classic_context_menu" => SetClassicContextMenu(false),
                            "win11_classic_taskbar" => SetClassicTaskbar(false),
                            "win11_colorize_taskbar" => SetColorizeTaskbar(false),
                            "win11_disable_background_apps" => SetDisableBackgroundApps(false),
                            "win11_disable_copilot" => SetDisableCopilot(false),
                            "win11_disable_recommended_start" => SetDisableRecommendedInStart(false),
                            "win11_enable_ribbon" => SetEnableRibbon(false),
                            "win11_enable_stickers" => SetEnableStickers(false),
                            "win11_remove_spotlight_icon" => SetRemoveSpotlightIcon(false),
                            _ => true
                        };

                        if (success)
                        {
                            item.IsEnabled = false;
                        }
                        else
                        {
                            allSuccess = false;
                        }
                    }
                    catch
                    {
                        allSuccess = false;
                    }
                }
                return allSuccess;
            });
        }

        #region 1. Classic Full Context Menus

        private static bool IsClassicContextMenuEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32");
                if (key == null) return false;
                var val = key.GetValue(string.Empty);
                return val != null && val.ToString() == string.Empty;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetClassicContextMenu(bool enable)
        {
            try
            {
                if (enable)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32");
                    key.SetValue(string.Empty, string.Empty);
                }
                else
                {
                    try
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", false);
                    }
                    catch { }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 2. Classic Taskbar (Left Alignment)

        private static bool IsClassicTaskbarEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                int val = Convert.ToInt32(key?.GetValue("TaskbarAl") ?? 1);
                return val == 0; // 0 = Left, 1 = Center
            }
            catch
            {
                return false;
            }
        }

        private static bool SetClassicTaskbar(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                key.SetValue("TaskbarAl", enable ? 0 : 1, RegistryValueKind.DWord);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 3. Colorize Taskbar

        private static bool IsColorizeTaskbarEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return Convert.ToInt32(key?.GetValue("ColorPrevalence") ?? 0) == 1;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetColorizeTaskbar(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                key.SetValue("ColorPrevalence", enable ? 1 : 0, RegistryValueKind.DWord);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 4. Disable Background Apps

        private static bool IsBackgroundAppsDisabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications");
                int userVal = Convert.ToInt32(key?.GetValue("GlobalUserDisabled") ?? 0);
                if (userVal == 1) return true;

                using var policyKey = Registry.LocalMachine.OpenSubKey(@"Software\Policies\Microsoft\Windows\AppPrivacy");
                int policyVal = Convert.ToInt32(policyKey?.GetValue("LetAppsRunInBackground") ?? 0);
                return policyVal == 2;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetDisableBackgroundApps(bool disable)
        {
            try
            {
                // HKCU BackgroundAccessApplications
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications"))
                {
                    key.SetValue("GlobalUserDisabled", disable ? 1 : 0, RegistryValueKind.DWord);
                }

                // HKCU AppPrivacy
                using (var cuPolicy = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Windows\AppPrivacy"))
                {
                    if (disable)
                        cuPolicy.SetValue("LetAppsRunInBackground", 2, RegistryValueKind.DWord);
                    else
                        cuPolicy.DeleteValue("LetAppsRunInBackground", false);
                }

                // HKLM AppPrivacy
                try
                {
                    using var lmPolicy = Registry.LocalMachine.CreateSubKey(@"Software\Policies\Microsoft\Windows\AppPrivacy");
                    if (disable)
                        lmPolicy.SetValue("LetAppsRunInBackground", 2, RegistryValueKind.DWord);
                    else
                        lmPolicy.DeleteValue("LetAppsRunInBackground", false);
                }
                catch { }

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 5. Disable Copilot

        private static bool IsCopilotDisabled()
        {
            try
            {
                using var cuPolicy = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\WindowsCopilot");
                if (Convert.ToInt32(cuPolicy?.GetValue("TurnOffWindowsCopilot") ?? 0) == 1) return true;

                using var lmPolicy = Registry.LocalMachine.OpenSubKey(@"Software\Policies\Microsoft\Windows\WindowsCopilot");
                if (Convert.ToInt32(lmPolicy?.GetValue("TurnOffWindowsCopilot") ?? 0) == 1) return true;

                using var advKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                return Convert.ToInt32(advKey?.GetValue("ShowCopilotButton") ?? 1) == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetDisableCopilot(bool disable)
        {
            try
            {
                // Policies HKCU
                using (var cuPolicy = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Windows\WindowsCopilot"))
                {
                    if (disable)
                        cuPolicy.SetValue("TurnOffWindowsCopilot", 1, RegistryValueKind.DWord);
                    else
                        cuPolicy.DeleteValue("TurnOffWindowsCopilot", false);
                }

                // Policies HKLM
                try
                {
                    using var lmPolicy = Registry.LocalMachine.CreateSubKey(@"Software\Policies\Microsoft\Windows\WindowsCopilot");
                    if (disable)
                        lmPolicy.SetValue("TurnOffWindowsCopilot", 1, RegistryValueKind.DWord);
                    else
                        lmPolicy.DeleteValue("TurnOffWindowsCopilot", false);
                }
                catch { }

                // Hide Copilot Button in Taskbar
                using (var advKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                {
                    advKey.SetValue("ShowCopilotButton", disable ? 0 : 1, RegistryValueKind.DWord);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 6. Disable Recommended in Start

        private static bool IsRecommendedInStartDisabled()
        {
            try
            {
                using var cuPol = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\Explorer");
                if (Convert.ToInt32(cuPol?.GetValue("HideRecommendedSection") ?? 0) == 1) return true;

                using var adv = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                int trackProgs = Convert.ToInt32(adv?.GetValue("Start_TrackProgs") ?? 1);
                int trackDocs = Convert.ToInt32(adv?.GetValue("Start_TrackDocs") ?? 1);
                return trackProgs == 0 && trackDocs == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetDisableRecommendedInStart(bool disable)
        {
            try
            {
                // Group Policy: HideRecommendedSection
                using (var cuPol = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Windows\Explorer"))
                {
                    if (disable)
                        cuPol.SetValue("HideRecommendedSection", 1, RegistryValueKind.DWord);
                    else
                        cuPol.DeleteValue("HideRecommendedSection", false);
                }

                try
                {
                    using var lmPol = Registry.LocalMachine.CreateSubKey(@"Software\Policies\Microsoft\Windows\Explorer");
                    if (disable)
                        lmPol.SetValue("HideRecommendedSection", 1, RegistryValueKind.DWord);
                    else
                        lmPol.DeleteValue("HideRecommendedSection", false);
                }
                catch { }

                // Explorer Advanced: Track recent progs & docs
                using (var adv = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                {
                    adv.SetValue("Start_TrackProgs", disable ? 0 : 1, RegistryValueKind.DWord);
                    adv.SetValue("Start_TrackDocs", disable ? 0 : 1, RegistryValueKind.DWord);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 7. Enable Ribbon in File Explorer

        private static bool IsRibbonEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked");
                var val = key?.GetValue("{e2bf9676-5f8f-435c-97eb-11607a5bedf7}");
                return val != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetEnableRibbon(bool enable)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked");
                if (enable)
                {
                    key.SetValue("{e2bf9676-5f8f-435c-97eb-11607a5bedf7}", string.Empty, RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue("{e2bf9676-5f8f-435c-97eb-11607a5bedf7}", false);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 8. Enable Stickers for Desktop Background

        private static bool IsStickersEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\PolicyManager\current\device\Stickers");
                return Convert.ToInt32(key?.GetValue("EnableStickers") ?? 0) == 1;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetEnableStickers(bool enable)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\PolicyManager\current\device\Stickers");
                if (enable)
                {
                    key.SetValue("EnableStickers", 1, RegistryValueKind.DWord);
                }
                else
                {
                    key.SetValue("EnableStickers", 0, RegistryValueKind.DWord);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 9. Remove Windows Spotlight Desktop Icon

        private static bool IsSpotlightIconRemoved()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel");
                return Convert.ToInt32(key?.GetValue("{2cc5ca98-6485-489a-920e-b3e88a6ccce3}") ?? 0) == 1;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetRemoveSpotlightIcon(bool remove)
        {
            try
            {
                int val = remove ? 1 : 0;

                using (var key1 = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel"))
                {
                    key1.SetValue("{2cc5ca98-6485-489a-920e-b3e88a6ccce3}", val, RegistryValueKind.DWord);
                }

                using (var key2 = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\ClassicStartMenu"))
                {
                    key2.SetValue("{2cc5ca98-6485-489a-920e-b3e88a6ccce3}", val, RegistryValueKind.DWord);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
