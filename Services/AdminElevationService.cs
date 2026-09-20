using System;
using System.Diagnostics;
using Microsoft.Win32;
using Bakım.Helpers;

namespace Bakım.Services
{
    /// <summary>
    /// Uygulamanın kalıcı yönetici haklarıyla başlatılmasını (AppCompatFlags RUNASADMIN) 
    /// ve UAC istemini bypass eden Görev Zamanlayıcı (Task Scheduler) otomasyonunu yöneten servis.
    /// </summary>
    public static class AdminElevationService
    {
        private const string AppCompatKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
        private const string TaskName = "Bakım_Admin_AutoStart";

        public static string GetExePath()
        {
            try
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath)) return processPath;

                var mainModule = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(mainModule)) return mainModule;
            }
            catch { }

            return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') + @"\Bakim.exe";
        }

        /// <summary>
        /// HKCU veya HKLM Compatibility Layer üzerinde RUNASADMIN bayrağının etkin olup olmadığını kontrol eder.
        /// </summary>
        public static bool IsAlwaysRunAsAdminEnabled()
        {
            try
            {
                string exePath = GetExePath();

                // 1. Önce HKCU kontrol edilir
                using (var key = Registry.CurrentUser.OpenSubKey(AppCompatKey, false))
                {
                    var value = key?.GetValue(exePath) as string;
                    if (value != null && value.Contains("RUNASADMIN", StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                // 2. Ardından HKLM kontrol edilir (Installer tarafından kurulduysa)
                using (var key = Registry.LocalMachine.OpenSubKey(AppCompatKey, false))
                {
                    var value = key?.GetValue(exePath) as string;
                    if (value != null && value.Contains("RUNASADMIN", StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Uygulamanın nereden açılırsa açılsın her zaman Yönetici olarak çalışmasını sağlar/kapatır.
        /// </summary>
        public static bool SetAlwaysRunAsAdmin(bool enable)
        {
            try
            {
                string exePath = GetExePath();

                // HKCU Ayarı
                using (var key = Registry.CurrentUser.OpenSubKey(AppCompatKey, true) 
                               ?? Registry.CurrentUser.CreateSubKey(AppCompatKey))
                {
                    if (enable)
                    {
                        key.SetValue(exePath, "~ RUNASADMIN");
                    }
                    else
                    {
                        if (key.GetValue(exePath) != null)
                        {
                            key.DeleteValue(exePath, false);
                        }
                    }
                }

                // Eğer yönetici haklarıyla çalışıyorsa HKLM kaydını da güncelle
                if (UacHelper.IsAdministrator())
                {
                    try
                    {
                        using var keyL = Registry.LocalMachine.OpenSubKey(AppCompatKey, true);
                        if (keyL != null)
                        {
                            if (enable)
                            {
                                keyL.SetValue(exePath, "~ RUNASADMIN");
                            }
                            else if (keyL.GetValue(exePath) != null)
                            {
                                keyL.DeleteValue(exePath, false);
                            }
                        }
                    }
                    catch { }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Uygulamanın Inno Setup ile resmi olarak kurulup kurulmadığını tespit eder.
        /// </summary>
        public static bool IsInstalledApplication()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (baseDir.Contains("Program Files", StringComparison.OrdinalIgnoreCase))
                    return true;

                const string innoAppId = "{D8E5F678-31A9-4B5C-8D12-9A4E2B5C6D7E}_is1";
                string uninstallKey = $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{innoAppId}";

                using var key = Registry.LocalMachine.OpenSubKey(uninstallKey, false) 
                             ?? Registry.CurrentUser.OpenSubKey(uninstallKey, false);

                return key != null;
            }
            catch
            {
                return false;
            }
        }

        public static string GetInstallationStatusText()
        {
            return IsInstalledApplication() 
                ? "Resmi Kurulum (Program Files / Standart Windows Uygulaması)" 
                : "Taşınabilir Mod (Portable / Bağımsız Çalışma)";
        }

        /// <summary>
        /// Windows Görev Zamanlayıcı üzerinde UAC bypass görev durumunu denetler.
        /// </summary>
        public static bool IsTaskSchedulerAutoStartEnabled()
        {
            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/query /tn \"{TaskName}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                proc.Start();
                proc.WaitForExit(2000);
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Sistem açılışında UAC uyarısı vermeden en yüksek yetkiyle (RunLevel.Highest) 
        /// arka planda başlatılmasını sağlayan Görev Zamanlayıcı kaydını oluşturur/siler.
        /// </summary>
        public static bool SetTaskSchedulerAutoStart(bool enable)
        {
            try
            {
                string exePath = GetExePath();

                if (enable)
                {
                    using var proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "schtasks.exe",
                            Arguments = $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\"\" /sc onlogon /rl highest /f",
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };
                    proc.Start();
                    proc.WaitForExit(3000);
                    return proc.ExitCode == 0;
                }
                else
                {
                    using var proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "schtasks.exe",
                            Arguments = $"/delete /tn \"{TaskName}\" /f",
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };
                    proc.Start();
                    proc.WaitForExit(3000);
                    return proc.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
