using System;
using System.Collections.Generic;

namespace Bakım.Models
{
    public enum ThreatSeverity
    {
        Clean,
        Info,
        Warning,
        Critical
    }

    public class ThreatFactor
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ThreatSeverity Severity { get; set; } = ThreatSeverity.Info;
        public int ScoreImpact { get; set; }

        public string IconSymbol => Severity switch
        {
            ThreatSeverity.Critical => "DismissCircle24",
            ThreatSeverity.Warning => "Warning24",
            ThreatSeverity.Clean => "CheckmarkCircle24",
            _ => "Info24"
        };

        public string SeverityBadgeText => Severity switch
        {
            ThreatSeverity.Critical => "KRİTİK",
            ThreatSeverity.Warning => "ŞÜPHELİ",
            ThreatSeverity.Clean => "GÜVENLİ",
            _ => "BİLGİ"
        };

        public string SeverityColor => Severity switch
        {
            ThreatSeverity.Critical => "#EF4444",
            ThreatSeverity.Warning => "#F59E0B",
            ThreatSeverity.Clean => "#10B981",
            _ => "#38BDF8"
        };

        public string SeverityBackground => Severity switch
        {
            ThreatSeverity.Critical => "#20EF4444",
            ThreatSeverity.Warning => "#20F59E0B",
            ThreatSeverity.Clean => "#2010B981",
            _ => "#2038BDF8"
        };
    }

    public class ThreatAnalysisResult
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileSizeFormatted { get; set; } = "0 B";
        public long FileSizeBytes { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public double EntropyScore { get; set; }
        public string EntropyText { get; set; } = string.Empty;

        public int RiskScore { get; set; } // 0 - 100

        public string RiskLevelText => RiskScore switch
        {
            <= 25 => "Düşük Risk / Güvenli",
            <= 60 => "Orta Risk / Şüpheli - İnceleyin",
            _ => "YÜKSEK RİSK / TEHLİKELİ ZARARLI"
        };

        public string RiskColor => RiskScore switch
        {
            <= 25 => "#10B981",
            <= 60 => "#F59E0B",
            _ => "#EF4444"
        };

        public string RiskBackgroundBrush => RiskScore switch
        {
            <= 25 => "#1A10B981",
            <= 60 => "#1AF59E0B",
            _ => "#25EF4444"
        };

        // Active process info
        public bool IsActiveProcess { get; set; }
        public int ActiveProcessId { get; set; }
        public string ActiveProcessMemory { get; set; } = string.Empty;

        // Signature & Metadata
        public bool IsSigned { get; set; }
        public string DigitalSignatureText { get; set; } = "İmzasız";
        public string SignerName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string FileVersion { get; set; } = string.Empty;

        // Mark of the Web
        public bool HasMarkOfTheWeb { get; set; }
        public string ZoneSourceUrl { get; set; } = string.Empty;

        // VirusTotal
        public string VirusTotalSummary { get; set; } = "Taranmadı";
        public int VirusTotalMalicious { get; set; } = -1;
        public int VirusTotalTotal { get; set; } = -1;

        // Analysis reasons & recommendation
        public List<ThreatFactor> Factors { get; set; } = new();
        public string Recommendation { get; set; } = string.Empty;
        public PersistenceItem? OriginAutorunItem { get; set; }
    }
}
