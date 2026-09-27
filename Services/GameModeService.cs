using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Core.GameMode;
using Bakım.Services.GameMode;

namespace Bakım.Services
{
    /// <summary>
    /// Oyun Modu v2 (docs/OYUN_MODU_TASARIM_PLANI.md). Yalnızca ölçülebilir etkisi olanlar:
    ///   • güç planı (önceki plan kaydedilir),
    ///   • Windows'un kendi Oyun Modu (kapalıysa oturum boyunca açılır),
    ///   • oyun "Normalin üstü", arka plan uygulamaları "Normalin altı" öncelik,
    ///   • bellek sıkışınca (≥ %85) yalnızca arka plan uygulamalarının çalışma kümesi kırpılır,
    ///   • seçili uygulamalar askıya alınır, Bakım'ın arka plan işleri duraklar.
    /// v1 her açılışta TÜM süreçlerin belleğini boşaltıyordu (oyunun kendisi dahil): bellek aslında boşalmıyor,
    /// geri çekilirken takılma yapıyordu. Oturum boyunca arka plan işlemci kullanımı ölçülür ve oturum sonunda
    /// "öncesi → oturum" olarak raporlanır. Bakım kapanırken oturum düzgün kapatılır (güç planı ve öncelikler geri döner).
    /// </summary>
    public class GameModeService : IGameModeService
    {
        private const string UltimateScheme = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        private const string HighPerformanceScheme = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        private const string BalancedScheme = "381b4222-f694-41f0-9685-ff5bb260df2e";
        private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);

        private readonly IBackgroundMaintenanceService _backgroundMaintenance;
        private readonly ISystemCleanService _cleanService;
        private readonly ILogService _log;
        private readonly IAppSettingsService? _settings;
        private readonly IGameLibraryService? _library;
        private readonly IGameModeSystem _system;

        private readonly object _gate = new();
        private bool _isGameModeActive;
        private readonly List<int> _suspendedByGameMode = new();
        private readonly List<(int Pid, DateTime? Started, ProcessPriorityClass Original, ProcessPriorityClass Applied)> _priorityChanges = new();
        private int? _gamePid;
        private string? _gameName;
        private Timer? _sampler;
        private CpuAverager _cpu = new();
        private TimeSpan _lastBusy;
        private TimeSpan _lastGameCpu;
        private DateTime _lastSampleUtc;

        private Timer? _autoTimer;
        private bool _autoEnabled;
        private int _autoTickRunning;
        private IReadOnlyList<string> _libraryGames = Array.Empty<string>();

        public GameModeService(
            IBackgroundMaintenanceService backgroundMaintenance,
            ISystemCleanService cleanService,
            ILogService log,
            IAppSettingsService? settings = null,
            IGameLibraryService? library = null,
            IGameModeSystem? system = null)
        {
            _backgroundMaintenance = backgroundMaintenance;
            _cleanService = cleanService;
            _log = log ?? NullLogService.Instance;
            _settings = settings;
            _library = library;
            _system = system ?? new WindowsGameModeSystem(_log);
        }

        /// <summary>"OneDrive, Teams.exe" → {"OneDrive","Teams"}. Kural tek yerde: <see cref="ProcessNameList"/>.</summary>
        public static IReadOnlyList<string> ParseProcessList(string? text) => ProcessNameList.Parse(text);

        public bool IsGameModeActive => _isGameModeActive;

        public event Action<bool>? GameModeChanged;

        public string LastActionSummary { get; private set; } = string.Empty;

        public GameModeSessionInfo? CurrentSession { get; private set; }

        public Task<long> EnableGameModeAsync() => EnableGameModeAsync(triggerGame: null);

        /// <param name="triggerGame">Otomatik tetiklemede oyunun süreç adı; elle açılışta null.</param>
        private async Task<long> EnableGameModeAsync(string? triggerGame)
        {
            if (_isGameModeActive) return 0;
            var profile = _settings?.Current;
            bool prioritize = profile?.GameModePrioritize ?? true;
            bool windowsGameMode = profile?.GameModeWindowsGameMode ?? true;
            bool trim = profile?.GameModeTrimMemory ?? true;

            // Ölçüm tabanı: değişikliklerden önce 1 sn arka plan işlemci (oyun biliniyorsa onun payı çıkarılır).
            var candidates = await GameCandidatesAsync(profile).ConfigureAwait(false);
            var running = _system.GetRunningProcesses();
            var game = FindGame(running, triggerGame, candidates);
            double? baseline = await MeasureBackgroundCpuAsync(game?.Pid).ConfigureAwait(false);

            // 1) Güç planı. Önceki plan diske de yazılır: Bakım çökerse açılışta geri yüklenir.
            string? previousScheme = _system.GetActivePowerScheme();
            string planChoice = profile?.GameModePowerPlan ?? GameModePlan.HighPerformance;
            string? appliedPlanName = null;
            bool planFailed = false;
            string planText;
            if (string.Equals(planChoice, GameModePlan.Keep, StringComparison.OrdinalIgnoreCase))
            {
                planText = "Güç planına dokunulmadı. ";
            }
            else
            {
                bool ultimate = string.Equals(planChoice, GameModePlan.Ultimate, StringComparison.OrdinalIgnoreCase);
                string target = ultimate ? UltimateScheme : HighPerformanceScheme;
                bool applied = string.Equals(previousScheme, target, StringComparison.OrdinalIgnoreCase) || _system.SetPowerScheme(target);
                if (!applied && ultimate)
                {
                    applied = _system.SetPowerScheme(HighPerformanceScheme);
                    planText = applied ? "Nihai Performans planı yok; Yüksek Performans etkin. " : "Performans güç planı uygulanamadı. ";
                    appliedPlanName = applied ? "Yüksek Performans" : null;
                }
                else
                {
                    planText = applied
                        ? (ultimate ? "Nihai Performans güç planı etkin. " : "Yüksek Performans güç planı etkin. ")
                        : "Yüksek Performans güç planı bu cihazda yok ya da uygulanamadı. ";
                    appliedPlanName = applied ? (ultimate ? "Nihai Performans" : "Yüksek Performans") : null;
                }
                planFailed = !applied;
            }

            // 2) Windows Oyun Modu: kapalıysa oturum boyunca aç, önceki değeri sakla.
            bool? winGameTurnedOn = null;
            int? previousWinGame = null;
            if (windowsGameMode)
            {
                int current = _system.GetWindowsGameMode();
                if (current == 1) winGameTurnedOn = false;
                else if (_system.SetWindowsGameMode(1))
                {
                    winGameTurnedOn = true;
                    previousWinGame = current;
                }
            }

            _system.SaveState(new GameModeState(previousScheme, DateTime.UtcNow, previousWinGame));
            _isGameModeActive = true;
            _backgroundMaintenance.IsGameModeActive = true;

            // 3) Seçili uygulamaları askıya al (defter sayesinde her koşulda devam ettirilir).
            int suspended = 0;
            var suspendedApps = new List<string>();
            var suspendNames = ParseProcessList(profile?.GameModeSuspendApps);
            foreach (var name in suspendNames)
            {
                bool any = false;
                foreach (var p in running.Where(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    if (game is { } g && p.Pid == g.Pid) continue;
                    if (await _cleanService.SuspendProcessAsync(p.Pid).ConfigureAwait(false))
                    {
                        lock (_suspendedByGameMode) _suspendedByGameMode.Add(p.Pid);
                        suspended++;
                        any = true;
                    }
                }
                if (any) suspendedApps.Add(name);
            }

            // 4) Öncelik: oyun öne, arka plan geriye.
            var background = BackgroundProcesses(running, suspendNames, game);
            int lowered = 0;
            if (prioritize && game is { } found)
                lowered = ApplyPriorities(found, background);

            // 5) Bellek: yalnızca sıkışmada, yalnızca arka plan.
            long freed = 0;
            bool trimSkipped = false;
            if (trim)
            {
                var (load, availBefore) = _system.GetMemoryStatus();
                if (GameModeRules.ShouldTrim(load))
                {
                    foreach (var p in background) _system.TrimWorkingSet(p.Pid);
                    var (_, availAfter) = _system.GetMemoryStatus();
                    freed = availAfter > availBefore ? (long)(availAfter - availBefore) : 0;
                }
                else trimSkipped = true;
            }

            lock (_gate)
            {
                _gamePid = game?.Pid;
                _gameName = game?.Name;
                _cpu = new CpuAverager();
                _lastBusy = _system.GetSystemBusyTime();
                _lastGameCpu = game is { } gg ? _system.GetProcessCpuTime(gg.Pid) ?? TimeSpan.Zero : TimeSpan.Zero;
                _lastSampleUtc = DateTime.UtcNow;
            }

            CurrentSession = new GameModeSessionInfo(
                DateTime.UtcNow, triggerGame, appliedPlanName, planFailed,
                trim ? freed : 0, suspendedApps, suspended,
                PrioritizedGame: prioritize ? game?.Name : null,
                LoweredCount: lowered,
                WindowsGameModeTurnedOn: winGameTurnedOn,
                BaselineBackgroundCpu: baseline,
                MemoryTrimSkipped: trimSkipped);

            _sampler = new Timer(_ => Sample(), null, SampleInterval, SampleInterval);

            LastActionSummary = planText +
                (winGameTurnedOn == true ? "Windows Oyun Modu açıldı. " : "") +
                (game != null && prioritize ? $"{game.Value.Name} öne alındı" + (lowered > 0 ? $", {lowered} arka plan süreci geriye. " : ". ") : "") +
                (suspended > 0 ? $"{suspended} arka plan süreci askıya alındı. " : "") +
                (trim && !trimSkipped ? Core.Text.MemoryResultText.Describe(freed) : "") +
                "Bakım'ın arka plan işleri duraklatıldı.";

            _log.Info("Oyun Modu açıldı: " + LastActionSummary, nameof(GameModeService));
            GameModeChanged?.Invoke(true);
            return freed;
        }

        private IReadOnlyList<RunningProcess> BackgroundProcesses(IReadOnlyList<RunningProcess> running, IReadOnlyList<string> suspendNames, RunningProcess? game)
        {
            var names = (_library?.KnownBackgroundApps.Select(k => k.Process) ?? Enumerable.Empty<string>()).Concat(suspendNames);
            var selected = GameModeRules.SelectBackground(running, names, game?.Name, _system.GetForegroundProcessId(), _system.CurrentProcessId);
            return selected.Where(p => !_system.IsProtected(p.Pid, p.Name)).ToList();
        }

        /// <summary>Oyunu "Normalin üstü", arka planı "Normalin altı" yapar; özgün değerleri saklar.</summary>
        private int ApplyPriorities(RunningProcess game, IReadOnlyList<RunningProcess> background)
        {
            int lowered = 0;
            lock (_gate)
            {
                Change(game.Pid, ProcessPriorityClass.AboveNormal, onlyIfNormal: false);
                foreach (var p in background)
                    if (Change(p.Pid, ProcessPriorityClass.BelowNormal, onlyIfNormal: true)) lowered++;
            }
            return lowered;

            bool Change(int pid, ProcessPriorityClass target, bool onlyIfNormal)
            {
                if (_priorityChanges.Any(c => c.Pid == pid)) return false;
                var original = _system.GetPriority(pid);
                if (original is not { } o || o == target) return false;
                // Kullanıcının kendi ayarladığı (Yüksek, Gerçek zamanlı…) önceliğe dokunulmaz.
                if (onlyIfNormal && o != ProcessPriorityClass.Normal) return false;
                if (o is ProcessPriorityClass.High or ProcessPriorityClass.RealTime) return false;
                if (!_system.SetPriority(pid, target)) return false;
                _priorityChanges.Add((pid, _system.GetStartTime(pid), o, target));
                return true;
            }
        }

        /// <summary>Öncelikleri geri alır: yalnızca süreç hâlâ aynıysa (PID yeniden kullanılmadıysa) ve önceliği hâlâ bizim verdiğimizse.</summary>
        private void RestorePriorities()
        {
            List<(int Pid, DateTime? Started, ProcessPriorityClass Original, ProcessPriorityClass Applied)> changes;
            lock (_gate)
            {
                changes = _priorityChanges.ToList();
                _priorityChanges.Clear();
            }
            foreach (var c in changes)
            {
                if (_system.GetStartTime(c.Pid) != c.Started) continue;
                if (_system.GetPriority(c.Pid) != c.Applied) continue;
                _system.SetPriority(c.Pid, c.Original);
            }
        }

        /// <summary>5 sn'de bir: arka plan işlemci örneği; oyun henüz bulunmadıysa arar ve önceliklendirir.</summary>
        private void Sample()
        {
            try
            {
                if (!_isGameModeActive) return;
                var profile = _settings?.Current;

                if (_gamePid == null && (profile?.GameModePrioritize ?? true))
                {
                    var running = _system.GetRunningProcesses();
                    var game = FindGame(running, null, ExplicitGames(profile).Concat(_libraryGames).ToList());
                    if (game is { } g)
                    {
                        int lowered = ApplyPriorities(g, BackgroundProcesses(running, ParseProcessList(profile?.GameModeSuspendApps), g));
                        lock (_gate)
                        {
                            _gamePid = g.Pid;
                            _gameName = g.Name;
                            _lastGameCpu = _system.GetProcessCpuTime(g.Pid) ?? TimeSpan.Zero;
                        }
                        if (CurrentSession is { } s)
                            CurrentSession = s with { PrioritizedGame = g.Name, LoweredCount = s.LoweredCount + lowered };
                        _log.Info($"Oyun Modu: {g.Name} bulundu ve öne alındı ({lowered} arka plan süreci geriye).", nameof(GameModeService));
                    }
                }

                lock (_gate)
                {
                    var now = DateTime.UtcNow;
                    var busy = _system.GetSystemBusyTime();
                    var gameCpu = _gamePid is { } pid ? _system.GetProcessCpuTime(pid) ?? _lastGameCpu : TimeSpan.Zero;
                    double percent = GameModeRules.BackgroundCpuPercent(busy - _lastBusy, gameCpu - _lastGameCpu, now - _lastSampleUtc, _system.LogicalCores);
                    _cpu.Add(percent);
                    _lastBusy = busy;
                    _lastGameCpu = gameCpu;
                    _lastSampleUtc = now;
                    if (CurrentSession is { } s) CurrentSession = s with { SessionBackgroundCpu = _cpu.Average };
                }
            }
            catch (Exception ex)
            {
                _log.Debug($"Oyun Modu ölçümü alınamadı: {ex.Message}", nameof(GameModeService));
            }
        }

        private async Task<double?> MeasureBackgroundCpuAsync(int? gamePid)
        {
            try
            {
                var busy0 = _system.GetSystemBusyTime();
                var game0 = gamePid is { } p ? _system.GetProcessCpuTime(p) ?? TimeSpan.Zero : TimeSpan.Zero;
                var t0 = DateTime.UtcNow;
                await Task.Delay(1000).ConfigureAwait(false);
                var busy1 = _system.GetSystemBusyTime();
                var game1 = gamePid is { } q ? _system.GetProcessCpuTime(q) ?? game0 : TimeSpan.Zero;
                return GameModeRules.BackgroundCpuPercent(busy1 - busy0, game1 - game0, DateTime.UtcNow - t0, _system.LogicalCores);
            }
            catch (Exception ex)
            {
                _log.Debug($"Oyun Modu taban ölçümü alınamadı: {ex.Message}", nameof(GameModeService));
                return null;
            }
        }

        private static IReadOnlyList<string> ExplicitGames(Models.AppSettingsData? profile) => ParseProcessList(profile?.GameModeAutoStartExes);

        private async Task<IReadOnlyList<string>> GameCandidatesAsync(Models.AppSettingsData? profile)
        {
            if ((profile?.GameModeAutoLibrary ?? true) && _library != null && _libraryGames.Count == 0)
                await RefreshLibraryAsync().ConfigureAwait(false);
            var list = ExplicitGames(profile).ToList();
            if (profile?.GameModeAutoLibrary ?? true) list.AddRange(_libraryGames);
            return list;
        }

        private async Task RefreshLibraryAsync()
        {
            if (_library == null) return;
            try
            {
                _libraryGames = (await _library.DetectGamesAsync().ConfigureAwait(false)).Select(g => g.ProcessName).ToList();
            }
            catch (Exception ex)
            {
                _log.Debug($"Oyun kütüphanesi okunamadı: {ex.Message}", nameof(GameModeService));
            }
        }

        private static RunningProcess? FindGame(IReadOnlyList<RunningProcess> running, string? preferred, IReadOnlyList<string> candidates)
        {
            if (preferred != null)
            {
                var hit = running.FirstOrDefault(p => string.Equals(p.Name, preferred, StringComparison.OrdinalIgnoreCase));
                if (hit.Pid != 0) return hit;
            }
            foreach (var name in candidates)
            {
                var hit = running.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (hit.Pid != 0) return hit;
            }
            return null;
        }

        public void DisableGameMode()
        {
            if (!_isGameModeActive) return;

            _isGameModeActive = false;
            _backgroundMaintenance.IsGameModeActive = false;
            _sampler?.Dispose();
            _sampler = null;

            RestorePriorities();

            var state = _system.LoadState();
            bool restored = _system.SetPowerScheme(state?.PreviousScheme ?? BalancedScheme);
            if (state?.PreviousWindowsGameMode is { } previousWinGame) _system.SetWindowsGameMode(previousWinGame);
            _system.DeleteState();

            int resumed = 0;
            List<int> pids;
            lock (_suspendedByGameMode)
            {
                pids = _suspendedByGameMode.ToList();
                _suspendedByGameMode.Clear();
            }
            if (pids.Count > 0)
            {
                // UI iş parçacığından çağrılabilir: async çağrı arka planda beklenir (bağlama dönüp kilitlenmesin).
                resumed = Task.Run(async () =>
                {
                    int ok = 0;
                    foreach (var pid in pids)
                        if (await _cleanService.ResumeProcessAsync(pid).ConfigureAwait(false)) ok++;
                    return ok;
                }).GetAwaiter().GetResult();
            }

            var session = CurrentSession;
            LastActionSummary = (restored
                ? "Önceki güç planı geri yüklendi; öncelikler ve Bakım'ın arka plan işleri eski hâlinde."
                : "Önceki güç planı geri yüklenemedi; Windows güç ayarlarından kontrol edin.") +
                (pids.Count > 0 ? $" {resumed}/{pids.Count} askıya alınan süreç devam ettirildi." : "") +
                (GameModeRules.CpuChangeText(session?.BaselineBackgroundCpu, session?.SessionBackgroundCpu) is { Length: > 0 } cpu
                    ? $" Arka plan işlemci: {cpu}." : "");

            RecordSession(state?.ActivatedAtUtc, restored, session);
            CurrentSession = null;
            lock (_gate)
            {
                _gamePid = null;
                _gameName = null;
            }

            _log.Info("Oyun Modu kapatıldı: " + LastActionSummary, nameof(GameModeService));
            GameModeChanged?.Invoke(false);
        }

        public void RecoverInterruptedSession()
        {
            if (_isGameModeActive) return;
            var state = _system.LoadState();
            if (state == null) return;

            _log.Warning("Önceki oturumda Oyun Modu kapatılmadan uygulama sonlanmış; güç planı ve Windows Oyun Modu geri yükleniyor.", null, nameof(GameModeService));
            _system.SetPowerScheme(state.PreviousScheme ?? BalancedScheme);
            if (state.PreviousWindowsGameMode is { } previous) _system.SetWindowsGameMode(previous);
            _system.DeleteState();
        }

        /// <summary>
        /// Oturumu Etkinlik Merkezi'ne yazar: süre, tetikleyen oyun, öne alınan oyun, arka plan işlemci değişimi,
        /// kırpılan bellek, askıya alınan uygulamalar ve güç planının geri gelip gelmediği.
        /// </summary>
        private static void RecordSession(DateTime? activatedAtUtc, bool planRestored, GameModeSessionInfo? session)
        {
            var activity = App.TryGetService<Activity.IActivityService>();
            if (activity == null) return;

            string duration = activatedAtUtc is { } start ? Core.Text.DurationText.Describe(DateTime.UtcNow - start) : "süre bilinmiyor";
            var parts = new List<string> { duration };
            if (session?.PrioritizedGame is { } game) parts.Add($"{game} öne alındı");
            if (GameModeRules.CpuChangeText(session?.BaselineBackgroundCpu, session?.SessionBackgroundCpu) is { Length: > 0 } cpu)
                parts.Add($"arka plan işlemci {cpu}");
            if (session?.FreedBytes > 0) parts.Add($"{Core.Text.ByteFormatter.Format(session.FreedBytes)} bellek");
            if (session?.SuspendedApps.Count > 0) parts.Add($"{session.SuspendedApps.Count} uygulama askıdaydı");
            parts.Add(planRestored ? "önceki güç planı geri yüklendi" : "güç planı geri yüklenemedi");
            string title = session?.TriggerGame is { } trigger ? $"Oyun Modu oturumu · {trigger}" : "Oyun Modu oturumu";
            Activity.ActivityRecording.RecordSimple(activity, Core.Activity.ActivityKind.GameModeSession, "Oyun Modu",
                title, string.Join(" · ", parts),
                planRestored ? Core.Activity.ActivityOutcome.Succeeded : Core.Activity.ActivityOutcome.PartiallySucceeded,
                deepLink: "GameMode");
        }

        public void StartAutoTrigger()
        {
            if (_settings == null || _autoTimer != null) return;
            _ = RefreshLibraryAsync();
            _autoTimer = new Timer(_ => _ = AutoTickAsync(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Listedeki ya da (açıksa) kütüphanedeki bir oyun çalışıyorsa Oyun Modu'nu açar; hepsi kapanınca
        /// (yalnızca otomatik açtıysa) kapatır. Kullanıcı elle açtıysa otomatik kapatılmaz.
        /// </summary>
        private async Task AutoTickAsync()
        {
            if (Interlocked.Exchange(ref _autoTickRunning, 1) == 1) return;
            try
            {
                var profile = _settings!.Current;
                if (!profile.GameModeAutoStart) return;
                var games = ExplicitGames(profile).Concat(profile.GameModeAutoLibrary ? _libraryGames : Array.Empty<string>())
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (games.Count == 0) return;

                var running = _system.GetRunningProcesses();
                string? runningGame = FindGame(running, null, games)?.Name;

                if (runningGame != null && !_isGameModeActive)
                {
                    _autoEnabled = true;
                    _log.Info($"Oyun başladı ({runningGame}); Oyun Modu otomatik açılıyor.", nameof(GameModeService));
                    await EnableGameModeAsync(runningGame).ConfigureAwait(false);
                }
                else if (runningGame == null && _isGameModeActive && _autoEnabled)
                {
                    _autoEnabled = false;
                    _log.Info("Oyunlar kapandı; Oyun Modu otomatik kapatılıyor.", nameof(GameModeService));
                    DisableGameMode();
                }
            }
            catch (Exception ex)
            {
                _log.Warning("Oyun Modu otomatik tetikleme denetimi başarısız.", ex, nameof(GameModeService));
            }
            finally
            {
                Interlocked.Exchange(ref _autoTickRunning, 0);
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
            return await EnableGameModeAsync().ConfigureAwait(false);
        }
    }
}
