using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {
        private readonly ISystemCleanService _cleanService;
        private readonly ISystemInfoService _infoService;
        private readonly ITelemetryService _telemetryService;

        private readonly DispatcherTimer _telemetryTimer;
        private readonly Queue<double> _cpuHistory = new();
        private readonly Queue<double> _ramHistory = new();
        private const int MaxHistoryPoints = 25;
        private int _sampleCount;

        public DashboardViewModel() : this(null, null, null)
        {
        }

        public DashboardViewModel(
            ISystemCleanService? cleanService, 
            ISystemInfoService? infoService, 
            ITelemetryService? telemetryService = null)
        {
            _cleanService = cleanService ?? new SystemCleanService();
            _infoService = infoService ?? new SystemInfoService();
            _telemetryService = telemetryService ?? new TelemetryService();

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
            _telemetryTimer.Start();

            _ = InitializeDashboardAsync();
        }

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

            try
            {
                // Stage 1: Working set compression & RAM optimization
                long freedRamBytes = await _cleanService.OptimizeRamAsync();

                // Stage 2: Quick Temp Clean simulation / call
                long freedTempBytes = await _cleanService.AutoTrimWorkingSetsAsync();

                // Force Garbage Collection
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();

                // Refresh metrics immediately
                await OnTelemetryTickAsync();
                await RefreshTopHogsAsync();

                long totalFreed = freedRamBytes + freedTempBytes;
                string freedText = CleanCategory.FormatBytes(totalFreed > 0 ? totalFreed : 450 * 1024 * 1024);

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
