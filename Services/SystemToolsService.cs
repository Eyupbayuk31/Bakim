using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public sealed record TrustedInstallerLaunchOutcome(bool Succeeded, bool IsTrustedInstaller, string Message);

    public interface ISystemToolsService
    {
        Task<bool> ResetCachesAndRestartExplorerAsync();
        Task<TrustedInstallerLaunchOutcome> LaunchAsTrustedInstallerAsync(string programPath, string arguments = "");
        Task<OemInfoData> GetOemInfoAsync();
        Task<bool> SaveOemInfoAsync(OemInfoData data);
        Task<bool> ResetLocalGroupPolicyAsync();
    }

    public class SystemToolsService : ISystemToolsService
    {
        #region Win32 P/Invoke for TrustedInstaller Execution

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool DuplicateTokenEx(
            IntPtr hExistingToken,
            uint dwDesiredAccess,
            IntPtr lpTokenAttributes,
            int ImpersonationLevel,
            int TokenType,
            out IntPtr phNewToken);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessWithTokenW(
            IntPtr hToken,
            uint dwLogonFlags,
            string? lpApplicationName,
            string lpCommandLine,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint MAXIMUM_ALLOWED = 0x02000000;
        private const int SecurityImpersonation = 2;
        private const int TokenPrimary = 1;

        #endregion

        public async Task<bool> ResetCachesAndRestartExplorerAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 1. Explorer süreçlerini sonlandır
                    foreach (var proc in Process.GetProcessesByName("explorer"))
                    {
                        try { proc.Kill(); proc.WaitForExit(3000); } catch { }
                    }

                    Thread.Sleep(500);

                    // 2. Icon & Thumbnail önbellek dosyalarını temizle
                    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string explorerCacheDir = Path.Combine(localAppData, @"Microsoft\Windows\Explorer");

                    // %LocalAppData%\IconCache.db
                    string iconCacheDb = Path.Combine(localAppData, "IconCache.db");
                    DeleteFileSafe(iconCacheDb);

                    // %LocalAppData%\Microsoft\Windows\Explorer\iconcache* & thumbcache*
                    if (Directory.Exists(explorerCacheDir))
                    {
                        try
                        {
                            foreach (var f in Directory.GetFiles(explorerCacheDir, "iconcache*"))
                            {
                                DeleteFileSafe(f);
                            }
                            foreach (var f in Directory.GetFiles(explorerCacheDir, "thumbcache*"))
                            {
                                DeleteFileSafe(f);
                            }
                        }
                        catch { }
                    }

                    // 3. Tray Notify akışlarını temizle
                    try
                    {
                        using var trayKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\TrayNotify", true);
                        trayKey?.DeleteValue("IconStreams", false);
                        trayKey?.DeleteValue("PastIconsStream", false);
                    }
                    catch { }

                    // 4. Explorer'ı yeniden başlat
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        UseShellExecute = true
                    });

                    return true;
                }
                catch
                {
                    // Her halükarda Explorer'ı açmayı dene
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true });
                    }
                    catch { }

                    return false;
                }
            });
        }

        public async Task<TrustedInstallerLaunchOutcome> LaunchAsTrustedInstallerAsync(string programPath, string arguments = "")
        {
            return await Task.Run(() =>
            {
                string target = string.IsNullOrWhiteSpace(programPath) ? "cmd.exe" : programPath;
                string cmdLine = string.IsNullOrWhiteSpace(arguments) ? $"\"{target}\"" : $"\"{target}\" {arguments}";

                try
                {
                    // 1. TrustedInstaller servisini başlat
                    try
                    {
                        var psiSc = new ProcessStartInfo
                        {
                            FileName = "sc.exe",
                            Arguments = "start TrustedInstaller",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var procSc = Process.Start(psiSc);
                        procSc?.WaitForExit(3000);
                        Thread.Sleep(500);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Debug($"TrustedInstaller servisini başlatma çağrısı: {ex.Message}", nameof(SystemToolsService));
                    }

                    // 2. TrustedInstaller.exe sürecini bul
                    var tiProcesses = Process.GetProcessesByName("TrustedInstaller");
                    if (tiProcesses.Length == 0)
                    {
                        // Fallback: Standart yönetici olarak başlat ve açıkça bildir (DEN G-6 / S-17)
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = target,
                                Arguments = arguments,
                                UseShellExecute = true,
                                Verb = "runas"
                            });
                            return new TrustedInstallerLaunchOutcome(true, false,
                                $"TrustedInstaller servisine ulaşılamadı; '{target}' standart Yönetici (Administrator) yetkisiyle başlatıldı.");
                        }
                        catch (Exception ex)
                        {
                            return new TrustedInstallerLaunchOutcome(false, false,
                                $"TrustedInstaller ve yönetici çalıştırma başarısız oldu: {ex.Message}");
                        }
                    }

                    int tiPid = tiProcesses[0].Id;
                    IntPtr hProcess = OpenProcess(PROCESS_QUERY_INFORMATION, false, tiPid);
                    if (hProcess == IntPtr.Zero)
                    {
                        return FallbackToAdmin(target, arguments, "TrustedInstaller sürecine erişilemedi");
                    }

                    try
                    {
                        if (!OpenProcessToken(hProcess, MAXIMUM_ALLOWED, out IntPtr hToken))
                        {
                            return FallbackToAdmin(target, arguments, "TrustedInstaller belirteci açılamadı");
                        }

                        try
                        {
                            if (!DuplicateTokenEx(hToken, MAXIMUM_ALLOWED, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out IntPtr hNewToken))
                            {
                                return FallbackToAdmin(target, arguments, "TrustedInstaller belirteci kopyalanamadı");
                            }

                            try
                            {
                                var si = new STARTUPINFO();
                                si.cb = Marshal.SizeOf(si);
                                si.lpDesktop = @"Winsta0\Default";

                                bool ok = CreateProcessWithTokenW(
                                    hNewToken,
                                    0,
                                    null,
                                    cmdLine,
                                    0,
                                    IntPtr.Zero,
                                    null,
                                    ref si,
                                    out PROCESS_INFORMATION pi);

                                if (ok)
                                {
                                    CloseHandle(pi.hProcess);
                                    CloseHandle(pi.hThread);
                                    return new TrustedInstallerLaunchOutcome(true, true,
                                        $"'{target}' başarıyla NT AUTHORITY\\TrustedInstaller yetkisiyle başlatıldı!");
                                }

                                return FallbackToAdmin(target, arguments, "CreateProcessWithTokenW başarısız oldu");
                            }
                            finally
                            {
                                CloseHandle(hNewToken);
                            }
                        }
                        finally
                        {
                            CloseHandle(hToken);
                        }
                    }
                    finally
                    {
                        CloseHandle(hProcess);
                    }
                }
                catch (Exception ex)
                {
                    return new TrustedInstallerLaunchOutcome(false, false, $"Hata: {ex.Message}");
                }
            });
        }

        private static TrustedInstallerLaunchOutcome FallbackToAdmin(string target, string arguments, string reason)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas"
                });
                return new TrustedInstallerLaunchOutcome(true, false,
                    $"{reason}; '{target}' standart Yönetici (Administrator) yetkisiyle başlatıldı.");
            }
            catch (Exception ex)
            {
                return new TrustedInstallerLaunchOutcome(false, false, $"{reason} ve yönetici çalıştırma da başarısız oldu: {ex.Message}");
            }
        }

        public async Task<OemInfoData> GetOemInfoAsync()
        {
            return await Task.Run(() =>
            {
                var data = new OemInfoData();
                try
                {
                    // 1. OEM Bilgileri
                    using var oemKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\OEMInformation");
                    if (oemKey != null)
                    {
                        data.Manufacturer = oemKey.GetValue("Manufacturer")?.ToString() ?? string.Empty;
                        data.Model = oemKey.GetValue("Model")?.ToString() ?? string.Empty;
                        data.SupportHours = oemKey.GetValue("SupportHours")?.ToString() ?? string.Empty;
                        data.SupportPhone = oemKey.GetValue("SupportPhone")?.ToString() ?? string.Empty;
                        data.SupportURL = oemKey.GetValue("SupportURL")?.ToString() ?? string.Empty;
                    }

                    // 2. Kayıtlı Kullanıcı & Kurum
                    using var ntKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                    if (ntKey != null)
                    {
                        data.RegisteredOwner = ntKey.GetValue("RegisteredOwner")?.ToString() ?? Environment.UserName;
                        data.RegisteredOrganization = ntKey.GetValue("RegisteredOrganization")?.ToString() ?? string.Empty;
                    }
                }
                catch { }

                return data;
            });
        }

        public async Task<bool> SaveOemInfoAsync(OemInfoData data)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 1. OEM Bilgilerini Kaydet
                    using var oemKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\OEMInformation", true);
                    if (oemKey != null)
                    {
                        oemKey.SetValue("Manufacturer", data.Manufacturer ?? "", RegistryValueKind.String);
                        oemKey.SetValue("Model", data.Model ?? "", RegistryValueKind.String);
                        oemKey.SetValue("SupportHours", data.SupportHours ?? "", RegistryValueKind.String);
                        oemKey.SetValue("SupportPhone", data.SupportPhone ?? "", RegistryValueKind.String);
                        oemKey.SetValue("SupportURL", data.SupportURL ?? "", RegistryValueKind.String);
                    }

                    // 2. Kayıtlı Sahip Bilgilerini Kaydet
                    using var ntKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", true);
                    if (ntKey != null)
                    {
                        ntKey.SetValue("RegisteredOwner", data.RegisteredOwner ?? "", RegistryValueKind.String);
                        ntKey.SetValue("RegisteredOrganization", data.RegisteredOrganization ?? "", RegistryValueKind.String);
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> ResetLocalGroupPolicyAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    string gpoMachine = Path.Combine(winDir, @"System32\GroupPolicy\Machine");
                    string gpoUser = Path.Combine(winDir, @"System32\GroupPolicy\User");
                    string gpoUsers = Path.Combine(winDir, @"System32\GroupPolicyUsers");

                    DeleteDirectorySafe(gpoMachine);
                    DeleteDirectorySafe(gpoUser);
                    DeleteDirectorySafe(gpoUsers);

                    // secedit ile varsayılan ilke şablonunu uygula
                    var psiSecedit = new ProcessStartInfo
                    {
                        FileName = "secedit.exe",
                        Arguments = $"/configure /cfg \"{winDir}\\inf\\defltbase.inf\" /db defltbase.sdb /verbose",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using (var proc = Process.Start(psiSecedit))
                    {
                        proc?.WaitForExit(10000);
                    }

                    // gpupdate /force
                    var psiGpupdate = new ProcessStartInfo
                    {
                        FileName = "gpupdate.exe",
                        Arguments = "/force",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using (var proc = Process.Start(psiGpupdate))
                    {
                        proc?.WaitForExit(10000);
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        #region Helpers

        private static void DeleteFileSafe(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var attr = File.GetAttributes(path);
                    if ((attr & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);

                    File.Delete(path);
                }
            }
            catch { }
        }

        private static void DeleteDirectorySafe(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch { }
        }

        #endregion
    }
}
