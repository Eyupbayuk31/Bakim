using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Bakım.Helpers
{
    /// <summary>
    /// IShellLinkW ile .lnk kısayolu oluşturur ve hedefini çözer (betik dosyası ya da WScript
    /// gerektirmez). Kalıcılık taraması, sağ tık kaldırma ve kaldırıcı iz toplama aynı çözücüyü kullanır.
    /// </summary>
    public static class ShellLink
    {
        public const int ShowMinimizedNoActive = 7;

        public static void Create(string shortcutPath, string targetPath, string arguments, string? iconPath,
            string description, int showCommand = 1)
        {
            var link = (IShellLinkW)new CShellLink();
            try
            {
                link.SetPath(targetPath);
                link.SetArguments(arguments);
                link.SetDescription(description);
                link.SetShowCmd(showCommand);
                if (!string.IsNullOrEmpty(iconPath)) link.SetIconLocation(iconPath, 0);
                ((IPersistFile)link).Save(shortcutPath, true);
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }

        /// <summary>Kısayolun hedef yolu; çözülemezse (bozuk, reklam/MSI kısayolu) null.</summary>
        public static string? ResolveTarget(string shortcutPath)
        {
            if (string.IsNullOrWhiteSpace(shortcutPath)) return null;
            IShellLinkW? link = null;
            try
            {
                link = (IShellLinkW)new CShellLink();
                ((IPersistFile)link).Load(shortcutPath, 0); // STGM_READ
                var buffer = new StringBuilder(1024);
                link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
                string target = buffer.ToString();
                return string.IsNullOrWhiteSpace(target) ? null : target;
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or System.IO.IOException or ArgumentException or InvalidCastException)
            {
                Services.AppLog.Debug($"Kısayol çözülemedi: {shortcutPath} — {ex.Message}", nameof(ShellLink));
                return null;
            }
            finally
            {
                if (link != null) Marshal.FinalReleaseComObject(link);
            }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink
        {
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
    }
}
