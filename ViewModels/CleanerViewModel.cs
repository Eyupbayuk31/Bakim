using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Models;
using Bakım.Core.Activity;
using ActivityKind = Bakım.Core.Activity.ActivityKind;
using Bakım.Services;
using Bakım.Services.Activity;

namespace Bakım.ViewModels
{
    public partial class CleanerViewModel : ObservableObject
    {
        private readonly ISystemCleanService _cleanService;
        private readonly IActivityService _activity;
        private CancellationTokenSource? _cts;

        public CleanerViewModel(ISystemCleanService cleanService, IActivityService activity)
        {
            _cleanService = cleanService;
            _activity = activity;
            Categories = new ObservableCollection<CleanCategory>(_cleanService.GetDefaultCategories());
            ScannedFiles = new ObservableCollection<CleanFileItem>();

            FilteredCategoriesView = CollectionViewSource.GetDefaultView(Categories);
            FilteredCategoriesView.Filter = FilterCategoryPredicate;

            FilteredFilesView = CollectionViewSource.GetDefaultView(ScannedFiles);
            FilteredFilesView.Filter = FilterFilePredicate;

            RefreshDriveInfo();
        }

        public ObservableCollection<CleanCategory> Categories { get; }
        public ObservableCollection<CleanFileItem> ScannedFiles { get; }
        public ICollectionView FilteredCategoriesView { get; }
        public ICollectionView FilteredFilesView { get; }

        #region Drive & Health Metrics

        [ObservableProperty]
        private DriveInfoItem _systemDrive = new();

        public void RefreshDriveInfo()
        {
            try
            {
                SystemDrive = _cleanService.GetSystemDriveInfo();
            }
            catch { }
        }

        #endregion

        #region Operational State Properties

        [ObservableProperty]
        private bool _isScanning;

        [ObservableProperty]
        private bool _isCleaning;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusText = "Geçici dosyaları ve önbellekleri temizlemek için 'Analiz Et' butonuna tıklayın.";

        [ObservableProperty]
        private string _currentScanningFile = string.Empty;

        [ObservableProperty]
        private int _progressPercent;

        [ObservableProperty]
        private long _totalFoundBytes;

        [ObservableProperty]
        private long _totalFreedBytes;

        [ObservableProperty]
        private int _totalFilesFound;

        [ObservableProperty]
        private int _totalFilesCleaned;

        [ObservableProperty]
        private string _operationResultBanner = string.Empty;

        [ObservableProperty]
        private bool _hasResultBanner;

        public string FormattedFoundBytes => CleanCategory.FormatBytes(TotalFoundBytes);
        public string FormattedFreedBytes => CleanCategory.FormatBytes(TotalFreedBytes);
        public string TotalFoundBytesFormatted => CleanCategory.FormatBytes(TotalFoundBytes);

        partial void OnTotalFoundBytesChanged(long value)
        {
            OnPropertyChanged(nameof(TotalFoundBytesFormatted));
            OnPropertyChanged(nameof(FormattedFoundBytes));
        }

        #endregion

        #region Presets & Filter Chips

        [ObservableProperty]
        private string _activePreset = "quick";

        [ObservableProperty]
        private string _categoryGroupFilter = "Tümü";

        partial void OnCategoryGroupFilterChanged(string value)
        {
            FilteredCategoriesView.Refresh();
        }

        private bool FilterCategoryPredicate(object obj)
        {
            if (obj is not CleanCategory cat) return false;
            if (string.IsNullOrEmpty(CategoryGroupFilter) || CategoryGroupFilter == "Tümü")
                return true;

            return string.Equals(cat.GroupName, CategoryGroupFilter, StringComparison.OrdinalIgnoreCase);
        }

        [RelayCommand]
        public void ApplyPreset(string preset)
        {
            ActivePreset = preset;
            switch (preset?.ToLowerInvariant())
            {
                case "quick":
                    foreach (var c in Categories)
                    {
                        c.IsSelected = !c.IsDeepClean;
                    }
                    StatusText = "Hızlı & Güvenli profil uygulandı (Risksiz temp ve önbellekler seçildi).";
                    break;
                case "deep":
                    foreach (var c in Categories)
                    {
                        c.IsSelected = true;
                    }
                    StatusText = "Kapsamlı Derin Temizlik profili uygulandı (Tüm 20 hedef seçildi).";
                    break;
                case "browsers":
                    foreach (var c in Categories)
                    {
                        c.IsSelected = c.GroupName == "Web Tarayıcıları";
                    }
                    StatusText = "Web Tarayıcıları profili uygulandı.";
                    break;
                case "gaming":
                    foreach (var c in Categories)
                    {
                        c.IsSelected = c.GroupName == "Oyunlar & Medya" || c.Id == "directx_shader" || c.Id == "user_temp";
                    }
                    StatusText = "Oyunlar & Medya profili uygulandı (Steam, Spotify, Epic, Shader seçildi).";
                    break;
                case "all":
                    foreach (var c in Categories) c.IsSelected = true;
                    StatusText = "Tüm hedefler seçildi.";
                    break;
                case "none":
                    foreach (var c in Categories) c.IsSelected = false;
                    StatusText = "Tüm seçimler kaldırıldı.";
                    break;
            }
        }

        [RelayCommand]
        public void SetCategoryGroup(string group)
        {
            CategoryGroupFilter = group;
        }

        [RelayCommand]
        public void SelectAllCategories()
        {
            ApplyPreset("all");
        }

        [RelayCommand]
        public void DeselectAllCategories()
        {
            ApplyPreset("none");
        }

        #endregion

        #region Scanned Files Search & Filters

        [ObservableProperty]
        private string _searchFileQuery = string.Empty;

        [ObservableProperty]
        private string _sizeFilter = "All";

        partial void OnSearchFileQueryChanged(string value)
        {
            FilteredFilesView.Refresh();
        }

        partial void OnSizeFilterChanged(string value)
        {
            FilteredFilesView.Refresh();
        }

        [RelayCommand]
        public void SetSizeFilter(string filter)
        {
            SizeFilter = filter;
        }

        private bool FilterFilePredicate(object obj)
        {
            if (obj is not CleanFileItem item) return false;

            // Search query
            if (!string.IsNullOrWhiteSpace(SearchFileQuery))
            {
                string q = SearchFileQuery.Trim();
                bool match = item.FileName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                             item.FilePath.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                             item.CategoryName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                             item.Extension.Contains(q, StringComparison.OrdinalIgnoreCase);

                if (!match) return false;
            }

            // Size filter
            switch (SizeFilter)
            {
                case "1MB":
                    if (item.SizeBytes < 1024 * 1024) return false;
                    break;
                case "10MB":
                    if (item.SizeBytes < 10 * 1024 * 1024) return false;
                    break;
                case "100MB":
                    if (item.SizeBytes < 100 * 1024 * 1024) return false;
                    break;
            }

            return true;
        }

        #endregion

        #region Master-Detail Drawer & Actions

        [ObservableProperty]
        private CleanFileItem? _selectedFile;

        [ObservableProperty]
        private bool _isDrawerOpen;

        partial void OnSelectedFileChanged(CleanFileItem? value)
        {
            IsDrawerOpen = value != null;
        }

        [RelayCommand]
        public void CloseDrawer()
        {
            IsDrawerOpen = false;
            SelectedFile = null;
        }

        [RelayCommand]
        public void OpenFileLocation(CleanFileItem? item)
        {
            var target = item ?? SelectedFile;
            if (target == null || string.IsNullOrWhiteSpace(target.FilePath)) return;

            try
            {
                if (File.Exists(target.FilePath))
                {
                    Process.Start("explorer.exe", $"/select,\"{target.FilePath}\"");
                }
                else if (Directory.Exists(target.DirectoryPath))
                {
                    Process.Start("explorer.exe", $"\"{target.DirectoryPath}\"");
                }
            }
            catch { }
        }

        [RelayCommand]
        public void CopyFilePath(CleanFileItem? item)
        {
            var target = item ?? SelectedFile;
            if (target == null || string.IsNullOrWhiteSpace(target.FilePath)) return;

            try
            {
                Clipboard.SetText(target.FilePath);
                StatusText = $"Kopyalandı: {target.FileName}";
            }
            catch { }
        }

        [RelayCommand]
        public void ExcludeFile(CleanFileItem? item)
        {
            var target = item ?? SelectedFile;
            if (target == null) return;

            target.IsExcluded = !target.IsExcluded;
            if (target.IsExcluded)
            {
                target.Status = "Muaf Tutuldu (Atlandı)";
                TotalFoundBytes = Math.Max(0, TotalFoundBytes - target.SizeBytes);
            }
            else
            {
                target.Status = "Bulundu";
                TotalFoundBytes += target.SizeBytes;
            }

            OnPropertyChanged(nameof(FormattedFoundBytes));
            OnPropertyChanged(nameof(TotalFoundBytesFormatted));
            FilteredFilesView.Refresh();
        }

        #endregion

        #region Scanning & Cleaning Workflow

        [RelayCommand]
        public void Cancel()
        {
            if (IsBusy && _cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                StatusText = "İşlem iptal ediliyor...";
            }
        }

        [RelayCommand]
        public async Task ScanAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            IsScanning = true;
            HasResultBanner = false;
            StatusText = "Sistem teşhis ve önbellek taraması başlatılıyor...";
            ProgressPercent = 0;
            TotalFoundBytes = 0;
            TotalFilesFound = 0;
            ScannedFiles.Clear();
            SelectedFile = null;
            IsDrawerOpen = false;

            _cts = new CancellationTokenSource();

            var selectedCategories = Categories.Where(c => c.IsSelected).ToList();
            if (selectedCategories.Count == 0)
            {
                StatusText = "Lütfen taranacak en az bir kategori seçin.";
                IsBusy = false;
                IsScanning = false;
                return;
            }

            try
            {
                // Kategori durumlarını sıfırla
                foreach (var c in Categories)
                {
                    c.IsScanning = false;
                    if (c.IsSelected)
                    {
                        c.TotalBytes = 0;
                        c.FileCount = 0;
                    }
                }

                int totalCats = selectedCategories.Count;
                int currentCatIndex = 0;
                int runningFileCount = 0;
                long runningBytes = 0;

                var scanProgress = new Progress<string>(file =>
                {
                    CurrentScanningFile = file;
                });

                foreach (var cat in selectedCategories)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    cat.IsScanning = true;
                    StatusText = $"Taranıyor: {cat.Name}...";

                    var (items, bytes) = await _cleanService.ScanCategoryAsync(cat, scanProgress, _cts.Token);
                    cat.TotalBytes = bytes;
                    cat.FileCount = items.Count;

                    if (items.Count > 0)
                    {
                        int batchSize = Math.Max(20, items.Count / 8);
                        for (int i = 0; i < items.Count; i += batchSize)
                        {
                            _cts.Token.ThrowIfCancellationRequested();
                            int take = Math.Min(batchSize, items.Count - i);
                            for (int j = 0; j < take; j++)
                            {
                                var itm = items[i + j];
                                ScannedFiles.Add(itm);
                                runningBytes += itm.SizeBytes;
                            }

                            runningFileCount += take;
                            TotalFilesFound = runningFileCount;
                            TotalFoundBytes = runningBytes;
                            OnPropertyChanged(nameof(FormattedFoundBytes));

                            CurrentScanningFile = items[Math.Min(i + take - 1, items.Count - 1)].FilePath;

                            double catProgress = (double)(i + take) / items.Count;
                            ProgressPercent = Math.Min(98, (int)(((currentCatIndex + catProgress) / totalCats) * 100));

                            // Arayüzün çizim yapabilmesi için yalnızca sıra ver; yapay bekleme yok (D-12).
                            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }

                    cat.IsScanning = false;
                    currentCatIndex++;
                    ProgressPercent = (int)(((double)currentCatIndex / totalCats) * 100);
                }

                TotalFoundBytes = runningBytes;
                TotalFilesFound = runningFileCount;
                OnPropertyChanged(nameof(FormattedFoundBytes));

                ProgressPercent = 100;
                StatusText = $"Tarama tamamlandı! Toplam {TotalFilesFound} dosya ({FormattedFoundBytes}) temizlenebilir.";
                CurrentScanningFile = string.Empty;
                RefreshDriveInfo();
            }
            catch (OperationCanceledException)
            {
                StatusText = "Tarama işlemi iptal edildi.";
                foreach (var cat in Categories) cat.IsScanning = false;
            }
            catch (Exception ex)
            {
                StatusText = $"Tarama hatası: {ex.Message}";
                foreach (var cat in Categories) cat.IsScanning = false;
            }
            finally
            {
                IsScanning = false;
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task CleanAsync()
        {
            if (IsBusy) return;

            var itemsToClean = ScannedFiles.Where(f => !f.IsDeleted && !f.IsExcluded).ToList();
            if (itemsToClean.Count == 0)
            {
                StatusText = "Temizlenecek öğe bulunamadı. Önce tarama yapın veya muafiyetleri kaldırın.";
                return;
            }

            IsBusy = true;
            IsCleaning = true;
            HasResultBanner = false;
            StatusText = "Geçici dosyalar güvenle temizleniyor...";
            ProgressPercent = 0;

            _cts = new CancellationTokenSource();

            try
            {
                var progress = new Progress<(string file, int percent)>(report =>
                {
                    CurrentScanningFile = report.file;
                    ProgressPercent = report.percent;
                });

                var result = await _cleanService.CleanItemsAsync(itemsToClean, progress, _cts.Token);

                TotalFreedBytes += result.TotalBytesFreed;
                TotalFilesCleaned += result.TotalFilesDeleted;
                OnPropertyChanged(nameof(FormattedFreedBytes));

                foreach (var cat in Categories.Where(c => c.IsSelected))
                {
                    cat.TotalBytes = 0;
                    cat.FileCount = 0;
                }

                RecordCleanActivity(itemsToClean, result, cancelled: false);

                HasResultBanner = true;
                OperationResultBanner = $"Başarıyla {result.TotalFilesDeleted} dosya silindi ve {result.FormattedBytesFreed} alan kazanıldı! ({result.TotalFilesSkipped} dosya kilitli/korumalı olduğu için güvenle atlandı)";
                StatusText = "Temizlik operasyonu tamamlandı.";
                CurrentScanningFile = string.Empty;
                RefreshDriveInfo();
            }
            catch (OperationCanceledException)
            {
                StatusText = "Temizlik işlemi iptal edildi.";
                int deleted = itemsToClean.Count(i => i.IsDeleted);
                if (deleted > 0)
                {
                    RecordCleanActivity(itemsToClean, new CleanResult
                    {
                        TotalFilesDeleted = deleted,
                        TotalBytesFreed = itemsToClean.Where(i => i.IsDeleted).Sum(i => i.SizeBytes)
                    }, cancelled: true);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Temizlik hatası: {ex.Message}";
            }
            finally
            {
                IsCleaning = false;
                IsBusy = false;
            }
        }

        /// <summary>Temizliği Etkinlik Merkezi'ne yazar: kategori başına dosya sayısı ve boyut.</summary>
        private void RecordCleanActivity(IReadOnlyList<CleanFileItem> requested, CleanResult result, bool cancelled)
        {
            var byCategory = requested
                .GroupBy(i => string.IsNullOrEmpty(i.CategoryName) ? "Diğer" : i.CategoryName)
                .Select(g =>
                {
                    int deleted = g.Count(i => i.IsDeleted);
                    long bytes = g.Where(i => i.IsDeleted).Sum(i => i.SizeBytes);
                    int skipped = g.Count() - deleted;
                    return new ActivityItem(g.Key, "Temizle",
                        $"{deleted} dosya · {CleanCategory.FormatBytes(bytes)}",
                        skipped > 0 ? $"{skipped} dosya kilitli/korumalı olduğu için atlandı" : null);
                })
                .ToList();

            var categories = byCategory.Select(c => c.Target).ToList();
            string title = categories.Count switch
            {
                0 => "Temizlik yapıldı",
                1 => $"{categories[0]} temizlendi",
                _ => $"{categories.Count} kategori temizlendi"
            };
            // Kilitli dosyaları atlamak olağandır; hiçbir dosya silinemediyse başarısızdır.
            var outcome = cancelled ? ActivityOutcome.Cancelled
                : result.TotalFilesDeleted == 0 && result.TotalFilesSkipped > 0 ? ActivityOutcome.Failed
                : ActivityOutcome.Succeeded;

            _activity.RecordSimple(ActivityKind.Clean, "Temizleyici", title,
                $"{result.TotalFilesDeleted:N0} dosya · {result.FormattedBytesFreed}" +
                (result.TotalFilesSkipped > 0 ? $" · {result.TotalFilesSkipped:N0} atlandı" : ""),
                outcome, byCategory, deepLink: "Cleaner");
        }

        #endregion
    }
}
