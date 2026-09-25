using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Core.Health;
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
        private readonly IHealthSignalsService _healthSignals;
        private readonly IQuickMaintenanceService _quickMaintenance;
        private readonly Services.Activity.IActivityService _activity;
        private readonly INavigationService _navigation;

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
            IAppSettingsService settingsService,
            IHealthSignalsService healthSignals,
            IQuickMaintenanceService quickMaintenance,
            Services.Activity.IActivityService activity,
            INavigationService navigation)
        {
            _healthSignals = healthSignals;
            _quickMaintenance = quickMaintenance;
            _activity = activity;
            _navigation = navigation;
            _activity.Changed += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_isActive) LoadRecentActivity();
            });
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
            _telemetryTimer.Tick += Helpers.AsyncTick.Guarded(OnTelemetryTickAsync, nameof(DashboardViewModel));
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

        public string GameModeStatusBadge => IsGameModeActive ? "Açık" : "Kapalı";

        public string GameModeButtonText => IsGameModeActive ? "Kapat" : "Aç";

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
                OptimizationResultMessage = _gameModeService.LastActionSummary;
                OptimizationFreedBadge = Bakım.Core.Text.MemoryResultText.Badge(freed);
                HasOptimizationResult = true;
            }
            else
            {
                OptimizationResultMessage = _gameModeService.LastActionSummary;
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
                LoadRecentActivity();
                var health = RefreshHealthAsync(forceRefresh: false);
                Hardware = await _infoService.GetSystemHardwareAsync();
                await OnTelemetryTickAsync();
                await RefreshTopHogsAsync();
                await health;
            }
            catch (Exception ex)
            {
                AppLog.Warning("Pano verileri yüklenemedi.", ex, nameof(DashboardViewModel));
            }
            finally
            {
                IsBusy = false;
            }
        }

        #region Sağlık (§5.1)

        public ObservableCollection<HealthRow> HealthRows { get; } = new();

        [ObservableProperty] private int _healthScoreValue = 100;
        [ObservableProperty] private string _healthTitle = "Sağlık denetleniyor…";
        [ObservableProperty] private string _healthSummary = string.Empty;
        [ObservableProperty] private Intent _healthIntent = Intent.Neutral;
        [ObservableProperty] private bool _isHealthLoading;

        private async Task RefreshHealthAsync(bool forceRefresh)
        {
            IsHealthLoading = true;
            try
            {
                var report = HealthScore.Evaluate(await _healthSignals.GetAsync(forceRefresh));
                HealthScoreValue = report.Score;
                HealthTitle = report.Title;
                HealthSummary = report.Summary;
                HealthIntent = report.Level switch
                {
                    HealthLevel.Good => Intent.Success,
                    HealthLevel.Attention => Intent.Caution,
                    HealthLevel.Problem => Intent.Critical,
                    _ => Intent.Neutral
                };
                HealthRows.Clear();
                // Önce puanı düşürenler, sonra iyi ve bilinmeyenler.
                foreach (var c in report.Components.OrderByDescending(c => c.Deduction).ThenBy(c => c.Level == HealthLevel.Unknown))
                    HealthRows.Add(new HealthRow(c));
            }
            catch (Exception ex)
            {
                AppLog.Warning("Sağlık sinyalleri okunamadı.", ex, nameof(DashboardViewModel));
                HealthTitle = "Sağlık denetlenemedi";
                HealthSummary = ex.Message;
                HealthIntent = Intent.Neutral;
            }
            finally
            {
                IsHealthLoading = false;
            }
        }

        [RelayCommand]
        private async Task RefreshHealth() => await RefreshHealthAsync(forceRefresh: true);

        [RelayCommand]
        private void OpenHealthAction(HealthRow? row)
        {
            if (row == null) return;
            if (row.Component.Key == "defender")
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("windowsdefender:") { UseShellExecute = true })?.Dispose();
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    AppLog.Warning("Windows Güvenliği açılamadı.", ex, nameof(DashboardViewModel));
                }
                return;
            }
            if (row.Component.DeepLink is { Length: > 0 } link) _navigation.Navigate(link);
        }

        #endregion

        #region Son etkinlikler

        public ObservableCollection<ActivityRow> RecentActivity { get; } = new();

        [ObservableProperty] private bool _hasRecentActivity;

        private void LoadRecentActivity()
        {
            var now = DateTime.Now;
            RecentActivity.Clear();
            foreach (var e in _activity.Entries.Where(e => e.Kind != Core.Activity.ActivityKind.Restore).Take(5))
                RecentActivity.Add(new ActivityRow(e, now));
            HasRecentActivity = RecentActivity.Count > 0;
        }

        [RelayCommand]
        private void OpenActivityCenter() => _navigation.Navigate("Activity");

        [RelayCommand]
        private void OpenGameModePage() => _navigation.Navigate("GameMode");

        #endregion

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

                // Sağlık puanı anlık CPU/RAM'den hesaplanmaz (§5.1); kalıcı sinyaller ayrı ve seyrek tazelenir.
                // (Eskiden sabit "500 MB geçici dosya" ve "6 başlangıç programı" girdileriyle uydurma bir puan üretiliyordu.)

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

        /// <summary>
        /// "Hızlı Bakım" (§5.1): güvenli, ölçülebilir temizlik + DNS önbelleği. İlerleme gerçek adım
        /// sayısıdır; sonuç ölçümdür ve Etkinlik Merkezi'ne yazılır. (Eskiden "Tek Tıkla Optimize Et"
        /// yalnızca RAM kırpıyordu; etkisi geçici olduğu için bu akıştan çıkarıldı.)
        /// </summary>
        [RelayCommand]
        public async Task QuickMaintenanceAsync()
        {
            if (IsOptimizing) return;

            IsOptimizing = true;
            HasOptimizationResult = false;
            BoostStageText = "Hazırlanıyor…";
            BoostProgressPercent = 0;
            _maintenanceCts = new CancellationTokenSource();

            try
            {
                var progress = new Progress<QuickMaintenanceProgress>(p =>
                {
                    BoostStageText = p.Stage;
                    BoostProgressPercent = p.Percent;
                });
                var result = await _quickMaintenance.RunAsync(progress, _maintenanceCts.Token);

                OptimizationFreedBadge = Core.Text.ByteFormatter.Format(result.BytesFreed);
                OptimizationResultMessage = result.Cancelled ? "Hızlı Bakım iptal edildi. " + result.Summary : result.Summary;
                MaintenanceDetails.Clear();
                foreach (var d in result.Details) MaintenanceDetails.Add(d);
                LastMaintenanceDeleted = result.FilesDeleted;
                LastMaintenanceSkipped = result.FilesSkipped;
                HasOptimizationResult = true;

                await RefreshHealthAsync(forceRefresh: false);
            }
            catch (Exception ex)
            {
                AppLog.Error("Hızlı Bakım başarısız.", ex, nameof(DashboardViewModel));
                OptimizationResultMessage = $"İşlem tamamlanamadı: {ex.Message}";
                HasOptimizationResult = true;
            }
            finally
            {
                _maintenanceCts?.Dispose();
                _maintenanceCts = null;
                BoostStageText = string.Empty;
                BoostProgressPercent = 0;
                IsOptimizing = false;
            }
        }

        private CancellationTokenSource? _maintenanceCts;

        public ObservableCollection<string> MaintenanceDetails { get; } = new();
        [ObservableProperty] private int _lastMaintenanceDeleted;
        [ObservableProperty] private int _lastMaintenanceSkipped;

        [RelayCommand]
        private void CancelMaintenance() => _maintenanceCts?.Cancel();

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

namespace Bakım.ViewModels
{
    /// <summary>Sağlık kartındaki bir satır (§5.1).</summary>
    public sealed class HealthRow
    {
        public HealthRow(Bakım.Core.Health.HealthComponent component) => Component = component;

        public Bakım.Core.Health.HealthComponent Component { get; }
        public string Title => Component.Title;
        public string Detail => Component.Detail;
        public bool HasAction => !string.IsNullOrEmpty(Component.ActionText) || Component.Level == Bakım.Core.Health.HealthLevel.Good && Component.DeepLink != null;
        public string ActionText => Component.ActionText ?? "Aç";
        public string DeductionText => Component.Deduction > 0 ? $"−{Component.Deduction}" : string.Empty;
        public bool HasDeduction => Component.Deduction > 0;

        public Models.Intent Intent => Component.Level switch
        {
            Bakım.Core.Health.HealthLevel.Good => Models.Intent.Success,
            Bakım.Core.Health.HealthLevel.Attention => Models.Intent.Caution,
            Bakım.Core.Health.HealthLevel.Problem => Models.Intent.Critical,
            _ => Models.Intent.Neutral
        };

        public string StatusText => Component.Level switch
        {
            Bakım.Core.Health.HealthLevel.Good => "İyi",
            Bakım.Core.Health.HealthLevel.Attention => "Dikkat",
            Bakım.Core.Health.HealthLevel.Problem => "Sorun",
            _ => "Bilinmiyor"
        };
    }
}
