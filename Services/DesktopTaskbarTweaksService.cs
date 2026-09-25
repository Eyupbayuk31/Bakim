using System.Diagnostics;
using Microsoft.Win32;
using Bakım.Models;
using Bakım.Helpers;

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
                        IconSymbol = "Search24",
                        IsEnabled = CheckWebSearchDisabled()
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

                list.InsertRange(0, Bakım.Services.Tweaks.TweakEngine.ItemsFor("Masaüstü & Görev Çubuğu"));
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

                        // 7. Classic Volume Mixer Launcher (Action)
                        case "classic_volume_mixer_launcher":
                            return LaunchClassicVolumeMixerSync();

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
                    FileName = System.IO.Path.Combine(Environment.SystemDirectory, "SndVol.exe"),
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

        private static bool SetRegistryDword(RegistryKey root, string subKey, string valueName, int value) =>
            VerifiedRegistry.SetDword(root, subKey, valueName, value);

        private static bool DeleteRegistryValue(RegistryKey root, string subKey, string valueName) =>
            VerifiedRegistry.DeleteValue(root, subKey, valueName);

        #endregion
    }
}
