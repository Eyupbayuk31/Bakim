using System;
using System.Diagnostics;
using Microsoft.Win32;

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

            return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') + @"\Bakım.exe";
        }

        /// <summary>
        /// HKCU Compatibility Layer üzerinde RUNASADMIN bayrağının etkin olup olmadığını kontrol eder.
        /// </summary>
        public static bool IsAlwaysRunAsAdminEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppCompatKey, false);
                string exePath = GetExePath();
                var value = key?.GetValue(exePath) as string;
                return value != null && value.Contains("RUNASADMIN", StringComparison.OrdinalIgnoreCase);
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
                using var key = Registry.CurrentUser.OpenSubKey(AppCompatKey, true) 
                               ?? Registry.CurrentUser.CreateSubKey(AppCompatKey);
                string exePath = GetExePath();

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
                return true;
            }
            catch
            {
                return false;
            }
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
