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

        /// <summary>
        /// Menünün görüneceği türler (KAL S-6, S-7):
        ///   • Kısayol, exe, .msi paketi, .url (Steam oyunları) — her zaman.
        ///   • Klasör — yalnızca Shift + sağ tık ("Extended"); eskiden her klasörde görünüyordu.
        /// </summary>
        private static readonly (string Key, bool ExtendedOnly)[] Targets =
        {
            (@"lnkfile\shell", false),
            (@"exefile\shell", false),
            (@"Msi.Package\shell", false),
            (@"InternetShortcut\shell", false),
            (@"Directory\shell", true)
        };

        private static string GetExePath()
        {
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath)) return exePath;
            return Path.Combine(AppContext.BaseDirectory, "Bakim.exe");
        }

        private static string CommandFor(string exePath) => $"\"{exePath}\" --uninstall-target \"%1\"";

        /// <summary>
        /// Kayıtlı ve komut GÜNCEL Bakım yolunu gösteriyorsa true (S-8). Bakım taşındı ya da
        /// güncellendiyse false döner; açılıştaki otomatik kayıt menüyü kendiliğinden onarır.
        /// </summary>
        public bool IsContextMenuRegistered()
        {
            try
            {
                string expected = CommandFor(GetExePath());
                foreach (var (key, _) in Targets)
                {
                    using var cmd = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{key}\{KeyName}\command");
                    if (!string.Equals(cmd?.GetValue("") as string, expected, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Yalnızca HKCU'ya yazar (yönetici gerekmez, kullanıcıya özel). Eskiden hem HKCU hem HKLM'e
        /// yazılıyordu; eski HKLM kopyaları yazılabiliyorsa temizlenir.
        /// </summary>
        public bool RegisterContextMenu()
        {
            string exePath = GetExePath();
            string commandStr = CommandFor(exePath);
            string iconStr = $"\"{exePath}\",0";
            bool allSuccess = true;

            foreach (var (key, extendedOnly) in Targets)
            {
                try
                {
                    using var baseKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{key}\{KeyName}");
                    baseKey.SetValue("", MenuText);
                    baseKey.SetValue("Icon", iconStr);
                    if (extendedOnly) baseKey.SetValue("Extended", string.Empty);
                    else baseKey.DeleteValue("Extended", false);

                    using var cmdKey = baseKey.CreateSubKey("command");
                    cmdKey.SetValue("", commandStr);
                }
                catch (Exception ex)
                {
                    AppLog.Warning($"Sağ tık menüsü yazılamadı: {key}", ex, nameof(ShellContextMenuService));
                    allSuccess = false;
                }

                RemoveLegacyMachineEntry(key);
            }

            return allSuccess;
        }

        private static void RemoveLegacyMachineEntry(string key)
        {
            try
            {
                using var classes = Registry.LocalMachine.OpenSubKey($@"Software\Classes\{key}", writable: true);
                classes?.DeleteSubKeyTree(KeyName, throwOnMissingSubKey: false);
            }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
            catch (IOException) { }
        }

        public bool UnregisterContextMenu()
        {
            bool anySuccess = false;

            foreach (var (key, _) in Targets)
            {
                try
                {
                    using var classes = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{key}", writable: true);
                    if (classes != null)
                    {
                        classes.DeleteSubKeyTree(KeyName, throwOnMissingSubKey: false);
                        anySuccess = true;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warning($"Sağ tık menüsü kaldırılamadı: {key}", ex, nameof(ShellContextMenuService));
                }

                RemoveLegacyMachineEntry(key);
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
