using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Models
{
    public class TelemetryMetrics
    {
        public int CpuUsagePercentage { get; set; }
        public int RamUsagePercentage { get; set; }
        public double UsedRamGb { get; set; }
        public double TotalRamGb { get; set; }
        public double FreeRamGb { get; set; }

        public long NetworkInBytesPerSec { get; set; }
        public long NetworkOutBytesPerSec { get; set; }
        public string FormattedNetworkInSpeed { get; set; } = "↓ 0.0 KB/s";
        public string FormattedNetworkOutSpeed { get; set; } = "↑ 0.0 KB/s";

        public long DiskReadBytesPerSec { get; set; }
        public long DiskWriteBytesPerSec { get; set; }
        public string FormattedDiskReadSpeed { get; set; } = "R: 0.0 MB/s";
        public string FormattedDiskWriteSpeed { get; set; } = "W: 0.0 MB/s";

        public string SystemDriveLetter { get; set; } = "C:";
        public int SystemDriveUsedPercentage { get; set; }
        public double SystemDriveFreeGb { get; set; }
        public double SystemDriveTotalGb { get; set; }

        public int CpuTemperatureC { get; set; } = 42;
        public int GpuTemperatureC { get; set; } = 50;
        public string ThermalStatus { get; set; } = "Normal";

        public string CpuName { get; set; } = "İşlemci";
        public string CpuClockSpeed { get; set; } = string.Empty;
        public string GpuName { get; set; } = "Ekran Kartı";
        public string GpuVram { get; set; } = string.Empty;
        public string GpuSecondaryName { get; set; } = string.Empty;
        public string GpuSecondaryVram { get; set; } = string.Empty;
        public bool HasDualGpu { get; set; }
        public string GpuBadgeText { get; set; } = "GPU";
        public string GpuTooltip { get; set; } = string.Empty;
    }

    public partial class ResourceHogItem : ObservableObject
    {
        public int Id { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public long MemoryBytes { get; set; }
        public string FormattedMemory { get; set; } = "0 MB";

        [ObservableProperty]
        private bool _isTerminating;
    }

    public class HealthScoreBreakdown
    {
        public int TotalScore { get; set; } = 95;
        public string ScoreTitle { get; set; } = "%95 Sistem Sağlığı";
        public string ScoreText { get; set; } = "Mükemmel - Sistem Hızlı";
        /// <summary>
        /// Sağlık skorunun anlamsal tonu. Model katmanı renk kodu taşımaz:
        /// renk seçimi XAML trigger'larında tema fırçalarıyla yapılır, böylece
        /// dört temada da doğru ve erişilebilir görünür.
        /// </summary>
        public Intent ScoreIntent { get; set; } = Intent.Success;
        public string ScoreStatusBrush { get; set; } = "SystemFillColorSuccessBrush";
        public int RamDeduction { get; set; }
        public int TempDeduction { get; set; }
        public int StartupDeduction { get; set; }
    }
}
