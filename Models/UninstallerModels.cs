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
        RegistryKey,
        /// <summary>Tek bir kayıt defteri değeri ("HKCU\...\OpenWithProgids → VLC.mp4").</summary>
        RegistryValue
    }

    public enum InstallerType
    {
        Msi,
        InnoSetup,
        Nsis,
        InstallShield,
        GenericExe,
        Custom,
        WixBurn,
        Squirrel,
        Steam
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
        /// <summary>
        /// Sessiz kaldırma mümkün mü? InstallShield yanıt dosyası ister, Steam ve genel
        /// exe'lerin sessiz parametresi bilinmez (eskiden bunlar da "sessiz" sayılıyordu).
        /// </summary>
        public bool HasSilentUninstall => !string.IsNullOrWhiteSpace(QuietUninstallString) ||
                                          InstallerKind is InstallerType.Msi or InstallerType.InnoSetup or InstallerType.Nsis
                                              or InstallerType.WixBurn or InstallerType.Squirrel;
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

        /// <summary>
        /// Güven: 100 kesin kanıt (kaldırma öncesi iz, doğrulanmış kurulum klasörü),
        /// 90 yüksek (ad birebir), 60 orta, 30 düşük. İsimden gelen kanıt asla 100 olmaz.
        /// </summary>
        public int ConfidenceScore { get; set; } = 60;

        /// <summary>Bu öğenin neden kalıntı sayıldığı (kullanıcıya gösterilir).</summary>
        public string EvidenceText { get; set; } = string.Empty;

        /// <summary>
        /// Bilinen uygulama köklerinin dışındaki bir klasörün silinmesine izin verir
        /// (yalnızca kesin kanıtlı kurulum klasörleri için).
        /// </summary>
        public bool AllowOutsideKnownRoots { get; set; }

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isDeleted;

        public string TypeBadgeText => ItemType switch
        {
            LeftoverType.Folder => "Klasör",
            LeftoverType.File => "Dosya",
            LeftoverType.RegistryKey => "Kayıt Defteri",
            LeftoverType.RegistryValue => "Kayıt Değeri",
            _ => "Kalıntı"
        };

        public string TypeBadgeBrush => ItemType switch
        {
            LeftoverType.Folder => "AccentTextFillColorPrimaryBrush",
            LeftoverType.File => "SystemFillColorCautionBrush",
            LeftoverType.RegistryKey => "SystemFillColorCriticalBrush",
            LeftoverType.RegistryValue => "SystemFillColorCriticalBrush",
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
        public IntPtr WindowHandle { get; set; } = IntPtr.Zero;
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public string WindowClass { get; set; } = string.Empty;
        public int WindowLeft { get; set; }
        public int WindowTop { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public bool IsSelfProcess { get; set; }
        public bool IsSystemShell { get; set; }
        public InstalledAppItem? MatchedApp { get; set; }
        public bool IsFound => (!string.IsNullOrWhiteSpace(ExecutablePath) || MatchedApp != null) && !IsSelfProcess;
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
