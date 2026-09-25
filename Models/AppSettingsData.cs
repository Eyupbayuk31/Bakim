using System;
namespace Bakım.Models
{
    /// <summary>
    /// Uygulamanın kalıcı kullanıcı tercihleri.
    /// Tek kaynak: %LocalAppData%\Bakim\appsettings.json — yalnızca IAppSettingsService okur/yazar.
    /// </summary>
    public class AppSettingsData
    {
        /// <summary>Geriye dönük göç (migration) kararları için şema sürümü.</summary>
        public int SchemaVersion { get; set; } = 1;

        // --- Görünüm ---
        public string Theme { get; set; } = "MicaDark";
        public bool IsMicaEnabled { get; set; } = true;

        /// <summary>Vurgu rengi Windows'un vurgu rengini takip eder (§3.1).</summary>
        public bool FollowWindowsAccent { get; set; } = false;

        /// <summary>Animasyonları azalt: tüm geçiş süreleri sıfırlanır (§3.3). Yeniden başlatınca uygulanır.</summary>
        public bool ReduceMotion { get; set; } = false;

        // --- Performans & Otomasyon ---
        /// <summary>Canlı telemetri örnekleme aralığı (saniye). 1-10 arası kısıtlanır.</summary>
        public int RefreshIntervalSeconds { get; set; } = 2;

        /// <summary>Otomatik RAM temizleme aralığı (dakika). 0 = kapalı.</summary>
        public int AutoRamCleanIntervalMinutes { get; set; } = 0;

        public bool StartWithWindows { get; set; } = false;
        public bool MinimizeToTray { get; set; } = false;
        public bool NotifyOnHighRam { get; set; } = true;
        public bool AutoCleanOnExit { get; set; } = false;
        public bool AlwaysRunAsAdmin { get; set; } = false;
        public bool TaskSchedulerAutoStart { get; set; } = false;

        // --- Kaldırıcı & Bağlam Menüsü ---
        public bool PromptRestorePointBeforeUninstall { get; set; } = true;
        public bool CreateRestorePointOnUninstall { get; set; } = true;
        /// <summary>Windows Gezgini sağ tık "Bakım ile Kaldır" menüsü etkin mi (Varsayılan: true).</summary>
        public bool EnableShellContextMenu { get; set; } = true;

        // --- VirusTotal ---
        /// <summary>
        /// ESKİ ALAN — düz metin API anahtarı. Yalnızca tek seferlik göç için okunur,
        /// göçten sonra boşaltılır. Yerine <see cref="VirusTotalApiKeyProtected"/> kullanılır.
        /// </summary>
        public string VirusTotalApiKey { get; set; } = string.Empty;

        /// <summary>DPAPI (CurrentUser) ile şifrelenmiş, Base64 kodlu VirusTotal API anahtarı.</summary>
        public string VirusTotalApiKeyProtected { get; set; } = string.Empty;

        // --- Tanılama ---
        /// <summary>Ayrıntılı günlükleme (Debug seviyesi) açık mı.</summary>
        public bool VerboseLogging { get; set; } = false;

        // --- Kurulum Nöbetçisi (Sentinel Setup Guard) ---
        /// <summary>Yeni kurulum başlatıldığında arka planda otomatik algılayıp izleme.</summary>
        public bool IsSentinelSetupGuardEnabled { get; set; } = true;

        // --- Gezinme (Faz 4, §2.3) ---
        /// <summary>Açılışta son açık sayfayı yükle (kapalıysa her zaman Kontrol Paneli).</summary>
        public bool OpenLastModuleOnStartup { get; set; } = true;

        /// <summary>Son açık sayfanın gezinme anahtarı.</summary>
        public string LastModule { get; set; } = "Dashboard";

        /// <summary>Etkinlik Merkezi'nin en son görüldüğü an (okunmamış rozeti için).</summary>
        public DateTime? ActivitySeenUtc { get; set; }

        /// <summary>Kurulum Nöbetçisi sayfasının en son görüldüğü an.</summary>
        public DateTime? SentinelSeenUtc { get; set; }

        public AppSettingsData Clone() => (AppSettingsData)MemberwiseClone();
    }
}
