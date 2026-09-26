using System.IO;
using Bakım.Models;
using Bakım.Services;
using Bakım.ViewModels;
using Xunit;

namespace Bakim.Tests;

public class SystemInfoRevampTests
{
    internal sealed class MockSettingsService : IAppSettingsService
    {
        public AppSettingsData Current { get; } = new();
        public string SettingsFilePath => "(test)";
        public event Action<AppSettingsData>? SettingsChanged;
        public AppSettingsData Load() => Current;
        public void Save(AppSettingsData data) => SettingsChanged?.Invoke(data);
        public void Update(Action<AppSettingsData> mutate) => mutate(Current);
    }

    internal class MockSystemInfoService : ISystemInfoService
    {
        public Task<SystemHardwareStats> GetSystemHardwareAsync()
        {
            var stats = new SystemHardwareStats
            {
                CpuName = "AMD Ryzen 5 5600 6-Core",
                CpuPercentage = 15,
                CpuCoresThreads = "6 Çekirdek / 12 İzlek",
                GpuName = "NVIDIA GeForce RTX 4060",
                GpuVram = "8 GB VRAM",
                TotalRamGb = 16.0,
                FreeRamGb = 8.0,
                RamPercentage = 50,
                NetworkAdapterName = "Realtek Gaming 2.5GbE",
                NetworkLinkSpeed = "1.0 Gbps (1000 Mbps)",
                NetworkIpAddress = "192.168.1.100",
                DisplayResolution = "1920 x 1080",
                DisplayRefreshRate = "144 Hz",
                SystemUptimeText = "2 Gün, 4 Saat",
                SecureBootStatus = "Aktif (UEFI Doğrulanmış)",
                TpmStatus = "TPM 2.0 Hazır & Etkin",
                VirtualizationStatus = "Etkin (VT-x / AMD-V)",
                Drives = new List<DriveInfoItem>
                {
                    new DriveInfoItem
                    {
                        Name = "C:\\",
                        VolumeLabel = "Yerel Disk",
                        DriveFormat = "NTFS",
                        TotalGb = 500.0,
                        FreeGb = 200.0,
                        UsagePercentage = 60
                    }
                }
            };
            return Task.FromResult(stats);
        }

        public Task<List<SmartDiskHealthItem>> GetDiskSmartHealthAsync()
        {
            return Task.FromResult(new List<SmartDiskHealthItem>
            {
                new SmartDiskHealthItem
                {
                    DeviceId = "0",
                    Model = "Samsung SSD 980 1TB",
                    InterfaceType = "NVMe / PCIe",
                    MediaType = "SSD (Katı Hal)",
                    HealthStatus = "Mükemmel (%100 Sağlık)",
                    Temperature = "Normal (36 °C)",
                    SizeGb = 1000.0
                }
            });
        }

        public Task<List<LargeDiskFileItem>> ScanLargeFilesAsync(string driveLetter, long minSizeBytes, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            return Task.FromResult(new List<LargeDiskFileItem>
            {
                new LargeDiskFileItem
                {
                    FileName = "game_backup.iso",
                    FilePath = "C:\\game_backup.iso",
                    SizeBytes = 4L * 1024L * 1024L * 1024L,
                    SizeFormatted = "4.00 GB",
                    Extension = "ISO",
                    Category = "Disk İmajı"
                },
                new LargeDiskFileItem
                {
                    FileName = "movie.mp4",
                    FilePath = "C:\\movie.mp4",
                    SizeBytes = 2L * 1024L * 1024L * 1024L,
                    SizeFormatted = "2.00 GB",
                    Extension = "MP4",
                    Category = "Video"
                },
                new LargeDiskFileItem
                {
                    FileName = "archive.zip",
                    FilePath = "C:\\archive.zip",
                    SizeBytes = 1500L * 1024L * 1024L,
                    SizeFormatted = "1.46 GB",
                    Extension = "ZIP",
                    Category = "Arşiv"
                }
            });
        }

        public Task<bool> DeleteLargeFileAsync(string filePath) => Task.FromResult(true);
        public Task<bool> DeleteLargeFileToRecycleBinAsync(string filePath) => Task.FromResult(true);
        public Task<TrimResult> OptimizeDriveTrimAsync(string driveLetter) => Task.FromResult(new TrimResult(true, "ok"));
        public string LastDeleteError => string.Empty;

        public Task<string> GenerateHardwareReportHtmlAsync()
        {
            string html = "<html><body><h1>Bakım Sistem Donanım Raporu</h1></body></html>";
            string path = Path.GetTempFileName();
            File.WriteAllText(path, html);
            return Task.FromResult(path);
        }
    }

    private static StorageViewModel NewStorage() =>
        TestFactories.Storage(new DuplicateFinderService(), new MockSystemInfoService());

    private static SystemInfoViewModel NewSystemInfo() =>
        new(new MockSystemInfoService(), new MockSettingsService(), NavigationService.Instance, NewStorage());

    [Fact]
    public async Task SystemInfoViewModel_SubTabSwitching_UpdatesActiveTabProperties()
    {
        var vm = NewSystemInfo();

        Assert.True(vm.IsHardwareTab);
        Assert.False(vm.IsSmartTab);

        vm.SwitchSubTab("Smart");
        Assert.False(vm.IsHardwareTab);
        Assert.True(vm.IsSmartTab);

        // Büyük dosyalar ve yinelenenler Faz 4'te Depolama sayfasına taşındı.
        var storage = NewStorage();
        Assert.True(storage.IsDiskMapTab);
        storage.SwitchSubTab("LargeFiles");
        Assert.True(storage.IsLargeFilesTab);
        storage.SwitchSubTab("Duplicates");
        Assert.False(storage.IsLargeFilesTab);
        Assert.True(storage.IsDuplicatesTab);
        Assert.True(storage.IsDuplicateFilesTab);

        // "Boş klasörler" ayrı üst sekmedir ama yinelenenler panelini paylaşır.
        storage.SwitchSubTab("EmptyFolders");
        Assert.True(storage.IsDuplicatesTab);
        Assert.True(storage.IsEmptyFoldersTab);
        Assert.False(storage.IsDuplicateFilesTab);
        storage.SwitchSubTab("Duplicates");
        Assert.True(storage.IsDuplicateFilesTab);
    }

    [Fact]
    public async Task SystemInfoViewModel_LargeFilesCategoryFilter_FiltersCorrectly()
    {
        var vm = NewStorage();
        
        await vm.ScanLargeFilesAsync();
        Assert.Equal(3, vm.LargeFiles.Count);
        Assert.Equal(3, vm.FilteredLargeFiles.Count);

        vm.SetCategoryFilter("Video");
        Assert.Single(vm.FilteredLargeFiles);
        Assert.Equal("movie.mp4", vm.FilteredLargeFiles[0].FileName);

        vm.SetCategoryFilter("Disk İmajı");
        Assert.Single(vm.FilteredLargeFiles);
        Assert.Equal("game_backup.iso", vm.FilteredLargeFiles[0].FileName);

        vm.SetCategoryFilter("Tümü");
        Assert.Equal(3, vm.FilteredLargeFiles.Count);
    }

    [Fact]
    public async Task SystemInfoViewModel_TelemetryAndSpecs_PopulatedCorrectly()
    {
        var vm = NewSystemInfo();
        await vm.RefreshAsync();

        Assert.Equal("AMD Ryzen 5 5600 6-Core", vm.Hardware.CpuName);
        Assert.Equal("%15", vm.Hardware.CpuPercentageText);
        Assert.Equal("Realtek Gaming 2.5GbE", vm.Hardware.NetworkAdapterName);
        Assert.Equal("192.168.1.100", vm.Hardware.NetworkIpAddress);
        Assert.Equal("Aktif (UEFI Doğrulanmış)", vm.Hardware.SecureBootStatus);
        Assert.Equal("TPM 2.0 Hazır & Etkin", vm.Hardware.TpmStatus);
        Assert.Single(vm.Drives);
    }

    [Fact]
    public async Task SystemInfoService_GenerateHardwareReportHtml_ProducesValidFile()
    {
        var service = new SystemInfoService();
        string reportPath = await service.GenerateHardwareReportHtmlAsync();

        Assert.True(File.Exists(reportPath));
        string content = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("Bakım - Sistem Donanım Raporu", content);
        Assert.Contains("İşletim Sistemi", content);
        Assert.Contains("Fiziksel Bellek (RAM)", content);

        try { File.Delete(reportPath); } catch { }
    }

    [Fact]
    public async Task SystemInfoViewModel_SetThreshold_AcceptsStringOrNumberWithoutException()
    {
        var vm = NewStorage();

        // 1. String parametre ("500") geçildiğinde hata vermemeli ve 500 MB eşiğini seçmeli
        vm.SetThresholdCommand.Execute("500");
        Assert.Equal(500, vm.MinFileSizeThresholdMb);
        Assert.True(vm.IsThreshold500Mb);
        Assert.False(vm.IsThreshold1Gb);

        // 2. String parametre ("2048") geçildiğinde 2 GB seçilmeli
        vm.SetThresholdCommand.Execute("2048");
        Assert.Equal(2048, vm.MinFileSizeThresholdMb);
        Assert.True(vm.IsThreshold2Gb);
        Assert.False(vm.IsThreshold500Mb);

        // 3. Sayısal parametre (5120L) geçildiğinde 5 GB seçilmeli
        vm.SetThresholdCommand.Execute(5120L);
        Assert.Equal(5120, vm.MinFileSizeThresholdMb);
        Assert.True(vm.IsThreshold5Gb);

        // 4. Null veya geçersiz parametrede varsayılan 1024'e düşmeli
        vm.SetThresholdCommand.Execute(null);
        Assert.Equal(1024, vm.MinFileSizeThresholdMb);
        Assert.True(vm.IsThreshold1Gb);
    }

    [Fact]
    public void SystemInfoViewModel_CategoryFilterChips_UpdateComputedSelectionBooleans()
    {
        var vm = NewStorage();

        Assert.True(vm.IsCategoryAll);
        Assert.False(vm.IsCategoryVideo);

        vm.SetCategoryFilterCommand.Execute("Video");
        Assert.False(vm.IsCategoryAll);
        Assert.True(vm.IsCategoryVideo);

        vm.SetCategoryFilterCommand.Execute("Disk İmajı");
        Assert.True(vm.IsCategoryDiskImage);
        Assert.False(vm.IsCategoryVideo);

        vm.SetCategoryFilterCommand.Execute("Arşiv");
        Assert.True(vm.IsCategoryArchive);

        vm.SetCategoryFilterCommand.Execute("Kurulum / Oyun");
        Assert.True(vm.IsCategoryInstaller);

        vm.SetCategoryFilterCommand.Execute("Tümü");
        Assert.True(vm.IsCategoryAll);
    }

    [Fact]
    public async Task SystemInfoViewModel_LiveSearch_FiltersByNameAndExtension()
    {
        var vm = NewStorage();
        await vm.ScanLargeFilesAsync();

        Assert.Equal(3, vm.FilteredLargeFiles.Count);

        // Search by extension
        vm.SearchQuery = "mp4";
        Assert.Single(vm.FilteredLargeFiles);
        Assert.Equal("movie.mp4", vm.FilteredLargeFiles[0].FileName);

        // Search by partial filename
        vm.SearchQuery = "backup";
        Assert.Single(vm.FilteredLargeFiles);
        Assert.Equal("game_backup.iso", vm.FilteredLargeFiles[0].FileName);

        // Clear search
        vm.SearchQuery = string.Empty;
        Assert.Equal(3, vm.FilteredLargeFiles.Count);
    }

    [Fact]
    public async Task SystemInfoViewModel_Sorting_OrdersCorrectly()
    {
        var vm = NewStorage();
        await vm.ScanLargeFilesAsync();

        // Default: SizeDesc (4GB, 2GB, 1.46GB)
        Assert.Equal("game_backup.iso", vm.FilteredLargeFiles[0].FileName);
        Assert.Equal("movie.mp4", vm.FilteredLargeFiles[1].FileName);
        Assert.Equal("archive.zip", vm.FilteredLargeFiles[2].FileName);

        // SizeAsc: (1.46GB, 2GB, 4GB)
        vm.SetSortMode("SizeAsc");
        Assert.Equal("archive.zip", vm.FilteredLargeFiles[0].FileName);
        Assert.Equal("game_backup.iso", vm.FilteredLargeFiles[2].FileName);

        // NameAsc: (archive, game_backup, movie)
        vm.SetSortMode("NameAsc");
        Assert.Equal("archive.zip", vm.FilteredLargeFiles[0].FileName);
        Assert.Equal("game_backup.iso", vm.FilteredLargeFiles[1].FileName);
        Assert.Equal("movie.mp4", vm.FilteredLargeFiles[2].FileName);
    }

    [Fact]
    public async Task SystemInfoViewModel_MultiSelectAndBatchActions_WorkCorrectly()
    {
        var vm = NewStorage();
        await vm.ScanLargeFilesAsync();

        Assert.Equal(3, vm.FilteredLargeFiles.Count);
        Assert.False(vm.IsAllSelected);
        Assert.Equal(0, vm.SelectedFilesCount);
        Assert.False(vm.HasSelectedFiles);

        // Select All
        vm.ToggleSelectAllCommand.Execute(null);
        Assert.True(vm.IsAllSelected);
        Assert.Equal(3, vm.SelectedFilesCount);
        Assert.True(vm.HasSelectedFiles);

        // Deselect first item
        vm.FilteredLargeFiles[0].IsSelected = false;
        Assert.False(vm.IsAllSelected);
        Assert.Equal(2, vm.SelectedFilesCount);

        // Batch Recycle
        await vm.RecycleSelectedFilesAsync();
        Assert.Single(vm.LargeFiles);
        Assert.Equal(0, vm.SelectedFilesCount);
        Assert.False(vm.HasSelectedFiles);
    }

    [Fact]
    public async Task SystemInfoViewModel_CategoryStats_CalculatesProportions()
    {
        var vm = NewStorage();
        await vm.ScanLargeFilesAsync();

        Assert.NotNull(vm.CategoryStats);
        Assert.True(vm.CategoryStats.HasData);
        Assert.Equal(3, vm.CategoryStats.FileCount);
        Assert.True(vm.CategoryStats.VideoBytes > 0);
        Assert.True(vm.CategoryStats.DiskImageBytes > 0);
        Assert.True(vm.CategoryStats.ArchiveBytes > 0);
    }
}
