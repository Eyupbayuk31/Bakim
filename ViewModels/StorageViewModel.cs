using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Helpers;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    /// <summary>
    /// Depolama (MASTER_PLAN §2.2, §5.3): Büyük Dosyalar, Yinelenenler ve Boş Klasörler.
    /// Dosya silen araçlar salt okunur Sistem Bilgisi ekranından buraya taşındı.
    /// </summary>
    public partial class StorageViewModel : ObservableObject, IModuleViewModel
    {
        private readonly IDuplicateFinderService _duplicateService;
        private readonly ISystemInfoService _infoService;
        private readonly IDiskMapService _diskMap;
        private readonly Services.Safety.ISafeDeleteService _safeDelete;
        private readonly Services.Activity.IActivityService _activity;
        private CancellationTokenSource? _scanCts;
        private CancellationTokenSource? _duplicateScanCts;

        public StorageViewModel(IDuplicateFinderService duplicateService, ISystemInfoService infoService,
            IDiskMapService diskMap, Services.Safety.ISafeDeleteService safeDelete, Services.Activity.IActivityService activity)
        {
            _duplicateService = duplicateService;
            _infoService = infoService;
            _diskMap = diskMap;
            _safeDelete = safeDelete;
            _activity = activity;
            LargeFiles = new ObservableCollection<LargeDiskFileItem>();
            FilteredLargeFiles = new ObservableCollection<LargeDiskFileItem>();
            DuplicateGroups = new ObservableCollection<DuplicateFileGroup>();
            FilteredDuplicateGroups = new ObservableCollection<DuplicateFileGroup>();
            EmptyFolders = new ObservableCollection<EmptyFolderItem>();
            FilteredEmptyFolders = new ObservableCollection<EmptyFolderItem>();
            AvailableDrives = new ObservableCollection<string> { "Tüm Sürücüler" };

            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.IsReady && d.DriveType == DriveType.Fixed)
                        AvailableDrives.Add(d.Name);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            UpdateSelectedDriveSummary();
        }

        public ObservableCollection<LargeDiskFileItem> LargeFiles { get; }
        public ObservableCollection<LargeDiskFileItem> FilteredLargeFiles { get; }
        public ObservableCollection<DuplicateFileGroup> DuplicateGroups { get; }
        public ObservableCollection<DuplicateFileGroup> FilteredDuplicateGroups { get; }
        public ObservableCollection<EmptyFolderItem> EmptyFolders { get; }
        public ObservableCollection<EmptyFolderItem> FilteredEmptyFolders { get; }
        public ObservableCollection<string> AvailableDrives { get; }

        [ObservableProperty]
        private string _activeSubTab = "DiskMap"; // DiskMap, LargeFiles, Duplicates

        public bool IsDiskMapTab => ActiveSubTab == "DiskMap";
        public bool IsLargeFilesTab => ActiveSubTab == "LargeFiles";
        public bool IsDuplicatesTab => ActiveSubTab == "Duplicates";

        partial void OnActiveSubTabChanged(string value)
        {
            OnPropertyChanged(nameof(IsDiskMapTab));
            OnPropertyChanged(nameof(IsLargeFilesTab));
            OnPropertyChanged(nameof(IsDuplicatesTab));
        }

        [RelayCommand]
        public void SwitchSubTab(string tab) => ActiveSubTab = tab;

        /// <summary>Sistem Bilgisi'ndeki "Büyük dosyaları tara" düğmesi: sürücüyü seçip taramayı başlatır.</summary>
        public async Task ScanDriveAsync(string? driveName)
        {
            if (!string.IsNullOrWhiteSpace(driveName))
            {
                var match = AvailableDrives.FirstOrDefault(d => d.StartsWith(driveName.Substring(0, 1), StringComparison.OrdinalIgnoreCase));
                if (match != null) SelectedDriveFilter = match;
            }
            ActiveSubTab = "LargeFiles";
            await ScanLargeFilesAsync();
        }

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _isScanning;

        [ObservableProperty]
        private string _statusText = "Büyük dosyaları ya da yinelenenleri bulmak için bir tarama başlatın.";

        [ObservableProperty]
        private string _scanProgressText = "Büyük dosya taraması başlatılmadı.";

        [ObservableProperty]
        private string _selectedDriveFilter = "Tüm Sürücüler";

        [ObservableProperty]
        private long _minFileSizeThresholdMb = 1024; // 1 GB varsayılan

        [ObservableProperty]
        private string _selectedCategoryFilter = "Tümü";

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private string _selectedSortMode = "SizeDesc"; // SizeDesc, SizeAsc, DateDesc, DateAsc, NameAsc, NameDesc

        [ObservableProperty]
        private bool _hasScanned;

        [ObservableProperty]
        private bool _hasResults;

        [ObservableProperty]
        private bool _hasNoResultsAfterScan;

        [ObservableProperty]
        private LargeFilesCategoryStats _categoryStats = new();

        [ObservableProperty]
        private bool _isAllSelected;

        [ObservableProperty]
        private int _selectedFilesCount;

        [ObservableProperty]
        private string _selectedFilesTotalBytesFormatted = "0 B";

        public bool HasSelectedFiles => SelectedFilesCount > 0;

        // Seçili Sürücü Depolama Özeti (Hero Empty State için)
        [ObservableProperty]
        private string _selectedDriveName = string.Empty;

        [ObservableProperty]
        private string _selectedDriveVolumeLabel = "Yerel Disk";

        [ObservableProperty]
        private string _selectedDriveFormat = "NTFS";

        [ObservableProperty]
        private double _selectedDriveTotalGb;

        [ObservableProperty]
        private double _selectedDriveFreeGb;

        [ObservableProperty]
        private double _selectedDriveUsedGb;

        [ObservableProperty]
        private double _selectedDriveUsagePercentage;

        [ObservableProperty]
        private string _selectedDriveUsageBarBrush = "#38BDF8";

        public bool IsThreshold100Mb => MinFileSizeThresholdMb == 100;
        public bool IsThreshold500Mb => MinFileSizeThresholdMb == 500;
        public bool IsThreshold1Gb => MinFileSizeThresholdMb == 1024;
        public bool IsThreshold2Gb => MinFileSizeThresholdMb == 2048;
        public bool IsThreshold5Gb => MinFileSizeThresholdMb == 5120;

        public bool IsCategoryAll => SelectedCategoryFilter == "Tümü";
        public bool IsCategoryVideo => SelectedCategoryFilter == "Video";
        public bool IsCategoryDiskImage => SelectedCategoryFilter == "Disk İmajı";
        public bool IsCategoryArchive => SelectedCategoryFilter == "Arşiv";
        public bool IsCategoryInstaller => SelectedCategoryFilter == "Kurulum / Oyun";

        partial void OnSelectedDriveFilterChanged(string value)
        {
            UpdateSelectedDriveSummary();
        }

        partial void OnSearchQueryChanged(string value)
        {
            UpdateLargeFilesFilter();
        }

        partial void OnSelectedSortModeChanged(string value)
        {
            UpdateLargeFilesFilter();
        }

        private bool _isUpdatingSelectionInternally;
        partial void OnIsAllSelectedChanged(bool value)
        {
            if (_isUpdatingSelectionInternally) return;
            foreach (var file in FilteredLargeFiles)
            {
                file.IsSelected = value;
            }
            UpdateSelectedFilesSummary(skipAllSelectedCheck: true);
        }

        partial void OnMinFileSizeThresholdMbChanged(long value)
        {
            OnPropertyChanged(nameof(IsThreshold100Mb));
            OnPropertyChanged(nameof(IsThreshold500Mb));
            OnPropertyChanged(nameof(IsThreshold1Gb));
            OnPropertyChanged(nameof(IsThreshold2Gb));
            OnPropertyChanged(nameof(IsThreshold5Gb));
        }

        partial void OnSelectedCategoryFilterChanged(string value)
        {
            OnPropertyChanged(nameof(IsCategoryAll));
            OnPropertyChanged(nameof(IsCategoryVideo));
            OnPropertyChanged(nameof(IsCategoryDiskImage));
            OnPropertyChanged(nameof(IsCategoryArchive));
            OnPropertyChanged(nameof(IsCategoryInstaller));
            UpdateLargeFilesFilter();
        }

        [ObservableProperty]
        private string _totalLargeFilesSizeFormatted = "0 Dosya (0 GB)";

        [RelayCommand]
        public async Task ScanLargeFilesAsync()
        {
            if (IsScanning) return;

            IsScanning = true;
            LargeFiles.Clear();
            _scanCts = new CancellationTokenSource();
            ScanProgressText = "Sürücüler taranıyor, lütfen bekleyin...";

            var progress = new Progress<string>(dir =>
            {
                ScanProgressText = dir;
            });

            try
            {
                long minBytes = MinFileSizeThresholdMb * 1024L * 1024L;
                var found = await _infoService.ScanLargeFilesAsync(SelectedDriveFilter, minBytes, progress, _scanCts.Token);

                foreach (var f in found)
                {
                    f.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(LargeDiskFileItem.IsSelected))
                        {
                            UpdateSelectedFilesSummary();
                        }
                    };
                    LargeFiles.Add(f);
                }

                HasScanned = true;
                UpdateLargeFilesFilter();
                ScanProgressText = $"Tarama tamamlandı! {found.Count} adet büyük dosya bulundu.";
            }
            catch (OperationCanceledException)
            {
                ScanProgressText = "Büyük dosya taraması kullanıcı tarafından durduruldu.";
            }
            catch (Exception ex)
            {
                ScanProgressText = $"Tarama hatası: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
            }
        }

        [RelayCommand]
        public void CancelScan()
        {
            _scanCts?.Cancel();
        }

        [RelayCommand]
        public void OpenFileLocation(LargeDiskFileItem? file)
        {
            if (file == null || string.IsNullOrWhiteSpace(file.FilePath)) return;

            try
            {
                if (File.Exists(file.FilePath))
                {
                    Process.Start("explorer.exe", $"/select,\"{file.FilePath}\"");
                }
                else if (Directory.Exists(file.DirectoryPath))
                {
                    Process.Start("explorer.exe", $"\"{file.DirectoryPath}\"");
                }
            }
            catch { }
        }

        [RelayCommand]
        public async Task DeleteLargeFileAsync(LargeDiskFileItem? file)
        {
            if (file == null || file.IsDeleting) return;

            var confirm = System.Windows.MessageBox.Show(
                $"'{file.FilePath}' kalıcı olarak silinecek (Geri Dönüşüm Kutusu atlanır). Bu işlem geri alınamaz.\n\nDevam edilsin mi?",
                "Kalıcı Sil", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;

            file.IsDeleting = true;
            try
            {
                bool deleted = await _infoService.DeleteLargeFileAsync(file.FilePath);
                if (deleted)
                {
                    LargeFiles.Remove(file);
                    UpdateLargeFilesFilter();
                    ScanProgressText = $"'{file.FileName}' başarıyla silindi.";
                }
                else
                {
                    ScanProgressText = $"'{file.FileName}' silinemedi: {_infoService.LastDeleteError}";
                }
            }
            finally
            {
                file.IsDeleting = false;
            }
        }

        [RelayCommand]
        public async Task SendToRecycleBinAsync(LargeDiskFileItem? file)
        {
            if (file == null || file.IsDeleting) return;

            file.IsDeleting = true;
            try
            {
                bool moved = await _infoService.DeleteLargeFileToRecycleBinAsync(file.FilePath);
                if (moved)
                {
                    LargeFiles.Remove(file);
                    UpdateLargeFilesFilter();
                    ScanProgressText = $"'{file.FileName}' Geri Dönüşüm Kutusuna taşındı.";
                }
                else
                {
                    ScanProgressText = $"'{file.FileName}' Geri Dönüşüm Kutusuna taşınamadı: {_infoService.LastDeleteError}";
                }
            }
            finally
            {
                file.IsDeleting = false;
            }
        }

        [RelayCommand]
        public async Task SetThresholdAsync(object? parameter)
        {
            long threshold = 1024;
            if (parameter is long l)
            {
                threshold = l;
            }
            else if (parameter is int i)
            {
                threshold = i;
            }
            else if (parameter is string s && long.TryParse(s, out long parsed))
            {
                threshold = parsed;
            }

            MinFileSizeThresholdMb = threshold;
            await ScanLargeFilesAsync();
        }

        [RelayCommand]
        public void SetCategoryFilter(string? category)
        {
            SelectedCategoryFilter = string.IsNullOrWhiteSpace(category) ? "Tümü" : category;
        }

        [RelayCommand]
        public void SetSortMode(string? sortMode)
        {
            SelectedSortMode = string.IsNullOrWhiteSpace(sortMode) ? "SizeDesc" : sortMode;
        }

        [RelayCommand]
        public void ToggleSelectAll()
        {
            if (FilteredLargeFiles.Count == 0) return;
            bool target = !IsAllSelected;
            _isUpdatingSelectionInternally = true;
            IsAllSelected = target;
            foreach (var f in FilteredLargeFiles)
            {
                f.IsSelected = target;
            }
            _isUpdatingSelectionInternally = false;
            UpdateSelectedFilesSummary(skipAllSelectedCheck: true);
        }

        [RelayCommand]
        public async Task RecycleSelectedFilesAsync()
        {
            var selected = FilteredLargeFiles.Where(f => f.IsSelected).ToList();
            if (selected.Count == 0) return;

            int movedCount = 0;
            foreach (var file in selected)
            {
                file.IsDeleting = true;
                try
                {
                    bool moved = await _infoService.DeleteLargeFileToRecycleBinAsync(file.FilePath);
                    if (moved)
                    {
                        LargeFiles.Remove(file);
                        movedCount++;
                    }
                }
                finally
                {
                    file.IsDeleting = false;
                }
            }

            UpdateLargeFilesFilter();
            int notMoved = selected.Count - movedCount;
            ScanProgressText = notMoved == 0
                ? $"{movedCount} dosya Geri Dönüşüm Kutusuna taşındı."
                : $"{movedCount} dosya taşındı, {notMoved} dosya taşınamadı (korumalı ya da kullanımda).";
        }

        [RelayCommand]
        public async Task DeleteSelectedFilesAsync()
        {
            var selected = FilteredLargeFiles.Where(f => f.IsSelected).ToList();
            if (selected.Count == 0) return;

            var confirm = System.Windows.MessageBox.Show(
                $"{selected.Count} dosya kalıcı olarak silinecek (Geri Dönüşüm Kutusu atlanır). Bu işlem geri alınamaz.\n\nDevam edilsin mi?",
                "Seçilenleri Kalıcı Sil", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;

            int deletedCount = 0;
            foreach (var file in selected)
            {
                file.IsDeleting = true;
                try
                {
                    bool deleted = await _infoService.DeleteLargeFileAsync(file.FilePath);
                    if (deleted)
                    {
                        LargeFiles.Remove(file);
                        deletedCount++;
                    }
                }
                finally
                {
                    file.IsDeleting = false;
                }
            }

            UpdateLargeFilesFilter();
            int notDeleted = selected.Count - deletedCount;
            ScanProgressText = notDeleted == 0
                ? $"{deletedCount} dosya kalıcı olarak silindi."
                : $"{deletedCount} dosya silindi, {notDeleted} dosya silinemedi (korumalı ya da kullanımda).";
        }

        [RelayCommand]
        public void CopyFilePath(LargeDiskFileItem? file)
        {
            if (file == null || string.IsNullOrWhiteSpace(file.FilePath)) return;
            try
            {
                System.Windows.Clipboard.SetText(file.FilePath);
                ScanProgressText = $"Dosya yolu panoya kopyalandı: {file.FileName}";
            }
            catch { }
        }

        private void UpdateLargeFilesFilter()
        {
            IEnumerable<LargeDiskFileItem> query = LargeFiles;

            // Kategori Filtresi
            if (SelectedCategoryFilter != "Tümü")
            {
                query = query.Where(f => f.Category.Equals(SelectedCategoryFilter, StringComparison.OrdinalIgnoreCase) ||
                                         f.Category.Contains(SelectedCategoryFilter, StringComparison.OrdinalIgnoreCase));
            }

            // Canlı Arama (dosya adı, dosya yolu veya uzantı)
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                string term = SearchQuery.Trim();
                query = query.Where(f => f.FileName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                         f.FilePath.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                         f.Extension.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            // Sıralama
            query = SelectedSortMode switch
            {
                "SizeAsc" => query.OrderBy(f => f.SizeBytes),
                "DateDesc" => query.OrderByDescending(f => f.LastModified),
                "DateAsc" => query.OrderBy(f => f.LastModified),
                "NameAsc" => query.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase),
                "NameDesc" => query.OrderByDescending(f => f.FileName, StringComparer.OrdinalIgnoreCase),
                _ => query.OrderByDescending(f => f.SizeBytes) // "SizeDesc"
            };

            FilteredLargeFiles.Clear();
            long totalFilteredBytes = 0;
            foreach (var file in query)
            {
                FilteredLargeFiles.Add(file);
                totalFilteredBytes += file.SizeBytes;
            }

            double gb = totalFilteredBytes / (1024.0 * 1024.0 * 1024.0);
            TotalLargeFilesSizeFormatted = FilteredLargeFiles.Count > 0
                ? $"{FilteredLargeFiles.Count} Dosya ({gb:F1} GB Toplam Alan)"
                : "0 Dosya (0 GB)";

            HasResults = FilteredLargeFiles.Count > 0;
            HasNoResultsAfterScan = HasScanned && FilteredLargeFiles.Count == 0;

            UpdateSelectedFilesSummary();
            RecalculateCategoryStats();
        }

        private void UpdateSelectedDriveSummary()
        {
            try
            {
                DriveInfo? target = null;
                var allDrives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToList();

                if (SelectedDriveFilter != "Tüm Sürücüler")
                {
                    target = allDrives.FirstOrDefault(d => d.Name.Equals(SelectedDriveFilter, StringComparison.OrdinalIgnoreCase) ||
                                                           d.Name.StartsWith(SelectedDriveFilter.Substring(0, 1), StringComparison.OrdinalIgnoreCase));
                }

                if (target == null)
                {
                    target = allDrives.FirstOrDefault(d => d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase))
                             ?? allDrives.FirstOrDefault();
                }

                if (target != null && target.IsReady)
                {
                    SelectedDriveName = target.Name;
                    SelectedDriveVolumeLabel = string.IsNullOrWhiteSpace(target.VolumeLabel) ? "Yerel Disk" : target.VolumeLabel;
                    SelectedDriveFormat = target.DriveFormat;

                    double totalGb = target.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    double freeGb = target.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    double usedGb = Math.Max(0, totalGb - freeGb);
                    double usagePct = totalGb > 0 ? (usedGb / totalGb) * 100.0 : 0;

                    SelectedDriveTotalGb = Math.Round(totalGb, 1);
                    SelectedDriveFreeGb = Math.Round(freeGb, 1);
                    SelectedDriveUsedGb = Math.Round(usedGb, 1);
                    SelectedDriveUsagePercentage = Math.Round(usagePct, 1);

                    SelectedDriveUsageBarBrush = usagePct switch
                    {
                        >= 90 => "#EF4444",
                        >= 75 => "#F59E0B",
                        _ => "#38BDF8"
                    };
                }
            }
            catch
            {
                // Savunmacı
            }
        }

        private void UpdateSelectedFilesSummary(bool skipAllSelectedCheck = false)
        {
            var selected = FilteredLargeFiles.Where(f => f.IsSelected).ToList();
            SelectedFilesCount = selected.Count;
            long bytes = selected.Sum(f => f.SizeBytes);

            if (bytes >= 1024L * 1024 * 1024)
                SelectedFilesTotalBytesFormatted = $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
            else if (bytes >= 1024L * 1024)
                SelectedFilesTotalBytesFormatted = $"{bytes / (1024.0 * 1024):F1} MB";
            else
                SelectedFilesTotalBytesFormatted = $"{bytes / 1024.0:F0} KB";

            if (!skipAllSelectedCheck)
            {
                _isUpdatingSelectionInternally = true;
                IsAllSelected = FilteredLargeFiles.Count > 0 && selected.Count == FilteredLargeFiles.Count;
                _isUpdatingSelectionInternally = false;
            }

            OnPropertyChanged(nameof(HasSelectedFiles));
        }

        private void RecalculateCategoryStats()
        {
            long totalBytes = LargeFiles.Sum(f => f.SizeBytes);
            int totalCount = LargeFiles.Count;

            if (totalCount == 0 || totalBytes == 0)
            {
                CategoryStats = new LargeFilesCategoryStats();
                return;
            }

            long videoBytes = 0, diskImageBytes = 0, archiveBytes = 0, installerBytes = 0, otherBytes = 0;

            foreach (var f in LargeFiles)
            {
                switch (f.Category)
                {
                    case "Video":
                        videoBytes += f.SizeBytes;
                        break;
                    case "Disk İmajı":
                        diskImageBytes += f.SizeBytes;
                        break;
                    case "Arşiv":
                        archiveBytes += f.SizeBytes;
                        break;
                    case "Kurulum / Oyun":
                        installerBytes += f.SizeBytes;
                        break;
                    default:
                        otherBytes += f.SizeBytes;
                        break;
                }
            }

            static string FormatBytes(long bytes)
            {
                if (bytes >= 1024L * 1024 * 1024)
                    return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
                if (bytes >= 1024L * 1024)
                    return $"{bytes / (1024.0 * 1024):F1} MB";
                return $"{bytes / 1024.0:F0} KB";
            }

            var stats = new LargeFilesCategoryStats
            {
                TotalBytes = totalBytes,
                TotalFormatted = FormatBytes(totalBytes),
                FileCount = totalCount,

                VideoBytes = videoBytes,
                VideoFormatted = FormatBytes(videoBytes),
                VideoPercent = Math.Round((double)videoBytes / totalBytes * 100.0, 1),

                DiskImageBytes = diskImageBytes,
                DiskImageFormatted = FormatBytes(diskImageBytes),
                DiskImagePercent = Math.Round((double)diskImageBytes / totalBytes * 100.0, 1),

                ArchiveBytes = archiveBytes,
                ArchiveFormatted = FormatBytes(archiveBytes),
                ArchivePercent = Math.Round((double)archiveBytes / totalBytes * 100.0, 1),

                InstallerBytes = installerBytes,
                InstallerFormatted = FormatBytes(installerBytes),
                InstallerPercent = Math.Round((double)installerBytes / totalBytes * 100.0, 1),

                OtherBytes = otherBytes,
                OtherFormatted = FormatBytes(otherBytes),
                OtherPercent = Math.Round((double)otherBytes / totalBytes * 100.0, 1)
            };

            CategoryStats = stats;
        }

        #region Yinelenen Dosya & Boş Klasör Yöneticisi

        [ObservableProperty]
        private bool _isDuplicatesScanning;

        [ObservableProperty]
        private string _duplicateProgressText = "Tarama başlatılmadı.";

        [ObservableProperty]
        private int _duplicateProgressPercent;

        [ObservableProperty]
        private string _duplicateCurrentFilePath = string.Empty;

        [ObservableProperty]
        private string _duplicateViewMode = "Duplicates"; // Duplicates, EmptyFolders

        public bool IsShowingDuplicates => DuplicateViewMode == "Duplicates";
        public bool IsShowingEmptyFolders => DuplicateViewMode == "EmptyFolders";

        [ObservableProperty]
        private string _duplicateTypeFilter = "All"; // All, Images, Videos, Documents, Archives, Audio

        public bool IsDuplicateFilterAll => DuplicateTypeFilter == "All";
        public bool IsDuplicateFilterImages => DuplicateTypeFilter == "Images";
        public bool IsDuplicateFilterVideos => DuplicateTypeFilter == "Videos";
        public bool IsDuplicateFilterDocs => DuplicateTypeFilter == "Documents";
        public bool IsDuplicateFilterArchives => DuplicateTypeFilter == "Archives";
        public bool IsDuplicateFilterAudio => DuplicateTypeFilter == "Audio";

        [ObservableProperty]
        private string _duplicateSearchText = string.Empty;

        [ObservableProperty]
        private string _emptyFolderSearchText = string.Empty;

        [ObservableProperty]
        private string _duplicateSelectedDrive = "Tüm Sürücüler";

        [ObservableProperty]
        private string _customScanFolderPath = string.Empty;

        [ObservableProperty]
        private bool _isCustomFolderActive;

        [ObservableProperty]
        private long _totalWastedBytes;

        [ObservableProperty]
        private string _totalWastedFormatted = "0 B";

        [ObservableProperty]
        private int _totalDuplicateCount;

        [ObservableProperty]
        private int _totalEmptyFolderCount;

        [ObservableProperty]
        private int _selectedDuplicateCount;

        [ObservableProperty]
        private string _selectedDuplicateBytesFormatted = "0 B";

        [ObservableProperty]
        private int _selectedEmptyFolderCount;

        [ObservableProperty]
        private string _duplicateOperationResult = string.Empty;

        [ObservableProperty]
        private bool _hasDuplicateOperationResult;

        [ObservableProperty]
        private bool _hasDuplicateScanned;

        [ObservableProperty]
        private bool _hasEmptyFolderScanned;

        [ObservableProperty]
        private bool _deletePermanently;

        partial void OnDuplicateViewModeChanged(string value)
        {
            OnPropertyChanged(nameof(IsShowingDuplicates));
            OnPropertyChanged(nameof(IsShowingEmptyFolders));
        }

        partial void OnDuplicateTypeFilterChanged(string value)
        {
            OnPropertyChanged(nameof(IsDuplicateFilterAll));
            OnPropertyChanged(nameof(IsDuplicateFilterImages));
            OnPropertyChanged(nameof(IsDuplicateFilterVideos));
            OnPropertyChanged(nameof(IsDuplicateFilterDocs));
            OnPropertyChanged(nameof(IsDuplicateFilterArchives));
            OnPropertyChanged(nameof(IsDuplicateFilterAudio));
            UpdateDuplicateFilters();
        }

        partial void OnDuplicateSearchTextChanged(string value)
        {
            UpdateDuplicateFilters();
        }

        partial void OnEmptyFolderSearchTextChanged(string value)
        {
            UpdateEmptyFolderFilters();
        }

        [RelayCommand]
        public void SwitchDuplicateView(string view)
        {
            DuplicateViewMode = view;
        }

        [RelayCommand]
        public void SetDuplicateTypeFilter(string filter)
        {
            DuplicateTypeFilter = filter;
        }

        [RelayCommand]
        public async Task StartDuplicateScanAsync()
        {
            if (IsDuplicatesScanning) return;

            IsDuplicatesScanning = true;
            HasDuplicateOperationResult = false;
            DuplicateProgressPercent = 0;
            DuplicateProgressText = "Tarama başlatılıyor...";
            _duplicateScanCts = new CancellationTokenSource();

            DuplicateGroups.Clear();
            FilteredDuplicateGroups.Clear();
            TotalWastedBytes = 0;
            TotalWastedFormatted = "0 B";
            TotalDuplicateCount = 0;
            SelectedDuplicateCount = 0;
            SelectedDuplicateBytesFormatted = "0 B";

            try
            {
                var targetPaths = new List<string>();
                if (IsCustomFolderActive && !string.IsNullOrWhiteSpace(CustomScanFolderPath) && Directory.Exists(CustomScanFolderPath))
                {
                    targetPaths.Add(CustomScanFolderPath);
                }
                else if (DuplicateSelectedDrive != "Tüm Sürücüler" && Directory.Exists(DuplicateSelectedDrive))
                {
                    targetPaths.Add(DuplicateSelectedDrive);
                }
                else
                {
                    foreach (var d in AvailableDrives.Where(x => x != "Tüm Sürücüler"))
                    {
                        if (Directory.Exists(d))
                            targetPaths.Add(d);
                    }
                }

                if (targetPaths.Count == 0)
                {
                    DuplicateProgressText = "Taranacak geçerli bir sürücü veya klasör bulunamadı.";
                    IsDuplicatesScanning = false;
                    return;
                }

                var progress = new Progress<DuplicateScanProgress>(p =>
                {
                    DuplicateProgressText = $"{p.CurrentStage} ({p.ScannedFiles:N0} dosya incelendi)";
                    DuplicateProgressPercent = p.ProgressPercentage;
                    DuplicateCurrentFilePath = p.CurrentFilePath;
                });

                var allGroups = new List<DuplicateFileGroup>();
                foreach (var path in targetPaths)
                {
                    _duplicateScanCts.Token.ThrowIfCancellationRequested();
                    var options = new DuplicateScanOptions
                    {
                        TargetPath = path,
                        FileTypeFilter = DuplicateTypeFilter,
                        MinSizeBytes = 1024,
                        ExcludeSystemDirs = true
                    };

                    var groups = await _duplicateService.ScanDuplicatesAsync(options, progress, _duplicateScanCts.Token);
                    allGroups.AddRange(groups);
                }

                // Eşleşen hash gruplarını birleştir
                var mergedGroups = allGroups
                    .GroupBy(g => g.Hash)
                    .Select((g, idx) =>
                    {
                        var first = g.First();
                        var group = new DuplicateFileGroup
                        {
                            GroupId = idx + 1,
                            Hash = first.Hash,
                            FileSizeFormatted = first.FileSizeFormatted,
                            SingleFileSizeBytes = first.SingleFileSizeBytes,
                            IsExpanded = true
                        };

                        var allFiles = g.SelectMany(x => x.Files)
                                        .OrderBy(f => f.CreationTime)
                                        .ToList();

                        for (int i = 0; i < allFiles.Count; i++)
                        {
                            var file = allFiles[i];
                            file.IsOriginal = (i == 0);
                            file.IsSelected = (i > 0);
                            file.PropertyChanged += (s, e) =>
                            {
                                if (e.PropertyName == nameof(DuplicateFileItem.IsSelected))
                                {
                                    UpdateDuplicateSelectionSummary();
                                }
                            };
                            group.Files.Add(file);
                        }

                        return group;
                    })
                    .Where(g => g.Files.Count > 1)
                    .ToList();

                DuplicateGroups.Clear();
                foreach (var g in mergedGroups)
                {
                    DuplicateGroups.Add(g);
                }

                HasDuplicateScanned = true;
                UpdateDuplicateFilters();
                RecalculateDuplicateTotals();
                UpdateDuplicateSelectionSummary();

                DuplicateProgressText = $"Tarama tamamlandı: {DuplicateGroups.Count} yinelenen grup ({TotalDuplicateCount} dosya, {TotalWastedFormatted} gereksiz alan).";
                DuplicateProgressPercent = 100;
            }
            catch (OperationCanceledException)
            {
                DuplicateProgressText = "Tarama kullanıcı tarafından durduruldu.";
            }
            catch (Exception ex)
            {
                DuplicateProgressText = $"Tarama sırasında hata oluştu: {ex.Message}";
                Services.AppLog.Error("Yinelenen dosya tarama hatası.", ex, nameof(SystemInfoViewModel));
            }
            finally
            {
                IsDuplicatesScanning = false;
                _duplicateScanCts?.Dispose();
                _duplicateScanCts = null;
            }
        }

        [RelayCommand]
        public async Task StartEmptyFolderScanAsync()
        {
            if (IsDuplicatesScanning) return;

            IsDuplicatesScanning = true;
            HasDuplicateOperationResult = false;
            DuplicateProgressPercent = 0;
            DuplicateProgressText = "Boş klasörler taranıyor...";
            _duplicateScanCts = new CancellationTokenSource();

            EmptyFolders.Clear();
            FilteredEmptyFolders.Clear();
            TotalEmptyFolderCount = 0;
            SelectedEmptyFolderCount = 0;

            try
            {
                string targetPath = IsCustomFolderActive && !string.IsNullOrWhiteSpace(CustomScanFolderPath)
                    ? CustomScanFolderPath
                    : (DuplicateSelectedDrive != "Tüm Sürücüler" ? DuplicateSelectedDrive : "C:\\");

                var progress = new Progress<string>(dir =>
                {
                    DuplicateProgressText = $"İnceleniyor: {dir}";
                    DuplicateCurrentFilePath = dir;
                });

                var results = await _duplicateService.ScanEmptyFoldersAsync(targetPath, progress, _duplicateScanCts.Token);

                EmptyFolders.Clear();
                foreach (var item in results)
                {
                    item.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(EmptyFolderItem.IsSelected))
                        {
                            UpdateEmptyFolderSelectionSummary();
                        }
                    };
                    EmptyFolders.Add(item);
                }

                HasEmptyFolderScanned = true;
                UpdateEmptyFolderFilters();
                UpdateEmptyFolderSelectionSummary();
                TotalEmptyFolderCount = EmptyFolders.Count;

                DuplicateProgressText = $"Tarama tamamlandı: {TotalEmptyFolderCount} adet sahipsiz boş klasör bulundu.";
                DuplicateProgressPercent = 100;
            }
            catch (OperationCanceledException)
            {
                DuplicateProgressText = "Boş klasör taraması iptal edildi.";
            }
            catch (Exception ex)
            {
                DuplicateProgressText = $"Hata: {ex.Message}";
                Services.AppLog.Error("Boş klasör tarama hatası.", ex, nameof(SystemInfoViewModel));
            }
            finally
            {
                IsDuplicatesScanning = false;
                _duplicateScanCts?.Dispose();
                _duplicateScanCts = null;
            }
        }

        [RelayCommand]
        public void CancelDuplicateScan()
        {
            if (_duplicateScanCts != null && !_duplicateScanCts.IsCancellationRequested)
            {
                _duplicateScanCts.Cancel();
                DuplicateProgressText = "İptal ediliyor...";
            }
        }

        [RelayCommand]
        public void SelectAllDuplicatesExceptOne()
        {
            foreach (var group in DuplicateGroups)
            {
                var sorted = group.Files.OrderBy(f => f.CreationTime).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    sorted[i].IsOriginal = (i == 0);
                    sorted[i].IsSelected = (i > 0);
                }
            }
            UpdateDuplicateSelectionSummary();
        }

        [RelayCommand]
        public void SelectNewestDuplicates()
        {
            foreach (var group in DuplicateGroups)
            {
                var sorted = group.Files.OrderBy(f => f.CreationTime).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    sorted[i].IsOriginal = (i == 0);
                    sorted[i].IsSelected = (i > 0);
                }
            }
            UpdateDuplicateSelectionSummary();
        }

        [RelayCommand]
        public void SelectOldestDuplicates()
        {
            foreach (var group in DuplicateGroups)
            {
                var sorted = group.Files.OrderByDescending(f => f.CreationTime).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    sorted[i].IsOriginal = (i == 0);
                    sorted[i].IsSelected = (i > 0);
                }
            }
            UpdateDuplicateSelectionSummary();
        }

        [RelayCommand]
        public void ClearDuplicateSelection()
        {
            foreach (var group in DuplicateGroups)
            {
                foreach (var file in group.Files)
                {
                    file.IsSelected = false;
                }
            }
            UpdateDuplicateSelectionSummary();
        }

        [RelayCommand]
        public void ToggleSelectAllEmptyFolders(object? parameter)
        {
            bool select = true;
            if (parameter is bool b)
            {
                select = b;
            }
            else if (parameter is string s && bool.TryParse(s, out bool parsed))
            {
                select = parsed;
            }

            foreach (var folder in FilteredEmptyFolders)
            {
                folder.IsSelected = select;
            }
            UpdateEmptyFolderSelectionSummary();
        }

        [RelayCommand]
        public async Task DeleteSelectedDuplicatesAsync()
        {
            // Her grupta en az bir kopya KALIR: kullanıcı grubun tamamını seçmişse asıl (ya da ilk)
            // dosya listeden çıkarılır. Eskiden tüm kopyalar silinip dosya tamamen kaybolabiliyordu.
            var selectedFiles = new List<DuplicateFileItem>();
            int keptGroups = 0;
            foreach (var group in DuplicateGroups)
            {
                var selected = group.Files.Where(f => f.IsSelected).ToList();
                if (selected.Count > 0 && selected.Count == group.Files.Count)
                {
                    var keep = group.Files.FirstOrDefault(f => f.IsOriginal) ?? group.Files[0];
                    selected.Remove(keep);
                    keep.IsSelected = false;
                    keptGroups++;
                }
                selectedFiles.AddRange(selected);
            }

            if (selectedFiles.Count == 0) return;

            if (DeletePermanently)
            {
                var confirm = System.Windows.MessageBox.Show(
                    $"{selectedFiles.Count} kopya kalıcı olarak silinecek (Geri Dönüşüm Kutusu atlanır). Her gruptan en az bir dosya korunur.\n\nDevam edilsin mi?",
                    "Kopyaları Kalıcı Sil", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (confirm != System.Windows.MessageBoxResult.Yes) return;
            }

            IsDuplicatesScanning = true;
            DuplicateProgressText = keptGroups > 0
                ? $"{selectedFiles.Count} dosya temizleniyor ({keptGroups} grubun tamamı seçiliydi; birer kopyası korunuyor)..."
                : $"{selectedFiles.Count} dosya temizleniyor...";

            try
            {
                bool moveToRecycleBin = !DeletePermanently;
                var (successCount, freedBytes) = await _duplicateService.DeleteDuplicatesAsync(selectedFiles, moveToRecycleBin);

                var groupsToRemove = new List<DuplicateFileGroup>();
                foreach (var group in DuplicateGroups)
                {
                    var filesToRemove = group.Files.Where(f => f.IsSelected && !File.Exists(f.FilePath)).ToList();
                    foreach (var f in filesToRemove)
                    {
                        group.Files.Remove(f);
                    }

                    if (group.Files.Count <= 1)
                    {
                        groupsToRemove.Add(group);
                    }
                    else
                    {
                        var first = group.Files.FirstOrDefault();
                        if (first != null) first.IsOriginal = true;
                    }
                }

                foreach (var g in groupsToRemove)
                {
                    DuplicateGroups.Remove(g);
                }

                RecalculateDuplicateTotals();
                UpdateDuplicateFilters();
                UpdateDuplicateSelectionSummary();

                string actionType = moveToRecycleBin ? "Geri Dönüşüm Kutusu'na taşındı" : "kalıcı olarak silindi";
                DuplicateOperationResult = $"{successCount} adet kopya dosya başarıyla {actionType} ({CleanCategory.FormatBytes(freedBytes)} disk alanı kazanıldı).";
                HasDuplicateOperationResult = true;
            }
            catch (Exception ex)
            {
                DuplicateOperationResult = $"Silme işlemi sırasında hata: {ex.Message}";
                HasDuplicateOperationResult = true;
                Services.AppLog.Error("Yinelenen dosya silme hatası.", ex, nameof(SystemInfoViewModel));
            }
            finally
            {
                IsDuplicatesScanning = false;
            }
        }

        [RelayCommand]
        public async Task DeleteSelectedEmptyFoldersAsync()
        {
            var selected = EmptyFolders.Where(f => f.IsSelected).ToList();
            if (selected.Count == 0) return;

            IsDuplicatesScanning = true;
            DuplicateProgressText = $"{selected.Count} boş klasör siliniyor...";

            try
            {
                int deleted = await _duplicateService.DeleteEmptyFoldersAsync(selected);

                var removed = selected.Where(f => !Directory.Exists(f.FolderPath)).ToList();
                foreach (var f in removed)
                {
                    EmptyFolders.Remove(f);
                }

                TotalEmptyFolderCount = EmptyFolders.Count;
                UpdateEmptyFolderFilters();
                UpdateEmptyFolderSelectionSummary();

                DuplicateOperationResult = $"{deleted} adet sahipsiz boş klasör başarıyla temizlendi.";
                HasDuplicateOperationResult = true;
            }
            catch (Exception ex)
            {
                DuplicateOperationResult = $"Boş klasör temizleme hatası: {ex.Message}";
                HasDuplicateOperationResult = true;
                Services.AppLog.Error("Boş klasör temizleme hatası.", ex, nameof(SystemInfoViewModel));
            }
            finally
            {
                IsDuplicatesScanning = false;
            }
        }

        private void RecalculateDuplicateTotals()
        {
            TotalDuplicateCount = DuplicateGroups.Sum(g => g.Files.Count);
            TotalWastedBytes = DuplicateGroups.Sum(g => g.TotalWastedBytes);
            TotalWastedFormatted = CleanCategory.FormatBytes(TotalWastedBytes);
        }

        private void UpdateDuplicateSelectionSummary()
        {
            int count = 0;
            long bytes = 0;
            foreach (var group in DuplicateGroups)
            {
                foreach (var file in group.Files)
                {
                    if (file.IsSelected)
                    {
                        count++;
                        bytes += file.SizeBytes;
                    }
                }
            }
            SelectedDuplicateCount = count;
            SelectedDuplicateBytesFormatted = CleanCategory.FormatBytes(bytes);
        }

        private void UpdateEmptyFolderSelectionSummary()
        {
            SelectedEmptyFolderCount = EmptyFolders.Count(f => f.IsSelected);
        }

        private void UpdateDuplicateFilters()
        {
            var query = DuplicateGroups.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(DuplicateSearchText))
            {
                string search = DuplicateSearchText.Trim();
                query = query.Where(g => g.Files.Any(f =>
                    f.FileName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    f.DirectoryPath.Contains(search, StringComparison.OrdinalIgnoreCase)));
            }

            if (DuplicateTypeFilter != "All")
            {
                string catName = GetCategoryNameForFilter(DuplicateTypeFilter);
                query = query.Where(g => g.Files.Any(f =>
                    string.Equals(f.Category, catName, StringComparison.OrdinalIgnoreCase)));
            }

            FilteredDuplicateGroups.Clear();
            foreach (var g in query)
            {
                FilteredDuplicateGroups.Add(g);
            }
        }

        private static string GetCategoryNameForFilter(string filter) => filter switch
        {
            "Images" => "Resim",
            "Videos" => "Video",
            "Documents" => "Belge",
            "Archives" => "Arşiv",
            "Audio" => "Ses",
            _ => "Tümü"
        };

        private void UpdateEmptyFolderFilters()
        {
            var query = EmptyFolders.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(EmptyFolderSearchText))
            {
                string search = EmptyFolderSearchText.Trim();
                query = query.Where(f =>
                    f.FolderName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    f.FolderPath.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            FilteredEmptyFolders.Clear();
            foreach (var f in query)
            {
                FilteredEmptyFolders.Add(f);
            }
        }

        [RelayCommand]
        public void OpenDuplicateFileLocation(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        [RelayCommand]
        public void OpenFolderLocation(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        [RelayCommand]
        public void BrowseCustomFolder()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Taranacak Klasörü Seçin",
                    Multiselect = false
                };
                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
                {
                    CustomScanFolderPath = dialog.FolderName;
                    IsCustomFolderActive = true;
                }
            }
            catch
            {
                // Savunmacı UI
            }
        }

        [RelayCommand]
        public void ClearCustomFolder()
        {
            CustomScanFolderPath = string.Empty;
            IsCustomFolderActive = false;
        }

        #endregion

        #region Disk Haritası (§5.3)

        public ObservableCollection<TreemapTile> Tiles { get; } = new();
        public ObservableCollection<Bakım.Core.Storage.FolderNode> Breadcrumb { get; } = new();

        [ObservableProperty] private string _diskMapTarget = string.Empty;
        [ObservableProperty] private bool _isDiskMapScanning;
        [ObservableProperty] private string _diskMapStatus = "Bir sürücü ya da klasör seçip haritayı çıkarın.";
        [ObservableProperty] private bool _hasDiskMap;
        [ObservableProperty] private string _currentFolderSummary = string.Empty;

        private Bakım.Core.Storage.FolderNode? _currentNode;
        private CancellationTokenSource? _diskMapCts;
        private double _mapWidth, _mapHeight;

        /// <summary>Harita için seçilebilir hedefler: sabit sürücüler.</summary>
        public IEnumerable<string> DiskMapTargets => AvailableDrives.Where(d => d != "Tüm Sürücüler");

        [RelayCommand]
        private async Task ScanDiskMapAsync()
        {
            if (IsDiskMapScanning) return;
            string target = string.IsNullOrWhiteSpace(DiskMapTarget) ? (DiskMapTargets.FirstOrDefault() ?? "C:\\") : DiskMapTarget;
            DiskMapTarget = target;

            IsDiskMapScanning = true;
            _diskMapCts = new CancellationTokenSource();
            var started = DateTime.UtcNow;
            try
            {
                var progress = new Progress<DiskMapProgress>(p =>
                    DiskMapStatus = $"{p.FilesScanned:N0} dosya · {Core.Text.ByteFormatter.Format(p.BytesCounted)} · {p.CurrentFolder}");
                var root = await _diskMap.ScanAsync(target, progress, _diskMapCts.Token);
                Breadcrumb.Clear();
                ShowNode(root);
                HasDiskMap = true;
                DiskMapStatus = $"{Core.Text.ByteFormatter.Format(root.SizeBytes)} · {root.FileCount:N0} dosya · {(DateTime.UtcNow - started).TotalSeconds:F0} sn. " +
                                "Bağlantı noktaları ve yalnızca çevrimiçi OneDrive dosyaları sayılmadı.";
            }
            catch (OperationCanceledException)
            {
                DiskMapStatus = "Tarama iptal edildi.";
            }
            catch (Exception ex)
            {
                AppLog.Error("Disk haritası çıkarılamadı.", ex, nameof(StorageViewModel));
                DiskMapStatus = $"Harita çıkarılamadı: {ex.Message}";
            }
            finally
            {
                IsDiskMapScanning = false;
                _diskMapCts?.Dispose();
                _diskMapCts = null;
            }
        }

        [RelayCommand]
        private void CancelDiskMap() => _diskMapCts?.Cancel();

        /// <summary>Görünüm, harita alanının boyutu değişince çağırır.</summary>
        public void SetMapViewport(double width, double height)
        {
            _mapWidth = width;
            _mapHeight = height;
            LayoutTiles();
        }

        private void ShowNode(Bakım.Core.Storage.FolderNode node)
        {
            _currentNode = node;
            Breadcrumb.Clear();
            for (var n = node; n != null; n = n.Parent) Breadcrumb.Insert(0, n);
            CurrentFolderSummary = $"{node.FullPath} · {Core.Text.ByteFormatter.Format(node.SizeBytes)} · {node.FileCount:N0} dosya";
            LayoutTiles();
        }

        private void LayoutTiles()
        {
            Tiles.Clear();
            if (_currentNode == null || _mapWidth < 20 || _mapHeight < 20) return;

            var children = _currentNode.Children.Where(c => c.SizeBytes > 0).OrderByDescending(c => c.SizeBytes).ToList();
            var rects = Bakım.Core.Storage.Treemap.Layout(children.Select(c => (double)c.SizeBytes).ToList(),
                new Bakım.Core.Storage.TreemapRect(0, 0, _mapWidth, _mapHeight));
            for (int i = 0; i < children.Count; i++)
            {
                var r = rects[i];
                if (r.Width < 1 || r.Height < 1) continue;
                double percent = _currentNode.SizeBytes > 0 ? 100.0 * children[i].SizeBytes / _currentNode.SizeBytes : 0;
                Tiles.Add(new TreemapTile(children[i], r.X, r.Y, r.Width, r.Height, i % 6, Core.Text.ByteFormatter.Format(children[i].SizeBytes), percent));
            }
        }

        [RelayCommand]
        private void OpenTile(TreemapTile? tile)
        {
            if (tile == null || tile.Node.IsAggregate || tile.Node.Children.Count == 0) return;
            ShowNode(tile.Node);
        }

        [RelayCommand]
        private void NavigateCrumb(Bakım.Core.Storage.FolderNode? node)
        {
            if (node != null) ShowNode(node);
        }

        [RelayCommand]
        private void DiskMapUp()
        {
            if (_currentNode?.Parent != null) ShowNode(_currentNode.Parent);
        }

        [RelayCommand]
        private void OpenTileInExplorer(TreemapTile? tile)
        {
            if (tile == null) return;
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{tile.Node.FullPath}\"") { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                AppLog.Warning("Klasör açılamadı.", ex, nameof(StorageViewModel));
            }
        }

        /// <summary>Klasörü Geri Dönüşüm Kutusu'na taşır (korunan konumlar PathSafetyGuard ile reddedilir).</summary>
        [RelayCommand]
        private async Task RecycleTileAsync(TreemapTile? tile)
        {
            if (tile == null || tile.Node.IsAggregate || tile.Node.Parent == null) return;
            var confirm = System.Windows.MessageBox.Show(
                $"Bu klasör Geri Dönüşüm Kutusu'na taşınsın mı?\n\n{tile.Node.FullPath}\n{tile.SizeText} · {tile.Node.FileCount:N0} dosya\n\n" +
                "Windows ve program klasörleri gibi korunan konumlar taşınmaz.",
                "Geri Dönüşüm Kutusu'na taşı", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.No);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;

            var result = await _safeDelete.DeletePathAsync(tile.Node.FullPath, isDirectory: true, new Services.Safety.DeletePolicy());
            var outcome = result.Succeeded ? Core.Activity.ActivityOutcome.Succeeded : Core.Activity.ActivityOutcome.Failed;
            Services.Activity.ActivityRecording.RecordRecycled(_activity, Core.Activity.ActivityKind.Clean, "Depolama",
                $"\"{tile.Node.Name}\" Geri Dönüşüm Kutusu'na taşındı", $"{tile.SizeText} · {result.Message}", outcome,
                new[] { new Core.Activity.ActivityItem(tile.Node.FullPath, "Geri Dönüşüm Kutusu", result.Outcome.ToString(), result.Message) },
                deepLink: "Storage");

            if (result.Succeeded && _currentNode != null)
            {
                _currentNode.Children.Remove(tile.Node);
                _currentNode.SizeBytes -= tile.Node.SizeBytes;
                ShowNode(_currentNode);
                DiskMapStatus = $"\"{tile.Node.Name}\" Geri Dönüşüm Kutusu'na taşındı.";
            }
            else
            {
                DiskMapStatus = $"Taşınamadı: {result.Message}";
            }
        }

        #endregion

        #region Modül Yaşam Döngüsü

        public Task OnActivatedAsync() => Task.CompletedTask;

        /// <summary>Tarama arka planda sürebilir; sayfadan çıkınca iptal edilmez.</summary>
        public Task OnDeactivatedAsync() => Task.CompletedTask;

        #endregion
    }
}

namespace Bakım.ViewModels
{
    /// <summary>Disk haritasındaki bir dikdörtgen.</summary>
    public sealed class TreemapTile
    {
        public TreemapTile(Bakım.Core.Storage.FolderNode node, double x, double y, double width, double height,
            int colorIndex, string sizeText, double percent)
        {
            Node = node;
            X = x;
            Y = y;
            Width = Math.Max(0, width - 2);   // karolar arasında 2 px boşluk
            Height = Math.Max(0, height - 2);
            ColorIndex = colorIndex;
            SizeText = sizeText;
            Percent = percent;
        }

        public Bakım.Core.Storage.FolderNode Node { get; }
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }
        public int ColorIndex { get; }
        public string SizeText { get; }
        public double Percent { get; }
        public string Name => Node.Name;
        public bool ShowLabel => Width >= 70 && Height >= 36;
        public bool CanDrill => !Node.IsAggregate && Node.Children.Count > 0;
        public string Tooltip => $"{Node.FullPath}\n{SizeText} · %{Percent:F1} · {Node.FileCount:N0} dosya" +
                                 (Node.IsAggregate ? "\n(Küçük öğeler birleştirildi)" : CanDrill ? "\nİçine girmek için tıklayın" : "");
    }
}
