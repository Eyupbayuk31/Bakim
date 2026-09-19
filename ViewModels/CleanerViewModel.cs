using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    public partial class CleanerViewModel : ObservableObject
    {
        private readonly ISystemCleanService _cleanService;
        private CancellationTokenSource? _cts;

        public CleanerViewModel() : this(null)
        {
        }

        public CleanerViewModel(ISystemCleanService? cleanService)
        {
            _cleanService = cleanService ?? new SystemCleanService();
            Categories = new ObservableCollection<CleanCategory>(_cleanService.GetDefaultCategories());
            ScannedFiles = new ObservableCollection<CleanFileItem>();
        }

        public ObservableCollection<CleanCategory> Categories { get; }
        public ObservableCollection<CleanFileItem> ScannedFiles { get; }

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

        [RelayCommand]
        public void SelectAllCategories()
        {
            foreach (var cat in Categories) cat.IsSelected = true;
        }

        [RelayCommand]
        public void DeselectAllCategories()
        {
            foreach (var cat in Categories) cat.IsSelected = false;
        }

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
            StatusText = "Sistem önbellekleri ve geçici dosyalar taranıyor...";
            ProgressPercent = 0;
            TotalFoundBytes = 0;
            TotalFilesFound = 0;
            ScannedFiles.Clear();

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
                var progress = new Progress<string>(file =>
                {
                    CurrentScanningFile = file;
                });

                int totalCount = 0;
                long totalBytes = 0;

                foreach (var cat in selectedCategories)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    cat.IsScanning = true;
                    var (items, bytes) = await _cleanService.ScanCategoryAsync(cat, progress, _cts.Token);
                    cat.TotalBytes = bytes;
                    cat.FileCount = items.Count;
                    cat.IsScanning = false;

                    totalBytes += bytes;
                    totalCount += items.Count;

                    foreach (var itm in items)
                    {
                        ScannedFiles.Add(itm);
                    }
                }

                TotalFoundBytes = totalBytes;
                TotalFilesFound = totalCount;
                OnPropertyChanged(nameof(FormattedFoundBytes));

                StatusText = $"Tarama tamamlandı! Toplam {TotalFilesFound} dosya ({FormattedFoundBytes}) temizlenebilir.";
                CurrentScanningFile = string.Empty;
                ProgressPercent = 100;
            }
            catch (OperationCanceledException)
            {
                StatusText = "Tarama işlemi iptal edildi.";
                foreach (var cat in selectedCategories) cat.IsScanning = false;
            }
            catch (Exception ex)
            {
                StatusText = $"Tarama hatası: {ex.Message}";
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

            var itemsToClean = ScannedFiles.Where(f => !f.IsDeleted).ToList();
            if (itemsToClean.Count == 0)
            {
                StatusText = "Temizlenecek öğe bulunamadı. Önce tarama yapın.";
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

                HasResultBanner = true;
                OperationResultBanner = $"Başarıyla {result.TotalFilesDeleted} dosya silindi ve {result.FormattedBytesFreed} alan kazanıldı! ({result.TotalFilesSkipped} dosya kilitli/korumalı olduğu için atlandı)";
                StatusText = "Temizlik operasyonu tamamlandı.";
                CurrentScanningFile = string.Empty;
            }
            catch (OperationCanceledException)
            {
                StatusText = "Temizlik işlemi iptal edildi.";
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
    }
}
