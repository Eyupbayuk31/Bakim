using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Models
{
    public partial class StartupProgramItem : ObservableObject
    {
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string RegistryPath { get; set; } = string.Empty;
        public bool IsCurrentUser { get; set; }

        [ObservableProperty]
        private bool _isEnabled = true;

        [ObservableProperty]
        private string _statusText = "Etkin";

        [ObservableProperty]
        private int _impactLevel = 1; // 3: Yüksek, 2: Orta, 1: Düşük

        [ObservableProperty]
        private string _impactText = "Düşük Etki";

        [ObservableProperty]
        private string _impactBadgeBrush = "SystemFillColorSuccessBrush";

        [ObservableProperty]
        private bool _isActionBusy;
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
        public string FormattedTotal => $"{TotalGb:F1} GB";
        public string FormattedFree => $"{FreeGb:F1} GB Boş";
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
        public string MotherboardModel { get; set; } = "Anakart Belirleniyor...";
        public string BiosVersion { get; set; } = string.Empty;
        public string RamSpeedMhz { get; set; } = string.Empty;

        public List<DriveInfoItem> Drives { get; set; } = new();
    }

    public partial class ProcessMemoryItem : ObservableObject
    {
        public int Id { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public long WorkingSetBytes { get; set; }
        public double WorkingSetMb { get; set; }
        public string FormattedMemory => $"{WorkingSetMb:F1} MB";
        public bool IsSystemProcess { get; set; }
        public string PriorityText { get; set; } = "Normal";
        public string AffinityText { get; set; } = "Tüm Çekirdekler";

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
        public string TotalBytesWritten { get; set; } = "Destekleniyor";
        public string PowerOnHours { get; set; } = "Aktif";
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
        public string LastModifiedFormatted { get; set; } = string.Empty;
        public string DriveLetter { get; set; } = "C:";
        public double SizePercentage { get; set; } = 10;

        [ObservableProperty]
        private bool _isDeleting;
    }
}
