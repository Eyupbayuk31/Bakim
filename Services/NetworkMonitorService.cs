using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Bakım.Models;

namespace Bakım.Services
{
    public interface INetworkMonitorService
    {
        Task<(List<NetworkConnectionItem> Connections, NetworkOverviewStats Stats)> GetActiveConnectionsAsync();
        Task<bool> BlockProcessInFirewallAsync(NetworkConnectionItem item);
        Task<bool> UnblockProcessInFirewallAsync(NetworkConnectionItem item);
        bool KillProcess(int pid);
        void OpenProcessLocation(string processPath);
    }

    public class NetworkMonitorService : INetworkMonitorService
    {
        #region IP Helper API (iphlpapi.dll) P/Invoke & Structures

        private const int AF_INET = 2; // IPv4
        private const int AF_INET6 = 23; // IPv6

        private enum TCP_TABLE_CLASS
        {
            TCP_TABLE_BASIC_LISTENER = 0,
            TCP_TABLE_BASIC_CONNECTIONS = 1,
            TCP_TABLE_BASIC_ALL = 2,
            TCP_TABLE_OWNER_PID_LISTENER = 3,
            TCP_TABLE_OWNER_PID_CONNECTIONS = 4,
            TCP_TABLE_OWNER_PID_ALL = 5,
            TCP_TABLE_OWNER_MODULE_LISTENER = 6,
            TCP_TABLE_OWNER_MODULE_CONNECTIONS = 7,
            TCP_TABLE_OWNER_MODULE_ALL = 8
        }

        private enum UDP_TABLE_CLASS
        {
            UDP_TABLE_BASIC = 0,
            UDP_TABLE_OWNER_PID = 1,
            UDP_TABLE_OWNER_MODULE = 2
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            public byte localPort1;
            public byte localPort2;
            public byte localPort3;
            public byte localPort4;
            public uint remoteAddr;
            public byte remotePort1;
            public byte remotePort2;
            public byte remotePort3;
            public byte remotePort4;
            public uint owningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_UDPROW_OWNER_PID
        {
            public uint localAddr;
            public byte localPort1;
            public byte localPort2;
            public byte localPort3;
            public byte localPort4;
            public uint owningPid;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable,
            ref int pdwSize,
            bool bOrder,
            int ulAf,
            TCP_TABLE_CLASS TableClass,
            uint Reserved = 0);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(
            IntPtr pUdpTable,
            ref int pdwSize,
            bool bOrder,
            int ulAf,
            UDP_TABLE_CLASS TableClass,
            uint Reserved = 0);

        #endregion

        #region Cache & Bandwidth State

        private static readonly ConcurrentDictionary<int, (string Name, string Path)> _processCache = new();
        private static readonly ConcurrentDictionary<string, string> _dnsCache = new();
        private static readonly HashSet<int> SafeWellKnownPorts = new()
        {
            80, 443, 53, 123, 853, 993, 995, 587, 22, 21, 3389, 8080, 8443
        };
        private static readonly HashSet<int> KnownMalwarePorts = new()
        {
            4444, 5555, 6667, 1337, 31337, 8888, 9999, 12345, 27374, 30128
        };

        private static long _prevBytesReceived;
        private static long _prevBytesSent;
        private static DateTime _prevTime = DateTime.UtcNow;
        private static bool _hasSpeedSample;

        #endregion

        public async Task<(List<NetworkConnectionItem> Connections, NetworkOverviewStats Stats)> GetActiveConnectionsAsync()
        {
            return await Task.Run(async () =>
            {
                var connections = new List<NetworkConnectionItem>();

                // 1. Fetch TCP Table (IPv4)
                connections.AddRange(GetTcpConnections());

                // 2. Fetch UDP Table (IPv4)
                connections.AddRange(GetUdpConnections());

                // 3. Populate Process Info & Categorize
                int establishedCount = 0;
                int listeningCount = 0;

                foreach (var item in connections)
                {
                    var (name, path) = GetProcessDetails(item.ProcessId);
                    item.ProcessName = name;
                    item.ProcessPath = path;

                    CategorizeSecurity(item);

                    if (item.State == "ESTABLISHED") establishedCount++;
                    else if (item.State == "LISTENING") listeningCount++;
                }

                // 4. Bandwidth calculation
                var (rxSpeed, txSpeed) = CalculateGlobalSpeed();

                // Sort: Established external connections first, then listening, then others
                var sorted = connections
                    .OrderByDescending(c => c.State == "ESTABLISHED" && c.IsExternal)
                    .ThenByDescending(c => c.State == "ESTABLISHED")
                    .ThenByDescending(c => c.State == "LISTENING")
                    .ThenBy(c => c.ProcessName)
                    .ToList();

                var stats = new NetworkOverviewStats
                {
                    TotalConnections = sorted.Count,
                    EstablishedCount = establishedCount,
                    ListeningCount = listeningCount,
                    DownloadSpeedKb = rxSpeed,
                    UploadSpeedKb = txSpeed
                };

                // 5. Trigger Async DNS Resolution in background for external IPs (cached)
                _ = ResolveDnsForListAsync(sorted);

                return (sorted, stats);
            });
        }

        private static List<NetworkConnectionItem> GetTcpConnections()
        {
            var list = new List<NetworkConnectionItem>();
            int bufferSize = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);

            if (bufferSize <= 0) return list;

            IntPtr pTable = Marshal.AllocHGlobal(bufferSize);
            try
            {
                uint ret = GetExtendedTcpTable(pTable, ref bufferSize, true, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
                if (ret == 0)
                {
                    int numEntries = Marshal.ReadInt32(pTable);
                    IntPtr rowPtr = IntPtr.Add(pTable, 4);
                    int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));

                    for (int i = 0; i < numEntries; i++)
                    {
                        var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                        string localIp = new IPAddress(row.localAddr).ToString();
                        ushort localPort = ConvertPort(row.localPort1, row.localPort2);
                        string remoteIp = new IPAddress(row.remoteAddr).ToString();
                        ushort remotePort = ConvertPort(row.remotePort1, row.remotePort2);
                        string state = ConvertTcpState(row.state);
                        int pid = (int)row.owningPid;

                        list.Add(new NetworkConnectionItem
                        {
                            ProcessId = pid,
                            Protocol = "TCP",
                            LocalAddress = localIp,
                            LocalPort = localPort,
                            RemoteAddress = remoteIp,
                            RemotePort = remotePort,
                            State = state
                        });

                        rowPtr = IntPtr.Add(rowPtr, rowSize);
                    }
                }
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(pTable);
            }
            return list;
        }

        private static List<NetworkConnectionItem> GetUdpConnections()
        {
            var list = new List<NetworkConnectionItem>();
            int bufferSize = 0;
            GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);

            if (bufferSize <= 0) return list;

            IntPtr pTable = Marshal.AllocHGlobal(bufferSize);
            try
            {
                uint ret = GetExtendedUdpTable(pTable, ref bufferSize, true, AF_INET, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
                if (ret == 0)
                {
                    int numEntries = Marshal.ReadInt32(pTable);
                    IntPtr rowPtr = IntPtr.Add(pTable, 4);
                    int rowSize = Marshal.SizeOf(typeof(MIB_UDPROW_OWNER_PID));

                    for (int i = 0; i < numEntries; i++)
                    {
                        var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);
                        string localIp = new IPAddress(row.localAddr).ToString();
                        ushort localPort = ConvertPort(row.localPort1, row.localPort2);
                        int pid = (int)row.owningPid;

                        list.Add(new NetworkConnectionItem
                        {
                            ProcessId = pid,
                            Protocol = "UDP",
                            LocalAddress = localIp,
                            LocalPort = localPort,
                            RemoteAddress = "*",
                            RemotePort = 0,
                            State = "LISTENING"
                        });

                        rowPtr = IntPtr.Add(rowPtr, rowSize);
                    }
                }
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(pTable);
            }
            return list;
        }

        private static ushort ConvertPort(byte b1, byte b2)
        {
            return (ushort)((b1 << 8) | b2);
        }

        private static string ConvertTcpState(uint state)
        {
            return state switch
            {
                1 => "CLOSED",
                2 => "LISTENING",
                3 => "SYN_SENT",
                4 => "SYN_RCVD",
                5 => "ESTABLISHED",
                6 => "FIN_WAIT1",
                7 => "FIN_WAIT2",
                8 => "CLOSE_WAIT",
                9 => "CLOSING",
                10 => "LAST_ACK",
                11 => "TIME_WAIT",
                12 => "DELETE_TCB",
                _ => "UNKNOWN"
            };
        }

        private static (string Name, string Path) GetProcessDetails(int pid)
        {
            if (pid <= 0) return ("Sistem / Boşta", string.Empty);
            if (pid == 4) return ("System Kernel", @"C:\Windows\System32\ntoskrnl.exe");

            if (_processCache.TryGetValue(pid, out var cached))
            {
                return cached;
            }

            try
            {
                using var proc = Process.GetProcessById(pid);
                string name = proc.ProcessName;
                string path = string.Empty;
                try
                {
                    path = proc.MainModule?.FileName ?? string.Empty;
                }
                catch { }

                var result = (name, path);
                _processCache[pid] = result;
                return result;
            }
            catch
            {
                var fallback = ($"PID: {pid}", string.Empty);
                _processCache[pid] = fallback;
                return fallback;
            }
        }

        private static void CategorizeSecurity(NetworkConnectionItem item)
        {
            if (item.State == "LISTENING")
            {
                item.PortCategory = "Dinleme Modu (Yerel)";
                item.BadgeBrush = "AccentTextFillColorPrimaryBrush";
                item.IsSuspicious = false;
                return;
            }

            if (item.RemotePort > 0)
            {
                if (KnownMalwarePorts.Contains(item.RemotePort) || KnownMalwarePorts.Contains(item.LocalPort))
                {
                    item.PortCategory = "Kritik / Şüpheli Port";
                    item.BadgeBrush = "SystemFillColorCriticalBrush";
                    item.IsSuspicious = true;
                    return;
                }

                if (SafeWellKnownPorts.Contains(item.RemotePort))
                {
                    item.PortCategory = item.RemotePort == 443 ? "Güvenli (HTTPS)" : (item.RemotePort == 80 ? "Standart (HTTP)" : "Güvenli Standart Port");
                    item.BadgeBrush = "SystemFillColorSuccessBrush";
                    item.IsSuspicious = false;
                    return;
                }

                if (item.IsExternal && item.RemotePort > 1024)
                {
                    item.PortCategory = "Dış Bağlantı (Özel Port)";
                    item.BadgeBrush = "SystemFillColorCautionBrush";
                    item.IsSuspicious = true;
                    return;
                }
            }

            item.PortCategory = "Normal Ağ Trafiği";
            item.BadgeBrush = "TextFillColorSecondaryBrush";
            item.IsSuspicious = false;
        }

        private static (double RxSpeedKb, double TxSpeedKb) CalculateGlobalSpeed()
        {
            try
            {
                long totalRx = 0;
                long totalTx = 0;

                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up && 
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        var stats = ni.GetIPStatistics();
                        totalRx += stats.BytesReceived;
                        totalTx += stats.BytesSent;
                    }
                }

                var now = DateTime.UtcNow;
                double elapsedSeconds = (now - _prevTime).TotalSeconds;

                if (!_hasSpeedSample || elapsedSeconds < 0.5)
                {
                    _prevBytesReceived = totalRx;
                    _prevBytesSent = totalTx;
                    _prevTime = now;
                    _hasSpeedSample = true;
                    return (0, 0);
                }

                long rxDiff = totalRx - _prevBytesReceived;
                long txDiff = totalTx - _prevBytesSent;

                _prevBytesReceived = totalRx;
                _prevBytesSent = totalTx;
                _prevTime = now;

                if (rxDiff < 0) rxDiff = 0;
                if (txDiff < 0) txDiff = 0;

                double rxSpeed = (rxDiff / 1024.0) / elapsedSeconds;
                double txSpeed = (txDiff / 1024.0) / elapsedSeconds;

                return (rxSpeed, txSpeed);
            }
            catch
            {
                return (0, 0);
            }
        }

        private static async Task ResolveDnsForListAsync(List<NetworkConnectionItem> items)
        {
            var targets = items
                .Where(x => x.IsExternal && string.IsNullOrEmpty(x.RemoteHostName))
                .Take(25)
                .ToList();

            foreach (var item in targets)
            {
                if (_dnsCache.TryGetValue(item.RemoteAddress, out var cached))
                {
                    item.RemoteHostName = cached;
                    continue;
                }

                try
                {
                    using var cts = new CancellationTokenSource(400);
                    var entry = await Dns.GetHostEntryAsync(item.RemoteAddress, cts.Token);
                    if (!string.IsNullOrWhiteSpace(entry.HostName))
                    {
                        _dnsCache[item.RemoteAddress] = entry.HostName;
                        item.RemoteHostName = entry.HostName;
                        continue;
                    }
                }
                catch { }

                _dnsCache[item.RemoteAddress] = item.RemoteAddress;
                item.RemoteHostName = item.RemoteAddress;
            }
        }

        public async Task<bool> BlockProcessInFirewallAsync(NetworkConnectionItem item)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(item.ProcessPath) || !File.Exists(item.ProcessPath))
                        return false;

                    string ruleName = $"Bakim_Block_{Path.GetFileNameWithoutExtension(item.ProcessPath)}";
                    var psi = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"advfirewall firewall add rule name=\"{ruleName}\" dir=out action=block program=\"{item.ProcessPath}\" enable=yes",
                        CreateNoWindow = true,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(3000);
                    item.IsBlocked = true;
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> UnblockProcessInFirewallAsync(NetworkConnectionItem item)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string ruleName = $"Bakim_Block_{Path.GetFileNameWithoutExtension(item.ProcessPath)}";
                    var psi = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"advfirewall firewall delete rule name=\"{ruleName}\"",
                        CreateNoWindow = true,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(3000);
                    item.IsBlocked = false;
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public bool KillProcess(int pid)
        {
            try
            {
                if (pid <= 4) return false; // Kernel & System guard
                var proc = Process.GetProcessById(pid);
                proc.Kill(true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void OpenProcessLocation(string processPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{processPath}\"");
                }
            }
            catch { }
        }
    }
}
