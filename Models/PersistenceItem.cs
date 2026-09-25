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
        ShellExtension
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

        public bool HasValidFile => !string.IsNullOrWhiteSpace(FilePath) && System.IO.File.Exists(FilePath);
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
                int score = 0;

                score += Signature switch
                {
                    SignatureStatus.InvalidOrTampered => 60,  // imza var ama bozuk: en kötü işaret
                    SignatureStatus.Unsigned => 35,
                    _ => 0
                };

                if (MaliciousDetections > 0)
                    score += Math.Min(40, MaliciousDetections * 8);

                // Yüksek değerli kalıcılık vektörleri: meşru yazılım burayı nadiren kullanır
                score += Category switch
                {
                    PersistenceCategory.WmiEventConsumer => 20,
                    PersistenceCategory.WinlogonIfeo => 20,
                    PersistenceCategory.ShellExtension => 10,
                    _ => 0
                };

                if (IsRecentlyAdded) score += 10;
                if (!IsMicrosoft && Signature == SignatureStatus.Unsigned) score += 5;

                // Dosya artık yoksa girdi bozuk demektir — tehdit değil, temizlik konusu
                if (!string.IsNullOrWhiteSpace(FilePath) && !HasValidFile) score = Math.Max(score - 15, 5);

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
