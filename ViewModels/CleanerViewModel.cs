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

        public CleanerViewModel(ISystemCleanService cleanService)
        {
            _cleanService = cleanService;
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
        // Alias used in animated scan status bar XAML binding
        public string TotalFoundBytesFormatted => CleanCategory.FormatBytes(TotalFoundBytes);

        partial void OnTotalFoundBytesChanged(long value)
        {
            OnPropertyChanged(nameof(TotalFoundBytesFormatted));
            OnPropertyChanged(nameof(FormattedFoundBytes));
        }

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
            StatusText = "Sistem teşhis ve önbellek taraması başlatılıyor...";
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

                    // Teşhis görsel kadansı (her kategori için hissedilir başlangıç)
                    await Task.Delay(90, _cts.Token);

                    var (items, bytes) = await _cleanService.ScanCategoryAsync(cat, scanProgress, _cts.Token);
                    cat.TotalBytes = bytes;
                    cat.FileCount = items.Count;

                    if (items.Count > 0)
                    {
                        // Öğeleri mikro-batch halinde (15-35'lik paketler) akıcı şekilde ekle
                        int batchSize = Math.Max(15, items.Count / 8);
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

                            // Akıcı gözlem gecikmesi
                            await Task.Delay(20, _cts.Token);
                        }
                    }
                    else
                    {
                        await Task.Delay(70, _cts.Token);
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
