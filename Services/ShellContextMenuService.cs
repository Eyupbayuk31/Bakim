using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace Bakım.Services
{
    public interface IShellContextMenuService
    {
        bool IsContextMenuRegistered();
        bool RegisterContextMenu();
        bool UnregisterContextMenu();
        bool ToggleContextMenu();
    }

    public class ShellContextMenuService : IShellContextMenuService
    {
        private const string KeyName = "BakimUninstall";
        private const string MenuText = "Bakım ile Kaldır";

        private static readonly string[] SubTargetKeys = new[]
        {
            @"lnkfile\shell",
            @"exefile\shell",
            @"Directory\shell"
        };

        private static string GetExePath()
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    return exePath;
                }
            }
            catch { }

            string fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Bakim.exe");
            return fallback;
        }

        public bool IsContextMenuRegistered()
        {
            try
            {
                // Check in HKCU first
                using var hkcu = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{SubTargetKeys[0]}\{KeyName}");
                if (hkcu != null) return true;

                // Check in HKLM
                using var hklm = Registry.LocalMachine.OpenSubKey($@"Software\Classes\{SubTargetKeys[0]}\{KeyName}");
                if (hklm != null) return true;

                // Check in ClassesRoot
                using var hkcr = Registry.ClassesRoot.OpenSubKey($@"{SubTargetKeys[0]}\{KeyName}");
                return hkcr != null;
            }
            catch
            {
                return false;
            }
        }

        public bool RegisterContextMenu()
        {
            string exePath = GetExePath();
            string commandStr = $"\"{exePath}\" --uninstall-target \"%1\"";
            string iconStr = $"\"{exePath}\",0";

            bool allSuccess = true;

            foreach (var subKey in SubTargetKeys)
            {
                bool registered = false;

                // 1. Try HKCU (Doesn't need admin elevation, user-specific)
                try
                {
                    using var baseKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{subKey}\{KeyName}");
                    if (baseKey != null)
                    {
                        baseKey.SetValue("", MenuText);
                        baseKey.SetValue("Icon", iconStr);

                        using var cmdKey = baseKey.CreateSubKey("command");
                        cmdKey?.SetValue("", commandStr);
                        registered = true;
                    }
                }
                catch { }

                // 2. Try HKLM (System-wide if admin)
                try
                {
                    using var hklmBase = Registry.LocalMachine.CreateSubKey($@"Software\Classes\{subKey}\{KeyName}");
                    if (hklmBase != null)
                    {
                        hklmBase.SetValue("", MenuText);
                        hklmBase.SetValue("Icon", iconStr);

                        using var cmdKey = hklmBase.CreateSubKey("command");
                        cmdKey?.SetValue("", commandStr);
                        registered = true;
                    }
                }
                catch { }

                if (!registered)
                {
                    allSuccess = false;
                }
            }

            return allSuccess;
        }

        public bool UnregisterContextMenu()
        {
            bool anySuccess = false;

            foreach (var subKey in SubTargetKeys)
            {
                // Delete from HKCU
                try
                {
                    using var classes = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{subKey}", writable: true);
                    if (classes != null)
                    {
                        classes.DeleteSubKeyTree(KeyName, throwOnMissingSubKey: false);
                        anySuccess = true;
                    }
                }
                catch { }

                // Delete from HKLM
                try
                {
                    using var classes = Registry.LocalMachine.OpenSubKey($@"Software\Classes\{subKey}", writable: true);
                    if (classes != null)
                    {
                        classes.DeleteSubKeyTree(KeyName, throwOnMissingSubKey: false);
                        anySuccess = true;
                    }
                }
                catch { }
            }

            return anySuccess;
        }

        public bool ToggleContextMenu()
        {
            if (IsContextMenuRegistered())
            {
                UnregisterContextMenu();
                return false;
            }
            else
            {
                RegisterContextMenu();
                return true;
            }
        }
    }
}
