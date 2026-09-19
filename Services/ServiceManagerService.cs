using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IServiceManagerService
    {
        Task<List<ServiceItem>> GetServicesAsync();
        Task<bool> StartServiceAsync(string serviceName);
        Task<bool> StopServiceAsync(string serviceName);
        Task<bool> RestartServiceAsync(string serviceName);
        Task<bool> SetStartupTypeAsync(string serviceName, string startupType);

        Task<List<DriverItem>> GetDriversAsync();
        void OpenFileLocation(string rawPath);
    }

    public class ServiceManagerService : IServiceManagerService
    {
        #region Known Service Classifications

        private static readonly HashSet<string> CriticalServices = new(StringComparer.OrdinalIgnoreCase)
        {
            "RpcSs", "DcomLaunch", "EventLog", "PlugPlay", "SamSs", "LSM",
            "RpcEptMapper", "BrokerInfrastructure", "SystemEventsBroker",
            "KeyIso", "VaultSvc", "CryptSvc", "ProfSvc", "Winmgmt", "Power",
            "CoreMessagingRegistrar", "Schedule", "UserManager", "StateRepository"
        };

        private static readonly HashSet<string> SafeToOptimizeServices = new(StringComparer.OrdinalIgnoreCase)
        {
            "SysMain", "DiagTrack", "dmwappushservice", "RemoteRegistry", "MapsBroker",
            "RetailDemo", "WMPNetworkSvc", "wisvc", "XblAuthManager", "XblGameSave",
            "XboxGipSvc", "XboxNetApiSvc", "Fax", "WerSvc", "SharedAccess",
            "Downloaded Maps Manager", "PhoneSvc", "SensorService", "SensrSvc"
        };

        #endregion

        public async Task<List<ServiceItem>> GetServicesAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<ServiceItem>();
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name, DisplayName, State, StartMode, StartName, ProcessId, PathName, Description FROM Win32_Service");
                    foreach (var obj in searcher.Get())
                    {
                        string name = obj["Name"]?.ToString() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        string displayName = obj["DisplayName"]?.ToString() ?? name;
                        string state = obj["State"]?.ToString() ?? "Stopped";
                        string startMode = obj["StartMode"]?.ToString() ?? "Manual";
                        string startName = obj["StartName"]?.ToString() ?? "LocalSystem";
                        int pid = Convert.ToInt32(obj["ProcessId"] ?? 0);
                        string path = obj["PathName"]?.ToString() ?? string.Empty;
                        string desc = obj["Description"]?.ToString() ?? string.Empty;

                        string classification = "Üçüncü Taraf";
                        bool isCritical = false;
                        bool isOptimizable = false;

                        if (CriticalServices.Contains(name))
                        {
                            classification = "Sistem Kritik";
                            isCritical = true;
                        }
                        else if (SafeToOptimizeServices.Contains(name))
                        {
                            classification = "Güvenli Optimize";
                            isOptimizable = true;
                        }
                        else if (path.Contains("system32", StringComparison.OrdinalIgnoreCase) ||
                                 path.Contains("windows", StringComparison.OrdinalIgnoreCase))
                        {
                            classification = "Windows Hizmeti";
                        }

                        list.Add(new ServiceItem
                        {
                            ServiceName = name,
                            DisplayName = displayName,
                            Status = state,
                            StartupType = startMode,
                            Account = startName,
                            ProcessId = pid,
                            ExecutablePath = path,
                            Description = desc,
                            SafetyClassification = classification,
                            IsCritical = isCritical,
                            IsOptimizable = isOptimizable
                        });
                    }
                }
                catch { }

                return list
                    .OrderByDescending(s => s.IsOptimizable)
                    .ThenByDescending(s => s.IsRunning)
                    .ThenBy(s => s.DisplayName)
                    .ToList();
            });
        }

        public async Task<bool> StartServiceAsync(string serviceName)
        {
            return await RunScCommandAsync($"start \"{serviceName}\"");
        }

        public async Task<bool> StopServiceAsync(string serviceName)
        {
            return await RunScCommandAsync($"stop \"{serviceName}\"");
        }

        public async Task<bool> RestartServiceAsync(string serviceName)
        {
            await RunScCommandAsync($"stop \"{serviceName}\"");
            await Task.Delay(800);
            return await RunScCommandAsync($"start \"{serviceName}\"");
        }

        public async Task<bool> SetStartupTypeAsync(string serviceName, string startupType)
        {
            string scType = startupType.ToLowerInvariant() switch
            {
                "auto" => "auto",
                "automatic" => "auto",
                "disabled" => "disabled",
                _ => "demand"
            };

            // Note: sc.exe requires a space after start= (e.g. start= auto)
            return await RunScCommandAsync($"config \"{serviceName}\" start= {scType}");
        }

        private static async Task<bool> RunScCommandAsync(string arguments)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "sc.exe",
                        Arguments = arguments,
                        CreateNoWindow = true,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(4000);
                    return proc?.ExitCode == 0;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<List<DriverItem>> GetDriversAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<DriverItem>();
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT DeviceName, DeviceClass, Manufacturer, DriverVersion, DriverDate, IsSigned, Signer, DeviceID FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL");
                    foreach (var obj in searcher.Get())
                    {
                        string devName = obj["DeviceName"]?.ToString()?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(devName)) continue;

                        string devClass = obj["DeviceClass"]?.ToString()?.Trim() ?? "Sistem";
                        string mfg = obj["Manufacturer"]?.ToString()?.Trim() ?? "Standart Donanım";
                        string version = obj["DriverVersion"]?.ToString()?.Trim() ?? "1.0.0.0";
                        string rawDate = obj["DriverDate"]?.ToString()?.Trim() ?? string.Empty;
                        bool isSigned = Convert.ToBoolean(obj["IsSigned"] ?? true);
                        string signer = obj["Signer"]?.ToString()?.Trim() ?? (isSigned ? "Microsoft Windows" : "İmzasız");
                        string devId = obj["DeviceID"]?.ToString()?.Trim() ?? string.Empty;

                        string formattedDate = FormatWmiDate(rawDate);
                        bool isProblematic = !isSigned;

                        list.Add(new DriverItem
                        {
                            DeviceName = devName,
                            DeviceClass = devClass,
                            Manufacturer = mfg,
                            DriverVersion = version,
                            DriverDate = formattedDate,
                            IsSigned = isSigned,
                            Signer = signer,
                            DeviceID = devId,
                            IsProblematic = isProblematic
                        });
                    }
                }
                catch { }

                return list
                    .OrderByDescending(d => d.IsProblematic)
                    .ThenBy(d => d.DeviceClass)
                    .ThenBy(d => d.DeviceName)
                    .ToList();
            });
        }

        private static string FormatWmiDate(string rawDate)
        {
            if (string.IsNullOrWhiteSpace(rawDate) || rawDate.Length < 8) return "-";
            try
            {
                string y = rawDate.Substring(0, 4);
                string m = rawDate.Substring(4, 2);
                string d = rawDate.Substring(6, 2);
                return $"{d}.{m}.{y}";
            }
            catch
            {
                return "-";
            }
        }

        public void OpenFileLocation(string rawPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawPath)) return;

                // Strip arguments and quotes (e.g. "C:\Program Files\..." -k ...)
                string cleanPath = rawPath.Trim();
                if (cleanPath.StartsWith("\""))
                {
                    int closeIndex = cleanPath.IndexOf('"', 1);
                    if (closeIndex > 1)
                    {
                        cleanPath = cleanPath.Substring(1, closeIndex - 1);
                    }
                }
                else
                {
                    int spaceIndex = cleanPath.IndexOf(' ');
                    if (spaceIndex > 0)
                    {
                        cleanPath = cleanPath.Substring(0, spaceIndex);
                    }
                }

                if (File.Exists(cleanPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{cleanPath}\"");
                }
                else
                {
                    string? dir = Path.GetDirectoryName(cleanPath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        Process.Start("explorer.exe", dir);
                    }
                }
            }
            catch { }
        }
    }
}
