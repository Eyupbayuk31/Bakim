using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IAppearanceTweaksService
    {
        Task<List<SystemTweakItem>> GetAppearanceTweaksAsync();
        Task<bool> ApplyTweakAsync(SystemTweakItem item, bool enable);
        Task<bool> ApplyAeroLiteThemeAsync();
        Task<bool> SetInactiveTitleBarColorAsync(string hexColor);
        Task<string> GetInactiveTitleBarColorAsync();
        Task<bool> ApplyAllRecommendedAsync(IEnumerable<SystemTweakItem> items);
        Task<bool> RestoreDefaultsAsync(IEnumerable<SystemTweakItem> items);
        void BroadcastSettingsChange();
    }

    public class AppearanceTweaksService : IAppearanceTweaksService
    {
        private const string CategoryName = "Görünüm & Tema";

        #region Win32 P/Invoke for Theme / DWM Refresh

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            UIntPtr wParam,
            string lParam,
            uint fuFlags,
            uint uTimeout,
            out UIntPtr lpdwResult);

        private static readonly IntPtr HWND_BROADCAST = new(0xffff);
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint WM_THEMECHANGED = 0x031A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        public void BroadcastSettingsChange()
        {
            try
            {
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "ImmersiveColorSet", SMTO_ABORTIFHUNG, 500, out _);
                SendMessageTimeout(HWND_BROADCAST, WM_THEMECHANGED, UIntPtr.Zero, string.Empty, SMTO_ABORTIFHUNG, 500, out _);
            }
            catch { }
        }

        #endregion

        public async Task<List<SystemTweakItem>> GetAppearanceTweaksAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<SystemTweakItem>
                {
                    new()
                    {
                        Id = "app_dark_mode_all",
                        Category = CategoryName,
                        Title = "Sistem & Uygulama Koyu Tema (Dark Mode)",
                        Description = "Hem Windows kabuğunu hem de tüm yüklü modern uygulamaları senkronize koyu temaya geçirir.",
                        IconSymbol = "WeatherMoon24",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IsEnabled = IsDarkModeEnabled()
                    },
                    new()
                    {
                        Id = "app_disable_display_animations",
                        Category = CategoryName,
                        Title = "Pencere Küçültme/Büyütme Animasyonlarını Kapat",
                        Description = "Pencereler küçültülürken veya büyütülürken oynatılan animasyonları kapatarak anlık tepki hızı sağlar.",
                        IconSymbol = "PlayCircle24",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IsEnabled = IsDisplayChangeAnimationDisabled()
                    },
                    new()
                    {
                        Id = "app_inactive_titlebar_color",
                        Category = CategoryName,
                        Title = "Aktif Olmayan Başlık Çubuklarını Renklendir",
                        Description = "Arka planda kalan inaktif pencerelerin başlık çubuklarına özel tema rengi atar.",
                        IconSymbol = "Color24",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IsEnabled = IsInactiveTitleBarColorActive()
                    },
                    new()
                    {
                        Id = "app_slowdown_animations_shift",
                        Category = CategoryName,
                        Title = "Shift Tuşu ile Animasyonları Yavaşlatma (Slow-Mo)",
                        Description = "Shift tuşuna basılı tutulduğunda pencere animasyonlarını ağır çekimde oynatan DWM geliştirici modunu açar.",
                        IconSymbol = "Timer24",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IsEnabled = IsSlowdownAnimationsEnabled()
                    },
                    new()
                    {
                        Id = "app_startup_sound",
                        Category = CategoryName,
                        Title = "Windows Açılış Sesini Etkinleştir",
                        Description = "Bilgisayar açılırken klasik Windows oturum açma sesinin çalınmasını zorunlu kılar.",
                        IconSymbol = "Speaker224",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IsEnabled = IsStartupSoundEnabled()
                    },
                    new()
                    {
                        Id = "app_prevent_theme_change_mouse",
                        Category = CategoryName,
                        Title = "Temaların Fare İmleçlerini Değiştirmesini Engelle",
                        Description = "Yeni bir Windows teması uygulandığında mevcut fare imlecinin bozulmasını veya değişmesini önler.",
                        IconSymbol = "Cursor24",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IsEnabled = IsThemeMousePointerProtected()
                    },
                    new()
                    {
                        Id = "app_prevent_theme_change_icons",
                        Category = CategoryName,
                        Title = "Temaların Masaüstü Simgelerini Değiştirmesini Engelle",
                        Description = "Tema yüklendiğinde Bu Bilgisayar, Geri Dönüşüm Kutusu simgelerinin değişmesini kilitler.",
                        IconSymbol = "Desktop24",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IsEnabled = IsThemeDesktopIconsProtected()
                    },
                    new()
                    {
                        Id = "app_aerolite_theme_launcher",
                        Category = CategoryName,
                        Title = "Aero Lite Gizli Temasını Uygula",
                        Description = "Windows'ta gömülü gelen net ve yüksek kontrastlı kenarlıklara sahip Aero Lite temasını aktif eder.",
                        IconSymbol = "PaintBrush24",
                        Type = TweakType.Action,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IsEnabled = false
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
                    bool result = item.Id switch
                    {
                        "app_dark_mode_all" => SetDarkMode(enable),
                        "app_disable_display_animations" => SetDisplayChangeAnimationDisabled(enable),
                        "app_inactive_titlebar_color" => SetInactiveTitleBarColorToggle(enable),
                        "app_slowdown_animations_shift" => SetSlowdownAnimations(enable),
                        "app_startup_sound" => SetStartupSound(enable),
                        "app_prevent_theme_change_mouse" => SetThemeMousePointerProtection(enable),
                        "app_prevent_theme_change_icons" => SetThemeDesktopIconsProtection(enable),
                        "app_aerolite_theme_launcher" => ApplyAeroLiteThemeSync(),
                        _ => false
                    };

                    BroadcastSettingsChange();
                    return result;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> ApplyAeroLiteThemeAsync()
        {
            return await Task.Run(() => ApplyAeroLiteThemeSync());
        }

        private static bool ApplyAeroLiteThemeSync()
        {
            try
            {
                string themePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Resources", "Themes", "aerolite.theme");
                if (!File.Exists(themePath)) return false;

                var psi = new ProcessStartInfo
                {
                    FileName = themePath,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> GetInactiveTitleBarColorAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\DWM");
                    var val = key?.GetValue("AccentColorInactive");
                    if (val is int intVal)
                    {
                        // Convert DWORD (ABGR or ARGB) to hex
                        uint color = (uint)intVal;
                        byte r = (byte)(color & 0xFF);
                        byte g = (byte)((color >> 8) & 0xFF);
                        byte b = (byte)((color >> 16) & 0xFF);
                        return $"#{r:X2}{g:X2}{b:X2}";
                    }
                    return "#2B2B2B";
                }
                catch
                {
                    return "#2B2B2B";
                }
            });
        }

        public async Task<bool> SetInactiveTitleBarColorAsync(string hexColor)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(hexColor)) return false;
                    string hex = hexColor.TrimStart('#');
                    if (hex.Length != 6) return false;

                    byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                    byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                    byte b = Convert.ToByte(hex.Substring(4, 2), 16);

                    // Windows DWM expects 0x00BBGGRR or 0xAABBGGRR
                    uint dwmColor = 0xFF000000 | ((uint)b << 16) | ((uint)g << 8) | r;

                    using (var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\DWM"))
                    {
                        Bakım.Helpers.RegistryCapture.Track(key, "AccentColorInactive");
                        key.SetValue("AccentColorInactive", (int)dwmColor, RegistryValueKind.DWord);
                    }

                    BroadcastSettingsChange();
                    return true;
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
                bool allOk = true;
                foreach (var item in items.Where(i => i.IsRecommended))
                {
                    try
                    {
                        bool ok = item.Id switch
                        {
                            "app_dark_mode_all" => SetDarkMode(true),
                            "app_disable_display_animations" => SetDisplayChangeAnimationDisabled(true),
                            "app_prevent_theme_change_mouse" => SetThemeMousePointerProtection(true),
                            "app_prevent_theme_change_icons" => SetThemeDesktopIconsProtection(true),
                            _ => true
                        };

                        if (ok) item.IsEnabled = true;
                        else allOk = false;
                    }
                    catch
                    {
                        allOk = false;
                    }
                }

                BroadcastSettingsChange();
                return allOk;
            });
        }

        public async Task<bool> RestoreDefaultsAsync(IEnumerable<SystemTweakItem> items)
        {
            return await Task.Run(() =>
            {
                bool allOk = true;
                foreach (var item in items)
                {
                    try
                    {
                        bool ok = item.Id switch
                        {
                            "app_dark_mode_all" => SetDarkMode(false),
                            "app_disable_display_animations" => SetDisplayChangeAnimationDisabled(false),
                            "app_inactive_titlebar_color" => SetInactiveTitleBarColorToggle(false),
                            "app_slowdown_animations_shift" => SetSlowdownAnimations(false),
                            "app_startup_sound" => SetStartupSound(false),
                            "app_prevent_theme_change_mouse" => SetThemeMousePointerProtection(false),
                            "app_prevent_theme_change_icons" => SetThemeDesktopIconsProtection(false),
                            _ => true
                        };

                        if (ok) item.IsEnabled = false;
                        else allOk = false;
                    }
                    catch
                    {
                        allOk = false;
                    }
                }

                BroadcastSettingsChange();
                return allOk;
            });
        }

        #region Internal Registry Helpers

        private static bool IsDarkModeEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                int apps = Convert.ToInt32(key?.GetValue("AppsUseLightTheme") ?? 1);
                int system = Convert.ToInt32(key?.GetValue("SystemUsesLightTheme") ?? 1);
                return apps == 0 && system == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetDarkMode(bool dark)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                int val = dark ? 0 : 1; // 0 = Dark, 1 = Light
                Bakım.Helpers.RegistryCapture.Track(key, "AppsUseLightTheme");
                key.SetValue("AppsUseLightTheme", val, RegistryValueKind.DWord);
                Bakım.Helpers.RegistryCapture.Track(key, "SystemUsesLightTheme");
                key.SetValue("SystemUsesLightTheme", val, RegistryValueKind.DWord);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsDisplayChangeAnimationDisabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                return key?.GetValue("MinAnimate")?.ToString() == "0";
            }
            catch
            {
                return false;
            }
        }

        private static bool SetDisplayChangeAnimationDisabled(bool disabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop");
                Bakım.Helpers.RegistryCapture.Track(key, "MinAnimate");
                key.SetValue("MinAnimate", disabled ? "0" : "1", RegistryValueKind.String);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsInactiveTitleBarColorActive()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\DWM");
                return key?.GetValue("AccentColorInactive") != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetInactiveTitleBarColorToggle(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\DWM");
                if (enable)
                {
                    // Default dark gray inactive: 0xFF2B2B2B
                    Bakım.Helpers.RegistryCapture.Track(key, "AccentColorInactive");
                    key.SetValue("AccentColorInactive", unchecked((int)0xFF2B2B2B), RegistryValueKind.DWord);
                }
                else
                {
                    Bakım.Helpers.RegistryCapture.Track(key, "AccentColorInactive");
                    key.DeleteValue("AccentColorInactive", false);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSlowdownAnimationsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                return Convert.ToInt32(key?.GetValue("AnimationsShiftKey") ?? 0) == 1;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetSlowdownAnimations(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\DWM");
                Bakım.Helpers.RegistryCapture.Track(key, "AnimationsShiftKey");
                key.SetValue("AnimationsShiftKey", enable ? 1 : 0, RegistryValueKind.DWord);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsStartupSoundEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\BootAnimation");
                return Convert.ToInt32(key?.GetValue("DisableStartupSound") ?? 1) == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetStartupSound(bool enable)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\BootAnimation");
                Bakım.Helpers.RegistryCapture.Track(key, "DisableStartupSound");
                key.SetValue("DisableStartupSound", enable ? 0 : 1, RegistryValueKind.DWord);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsThemeMousePointerProtected()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes");
                return Convert.ToInt32(key?.GetValue("ThemeChangesMousePointers") ?? 1) == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetThemeMousePointerProtection(bool protect)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes");
                Bakım.Helpers.RegistryCapture.Track(key, "ThemeChangesMousePointers");
                key.SetValue("ThemeChangesMousePointers", protect ? 0 : 1, RegistryValueKind.DWord);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsThemeDesktopIconsProtected()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes");
                return Convert.ToInt32(key?.GetValue("ThemeChangesDesktopIcons") ?? 1) == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetThemeDesktopIconsProtection(bool protect)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes");
                Bakım.Helpers.RegistryCapture.Track(key, "ThemeChangesDesktopIcons");
                key.SetValue("ThemeChangesDesktopIcons", protect ? 0 : 1, RegistryValueKind.DWord);
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
