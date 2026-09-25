using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Bakım.Core.History
{
    /// <summary>Analiz nereden başlatıldı? (Geçmiş'te kaynak çipi olarak görünür.)</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<AnalysisSource>))]
    public enum AnalysisSource
    {
        Unknown,
        Analyzer,
        ContextMenu,
        DragDrop,
        SetupSentinel,
        Uninstaller,
        Processes,
        NetworkMonitor,
        Startup,
        Scheduled
    }

    [JsonConverter(typeof(JsonStringEnumConverter<AnalysisVerdict>))]
    public enum AnalysisVerdict
    {
        Clean,
        Info,
        Caution,
        Suspicious,
        Dangerous,
        Missing
    }

    /// <summary>Kullanıcının bir analiz sonucu için verdiği karar.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<UserDecision>))]
    public enum UserDecision
    {
        None,
        Trusted,
        Quarantined,
        Deleted,
        Disabled,
        Ignored
    }

    public sealed record AnalysisFactorSummary(string Title, string Severity, int ScoreImpact);

    /// <summary>
    /// Bir dosya analizinin kalıcı kaydı. records.jsonl dosyasında satır başına bir kayıt;
    /// karar/not güncellemesi aynı <see cref="Id"/> ile yeni satır olarak eklenir, son satır geçerlidir.
    /// </summary>
    public sealed record AnalysisRecord
    {
        public int SchemaVersion { get; init; } = 1;
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public DateTime AnalyzedAtUtc { get; init; }
        public AnalysisSource Source { get; init; }
        public string? SourceDetail { get; init; }
        public string FilePath { get; init; } = "";
        public string FileName { get; init; } = "";
        public long FileSizeBytes { get; init; }
        public string Sha256 { get; init; } = "";
        public string? Md5 { get; init; }
        public string? Sha1 { get; init; }
        public DateTime? FileLastWriteUtc { get; init; }
        public string SignatureStatus { get; init; } = "";
        public string? Signer { get; init; }
        public bool IsCatalogSigned { get; init; }
        public string? CompanyName { get; init; }
        public string? ProductName { get; init; }
        public string? FileVersion { get; init; }
        public int RiskScore { get; init; }
        public AnalysisVerdict Verdict { get; init; }
        public IReadOnlyList<AnalysisFactorSummary> Factors { get; init; } = Array.Empty<AnalysisFactorSummary>();
        public string? Recommendation { get; init; }
        public int? VirusTotalMalicious { get; init; }
        public int? VirusTotalTotal { get; init; }
        public DateTime? VirusTotalCheckedAtUtc { get; init; }
        public string? MotwHostUrl { get; init; }
        public string? PersistenceLocation { get; init; }
        public UserDecision Decision { get; init; }
        public DateTime? DecisionAtUtc { get; init; }
        public string? Note { get; init; }

        [JsonIgnore]
        public bool HasVirusTotalHit => VirusTotalMalicious is > 0;

        [JsonIgnore]
        public bool IsKeptForever => Decision is UserDecision.Trusted or UserDecision.Quarantined;
    }

    /// <summary>Bir kalıcılık (başlangıç/görev/hizmet…) girdisinin tarama anındaki hali.</summary>
    public sealed record PersistenceEntry(
        string Key,
        string Category,
        string Name,
        string Location,
        string FilePath,
        string Arguments,
        bool IsEnabled,
        string SignatureStatus,
        string? Signer,
        string? Sha256);

    public sealed record PersistenceSnapshot
    {
        public int SchemaVersion { get; init; } = 1;
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public DateTime TakenAtUtc { get; init; }
        public string Trigger { get; init; } = "Manual";
        public IReadOnlyList<PersistenceEntry> Entries { get; init; } = Array.Empty<PersistenceEntry>();
    }

    /// <summary>Anlık görüntü listesinde gösterilen başlık (girdiler yüklenmeden).</summary>
    public sealed record SnapshotHeader(string Id, DateTime TakenAtUtc, string Trigger, int EntryCount, string FileName);

    [JsonConverter(typeof(JsonStringEnumConverter<DiffKind>))]
    public enum DiffKind
    {
        Added,
        Removed,
        Changed
    }

    public sealed record SnapshotDiffItem(DiffKind Kind, PersistenceEntry? Before, PersistenceEntry? After, IReadOnlyList<string> ChangedFields)
    {
        public PersistenceEntry Current => After ?? Before!;
    }

    public enum HistoryDateRange
    {
        All,
        Today,
        Last7Days,
        Last30Days
    }

    /// <summary>Geçmiş sekmesindeki filtre çubuğu.</summary>
    public sealed record AnalysisHistoryFilter
    {
        public string? Text { get; init; }
        public HistoryDateRange Range { get; init; } = HistoryDateRange.All;
        public IReadOnlyCollection<AnalysisVerdict>? Verdicts { get; init; }
        public AnalysisSource? Source { get; init; }
        public string? SignatureStatus { get; init; }
        public bool OnlyVirusTotalHits { get; init; }
        public bool OnlyUndecided { get; init; }
        public int Limit { get; init; } = 5000;

        public static AnalysisHistoryFilter All { get; } = new();
    }
}
