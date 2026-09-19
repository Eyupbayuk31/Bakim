using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IAdvancedAppearanceService
    {
        Task<WindowMetricsData> GetWindowMetricsAsync();
        Task<bool> SaveWindowMetricsAsync(WindowMetricsData data);
        Task<bool> ResetToDefaultsAsync();
        void BroadcastMetricsChange();
    }

    public class AdvancedAppearanceService : IAdvancedAppearanceService
    {
        private const string WindowMetricsKey = @"Control Panel\Desktop\WindowMetrics";

        #region Win32 P/Invoke & Constants

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
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        public void BroadcastMetricsChange()
        {
            try
            {
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "WindowMetrics", SMTO_ABORTIFHUNG, 1000, out _);
            }
            catch { }
        }

        #endregion

        public async Task<WindowMetricsData> GetWindowMetricsAsync()
        {
            return await Task.Run(() =>
            {
                var data = new WindowMetricsData();
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(WindowMetricsKey);
                    if (key != null)
                    {
                        data.IconSpacing = TwipsToPixels(key.GetValue("IconSpacing")?.ToString(), 75);
                        data.IconVerticalSpacing = TwipsToPixels(key.GetValue("IconVerticalSpacing")?.ToString(), 75);
                        data.ScrollWidth = TwipsToPixels(key.GetValue("ScrollWidth")?.ToString(), 17);
                        data.BorderWidth = TwipsToPixels(key.GetValue("BorderWidth")?.ToString(), 1);
                        data.PaddedBorderWidth = TwipsToPixels(key.GetValue("PaddedBorderWidth")?.ToString(), 4);
                        data.CaptionHeight = TwipsToPixels(key.GetValue("CaptionHeight")?.ToString(), 22);
                        data.MenuHeight = TwipsToPixels(key.GetValue("MenuHeight")?.ToString(), 19);
                    }

                    // Inactive title bar color
                    using var dwmKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\DWM");
                    var val = dwmKey?.GetValue("AccentColorInactive");
                    if (val is int intVal)
                    {
                        uint c = (uint)intVal;
                        byte r = (byte)(c & 0xFF);
                        byte g = (byte)((c >> 8) & 0xFF);
                        byte b = (byte)((c >> 16) & 0xFF);
                        data.InactiveTitleBarHex = $"#{r:X2}{g:X2}{b:X2}";
                    }
                    else
                    {
                        data.InactiveTitleBarHex = "#2B2B2B";
                    }
                }
                catch
                {
                    // Fallback to standard defaults
                }
                return data;
            });
        }

        public async Task<bool> SaveWindowMetricsAsync(WindowMetricsData data)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(WindowMetricsKey))
                    {
                        key.SetValue("IconSpacing", PixelsToTwips(data.IconSpacing), RegistryValueKind.String);
                        key.SetValue("IconVerticalSpacing", PixelsToTwips(data.IconVerticalSpacing), RegistryValueKind.String);
                        key.SetValue("ScrollWidth", PixelsToTwips(data.ScrollWidth), RegistryValueKind.String);
                        key.SetValue("ScrollHeight", PixelsToTwips(data.ScrollWidth), RegistryValueKind.String);
                        key.SetValue("BorderWidth", PixelsToTwips(data.BorderWidth), RegistryValueKind.String);
                        key.SetValue("PaddedBorderWidth", PixelsToTwips(data.PaddedBorderWidth), RegistryValueKind.String);
                        key.SetValue("CaptionHeight", PixelsToTwips(data.CaptionHeight), RegistryValueKind.String);
                        key.SetValue("CaptionWidth", PixelsToTwips(data.CaptionHeight), RegistryValueKind.String);
                        key.SetValue("MenuHeight", PixelsToTwips(data.MenuHeight), RegistryValueKind.String);
                        key.SetValue("MenuWidth", PixelsToTwips(data.MenuHeight), RegistryValueKind.String);
                    }

                    // Save Inactive Title Bar color if valid hex
                    if (!string.IsNullOrWhiteSpace(data.InactiveTitleBarHex))
                    {
                        string hex = data.InactiveTitleBarHex.TrimStart('#');
                        if (hex.Length == 6)
                        {
                            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                            uint dwmColor = 0xFF000000 | ((uint)b << 16) | ((uint)g << 8) | r;

                            using var dwmKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\DWM");
                            dwmKey.SetValue("AccentColorInactive", (int)dwmColor, RegistryValueKind.DWord);
                        }
                    }

                    BroadcastMetricsChange();
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> ResetToDefaultsAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(WindowMetricsKey))
                    {
                        // Default Windows 10/11 factory metrics (twips = -15 * pixels)
                        key.SetValue("IconSpacing", "-1125", RegistryValueKind.String); // 75px
                        key.SetValue("IconVerticalSpacing", "-1125", RegistryValueKind.String); // 75px
                        key.SetValue("ScrollWidth", "-255", RegistryValueKind.String); // 17px
                        key.SetValue("ScrollHeight", "-255", RegistryValueKind.String); // 17px
                        key.SetValue("BorderWidth", "-15", RegistryValueKind.String); // 1px
                        key.SetValue("PaddedBorderWidth", "-60", RegistryValueKind.String); // 4px
                        key.SetValue("CaptionHeight", "-330", RegistryValueKind.String); // 22px
                        key.SetValue("CaptionWidth", "-330", RegistryValueKind.String); // 22px
                        key.SetValue("MenuHeight", "-285", RegistryValueKind.String); // 19px
                        key.SetValue("MenuWidth", "-285", RegistryValueKind.String); // 19px
                    }

                    // Reset inactive title bar color
                    try
                    {
                        using var dwmKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\DWM");
                        dwmKey.DeleteValue("AccentColorInactive", false);
                    }
                    catch { }

                    BroadcastMetricsChange();
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        private static int TwipsToPixels(string? twipsStr, int defaultPixels)
        {
            if (int.TryParse(twipsStr, out int twips))
            {
                int px = Math.Abs(twips) / 15;
                return px > 0 ? px : defaultPixels;
            }
            return defaultPixels;
        }

        private static string PixelsToTwips(int pixels)
        {
            int p = Math.Max(1, pixels);
            return (-(p * 15)).ToString();
        }
    }
}
