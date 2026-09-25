using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private readonly INavigationService _navigation;
        private readonly StorageViewModel _storage;
        private readonly DispatcherTimer _liveTelemetryTimer;

        public SystemInfoViewModel(ISystemInfoService infoService,
            IAppSettingsService settingsService,
            INavigationService navigation,
            StorageViewModel storage)
        {
            _settingsService = settingsService;
            _infoService = infoService;
            _navigation = navigation;
            _storage = storage;
            Drives = new ObservableCollection<DriveInfoItem>();
            SmartDisks = new ObservableCollection<SmartDiskHealthItem>();

            // Gerçek Zamanlı Donanım Telemetrisi (CPU, RAM vb. 3 saniyede bir güncellenir)
            _liveTelemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _liveTelemetryTimer.Tick += Helpers.AsyncTick.Guarded(async () =>
            {
                // Donanım sorgusu (WMI) 3 sn'den uzun sürebilir; tur korumalı (P-2).
                if (!IsBusy && ActiveSubTab == "Hardware")
                {
                    Hardware = await _infoService.GetSystemHardwareAsync();
                }
            }, nameof(SystemInfoViewModel));
            // Zamanlayıcı ve ilk yükleme OnActivatedAsync() içinde — bkz. IModuleViewModel
        }

        public ObservableCollection<DriveInfoItem> Drives { get; }
        public ObservableCollection<SmartDiskHealthItem> SmartDisks { get; }

        [ObservableProperty]
        private SystemHardwareStats _hardware = new();

        [ObservableProperty]
        private string _activeSubTab = "Hardware"; // Hardware, Smart

        public bool IsHardwareTab => ActiveSubTab == "Hardware";
        public bool IsSmartTab => ActiveSubTab == "Smart";

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusText = "Sistem donanımı ve sürücü bilgileri hazır.";

        partial void OnActiveSubTabChanged(string value)
        {
            OnPropertyChanged(nameof(IsHardwareTab));
            OnPropertyChanged(nameof(IsSmartTab));
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

        /// <summary>Sürücü kartındaki "Büyük dosyalar": Depolama sayfasında o sürücüyü tarar.</summary>
        [RelayCommand]
        public async Task ScanDriveForLargeFilesAsync(string? driveName)
        {
            _navigation.Navigate("Storage");
            await _storage.ScanDriveAsync(driveName);
        }

        [RelayCommand]
        public async Task RunTrimAsync(string? driveName)
        {
            string drive = string.IsNullOrWhiteSpace(driveName) ? "C" : driveName.Substring(0, 1);
            StatusText = $"{drive}: sürücüsüne TRIM komutu gönderiliyor...";
            var result = await _infoService.OptimizeDriveTrimAsync(drive);
            StatusText = result.Message;
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
