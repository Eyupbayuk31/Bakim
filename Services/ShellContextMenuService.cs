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
            string shellExe = Path.Combine(AppContext.BaseDirectory, "BakimShell.exe");
            if (File.Exists(shellExe)) return shellExe;

            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath)) return exePath;
            return Path.Combine(AppContext.BaseDirectory, "Bakim.exe");
        }

        private static string CommandFor(string exePath) => $"\"{exePath}\" --uninstall-target \"%1\"";

        /// <summary>"Bakım ile analiz et" (ANL Faz 5): tüm dosya türlerinde, Analizör'de dosya analizi açar.</summary>
        private const string AnalyzeKeyName = "BakimAnalyze";
        private const string AnalyzeMenuText = "Bakım ile analiz et";
        private const string AnalyzeTarget = @"*\shell";

        private static string AnalyzeCommandFor(string exePath) => $"\"{exePath}\" --analyze-target \"%1\"";

        private static bool IsAnalyzeRegistered(string exePath)
        {
            try
            {
                using var cmd = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{AnalyzeTarget}\{AnalyzeKeyName}\command");
                return string.Equals(cmd?.GetValue("") as string, AnalyzeCommandFor(exePath), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                AppLog.Debug($"Analiz menüsü okunamadı: {ex.Message}", nameof(ShellContextMenuService));
                return false;
            }
        }

        private static bool RegisterAnalyze(string exePath)
        {
            try
            {
                using var baseKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{AnalyzeTarget}\{AnalyzeKeyName}");
                baseKey.SetValue("", AnalyzeMenuText);
                baseKey.SetValue("Icon", $"\"{exePath}\",0");
                using var cmdKey = baseKey.CreateSubKey("command");
                cmdKey.SetValue("", AnalyzeCommandFor(exePath));
                return true;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                AppLog.Warning("\"Bakım ile analiz et\" menüsü yazılamadı.", ex, nameof(ShellContextMenuService));
                return false;
            }
        }

        private static void UnregisterAnalyze()
        {
            try
            {
                using var classes = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{AnalyzeTarget}", writable: true);
                classes?.DeleteSubKeyTree(AnalyzeKeyName, throwOnMissingSubKey: false);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                AppLog.Warning("\"Bakım ile analiz et\" menüsü kaldırılamadı.", ex, nameof(ShellContextMenuService));
            }
        }

        /// <summary>
        /// Kayıtlı ve komut GÜNCEL Bakım yolunu gösteriyorsa true (S-8). Bakım taşındı ya da
        /// güncellendiyse false döner; açılıştaki otomatik kayıt menüyü kendiliğinden onarır.
        /// </summary>
        public bool IsContextMenuRegistered()
        {
            try
            {
                string exe = GetExePath();
                if (!IsAnalyzeRegistered(exe)) return false;
                string expected = CommandFor(exe);
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
            bool allSuccess = RegisterAnalyze(exePath);

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
            UnregisterAnalyze();

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
