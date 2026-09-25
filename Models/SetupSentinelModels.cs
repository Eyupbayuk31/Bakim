using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Bakım.Models
{
    public class SetupFileEvent
    {
        public string FilePath { get; set; } = string.Empty;
        public string ChangeType { get; set; } = "Created"; // Created, Changed, Renamed
        public long SizeBytes { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool IsExecutable { get; set; }
        public string Extension { get; set; } = string.Empty;
    }

    public class SetupRegistryRecord
    {
        public string Hive { get; set; } = "HKLM";
        public string KeyPath { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public string ValueData { get; set; } = string.Empty;
        public string ChangeKind { get; set; } = "KeyAdded"; // KeyAdded, ValueAdded, ValueModified
        public bool IsAutorunOrService { get; set; }
    }

    public class WatchedSetupSession
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
        public int RootProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string InstallerPath { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }
        public bool IsActive { get; set; } = true;

        public HashSet<int> TrackedProcessIds { get; } = new();
        public ConcurrentBag<SetupFileEvent> CapturedFileEvents { get; } = new();
        public InstallationSnapshot? PreSnapshot { get; set; }
    }

    public class SetupDeltaReport
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
        public string AppName { get; set; } = string.Empty;
        public string InstallerPath { get; set; } = string.Empty;
        public DateTime InstallTime { get; set; } = DateTime.UtcNow;
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;

        public List<string> AddedFiles { get; set; } = new();
        public List<string> AddedFolders { get; set; } = new();
        public List<string> AddedExecutables { get; set; } = new();
        public List<SetupRegistryRecord> AddedRegistryRecords { get; set; } = new();
        public List<string> AddedServices { get; set; } = new();
        public List<string> AddedStartupEntries { get; set; } = new();

        public long TotalSizeBytes { get; set; }
        public string FormattedSize { get; set; } = "0 MB";
        public bool IsProfileSaved { get; set; }
        public bool ThreatScanRequested { get; set; }
    }
}
