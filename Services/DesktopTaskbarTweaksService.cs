using System.Diagnostics;
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IDesktopTaskbarTweaksService
    {
        Task<List<SystemTweakItem>> GetDesktopTaskbarTweaksAsync();
        Task<bool> ApplyTweakAsync(SystemTweakItem tweak, bool enable);
        Task<bool> LaunchClassicVolumeMixerAsync();
        Task<bool> ApplyAllRecommendedAsync(List<SystemTweakItem> tweaks);
        Task<bool> RestoreDefaultsAsync(List<SystemTweakItem> tweaks);
    }

    public class DesktopTaskbarTweaksService : IDesktopTaskbarTweaksService
    {
        public async Task<List<SystemTweakItem>> GetDesktopTaskbarTweaksAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<SystemTweakItem>
                {
                    // 1. Action Center Always Open / Disable Notification Center
                    new()
                    {
                        Id = "disable_action_center",
                        Category = "Masaüstü & Görev Çubuğu",
                        Title = "Bildirim Merkezini (Eylem Merkezi) Tamamen Kapat",
                        Description = "Görev çubuğunun sağındaki bildirim simgesini ve yan paneli kapatarak bildirim pop-up'larının dikkatinizi dağıtmasını engeller.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IconSymbol = "DismissCircle24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableNotificationCenter", 1)
                    },

                    // 2. Classic Volume Mixer (System-wide MTCUVC Toggle)
                    new()
                    {
                        Id = "classic_volume_mixer",
                        Category = "Masaüstü & Görev Çubuğu",
                        Title = "Windows 7 Stili Klasik Ses Karıştırıcıyı (MTCUVC) Varsayılan Yap",
                        Description = "Ses simgesine tıklandığında açılan modern ses çubuğu yerine her programın sesini bağımsız ayarlayan klasik Windows 7 ses panelini açar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IconSymbol = "Speaker224",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\MTCUVC", "EnableMtcUvc", 0)
                    },

                    // 3. Disable Web Search in Start Menu
                    new()
                    {
                        Id = "disable_web_search",
                        Category = "Masaüstü & Görev Çubuğu",
                        Title = "Başlat Menüsündeki Bing Web Aramalarını Tamamen Kapat",
                        Description = "Başlat menüsünde arama yaparken internetten ve Bing sunucularından gelen gereksiz web sonuçlarını kapatır, sadece yerel dosyaları arar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "SearchDismiss24",
                        IsEnabled = CheckWebSearchDisabled()
                    },

                    // 4. Show Seconds on Taskbar Clock
                    new()
                    {
                        Id = "show_seconds_taskbar",
                        Category = "Masaüstü & Görev Çubuğu",
                        Title = "Görev Çubuğu Saatinde Saniyeleri Göster",
                        Description = "Windows 11/10 görev çubuğunun sağ alt köşesindeki dijital saate saniye göstergesini (SS:DD:ss) ekler.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "Timer24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSecondsInSystemClock", 1)
                    },

                    // 5. Wallpaper Quality 100%
                    new()
                    {
                        Id = "wallpaper_quality_100",
                        Category = "Masaüstü & Görev Çubuğu",
                        Title = "Duvar Kağıdı Kalite Kaybını (JPEG Sıkıştırması) Kapat",
                        Description = "Windows'un yüksek çözünürlüklü duvar kağıtlarını %85 oranında sıkıştırıp bozmasını engelleyerek %100 orijinal kalitede gösterir.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "Image24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Control Panel\Desktop", "JPEGImportQuality", 100)
                    },

                    // 6. Windows Version on Desktop
                    new()
                    {
                        Id = "paint_desktop_version",
                        Category = "Masaüstü & Görev Çubuğu",
                        Title = "Masaüstünün Sağ Alt Köşesine Windows Sürümünü Bas",
                        Description = "Masaüstü arka planının sağ alt köşesine işletim sistemi adını, derleme (Build) numarasını ve mimarisini filigran olarak yansıtır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IconSymbol = "SlideTextSparkle24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Control Panel\Desktop", "PaintDesktopVersion", 1)
                    },

                    // 7. Classic Volume Mixer Launcher (Action Tool)
                    new()
                    {
                        Id = "classic_volume_mixer_launcher",
                        Category = "Masaüstü & Görev Çubuğu",
                        Title = "Klasik Ses Karıştırıcısını Çalıştır (SndVol.exe)",
                        Description = "Windows 7 stili gelişmiş ve her uygulamayı ayrı kontrol edebilen bağımsız ses düzeyi karıştırıcısını hemen açar.",
                        Type = TweakType.Action,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "SpeakerSettings24",
                        IsEnabled = true
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
                        // 1. Action Center Always Open / Disable Notification Center
                        case "disable_action_center":
                            if (enable)
                            {
                                SetRegistryDword(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableNotificationCenter", 1);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableNotificationCenter");
                            }
                            break;

                        // 2. Classic Volume Mixer (MTCUVC)
                        case "classic_volume_mixer":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\MTCUVC", "EnableMtcUvc", 0);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\MTCUVC", "EnableMtcUvc");
                            }
                            break;

                        // 3. Disable Web Search
                        case "disable_web_search":
                            if (enable)
                            {
                                SetRegistryDword(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1);
                                SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0);
                                SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "CortanaConsent", 0);
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch", 1);
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", 0);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions");
                                SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 1);
                                SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "CortanaConsent", 1);
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch");
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb");
                            }
                            break;

                        // 4. Show Seconds on Taskbar Clock
                        case "show_seconds_taskbar":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSecondsInSystemClock", enable ? 1 : 0);
                            break;

                        // 5. Wallpaper Quality 100%
                        case "wallpaper_quality_100":
                            if (enable)
                            {
                                SetRegistryDword(Registry.CurrentUser, @"Control Panel\Desktop", "JPEGImportQuality", 100);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.CurrentUser, @"Control Panel\Desktop", "JPEGImportQuality");
                            }
                            break;

                        // 6. Windows Version on Desktop
                        case "paint_desktop_version":
                            SetRegistryDword(Registry.CurrentUser, @"Control Panel\Desktop", "PaintDesktopVersion", enable ? 1 : 0);
                            break;

                        // 7. Classic Volume Mixer Launcher (Action)
                        case "classic_volume_mixer_launcher":
                            return LaunchClassicVolumeMixerSync();

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

        public async Task<bool> LaunchClassicVolumeMixerAsync()
        {
            return await Task.Run(LaunchClassicVolumeMixerSync);
        }

        private static bool LaunchClassicVolumeMixerSync()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = @"C:\Windows\System32\SndVol.exe",
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
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

        private static bool CheckWebSearchDisabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\Explorer");
                if (key != null)
                {
                    var val = key.GetValue("DisableSearchBoxSuggestions");
                    if (val != null && Convert.ToInt32(val) == 1) return true;
                }

                using var key2 = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search");
                if (key2 != null)
                {
                    var val = key2.GetValue("BingSearchEnabled");
                    if (val != null && Convert.ToInt32(val) == 0) return true;
                }

                return false;
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

        private static void DeleteRegistryValue(RegistryKey root, string subKey, string valueName)
        {
            try
            {
                using var key = root.OpenSubKey(subKey, true);
                key?.DeleteValue(valueName, false);
            }
            catch { }
        }

        #endregion
    }
}
