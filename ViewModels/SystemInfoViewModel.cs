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
    public partial class SystemInfoViewModel : ObservableObject
    {
        private readonly ISystemInfoService _infoService;
        private readonly DispatcherTimer _liveTelemetryTimer;
        private CancellationTokenSource? _scanCts;

        public SystemInfoViewModel() : this(null)
        {
        }

        public SystemInfoViewModel(ISystemInfoService? infoService)
        {
            _infoService = infoService ?? new SystemInfoService();
            Drives = new ObservableCollection<DriveInfoItem>();
            SmartDisks = new ObservableCollection<SmartDiskHealthItem>();
            LargeFiles = new ObservableCollection<LargeDiskFileItem>();
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
            _liveTelemetryTimer.Start();
        }

        public ObservableCollection<DriveInfoItem> Drives { get; }
        public ObservableCollection<SmartDiskHealthItem> SmartDisks { get; }
        public ObservableCollection<LargeDiskFileItem> LargeFiles { get; }
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
        public void RestartAsAdmin()
        {
            UacHelper.RestartAsAdministrator();
        }
    }
}
