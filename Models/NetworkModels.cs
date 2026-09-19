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

        public string LocalEndpoint => $"{LocalAddress}:{LocalPort}";
        public string RemoteEndpoint => RemotePort > 0 ? $"{RemoteAddress}:{RemotePort}" : (string.IsNullOrWhiteSpace(RemoteAddress) ? "*:*" : RemoteAddress);
        public bool IsExternal => !string.IsNullOrEmpty(RemoteAddress) && RemoteAddress != "0.0.0.0" && RemoteAddress != "127.0.0.1" && !RemoteAddress.StartsWith("192.168.") && !RemoteAddress.StartsWith("10.");
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
