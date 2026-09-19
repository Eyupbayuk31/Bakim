using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Models
{
    public partial class ServiceItem : ObservableObject
    {
        public string ServiceName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        [ObservableProperty]
        private string _status = "Stopped"; // Running, Stopped, Paused

        [ObservableProperty]
        private string _startupType = "Manual"; // Auto, Manual, Disabled

        public string Account { get; set; } = "LocalSystem";
        public int ProcessId { get; set; }
        public string ExecutablePath { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public string SafetyClassification { get; set; } = "Üçüncü Taraf"; // Sistem Kritik, Güvenli Optimize, Üçüncü Taraf
        public bool IsCritical { get; set; }
        public bool IsOptimizable { get; set; }

        public bool IsRunning => Status.Equals("Running", StringComparison.OrdinalIgnoreCase);

        [ObservableProperty]
        private bool _isBusy;
    }

    public partial class DriverItem : ObservableObject
    {
        public string DeviceName { get; set; } = "Bilinmeyen Aygıt";
        public string DeviceClass { get; set; } = "Sistem";
        public string Manufacturer { get; set; } = "Standart";
        public string DriverVersion { get; set; } = "1.0.0.0";
        public string DriverDate { get; set; } = "-";
        public bool IsSigned { get; set; } = true;
        public string Signer { get; set; } = "Microsoft Windows";
        public string DeviceID { get; set; } = string.Empty;
        public string DriverPath { get; set; } = string.Empty;
        public bool IsProblematic { get; set; }
    }

    public class ServiceDriverStats
    {
        public int TotalServices { get; set; }
        public int RunningServices { get; set; }
        public int StoppedServices { get; set; }
        public int OptimizableServices { get; set; }

        public int TotalDrivers { get; set; }
        public int SignedDrivers { get; set; }
        public int ProblematicDrivers { get; set; }
    }
}
