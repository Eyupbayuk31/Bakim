using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Bakım.Helpers;

namespace Bakım.Services
{
    public enum AutostartHealthState
    {
        TaskSchedulerActive,
        RegistryRunActive,
        BlockedByAppCompatAdmin,
        Disabled
    }

    /// <summary>
    /// Uygulamanın kalıcı yönetici haklarıyla başlatılmasını (AppCompatFlags RUNASADMIN),
    /// UAC istemini bypass eden Görev Zamanlayıcı (Task Scheduler) otomasyonunu ve
    /// Windows başlangıç sağlığı teşhisini yöneten servis.
    /// </summary>
    public static class AdminElevationService
    {
        private const string AppCompatKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
        private const string TaskName = "Bakım_Admin_AutoStart";
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

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
                if (proc.ExitCode == 0) return true;

                // ASCII yedek ad kontrolü
                using var proc2 = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = "/query /tn \"Bakim_Admin_AutoStart\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                proc2.Start();
                proc2.WaitForExit(2000);
                return proc2.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static string GenerateTaskXml(string exePath)
        {
            string cleanExe = exePath.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
            string workDir = Path.GetDirectoryName(exePath) ?? string.Empty;
            string cleanDir = workDir.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

            return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Bakım Sistem Optimizasyon ve Güvenlik Aracı - Otomatik Yönetici Başlangıç Görevi</Description>
    <Author>Bakım</Author>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>4</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{cleanExe}</Command>
      <Arguments>--autostart</Arguments>
      <WorkingDirectory>{cleanDir}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
        }

        /// <summary>
        /// Task Scheduler XML kullanarak tırnak/boşluk/Türkçe karakter hatası olmadan
        /// görevi garantili olarak kaydeder (Yönetici yetkisi gerektirir).
        /// </summary>
        public static bool RegisterTaskSchedulerInternal()
        {
            string tempXml = Path.Combine(Path.GetTempPath(), $"Bakim_Task_{Guid.NewGuid():N}.xml");
            try
            {
                string xml = GenerateTaskXml(GetExePath());
                File.WriteAllText(tempXml, xml, System.Text.Encoding.Unicode);

                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/create /tn \"{TaskName}\" /xml \"{tempXml}\" /f",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                proc.Start();
                proc.WaitForExit(4000);
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { if (File.Exists(tempXml)) File.Delete(tempXml); } catch { }
            }
        }

        public static bool UnregisterTaskSchedulerInternal()
        {
            try
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

                try
                {
                    using var proc2 = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "schtasks.exe",
                            Arguments = "/delete /tn \"Bakim_Admin_AutoStart\" /f",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        }
                    };
                    proc2.Start();
                    proc2.WaitForExit(2000);
                }
                catch { }

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
        /// Standart kullanıcı modundaysa tek seferlik sessiz UAC istemiyle kendini yükselterek kaydeder.
        /// </summary>
        public static bool SetTaskSchedulerAutoStart(bool enable)
        {
            if (UacHelper.IsAdministrator())
            {
                bool ok = enable ? RegisterTaskSchedulerInternal() : UnregisterTaskSchedulerInternal();
                if (enable && ok)
                {
                    RemoveRegistryRunKey();
                }
                return ok;
            }

            try
            {
                string exePath = GetExePath();
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = enable ? "--register-autostart" : "--unregister-autostart",
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(6000);

                if (enable && IsTaskSchedulerAutoStartEnabled())
                {
                    RemoveRegistryRunKey();
                    return true;
                }
                return !enable && !IsTaskSchedulerAutoStartEnabled();
            }
            catch
            {
                return false;
            }
        }

        public static bool HasRegistryRunKey()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
                return key?.GetValue("BakimApp") != null || key?.GetValue("BakımApp") != null || key?.GetValue("Bakim") != null;
            }
            catch
            {
                return false;
            }
        }

        public static void RemoveRegistryRunKey()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                if (key != null)
                {
                    if (key.GetValue("BakimApp") != null) key.DeleteValue("BakimApp", false);
                    if (key.GetValue("BakımApp") != null) key.DeleteValue("BakımApp", false);
                    if (key.GetValue("Bakim") != null) key.DeleteValue("Bakim", false);
                }
            }
            catch { }
        }

        public static AutostartHealthState GetAutostartHealthState()
        {
            if (IsTaskSchedulerAutoStartEnabled())
            {
                return AutostartHealthState.TaskSchedulerActive;
            }

            bool hasRegistryRun = HasRegistryRunKey();
            if (hasRegistryRun)
            {
                bool hasRunAsAdmin = IsAlwaysRunAsAdminEnabled();
                if (hasRunAsAdmin)
                {
                    return AutostartHealthState.BlockedByAppCompatAdmin;
                }
                return AutostartHealthState.RegistryRunActive;
            }

            return AutostartHealthState.Disabled;
        }

        public static bool RepairAutostartInternal()
        {
            RemoveRegistryRunKey();
            return RegisterTaskSchedulerInternal();
        }

        public static bool RepairAutostartConfiguration()
        {
            if (UacHelper.IsAdministrator())
            {
                return RepairAutostartInternal();
            }

            try
            {
                string exePath = GetExePath();
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "--repair-autostart",
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(6000);
                return IsTaskSchedulerAutoStartEnabled();
            }
            catch
            {
                return false;
            }
        }
    }
}
