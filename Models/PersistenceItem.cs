using System;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Models
{
    public enum PersistenceCategory
    {
        RegistryRun,
        StartupFolder,
        ScheduledTask,
        WindowsService,
        WmiEventConsumer,
        WinlogonIfeo,
        ShellExtension
    }

    public enum SignatureStatus
    {
        Verified,
        InvalidOrTampered,
        Unsigned
    }

    public partial class PersistenceItem : ObservableObject
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public PersistenceCategory Category { get; set; }
        public string CategoryDisplayName { get; set; } = string.Empty;
        public string LocationSource { get; set; } = string.Empty; // e.g., HKLM\...\Run or Task: \GoogleUpdateTask
        public string Publisher { get; set; } = string.Empty;
        public SignatureStatus Signature { get; set; } = SignatureStatus.Unsigned;
        public string SignatureSignerName { get; set; } = string.Empty;
        public string Sha256Hash { get; set; } = string.Empty;
        public bool IsMicrosoft { get; set; }
        public ImageSource? IconSource { get; set; }

        [ObservableProperty]
        private bool _isEnabled = true;

        [ObservableProperty]
        private string _virusTotalScore = "Analiz Et";

        [ObservableProperty]
        private int _maliciousDetections = -1;

        [ObservableProperty]
        private int _totalScanners = -1;

        [ObservableProperty]
        private bool _isBusy;

        public bool HasValidFile => !string.IsNullOrWhiteSpace(FilePath) && System.IO.File.Exists(FilePath);
        public bool IsRisk => Signature != SignatureStatus.Verified || MaliciousDetections > 0;
    }
}
