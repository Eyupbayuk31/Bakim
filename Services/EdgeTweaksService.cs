using Microsoft.Win32;
using Bakım.Models;
using Bakım.Helpers;

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
                };

                list.InsertRange(0, Bakım.Services.Tweaks.TweakEngine.ItemsFor("Microsoft Edge"));
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
                tweak.LastError = "Ayar bulunamadı.";
                return false;
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

        private static bool SetRegistryDword(RegistryKey root, string subKey, string valueName, int value) =>
            VerifiedRegistry.SetDword(root, subKey, valueName, value);

        private static bool DeleteRegistryValue(RegistryKey root, string subKey, string valueName) =>
            VerifiedRegistry.DeleteValue(root, subKey, valueName);

        #endregion
    }
}
