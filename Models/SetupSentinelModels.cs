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
        /// <summary>Yakalanma sırası. Olaylar eşzamanlı torbada tutulduğu için sıra buradan okunur.</summary>
        public long Sequence { get; set; }
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
        private volatile bool _isActive = true;
        public bool IsActive { get => _isActive; set => _isActive = value; }
        public bool IsPossiblyIncomplete { get; set; }

        public ConcurrentDictionary<(int Pid, long CreationTimeTicks), bool> TrackedProcesses { get; } = new();
        /// <summary>Ağaçtaki PID'ler. Nöbetçi döngüsü ve WMI iş parçacığı birlikte yazar.</summary>
        public ConcurrentDictionary<int, byte> TrackedProcessIds { get; } = new();
        /// <summary>Kurulumun sonunda başlattığı uygulama: ağaçtan ayrıldı, oturumu açık tutmaz (NÖB v3 A10).</summary>
        public ConcurrentDictionary<int, byte> DetachedProcessIds { get; } = new();
        public ConcurrentBag<SetupFileEvent> CapturedFileEvents { get; } = new();

        /// <summary>Kurulum dosyasının imza, özet, çatı ve indirme kaynağı (arka planda doldurulur).</summary>
        public System.Threading.Tasks.Task<InstallerInfo>? InstallerInfoTask { get; set; }
    }

    /// <summary>Kurulum dosyası hakkında bilinenler (NÖB 1.3, 1.4).</summary>
    public class InstallerInfo
    {
        public string? Sha256 { get; set; }
        /// <summary>Geçerli imza: true/false; denetlenemediyse null.</summary>
        public bool? Signed { get; set; }
        public string? Signer { get; set; }
        public string SignatureText { get; set; } = string.Empty;
        public Bakım.Core.Sentinel.InstallerFramework Framework { get; set; }
        public string? HostUrl { get; set; }
        public string? ReferrerUrl { get; set; }
        public string? SourceSite { get; set; }
        public bool FromInternet { get; set; }

        public string FrameworkText => Bakım.Core.Sentinel.InstallerFingerprint.DisplayName(Framework);
    }

    public class SetupDeltaReport
    {
        public int SchemaVersion { get; set; } = 3;
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
        /// <summary>Kurulumun oluşturup yine sildiği geçici öğe sayısı (rapora tek tek girmez).</summary>
        public int TempFileCount { get; set; }

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

        // Nöbetçi v2 (şema 3)
        public InstallerInfo? Installer { get; set; }
        /// <summary>IFEO, Winlogon, PATH, proxy, sertifika, hosts, güvenlik duvarı, görev, sağ tık … değişiklikleri.</summary>
        public List<Bakım.Core.Sentinel.SystemChange> SystemChanges { get; set; } = new();
        /// <summary>Oturumda yeni oluşan Uninstall kayıtları (paket yazılım tespiti).</summary>
        public List<string> NewPrograms { get; set; } = new();
        public bool RiskEvaluated { get; set; }
        public int RiskScore { get; set; }
        public Bakım.Core.Sentinel.RiskVerdict RiskVerdict { get; set; }
        public List<Bakım.Core.Sentinel.RiskFinding> RiskFindings { get; set; } = new();
    }

    /// <summary>Kurulum kararının arayüz karşılıkları (RiskBadge seviyesi, açıklama).</summary>
    public static class SetupRiskPresentation
    {
        public static RiskLevel ToLevel(Bakım.Core.Sentinel.RiskVerdict verdict) => verdict switch
        {
            Bakım.Core.Sentinel.RiskVerdict.Info => RiskLevel.Low,
            Bakım.Core.Sentinel.RiskVerdict.Caution => RiskLevel.Medium,
            Bakım.Core.Sentinel.RiskVerdict.Suspicious => RiskLevel.High,
            Bakım.Core.Sentinel.RiskVerdict.Dangerous => RiskLevel.Critical,
            _ => RiskLevel.Clean
        };

        /// <summary>
        /// Rapor bildirime değer mi? Dosya bırakmayan ama başlangıç girdisi, hizmet, sertifika,
        /// Defender istisnası gibi değişiklik yapan kurulumlar da bildirilir (NÖB v3 A1).
        /// </summary>
        public static bool HasReportableChanges(SetupDeltaReport report) =>
            report.CreatedFiles.Count > 0 || report.AddedFiles.Count > 0 || report.AddedExecutables.Count > 0 ||
            report.AddedStartupEntries.Count > 0 || report.AddedServices.Count > 0 ||
            report.SystemChanges.Count > 0 || report.NewPrograms.Count > 0 ||
            (report.RiskEvaluated && report.RiskVerdict >= Bakım.Core.Sentinel.RiskVerdict.Caution);

        public static string VerdictText(SetupDeltaReport report) =>
            report.RiskEvaluated ? Bakım.Core.Sentinel.SetupRiskEngine.VerdictLabel(report.RiskVerdict) : "Değerlendirilmedi";

        public static string VerdictDetail(SetupDeltaReport report)
        {
            if (!report.RiskEvaluated)
                return "Bu rapor risk değerlendirmesinden önceki bir sürümle oluşturuldu.";
            int shown = report.RiskFindings.Count(f => f.Severity > Bakım.Core.Sentinel.RiskSeverity.Info);
            string basis = report.RiskVerdict switch
            {
                Bakım.Core.Sentinel.RiskVerdict.Clean => "Kalıcılık, hizmet ya da sistem ayarı değişikliği saptanmadı.",
                Bakım.Core.Sentinel.RiskVerdict.Info => "Olağan kurulum izleri (başlangıç girdisi, hizmet …) var; tek başına tehlike işareti değil.",
                Bakım.Core.Sentinel.RiskVerdict.Caution => "Gözden geçirilmesi gereken değişiklikler var.",
                Bakım.Core.Sentinel.RiskVerdict.Suspicious => "Kötü amaçlı yazılımlarda sık görülen değişiklikler var; ayrıntıları inceleyin.",
                _ => "Birden çok ciddi sistem değişikliği var; kurulumu Analizör'de taramanız önerilir."
            };
            return $"{basis} Puan {report.RiskScore}/100 · {shown} bulgu. Karar, kurulum sırasında gözlenen değişikliklere dayanır; dosya içeriği taranmadı.";
        }
    }
}

