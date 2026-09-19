namespace Bakım.Models
{
    public class CleanResult
    {
        public int TotalFilesDeleted { get; set; }
        public int TotalFilesSkipped { get; set; }
        public long TotalBytesFreed { get; set; }
        public string FormattedBytesFreed => CleanCategory.FormatBytes(TotalBytesFreed);
    }

    public class SystemStats
    {
        public double TotalRamGb { get; set; }
        public double UsedRamGb { get; set; }
        public double FreeRamGb { get; set; }
        public int RamUsagePercentage { get; set; }
        public bool IsAdmin { get; set; }
        public string OsVersion { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
    }
}
