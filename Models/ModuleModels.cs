using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;

namespace Bakım.Models
{
    public partial class StartupProgramItem : ObservableObject
    {
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string CleanExePath { get; set; } = string.Empty;
        public string RegistryPath { get; set; } = string.Empty;
        public string LocationType { get; set; } = "Kayıt Defteri"; // HKCU, HKLM, WOW6432, Folder, CommonFolder
        public string Publisher { get; set; } = "Bilinmeyen Yayıncı";
        public bool IsCurrentUser { get; set; }
        public bool FileExists { get; set; } = true;
        public ImageSource? IconSource { get; set; }
        /// <summary>Ölçüm ayrıntısı ("Windows ölçümü · 3 açılış"); ölçüm yoksa açıklama.</summary>
        [ObservableProperty]
        private string _estimatedDelayText = string.Empty;

        [ObservableProperty]
        private bool _isEnabled = true;

        [ObservableProperty]
        private string _statusText = "Etkin";

        [ObservableProperty]
        private int _impactLevel; // Windows ölçümü: 3 ≥1 sn, 2 ≥300 ms, 1 daha az, 0 ölçüm yok

        [ObservableProperty]
        private string _impactText = "Ölçüm yok";

        [ObservableProperty]
        private string _impactBadgeBrush = "SystemFillColorSuccessBrush";

        [ObservableProperty]
        private bool _isActionBusy;
    }

    public class StartupSummaryStats
    {
        public int TotalCount { get; set; }
        public int EnabledCount { get; set; }
        public int DisabledCount { get; set; }
        /// <summary>Windows'un ölçümüne göre açılışı ≥1 sn yavaşlatan etkin uygulamalar.</summary>
        public int HighImpactCount { get; set; }
        /// <summary>Son açılış (Windows ölçümü); yoksa null.</summary>
        public int? LastBootMs { get; set; }
        public string FormattedBootDelay => LastBootMs is int ms ? Bakım.Core.Startup.BootEventParser.FormatSeconds(ms) : "Ölçüm yok";
        public string BootDetail { get; set; } = string.Empty;
    }

    public class DriveInfoItem
    {
        public string Name { get; set; } = string.Empty;
        public string VolumeLabel { get; set; } = string.Empty;
        public string DriveFormat { get; set; } = string.Empty;
        public double TotalGb { get; set; }
        public double FreeGb { get; set; }
        public double UsedGb { get; set; }
        public int UsagePercentage { get; set; }

        /// <summary>Bilgi gerçekten okunabildi mi? false ise sayılar gösterilmez.</summary>
        public bool IsAvailable { get; set; } = true;

        public string FormattedTotal => IsAvailable ? $"{TotalGb:F1} GB" : "—";
        public string FormattedFree => IsAvailable ? $"{FreeGb:F1} GB Boş" : "Okunamadı";
    }

    public class SystemHardwareStats
    {
        public string OsVersion { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string ProcessorCount { get; set; } = string.Empty;
        public int CpuPercentage { get; set; }
        public double TotalRamGb { get; set; }
        public double UsedRamGb { get; set; }
        public double FreeRamGb { get; set; }
        public int RamPercentage { get; set; }
        public bool IsAdmin { get; set; }
        public string DiskActivityText { get; set; } = "Normal";

        // Deep WMI Hardware Analytics (Master Protocol V7.0)
        public string CpuName { get; set; } = "İşlemci Belirleniyor...";
        public string CpuClockSpeed { get; set; } = string.Empty;
        public string CpuL3Cache { get; set; } = string.Empty;
        public string GpuName { get; set; } = "Grafik Kartı Belirleniyor...";
        public string GpuVram { get; set; } = string.Empty;
        public string GpuDriverVersion { get; set; } = "Güncel";
        public string GpuSecondaryName { get; set; } = string.Empty;
        public string GpuSecondaryVram { get; set; } = string.Empty;
        public bool HasDualGpu { get; set; }
        public string GpuBadgeText { get; set; } = "GPU";
        public string GpuTooltip { get; set; } = string.Empty;
        public string MotherboardModel { get; set; } = "Anakart Belirleniyor...";
        public string BiosVersion { get; set; } = string.Empty;
        public string RamSpeedMhz { get; set; } = string.Empty;

        // Extended Telemetry & Diagnostics (Revamp V8.0)
        public string NetworkAdapterName { get; set; } = "Yerel Ağ / Wi-Fi";
        public string NetworkIpAddress { get; set; } = "127.0.0.1";
        public string NetworkLinkSpeed { get; set; } = "1000 Mbps";
        public string DisplayResolution { get; set; } = "1920 x 1080";
        public string DisplayRefreshRate { get; set; } = "60 Hz";
        public string SystemUptimeText { get; set; } = "1 Saat";
        public string SecureBootStatus { get; set; } = "Aktif";
        public string TpmStatus { get; set; } = "TPM 2.0 Hazır";
        public string VirtualizationStatus { get; set; } = "Etkin (VT-x / AMD-V)";
        public string CpuCoresThreads { get; set; } = string.Empty;
        public string CpuPercentageText => $"%{CpuPercentage}";

        public List<DriveInfoItem> Drives { get; set; } = new();
    }

    public class DetailedMemoryComposition
    {
        public double InUseGb { get; set; }
        public double ModifiedGb { get; set; }
        public double StandbyGb { get; set; }
        public double FreeGb { get; set; }
        public double TotalGb { get; set; }

        public double InUsePercent => TotalGb > 0 ? Math.Round((InUseGb / TotalGb) * 100, 1) : 0;
        public double ModifiedPercent => TotalGb > 0 ? Math.Round((ModifiedGb / TotalGb) * 100, 1) : 0;
        public double StandbyPercent => TotalGb > 0 ? Math.Round((StandbyGb / TotalGb) * 100, 1) : 0;
        public double FreePercent => TotalGb > 0 ? Math.Round((FreeGb / TotalGb) * 100, 1) : 0;

        public double CommitTotalGb { get; set; }
        public double CommitLimitGb { get; set; }
        public int CommitPercentage => CommitLimitGb > 0 ? (int)Math.Round((CommitTotalGb / CommitLimitGb) * 100) : 0;

        public double PagedPoolMb { get; set; }
        public double NonPagedPoolMb { get; set; }
        public string RamSpeedText { get; set; } = "3200 MT/s";
        public string SlotsText { get; set; } = "2 / 4 Yuva";
        public string HardwareReservedText { get; set; } = "128 MB";
    }

    public partial class ProcessMemoryItem : ObservableObject
    {
        public int Id { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Publisher { get; set; } = "Bilinmeyen Yayıncı";
        public string FilePath { get; set; } = string.Empty;
        public string CommandLine { get; set; } = string.Empty;
        public ImageSource? IconSource { get; set; }
        public long WorkingSetBytes { get; set; }
        public double WorkingSetMb { get; set; }
        public string FormattedMemory => $"{WorkingSetMb:F1} MB";
        public long PrivateBytes { get; set; }
        public string FormattedPrivateBytes => $"{PrivateBytes / (1024.0 * 1024.0):F1} MB";
        public double CpuPercent { get; set; }
        public string FormattedCpu => CpuPercent > 0.05 ? $"{CpuPercent:F1}%" : "0.0%";
        public int ThreadCount { get; set; } = 1;
        public int HandleCount { get; set; }
        public bool IsSystemProcess { get; set; }
        public string PriorityText { get; set; } = "Normal";
        public string AffinityText { get; set; } = "Tüm Çekirdekler";
        public string UptimeText { get; set; } = "Bilinmiyor";

        [ObservableProperty]
        private bool _isSuspended;

        [ObservableProperty]
        private bool _isTerminating;
    }

    public class SmartDiskHealthItem
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string InterfaceType { get; set; } = "NVMe / PCIe";
        public string MediaType { get; set; } = "SSD (Katı Hal)";
        public string HealthStatus { get; set; } = "Mükemmel (%100 Sağlık)";
        public string HealthBadgeBrush { get; set; } = "SystemFillColorSuccessBrush";
        public string OperationalStatus { get; set; } = "Sağlam (OK)";
        public string Temperature { get; set; } = "Normal (34 °C)";
        public int TemperatureNumeric { get; set; } = 34;
        public string TemperatureColorBrush => TemperatureNumeric > 55 ? "SystemFillColorCriticalBrush" : TemperatureNumeric > 45 ? "SystemFillColorCautionBrush" : "SystemFillColorSuccessBrush";
        public string TotalBytesWritten { get; set; } = "Destekleniyor";
        public string PowerOnHours { get; set; } = "Aktif";
        public string PowerCycleCount { get; set; } = "Normal";
        public double SizeGb { get; set; }
        public string FormattedSize => $"{SizeGb:F1} GB";
        public int PartitionsCount { get; set; } = 1;
        public string FirmwareRevision { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
    }

    public partial class LargeDiskFileItem : ObservableObject
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string SizeFormatted { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string Category { get; set; } = "Diğer";
        public DateTime LastModified { get; set; } = DateTime.MinValue;
        public string LastModifiedFormatted { get; set; } = string.Empty;
        public string DriveLetter { get; set; } = "C:";
        public double SizePercentage { get; set; } = 10;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isDeleting;

        public SymbolRegular CategorySymbol => Category switch
        {
            "Video" => SymbolRegular.Video24,
            "Disk İmajı" => SymbolRegular.HardDrive24,
            "Arşiv" => SymbolRegular.FolderZip24,
            "Kurulum / Oyun" => SymbolRegular.AppGeneric24,
            _ => SymbolRegular.Document24
        };

        public string CategoryColorHex => Category switch
        {
            "Video" => "#A855F7",
            "Disk İmajı" => "#38BDF8",
            "Arşiv" => "#F59E0B",
            "Kurulum / Oyun" => "#10B981",
            _ => "#94A3B8"
        };
    }

    public class LargeFilesCategoryStats
    {
        public long TotalBytes { get; set; }
        public string TotalFormatted { get; set; } = "0 GB";
        public int FileCount { get; set; }

        public long VideoBytes { get; set; }
        public string VideoFormatted { get; set; } = "0 MB";
        public double VideoPercent { get; set; }

        public long DiskImageBytes { get; set; }
        public string DiskImageFormatted { get; set; } = "0 MB";
        public double DiskImagePercent { get; set; }

        public long ArchiveBytes { get; set; }
        public string ArchiveFormatted { get; set; } = "0 MB";
        public double ArchivePercent { get; set; }

        public long InstallerBytes { get; set; }
        public string InstallerFormatted { get; set; } = "0 MB";
        public double InstallerPercent { get; set; }

        public long OtherBytes { get; set; }
        public string OtherFormatted { get; set; } = "0 MB";
        public double OtherPercent { get; set; }

        public bool HasData => FileCount > 0 && TotalBytes > 0;
    }
}
