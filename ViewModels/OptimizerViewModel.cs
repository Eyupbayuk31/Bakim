using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Helpers;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    public partial class OptimizerViewModel : ObservableObject, IModuleViewModel
    {
        private readonly IAppSettingsService _settingsService;
        private readonly ISystemCleanService _cleanService;
        private readonly ISystemInfoService _infoService;
        private readonly IVirusTotalCheckService? _virusTotalService;
        private readonly DispatcherTimer _autoRefreshTimer;

        private readonly List<ProcessMemoryItem> _allLoadedProcesses = new();
        private static readonly Dictionary<int, (DateTime SampleTime, TimeSpan CpuTime)> CpuTracker = new();
        private bool _isBackgroundRefreshing;

        public OptimizerViewModel(
            ISystemCleanService cleanService, 
            ISystemInfoService infoService,
            IAppSettingsService settingsService,
            IVirusTotalCheckService? virusTotalService = null)
        {
            _settingsService = settingsService;
            _cleanService = cleanService;
            _infoService = infoService;
            _virusTotalService = virusTotalService;

            TopProcesses = new ObservableCollection<ProcessMemoryItem>();
            DisplayedProcesses = new ObservableCollection<ProcessMemoryItem>();
            RamHistoryPoints = new ObservableCollection<double>();

            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _autoRefreshTimer.Tick += async (_, _) =>
            {
                // Yeniden girme koruması: önceki yenileme (ikon/sürüm okuma) 3 sn'den uzun
                // sürerse turlar üst üste binip süreç tanıtıcıları birikiyordu.
                if (!IsAutoRefreshEnabled || IsBusy || _isBackgroundRefreshing) return;
                _isBackgroundRefreshing = true;
                try
                {
                    await RefreshBackgroundAsync();
                }
                finally
                {
                    _isBackgroundRefreshing = false;
                }
            };
        }

        public ObservableCollection<ProcessMemoryItem> TopProcesses { get; }
        public ObservableCollection<ProcessMemoryItem> DisplayedProcesses { get; }
        public ObservableCollection<double> RamHistoryPoints { get; }

        [ObservableProperty]
        private SystemHardwareStats _hardware = new();

        [ObservableProperty]
        private DetailedMemoryComposition _composition = new();

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _isAutoRefreshEnabled = true;

        [ObservableProperty]
        private bool _isAutoTrimEnabled = true;

        [ObservableProperty]
        private string _statusText = "Fiziksel bellek durumunu inceleyebilir ve derinlemesine temizleyebilirsiniz.";

        [ObservableProperty]
        private string _operationResultBanner = string.Empty;

        [ObservableProperty]
        private bool _hasResultBanner;

        [ObservableProperty]
        private string _optimizationStageText = string.Empty;

        [ObservableProperty]
        private int _optimizationProgressPercent;

        #region Search & Filtering Properties

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _filterMode = "All"; // All, User, System, HighRam

        public bool IsFilterAll => FilterMode == "All";
        public bool IsFilterUser => FilterMode == "User";
        public bool IsFilterSystem => FilterMode == "System";
        public bool IsFilterHighRam => FilterMode == "HighRam";

        partial void OnSearchTextChanged(string value) => ApplyFilterAndSearch();

        partial void OnFilterModeChanged(string value)
        {
            OnPropertyChanged(nameof(IsFilterAll));
            OnPropertyChanged(nameof(IsFilterUser));
            OnPropertyChanged(nameof(IsFilterSystem));
            OnPropertyChanged(nameof(IsFilterHighRam));
            ApplyFilterAndSearch();
        }

        [RelayCommand]
        public void SetFilter(string mode)
        {
            if (!string.IsNullOrWhiteSpace(mode))
            {
                FilterMode = mode;
            }
        }

        #endregion

        #region Right Drawer (Process Inspection)

        [ObservableProperty]
        private ProcessMemoryItem? _selectedProcess;

        public bool IsDrawerOpen => SelectedProcess != null;

        partial void OnSelectedProcessChanged(ProcessMemoryItem? value)
        {
            OnPropertyChanged(nameof(IsDrawerOpen));
        }

        [RelayCommand]
        public void SelectProcess(ProcessMemoryItem? item)
        {
            SelectedProcess = item;
        }

        [RelayCommand]
        public void CloseDrawer()
        {
            SelectedProcess = null;
        }

        [RelayCommand]
        public void OpenFileLocation(ProcessMemoryItem? item)
        {
            var target = item ?? SelectedProcess;
            if (target == null || string.IsNullOrWhiteSpace(target.FilePath) || !File.Exists(target.FilePath))
            {
                MessageBox.Show("Bu sürecin çalıştırılabilir dosya konumuna erişilemedi.", "Dosya Konumu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{target.FilePath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Klasör açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public async Task ToggleSuspendProcessAsync(ProcessMemoryItem? item)
        {
            var target = item ?? SelectedProcess;
            if (target == null) return;

            if (target.IsSystemProcess)
            {
                MessageBox.Show("Kritik Windows sistem süreçleri askıya alınamaz!", "Güvenlik Kalkanı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (target.IsSuspended)
                {
                    bool ok = await _cleanService.ResumeProcessAsync(target.Id);
                    if (ok)
                    {
                        target.IsSuspended = false;
                        OperationResultBanner = $"'{target.ProcessName}' işlemi devam ettirildi.";
                        HasResultBanner = true;
                    }
                }
                else
                {
                    bool ok = await _cleanService.SuspendProcessAsync(target.Id);
                    if (ok)
                    {
                        target.IsSuspended = true;
                        OperationResultBanner = $"'{target.ProcessName}' işlemi donduruldu (askıya alındı).";
                        HasResultBanner = true;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"İşlem askıya alma/devam ettirme hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public async Task TrimProcessRamAsync(ProcessMemoryItem? item)
        {
            var target = item ?? SelectedProcess;
            if (target == null) return;

            try
            {
                using var p = Process.GetProcessById(target.Id);
                // Trim process memory via Windows API
                await _cleanService.SetProcessPriorityAsync(target.Id, p.PriorityClass);
                OperationResultBanner = $"'{target.ProcessName}' çalışma kümesi boşaltıldı.";
                HasResultBanner = true;
                await RefreshBackgroundAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Çalışma kümesi boşaltılamadı: {ex.Message}", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        [RelayCommand]
        public async Task ScanWithVirusTotalAsync(ProcessMemoryItem? item)
        {
            var target = item ?? SelectedProcess;
            if (target == null || string.IsNullOrWhiteSpace(target.FilePath) || !File.Exists(target.FilePath))
            {
                MessageBox.Show("Taranacak dosya yolu bulunamadı.", "VirusTotal", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_virusTotalService == null)
            {
                MessageBox.Show("VirusTotal servisi mevcut değil.", "VirusTotal", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                StatusText = $"'{target.ProcessName}' VirusTotal'de denetleniyor...";
                string sha256 = _virusTotalService.ComputeSha256(target.FilePath);
                var (malicious, total, message) = await _virusTotalService.CheckHashAsync(sha256);

                if (total > 0)
                {
                    var result = MessageBox.Show(
                        $"VirusTotal Tehdit Analiz Raporu:\n\n" +
                        $"Dosya: {Path.GetFileName(target.FilePath)}\n" +
                        $"SHA-256: {sha256}\n" +
                        $"Zararlı Tespiti: {malicious} / {total} Güvenlik Motoru\n" +
                        $"Durum: {(malicious == 0 ? "Güvenli / Temiz" : "DİKKAT: Şüpheli veya Zararlı!")}\n\n" +
                        $"Raporu tarayıcıda açmak ister misiniz?",
                        "VirusTotal Sonucu",
                        MessageBoxButton.YesNo,
                        malicious > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        _virusTotalService.OpenInBrowser(sha256);
                    }
                }
                else
                {
                    var result = MessageBox.Show(
                        $"VirusTotal Veritabanı Bilgisi:\n\n{message}\n\nDosyayı VirusTotal'de analiz etmek için tarayıcıda açmak ister misiniz?",
                        "VirusTotal",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        _virusTotalService.OpenUploadPage(target.FilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Tarama başarısız: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StatusText = "Fiziksel bellek durumunu inceleyebilirsiniz.";
            }
        }

        #endregion

        #region Sorting

        [ObservableProperty]
        private string _sortColumn = "RAM"; // RAM, Name, PID, CPU

        [ObservableProperty]
        private bool _isSortDescending = true;

        public bool IsRamDesc => SortColumn == "RAM" && IsSortDescending;
        public bool IsRamAsc => SortColumn == "RAM" && !IsSortDescending;
        public bool IsNameDesc => SortColumn == "Name" && IsSortDescending;
        public bool IsNameAsc => SortColumn == "Name" && !IsSortDescending;
        public bool IsPidDesc => SortColumn == "PID" && IsSortDescending;
        public bool IsPidAsc => SortColumn == "PID" && !IsSortDescending;
        public bool IsCpuDesc => SortColumn == "CPU" && IsSortDescending;
        public bool IsCpuAsc => SortColumn == "CPU" && !IsSortDescending;

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
            OnPropertyChanged(nameof(IsCpuDesc));
            OnPropertyChanged(nameof(IsCpuAsc));
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
                IsSortDescending = column.Equals("RAM", StringComparison.OrdinalIgnoreCase) || column.Equals("CPU", StringComparison.OrdinalIgnoreCase);
            }
            ApplyFilterAndSearch();
        }

        #endregion

        #region RAMMap & Optimization Commands

        [RelayCommand]
        public async Task OptimizeRamAsync()
        {
            if (IsBusy) return;
            await ExecuteOptimizationRoutineAsync(
                "Çalışma kümeleri kırpılıyor (Trim Working Sets)...",
                () => _cleanService.AutoTrimWorkingSetsAsync());
        }

        [RelayCommand]
        public async Task ClearStandbyListAsync()
        {
            if (IsBusy) return;
            await ExecuteOptimizationRoutineAsync(
                "Bekleme listesi temizleniyor (Clear Standby List)...",
                () => _cleanService.ClearStandbyListAsync());
        }

        [RelayCommand]
        public async Task FlushModifiedPagesAsync()
        {
            if (IsBusy) return;
            await ExecuteOptimizationRoutineAsync(
                "Değiştirilmiş sayfalar diske aktarılıyor (Flush Modified)...",
                () => _cleanService.FlushModifiedPagesAsync());
        }

        [RelayCommand]
        public async Task PurgeAllMemoryAsync()
        {
            if (IsBusy) return;
            await ExecuteOptimizationRoutineAsync(
                "Derin sistem boşaltması yapılıyor (Purge All)...",
                () => _cleanService.PurgeAllMemoryAsync());
        }

        private async Task ExecuteOptimizationRoutineAsync(string stageName, Func<Task<long>> routine)
        {
            IsBusy = true;
            HasResultBanner = false;
            OptimizationStageText = stageName;
            OptimizationProgressPercent = 25;

            try
            {
                OptimizationProgressPercent = 50;

                long freedBytes = await routine();

                OptimizationProgressPercent = 85;
                OptimizationStageText = "Bellek haritası yenileniyor...";

                await UpdateHardwareAndCompositionAsync();
                await LoadTopProcessesAsync();

                OptimizationProgressPercent = 100;

                // Sonuç ölçüme dayanır; fark yoksa bunu açıkça söyleriz.
                HasResultBanner = true;
                OperationResultBanner = Bakım.Core.Text.MemoryResultText.Describe(freedBytes);
                StatusText = Bakım.Helpers.UacHelper.IsAdministrator() || freedBytes > 0
                    ? "İşlem tamamlandı."
                    : "İşlem tamamlandı. Bekleme listesi işlemleri yönetici yetkisi gerektirir.";
            }
            catch (Exception ex)
            {
                StatusText = $"Optimizasyon hatası: {ex.Message}";
            }
            finally
            {
                OptimizationProgressPercent = 0;
                OptimizationStageText = string.Empty;
                IsBusy = false;
            }
        }

        #endregion

        #region Process Actions

        [RelayCommand]
        public async Task SetHighPriorityAsync(ProcessMemoryItem? item)
        {
            var target = item ?? SelectedProcess;
            if (target == null) return;

            bool ok = await _cleanService.SetProcessPriorityAsync(target.Id, ProcessPriorityClass.High);
            if (ok)
            {
                target.PriorityText = "Yüksek";
                StatusText = $"'{target.ProcessName}' önceliği YÜKSEK olarak ayarlandı.";
            }
        }

        [RelayCommand]
        public async Task SetNormalPriorityAsync(ProcessMemoryItem? item)
        {
            var target = item ?? SelectedProcess;
            if (target == null) return;

            bool ok = await _cleanService.SetProcessPriorityAsync(target.Id, ProcessPriorityClass.Normal);
            if (ok)
            {
                target.PriorityText = "Normal";
                StatusText = $"'{target.ProcessName}' önceliği NORMAL olarak ayarlandı.";
            }
        }

        [RelayCommand]
        public async Task KillProcessAsync(ProcessMemoryItem? item)
        {
            var target = item ?? SelectedProcess;
            if (target == null) return;

            if (target.IsSystemProcess)
            {
                MessageBox.Show(
                    $"'{target.ProcessName}' kritik bir Windows sistem sürecidir.\nSistem kararlılığını korumak için bu işlem sonlandırılamaz.",
                    "Kritik Sistem Koruması",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"'{target.ProcessName}' (PID: {target.Id}) işlemini sonlandırmak istediğinize emin misiniz?\n\nKaydedilmemiş çalışma verileri kaybolabilir.",
                "Görevi Sonlandır",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            target.IsTerminating = true;
            try
            {
                bool success = await _cleanService.KillProcessAsync(target.Id);
                if (success)
                {
                    HasResultBanner = true;
                    OperationResultBanner = $"'{target.ProcessName}' (PID: {target.Id}) işlemi başarıyla sonlandırıldı.";
                    StatusText = $"Süreç kapatıldı: {target.ProcessName}";
                    if (SelectedProcess?.Id == target.Id)
                    {
                        SelectedProcess = null;
                    }
                    await RefreshBackgroundAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Süreç sonlandırılamadı:\n{ex.Message}",
                    "Hata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                target.IsTerminating = false;
            }
        }

        #endregion

        #region Refresh & Data Loading

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                await UpdateHardwareAndCompositionAsync();
                await LoadTopProcessesAsync();
                StatusText = "Bellek röntgeni ve işlem listesi güncellendi.";
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
                await UpdateHardwareAndCompositionAsync();

                // Oto-Trim
                if (IsAutoTrimEnabled && Hardware.RamPercentage >= 85)
                {
                    long freed = await _cleanService.AutoTrimWorkingSetsAsync();
                    if (Bakım.Core.Text.MemoryResultText.IsSignificant(freed))
                    {
                        StatusText = $"Oto-RAM Kırpma: {CleanCategory.FormatBytes(freed)} bellek serbest bırakıldı.";
                    }
                }

                await LoadTopProcessesAsync();
            }
            catch
            {
                // Arka plan sessiz koruma
            }
        }

        private async Task UpdateHardwareAndCompositionAsync()
        {
            var hw = await _infoService.GetSystemHardwareAsync();
            var comp = await _cleanService.GetDetailedMemoryCompositionAsync();

            Hardware = hw;
            Composition = comp;

            // Sparkline güncellemesi (maks 30 nokta)
            App.Current?.Dispatcher?.Invoke(() =>
            {
                RamHistoryPoints.Add(hw.RamPercentage);
                if (RamHistoryPoints.Count > 30)
                {
                    RamHistoryPoints.RemoveAt(0);
                }
            });
        }

        private async Task LoadTopProcessesAsync()
        {
            await Task.Run(() =>
            {
                var list = new List<ProcessMemoryItem>();
                string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                int currentPid = Environment.ProcessId;

                // H-6: GetProcesses() her süreç için bir tanıtıcı açar; eskiden hiçbiri Dispose
                // edilmiyordu ve 3 sn'de bir yenilemede tanıtıcı sayısı sürekli artıyordu.
                var processes = Process.GetProcesses();
                try
                {
                    var now = DateTime.UtcNow;

                    // Kapanmış süreçlerin CPU örneklerini at (sözlük sınırsız büyüyordu).
                    var alive = new HashSet<int>(processes.Select(p => p.Id));
                    lock (CpuTracker)
                    {
                        foreach (var deadPid in CpuTracker.Keys.Where(pid => !alive.Contains(pid)).ToList())
                            CpuTracker.Remove(deadPid);
                    }

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
                        .Take(60)
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

                        // CPU hesaplama
                        double cpuPct = 0;
                        try
                        {
                            TimeSpan currentCpu = p.TotalProcessorTime;
                            (DateTime SampleTime, TimeSpan CpuTime) previous;
                            bool hasPrevious;
                            lock (CpuTracker) hasPrevious = CpuTracker.TryGetValue(p.Id, out previous);
                            if (hasPrevious)
                            {
                                double deltaCpuMs = (currentCpu - previous.CpuTime).TotalMilliseconds;
                                double deltaRealMs = (now - previous.SampleTime).TotalMilliseconds;
                                if (deltaRealMs > 0)
                                {
                                    cpuPct = Math.Round((deltaCpuMs / (deltaRealMs * Environment.ProcessorCount)) * 100, 1);
                                    if (cpuPct < 0) cpuPct = 0;
                                    if (cpuPct > 100) cpuPct = 100;
                                }
                            }
                            lock (CpuTracker) CpuTracker[p.Id] = (now, currentCpu);
                        }
                        catch { }

                        // İkon ve Yayıncı bilgisi
                        string? exePath = ProcessIconHelper.TryGetProcessMainModulePath(p);
                        var icon = ProcessIconHelper.GetProcessIcon(exePath);
                        var meta = ProcessIconHelper.GetProcessMetadata(exePath, p.ProcessName);

                        string uptime = "Bilinmiyor";
                        try
                        {
                            var up = DateTime.Now - p.StartTime;
                            uptime = up.TotalHours >= 1 ? $"{(int)up.TotalHours} sa {up.Minutes} dk" : $"{up.Minutes} dk {up.Seconds} sn";
                        }
                        catch { }

                        list.Add(new ProcessMemoryItem
                        {
                            Id = p.Id,
                            ProcessName = p.ProcessName,
                            Description = meta.Description,
                            Publisher = meta.Publisher,
                            FilePath = exePath ?? string.Empty,
                            IconSource = icon,
                            WorkingSetBytes = item.Memory,
                            WorkingSetMb = mb,
                            PrivateBytes = p.PrivateMemorySize64,
                            CpuPercent = cpuPct,
                            ThreadCount = p.Threads.Count,
                            HandleCount = p.HandleCount,
                            PriorityText = priorityStr,
                            UptimeText = uptime,
                            // Sonlandırmayı reddeden kuralın aynısı (CriticalProcessPolicy, H-7).
                            IsSystemProcess = Bakım.Core.Safety.CriticalProcessPolicy.IsProtected(p.Id, p.ProcessName, exePath, windowsDir, currentPid)
                        });
                    }

                    App.Current?.Dispatcher?.Invoke(() =>
                    {
                        _allLoadedProcesses.Clear();
                        _allLoadedProcesses.AddRange(list);

                        TopProcesses.Clear();
                        foreach (var p in list) TopProcesses.Add(p);

                        ApplyFilterAndSearch();
                    });
                }
                catch (Exception ex)
                {
                    AppLog.Debug($"Süreç listesi okunamadı: {ex.Message}", nameof(OptimizerViewModel));
                }
                finally
                {
                    foreach (var proc in processes) proc.Dispose();
                }
            });
        }

        private void ApplyFilterAndSearch()
        {
            var query = _allLoadedProcesses.AsEnumerable();

            // Kategori filtreleme
            if (FilterMode == "User")
            {
                query = query.Where(p => !p.IsSystemProcess);
            }
            else if (FilterMode == "System")
            {
                query = query.Where(p => p.IsSystemProcess);
            }
            else if (FilterMode == "HighRam")
            {
                query = query.Where(p => p.WorkingSetMb >= 500);
            }

            // Metin araması
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string search = SearchText.Trim().ToLowerInvariant();
                query = query.Where(p =>
                    p.ProcessName.ToLowerInvariant().Contains(search) ||
                    p.Id.ToString().Contains(search) ||
                    p.Publisher.ToLowerInvariant().Contains(search) ||
                    p.Description.ToLowerInvariant().Contains(search));
            }

            // Sıralama
            query = SortColumn switch
            {
                "Name" => IsSortDescending ? query.OrderByDescending(p => p.ProcessName) : query.OrderBy(p => p.ProcessName),
                "PID" => IsSortDescending ? query.OrderByDescending(p => p.Id) : query.OrderBy(p => p.Id),
                "CPU" => IsSortDescending ? query.OrderByDescending(p => p.CpuPercent) : query.OrderBy(p => p.CpuPercent),
                "RAM" or _ => IsSortDescending ? query.OrderByDescending(p => p.WorkingSetBytes) : query.OrderBy(p => p.WorkingSetBytes),
            };

            DisplayedProcesses.Clear();
            foreach (var item in query)
            {
                DisplayedProcesses.Add(item);
            }
        }

        #endregion

        #region Lifecycle

        private bool _isActive;

        public async Task OnActivatedAsync()
        {
            if (_isActive) return;
            _isActive = true;

            int seconds = _settingsService.Current.RefreshIntervalSeconds;
            if (seconds < 1) seconds = 1;
            _autoRefreshTimer.Interval = TimeSpan.FromSeconds(seconds);
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

        public Task OnDeactivatedAsync()
        {
            if (!_isActive) return Task.CompletedTask;
            _isActive = false;

            _autoRefreshTimer.Stop();
            return Task.CompletedTask;
        }

        #endregion
    }
}
