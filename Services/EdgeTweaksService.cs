using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IEdgeTweaksService
    {
        Task<List<SystemTweakItem>> GetEdgeTweaksAsync();
        Task<bool> ApplyTweakAsync(SystemTweakItem tweak, bool enable);
        Task<bool> ApplyAllRecommendedAsync(List<SystemTweakItem> tweaks);
        Task<bool> RestoreDefaultsAsync(List<SystemTweakItem> tweaks);
    }

    public class EdgeTweaksService : IEdgeTweaksService
    {
        public async Task<List<SystemTweakItem>> GetEdgeTweaksAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<SystemTweakItem>
                {
                    // 1. Disable Annoyances and Bloat
                    new()
                    {
                        Id = "edge_disable_annoyances_bloat",
                        Category = "Microsoft Edge",
                        Title = "Edge Kenar Çubuğunu, Alışveriş ve Reklam Önerilerini Kapat",
                        Description = "Microsoft Edge'in yan panelini (Hubs Sidebar), alışveriş asistanını ve kişiselleştirilmiş tanıtım tekliflerini devre dışı bırakır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "DismissCircle24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "HubsSidebarEnabled", 0)
                    },

                    // 2. Disable Desktop Shortcut Creation after updates
                    new()
                    {
                        Id = "edge_disable_desktop_shortcut",
                        Category = "Microsoft Edge",
                        Title = "Edge Güncellemeleri Sonrası Masaüstü Kısayolu Eklenmesini Engelle",
                        Description = "Her Edge tarayıcı güncellemesinden sonra masaüstüne zorla yeniden eklenen Microsoft Edge kısayolunun oluşturulmasını engeller.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "Desktop24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\EdgeUpdate", "CreateDesktopShortcutDefault", 0)
                    },

                    // 3. Disable Updates
                    new()
                    {
                        Id = "edge_disable_updates",
                        Category = "Microsoft Edge",
                        Title = "Microsoft Edge Otomatik Güncelleştirmelerini Kapat",
                        Description = "Edge tarayıcısının arka planda otomatik güncellenmesini ve bant genişliği harcamasını durdurur.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "ArrowDownload24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\EdgeUpdate", "UpdateDefault", 0)
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
                        case "edge_disable_annoyances_bloat":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "HubsSidebarEnabled", 0);
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "ShowRecommendOffers", 0);
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "EdgeShoppingAssistantEnabled", 0);
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "PersonalizationReportingEnabled", 0);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "HubsSidebarEnabled");
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "ShowRecommendOffers");
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "EdgeShoppingAssistantEnabled");
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "PersonalizationReportingEnabled");
                            }
                            break;

                        case "edge_disable_desktop_shortcut":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\EdgeUpdate", "CreateDesktopShortcutDefault", 0);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\EdgeUpdate", "CreateDesktopShortcutDefault");
                            }
                            break;

                        case "edge_disable_updates":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\EdgeUpdate", "UpdateDefault", 0);
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\EdgeUpdate", "Update{56EB18F4-5E85-450F-A862-0941E302A384}", 0);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\EdgeUpdate", "UpdateDefault");
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\EdgeUpdate", "Update{56EB18F4-5E85-450F-A862-0941E302A384}");
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
