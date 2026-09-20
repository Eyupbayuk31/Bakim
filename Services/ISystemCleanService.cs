using Bakım.Models;

namespace Bakım.Services
{
    public interface ISystemCleanService
    {
        List<CleanCategory> GetDefaultCategories();
        DriveInfoItem GetSystemDriveInfo();
        Task<(List<CleanFileItem> items, long totalBytes)> ScanCategoryAsync(CleanCategory category, IProgress<string> progress, CancellationToken ct);
        Task<CleanResult> CleanItemsAsync(IEnumerable<CleanFileItem> items, IProgress<(string file, int percent)> progress, CancellationToken ct);
        Task<SystemStats> GetSystemStatsAsync();
        Task<long> OptimizeRamAsync();
        Task<long> ClearStandbyListAsync();
        Task<long> FlushModifiedPagesAsync();
        Task<long> PurgeAllMemoryAsync();
        Task<bool> SuspendProcessAsync(int processId);
        Task<bool> ResumeProcessAsync(int processId);
        Task<DetailedMemoryComposition> GetDetailedMemoryCompositionAsync();
        Task<bool> KillProcessAsync(int processId);
        Task<bool> SetProcessPriorityAsync(int processId, System.Diagnostics.ProcessPriorityClass priority);
        Task<bool> SetProcessAffinityAsync(int processId, long affinityMask);
        Task<long> AutoTrimWorkingSetsAsync();
    }
}
