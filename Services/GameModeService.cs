using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Bakım.Services
{
    public class GameModeService : IGameModeService
    {
        private readonly IBackgroundMaintenanceService _backgroundMaintenance;
        private readonly ISystemCleanService _cleanService;
        private readonly ILogService _log;

        private bool _isGameModeActive;

        public GameModeService(
            IBackgroundMaintenanceService backgroundMaintenance,
            ISystemCleanService cleanService,
            ILogService log)
        {
            _backgroundMaintenance = backgroundMaintenance;
            _cleanService = cleanService;
            _log = log ?? NullLogService.Instance;
        }

        public bool IsGameModeActive => _isGameModeActive;

        public event Action<bool>? GameModeChanged;

        public async Task<long> EnableGameModeAsync()
        {
            if (_isGameModeActive) return 0;

            _isGameModeActive = true;
            _backgroundMaintenance.IsGameModeActive = true;

            _log.Info("Ultra Oyun Modu devreye alındı. Arka plan servisleri donduruldu.", nameof(GameModeService));

            // 1. Windows Yüksek / Nihai Performans Güç Planını tetikle
            TrySetPowerScheme("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"); // Yüksek Performans GUID

            // 2. Oyun öncesi derin bellek boşaltması yap
            long freedBytes = 0;
            try
            {
                freedBytes = await _cleanService.AutoTrimWorkingSetsAsync();
                _log.Info($"Oyun Modu öncesi {freedBytes / (1024 * 1024)} MB RAM serbest bırakıldı.", nameof(GameModeService));
            }
            catch (Exception ex)
            {
                _log.Warning("Oyun Modu bellek optimizasyonunda hata.", ex, nameof(GameModeService));
            }

            GameModeChanged?.Invoke(true);
            return freedBytes;
        }

        public void DisableGameMode()
        {
            if (!_isGameModeActive) return;

            _isGameModeActive = false;
            _backgroundMaintenance.IsGameModeActive = false;

            // Dengeli Güç Planına geri dön
            TrySetPowerScheme("381b4222-f694-41f0-9685-ff5bb260df2e"); // Dengeli GUID

            _log.Info("Oyun Modu kapatıldı. Arka plan servisleri normale döndü.", nameof(GameModeService));
            GameModeChanged?.Invoke(false);
        }

        public async Task<long> ToggleGameModeAsync()
        {
            if (_isGameModeActive)
            {
                DisableGameMode();
                return 0;
            }
            else
            {
                return await EnableGameModeAsync();
            }
        }

        private void TrySetPowerScheme(string schemeGuid)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = $"/setactive {schemeGuid}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(1000);
            }
            catch (Exception ex)
            {
                _log.Debug($"Güç planı değiştirilemedi: {ex.Message}", nameof(GameModeService));
            }
        }
    }
}
