using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly IAppSettingsService? _settings;

        private bool _isGameModeActive;
        private readonly List<int> _suspendedByGameMode = new();
        private System.Threading.Timer? _autoTimer;
        private bool _autoEnabled;
        private int _autoTickRunning;

        public GameModeService(
            IBackgroundMaintenanceService backgroundMaintenance,
            ISystemCleanService cleanService,
            ILogService log,
            IAppSettingsService? settings = null)
        {
            _backgroundMaintenance = backgroundMaintenance;
            _cleanService = cleanService;
            _log = log ?? NullLogService.Instance;
            _settings = settings;
        }

        private const string UltimateScheme = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        /// <summary>"OneDrive, Teams.exe" → {"OneDrive","Teams"}. Kural tek yerde: <see cref="Core.GameMode.ProcessNameList"/>.</summary>
        public static IReadOnlyList<string> ParseProcessList(string? text) => Core.GameMode.ProcessNameList.Parse(text);

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

            var profile = _settings?.Current;
            string planChoice = profile?.GameModePowerPlan ?? "HighPerformance";
            string planText;
            if (string.Equals(planChoice, "Keep", StringComparison.OrdinalIgnoreCase))
            {
                planText = "Güç planına dokunulmadı. ";
            }
            else
            {
                bool ultimate = string.Equals(planChoice, "Ultimate", StringComparison.OrdinalIgnoreCase);
                string target = ultimate ? UltimateScheme : HighPerformanceScheme;
                bool applied = string.Equals(previous, target, StringComparison.OrdinalIgnoreCase) || TrySetPowerScheme(target);
                if (!applied && ultimate)
                {
                    // Nihai Performans planı çoğu cihazda gizli/yoktur: Yüksek Performans'a düşülür.
                    applied = TrySetPowerScheme(HighPerformanceScheme);
                    planText = applied ? "Nihai Performans planı yok; Yüksek Performans etkin. " : "Performans güç planı uygulanamadı. ";
                }
                else
                {
                    planText = applied
                        ? (ultimate ? "Nihai Performans güç planı etkin. " : "Yüksek Performans güç planı etkin. ")
                        : "Yüksek Performans güç planı bu cihazda yok ya da uygulanamadı. ";
                }
            }

            // Kullanıcının seçtiği arka plan uygulamaları askıya alınır (defter sayesinde her koşulda devam ettirilir).
            int suspended = 0;
            foreach (var name in ParseProcessList(profile?.GameModeSuspendApps))
            {
                var processes = Process.GetProcessesByName(name);
                try
                {
                    foreach (var p in processes)
                    {
                        if (await _cleanService.SuspendProcessAsync(p.Id))
                        {
                            lock (_suspendedByGameMode) _suspendedByGameMode.Add(p.Id);
                            suspended++;
                        }
                    }
                }
                finally
                {
                    foreach (var p in processes) p.Dispose();
                }
            }

            long freedBytes = 0;
            if (profile?.GameModeTrimMemory ?? true)
            {
                try
                {
                    freedBytes = await _cleanService.AutoTrimWorkingSetsAsync();
                }
                catch (Exception ex)
                {
                    _log.Warning("Oyun Modu bellek işleminde hata.", ex, nameof(GameModeService));
                }
            }

            LastActionSummary = planText +
                "Bakım'ın arka plan işleri duraklatıldı. " +
                (suspended > 0 ? $"{suspended} arka plan süreci askıya alındı. " : "") +
                (profile?.GameModeTrimMemory ?? true ? Bakım.Core.Text.MemoryResultText.Describe(freedBytes) : "");

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

            int resumed = 0;
            List<int> pids;
            lock (_suspendedByGameMode)
            {
                pids = _suspendedByGameMode.ToList();
                _suspendedByGameMode.Clear();
            }
            if (pids.Count > 0)
            {
                // DisableGameMode UI iş parçacığından çağrılabilir: async çağrıyı arka planda bekle,
                // yoksa await sonrası devam UI bağlamına dönmeye çalışır ve kilitlenir.
                resumed = Task.Run(async () =>
                {
                    int ok = 0;
                    foreach (var pid in pids)
                        if (await _cleanService.ResumeProcessAsync(pid).ConfigureAwait(false)) ok++;
                    return ok;
                }).GetAwaiter().GetResult();
            }

            LastActionSummary = (restored
                ? "Önceki güç planı geri yüklendi; Bakım'ın arka plan işleri devam ediyor."
                : "Önceki güç planı geri yüklenemedi; Windows güç ayarlarından kontrol edin.") +
                (pids.Count > 0 ? $" {resumed}/{pids.Count} askıya alınan süreç devam ettirildi." : "");

            RecordSession(state?.ActivatedAtUtc, restored);

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

        /// <summary>Oturumu Etkinlik Merkezi'ne yazar: süre ve güç planının geri gelip gelmediği.</summary>
        private static void RecordSession(DateTime? activatedAtUtc, bool planRestored)
        {
            var activity = App.TryGetService<Activity.IActivityService>();
            if (activity == null) return;

            string duration = activatedAtUtc is { } start ? Core.Text.DurationText.Describe(DateTime.UtcNow - start) : "süre bilinmiyor";
            Activity.ActivityRecording.RecordSimple(activity, Core.Activity.ActivityKind.GameModeSession, "Oyun Modu",
                "Oyun Modu oturumu", $"{duration} · " + (planRestored ? "önceki güç planı geri yüklendi" : "güç planı geri yüklenemedi"),
                planRestored ? Core.Activity.ActivityOutcome.Succeeded : Core.Activity.ActivityOutcome.PartiallySucceeded,
                deepLink: "GameMode");
        }

        public void StartAutoTrigger()
        {
            if (_settings == null || _autoTimer != null) return;
            _autoTimer = new System.Threading.Timer(_ => _ = AutoTickAsync(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Listedeki bir oyun çalışıyorsa Oyun Modu'nu açar; hepsi kapanınca (yalnızca otomatik açtıysa) kapatır.
        /// Kullanıcı elle açtıysa otomatik kapatılmaz.
        /// </summary>
        private async Task AutoTickAsync()
        {
            if (System.Threading.Interlocked.Exchange(ref _autoTickRunning, 1) == 1) return;
            try
            {
                var profile = _settings!.Current;
                if (!profile.GameModeAutoStart) return;
                var games = ParseProcessList(profile.GameModeAutoStartExes);
                if (games.Count == 0) return;

                bool anyRunning = games.Any(name =>
                {
                    var ps = Process.GetProcessesByName(name);
                    try { return ps.Length > 0; }
                    finally { foreach (var p in ps) p.Dispose(); }
                });

                if (anyRunning && !_isGameModeActive)
                {
                    _autoEnabled = true;
                    _log.Info("Listedeki oyun başladı; Oyun Modu otomatik açılıyor.", nameof(GameModeService));
                    await EnableGameModeAsync();
                }
                else if (!anyRunning && _isGameModeActive && _autoEnabled)
                {
                    _autoEnabled = false;
                    _log.Info("Listedeki oyunlar kapandı; Oyun Modu otomatik kapatılıyor.", nameof(GameModeService));
                    DisableGameMode();
                }
            }
            catch (Exception ex)
            {
                _log.Warning("Oyun Modu otomatik tetikleme denetimi başarısız.", ex, nameof(GameModeService));
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _autoTickRunning, 0);
            }
        }

        public async Task<long> ToggleGameModeAsync()
        {
            _autoEnabled = false; // elle değiştirildi: otomatik kapatma devreden çıkar
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
