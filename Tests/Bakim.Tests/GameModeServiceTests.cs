using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Models;
using Bakım.Services;
using Xunit;

namespace Bakim.Tests
{
    public class GameModeServiceTests
    {
        private class MockBackgroundMaintenanceService : IBackgroundMaintenanceService
        {
            public bool IsGameModeActive { get; set; }
#pragma warning disable CS0067
            public event Action<int>? HighRamDetected;
            public event Action<long>? AutoRamCleanCompleted;
#pragma warning restore CS0067

            public void Start() { }
            public void Stop() { }
        }

        private class MockSystemCleanService : ISystemCleanService
        {
            public Task<long> AutoTrimWorkingSetsAsync() => Task.FromResult(250L * 1024 * 1024);
            public Task<long> OptimizeRamAsync() => Task.FromResult(500L * 1024 * 1024);
            public Task<long> ClearStandbyListAsync() => Task.FromResult(100L * 1024 * 1024);
            public Task<long> FlushModifiedPagesAsync() => Task.FromResult(50L * 1024 * 1024);
            public Task<long> PurgeAllMemoryAsync() => Task.FromResult(400L * 1024 * 1024);
            public Task<bool> SuspendProcessAsync(int processId) => Task.FromResult(true);
            public Task<bool> ResumeProcessAsync(int processId) => Task.FromResult(true);
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

        [Fact]
        public async Task GameModeService_Enable_ActivatesGameModeAndFreezesBackground()
        {
            var bgMaintenance = new MockBackgroundMaintenanceService();
            var cleanService = new MockSystemCleanService();
            var service = new GameModeService(bgMaintenance, cleanService, NullLogService.Instance);

            bool eventFired = false;
            service.GameModeChanged += active => eventFired = active;

            Assert.False(service.IsGameModeActive);
            Assert.False(bgMaintenance.IsGameModeActive);

            long freed = await service.EnableGameModeAsync();

            Assert.True(service.IsGameModeActive);
            Assert.True(bgMaintenance.IsGameModeActive);
            Assert.True(eventFired);
            Assert.True(freed > 0);
        }

        [Fact]
        public async Task GameModeService_Disable_DeactivatesGameModeAndRestoresBackground()
        {
            var bgMaintenance = new MockBackgroundMaintenanceService();
            var cleanService = new MockSystemCleanService();
            var service = new GameModeService(bgMaintenance, cleanService, NullLogService.Instance);

            await service.EnableGameModeAsync();
            Assert.True(service.IsGameModeActive);

            bool? lastState = null;
            service.GameModeChanged += active => lastState = active;

            service.DisableGameMode();

            Assert.False(service.IsGameModeActive);
            Assert.False(bgMaintenance.IsGameModeActive);
            Assert.False(lastState);
        }

        [Fact]
        public async Task GameModeService_Toggle_SwitchesStateCorrectly()
        {
            var bgMaintenance = new MockBackgroundMaintenanceService();
            var cleanService = new MockSystemCleanService();
            var service = new GameModeService(bgMaintenance, cleanService, NullLogService.Instance);

            Assert.False(service.IsGameModeActive);

            // 1. Toggle -> Enable
            await service.ToggleGameModeAsync();
            Assert.True(service.IsGameModeActive);
            Assert.True(bgMaintenance.IsGameModeActive);

            // 2. Toggle -> Disable
            await service.ToggleGameModeAsync();
            Assert.False(service.IsGameModeActive);
            Assert.False(bgMaintenance.IsGameModeActive);
        }
    }
}
