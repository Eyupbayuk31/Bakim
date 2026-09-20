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

        /// <summary>
        /// Anlamsal ton. Model KATMANINDA RENK DÖNDÜRÜLMEZ: renk seçimi XAML
        /// trigger'larında tema fırçalarıyla yapılır. Aksi halde bu rozetler
        /// tema değişimine tepki veremez ve Fluent Açık temada okunamaz hale gelir
        /// (ör. #10B981 beyaz üzerinde 2.2:1 — WCAG AA sınırı 4.5:1).
        /// </summary>
        public Intent SeverityIntent => Severity switch
        {
            ThreatSeverity.Critical => Intent.Critical,
            ThreatSeverity.Warning => Intent.Caution,
            ThreatSeverity.Clean => Intent.Success,
            _ => Intent.Accent
        };
    }

    public class PeHeaderInfo
    {
        public bool IsPeFile { get; set; }
        public string MachineArchitecture { get; set; } = "Bilinmiyor";
        public string Subsystem { get; set; } = "Bilinmiyor";
        public string CompileTimeUtc { get; set; } = "Bilinmiyor";
        public uint EntryPointRva { get; set; }
        public string EntryPointHex => $"0x{EntryPointRva:X8}";
        public ulong ImageBase { get; set; }
        public string ImageBaseHex => $"0x{ImageBase:X16}";
        public int SectionCount { get; set; }
        public bool Is64Bit { get; set; }
    }

    public class ExploitMitigationMatrix
    {
        public bool HasAslr { get; set; }
        public bool HasDep { get; set; }
        public bool HasCfg { get; set; }
        public bool HasHighEntropyVa { get; set; }
        public bool HasSafeSeh { get; set; }
        public bool IsDotNet { get; set; }

        public int ActiveMitigationCount
        {
            get
            {
                int count = 0;
                if (HasAslr) count++;
                if (HasDep) count++;
                if (HasCfg) count++;
                if (HasHighEntropyVa) count++;
                if (HasSafeSeh || IsDotNet) count++;
                return count;
            }
        }

        public string MitigationScoreText => $"{ActiveMitigationCount} / 5 Aktif Kalkan";

        public string SecurityGrade => ActiveMitigationCount switch
        {
            >= 4 => "A+ (Yüksek Koruma)",
            3 => "B (Standart Koruma)",
            2 => "C (Zayıf / Kısmi Koruma)",
            _ => "D (Korumasız / Savunmasız)"
        };

        public Intent SecurityGradeIntent => ActiveMitigationCount switch
        {
            >= 4 => Intent.Success,
            3 => Intent.Accent,
            2 => Intent.Caution,
            _ => Intent.Critical
        };
    }

    public class PeSectionItem
    {
        public string Name { get; set; } = string.Empty;
        public uint VirtualAddress { get; set; }
        public string VirtualAddressHex => $"0x{VirtualAddress:X8}";
        public uint VirtualSize { get; set; }
        public string VirtualSizeFormatted => FormatBytes(VirtualSize);
        public uint RawSize { get; set; }
        public string RawSizeFormatted => FormatBytes(RawSize);
        public double Entropy { get; set; }
        public string EntropyFormatted => $"{Entropy:F2} / 8.0";
        public bool IsExecutable { get; set; }
        public bool IsWritable { get; set; }
        public bool IsSuspiciousPacker { get; set; }

        public string PermissionsText
        {
            get
            {
                var perms = new List<string>();
                perms.Add("R"); // Read
                if (IsWritable) perms.Add("W");
                if (IsExecutable) perms.Add("X");
                return string.Join("", perms);
            }
        }

        /// <summary>7.3 üzeri entropi sıkıştırılmış/şifrelenmiş payload işaretidir.</summary>
        public Intent EntropyIntent => Entropy switch
        {
            >= 7.3 => Intent.Critical,
            >= 6.5 => Intent.Caution,
            _ => Intent.Success
        };

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    public class ImportedApiFunction
    {
        public string Name { get; set; } = string.Empty;
        public bool IsSuspicious { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Kategorinin tehlike tonu. Önceki sürümde her kategori kendi keyfi
        /// rengini (mor, pembe...) döndürüyordu; bu hem tema dışıydı hem de
        /// "mor ne demek?" sorusunu doğuruyordu. Artık renk TEHLİKE SEVİYESİNİ
        /// anlatır, kategori ayrımı ise ikon ve metinle yapılır.
        /// </summary>
        public Intent CategoryIntent => Category switch
        {
            "Bellek Enjeksiyonu" or "Process Hollowing" => Intent.Critical,
            "Klavye / Casusluk" => Intent.Critical,
            "Ağ / C2 İletişimi" => Intent.Caution,
            "Kalıcılık / Registry" => Intent.Caution,
            "Süreç / Token" => Intent.Caution,
            _ => Intent.Neutral
        };

        /// <summary>Kategoriyi renkten bağımsız ayırt eden simge (WCAG 1.4.1).</summary>
        public string CategoryIcon => Category switch
        {
            "Bellek Enjeksiyonu" or "Process Hollowing" => "Bug24",
            "Klavye / Casusluk" => "Eye24",
            "Ağ / C2 İletişimi" => "Globe24",
            "Kalıcılık / Registry" => "Pin24",
            "Süreç / Token" => "Key24",
            _ => "Code24"
        };
    }

    public class ImportedDllGroup
    {
        public string DllName { get; set; } = string.Empty;
        public List<ImportedApiFunction> Functions { get; set; } = new();
        public int TotalFunctions => Functions.Count;
        public int SuspiciousCount => Functions.Count(f => f.IsSuspicious);
        public bool HasSuspicious => SuspiciousCount > 0;
        public string SummaryText => $"{TotalFunctions} Fonksiyon" + (HasSuspicious ? $" ({SuspiciousCount} Şüpheli Win32 API)" : "");
    }

    public class ThreatAnalysisResult
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileSizeFormatted { get; set; } = "0 B";
        public long FileSizeBytes { get; set; }

        // Hashes
        public string Sha256 { get; set; } = string.Empty;
        public string Md5 { get; set; } = string.Empty;
        public string Sha1 { get; set; } = string.Empty;
        public string ImpHash { get; set; } = string.Empty;

        public double EntropyScore { get; set; }
        public string EntropyText { get; set; } = string.Empty;

        public int RiskScore { get; set; } // 0 - 100

        public string RiskLevelText => RiskScore switch
        {
            <= 25 => "Düşük Risk / Güvenli",
            <= 60 => "Orta Risk / Şüpheli - İnceleyin",
            _ => "YÜKSEK RİSK / TEHLİKELİ ZARARLI"
        };

        public Intent RiskIntent => RiskScore switch
        {
            <= 25 => Intent.Success,
            <= 60 => Intent.Caution,
            _ => Intent.Critical
        };

        /// <summary>Risk seviyesini renkten bağımsız anlatan simge.</summary>
        public string RiskIconSymbol => RiskScore switch
        {
            <= 25 => "ShieldCheckmark24",
            <= 60 => "ShieldError24",
            _ => "ShieldDismiss24"
        };

        // Active process info
        public bool IsActiveProcess { get; set; }
        public int ActiveProcessId { get; set; }
        public string ActiveProcessMemory { get; set; } = string.Empty;

        // Signature & Metadata
        public bool IsSigned { get; set; }
        public string DigitalSignatureText { get; set; } = "İmzasız";
        public string SignerName { get; set; } = string.Empty;

        /// <summary>İmza dosyaya gömülü değil, bir Windows kataloğundan (.cat) geliyor.</summary>
        public bool IsCatalogSigned { get; set; }

        /// <summary>İmzayı sağlayan katalog dosyasının yolu.</summary>
        public string SignatureCatalogPath { get; set; } = string.Empty;

        /// <summary>İmzalama sertifikasının geçerlilik bitişi.</summary>
        public DateTime? CertificateExpiry { get; set; }

        /// <summary>Sertifika süresi dolmuş mu? Zaman damgalı imzada dosya yine geçerlidir.</summary>
        public bool IsCertificateExpired { get; set; }

        public string SignatureSourceText => IsCatalogSigned ? "Windows Kataloğu" : "Gömülü Authenticode";
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

        // PE Binary Röntgen
        public bool HasPeAnalysis => PeHeader != null && PeHeader.IsPeFile;
        public PeHeaderInfo? PeHeader { get; set; }
        public ExploitMitigationMatrix? Mitigations { get; set; }
        public List<PeSectionItem> Sections { get; set; } = new();
        public List<ImportedDllGroup> ImportedDlls { get; set; } = new();
        public int TotalSuspiciousApisCount => ImportedDlls.Sum(d => d.SuspiciousCount);

        // Analysis reasons & recommendation
        public List<ThreatFactor> Factors { get; set; } = new();
        public string Recommendation { get; set; } = string.Empty;
        public PersistenceItem? OriginAutorunItem { get; set; }
    }
}
