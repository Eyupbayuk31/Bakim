using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Bakım.Models;

namespace Bakım.Services
{
    public interface INetworkMonitorService
    {
        Task<(List<NetworkConnectionItem> Connections, NetworkOverviewStats Stats)> GetActiveConnectionsAsync();
        Task<List<ListeningPortItem>> GetListeningPortsAsync();
        Task<List<NetworkAdapterItem>> GetNetworkAdaptersAsync();
        Task<PingResultItem> PingHostAsync(string host, int timeoutMs = 2000);
        Task<bool> FlushDnsCacheAsync();
        Task<bool> RenewIpAddressAsync();
        Task<PortCheckResult> CheckPortAsync(string host, int port, int timeoutMs = 2500);
        Task RunSpeedTestAsync(IProgress<SpeedTestProgress> progress, CancellationToken ct);
        Task<bool> BlockProcessInFirewallAsync(NetworkConnectionItem item);
        Task<bool> UnblockProcessInFirewallAsync(NetworkConnectionItem item);
        string LastFirewallError { get; }
        bool KillProcess(int pid);
        void OpenProcessLocation(string processPath);
    }

    public class NetworkMonitorService : INetworkMonitorService
    {
        #region IP Helper API (iphlpapi.dll) & DnsApi P/Invoke

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

        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
        private static extern int DnsFlushResolverCache();

        #endregion

        #region Cache & State

        private static readonly ConcurrentDictionary<int, (string Name, string Path, string Publisher, bool IsSigned)> _processCache = new();
        private static readonly ConcurrentDictionary<string, string> _dnsCache = new();
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static readonly HashSet<int> KnownMalwarePorts = new()
        {
            4444, 5555, 6667, 1337, 31337, 8888, 9999, 12345, 27374, 30128
        };

        private static readonly Dictionary<int, string> WellKnownServiceNames = new()
        {
            { 80, "HTTP (Standart Web)" },
            { 443, "HTTPS (Güvenli Web)" },
            { 53, "DNS (Alan Adı)" },
            { 853, "DNS over TLS (DoT)" },
            { 22, "SSH (Güvenli Terminal)" },
            { 21, "FTP (Dosya Aktarımı)" },
            { 25, "SMTP (E-posta Gönderimi)" },
            { 110, "POP3 (E-posta)" },
            { 143, "IMAP (E-posta)" },
            { 587, "SMTP (Güvenli Gönderim)" },
            { 993, "IMAPS (Güvenli E-posta)" },
            { 995, "POP3S (Güvenli E-posta)" },
            { 123, "NTP (Zaman Senkronu)" },
            { 3389, "RDP (Uzak Masaüstü)" },
            { 3306, "MySQL Veritabanı" },
            { 5432, "PostgreSQL Veritabanı" },
            { 1433, "MSSQL Veritabanı" },
            { 27017, "MongoDB Veritabanı" },
            { 6379, "Redis Önbellek" },
            { 8080, "Alternatif HTTP Proxy/Web" },
            { 8443, "Alternatif HTTPS Web" },
            { 5000, "Geliştirme / Yerel API" },
            { 3000, "Geliştirme Sunucusu (Node/React)" },
            { 5173, "Vite Geliştirme Sunucusu" },
            { 27015, "Steam / Oyun Trafiği" }
        };

        private static long _prevBytesReceived;
        private static long _prevBytesSent;
        private static DateTime _prevTime = DateTime.UtcNow;
        private static bool _hasSpeedSample;

        #endregion

        #region 1. Canlı Bağlantılar (Active Connections)

        public async Task<(List<NetworkConnectionItem> Connections, NetworkOverviewStats Stats)> GetActiveConnectionsAsync()
        {
            return await Task.Run(() =>
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
                    var (name, path, publisher, isSigned) = GetProcessDetails(item.ProcessId);
                    item.ProcessName = name;
                    item.ProcessPath = path;
                    item.Publisher = publisher;
                    item.IsSigned = isSigned;

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

        private static (string Name, string Path, string Publisher, bool IsSigned) GetProcessDetails(int pid)
        {
            if (pid <= 0) return ("Sistem / Boşta", string.Empty, "Windows Kernel", true);
            if (pid == 4) return ("System Kernel", @"C:\Windows\System32\ntoskrnl.exe", "Microsoft Windows", true);

            if (_processCache.TryGetValue(pid, out var cached))
            {
                return cached;
            }

            try
            {
                using var proc = Process.GetProcessById(pid);
                string name = proc.ProcessName;
                string path = string.Empty;
                string publisher = "Bilinmiyor";
                bool isSigned = false;

                try
                {
                    path = proc.MainModule?.FileName ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        var info = FileVersionInfo.GetVersionInfo(path);
                        if (!string.IsNullOrWhiteSpace(info.CompanyName))
                        {
                            publisher = info.CompanyName;
                        }

                        // Check digital signature existence
                        try
                        {
#pragma warning disable SYSLIB0057
                            using var cert = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
                            isSigned = true;
                            if (publisher == "Bilinmiyor" && !string.IsNullOrWhiteSpace(cert.Subject))
                            {
                                publisher = cert.Subject;
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                var result = (name, path, publisher, isSigned);
                _processCache[pid] = result;
                return result;
            }
            catch
            {
                var fallback = ($"PID: {pid}", string.Empty, "Bilinmiyor", false);
                _processCache[pid] = fallback;
                return fallback;
            }
        }

        private static void CategorizeSecurity(NetworkConnectionItem item)
        {
            int targetPort = item.RemotePort > 0 ? item.RemotePort : item.LocalPort;
            if (WellKnownServiceNames.TryGetValue(targetPort, out var serviceName))
            {
                item.ServiceDescription = serviceName;
            }
            else
            {
                item.ServiceDescription = targetPort > 0 ? $"Port {targetPort}" : "-";
            }

            if (item.State == "LISTENING")
            {
                item.PortCategory = "Dinleme Modu (Yerel)";
                item.BadgeBrush = "AccentTextFillColorPrimaryBrush";
                item.IsSuspicious = false;
                item.ThreatDescription = "Bu süreç yerel sistemde gelen istekleri dinlemektedir.";
                return;
            }

            if (item.RemotePort > 0)
            {
                if (KnownMalwarePorts.Contains(item.RemotePort) || KnownMalwarePorts.Contains(item.LocalPort))
                {
                    item.PortCategory = "Kritik / Şüpheli Port";
                    item.BadgeBrush = "SystemFillColorCriticalBrush";
                    item.IsSuspicious = true;
                    item.ThreatDescription = $"Bağlantı bilinen arka kapı / trojan portlarından biriyle (Port: {item.RemotePort}) kurulmuştur!";
                    return;
                }

                // Check suspicious path (temp / appdata temp execution)
                if (!string.IsNullOrWhiteSpace(item.ProcessPath))
                {
                    string lowPath = item.ProcessPath.ToLowerInvariant();
                    if (lowPath.Contains(@"\temp\") || lowPath.Contains(@"\appdata\local\temp\"))
                    {
                        item.PortCategory = "Riskli Dizin Süreci";
                        item.BadgeBrush = "SystemFillColorCriticalBrush";
                        item.IsSuspicious = true;
                        item.ThreatDescription = "Bu süreç Temp klasöründen çalıştırılarak dış ağa bağlanıyor! Güvenlik taraması önerilir.";
                        return;
                    }
                }

                if (item.RemotePort == 443)
                {
                    item.PortCategory = "Güvenli (HTTPS)";
                    item.BadgeBrush = "SystemFillColorSuccessBrush";
                    item.IsSuspicious = false;
                    item.ThreatDescription = "Şifreli HTTPS web trafiği. Standart güvenli protokoldür.";
                    return;
                }

                if (item.RemotePort == 80)
                {
                    item.PortCategory = "Standart (HTTP)";
                    item.BadgeBrush = "SystemFillColorSuccessBrush";
                    item.IsSuspicious = false;
                    item.ThreatDescription = "Şifresiz HTTP web trafiği.";
                    return;
                }

                if (item.IsExternal && item.RemotePort > 1024)
                {
                    item.PortCategory = "Dış Bağlantı (Özel Port)";
                    item.BadgeBrush = "SystemFillColorCautionBrush";
                    item.IsSuspicious = true;
                    item.ThreatDescription = $"Dış ağdaki özel bir porta ({item.RemotePort}) veri aktarılıyor.";
                    return;
                }
            }

            item.PortCategory = "Normal Ağ Trafiği";
            item.BadgeBrush = "TextFillColorSecondaryBrush";
            item.IsSuspicious = false;
            item.ThreatDescription = "Sıradan ağ iletişimi.";
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

        #endregion

        #region 2. Dinlenen Portlar (Listening Ports)

        public async Task<List<ListeningPortItem>> GetListeningPortsAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<ListeningPortItem>();
                var allConns = GetTcpConnections();

                foreach (var conn in allConns.Where(c => c.State == "LISTENING"))
                {
                    var (name, path, _, _) = GetProcessDetails(conn.ProcessId);
                    WellKnownServiceNames.TryGetValue(conn.LocalPort, out var serviceName);

                    list.Add(new ListeningPortItem
                    {
                        ProcessId = conn.ProcessId,
                        ProcessName = name,
                        ProcessPath = path,
                        Protocol = "TCP",
                        LocalAddress = conn.LocalAddress,
                        Port = conn.LocalPort,
                        ServiceName = serviceName ?? "Özel Servis",
                        PortCategory = KnownMalwarePorts.Contains(conn.LocalPort) ? "Şüpheli Dinleyici" : "Standart Dinleme"
                    });
                }

                // Also fetch UDP listening ports
                var udpConns = GetUdpConnections();
                foreach (var conn in udpConns)
                {
                    var (name, path, _, _) = GetProcessDetails(conn.ProcessId);
                    WellKnownServiceNames.TryGetValue(conn.LocalPort, out var serviceName);

                    list.Add(new ListeningPortItem
                    {
                        ProcessId = conn.ProcessId,
                        ProcessName = name,
                        ProcessPath = path,
                        Protocol = "UDP",
                        LocalAddress = conn.LocalAddress,
                        Port = conn.LocalPort,
                        ServiceName = serviceName ?? "Özel Servis",
                        PortCategory = KnownMalwarePorts.Contains(conn.LocalPort) ? "Şüpheli Dinleyici" : "UDP Soketi"
                    });
                }

                return list
                    .OrderBy(x => x.Port)
                    .ThenBy(x => x.ProcessName)
                    .ToList();
            });
        }

        #endregion

        #region 3. Ağ Adaptörleri & Donanım (Network Interfaces)

        public async Task<List<NetworkAdapterItem>> GetNetworkAdaptersAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<NetworkAdapterItem>();

                try
                {
                    foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        // 1. Loopback ve Tunnel (Teredo vb.) arayüzlerini tamamen atla
                        if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                            nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                            continue;

                        // 2. Yalnızca fiilen çalışır durumda (Up) olan aktif bağlantıları göster
                        if (nic.OperationalStatus != OperationalStatus.Up)
                            continue;

                        // 3. Sanal / NDIS Filtre / Paket Zamanlayıcı / WFP sürücülerini ele
                        if (IsVirtualOrFilterAdapter(nic))
                            continue;

                        var ipProps = nic.GetIPProperties();
                        var stats = nic.GetIPStatistics();

                        // IPv4 and Subnet
                        string ipv4 = "-";
                        string subnet = "-";
                        foreach (var uni in ipProps.UnicastAddresses)
                        {
                            if (uni.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                ipv4 = uni.Address.ToString();
                                subnet = uni.IPv4Mask?.ToString() ?? "-";
                                break;
                            }
                        }

                        // 4. Geçerli bir IPv4 adresi olmayan (internetsiz sahte/alt arayüz) kartları ele
                        if (ipv4 == "-" || string.IsNullOrWhiteSpace(ipv4))
                            continue;

                        // Gateway
                        string gateway = "-";
                        var firstGw = ipProps.GatewayAddresses.FirstOrDefault();
                        if (firstGw != null && firstGw.Address != null)
                        {
                            gateway = firstGw.Address.ToString();
                        }

                        // DNS
                        var dnsList = ipProps.DnsAddresses
                            .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                            .Select(d => d.ToString())
                            .ToList();
                        string dns = dnsList.Count > 0 ? string.Join(", ", dnsList) : "-";

                        // MAC
                        byte[] macBytes = nic.GetPhysicalAddress().GetAddressBytes();
                        string mac = macBytes.Length > 0 ? string.Join(":", macBytes.Select(b => b.ToString("X2"))) : "-";

                        // Speed
                        string speedText = FormatSpeed(nic.Speed);

                        // Data transferred
                        string rxFormatted = FormatBytes(stats.BytesReceived);
                        string txFormatted = FormatBytes(stats.BytesSent);

                        string typeName = nic.NetworkInterfaceType switch
                        {
                            NetworkInterfaceType.Ethernet => "Kablolu (Ethernet)",
                            NetworkInterfaceType.Wireless80211 => "Kablosuz (Wi-Fi)",
                            _ => nic.NetworkInterfaceType.ToString()
                        };

                        bool isUp = nic.OperationalStatus == OperationalStatus.Up;

                        list.Add(new NetworkAdapterItem
                        {
                            Id = nic.Id,
                            Name = nic.Name,
                            Description = nic.Description,
                            TypeName = typeName,
                            Status = isUp ? "Etkin / Bağlı" : "Bağlantı Yok",
                            IsUp = isUp,
                            SpeedText = speedText,
                            Ipv4Address = ipv4,
                            SubnetMask = subnet,
                            Gateway = gateway,
                            DnsServers = dns,
                            MacAddress = mac,
                            TotalReceivedFormatted = rxFormatted,
                            TotalSentFormatted = txFormatted
                        });
                    }
                }
                catch { }

                return list.OrderByDescending(x => x.IsUp).ThenBy(x => x.Name).ToList();
            });
        }

        private static bool IsVirtualOrFilterAdapter(NetworkInterface nic)
        {
            string name = nic.Name ?? string.Empty;
            string desc = nic.Description ?? string.Empty;
            string combined = (name + " " + desc).ToLowerInvariant();

            string[] blacklistedKeywords = new[]
            {
                "packet scheduler",
                "lightweight filter",
                "wfp 802.3",
                "wfp native",
                "filter-0",
                "filter driver",
                "qos",
                "npcap",
                "tap-windows",
                "hyper-v",
                "vethernet",
                "virtualbox",
                "vmware",
                "teredo",
                "isatap",
                "pseudo-interface",
                "bluetooth device (personal area",
                "wan miniport"
            };

            foreach (var kw in blacklistedKeywords)
            {
                if (combined.Contains(kw)) return true;
            }

            return false;
        }

        private static string FormatSpeed(long speedBits)
        {
            if (speedBits <= 0) return "Bilinmiyor";
            if (speedBits >= 1_000_000_000)
            {
                double gbps = speedBits / 1_000_000_000.0;
                return gbps >= 1.0 ? $"{gbps:F1} Gbps ({speedBits / 1_000_000} Mbps)" : $"{speedBits / 1_000_000} Mbps";
            }
            if (speedBits >= 1_000_000)
            {
                return $"{speedBits / 1_000_000} Mbps";
            }
            return $"{speedBits / 1_000} Kbps";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            double mb = bytes / (1024.0 * 1024.0);
            if (mb >= 1024.0)
            {
                return $"{mb / 1024.0:F2} GB";
            }
            return $"{mb:F1} MB";
        }

        #endregion

        #region 4. 1000 Mbps Gigabit Hız Testi (Speed Test Engine)

        public async Task RunSpeedTestAsync(IProgress<SpeedTestProgress> progress, CancellationToken ct)
        {
            await Task.Run(async () =>
            {
                var report = new SpeedTestProgress
                {
                    State = "TestingPing",
                    StatusMessage = "Gecikme (Ping & Jitter) ölçülüyor..."
                };
                progress.Report(report);

                // 1. Ping & Jitter measurement
                double pingAvg = 0;
                double jitter = 0;
                try
                {
                    using var ping = new Ping();
                    var pings = new List<long>();
                    for (int i = 0; i < 3; i++)
                    {
                        if (ct.IsCancellationRequested) return;
                        var reply = await ping.SendPingAsync("1.1.1.1", 1200);
                        if (reply.Status == IPStatus.Success)
                        {
                            pings.Add(reply.RoundtripTime);
                        }
                        await Task.Delay(100, ct);
                    }

                    if (pings.Count > 0)
                    {
                        pingAvg = pings.Average();
                        if (pings.Count > 1)
                        {
                            jitter = Math.Abs(pings.Max() - pings.Min()) / 2.0;
                        }
                    }
                }
                catch
                {
                    pingAvg = 15; // fallback
                }

                report.PingMs = Math.Round(pingAvg, 1);
                report.JitterMs = Math.Round(jitter, 1);
                report.State = "Downloading";
                report.StatusMessage = "1000 Mbps Çoklu Akış (Multi-Stream) İndirme Başlatılıyor...";
                progress.Report(report);

                // 2. High-speed multi-stream download test (6 seconds duration)
                // Cloudflare CDN large chunk endpoints (50MB - 100MB)
                string[] downloadUrls = new[]
                {
                    "https://speed.cloudflare.com/__down?bytes=50000000",
                    "https://speed.cloudflare.com/__down?bytes=50000000",
                    "https://speed.cloudflare.com/__down?bytes=50000000"
                };

                long totalBytes = 0;
                var startTime = DateTime.UtcNow;
                var testDuration = TimeSpan.FromSeconds(6.0);
                var endTime = startTime + testDuration;

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linkedCts.CancelAfter(testDuration);

                // Parallel download streams
                var downloadTasks = downloadUrls.Select(url => Task.Run(async () =>
                {
                    byte[] buffer = new byte[128 * 1024]; // 128 KB buffer for Gigabit throughput
                    while (!linkedCts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
                            if (!response.IsSuccessStatusCode) break;

                            using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token);
                            int bytesRead;
                            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, linkedCts.Token)) > 0)
                            {
                                Interlocked.Add(ref totalBytes, bytesRead);
                                if (linkedCts.Token.IsCancellationRequested) break;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch
                        {
                            // If one stream fails, retry after brief delay
                            try { await Task.Delay(200, linkedCts.Token); } catch { break; }
                        }
                    }
                }, linkedCts.Token)).ToList();

                // Sampling loop (every 250ms)
                long lastBytes = 0;
                var lastSampleTime = DateTime.UtcNow;
                double peakMbps = 0;

                while (!linkedCts.Token.IsCancellationRequested && DateTime.UtcNow < endTime)
                {
                    await Task.Delay(250);

                    var now = DateTime.UtcNow;
                    double deltaSeconds = (now - lastSampleTime).TotalSeconds;
                    long currentTotalBytes = Interlocked.Read(ref totalBytes);
                    long deltaBytes = currentTotalBytes - lastBytes;

                    if (deltaSeconds > 0.05)
                    {
                        double currentMbps = (deltaBytes * 8.0) / (deltaSeconds * 1_000_000.0);
                        if (currentMbps > peakMbps) peakMbps = currentMbps;

                        double totalElapsed = (now - startTime).TotalSeconds;
                        double averageMbps = totalElapsed > 0.1 ? (currentTotalBytes * 8.0) / (totalElapsed * 1_000_000.0) : 0;
                        double downloadedMb = currentTotalBytes / (1024.0 * 1024.0);
                        int percent = Math.Min(98, (int)((totalElapsed / testDuration.TotalSeconds) * 100));

                        report.CurrentMbps = Math.Round(currentMbps, 1);
                        report.AverageMbps = Math.Round(averageMbps, 1);
                        report.PeakMbps = Math.Round(peakMbps, 1);
                        report.DownloadedMb = Math.Round(downloadedMb, 1);
                        report.ProgressPercent = percent;
                        report.StatusMessage = $"1000 Mbps Akış: {report.CurrentMbps:F1} Mbps (İnen: {report.DownloadedMb:F1} MB)";
                        progress.Report(report);

                        lastBytes = currentTotalBytes;
                        lastSampleTime = now;
                    }
                }

                // Wait for all download tasks to wind down
                try
                {
                    await Task.WhenAll(downloadTasks);
                }
                catch { }

                if (ct.IsCancellationRequested)
                {
                    report.State = "Canceled";
                    report.StatusMessage = "Hız testi kullanıcı tarafından iptal edildi.";
                    progress.Report(report);
                    return;
                }

                // Final calculation
                double finalElapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                long finalTotalBytes = Interlocked.Read(ref totalBytes);
                double finalAvgMbps = finalElapsed > 0.1 ? (finalTotalBytes * 8.0) / (finalElapsed * 1_000_000.0) : 0;
                double finalMb = finalTotalBytes / (1024.0 * 1024.0);

                report.State = "Completed";
                report.CurrentMbps = Math.Round(finalAvgMbps, 1);
                report.AverageMbps = Math.Round(finalAvgMbps, 1);
                report.PeakMbps = Math.Round(peakMbps, 1);
                report.DownloadedMb = Math.Round(finalMb, 1);
                report.ProgressPercent = 100;
                report.StatusMessage = $"Test Tamamlandı! Ortalama: {report.AverageMbps:F1} Mbps | Tepe: {report.PeakMbps:F1} Mbps (Toplam {report.DownloadedMb:F1} MB)";
                progress.Report(report);
            }, ct);
        }

        #endregion

        #region 5. Ağ Teşhis Araçları (Ping, DNS Flush, Port Check)

        public async Task<PingResultItem> PingHostAsync(string host, int timeoutMs = 2000)
        {
            return await Task.Run(async () =>
            {
                var cleanHost = host.Trim().Replace("http://", "").Replace("https://", "").Split('/')[0];
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(cleanHost, timeoutMs);
                    if (reply.Status == IPStatus.Success)
                    {
                        return new PingResultItem
                        {
                            Host = cleanHost,
                            RoundtripMs = reply.RoundtripTime,
                            Status = "Başarılı (OK)",
                            IsSuccess = true,
                            Ttl = reply.Options?.Ttl ?? 64,
                            BufferSize = reply.Buffer?.Length ?? 32
                        };
                    }
                    else
                    {
                        return new PingResultItem
                        {
                            Host = cleanHost,
                            RoundtripMs = 0,
                            Status = reply.Status.ToString(),
                            IsSuccess = false
                        };
                    }
                }
                catch (Exception ex)
                {
                    return new PingResultItem
                    {
                        Host = cleanHost,
                        RoundtripMs = 0,
                        Status = $"Hata: {ex.Message}",
                        IsSuccess = false
                    };
                }
            });
        }

        public async Task<bool> FlushDnsCacheAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 1. Windows API direct flush
                    int apiResult = DnsFlushResolverCache();

                    // 2. Commandline ipconfig fallback
                    var psi = new ProcessStartInfo
                    {
                        FileName = "ipconfig",
                        Arguments = "/flushdns",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(3000);

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> RenewIpAddressAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "ipconfig",
                        Arguments = "/renew",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(6000);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<PortCheckResult> CheckPortAsync(string host, int port, int timeoutMs = 2500)
        {
            return await Task.Run(async () =>
            {
                var cleanHost = host.Trim().Replace("http://", "").Replace("https://", "").Split('/')[0];
                WellKnownServiceNames.TryGetValue(port, out var serviceName);
                if (string.IsNullOrEmpty(serviceName)) serviceName = $"Port {port}";

                var sw = Stopwatch.StartNew();
                try
                {
                    using var client = new TcpClient();
                    var connectTask = client.ConnectAsync(cleanHost, port);
                    var delayTask = Task.Delay(timeoutMs);

                    var completed = await Task.WhenAny(connectTask, delayTask);
                    sw.Stop();

                    if (completed == connectTask && client.Connected)
                    {
                        return new PortCheckResult
                        {
                            Host = cleanHost,
                            Port = port,
                            IsOpen = true,
                            ServiceName = serviceName,
                            LatencyMs = sw.ElapsedMilliseconds,
                            Message = $"Port {port} ({serviceName}) AÇIK! Yanıt süresi: {sw.ElapsedMilliseconds} ms."
                        };
                    }
                    else
                    {
                        return new PortCheckResult
                        {
                            Host = cleanHost,
                            Port = port,
                            IsOpen = false,
                            ServiceName = serviceName,
                            LatencyMs = timeoutMs,
                            Message = $"Port {port} ({serviceName}) KAPALI veya Zaman Aşımına Uğradı."
                        };
                    }
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new PortCheckResult
                    {
                        Host = cleanHost,
                        Port = port,
                        IsOpen = false,
                        ServiceName = serviceName,
                        LatencyMs = sw.ElapsedMilliseconds,
                        Message = $"Erişim Hatası: {ex.Message}"
                    };
                }
            });
        }

        #endregion

        #region 6. Güvenlik Duvarı ve Süreç Kontrolü

        /// <summary>Bakım'ın bu program için eklediği engelleme kuralının adı.</summary>
        public static string FirewallRuleName(string processPath) => $"Bakim_Block_{Path.GetFileNameWithoutExtension(processPath)}";

        // Eskiden netsh "runas" ile başlatılıp 3 sn bekleniyor ve sonuç ne olursa olsun true
        // dönülüyordu: UAC reddedilse bile arayüz "engellendi" gösteriyordu. Artık kural
        // NetSecurity cmdlet'leriyle yazılır ve çıkış kodu doğrulanır.
        public async Task<bool> BlockProcessInFirewallAsync(NetworkConnectionItem item)
        {
            if (string.IsNullOrWhiteSpace(item.ProcessPath) || !File.Exists(item.ProcessPath))
            {
                LastFirewallError = "Programın dosya yolu bulunamadı.";
                return false;
            }

            var result = await Activity.FirewallRules.AddBlockAsync(FirewallRuleName(item.ProcessPath), item.ProcessPath);
            LastFirewallError = result.Succeeded ? string.Empty : (result.Cancelled ? "Yönetici izni verilmedi." : result.Message);
            if (result.Succeeded) item.IsBlocked = true;
            return result.Succeeded;
        }

        public async Task<bool> UnblockProcessInFirewallAsync(NetworkConnectionItem item)
        {
            var result = await Activity.FirewallRules.RemoveAsync(FirewallRuleName(item.ProcessPath));
            LastFirewallError = result.Succeeded ? string.Empty : (result.Cancelled ? "Yönetici izni verilmedi." : result.Message);
            if (result.Succeeded) item.IsBlocked = false;
            return result.Succeeded;
        }

        /// <summary>Son güvenlik duvarı işleminin başarısızlık nedeni.</summary>
        public string LastFirewallError { get; private set; } = string.Empty;

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

        #endregion
    }
}
