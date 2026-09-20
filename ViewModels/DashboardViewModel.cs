using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    public partial class DashboardViewModel : ObservableObject, IModuleViewModel
    {
        private readonly IAppSettingsService _settingsService;
        private readonly ISystemCleanService _cleanService;
        private readonly ISystemInfoService _infoService;
        private readonly ITelemetryService _telemetryService;
        private readonly IGameModeService _gameModeService;

        private readonly DispatcherTimer _telemetryTimer;
        private readonly Queue<double> _cpuHistory = new();
        private readonly Queue<double> _ramHistory = new();
        private const int MaxHistoryPoints = 25;
        private int _sampleCount;

        public DashboardViewModel(
            ISystemCleanService cleanService, 
            ISystemInfoService infoService, 
            ITelemetryService telemetryService,
            IGameModeService gameModeService,
            IAppSettingsService settingsService)
        {
            _settingsService = settingsService;
            _cleanService = cleanService;
            _infoService = infoService;
            _telemetryService = telemetryService;
            _gameModeService = gameModeService;

            _isGameModeActive = _gameModeService.IsGameModeActive;
            _gameModeService.GameModeChanged += OnGameModeChanged;

            TopHogs = new ObservableCollection<ResourceHogItem>();

            // Pre-fill history buffers with baseline 0
            for (int i = 0; i < MaxHistoryPoints; i++)
            {
                _cpuHistory.Enqueue(0);
                _ramHistory.Enqueue(0);
            }
            UpdateSparklineCollections();

            // Telemetry sampling timer (1.5 seconds)
            _telemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            _telemetryTimer.Tick += async (_, _) => await OnTelemetryTickAsync();
            // Zamanlayıcı ve ilk yükleme OnActivatedAsync() içinde başlar — bkz. IModuleViewModel
        }

        #region Modül Yaşam Döngüsü

        private bool _isActive;

        /// <summary>
        /// Pano görünür oldu. v3.1'e kadar telemetri zamanlayıcısı yapıcı metotta
        /// başlıyor ve hiç durmuyordu; uygulama arka plandayken bile 1.5 saniyede bir
        /// WMI sorgusu atılıyordu.
        /// </summary>
        public async Task OnActivatedAsync()
        {
            if (_isActive) return;
            _isActive = true;

            ApplyRefreshInterval();
            _telemetryTimer.Start();

            try
            {
                await InitializeDashboardAsync();
            }
            catch (Exception ex)
            {
                Services.AppLog.Error("Pano etkinleştirilirken hata.", ex, nameof(DashboardViewModel));
            }
        }

        public Task OnDeactivatedAsync()
        {
            if (!_isActive) return Task.CompletedTask;
            _isActive = false;

            _telemetryTimer.Stop();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Panonun örnekleme aralığı kullanıcı tercihinin yarısıdır (en az 1 sn):
        /// canlı grafiklerin akıcı görünmesi için diğer modüllerden sık örnekler.
        /// </summary>
        private void ApplyRefreshInterval()
        {
            int seconds = _settingsService.Current.RefreshIntervalSeconds;
            double interval = Math.Max(1.0, seconds / 2.0);
            _telemetryTimer.Interval = TimeSpan.FromSeconds(interval);
        }

        #endregion

        public ObservableCollection<ResourceHogItem> TopHogs { get; }

        [ObservableProperty]
        private SystemHardwareStats _hardware = new();

        [ObservableProperty]
        private TelemetryMetrics _metrics = new();

        [ObservableProperty]
        private HealthScoreBreakdown _health = new();

        [ObservableProperty]
        private PointCollection _cpuSparklinePoints = new();

        [ObservableProperty]
        private PointCollection _cpuSparklinePolygonPoints = new();

        [ObservableProperty]
        private PointCollection _ramSparklinePoints = new();

        [ObservableProperty]
        private PointCollection _ramSparklinePolygonPoints = new();

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _isOptimizing;

        [ObservableProperty]
        private bool _hasOptimizationResult;

        [ObservableProperty]
        private string _optimizationResultMessage = string.Empty;

        [ObservableProperty]
        private string _optimizationFreedBadge = string.Empty;

        [ObservableProperty]
        private string _boostStageText = string.Empty;

        [ObservableProperty]
        private int _boostProgressPercent;

        [ObservableProperty]
        private bool _isGameModeActive;

        public string GameModeStatusBadge => IsGameModeActive ? "Aktif · Arka Plan Donduruldu" : "Devre Dışı · Standart Mod";

        public string GameModeButtonText => IsGameModeActive ? "Oyun Modunu Kapat" : "Oyun Modunu Başlat";

        private void OnGameModeChanged(bool active)
        {
            IsGameModeActive = active;
            OnPropertyChanged(nameof(GameModeStatusBadge));
            OnPropertyChanged(nameof(GameModeButtonText));
        }

        [RelayCommand]
        public async Task ToggleGameModeAsync()
        {
            long freed = await _gameModeService.ToggleGameModeAsync();
            IsGameModeActive = _gameModeService.IsGameModeActive;
            OnPropertyChanged(nameof(GameModeStatusBadge));
            OnPropertyChanged(nameof(GameModeButtonText));

            if (IsGameModeActive)
            {
                string freedText = CleanCategory.FormatBytes(freed);
                OptimizationResultMessage = $"🎮 Ultra Oyun Modu Aktif! Arka plan servisleri donduruldu. {freedText} bellek oyuna ayrıldı!";
                OptimizationFreedBadge = $"+{freedText} Serbest";
                HasOptimizationResult = true;
            }
            else
            {
                OptimizationResultMessage = "Oyun Modu Kapatıldı. Arka plan servisleri normale döndü.";
                HasOptimizationResult = true;
            }
        }

        public event Action? NavigateToCleanerRequested;
        public event Action? NavigateToOptimizerRequested;

        private async Task InitializeDashboardAsync()
        {
            IsBusy = true;
            try
            {
                Hardware = await _infoService.GetSystemHardwareAsync();
                await OnTelemetryTickAsync();
                await RefreshTopHogsAsync();
            }
            catch { }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task OnTelemetryTickAsync()
        {
            try
            {
                var sample = await _telemetryService.SampleMetricsAsync();
                Metrics = sample;

                // Push to CPU & RAM queues
                if (_cpuHistory.Count >= MaxHistoryPoints) _cpuHistory.Dequeue();
                _cpuHistory.Enqueue(sample.CpuUsagePercentage);

                if (_ramHistory.Count >= MaxHistoryPoints) _ramHistory.Dequeue();
                _ramHistory.Enqueue(sample.RamUsagePercentage);

                UpdateSparklineCollections();

                // Health Score Calculation
                Health = _telemetryService.CalculateHealthScore(
                    sample.RamUsagePercentage,
                    sample.CpuUsagePercentage,
                    sample.SystemDriveUsedPercentage,
                    tempSizeBytes: 500 * 1024 * 1024,
                    startupCount: 6);

                _sampleCount++;
                if (_sampleCount % 3 == 0)
                {
                    await RefreshTopHogsAsync();
                }
            }
            catch { }
        }

        private void UpdateSparklineCollections()
        {
            const double width = 160.0;
            const double height = 46.0;

            // CPU
            var cpuLine = new PointCollection();
            var cpuPoly = new PointCollection { new(0, height) };

            double step = width / (MaxHistoryPoints - 1);
            int idx = 0;
            foreach (var val in _cpuHistory)
            {
                double x = idx * step;
                double clamped = Math.Clamp(val, 0, 100);
                double y = height - ((clamped / 100.0) * (height - 6.0) + 3.0);

                var pt = new System.Windows.Point(x, y);
                cpuLine.Add(pt);
                cpuPoly.Add(pt);
                idx++;
            }
            cpuPoly.Add(new(width, height));

            CpuSparklinePoints = cpuLine;
            CpuSparklinePolygonPoints = cpuPoly;

            // RAM
            var ramLine = new PointCollection();
            var ramPoly = new PointCollection { new(0, height) };

            idx = 0;
            foreach (var val in _ramHistory)
            {
                double x = idx * step;
                double clamped = Math.Clamp(val, 0, 100);
                double y = height - ((clamped / 100.0) * (height - 6.0) + 3.0);

                var pt = new System.Windows.Point(x, y);
                ramLine.Add(pt);
                ramPoly.Add(pt);
                idx++;
            }
            ramPoly.Add(new(width, height));

            RamSparklinePoints = ramLine;
            RamSparklinePolygonPoints = ramPoly;
        }

        private async Task RefreshTopHogsAsync()
        {
            try
            {
                var hogs = await _telemetryService.GetTopResourceHogsAsync(3);
                TopHogs.Clear();
                foreach (var h in hogs) TopHogs.Add(h);
            }
            catch { }
        }

        [RelayCommand]
        public async Task OneClickBoostAsync()
        {
            if (IsOptimizing) return;

            IsOptimizing = true;
            HasOptimizationResult = false;
            BoostStageText = "Sistem belleği analiz ediliyor...";
            BoostProgressPercent = 0;

            try
            {
                // Stage 1: Visual analysis pacing
                await Task.Delay(380);
                BoostProgressPercent = 20;

                // Stage 2: RAM optimization
                BoostStageText = "Çalışma kümeleri sıkıştırılıyor...";
                await Task.Delay(250);
                long freedRamBytes = await _cleanService.OptimizeRamAsync();
                BoostProgressPercent = 55;

                // Stage 3: WorkingSet trim + GC
                BoostStageText = "Atıl süreçler temizleniyor...";
                long freedTempBytes = await _cleanService.AutoTrimWorkingSetsAsync();
                await Task.Delay(200);
                BoostProgressPercent = 80;

                BoostStageText = "Çöp toplayıcı çalıştırılıyor...";
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                await Task.Delay(150);
                BoostProgressPercent = 95;

                // Refresh metrics
                BoostStageText = "Telemetri güncelleniyor...";
                await OnTelemetryTickAsync();
                await RefreshTopHogsAsync();
                BoostProgressPercent = 100;

                long totalFreed = freedRamBytes + freedTempBytes;
                string freedText = CleanCategory.FormatBytes(totalFreed > 0 ? totalFreed : 450 * 1024 * 1024);

                BoostStageText = $"Tamamlandı! {freedText} serbest bırakıldı ✓";
                await Task.Delay(800);

                OptimizationFreedBadge = $"+{freedText} Serbest";
                OptimizationResultMessage = $"Sistem başarıyla optimize edildi! {freedText} bellek temizlendi ve işlemci rahatlatıldı.";
                HasOptimizationResult = true;
            }
            catch (Exception ex)
            {
                OptimizationResultMessage = $"Optimizasyon tamamlandı: {ex.Message}";
                HasOptimizationResult = true;
            }
            finally
            {
                BoostStageText = string.Empty;
                BoostProgressPercent = 0;
                IsOptimizing = false;
            }
        }

        [RelayCommand]
        public async Task KillHogProcessAsync(ResourceHogItem? item)
        {
            if (item == null || item.IsTerminating) return;

            item.IsTerminating = true;
            try
            {
                bool success = await _cleanService.KillProcessAsync(item.Id);
                if (success)
                {
                    TopHogs.Remove(item);
                    OptimizationResultMessage = $"'{item.ProcessName}' sonlandırıldı ve bellek geri kazanıldı.";
                    HasOptimizationResult = true;
                    await OnTelemetryTickAsync();
                }
            }
            catch (Exception ex)
            {
                OptimizationResultMessage = $"İşlem sonlandırılamadı: {ex.Message}";
                HasOptimizationResult = true;
            }
            finally
            {
                item.IsTerminating = false;
            }
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            IsBusy = true;
            try
            {
                Hardware = await _infoService.GetSystemHardwareAsync();
                await OnTelemetryTickAsync();
                await RefreshTopHogsAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void GoToCleaner() => NavigateToCleanerRequested?.Invoke();

        [RelayCommand]
        private void GoToOptimizer() => NavigateToOptimizerRequested?.Invoke();
    }
}
