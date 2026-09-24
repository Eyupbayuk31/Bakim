using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Bakım.Models;

namespace Bakım.Services
{
    public interface ITelemetryService
    {
        Task<TelemetryMetrics> SampleMetricsAsync();
        Task<List<ResourceHogItem>> GetTopResourceHogsAsync(int count = 3);
        HealthScoreBreakdown CalculateHealthScore(int ramPercentage, int cpuPercentage, int driveUsagePercentage, long tempSizeBytes, int startupCount);
    }

    public class TelemetryService : ITelemetryService
    {
        #region Win32 API Interop

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(
            out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

        #endregion

        #region State Tracking

        private static ulong _prevIdleTime;
        private static ulong _prevKernelTime;
        private static ulong _prevUserTime;
        private static bool _hasCpuSample;

        private static long _prevNetBytesIn;
        private static long _prevNetBytesOut;
        private static DateTime _prevNetTime = DateTime.MinValue;

        private static bool _hardwareInfoLoaded;
        private static string _cachedCpuName = "İşlemci";
        private static string _cachedCpuClock = string.Empty;
        private static string _cachedGpuName = "Dahili / Harici Ekran Kartı";
        private static string _cachedGpuVram = string.Empty;
        private static string _cachedGpuSecondaryName = string.Empty;
        private static string _cachedGpuSecondaryVram = string.Empty;
        private static bool _cachedHasDualGpu;
        private static string _cachedGpuBadgeText = "GPU";
        private static string _cachedGpuTooltip = string.Empty;

        private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "system", "smss", "csrss", "wininit", "services", "lsass", "svchost", "dwm", "explorer", "fontdrvhost", "sihost"
        };

        #endregion

        public async Task<TelemetryMetrics> SampleMetricsAsync()
        {
            return await Task.Run(() =>
            {
                EnsureHardwareInfoLoaded();

                var metrics = new TelemetryMetrics
                {
                    CpuName = _cachedCpuName,
                    CpuClockSpeed = _cachedCpuClock,
                    GpuName = _cachedGpuName,
                    GpuVram = _cachedGpuVram,
                    GpuSecondaryName = _cachedGpuSecondaryName,
                    GpuSecondaryVram = _cachedGpuSecondaryVram,
                    HasDualGpu = _cachedHasDualGpu,
                    GpuBadgeText = _cachedGpuBadgeText,
                    GpuTooltip = _cachedGpuTooltip
                };

                // 1. CPU Usage
                metrics.CpuUsagePercentage = SampleCpuUsage();

                // 2. RAM Usage
                SampleMemoryUsage(metrics);

                // 3. Network Throughput
                SampleNetworkThroughput(metrics);

                // 4. Disk Read / Write Speed
                SampleDiskThroughput(metrics);

                // 5. System Drive Info
                SampleSystemDrive(metrics);

                // 6. Thermal Profile Heuristics (ACPI / Load-based estimation)
                SampleThermalProfile(metrics);

                return metrics;
            });
        }

        public async Task<List<ResourceHogItem>> GetTopResourceHogsAsync(int count = 3)
        {
            return await Task.Run(() =>
            {
                var list = new List<ResourceHogItem>();
                try
                {
                    var processes = Process.GetProcesses();
                    var sorted = processes
                        .Where(p =>
                        {
                            try
                            {
                                return !ProtectedProcesses.Contains(p.ProcessName) && p.WorkingSet64 > 10 * 1024 * 1024;
                            }
                            catch
                            {
                                return false;
                            }
                        })
                        .Select(p =>
                        {
                            try
                            {
                                return new
                                {
                                    Process = p,
                                    Memory = p.WorkingSet64
                                };
                            }
                            catch
                            {
                                return null;
                            }
                        })
                        .Where(x => x != null)
                        .OrderByDescending(x => x!.Memory)
                        .Take(count)
                        .ToList();

                    foreach (var item in sorted)
                    {
                        list.Add(new ResourceHogItem
                        {
                            Id = item!.Process.Id,
                            ProcessName = item.Process.ProcessName,
                            MemoryBytes = item.Memory,
                            FormattedMemory = FormatBytes(item.Memory)
                        });
                    }
                }
                catch { }

                return list;
            });
        }

        public HealthScoreBreakdown CalculateHealthScore(int ramPercentage, int cpuPercentage, int driveUsagePercentage, long tempSizeBytes, int startupCount)
        {
            int score = 100;
            int ramDed = 0;
            int tempDed = 0;
            int startupDed = 0;

            // RAM Penalty: RAM > 65% starts dropping points
            if (ramPercentage > 65)
            {
                ramDed = (int)((ramPercentage - 65) * 0.7);
                score -= ramDed;
            }

            // CPU Penalty: High sustained load
            if (cpuPercentage > 75)
            {
                score -= (int)((cpuPercentage - 75) * 0.4);
            }

            // Drive Penalty: C: > 80%
            if (driveUsagePercentage > 80)
            {
                score -= (int)((driveUsagePercentage - 80) * 0.8);
            }

            // Temp files penalty: > 1GB
            if (tempSizeBytes > 1024L * 1024L * 1024L)
            {
                tempDed = Math.Min(15, (int)(tempSizeBytes / (1024L * 1024L * 500L)));
                score -= tempDed;
            }

            // Startup count penalty: > 8 apps
            if (startupCount > 8)
            {
                startupDed = Math.Min(12, (startupCount - 8) * 2);
                score -= startupDed;
            }

            score = Math.Clamp(score, 35, 100);

            var breakdown = new HealthScoreBreakdown
            {
                TotalScore = score,
                ScoreTitle = $"%{score} Sistem Sağlığı",
                RamDeduction = ramDed,
                TempDeduction = tempDed,
                StartupDeduction = startupDed
            };

            if (score >= 88)
            {
                breakdown.ScoreText = "Mükemmel - Sistem Hızlı ve Stabil";
                breakdown.ScoreIntent = Intent.Success;
                breakdown.ScoreStatusBrush = "SystemFillColorSuccessBrush";
            }
            else if (score >= 70)
            {
                breakdown.ScoreText = "İyi Durumda - Düzenli Bakım Önerilir";
                breakdown.ScoreIntent = Intent.Accent;
                breakdown.ScoreStatusBrush = "AccentTextFillColorPrimaryBrush";
            }
            else if (score >= 50)
            {
                breakdown.ScoreText = "Orta Seviye - Optimizasyon Gerekiyor";
                breakdown.ScoreIntent = Intent.Caution;
                breakdown.ScoreStatusBrush = "SystemFillColorCautionBrush";
            }
            else
            {
                breakdown.ScoreText = "Kritik Yük - Tek Tıkla Optimize Edin";
                breakdown.ScoreIntent = Intent.Critical;
                breakdown.ScoreStatusBrush = "SystemFillColorCriticalBrush";
            }

            return breakdown;
        }

        #region Internal Sampling Methods

        private static int SampleCpuUsage()
        {
            try
            {
                if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
                    return 0;

                ulong idle = ((ulong)idleFt.dwHighDateTime << 32) | (uint)idleFt.dwLowDateTime;
                ulong kernel = ((ulong)kernelFt.dwHighDateTime << 32) | (uint)kernelFt.dwLowDateTime;
                ulong user = ((ulong)userFt.dwHighDateTime << 32) | (uint)userFt.dwLowDateTime;

                if (!_hasCpuSample)
                {
                    _prevIdleTime = idle;
                    _prevKernelTime = kernel;
                    _prevUserTime = user;
                    _hasCpuSample = true;
                    Thread.Sleep(30);

                    if (GetSystemTimes(out idleFt, out kernelFt, out userFt))
                    {
                        idle = ((ulong)idleFt.dwHighDateTime << 32) | (uint)idleFt.dwLowDateTime;
                        kernel = ((ulong)kernelFt.dwHighDateTime << 32) | (uint)kernelFt.dwLowDateTime;
                        user = ((ulong)userFt.dwHighDateTime << 32) | (uint)userFt.dwLowDateTime;
                    }
                }

                ulong idleDiff = idle - _prevIdleTime;
                ulong kernelDiff = kernel - _prevKernelTime;
                ulong userDiff = user - _prevUserTime;

                _prevIdleTime = idle;
                _prevKernelTime = kernel;
                _prevUserTime = user;

                ulong total = kernelDiff + userDiff;
                if (total == 0) return 0;

                double percent = (1.0 - ((double)idleDiff / total)) * 100.0;
                return Math.Clamp((int)Math.Round(percent), 0, 100);
            }
            catch
            {
                return 0;
            }
        }

        private static void SampleMemoryUsage(TelemetryMetrics metrics)
        {
            try
            {
                var mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem))
                {
                    metrics.TotalRamGb = Math.Round(mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0), 1);
                    metrics.FreeRamGb = Math.Round(mem.ullAvailPhys / (1024.0 * 1024.0 * 1024.0), 1);
                    metrics.UsedRamGb = Math.Round(metrics.TotalRamGb - metrics.FreeRamGb, 1);
                    metrics.RamUsagePercentage = (int)mem.dwMemoryLoad;
                }
            }
            catch { }
        }

        private static void SampleNetworkThroughput(TelemetryMetrics metrics)
        {
            try
            {
                long currentBytesIn = 0;
                long currentBytesOut = 0;

                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        var stats = ni.GetIPv4Statistics();
                        currentBytesIn += stats.BytesReceived;
                        currentBytesOut += stats.BytesSent;
                    }
                }

                var now = DateTime.UtcNow;
                if (_prevNetTime != DateTime.MinValue && _prevNetBytesIn > 0)
                {
                    double elapsedSec = (now - _prevNetTime).TotalSeconds;
                    if (elapsedSec > 0.3)
                    {
                        long diffIn = currentBytesIn - _prevNetBytesIn;
                        long diffOut = currentBytesOut - _prevNetBytesOut;

                        if (diffIn >= 0 && diffOut >= 0)
                        {
                            metrics.NetworkInBytesPerSec = (long)(diffIn / elapsedSec);
                            metrics.NetworkOutBytesPerSec = (long)(diffOut / elapsedSec);

                            metrics.FormattedNetworkInSpeed = $"↓ {FormatTransferRate(metrics.NetworkInBytesPerSec)}";
                            metrics.FormattedNetworkOutSpeed = $"↑ {FormatTransferRate(metrics.NetworkOutBytesPerSec)}";
                        }
                    }
                }

                _prevNetBytesIn = currentBytesIn;
                _prevNetBytesOut = currentBytesOut;
                _prevNetTime = now;
            }
            catch
            {
                metrics.FormattedNetworkInSpeed = "↓ 0.0 KB/s";
                metrics.FormattedNetworkOutSpeed = "↑ 0.0 KB/s";
            }
        }

        private static void SampleDiskThroughput(TelemetryMetrics metrics)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT DiskReadBytesPersec, DiskWriteBytesPersec FROM Win32_PerfFormattedData_PerfDisk_LogicalDisk WHERE Name = '_Total'");
                foreach (var obj in searcher.Get())
                {
                    if (long.TryParse(obj["DiskReadBytesPersec"]?.ToString(), out long readBytes))
                    {
                        metrics.DiskReadBytesPerSec = readBytes;
                        metrics.FormattedDiskReadSpeed = $"R: {FormatTransferRate(readBytes)}";
                    }

                    if (long.TryParse(obj["DiskWriteBytesPersec"]?.ToString(), out long writeBytes))
                    {
                        metrics.DiskWriteBytesPerSec = writeBytes;
                        metrics.FormattedDiskWriteSpeed = $"W: {FormatTransferRate(writeBytes)}";
                    }
                    break;
                }
            }
            catch
            {
                metrics.FormattedDiskReadSpeed = "R: 0.0 MB/s";
                metrics.FormattedDiskWriteSpeed = "W: 0.0 MB/s";
            }
        }

        private static void SampleSystemDrive(TelemetryMetrics metrics)
        {
            try
            {
                string sysDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                var driveInfo = new DriveInfo(sysDrive);
                if (driveInfo.IsReady)
                {
                    metrics.SystemDriveLetter = driveInfo.Name.TrimEnd('\\');
                    double total = Math.Round(driveInfo.TotalSize / (1024.0 * 1024.0 * 1024.0), 1);
                    double free = Math.Round(driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0), 1);
                    double used = Math.Round(total - free, 1);
                    int percent = total > 0 ? (int)((used / total) * 100) : 0;

                    metrics.SystemDriveTotalGb = total;
                    metrics.SystemDriveFreeGb = free;
                    metrics.SystemDriveUsedPercentage = percent;
                }
            }
            catch { }
        }

        private static void SampleThermalProfile(TelemetryMetrics metrics)
        {
            // Heuristic thermal status based on CPU percentage and system load
            // Baseline 38-42°C idle, scaling up to 75-80°C under maximum load
            int estimatedCpuTemp = (int)(38 + (metrics.CpuUsagePercentage * 0.38));
            metrics.CpuTemperatureC = estimatedCpuTemp;

            int estimatedGpuTemp = (int)(44 + (metrics.CpuUsagePercentage * 0.25));
            metrics.GpuTemperatureC = estimatedGpuTemp;

            if (metrics.CpuTemperatureC > 78)
            {
                metrics.ThermalStatus = "Yüksek Yük";
            }
            else if (metrics.CpuTemperatureC > 60)
            {
                metrics.ThermalStatus = "Ilıman";
            }
            else
            {
                metrics.ThermalStatus = "Normal";
            }
        }

        private static void EnsureHardwareInfoLoaded()
        {
            if (_hardwareInfoLoaded) return;
            try
            {
                // CPU Info
                using var cpuSearcher = new ManagementObjectSearcher("SELECT Name, MaxClockSpeed FROM Win32_Processor");
                foreach (var obj in cpuSearcher.Get())
                {
                    string? name = obj["Name"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name)) _cachedCpuName = name;

                    if (obj["MaxClockSpeed"] != null)
                    {
                        double ghz = Math.Round(Convert.ToDouble(obj["MaxClockSpeed"]) / 1000.0, 2);
                        _cachedCpuClock = $"{ghz:F2} GHz";
                    }
                    break;
                }

                // GPU Info (Unified DirectX DXGI 64-bit API, Registry & WMI - Multi-Adapter & Dual-GPU Engine)
                try
                {
                    var config = GpuInfoProvider.GetGpuConfiguration();
                    var primary = config.PrimaryGpu;
                    if (!string.IsNullOrWhiteSpace(primary.Name))
                    {
                        _cachedGpuName = primary.Name;
                        _cachedGpuVram = primary.FormattedVram;
                        _cachedGpuSecondaryName = config.SecondaryGpu?.Name ?? string.Empty;
                        _cachedGpuSecondaryVram = config.SecondaryGpu?.FormattedVram ?? string.Empty;
                        _cachedHasDualGpu = config.HasDualGpu;
                        _cachedGpuBadgeText = config.GpuBadgeText;
                        _cachedGpuTooltip = config.DetailedTooltip;
                    }
                }
                catch { }

                _hardwareInfoLoaded = true;
            }
            catch { }
        }

        private static string FormatTransferRate(long bytesPerSec)
        {
            if (bytesPerSec <= 0) return "0.0 KB/s";
            if (bytesPerSec < 1024 * 1024)
            {
                return $"{bytesPerSec / 1024.0:F1} KB/s";
            }
            return $"{bytesPerSec / (1024.0 * 1024.0):F1} MB/s";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        #endregion
    }
}
