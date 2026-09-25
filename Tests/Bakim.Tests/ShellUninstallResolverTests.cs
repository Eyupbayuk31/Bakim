using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Bakım.Models;
using Bakım.Services;
using Bakım.ViewModels;
using Xunit;

namespace Bakim.Tests;

public class ShellUninstallResolverTests
{
    private class MockDeepUninstallerService : IDeepUninstallerService
    {
        public List<InstalledAppItem> MockApps { get; set; } = new();

        public Task<List<InstalledAppItem>> GetInstalledAppsAsync() => Task.FromResult(MockApps);
        public Task<bool> LaunchUninstallAsync(InstalledAppItem app, bool silent = false) => Task.FromResult(true);
        public Task<Bakım.Services.Uninstall.UninstallRunResult> RunUninstallAsync(InstalledAppItem app, bool silent, IProgress<string>? progress = null, System.Threading.CancellationToken ct = default) =>
            Task.FromResult(new Bakım.Services.Uninstall.UninstallRunResult(Bakım.Services.Uninstall.UninstallOutcome.Removed, 0, TimeSpan.Zero, "test"));
        public bool IsStillInstalled(InstalledAppItem app) => false;
        public bool SupportsSilentUninstall(InstalledAppItem app) => true;
        public Task<bool> CreateRestorePointAsync(string appName) => Task.FromResult(true);
        public Task<Bakım.Services.Safety.RestorePointResult> CreateRestorePointDetailedAsync(string description) =>
            Task.FromResult(new Bakım.Services.Safety.RestorePointResult(Bakım.Services.Safety.RestorePointOutcome.Created, "test"));
        public Task<BatchUninstallResult> ExecuteBatchSilentUninstallAsync(IEnumerable<InstalledAppItem> apps, bool autoClean, IProgress<BatchUninstallProgress>? progress = null) => Task.FromResult(new BatchUninstallResult());
        public Task<int> ExecuteForceUninstallAsync(InstalledAppItem app, IProgress<string>? progress = null) => Task.FromResult(1);
        public Task<long> ExecuteAutoCleanResidualsAsync(InstalledAppItem app, IProgress<string>? progress = null) => Task.FromResult(1024L);
        public void OpenInstallLocation(InstalledAppItem app) { }
    }

    private class MockResidualScannerEngine : IResidualScannerEngine
    {
        public Task<List<LeftoverItem>> ScanResidualsAsync(InstalledAppItem app, IProgress<string>? progress = null) => Task.FromResult(new List<LeftoverItem>());
        public Task<List<LeftoverItem>> ScanResidualsAsync(InstalledAppItem app, ResidualScanOptions options, IProgress<string>? progress = null) => Task.FromResult(new List<LeftoverItem>());
        public Task<ResidualCleanReport> CleanResidualsDetailedAsync(IEnumerable<LeftoverItem> leftovers, string title, IProgress<string>? progress = null) =>
            Task.FromResult(new ResidualCleanReport("test", Array.Empty<Bakım.Services.Safety.OperationResult>()));
        public Task<List<ResidualItem>> ScanResidualItemsAsync(InstalledAppItem app, ResidualScanOptions options, IProgress<string>? progress = null) => Task.FromResult(new List<ResidualItem>());
        public Task<ResidualCleanReport> CleanResidualItemsDetailedAsync(IEnumerable<ResidualItem> items, string title, IProgress<string>? progress = null) =>
            Task.FromResult(new ResidualCleanReport("test", Array.Empty<Bakım.Services.Safety.OperationResult>()));
        public Task<int> CleanResidualsAsync(IEnumerable<LeftoverItem> leftovers, IProgress<string>? progress = null) => Task.FromResult(0);
        public Task<List<LeftoverItem>> ScanHeuristicResidualsAsync(string targetPathOrExe, string appNameHint) => Task.FromResult(new List<LeftoverItem>());
        public Task<List<ResidualItem>> ScanResidualItemsAsync(InstalledAppItem app, IProgress<string>? progress = null) => Task.FromResult(new List<ResidualItem>());
        public Task<int> CleanResidualItemsAsync(IEnumerable<ResidualItem> items, IProgress<string>? progress = null) => Task.FromResult(0);
    }

    [Fact]
    public async Task ResolveTargetApp_NullOrEmptyPath_ReturnsNull()
    {
        var mock = new MockDeepUninstallerService();
        var resolver = new ShellUninstallResolverService(mock);

        var result1 = await resolver.ResolveTargetAppAsync("");
        var result2 = await resolver.ResolveTargetAppAsync("   ");

        Assert.Null(result1);
        Assert.Null(result2);
    }

    [Fact]
    public async Task ResolveTargetApp_NonExistentPath_ReturnsNull()
    {
        var mock = new MockDeepUninstallerService();
        var resolver = new ShellUninstallResolverService(mock);

        var result = await resolver.ResolveTargetAppAsync(@"C:\NonExistent_Fake_Directory_12345\fake.exe");
        Assert.Null(result);
    }

    [Fact]
    public void WizardViewModel_InitialState_IsReadyAndCalculatesCorrectly()
    {
        var mockUninstaller = new MockDeepUninstallerService();
        var mockScanner = new MockResidualScannerEngine();

        var app = new InstalledAppItem
        {
            DisplayName = "Test App",
            Publisher = "Test Publisher",
            DisplayVersion = "1.0.0",
            EstimatedSizeBytes = 1048576,
            FormattedSize = "1.0 MB"
        };

        var vm = new DeepUninstallWizardViewModel(app, mockUninstaller, mockScanner);

        Assert.Equal(WizardStep.Ready, vm.CurrentStep);
        Assert.Equal(1, vm.StepNumber);
        Assert.Equal("Test App", vm.AppName);
        Assert.True(vm.CreateRestorePoint);
        Assert.True(vm.KillRelatedProcesses);
        Assert.True(vm.BackupRegistryBeforeClean);
    }

    [Fact]
    public void WizardViewModel_FilterAndSelection_FunctionsProperly()
    {
        var mockUninstaller = new MockDeepUninstallerService();
        var mockScanner = new MockResidualScannerEngine();

        var app = new InstalledAppItem { DisplayName = "Sample" };
        var vm = new DeepUninstallWizardViewModel(app, mockUninstaller, mockScanner);

        vm.Residuals.Add(new ResidualItem { Path = @"HKCU\Software\Sample", Type = ResidualType.RegistryKey, SizeInBytes = 100, IsSelected = true });
        vm.Residuals.Add(new ResidualItem { Path = @"C:\AppData\Sample", Type = ResidualType.Folder, SizeInBytes = 200, IsSelected = true });

        vm.ToggleSelectAllCommand.Execute(null);
        Assert.False(vm.IsAllSelected);
        Assert.All(vm.Residuals, r => Assert.False(r.IsSelected));

        vm.ToggleSelectAllCommand.Execute(null);
        Assert.True(vm.IsAllSelected);
        Assert.All(vm.Residuals, r => Assert.True(r.IsSelected));

        vm.SetFilterCommand.Execute("Registry");
        Assert.Equal("Registry", vm.SelectedFilter);
    }
}
