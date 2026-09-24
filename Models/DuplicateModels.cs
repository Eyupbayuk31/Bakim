using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Models
{
    /// <summary>
    /// Yinelenen dosya grubundaki tekil dosya girdisi.
    /// </summary>
    public partial class DuplicateFileItem : ObservableObject
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string FormattedSize { get; set; } = string.Empty;
        public DateTime CreationTime { get; set; }
        public DateTime LastWriteTime { get; set; }
        public string Sha256Hash { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string Category { get; set; } = "Belge";

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isOriginal;

        public string StatusBadge => IsOriginal ? "Orijinal" : "Kopya";
    }

    /// <summary>
    /// Aynı içeriğe (SHA-256 hash) ve boyuta sahip dosyaların oluşturduğu grup.
    /// </summary>
    public partial class DuplicateFileGroup : ObservableObject
    {
        public int GroupId { get; set; }
        public string Hash { get; set; } = string.Empty;
        public string FileSizeFormatted { get; set; } = string.Empty;
        public long SingleFileSizeBytes { get; set; }
        public int GroupCount => Files.Count;
        public long TotalWastedBytes => Math.Max(0, (GroupCount - 1) * SingleFileSizeBytes);
        public string TotalWastedFormatted => CleanCategory.FormatBytes(TotalWastedBytes);

        public ObservableCollection<DuplicateFileItem> Files { get; } = new();

        [ObservableProperty]
        private bool _isExpanded = true;
    }

    /// <summary>
    /// Sistemde tespit edilen sahipsiz boş klasör girdisi.
    /// </summary>
    public partial class EmptyFolderItem : ObservableObject
    {
        public string FolderPath { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public string ParentPath { get; set; } = string.Empty;
        public DateTime CreationTime { get; set; }

        [ObservableProperty]
        private bool _isSelected = true;
    }

    /// <summary>
    /// Yinelenen dosya ve boş klasör tarama seçenekleri.
    /// </summary>
    public class DuplicateScanOptions
    {
        public string TargetPath { get; set; } = "C:\\";
        public long MinSizeBytes { get; set; } = 1024; // 1 KB altı boş/meta dosyaları atla
        public string FileTypeFilter { get; set; } = "All"; // All, Images, Videos, Documents, Archives, Audio
        public bool ExcludeSystemDirs { get; set; } = true;
    }

    /// <summary>
    /// Tarama ilerleme durumu bildirim modeli.
    /// </summary>
    public class DuplicateScanProgress
    {
        public int ScannedFiles { get; set; }
        public int CandidateGroups { get; set; }
        public int ConfirmedDuplicates { get; set; }
        public string CurrentFilePath { get; set; } = string.Empty;
        public string CurrentStage { get; set; } = "Dosyalar taranıyor...";
        public int ProgressPercentage { get; set; }
    }

    /// <summary>
    /// Tarama sonucu özet istatistik modeli.
    /// </summary>
    public class DuplicateScanSummary
    {
        public int TotalScannedFiles { get; set; }
        public int DuplicateGroupCount { get; set; }
        public int TotalDuplicateFilesCount { get; set; }
        public long TotalWastedBytes { get; set; }
        public string TotalWastedFormatted => CleanCategory.FormatBytes(TotalWastedBytes);
        public int EmptyFolderCount { get; set; }
    }
}
