using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Helpers;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    public partial class SystemInfoViewModel : ObservableObject, IModuleViewModel
    {
        private readonly IAppSettingsService _settingsService;
        private readonly ISystemInfoService _infoService;
        private readonly DispatcherTimer _liveTelemetryTimer;
        private CancellationTokenSource? _scanCts;

        public SystemInfoViewModel(ISystemInfoService infoService,
            IAppSettingsService settingsService)
        {
            _settingsService = settingsService;
            _infoService = infoService;
            Drives = new ObservableCollection<DriveInfoItem>();
            SmartDisks = new ObservableCollection<SmartDiskHealthItem>();
            LargeFiles = new ObservableCollection<LargeDiskFileItem>();
            FilteredLargeFiles = new ObservableCollection<LargeDiskFileItem>();
            AvailableDrives = new ObservableCollection<string> { "Tüm Sürücüler" };

            // Sabit sürücü harflerini ekle
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.IsReady && d.DriveType == DriveType.Fixed)
                    {
                        AvailableDrives.Add(d.Name);
                    }
                }
            }
            catch { }

            _ = RefreshAsync();
            _ = LoadSmartHealthAsync();

            // Gerçek Zamanlı Donanım Telemetrisi (CPU, RAM vb. 3 saniyede bir güncellenir)
            _liveTelemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _liveTelemetryTimer.Tick += async (_, _) =>
            {
                if (!IsBusy && ActiveSubTab == "Hardware")
                {
                    try
                    {
                        var hw = await _infoService.GetSystemHardwareAsync();
                        Hardware = hw;
                    }
                    catch
                    {
                        // Savunmacı arka plan yenileme
                    }
                }
            };
            // Zamanlayıcı OnActivatedAsync() içinde başlar — bkz. IModuleViewModel
        }

        public ObservableCollection<DriveInfoItem> Drives { get; }
        public ObservableCollection<SmartDiskHealthItem> SmartDisks { get; }
        public ObservableCollection<LargeDiskFileItem> LargeFiles { get; }
        public ObservableCollection<LargeDiskFileItem> FilteredLargeFiles { get; }
        public ObservableCollection<string> AvailableDrives { get; }

        [ObservableProperty]
        private SystemHardwareStats _hardware = new();

        [ObservableProperty]
        private string _activeSubTab = "Hardware"; // Hardware, Smart, LargeFiles

        public bool IsHardwareTab => ActiveSubTab == "Hardware";
        public bool IsSmartTab => ActiveSubTab == "Smart";
        public bool IsLargeFilesTab => ActiveSubTab == "LargeFiles";

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _isScanning;

        [ObservableProperty]
        private string _statusText = "Sistem donanımı ve sürücü bilgileri hazır.";

        [ObservableProperty]
        private string _scanProgressText = "Büyük dosya taraması başlatılmadı.";

        [ObservableProperty]
        private string _selectedDriveFilter = "Tüm Sürücüler";

        [ObservableProperty]
        private long _minFileSizeThresholdMb = 1024; // 1 GB varsayılan

        [ObservableProperty]
        private string _selectedCategoryFilter = "Tümü";

        public bool IsThreshold500Mb => MinFileSizeThresholdMb == 500;
        public bool IsThreshold1Gb => MinFileSizeThresholdMb == 1024;
        public bool IsThreshold2Gb => MinFileSizeThresholdMb == 2048;
        public bool IsThreshold5Gb => MinFileSizeThresholdMb == 5120;

        public bool IsCategoryAll => SelectedCategoryFilter == "Tümü";
        public bool IsCategoryVideo => SelectedCategoryFilter == "Video";
        public bool IsCategoryDiskImage => SelectedCategoryFilter == "Disk İmajı";
        public bool IsCategoryArchive => SelectedCategoryFilter == "Arşiv";
        public bool IsCategoryInstaller => SelectedCategoryFilter == "Kurulum / Oyun";

        partial void OnMinFileSizeThresholdMbChanged(long value)
        {
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
        }

        [ObservableProperty]
        private string _totalLargeFilesSizeFormatted = "0 Dosya (0 GB)";

        partial void OnActiveSubTabChanged(string value)
        {
            OnPropertyChanged(nameof(IsHardwareTab));
            OnPropertyChanged(nameof(IsSmartTab));
            OnPropertyChanged(nameof(IsLargeFilesTab));
        }

        [RelayCommand]
        public void SwitchSubTab(string tab)
        {
            ActiveSubTab = tab;
            if (tab == "Smart" && SmartDisks.Count == 0)
            {
                _ = LoadSmartHealthAsync();
            }
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusText = "Sistem donanımı ve sürücüleri taranıyor...";

            try
            {
                Hardware = await _infoService.GetSystemHardwareAsync();
                Drives.Clear();
                foreach (var d in Hardware.Drives)
                {
                    Drives.Add(d);
                }
                StatusText = "Sistem raporu güncellendi.";
            }
            catch (Exception ex)
            {
                StatusText = $"Hata: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task LoadSmartHealthAsync()
        {
            try
            {
                var disks = await _infoService.GetDiskSmartHealthAsync();
                SmartDisks.Clear();
                foreach (var disk in disks)
                {
                    SmartDisks.Add(disk);
                }
            }
            catch { }
        }

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
                    LargeFiles.Add(f);
                }

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
                    ScanProgressText = $"'{file.FileName}' silinemedi (kullanımda veya erişim engellendi).";
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
                    ScanProgressText = $"'{file.FileName}' Geri Dönüşüm Kutusuna taşınamadı (kullanımda olabilir).";
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
            UpdateLargeFilesFilter();
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
            FilteredLargeFiles.Clear();
            var matches = SelectedCategoryFilter == "Tümü"
                ? LargeFiles
                : LargeFiles.Where(f => f.Category.Equals(SelectedCategoryFilter, StringComparison.OrdinalIgnoreCase) ||
                                       f.Category.Contains(SelectedCategoryFilter, StringComparison.OrdinalIgnoreCase));

            long totalBytes = 0;
            int count = 0;
            foreach (var file in matches)
            {
                FilteredLargeFiles.Add(file);
                totalBytes += file.SizeBytes;
                count++;
            }

            double gb = totalBytes / (1024.0 * 1024.0 * 1024.0);
            TotalLargeFilesSizeFormatted = count > 0 
                ? $"{count} Dosya ({gb:F1} GB Toplam Alan)" 
                : "0 Dosya (0 GB)";
        }

        [RelayCommand]
        public void CleanDrive(string? driveName)
        {
            try
            {
                string drive = string.IsNullOrWhiteSpace(driveName) ? "C:" : driveName.TrimEnd('\\');
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cleanmgr.exe",
                    Arguments = $"/d {drive}",
                    UseShellExecute = true
                });
                StatusText = $"{drive} için Windows Disk Temizleme aracı açıldı.";
            }
            catch (Exception ex)
            {
                StatusText = $"Disk temizleme açılamadı: {ex.Message}";
            }
        }

        [RelayCommand]
        public void OpenDriveInExplorer(string? driveName)
        {
            try
            {
                string drive = string.IsNullOrWhiteSpace(driveName) ? "C:\\" : driveName;
                Process.Start("explorer.exe", drive);
            }
            catch { }
        }

        [RelayCommand]
        public async Task ScanDriveForLargeFilesAsync(string? driveName)
        {
            if (!string.IsNullOrWhiteSpace(driveName))
            {
                var match = AvailableDrives.FirstOrDefault(d => d.StartsWith(driveName.Substring(0, 1), StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    SelectedDriveFilter = match;
                }
            }
            ActiveSubTab = "LargeFiles";
            await ScanLargeFilesAsync();
        }

        [RelayCommand]
        public async Task RunTrimAsync(string? driveName)
        {
            string drive = string.IsNullOrWhiteSpace(driveName) ? "C" : driveName.Substring(0, 1);
            StatusText = $"{drive}: sürücüsüne TRIM komutu gönderiliyor...";
            bool ok = await _infoService.OptimizeDriveTrimAsync(drive);
            if (ok)
            {
                StatusText = $"{drive}: sürücüsü başarıyla optimize edildi (TRIM tamamlandı).";
            }
            else
            {
                StatusText = $"{drive}: TRIM komutu uygulanamadı (Yönetici yetkisi gerekebilir).";
            }
        }

        [RelayCommand]
        public void CopySpecsToClipboard()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== BAKIM - SİSTEM DONANIM & PLATFORM ÖZETİ ===");
                sb.AppendLine($"İşlemci (CPU): {Hardware.CpuName} ({Hardware.CpuCoresThreads}, {Hardware.CpuClockSpeed}, {Hardware.CpuL3Cache})");
                sb.AppendLine($"Grafik Kartı (GPU): {Hardware.GpuName} ({Hardware.GpuVram}, Sürücü: {Hardware.GpuDriverVersion}, {Hardware.DisplayResolution} @ {Hardware.DisplayRefreshRate})");
                sb.AppendLine($"Bellek (RAM): {Hardware.TotalRamGb:F1} GB Toplam ({Hardware.RamSpeedMhz}) - Boş: {Hardware.FreeRamGb:F1} GB (%{Hardware.RamPercentage} Yük)");
                sb.AppendLine($"Anakart & BIOS: {Hardware.MotherboardModel} - {Hardware.BiosVersion}");
                sb.AppendLine($"Ağ Kartı: {Hardware.NetworkAdapterName} ({Hardware.NetworkLinkSpeed}) - IP: {Hardware.NetworkIpAddress}");
                sb.AppendLine($"İşletim Sistemi: {Hardware.OsVersion} (Uptime: {Hardware.SystemUptimeText})");
                sb.AppendLine($"Güvenlik & Bellenim: Secure Boot: {Hardware.SecureBootStatus} | TPM: {Hardware.TpmStatus} | Sanallaştırma: {Hardware.VirtualizationStatus}");
                sb.AppendLine("--- Sabit Sürücüler ---");
                foreach (var d in Drives)
                {
                    sb.AppendLine($"{d.Name} ({d.VolumeLabel}) - Toplam: {d.FormattedTotal}, Boş: {d.FormattedFree} (%{d.UsagePercentage} Dolu)");
                }

                System.Windows.Clipboard.SetText(sb.ToString());
                StatusText = "Sistem donanım özeti panoya başarıyla kopyalandı!";
            }
            catch (Exception ex)
            {
                StatusText = $"Panoya kopyalanamadı: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task ExportReportHtmlAsync()
        {
            try
            {
                StatusText = "HTML donanım raporu hazırlanıyor...";
                string path = await _infoService.GenerateHardwareReportHtmlAsync();
                StatusText = "Rapor oluşturuldu, varsayılan tarayıcıda açılıyor...";
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusText = $"Rapor oluşturulamadı: {ex.Message}";
            }
        }

        [RelayCommand]
        public void OpenDiskManagement()
        {
            try { Process.Start(new ProcessStartInfo("diskmgmt.msc") { UseShellExecute = true }); } catch { }
        }

        [RelayCommand]
        public void OpenDeviceManager()
        {
            try { Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true }); } catch { }
        }

        [RelayCommand]
        public void OpenTaskManager()
        {
            try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); } catch { }
        }

        [RelayCommand]
        public void RestartAsAdmin()
        {
            UacHelper.RestartAsAdministrator();
        }
    
        #region Modül Yaşam Döngüsü

        private bool _isActive;

        /// <summary>
        /// Modül görünür oldu. Zamanlayıcı BURADA başlar — yapıcı metotta değil.
        /// Böylece açılışta yalnızca ilk modül kaynak tüketir.
        /// </summary>
        public async Task OnActivatedAsync()
        {
            if (_isActive) return;
            _isActive = true;

            ApplyRefreshInterval();
            _liveTelemetryTimer.Start();

            try
            {
                await RefreshAsync();
                await LoadSmartHealthAsync();
            }
            catch (Exception ex)
            {
                Services.AppLog.Error("Modül etkinleştirilirken hata.", ex, nameof(SystemInfoViewModel));
            }
        }

        /// <summary>Modülden çıkıldı: arka planda WMI sorgusu atmaya devam etme.</summary>
        public Task OnDeactivatedAsync()
        {
            if (!_isActive) return Task.CompletedTask;
            _isActive = false;

            _liveTelemetryTimer.Stop();
            return Task.CompletedTask;
        }

        /// <summary>Kullanıcının ayarlardaki yenileme aralığı tercihini uygular.</summary>
        private void ApplyRefreshInterval()
        {
            int seconds = _settingsService.Current.RefreshIntervalSeconds;
            if (seconds < 1) seconds = 1;
            _liveTelemetryTimer.Interval = TimeSpan.FromSeconds(seconds);
        }

        #endregion

}
}
