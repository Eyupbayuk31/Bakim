using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Models
{
    public partial class NetworkConnectionItem : ObservableObject
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = "Bilinmeyen Süreç";
        public string ProcessPath { get; set; } = string.Empty;
        public string Protocol { get; set; } = "TCP";
        public string LocalAddress { get; set; } = string.Empty;
        public int LocalPort { get; set; }
        public string RemoteAddress { get; set; } = string.Empty;
        public int RemotePort { get; set; }
        public string State { get; set; } = "ESTABLISHED";

        [ObservableProperty]
        private string _remoteHostName = string.Empty;

        [ObservableProperty]
        private bool _isSuspicious;

        [ObservableProperty]
        private string _portCategory = "Standart";

        [ObservableProperty]
        private string _badgeBrush = "SystemFillColorSuccessBrush";

        [ObservableProperty]
        private string _bandwidthText = "-";

        [ObservableProperty]
        private bool _isBlocked;

        [ObservableProperty]
        private string _publisher = "Bilinmiyor";

        [ObservableProperty]
        private bool _isSigned;

        [ObservableProperty]
        private string _serviceDescription = string.Empty;

        [ObservableProperty]
        private string _threatDescription = string.Empty;

        public string LocalEndpoint => $"{LocalAddress}:{LocalPort}";
        public string RemoteEndpoint => RemotePort > 0 ? $"{RemoteAddress}:{RemotePort}" : (string.IsNullOrWhiteSpace(RemoteAddress) ? "*:*" : RemoteAddress);
        public bool IsExternal => !string.IsNullOrEmpty(RemoteAddress) && RemoteAddress != "0.0.0.0" && RemoteAddress != "127.0.0.1" && !RemoteAddress.StartsWith("192.168.") && !RemoteAddress.StartsWith("10.");
    }

    public partial class ListeningPortItem : ObservableObject
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = "Bilinmeyen Servis";
        public string ProcessPath { get; set; } = string.Empty;
        public string Protocol { get; set; } = "TCP";
        public string LocalAddress { get; set; } = "0.0.0.0";
        public int Port { get; set; }
        public string ServiceName { get; set; } = "Özel Servis";
        public bool IsExposedGlobally => LocalAddress == "0.0.0.0" || LocalAddress == "::" || LocalAddress == "*";
        public string ExposureText => IsExposedGlobally ? "Ağ Dinlemesi (0.0.0.0 - Dışa Açık)" : "Yalnızca Yerel (127.0.0.1 - Güvenli)";
        public string ExposureBadgeBrush => IsExposedGlobally ? "SystemFillColorCautionBrush" : "SystemFillColorSuccessBrush";
        public string PortCategory { get; set; } = "Dinleme Modu";
    }

    public partial class NetworkAdapterItem : ObservableObject
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TypeName { get; set; } = "Ethernet";
        public string Status { get; set; } = "Bağlı";
        public bool IsUp { get; set; }
        public string SpeedText { get; set; } = "-";
        public string Ipv4Address { get; set; } = "-";
        public string SubnetMask { get; set; } = "-";
        public string Gateway { get; set; } = "-";
        public string DnsServers { get; set; } = "-";
        public string MacAddress { get; set; } = "-";
        public string TotalReceivedFormatted { get; set; } = "0 MB";
        public string TotalSentFormatted { get; set; } = "0 MB";
    }

    public class SpeedTestProgress
    {
        public string State { get; set; } = "Idle"; // Idle, TestingPing, Downloading, Completed, Canceled, Error
        public double CurrentMbps { get; set; }
        public double PeakMbps { get; set; }
        public double AverageMbps { get; set; }
        public double PingMs { get; set; }
        public double JitterMs { get; set; }
        public double DownloadedMb { get; set; }
        public int ProgressPercent { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
    }

    public class PingResultItem
    {
        public string Host { get; set; } = string.Empty;
        public long RoundtripMs { get; set; }
        public string Status { get; set; } = "Yanıt Yok";
        public bool IsSuccess { get; set; }
        public int Ttl { get; set; }
        public int BufferSize { get; set; }
        public string FormattedResult => IsSuccess 
            ? $"{Host} adresinden yanıt: süre={RoundtripMs}ms TTL={Ttl} bayt={BufferSize}" 
            : $"{Host} adresine erişilemedi ({Status}).";
    }

    public class PortCheckResult
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public bool IsOpen { get; set; }
        public string ServiceName { get; set; } = string.Empty;
        public long LatencyMs { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class NetworkOverviewStats
    {
        public int TotalConnections { get; set; }
        public int EstablishedCount { get; set; }
        public int ListeningCount { get; set; }
        public double DownloadSpeedKb { get; set; }
        public double UploadSpeedKb { get; set; }
        public string FormattedDownloadSpeed => DownloadSpeedKb >= 1024 ? $"{DownloadSpeedKb / 1024:F1} MB/s" : $"{DownloadSpeedKb:F1} KB/s";
        public string FormattedUploadSpeed => UploadSpeedKb >= 1024 ? $"{UploadSpeedKb / 1024:F1} MB/s" : $"{UploadSpeedKb:F1} KB/s";
    }
}
