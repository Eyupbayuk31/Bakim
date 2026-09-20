using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    public partial class OptimizerViewModel : ObservableObject, IModuleViewModel
    {
        private readonly IAppSettingsService _settingsService;
        private readonly ISystemCleanService _cleanService;
        private readonly ISystemInfoService _infoService;
        private readonly DispatcherTimer _autoRefreshTimer;

        public OptimizerViewModel(ISystemCleanService cleanService, ISystemInfoService infoService,
            IAppSettingsService settingsService)
        {
            _settingsService = settingsService;
            _cleanService = cleanService;
            _infoService = infoService;
            TopProcesses = new ObservableCollection<ProcessMemoryItem>();

            _ = RefreshAsync();

            // Real-Time Data Refresh (3 saniyede bir akıcı periyodik güncelleme)
            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _autoRefreshTimer.Tick += async (_, _) =>
            {
                if (IsAutoRefreshEnabled && !IsBusy)
                {
                    await RefreshBackgroundAsync();
                }
            };
            // Zamanlayıcı OnActivatedAsync() içinde başlar — bkz. IModuleViewModel
        }

        public ObservableCollection<ProcessMemoryItem> TopProcesses { get; }

        [ObservableProperty]
        private SystemHardwareStats _hardware = new();

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _isAutoRefreshEnabled = true;

        [ObservableProperty]
        private bool _isAutoTrimEnabled = true;

        [ObservableProperty]
        private bool _isGamingModeEnabled;

        [ObservableProperty]
        private string _statusText = "Fiziksel bellek durumunu inceleyebilir ve belleği temizleyebilirsiniz.";

        [ObservableProperty]
        private string _operationResultBanner = string.Empty;

        [ObservableProperty]
        private bool _hasResultBanner;

        [ObservableProperty]
        private string _optimizationStageText = string.Empty;

        [ObservableProperty]
        private int _optimizationProgressPercent;

        [ObservableProperty]
        private string _sortColumn = "RAM"; // RAM, Name, PID

        [ObservableProperty]
        private bool _isSortDescending = true;

        public bool IsRamDesc => SortColumn == "RAM" && IsSortDescending;
        public bool IsRamAsc => SortColumn == "RAM" && !IsSortDescending;
        public bool IsNameDesc => SortColumn == "Name" && IsSortDescending;
        public bool IsNameAsc => SortColumn == "Name" && !IsSortDescending;
        public bool IsPidDesc => SortColumn == "PID" && IsSortDescending;
        public bool IsPidAsc => SortColumn == "PID" && !IsSortDescending;

        partial void OnSortColumnChanged(string value) => NotifySortProps();
        partial void OnIsSortDescendingChanged(bool value) => NotifySortProps();

        private void NotifySortProps()
        {
            OnPropertyChanged(nameof(IsRamDesc));
            OnPropertyChanged(nameof(IsRamAsc));
            OnPropertyChanged(nameof(IsNameDesc));
            OnPropertyChanged(nameof(IsNameAsc));
            OnPropertyChanged(nameof(IsPidDesc));
            OnPropertyChanged(nameof(IsPidAsc));
        }

        [RelayCommand]
        public void ToggleSort(string column)
        {
            if (string.Equals(SortColumn, column, StringComparison.OrdinalIgnoreCase))
            {
                IsSortDescending = !IsSortDescending;
            }
            else
            {
                SortColumn = column;
                IsSortDescending = column.Equals("RAM", StringComparison.OrdinalIgnoreCase);
            }
            ApplyCurrentSort();
        }

        private void ApplyCurrentSort()
        {
            var current = TopProcesses.ToList();
            IEnumerable<ProcessMemoryItem> sorted = SortColumn switch
            {
                "Name" => IsSortDescending ? current.OrderByDescending(p => p.ProcessName) : current.OrderBy(p => p.ProcessName),
                "PID" => IsSortDescending ? current.OrderByDescending(p => p.Id) : current.OrderBy(p => p.Id),
                "RAM" or _ => IsSortDescending ? current.OrderByDescending(p => p.WorkingSetBytes) : current.OrderBy(p => p.WorkingSetBytes),
            };

            TopProcesses.Clear();
            foreach (var item in sorted) TopProcesses.Add(item);
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                Hardware = await _infoService.GetSystemHardwareAsync();
                await LoadTopProcessesAsync();
                StatusText = "Bellek ve işlem listesi güncellendi.";
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

        private async Task RefreshBackgroundAsync()
        {
            try
            {
                var hw = await _infoService.GetSystemHardwareAsync();
                Hardware = hw;

                // Process Auto-Tuner: RAM %85'i aştığında otomatik WorkingSet boşalt
                if (IsAutoTrimEnabled && hw.RamPercentage >= 85)
                {
                    long freed = await _cleanService.AutoTrimWorkingSetsAsync();
                    if (freed > 0)
                    {
                        StatusText = $"Oto-RAM Kırpma: {CleanCategory.FormatBytes(freed)} bellek serbest bırakıldı.";
                    }
                }

                await LoadTopProcessesAsync();
            }
            catch
            {
                // Arka plan otomatik yenilemede UI'yı rahatsız etme
            }
        }

        [RelayCommand]
        public async Task SetHighPriorityAsync(ProcessMemoryItem? item)
        {
            if (item == null) return;
            bool ok = await _cleanService.SetProcessPriorityAsync(item.Id, ProcessPriorityClass.High);
            if (ok)
            {
                item.PriorityText = "Yüksek";
                StatusText = $"'{item.ProcessName}' önceliği YÜKSEK olarak ayarlandı.";
            }
        }

        [RelayCommand]
        public async Task SetNormalPriorityAsync(ProcessMemoryItem? item)
        {
            if (item == null) return;
            bool ok = await _cleanService.SetProcessPriorityAsync(item.Id, ProcessPriorityClass.Normal);
            if (ok)
            {
                item.PriorityText = "Normal";
                StatusText = $"'{item.ProcessName}' önceliği NORMAL olarak ayarlandı.";
            }
        }

        [RelayCommand]
        public async Task OptimizeRamAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            HasResultBanner = false;
            OptimizationStageText = "Bellek haritası analiz ediliyor...";
            OptimizationProgressPercent = 0;

            try
            {
                // Stage 1: Analysis pacing
                await Task.Delay(450);
                OptimizationProgressPercent = 33;

                // Stage 2: Working set compression
                OptimizationStageText = "Çalışma kümeleri kırpılıyor...";
                await Task.Delay(350);
                OptimizationProgressPercent = 66;

                // Stage 3: Actual RAM optimization
                long freedBytes = await _cleanService.OptimizeRamAsync();
                OptimizationProgressPercent = 90;
                OptimizationStageText = "Donanım metrikleri yenileniyor...";

                Hardware = await _infoService.GetSystemHardwareAsync();
                await LoadTopProcessesAsync();

                OptimizationProgressPercent = 100;
                OptimizationStageText = "Bellek boşaltıldı! ✓";
                await Task.Delay(600);

                HasResultBanner = true;
                OperationResultBanner = $"Bellek temizlendi: {CleanCategory.FormatBytes(freedBytes)} alan serbest bırakıldı.";
                StatusText = "İşlem tamamlandı.";
            }
            catch (Exception ex)
            {
                OptimizationStageText = string.Empty;
                StatusText = $"Hata: {ex.Message}";
            }
            finally
            {
                OptimizationProgressPercent = 0;
                OptimizationStageText = string.Empty;
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task KillProcessAsync(ProcessMemoryItem? item)
        {
            if (item == null) return;

            if (item.IsSystemProcess)
            {
                MessageBox.Show(
                    $"'{item.ProcessName}' kritik bir Windows sistem sürecidir.\nSistem kararlılığını korumak için bu işlem sonlandırılamaz.",
                    "Kritik Sistem Koruması",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"'{item.ProcessName}' (PID: {item.Id}) işlemini sonlandırmak istediğinize emin misiniz?\n\nKaydedilmemiş çalışma verileri kaybolabilir.",
                "Görevi Sonlandır",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            item.IsTerminating = true;
            try
            {
                bool success = await _cleanService.KillProcessAsync(item.Id);
                if (success)
                {
                    HasResultBanner = true;
                    OperationResultBanner = $"'{item.ProcessName}' (PID: {item.Id}) işlemi başarıyla sonlandırıldı.";
                    StatusText = $"Süreç kapatıldı: {item.ProcessName}";
                    await RefreshBackgroundAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Süreç sonlandırılamadı:\n{ex.Message}\n\nBu işlem için yönetici hakları gerekebilir.",
                    "Hata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                item.IsTerminating = false;
            }
        }

        private async Task LoadTopProcessesAsync()
        {
            await Task.Run(() =>
            {
                var list = new List<ProcessMemoryItem>();
                var protectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "system", "smss", "csrss", "wininit", "services", "lsass", "svchost", "dwm", "explorer"
                };

                try
                {
                    var processes = Process.GetProcesses();
                    var topList = processes
                        .Select(p =>
                        {
                            try
                            {
                                return new
                                {
                                    Process = p,
                                    Memory = p.WorkingSet64
                                };
                            }
                            catch
                            {
                                return null;
                            }
                        })
                        .Where(x => x != null && x.Memory > 512 * 1024)
                        .OrderByDescending(x => x!.Memory)
                        .Take(50)
                        .ToList();

                    foreach (var item in topList)
                    {
                        if (item == null) continue;
                        var p = item.Process;
                        double mb = Math.Round(item.Memory / (1024.0 * 1024.0), 1);
                        string priorityStr = "Normal";
                        try
                        {
                            priorityStr = p.PriorityClass switch
                            {
                                ProcessPriorityClass.RealTime => "Gerçek Zamanlı",
                                ProcessPriorityClass.High => "Yüksek",
                                ProcessPriorityClass.AboveNormal => "Normal Üstü",
                                ProcessPriorityClass.BelowNormal => "Normal Altı",
                                ProcessPriorityClass.Idle => "Düşük",
                                _ => "Normal"
                            };
                        }
                        catch { }

                        list.Add(new ProcessMemoryItem
                        {
                            Id = p.Id,
                            ProcessName = p.ProcessName,
                            WorkingSetBytes = item.Memory,
                            WorkingSetMb = mb,
                            PriorityText = priorityStr,
                            IsSystemProcess = protectedNames.Contains(p.ProcessName)
                        });
                    }

                    // Kullanıcının seçtiği aktif sıralamayı koru
                    IEnumerable<ProcessMemoryItem> sorted = SortColumn switch
                    {
                        "Name" => IsSortDescending ? list.OrderByDescending(p => p.ProcessName) : list.OrderBy(p => p.ProcessName),
                        "PID" => IsSortDescending ? list.OrderByDescending(p => p.Id) : list.OrderBy(p => p.Id),
                        "RAM" or _ => IsSortDescending ? list.OrderByDescending(p => p.WorkingSetBytes) : list.OrderBy(p => p.WorkingSetBytes),
                    };

                    var finalList = sorted.ToList();

                    App.Current?.Dispatcher?.Invoke(() =>
                    {
                        TopProcesses.Clear();
                        foreach (var p in finalList)
                        {
                            TopProcesses.Add(p);
                        }
                    });
                }
                catch (Exception)
                {
                    // Savunmacı yaklaşım
                }
            });
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
            _autoRefreshTimer.Start();

            try
            {
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                Services.AppLog.Error("Modül etkinleştirilirken hata.", ex, nameof(OptimizerViewModel));
            }
        }

        /// <summary>Modülden çıkıldı: arka planda WMI sorgusu atmaya devam etme.</summary>
        public Task OnDeactivatedAsync()
        {
            if (!_isActive) return Task.CompletedTask;
            _isActive = false;

            _autoRefreshTimer.Stop();
            return Task.CompletedTask;
        }

        /// <summary>Kullanıcının ayarlardaki yenileme aralığı tercihini uygular.</summary>
        private void ApplyRefreshInterval()
        {
            int seconds = _settingsService.Current.RefreshIntervalSeconds;
            if (seconds < 1) seconds = 1;
            _autoRefreshTimer.Interval = TimeSpan.FromSeconds(seconds);
        }

        #endregion

}
}
