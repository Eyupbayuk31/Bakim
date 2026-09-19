using System;
using System.Collections.Generic;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Models
{
    public enum LeftoverType
    {
        Folder,
        File,
        RegistryKey
    }

    public enum InstallerType
    {
        Msi,
        InnoSetup,
        Nsis,
        InstallShield,
        GenericExe,
        Custom
    }

    public partial class InstalledAppItem : ObservableObject
    {
        public string DisplayName { get; set; } = string.Empty;
        public string Publisher { get; set; } = "Bilinmeyen Yayıncı";
        public string DisplayVersion { get; set; } = string.Empty;
        public string InstallDate { get; set; } = string.Empty;
        public long EstimatedSizeBytes { get; set; }
        public string FormattedSize { get; set; } = "0 MB";
        public string UninstallString { get; set; } = string.Empty;
        public string QuietUninstallString { get; set; } = string.Empty;
        public string InstallLocation { get; set; } = string.Empty;
        public string DisplayIconPath { get; set; } = string.Empty;
        public string RegistryKeyPath { get; set; } = string.Empty;
        public bool Is64Bit { get; set; } = true;
        public bool IsSystemComponent { get; set; } // VC++ redist, .NET, drivers
        public ImageSource? IconSource { get; set; }

        public InstallerType InstallerKind { get; set; } = InstallerType.GenericExe;
        public bool HasSilentUninstall => !string.IsNullOrWhiteSpace(QuietUninstallString) || InstallerKind != InstallerType.GenericExe;
        public bool IsMonitored { get; set; }

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _isSelected;
    }

    public partial class LeftoverItem : ObservableObject
    {
        public string Path { get; set; } = string.Empty;
        public LeftoverType ItemType { get; set; } = LeftoverType.Folder;
        public long SizeBytes { get; set; }
        public string FormattedSize { get; set; } = "0 KB";
        public string Description { get; set; } = string.Empty;
        public int ConfidenceScore { get; set; } = 100; // 0-100%

        [ObservableProperty]
        private bool _isSelected = true;

        [ObservableProperty]
        private bool _isDeleted;

        public string TypeBadgeText => ItemType switch
        {
            LeftoverType.Folder => "Klasör",
            LeftoverType.File => "Dosya",
            LeftoverType.RegistryKey => "Kayıt Defteri",
            _ => "Kalıntı"
        };

        public string TypeBadgeBrush => ItemType switch
        {
            LeftoverType.Folder => "AccentTextFillColorPrimaryBrush",
            LeftoverType.File => "SystemFillColorCautionBrush",
            LeftoverType.RegistryKey => "SystemFillColorCriticalBrush",
            _ => "TextFillColorSecondaryBrush"
        };
    }

    public class UninstallerStats
    {
        public int TotalAppsCount { get; set; }
        public long TotalFootprintBytes { get; set; }
        public string FormattedTotalFootprint { get; set; } = "0 GB";
        public int SystemComponentsCount { get; set; }
        public int LeftoversFoundCount { get; set; }
        public long TotalLeftoverBytes { get; set; }
        public string FormattedTotalLeftovers { get; set; } = "0 MB";
    }

    public class InstallationSnapshot
    {
        public string AppName { get; set; } = string.Empty;
        public DateTime SnapshotTime { get; set; } = DateTime.UtcNow;
        public HashSet<string> RegistryKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> FilePaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class SnapshotDelta
    {
        public string AppName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<string> AddedFiles { get; set; } = new();
        public List<string> AddedFolders { get; set; } = new();
        public List<string> AddedRegistryKeys { get; set; } = new();
        public long TotalSizeBytes { get; set; }
        public string FormattedSize { get; set; } = "0 MB";
    }

    public class HunterTargetInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public InstalledAppItem? MatchedApp { get; set; }
        public bool IsFound => !string.IsNullOrWhiteSpace(ExecutablePath) || MatchedApp != null;
    }

    public class BatchUninstallProgress
    {
        public int CurrentIndex { get; set; }
        public int TotalCount { get; set; }
        public string CurrentAppName { get; set; } = string.Empty;
        public long TotalCleanedBytes { get; set; }
        public bool IsCompleted { get; set; }
    }
}
