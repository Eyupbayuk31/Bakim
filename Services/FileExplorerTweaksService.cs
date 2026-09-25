using Microsoft.Win32;
using Bakım.Models;
using Bakım.Helpers;

namespace Bakım.Services
{
    public interface IFileExplorerTweaksService
    {
        Task<List<SystemTweakItem>> GetFileExplorerTweaksAsync();
        Task<bool> ApplyTweakAsync(SystemTweakItem tweak, bool enable);
        Task<bool> SetJumpListItemsAsync(int count);
        Task<bool> ApplyAllRecommendedAsync(List<SystemTweakItem> tweaks);
        Task<bool> RestoreDefaultsAsync(List<SystemTweakItem> tweaks);
    }

    public class FileExplorerTweaksService : IFileExplorerTweaksService
    {
        public async Task<List<SystemTweakItem>> GetFileExplorerTweaksAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<SystemTweakItem>
                {

                    // 3. Change Jump List Item Number
                    new()
                    {
                        Id = "explorer_jumplist_item_count",
                        Category = "Dosya Gezgini",
                        Title = "Görev Çubuğu Jump List (Son Kullanılanlar) Öge Sayısı",
                        Description = "Görev çubuğu ve Başlat menüsündeki uygulama simgelerine sağ tıklandığında listelenecek son kullanılan dosya sayısını ayarlar.",
                        Type = TweakType.Numeric,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "DocumentBulletList24",
                        NumericValue = GetJumpListItems(),
                        MinNumericValue = 0,
                        MaxNumericValue = 60,
                        NumericUnit = "öğe",
                        IsEnabled = true
                    },

                    // 12. Drag-n-Drop Sensitivity
                    new()
                    {
                        Id = "explorer_drag_drop_sensitivity",
                        Category = "Dosya Gezgini",
                        Title = "Yanlışlıkla Sürüklemeyi Önlemek İçin Sürükle-Bırak Hassasiyetini Artır",
                        Description = "Tıklarken yanlışlıkla dosyaların komşu klasöre taşınmasını önlemek için sürükleme tetikleme mesafesini 4 pikselden 15 piksele çıkarır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "Cursor24",
                        IsEnabled = CheckRegistryString(Registry.CurrentUser, @"Control Panel\Desktop", "DragHeight", "15")
                    },

                    // 14. Enable Auto Completion
                    new()
                    {
                        Id = "explorer_enable_auto_completion",
                        Category = "Dosya Gezgini",
                        Title = "Dosya Gezgini Adres Çubuğunda Otomatik Tamamlamayı Etkinleştir",
                        Description = "Dosya Gezgini adres çubuğuna ve 'Çalıştır' penceresine yazılan yolları yazarken otomatik tamamlar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "Edit24",
                        IsEnabled = CheckRegistryString(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoComplete", "AutoSuggest", "yes")
                    },

                    // 15. Enable Classic Search
                    new()
                    {
                        Id = "explorer_enable_classic_search",
                        Category = "Dosya Gezgini",
                        Title = "Dosya Gezgini Klasik Arama Davranışını ve Şeridini Geri Getir",
                        Description = "Windows 10/11'de yavaşlayan web tabanlı arama kutusu yerine anlık tepki veren klasik Win32 yerel arama çubuğunu açar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IconSymbol = "Search24",
                        IsEnabled = CheckKeyExists(Registry.CurrentUser, @"Software\Classes\CLSID\{1d64637d-31e9-4706-970d-0399b943d0b6}")
                    },

                };

                list.InsertRange(0, Bakım.Services.Tweaks.TweakEngine.ItemsFor("Dosya Gezgini"));
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

                        // 12. Drag-n-Drop Sensitivity
                        case "explorer_drag_drop_sensitivity":
                            string sensVal = enable ? "15" : "4";
                            SetRegistryString(Registry.CurrentUser, @"Control Panel\Desktop", "DragHeight", sensVal);
                            SetRegistryString(Registry.CurrentUser, @"Control Panel\Desktop", "DragWidth", sensVal);
                            break;

                        // 14. Enable Auto Completion
                        case "explorer_enable_auto_completion":
                            string autoVal = enable ? "yes" : "no";
                            SetRegistryString(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoComplete", "AutoSuggest", autoVal);
                            break;

                        // 15. Enable Classic Search
                        case "explorer_enable_classic_search":
                            if (enable)
                            {
                                // Anahtar yoksa geri almada tamamen silinir.
                                Bakım.Helpers.RegistryCapture.TrackKey(Registry.CurrentUser, @"Software\Classes\CLSID\{1d64637d-31e9-4706-970d-0399b943d0b6}");
                                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\CLSID\{1d64637d-31e9-4706-970d-0399b943d0b6}\TreatAs", true);
                                key?.SetValue("", "{00000000-0000-0000-0000-000000000000}");
                            }
                            else
                            {
                                DeleteRegistryKey(Registry.CurrentUser, @"Software\Classes\CLSID\{1d64637d-31e9-4706-970d-0399b943d0b6}");
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

        public async Task<bool> SetJumpListItemsAsync(int count)
        {
            return await Task.Run(() =>
            {
                try
                {
                    int clamped = Math.Clamp(count, 0, 60);
                    return SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_JumpListItems", clamped);
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
                else if (tweak.Type == TweakType.Numeric && tweak.Id == "explorer_jumplist_item_count")
                {
                    await SetJumpListItemsAsync(10);
                    tweak.NumericValue = 10;
                }
            }
            return allOk;
        }

        #region Helpers

        private static int GetJumpListItems()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                if (key == null) return 10;
                var val = key.GetValue("Start_JumpListItems");
                if (val == null) return 10;
                return Convert.ToInt32(val);
            }
            catch
            {
                return 10;
            }
        }

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

        private static bool CheckRegistryValueExists(RegistryKey root, string subKey, string valueName)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key == null) return false;
                return key.GetValue(valueName) != null;
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

        private static bool SetRegistryDword(RegistryKey root, string subKey, string valueName, int value) =>
            VerifiedRegistry.SetDword(root, subKey, valueName, value);

        private static bool SetRegistryString(RegistryKey root, string subKey, string valueName, string value) =>
            VerifiedRegistry.SetString(root, subKey, valueName, value);

        private static bool DeleteRegistryValue(RegistryKey root, string subKey, string valueName) =>
            VerifiedRegistry.DeleteValue(root, subKey, valueName);

        private static bool DeleteRegistryKey(RegistryKey root, string subKey) =>
            VerifiedRegistry.DeleteKeyTree(root, subKey);

        #endregion
    }
}
