using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Core.GameMode;
using Bakım.Models;
using Bakım.Services;
using Bakım.Services.GameMode;
using Xunit;

namespace Bakim.Tests
{
    /// <summary>
    /// Oyun Modu v2. Testler sahte sistemle çalışır: eskiden gerçek powercfg çağrılıyor, geliştirme makinesinin
    /// güç planı Yüksek Performans'ta kalıyor ve gerçek durum dosyası siliniyordu.
    /// </summary>
    public class GameModeServiceTests
    {
        private class MockBackgroundMaintenanceService : IBackgroundMaintenanceService
        {
            public bool IsGameModeActive { get; set; }
#pragma warning disable CS0067
            public event Action<int>? HighRamDetected;
            public event Action<long>? AutoRamCleanCompleted;
            public event Action<string>? ScheduledCleanupCompleted;
#pragma warning restore CS0067

            public void Start() { }
            public void Stop() { }
        }

        private class MockSystemCleanService : ISystemCleanService
        {
            public List<int> Suspended { get; } = new();
            public List<int> Resumed { get; } = new();
            public Task<long> AutoTrimWorkingSetsAsync() => throw new InvalidOperationException("v2 tüm süreçleri kırpmaz");
            public Task<long> OptimizeRamAsync() => Task.FromResult(500L * 1024 * 1024);
            public Task<long> ClearStandbyListAsync() => Task.FromResult(100L * 1024 * 1024);
            public Task<long> FlushModifiedPagesAsync() => Task.FromResult(50L * 1024 * 1024);
            public Task<long> PurgeAllMemoryAsync() => Task.FromResult(400L * 1024 * 1024);
            public Task<bool> SuspendProcessAsync(int processId) { Suspended.Add(processId); return Task.FromResult(true); }
            public Task<bool> ResumeProcessAsync(int processId) { Resumed.Add(processId); return Task.FromResult(true); }
            public Task<DetailedMemoryComposition> GetDetailedMemoryCompositionAsync() => Task.FromResult(new DetailedMemoryComposition());
            public Task<bool> KillProcessAsync(int processId) => Task.FromResult(true);
            public Task<bool> SetProcessPriorityAsync(int processId, ProcessPriorityClass priority) => Task.FromResult(true);
            public Task<bool> SetProcessAffinityAsync(int processId, long affinityMask) => Task.FromResult(true);
            public Task<SystemStats> GetSystemStatsAsync() => Task.FromResult(new SystemStats());
            public List<CleanCategory> GetDefaultCategories() => new();
            public DriveInfoItem GetSystemDriveInfo() => new();
            public Task<(List<CleanFileItem> items, long totalBytes)> ScanCategoryAsync(CleanCategory category, IProgress<string> progress, CancellationToken ct) =>
                Task.FromResult((new List<CleanFileItem>(), 0L));
            public Task<CleanResult> CleanItemsAsync(IEnumerable<CleanFileItem> items, IProgress<(string file, int percent)> progress, CancellationToken ct) =>
                Task.FromResult(new CleanResult());
        }

        /// <summary>Hiçbir gerçek sistem ayarına dokunmayan sahte Oyun Modu sistemi.</summary>
        private sealed class FakeSystem : IGameModeSystem
        {
            public string ActiveScheme = "381b4222-f694-41f0-9685-ff5bb260df2e"; // Dengeli
            public int WindowsGameMode = 0;
            public GameModeState? State;
            public uint MemoryLoad = 60;
            public ulong Available = 4UL << 30;
            public int Foreground = 999;
            public readonly List<RunningProcess> Processes = new();
            public readonly Dictionary<int, ProcessPriorityClass> Priorities = new();
            public readonly List<int> Trimmed = new();

            public string? GetActivePowerScheme() => ActiveScheme;
            public bool SetPowerScheme(string schemeGuid) { ActiveScheme = schemeGuid; return true; }
            public int GetWindowsGameMode() => WindowsGameMode;
            public bool SetWindowsGameMode(int value) { WindowsGameMode = value; return true; }
            public GameModeState? LoadState() => State;
            public void SaveState(GameModeState state) => State = state;
            public void DeleteState() => State = null;
            public TimeSpan GetSystemBusyTime() => TimeSpan.Zero;
            public int LogicalCores => 12;
            public TimeSpan? GetProcessCpuTime(int pid) => TimeSpan.Zero;
            private int _trimSeen;
            public (uint Load, ulong AvailableBytes) GetMemoryStatus()
            {
                // Her yeni kırpmadan sonra kullanılabilir bellek artar.
                if (Trimmed.Count > _trimSeen)
                {
                    Available += 512UL << 20;
                    _trimSeen = Trimmed.Count;
                }
                return (MemoryLoad, Available);
            }
            public IReadOnlyList<RunningProcess> GetRunningProcesses() => Processes.ToList();
            public int GetForegroundProcessId() => Foreground;
            public int CurrentProcessId => 1;
            public ProcessPriorityClass? GetPriority(int pid) => Priorities.TryGetValue(pid, out var p) ? p : null;
            public bool SetPriority(int pid, ProcessPriorityClass priority) { Priorities[pid] = priority; return true; }
            public DateTime? GetStartTime(int pid) => new DateTime(2026, 1, 1);
            public bool TrimWorkingSet(int pid) { Trimmed.Add(pid); return true; }
            public bool IsProtected(int pid, string name) => name == "csrss";
        }

        private sealed class FakeSettings : IAppSettingsService
        {
            public AppSettingsData Current { get; } = new();
#pragma warning disable CS0067
            public event Action<AppSettingsData>? SettingsChanged;
#pragma warning restore CS0067
            public string SettingsFilePath => "yok.json";
            public AppSettingsData Load() => Current;
            public void Save(AppSettingsData data) { }
            public void Update(Action<AppSettingsData> mutate) => mutate(Current);
        }

        private sealed class FakeLibrary : IGameLibraryService
        {
            public IReadOnlyList<KnownBackgroundApp> KnownBackgroundApps { get; } = new[] { new KnownBackgroundApp("Discord", "Discord"), new KnownBackgroundApp("chrome", "Chrome") };
            public Task<IReadOnlyList<DetectedGame>> DetectGamesAsync(bool refresh = false, CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyList<DetectedGame>>(new[] { new DetectedGame("VALORANT", "VALORANT-Win64-Shipping", "Riot") });
            public IReadOnlyList<RunningApp> GetRunningApps() => Array.Empty<RunningApp>();
        }

        private static (GameModeService Service, FakeSystem System, MockBackgroundMaintenanceService Maintenance, FakeSettings Settings, MockSystemCleanService Clean) Create()
        {
            var system = new FakeSystem();
            var maintenance = new MockBackgroundMaintenanceService();
            var settings = new FakeSettings();
            var clean = new MockSystemCleanService();
            var service = new GameModeService(maintenance, clean, NullLogService.Instance, settings, new FakeLibrary(), system);
            return (service, system, maintenance, settings, clean);
        }

        [Fact]
        public async Task Enable_ActivatesAndPausesMaintenance_Disable_RestoresEverything()
        {
            var (service, system, maintenance, _, _) = Create();
            bool? last = null;
            service.GameModeChanged += active => last = active;

            await service.EnableGameModeAsync();
            Assert.True(service.IsGameModeActive);
            Assert.True(maintenance.IsGameModeActive);
            Assert.Equal("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", system.ActiveScheme);
            Assert.NotNull(system.State);
            Assert.True(last);

            service.DisableGameMode();
            Assert.False(service.IsGameModeActive);
            Assert.False(maintenance.IsGameModeActive);
            Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", system.ActiveScheme);
            Assert.Null(system.State);
            Assert.False(last);
        }

        [Fact]
        public async Task WindowsGameMode_TurnedOnForSession_ThenRestored()
        {
            var (service, system, _, _, _) = Create();
            system.WindowsGameMode = 0;

            await service.EnableGameModeAsync();
            Assert.Equal(1, system.WindowsGameMode);
            Assert.True(service.CurrentSession!.WindowsGameModeTurnedOn);

            service.DisableGameMode();
            Assert.Equal(0, system.WindowsGameMode);
        }

        [Fact]
        public async Task WindowsGameMode_AlreadyOn_IsLeftAlone()
        {
            var (service, system, _, _, _) = Create();
            system.WindowsGameMode = 1;
            await service.EnableGameModeAsync();
            Assert.False(service.CurrentSession!.WindowsGameModeTurnedOn);
            service.DisableGameMode();
            Assert.Equal(1, system.WindowsGameMode);
        }

        [Fact]
        public async Task Priorities_GameUp_BackgroundDown_ForegroundAndProtectedUntouched_ThenRestored()
        {
            var (service, system, _, _, _) = Create();
            system.Processes.AddRange(new[]
            {
                new RunningProcess(10, "VALORANT-Win64-Shipping"), new RunningProcess(20, "Discord"),
                new RunningProcess(30, "chrome"), new RunningProcess(31, "chrome"), new RunningProcess(40, "csrss")
            });
            foreach (var p in system.Processes) system.Priorities[p.Pid] = ProcessPriorityClass.Normal;
            system.Priorities[31] = ProcessPriorityClass.High; // kullanıcının ayarı: dokunulmaz
            system.Foreground = 30;                             // ön plandaki chrome: dokunulmaz

            await service.EnableGameModeAsync();
            Assert.Equal(ProcessPriorityClass.AboveNormal, system.Priorities[10]);
            Assert.Equal(ProcessPriorityClass.BelowNormal, system.Priorities[20]);
            Assert.Equal(ProcessPriorityClass.Normal, system.Priorities[30]);
            Assert.Equal(ProcessPriorityClass.High, system.Priorities[31]);
            Assert.Equal(ProcessPriorityClass.Normal, system.Priorities[40]);
            Assert.Equal("VALORANT-Win64-Shipping", service.CurrentSession!.PrioritizedGame);
            Assert.Equal(1, service.CurrentSession.LoweredCount);

            service.DisableGameMode();
            Assert.Equal(ProcessPriorityClass.Normal, system.Priorities[10]);
            Assert.Equal(ProcessPriorityClass.Normal, system.Priorities[20]);
        }

        [Fact]
        public async Task Memory_TrimmedOnlyUnderPressure_AndNeverTheGame()
        {
            var (service, system, _, _, _) = Create();
            system.Processes.AddRange(new[] { new RunningProcess(10, "VALORANT-Win64-Shipping"), new RunningProcess(20, "Discord") });
            foreach (var p in system.Processes) system.Priorities[p.Pid] = ProcessPriorityClass.Normal;

            system.MemoryLoad = 60;
            await service.EnableGameModeAsync();
            Assert.Empty(system.Trimmed);
            Assert.True(service.CurrentSession!.MemoryTrimSkipped);
            service.DisableGameMode();

            system.MemoryLoad = 92;
            long freed = await service.EnableGameModeAsync();
            Assert.Equal(new[] { 20 }, system.Trimmed);
            Assert.True(freed > 0);
            service.DisableGameMode();
        }

        [Fact]
        public async Task SuspendList_IsSuspendedAndResumed()
        {
            var (service, system, _, settings, clean) = Create();
            settings.Current.GameModeSuspendApps = "OneDrive";
            system.Processes.Add(new RunningProcess(50, "OneDrive"));

            await service.EnableGameModeAsync();
            Assert.Equal(new[] { 50 }, clean.Suspended);
            service.DisableGameMode();
            Assert.Equal(new[] { 50 }, clean.Resumed);
        }

        [Fact]
        public async Task Toggle_SwitchesState()
        {
            var (service, _, maintenance, _, _) = Create();
            await service.ToggleGameModeAsync();
            Assert.True(service.IsGameModeActive);
            Assert.True(maintenance.IsGameModeActive);
            await service.ToggleGameModeAsync();
            Assert.False(service.IsGameModeActive);
            Assert.False(maintenance.IsGameModeActive);
        }

        [Fact]
        public void Recover_RestoresPlanAndWindowsGameMode()
        {
            var (service, system, _, _, _) = Create();
            system.ActiveScheme = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
            system.WindowsGameMode = 1;
            system.State = new GameModeState("381b4222-f694-41f0-9685-ff5bb260df2e", DateTime.UtcNow, PreviousWindowsGameMode: 0);

            service.RecoverInterruptedSession();
            Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", system.ActiveScheme);
            Assert.Equal(0, system.WindowsGameMode);
            Assert.Null(system.State);
        }
    }
}
