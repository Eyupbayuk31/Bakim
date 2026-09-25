using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
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

        public string LastActionSummary { get; private set; } = string.Empty;

        private const string HighPerformanceScheme = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        private const string BalancedScheme = "381b4222-f694-41f0-9685-ff5bb260df2e";

        private static readonly string StateFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bakim", "gamemode-state.json");

        private sealed record GameModeState(string? PreviousScheme, DateTime ActivatedAtUtc);

        public async Task<long> EnableGameModeAsync()
        {
            if (_isGameModeActive) return 0;

            // Önceki güç planı kaydedilir: kapatınca birebir geri yüklenir. Diske de
            // yazılır; Bakım çökerse açılışta RecoverInterruptedSession onarır.
            // (Eskiden kapatınca her zaman "Dengeli"ye dönülüyordu.)
            string? previous = GetActiveSchemeGuid();
            SaveState(new GameModeState(previous, DateTime.UtcNow));

            _isGameModeActive = true;
            _backgroundMaintenance.IsGameModeActive = true;

            bool planApplied = !string.Equals(previous, HighPerformanceScheme, StringComparison.OrdinalIgnoreCase)
                ? TrySetPowerScheme(HighPerformanceScheme)
                : true;

            long freedBytes = 0;
            try
            {
                freedBytes = await _cleanService.AutoTrimWorkingSetsAsync();
            }
            catch (Exception ex)
            {
                _log.Warning("Oyun Modu bellek işleminde hata.", ex, nameof(GameModeService));
            }

            LastActionSummary = (planApplied
                    ? "Yüksek Performans güç planı etkin. "
                    : "Yüksek Performans güç planı bu cihazda yok ya da uygulanamadı. ") +
                "Bakım'ın arka plan işleri duraklatıldı. " +
                Bakım.Core.Text.MemoryResultText.Describe(freedBytes);

            _log.Info("Oyun Modu açıldı: " + LastActionSummary, nameof(GameModeService));
            GameModeChanged?.Invoke(true);
            return freedBytes;
        }

        public void DisableGameMode()
        {
            if (!_isGameModeActive) return;

            _isGameModeActive = false;
            _backgroundMaintenance.IsGameModeActive = false;

            var state = LoadState();
            string target = state?.PreviousScheme ?? BalancedScheme;
            bool restored = TrySetPowerScheme(target);
            DeleteState();

            LastActionSummary = restored
                ? "Önceki güç planı geri yüklendi; Bakım'ın arka plan işleri devam ediyor."
                : "Önceki güç planı geri yüklenemedi; Windows güç ayarlarından kontrol edin.";

            _log.Info("Oyun Modu kapatıldı: " + LastActionSummary, nameof(GameModeService));
            GameModeChanged?.Invoke(false);
        }

        public void RecoverInterruptedSession()
        {
            if (_isGameModeActive) return;
            var state = LoadState();
            if (state == null) return;

            _log.Warning("Önceki oturumda Oyun Modu kapatılmadan uygulama sonlanmış; güç planı geri yükleniyor.", null, nameof(GameModeService));
            TrySetPowerScheme(state.PreviousScheme ?? BalancedScheme);
            DeleteState();
        }

        public async Task<long> ToggleGameModeAsync()
        {
            if (_isGameModeActive)
            {
                DisableGameMode();
                return 0;
            }
            return await EnableGameModeAsync();
        }

        private string? GetActiveSchemeGuid()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "/getactivescheme",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using var p = Process.Start(psi);
                if (p == null) return null;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                var m = System.Text.RegularExpressions.Regex.Match(output, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                return m.Success ? m.Value.ToLowerInvariant() : null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                _log.Debug($"Etkin güç planı okunamadı: {ex.Message}", nameof(GameModeService));
                return null;
            }
        }

        private bool TrySetPowerScheme(string schemeGuid)
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
                if (p == null) return false;
                if (!p.WaitForExit(5000)) return false;
                return p.ExitCode == 0;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                _log.Debug($"Güç planı değiştirilemedi: {ex.Message}", nameof(GameModeService));
                return false;
            }
        }

        private void SaveState(GameModeState state)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
                File.WriteAllText(StateFile, JsonSerializer.Serialize(state));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warning("Oyun Modu durumu kaydedilemedi.", ex, nameof(GameModeService));
            }
        }

        private GameModeState? LoadState()
        {
            try
            {
                return File.Exists(StateFile) ? JsonSerializer.Deserialize<GameModeState>(File.ReadAllText(StateFile)) : null;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _log.Warning("Oyun Modu durumu okunamadı.", ex, nameof(GameModeService));
                return null;
            }
        }

        private void DeleteState()
        {
            try { if (File.Exists(StateFile)) File.Delete(StateFile); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Debug($"Oyun Modu durum dosyası silinemedi: {ex.Message}", nameof(GameModeService));
            }
        }
    }
}
