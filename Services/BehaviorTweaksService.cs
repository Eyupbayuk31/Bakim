using System.Diagnostics;
using Microsoft.Win32;
using Bakım.Models;
using Bakım.Helpers;

namespace Bakım.Services
{
    public interface IBehaviorTweaksService
    {
        Task<List<SystemTweakItem>> GetBehaviorTweaksAsync();
        Task<bool> ApplyTweakAsync(SystemTweakItem tweak, bool enable);
        Task<bool> ApplyAllRecommendedAsync(List<SystemTweakItem> tweaks);
        Task<bool> RestoreDefaultsAsync(List<SystemTweakItem> tweaks);
    }

    public class BehaviorTweaksService : IBehaviorTweaksService
    {
        // Caps Lock tuşunu donanım seviyesinde sıfırlayan Scancode Map (0x3A -> 0x00)
        private static readonly byte[] CapsLockDisableScancodeMap =
        [
            0x00, 0x00, 0x00, 0x00, // Version
            0x00, 0x00, 0x00, 0x00, // Flags
            0x02, 0x00, 0x00, 0x00, // Count: 1 mapping + 1 null terminator = 2
            0x00, 0x00, 0x3A, 0x00, // Map 0x003A (Caps Lock) to 0x0000 (Null)
            0x00, 0x00, 0x00, 0x00  // Null Terminator
        ];

        public async Task<List<SystemTweakItem>> GetBehaviorTweaksAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<SystemTweakItem>
                {

                    // 7. Disable Downloads Blocking (SmartScreen Zone Identifier)
                    new()
                    {
                        Id = "disable_zone_identifier",
                        Category = "Davranışlar (Behavior)",
                        Title = "İnternetten İndirilen Dosyaların 'Engellendi' İşaretini Kaldır",
                        Description = "Tarayıcıdan indirilen dosya ve arşivlere iliştirilen NTFS Zone.Identifier etiketini kaldırarak 'Bu dosya engellendi' uyarısını sonlandırır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "CheckmarkCircle24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Policies\Attachments", "SaveZoneInformation", 1)
                    },

                    // 10. Disable SmartScreen
                    new()
                    {
                        Id = "disable_smartscreen",
                        Category = "Davranışlar (Behavior)",
                        Title = "Windows Defender SmartScreen Uyarılarını Tamamen Kapat",
                        Description = "İnternetten indirilen veya imzasız çalıştırılabilir (.exe) dosyaların başlatılmasını engelleyen SmartScreen mavi ekran uyarılarını kapatır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "Shield24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender\SmartScreenHost", "EnableSmartScreen", 0) ||
                                    CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", 0)
                    },

                    // 12. Disable Windows Update
                    new()
                    {
                        Id = "disable_windows_update",
                        Category = "Davranışlar (Behavior)",
                        Title = "Windows Update Hizmetlerini Durdur ve Devre Dışı Bırak",
                        Description = "Windows Update (wuauserv) ve Arka Plan Akıllı Aktarım (BITS) hizmetlerini tamamen durdurup otomatik güncelleme denetimini kilitler.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "ClockDismiss24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\wuauserv", "Start", 4) ||
                                    CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", 1)
                    },

                    // 14. Enable Emoji Picker
                    new()
                    {
                        Id = "enable_emoji_picker",
                        Category = "Davranışlar (Behavior)",
                        Title = "Win + Nokta (.) Emoji ve İfade Seçici Panelini Etkinleştir",
                        Description = "Klavyeden Win + . veya Win + ; tuşlarına basıldığında modern Windows Emoji, GIF ve Kaomoji paneli kısayolunu açık tutar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "Emoji24",
                        IsEnabled = !CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Input\Settings", "EnableExpressiveInputShellHotkey", 0)
                    },

                    // 19. Redefine Extra Keys on Keyboard (Disable Caps Lock)
                    new()
                    {
                        Id = "disable_caps_lock_key",
                        Category = "Davranışlar (Behavior)",
                        Title = "Klavyedeki Caps Lock Tuşunu Tamamen Devre Dışı Bırak",
                        Description = "Yazı yazarken yanlışlıkla Caps Lock tuşuna basılıp büyük harfe geçilmesini donanım seviyesinde (Scancode Map) devre dışı bırakır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IconSymbol = "Keyboard24",
                        IsEnabled = CheckRegistryBinary(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Keyboard Layout", "Scancode Map")
                    },

                    // 23. Sound for Print Screen key
                    new()
                    {
                        Id = "sound_print_screen",
                        Category = "Davranışlar (Behavior)",
                        Title = "Print Screen Tuşu ile Ekran Alındığında Ses Çal",
                        Description = "Klavyeden Print Screen (PrtScn) tuşuna basılarak ekran görüntüsü alındığında sesli geribildirim efekti çalar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "Speaker224",
                        IsEnabled = CheckPrintScreenSoundEnabled()
                    },

                    // 24. Split Threshold for Svchost
                    new()
                    {
                        Id = "svchost_grouping",
                        Category = "Davranışlar (Behavior)",
                        Title = "Svchost Süreçlerini Grupla (RAM Tasarrufu)",
                        Description = "Windows 10/11'de servislerin onlarca ayrı svchost.exe açması yerine tek grupta toplanmasını sağlayarak RAM kullanımını azaltır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "TopSpeed24",
                        IsEnabled = IsSvchostGroupingEnabled()
                    },

                    // 26. Windows Installer in Safe Mode
                    new()
                    {
                        Id = "safemode_msi",
                        Category = "Davranışlar (Behavior)",
                        Title = "Güvenli Modda Windows Installer (MSI) Kurulumuna İzin Ver",
                        Description = "Windows Güvenli Modda (Safe Mode) normalde engellenen MSI paketlerinin kurulabilmesi ve kaldırılabilmesi için MSIServer servisini yetkilendirir.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "Apps24",
                        IsEnabled = CheckSafeModeMsiEnabled()
                    },

                    // 27. XMouse Options
                    new()
                    {
                        Id = "xmouse_window_tracking",
                        Category = "Davranışlar (Behavior)",
                        Title = "XMouse: Fareyle Üzerine Gelinen Pencereyi Otomatik Odakla",
                        Description = "Fare imleci bir pencerenin üzerine getirildiğinde tıklamaya gerek kalmadan pencereyi otomatik olarak aktif ve odaklı hale getirir.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "Cursor24",
                        IsEnabled = IsXMouseEnabled()
                    }
                };

                list.InsertRange(0, Bakım.Services.Tweaks.TweakEngine.ItemsFor("Davranışlar (Behavior)"));
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

                        // 7. Disable Downloads Blocking (SmartScreen Zone Identifier)
                        case "disable_zone_identifier":
                            int zoneVal = enable ? 1 : 2;
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Policies\Attachments", "SaveZoneInformation", zoneVal);
                            SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Attachments", "SaveZoneInformation", zoneVal);
                            break;

                        // 10. Disable SmartScreen
                        case "disable_smartscreen":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender\SmartScreenHost", "EnableSmartScreen", 0);
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", 0);
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender\SmartScreen", "ConfigureAppInstallControlEnabled", 0);
                                SetRegistryString(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender\SmartScreen", "ConfigureAppInstallControl", "Anywhere");
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender\SmartScreenHost", "EnableSmartScreen");
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen");
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender\SmartScreen", "ConfigureAppInstallControlEnabled");
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender\SmartScreen", "ConfigureAppInstallControl");
                            }
                            break;

                        // 12. Disable Windows Update
                        case "disable_windows_update":
                            SetWindowsUpdateState(enable);
                            break;

                        // 14. Enable Emoji Picker
                        case "enable_emoji_picker":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Input\Settings", "EnableExpressiveInputShellHotkey", enable ? 1 : 0);
                            break;

                        // 19. Redefine Extra Keys on Keyboard (Disable Caps Lock)
                        case "disable_caps_lock_key":
                            if (enable)
                            {
                                SetRegistryBinary(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Keyboard Layout", "Scancode Map", CapsLockDisableScancodeMap);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Keyboard Layout", "Scancode Map");
                            }
                            break;

                        // 23. Sound for Print Screen key
                        case "sound_print_screen":
                            string sound = enable ? @"C:\Windows\Media\Windows Ding.wav" : string.Empty;
                            SetRegistryString(Registry.CurrentUser, @"AppEvents\Schemes\Apps\.Default\SnapShot\.Current", "", sound);
                            break;

                        // 24. Split Threshold for Svchost
                        case "svchost_grouping":
                            int threshold = enable ? int.MaxValue : 3670016;
                            SetRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", threshold);
                            break;

                        // 26. Windows Installer in Safe Mode
                        case "safemode_msi":
                            if (enable)
                            {
                                SetRegistryString(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SafeBoot\Minimal\MSIServer", "", "Service");
                                SetRegistryString(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SafeBoot\Network\MSIServer", "", "Service");
                            }
                            else
                            {
                                DeleteRegistryKey(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SafeBoot\Minimal\MSIServer");
                                DeleteRegistryKey(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SafeBoot\Network\MSIServer");
                            }
                            break;

                        // 27. XMouse Options
                        case "xmouse_window_tracking":
                            SetXMouse(enable);
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

        #region Registry Helper Routines

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

        private static bool CheckRegistryBinary(RegistryKey root, string subKey, string valueName)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key == null) return false;
                var val = key.GetValue(valueName) as byte[];
                return val != null && val.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSvchostGroupingEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control");
                if (key == null) return false;
                var val = key.GetValue("SvcHostSplitThresholdInKB");
                if (val == null) return false;
                long num = Convert.ToInt64(val);
                return num > 3670016;
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckSafeModeMsiEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SafeBoot\Minimal\MSIServer");
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckPrintScreenSoundEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"AppEvents\Schemes\Apps\.Default\SnapShot\.Current");
                if (key == null) return false;
                var val = key.GetValue("")?.ToString();
                return !string.IsNullOrWhiteSpace(val);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsXMouseEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                if (key == null) return false;
                var trackVal = key.GetValue("ActiveWindowTracking");
                if (trackVal != null && Convert.ToInt32(trackVal) == 1) return true;

                var mask = key.GetValue("UserPreferencesMask") as byte[];
                if (mask != null && mask.Length > 0)
                {
                    return (mask[0] & 0x01) != 0;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void SetXMouse(bool enable)
        {
            const string desktop = @"Control Panel\Desktop";
            SetRegistryDword(Registry.CurrentUser, desktop, "ActiveWindowTracking", enable ? 1 : 0);

            byte[]? mask;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(desktop, false);
                mask = key?.GetValue("UserPreferencesMask") as byte[];
            }
            catch (Exception ex)
            {
                WriteScope.Report($@"HKEY_CURRENT_USER\{desktop} → UserPreferencesMask: {ex.Message}");
                return;
            }

            if (mask == null || mask.Length == 0) return;
            if (enable)
                mask[0] |= 0x01;
            else
                mask[0] = (byte)(mask[0] & ~0x01);

            SetRegistryBinary(Registry.CurrentUser, desktop, "UserPreferencesMask", mask);
        }

        private static void SetWindowsUpdateState(bool disableUpdates)
        {
            try
            {
                if (disableUpdates)
                {
                    SetRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\wuauserv", "Start", 4);
                    SetRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\bits", "Start", 4);
                    SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", 1);

                    RunCommandHidden("net.exe", "stop", "wuauserv", "/y");
                    RunCommandHidden("net.exe", "stop", "bits", "/y");
                }
                else
                {
                    SetRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\wuauserv", "Start", 3);
                    SetRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\bits", "Start", 3);
                    DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate");

                    RunCommandHidden("net.exe", "start", "bits");
                    RunCommandHidden("net.exe", "start", "wuauserv");
                }
            }
            catch { }
        }

        /// <summary>
        /// En iyi çaba ile hizmet durdurma/başlatma. Hizmetin kalıcı durumu "Start" değeriyle
        /// ayarlandığı için buradaki hata (ör. zaten durmuş hizmet) ince ayarı başarısız saymaz.
        /// </summary>
        private static void RunCommandHidden(string fileName, params string[] args)
        {
            ProcessRunner.Run(fileName, args, TimeSpan.FromSeconds(15));
        }

        private static bool SetRegistryDword(RegistryKey root, string subKey, string valueName, int value) =>
            VerifiedRegistry.SetDword(root, subKey, valueName, value);

        private static bool SetRegistryString(RegistryKey root, string subKey, string valueName, string value) =>
            VerifiedRegistry.SetString(root, subKey, valueName, value);

        private static bool SetRegistryBinary(RegistryKey root, string subKey, string valueName, byte[] value) =>
            VerifiedRegistry.SetBinary(root, subKey, valueName, value);

        private static bool DeleteRegistryValue(RegistryKey root, string subKey, string valueName) =>
            VerifiedRegistry.DeleteValue(root, subKey, valueName);

        private static bool DeleteRegistryKey(RegistryKey root, string subKey) =>
            VerifiedRegistry.DeleteKeyTree(root, subKey);

        #endregion
    }
}
