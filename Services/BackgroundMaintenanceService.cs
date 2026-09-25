using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bakım.Services
{
    public interface IBackgroundMaintenanceService
    {
        void Start();
        void Stop();
        bool IsGameModeActive { get; set; }

        /// <summary>RAM kullanımı eşiği aştığında tetiklenir (yüzde değeri taşır).</summary>
        event Action<int>? HighRamDetected;

        /// <summary>Zamanlanmış otomatik RAM temizliği bittiğinde tetiklenir (kazanılan bayt).</summary>
        event Action<long>? AutoRamCleanCompleted;

        /// <summary>Haftalık güvenli temizlik bitti (sonuç özeti). §5.2</summary>
        event Action<string>? ScheduledCleanupCompleted;
    }

    /// <summary>
    /// Ayarlar ekranındaki "Otomatik RAM Temizleme" ve "Yüksek RAM Uyarısı" tercihlerini
    /// gerçekten çalıştıran arka plan motoru. Bu tercihler v3.0.3'e kadar kaydediliyor
    /// fakat hiçbir kod tarafından okunmuyordu.
    ///
    /// Döngü UI thread'inde değil, arka planda çalışır; olaylar abonelere ham olarak iletilir
    /// ve arayüze geçiş sorumluluğu abonelerdedir.
    /// </summary>
    public sealed class BackgroundMaintenanceService : IBackgroundMaintenanceService, IDisposable
    {
        private const int HighRamThresholdPercent = 85;
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan HighRamNotifyCooldown = TimeSpan.FromMinutes(15);

        private static readonly TimeSpan ScheduledCleanupInterval = TimeSpan.FromDays(7);

        private readonly ISystemCleanService _cleanService;
        private readonly IAppSettingsService _settings;
        private readonly IQuickMaintenanceService _quickMaintenance;
        private readonly ILogService _log;
        private readonly object _gate = new();

        private CancellationTokenSource? _cts;
        private Task? _loop;
        private DateTime _lastAutoCleanUtc = DateTime.UtcNow;
        private DateTime _lastHighRamNotifyUtc = DateTime.MinValue;
        private bool _disposed;

        public BackgroundMaintenanceService(
            ISystemCleanService cleanService,
            IAppSettingsService settings,
            ILogService log,
            IQuickMaintenanceService quickMaintenance)
        {
            _quickMaintenance = quickMaintenance;
            _cleanService = cleanService;
            _settings = settings;
            _log = log ?? NullLogService.Instance;
        }

        public bool IsGameModeActive { get; set; }
        public event Action<int>? HighRamDetected;
        public event Action<long>? AutoRamCleanCompleted;
        public event Action<string>? ScheduledCleanupCompleted;

        public void Start()
        {
            lock (_gate)
            {
                if (_disposed || _loop != null) return;

                _cts = new CancellationTokenSource();
                _lastAutoCleanUtc = DateTime.UtcNow;
                _loop = Task.Run(() => RunLoopAsync(_cts.Token));
            }

            _log.Info("Arka plan bakım motoru başlatıldı.", nameof(BackgroundMaintenanceService));
        }

        public void Stop()
        {
            CancellationTokenSource? cts;
            Task? loop;

            lock (_gate)
            {
                cts = _cts;
                loop = _loop;
                _cts = null;
                _loop = null;
            }

            if (cts == null) return;

            try
            {
                cts.Cancel();
                // Kapanışta kilitlenmemek için kısa bir bekleme penceresi
                loop?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // İptal kaynaklı istisnalar beklenen durumdur
            }
            catch (Exception ex)
            {
                _log.Warning("Arka plan bakım motoru durdurulurken hata.", ex, nameof(BackgroundMaintenanceService));
            }
            finally
            {
                cts.Dispose();
            }

            _log.Info("Arka plan bakım motoru durduruldu.", nameof(BackgroundMaintenanceService));
        }

        private async Task RunLoopAsync(CancellationToken token)
        {
            using var timer = new PeriodicTimer(TickInterval);

            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    await TickAsync(token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal kapanış
            }
            catch (Exception ex)
            {
                _log.Error("Arka plan bakım döngüsü beklenmedik şekilde sonlandı.", ex, nameof(BackgroundMaintenanceService));
            }
        }

        private async Task TickAsync(CancellationToken token)
        {
            if (IsGameModeActive) return; // Ultra Oyun Modunda arka plan denetimleri dondurulur

            var settings = _settings.Current;

            // --- 1. Yüksek RAM uyarısı ---
            try
            {
                if (settings.NotifyOnHighRam)
                {
                    var stats = await _cleanService.GetSystemStatsAsync().ConfigureAwait(false);

                    if (stats.RamUsagePercentage >= HighRamThresholdPercent &&
                        DateTime.UtcNow - _lastHighRamNotifyUtc >= HighRamNotifyCooldown)
                    {
                        _lastHighRamNotifyUtc = DateTime.UtcNow;
                        _log.Info($"Yüksek RAM kullanımı saptandı: %{stats.RamUsagePercentage}", nameof(BackgroundMaintenanceService));

                        Raise(() => HighRamDetected?.Invoke(stats.RamUsagePercentage));
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warning("RAM durumu okunamadı.", ex, nameof(BackgroundMaintenanceService));
            }

            if (token.IsCancellationRequested) return;

            // --- 2. Haftalık güvenli temizlik (§5.2) ---
            await RunScheduledCleanupIfDueAsync(settings, token).ConfigureAwait(false);
            if (token.IsCancellationRequested) return;

            // --- 3. Zamanlanmış otomatik RAM temizliği ---
            try
            {
                int intervalMinutes = settings.AutoRamCleanIntervalMinutes;
                if (intervalMinutes <= 0) return;

                if (DateTime.UtcNow - _lastAutoCleanUtc < TimeSpan.FromMinutes(intervalMinutes)) return;

                _lastAutoCleanUtc = DateTime.UtcNow;

                long freed = await _cleanService.AutoTrimWorkingSetsAsync().ConfigureAwait(false);

                _log.Info(
                    $"Otomatik RAM temizliği tamamlandı, kazanılan: {Models.CleanCategory.FormatBytes(freed)}",
                    nameof(BackgroundMaintenanceService));

                // 16 MB altı ölçüm gürültüsüdür; "boşaltıldı" bildirimi göstermeye değmez.
                if (Core.Text.MemoryResultText.IsSignificant(freed))
                {
                    Raise(() => AutoRamCleanCompleted?.Invoke(freed));
                }
            }
            catch (Exception ex)
            {
                _log.Error("Otomatik RAM temizliği başarısız oldu.", ex, nameof(BackgroundMaintenanceService));
            }
        }

        /// <summary>
        /// Haftada bir, yalnızca Hızlı Bakım'ın güvenli kategorileri (yaş filtreli temp, önbellekler).
        /// Son çalışma ayarlara yazılır; uygulama kapalıyken kaçırılan hafta açılışta bir kez çalışır.
        /// </summary>
        private async Task RunScheduledCleanupIfDueAsync(Models.AppSettingsData settings, CancellationToken token)
        {
            if (!settings.WeeklySafeCleanup) return;
            var last = settings.LastScheduledCleanupUtc ?? DateTime.MinValue;
            if (DateTime.UtcNow - last < ScheduledCleanupInterval) return;

            try
            {
                // Önce zamanı yaz: temizlik yarıda kesilse bile döngü her dakika yeniden denemesin.
                _settings.Update(d => d.LastScheduledCleanupUtc = DateTime.UtcNow);
                var result = await _quickMaintenance.RunAsync(null, token, QuickMaintenanceOrigin.Scheduled).ConfigureAwait(false);
                _log.Info("Haftalık güvenli temizlik: " + result.Summary, nameof(BackgroundMaintenanceService));
                Raise(() => ScheduledCleanupCompleted?.Invoke(result.Summary));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log.Error("Haftalık güvenli temizlik başarısız oldu.", ex, nameof(BackgroundMaintenanceService));
            }
        }

        /// <summary>Abone hataları bakım döngüsünü öldürmemeli.</summary>
        private void Raise(Action invoke)
        {
            try
            {
                invoke();
            }
            catch (Exception ex)
            {
                _log.Warning("Bakım olayı abonesi hata verdi.", ex, nameof(BackgroundMaintenanceService));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
        }
    }
}
