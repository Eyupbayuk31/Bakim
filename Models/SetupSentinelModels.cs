using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Bakım.Models
{
    public enum SessionKind
    {
        Install,
        Uninstall,
        Update,
        Unknown
    }

    public class SetupFileEvent
    {
        public string FilePath { get; set; } = string.Empty;
        public string ChangeType { get; set; } = "Created"; // Created, Changed, Deleted, Renamed
        public string? OldFilePath { get; set; } // For Renamed events
        public long SizeBytes { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool IsExecutable { get; set; }
        public string Extension { get; set; } = string.Empty;
    }

    public class SetupRegistryRecord
    {
        public string Hive { get; set; } = "HKLM"; // HKLM, HKCU
        public string KeyPath { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public string ValueData { get; set; } = string.Empty;
        public string ChangeKind { get; set; } = "KeyAdded"; // KeyAdded, KeyDeleted, ValueAdded, ValueModified, ValueDeleted
        public bool IsAutorunOrService { get; set; }
    }

    public class WatchedSetupSession
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
        public int RootProcessId { get; set; }
        public long RootProcessCreationTicks { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string InstallerPath { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public SessionKind Kind { get; set; } = SessionKind.Install;
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsPossiblyIncomplete { get; set; }

        public ConcurrentDictionary<(int Pid, long CreationTimeTicks), bool> TrackedProcesses { get; } = new();
        public HashSet<int> TrackedProcessIds { get; } = new(); // Backward compatibility helper
        public ConcurrentBag<SetupFileEvent> CapturedFileEvents { get; } = new();
        public InstallationSnapshot? PreSnapshot { get; set; }
    }

    public class SetupDeltaReport
    {
        public int SchemaVersion { get; set; } = 2;
        public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
        public SessionKind Kind { get; set; } = SessionKind.Install;
        public string AppName { get; set; } = string.Empty;
        public string InstallerPath { get; set; } = string.Empty;
        public DateTime InstallTime { get; set; } = DateTime.UtcNow;
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;

        // Dosya Ayrımı (P0-1)
        public List<string> AddedFiles { get; set; } = new(); // CreatedFiles eşdeğeri (geriye uyumluluk)
        public List<string> CreatedFiles { get; set; } = new();
        public List<string> ModifiedFiles { get; set; } = new();
        public List<string> DeletedFiles { get; set; } = new();
        public List<string> RenamedFiles { get; set; } = new();
        public List<string> AddedFolders { get; set; } = new();
        public List<string> AddedExecutables { get; set; } = new();

        // Kayıt Defteri ve Sistem
        public List<SetupRegistryRecord> AddedRegistryRecords { get; set; } = new();
        public List<string> AddedServices { get; set; } = new();
        public List<string> AddedStartupEntries { get; set; } = new();

        // Özet & Risk (Faz 0 Hızlı Kazanım)
        public long TotalSizeBytes { get; set; }
        public string FormattedSize { get; set; } = "0 MB";
        public bool IsProfileSaved { get; set; }
        public bool ThreatScanRequested { get; set; }
        public bool IsPossiblyIncomplete { get; set; }
        public string QuickRiskSummary { get; set; } = string.Empty;
        public string RiskBadgeBrush { get; set; } = "AccentTextFillColorPrimaryBrush";
    }
}
