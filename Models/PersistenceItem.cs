using System;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Models
{
    public enum PersistenceCategory
    {
        RegistryRun,
        StartupFolder,
        ScheduledTask,
        WindowsService,
        WmiEventConsumer,
        WinlogonIfeo,
        ShellExtension,
        /// <summary>Çekirdek ve dosya sistemi sürücüleri (hizmetlerden ayrı; hiçbir zaman değiştirilemez).</summary>
        Driver,
        /// <summary>Kurulum Nöbetçisi ya da kullanıcının incelettiği dosya: başlangıç kaydı değil.</summary>
        InspectedFile,
        /// <summary>AppInit/AppCert DLL, LSA paketi, yazdırma izleyicisi, Winsock, Netsh, BootExecute, PowerShell profili.</summary>
        SystemHook,
        BrowserExtension,
        OfficeAddin
    }

    public enum SignatureStatus
    {
        Verified,
        InvalidOrTampered,
        Unsigned
    }

    public partial class PersistenceItem : ObservableObject
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public PersistenceCategory Category { get; set; }
        public string CategoryDisplayName { get; set; } = string.Empty;
        public string LocationSource { get; set; } = string.Empty; // e.g., HKLM\...\Run or Task: \GoogleUpdateTask

        /// <summary>
        /// Başlangıç klasörü girdisinde kısayolun KENDİ yolu (.lnk). <see cref="FilePath"/> kısayolun
        /// hedefidir (asıl program); devre dışı bırakma ve silme yalnızca bu dosyaya uygulanır.
        /// </summary>
        public string? SourceFilePath { get; set; }

        /// <summary>Kayıt defteri girdisinin tam anahtarı (ör. HKLM\Software\WOW6432Node\...\Run).</summary>
        public string? RegistryKeyPath { get; set; }

        /// <summary>Kayıt defteri girdisinin gerçek değer adı.</summary>
        public string? RegistryValueName { get; set; }
        /// <summary>Kayıttaki ham komut (çözümlemeden önce).</summary>
        public string RawCommand { get; set; } = string.Empty;
        /// <summary>Sarmalayıcı süreç (rundll32, regsvr32, cmd, powershell, wscript, svchost …); <see cref="FilePath"/> asıl yüktür.</summary>
        public string? HostName { get; set; }
        public bool HasHost => !string.IsNullOrEmpty(HostName);
        public string HostDisplay => HostName == null ? string.Empty
            : InlineScript != null && string.IsNullOrEmpty(FilePath) ? $"{HostName} ile satır içi komut"
            : $"{HostName} aracılığıyla çalışır";
        /// <summary>Komut satırına gömülü betik ya da adres (cmd /c, powershell -enc çözülmüş, regsvr32 /i:url).</summary>
        public string? InlineScript { get; set; }
        /// <summary>Komut çözümlenip dosya bulundu mu?</summary>
        public Bakım.Core.Analyzer.TargetResolution Resolution { get; set; } = Bakım.Core.Analyzer.TargetResolution.Resolved;
        /// <summary>Zamanlanmış görevin tam yolu (\Klasör\Ad).</summary>
        public string? TaskPath { get; set; }
        public string? ServiceName { get; set; }
        /// <summary>Hizmet "Start" değeri (0 Boot … 4 Devre dışı).</summary>
        public int? ServiceStart { get; set; }
        public int? ServiceType { get; set; }
        /// <summary>Hizmet Bakım tarafından kapatıldı (özgün başlangıç türü saklı).</summary>
        public bool DisabledByBakim { get; set; }

        public Bakım.Core.Analyzer.AutorunFacts Facts => new(
            Enum.TryParse<Bakım.Core.Analyzer.AutorunCategory>(Category.ToString(), out var c) ? c : Bakım.Core.Analyzer.AutorunCategory.InspectedFile,
            RegistryKeyPath, SourceFilePath, TaskPath, ServiceName, ServiceStart, ServiceType, DisabledByBakim);

        /// <summary>Açma/kapama güvenli mi (ANL Z1)? Arayüz anahtarı yalnızca bu true iken gösterir.</summary>
        public bool CanToggle => Bakım.Core.Analyzer.AutorunActionPolicy.CanToggle(Facts).Allowed;
        public string ToggleLockReason => Bakım.Core.Analyzer.AutorunActionPolicy.CanToggle(Facts).Reason;
        public bool CanDelete => Bakım.Core.Analyzer.AutorunActionPolicy.CanDelete(Facts).Allowed;
        public string DeleteLockReason => Bakım.Core.Analyzer.AutorunActionPolicy.CanDelete(Facts).Reason;

        public string Publisher { get; set; } = string.Empty;
        public SignatureStatus Signature { get; set; } = SignatureStatus.Unsigned;
        public string SignatureSignerName { get; set; } = string.Empty;
        public string Sha256Hash { get; set; } = string.Empty;
        public bool IsMicrosoft { get; set; }
        public ImageSource? IconSource { get; set; }

        [ObservableProperty]
        private bool _isEnabled = true;

        /// <summary>
        /// VirusTotal tarama DURUMU. Varsayılanı "Analiz Et" idi; bu iki sorun
        /// doğuruyordu:
        ///   1. Emir kipindeki bir ifade tıklanamayan bir durum rozetinde
        ///      duruyor, kullanıcı butona benzettiği için tıklamaya çalışıyordu.
        ///   2. Toplu tarama filtresi "Taranmadı" değerini arıyordu; varsayılan
        ///      eşleşmediği için hiç taranmamış girdiler toplu taramada
        ///      sessizce atlanıyordu.
        /// Durum rozeti durumu anlatır, eylem emretmez.
        /// </summary>
        [ObservableProperty]
        private string _virusTotalScore = "Taranmadı";

        [ObservableProperty]
        private int _maliciousDetections = -1;

        public int VirusTotalPositives
        {
            get => MaliciousDetections;
            set => MaliciousDetections = value;
        }

        [ObservableProperty]
        private int _totalScanners = -1;

        [ObservableProperty]
        private bool _isBusy;

        /// <summary>
        /// Dosya diskte var mı? Tarama sırasında bir kez belirlenir; eskiden her okumada File.Exists
        /// çağrılıyor, sıralama ve istatistik yüzlerce kez diske gidiyordu (AN-19).
        /// </summary>
        public bool HasValidFile
        {
            get => _hasValidFile ??= !string.IsNullOrWhiteSpace(FilePath) && System.IO.File.Exists(FilePath);
            set => _hasValidFile = value;
        }
        private bool? _hasValidFile;

        /// <summary>
        /// Dijital imza bu girdi için anlamlı mı? Tarayıcı eklentisi, betik profili gibi PE olmayan
        /// girdilerde "imzasız" puanı uygulanmaz (her eklenti gürültü üretirdi).
        /// </summary>
        public bool SignatureApplicable { get; set; } = true;

        /// <summary>Toplayıcının kendi bulguları (ör. politika ile zorla kurulan eklenti): puan ve açıklama.</summary>
        public int CollectorRisk { get; set; }
        public string? CollectorNotes { get; set; }
        public bool HasCollectorNotes => !string.IsNullOrWhiteSpace(CollectorNotes);

        /// <summary>Derin analizin puanı (varsa satır puanı budur: daha fazla kanıta dayanır).</summary>
        public int? DeepRiskScore { get; set; }

        /// <summary>VirusTotal toplam motor sayısı biliniyorsa uzlaşı tabanı uygulanır.</summary>
        public int VirusTotalSuspicious { get; set; }

        /// <summary>Risk ve imza özelliklerinin değiştiğini arayüze bildirir (derin analiz, VT sonrası).</summary>
        public void NotifyRiskChanged()
        {
            OnPropertyChanged(nameof(Signature));
            OnPropertyChanged(nameof(SignatureSignerName));
            OnPropertyChanged(nameof(RiskScore));
            OnPropertyChanged(nameof(RiskIntent));
            OnPropertyChanged(nameof(RiskLabel));
            OnPropertyChanged(nameof(RiskIconSymbol));
            OnPropertyChanged(nameof(IsRisk));
        }
        public bool IsRisk => Signature != SignatureStatus.Verified || MaliciousDetections > 0;

        #region Zaman Çizelgesi

        /// <summary>Hedef dosyanın oluşturulma zamanı (UTC). Tarama sonrası doldurulur.</summary>
        public DateTime? FileCreatedUtc { get; set; }

        /// <summary>
        /// Son 7 günde eklenmiş kalıcılık girdisi. Taze başlangıç girdileri
        /// bulaşmanın en güvenilir erken sinyallerinden biridir: kullanıcı
        /// "dün ne değişti?" sorusunu tek bakışta yanıtlayabilmelidir.
        /// </summary>
        public bool IsRecentlyAdded =>
            FileCreatedUtc.HasValue && (DateTime.UtcNow - FileCreatedUtc.Value).TotalDays <= 7;

        public string CreatedDisplay =>
            FileCreatedUtc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";

        public string AgeDisplay
        {
            get
            {
                if (!FileCreatedUtc.HasValue) return "Bilinmiyor";

                var age = DateTime.UtcNow - FileCreatedUtc.Value;
                if (age.TotalDays < 1) return "Bugün eklendi";
                if (age.TotalDays < 2) return "Dün eklendi";
                if (age.TotalDays < 7) return $"{(int)age.TotalDays} gün önce";
                if (age.TotalDays < 30) return $"{(int)(age.TotalDays / 7)} hafta önce";
                if (age.TotalDays < 365) return $"{(int)(age.TotalDays / 30)} ay önce";
                return $"{(int)(age.TotalDays / 365)} yıl önce";
            }
        }

        #endregion

        #region Risk Puanlaması

        /// <summary>
        /// 0-100 arası sezgisel risk skoru. Listeyi sıralamak için kullanılır:
        /// en tehlikeli girdi en üstte durur, kullanıcı aramak zorunda kalmaz.
        /// </summary>
        public int RiskScore
        {
            get
            {
                if (DeepRiskScore is int deep) return IsEnabled ? deep : (int)(deep * 0.4);

                int score = CollectorRisk;

                if (SignatureApplicable) score += Signature switch
                {
                    SignatureStatus.InvalidOrTampered => 60,  // imza var ama bozuk: en kötü işaret
                    SignatureStatus.Unsigned => 35,
                    _ => 0
                };

                // VirusTotal uzlaşısı baskın kanıttır (ANL Z4): imza bunu düşüremez.
                var vt = Bakım.Core.Analyzer.VirusTotalRules.Evaluate(Math.Max(0, MaliciousDetections), VirusTotalSuspicious, TotalScanners > 0 ? TotalScanners : 70);
                if (vt is { } floor) score = Math.Max(score + floor.Impact, floor.MinimumScore);

                // Yüksek değerli kalıcılık vektörleri: meşru yazılım burayı nadiren kullanır
                score += Category switch
                {
                    PersistenceCategory.WmiEventConsumer => 20,
                    PersistenceCategory.WinlogonIfeo => 20,
                    PersistenceCategory.ShellExtension => 10,
                    _ => 0
                };

                if (IsRecentlyAdded) score += 10;
                if (SignatureApplicable && !IsMicrosoft && Signature == SignatureStatus.Unsigned) score += 5;

                // Dosya artık yoksa girdi bozuk demektir — tehdit değil, temizlik konusu
                // İmza puanı düşer; yalnızca konumun kendi riski (WMI, Winlogon …) ve toplayıcı bulguları kalır.
                if (!string.IsNullOrWhiteSpace(FilePath) && !HasValidFile) score = Math.Clamp(score - 40, 5, 20);

                // Devre dışı girdi çalışmıyor: risk büyük ölçüde düşer
                if (!IsEnabled) score = (int)(score * 0.4);

                return Math.Clamp(score, 0, 100);
            }
        }

        public Intent RiskIntent => RiskScore switch
        {
            >= 60 => Intent.Critical,
            >= 30 => Intent.Caution,
            > 0 => Intent.Accent,
            _ => Intent.Success
        };

        public string RiskLabel => RiskScore switch
        {
            >= 60 => "YÜKSEK",
            >= 30 => "ORTA",
            > 0 => "DÜŞÜK",
            _ => "GÜVENLİ"
        };

        /// <summary>Renk körü kullanıcılar için renkten bağımsız risk simgesi.</summary>
        public string RiskIconSymbol => RiskScore switch
        {
            >= 60 => "ShieldDismiss24",
            >= 30 => "ShieldError24",
            _ => "ShieldCheckmark24"
        };

        #endregion
    }
}
