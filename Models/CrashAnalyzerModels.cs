using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Models
{
    public partial class BsodCrashItem : ObservableObject
    {
        public string DumpFileName { get; set; } = string.Empty;
        public string DumpFilePath { get; set; } = string.Empty;
        public DateTime CrashTime { get; set; } = DateTime.Now;
        public string FormattedCrashTime => CrashTime.ToString("dd.MM.yyyy HH:mm:ss");
        public string BugCheckCode { get; set; } = "0x00000000";
        public string BugCheckString { get; set; } = "UNKNOWN_BUGCHECK";
        public string CausedByDriver { get; set; } = "Bilinmeyen Sürücü";
        public string DriverDescription { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
        public string FileSizeFormatted { get; set; } = "0 KB";
        public string SeverityBrush => "SystemFillColorCriticalBrush";
    }

    public partial class SystemEventItem : ObservableObject
    {
        public long RecordId { get; set; }
        public string LogName { get; set; } = "System"; // System, Application
        public DateTime TimeGenerated { get; set; } = DateTime.Now;
        public string FormattedTime => TimeGenerated.ToString("dd.MM.yyyy HH:mm:ss");
        public int EventId { get; set; }
        public string Level { get; set; } = "Hata"; // Kritik, Hata, Uyarı
        public string Source { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        public string LevelBrush => Level switch
        {
            "Kritik" => "SystemFillColorCriticalBrush",
            "Hata" => "SystemFillColorCautionBrush",
            _ => "AccentTextFillColorPrimaryBrush"
        };
    }

    public class SystemHealthStats
    {
        public int HealthScore { get; set; } = 100;
        public string HealthStatusText { get; set; } = "Mükemmel";
        public int TotalCrashesCount { get; set; }
        public int CriticalEvents7DaysCount { get; set; }
        public int ErrorEvents7DaysCount { get; set; }
        public string LastCrashDate { get; set; } = "Yok";

        public string ScoreBadgeBrush => HealthScore switch
        {
            >= 90 => "SystemFillColorSuccessBrush",
            >= 75 => "AccentTextFillColorPrimaryBrush",
            >= 55 => "SystemFillColorCautionBrush",
            _ => "SystemFillColorCriticalBrush"
        };
    }
}
