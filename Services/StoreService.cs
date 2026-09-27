using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IStoreService
    {
        List<StoreAppItem> GetCatalog();
        Task CheckInstalledStatusesAsync(IEnumerable<StoreAppItem> items);
        Task<bool> InstallAppAsync(StoreAppItem app, Action<int, string>? progress = null, CancellationToken cancellationToken = default);
        Task<bool> IsWingetAvailableAsync();

        /// <summary>winget ile güncellenebilecek kurulu paketler (§5.12 Güncellemeler).</summary>
        Task<(IReadOnlyList<Bakım.Core.Store.WingetUpgrade> Upgrades, string? Error)> GetUpgradesAsync(CancellationToken ct = default);

        /// <summary>Tek paketi sessizce günceller; sonuç kullanıcıya gösterilecek metinle döner.</summary>
        Task<(bool Success, string Message)> UpgradeAsync(Bakım.Core.Store.WingetUpgrade package, CancellationToken ct = default);
    }

    public class StoreService : IStoreService
    {
        private readonly ILogService _log;
        private static readonly HttpClient HttpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true
        })
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        static StoreService()
        {
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        }

        public StoreService(ILogService log)
        {
            _log = log;
        }

        public async Task<bool> IsWingetAvailableAsync()
        {
            // İlk çalıştırmada winget birkaç saniye sürebilir; eskiden 3 sn sonra ExitCode
            // okunmaya çalışılıp istisna "winget yok" sayılıyordu.
            var result = await Helpers.ProcessRunner.RunAsync("winget", new[] { "--version" }, TimeSpan.FromSeconds(20));
            return result.Succeeded;
        }

        public List<StoreAppItem> GetCatalog()
        {
            return new List<StoreAppItem>
            {
                // ==========================================
                // 1. RUNTIMES & SİSTEM KÜTÜPHANELERİ
                // ==========================================
                new()
                {
                    Id = "vcredist_all",
                    Name = "Visual C++ Çalışma Zamanları (2005–2022)",
                    Description = "Oyunların ve programların ihtiyaç duyduğu tüm Microsoft Visual C++ kütüphanelerini (x86 & x64) resmi winget paketlerinden kurar.",
                    Category = StoreCategory.Runtimes,
                    CategoryDisplayName = "Runtimes",
                    IconSymbol = "DeveloperBoard24",
                    Publisher = "Microsoft Corporation",
                    SizeText = "≈ 90 MB (12 paket)",
                    InstallerType = StoreInstallerType.Winget,
                    BundleWingetIds = new[]
                    {
                        "Microsoft.VCRedist.2005.x86", "Microsoft.VCRedist.2005.x64",
                        "Microsoft.VCRedist.2008.x86", "Microsoft.VCRedist.2008.x64",
                        "Microsoft.VCRedist.2010.x86", "Microsoft.VCRedist.2010.x64",
                        "Microsoft.VCRedist.2012.x86", "Microsoft.VCRedist.2012.x64",
                        "Microsoft.VCRedist.2013.x86", "Microsoft.VCRedist.2013.x64",
                        "Microsoft.VCRedist.2015+.x86", "Microsoft.VCRedist.2015+.x64"
                    },
                    RegistryDetectKeyword = "Visual C++ 2015-2022|Visual C++ v14"
                },
                new()
                {
                    Id = "directx_web_setup",
                    Name = "DirectX End-User Runtimes",
                    Description = "Oyunlar için gerekli olan Direct3D 9, 10, 11 ve D3DX eksik kütüphanelerini tamamlar.",
                    Category = StoreCategory.Runtimes,
                    CategoryDisplayName = "Runtimes",
                    IconSymbol = "Games24",
                    Publisher = "Microsoft Corporation",
                    SizeText = "285 KB (Web)",
                    InstallerType = StoreInstallerType.DirectXWeb,
                    RegistryDetectKeyword = "DirectX"
                },
                new()
                {
                    Id = "dotnet_desktop_8",
                    Name = ".NET Desktop Runtime 8.0 (LTS)",
                    Description = "Modern Windows masaüstü uygulamaları ve oyun araçları için uzun vadeli destekli .NET çalışma zamanı.",
                    Category = StoreCategory.Runtimes,
                    CategoryDisplayName = "Runtimes",
                    IconSymbol = "Code24",
                    Publisher = "Microsoft Corporation",
                    SizeText = "55 MB",
                    WingetId = "Microsoft.DotNet.DesktopRuntime.8",
                    RegistryDetectKeyword = "Microsoft Windows Desktop Runtime - 8."
                },
                new()
                {
                    Id = "dotnet_desktop_10",
                    Name = ".NET Desktop Runtime 10.0 (Latest)",
                    Description = "En yeni nesil yüksek performanslı C# ve WPF uygulamaları için .NET çalışma kütüphanesi.",
                    Category = StoreCategory.Runtimes,
                    CategoryDisplayName = "Runtimes",
                    IconSymbol = "Code24",
                    Publisher = "Microsoft Corporation",
                    SizeText = "58 MB",
                    WingetId = "Microsoft.DotNet.DesktopRuntime.10",
                    RegistryDetectKeyword = "Microsoft Windows Desktop Runtime - 10."
                },
                new()
                {
                    Id = "openal_audio",
                    Name = "OpenAL 3D Audio Library",
                    Description = "Özellikle 3D oyunlar ve ses simülasyonları için donanım hızlandırmalı ses kütüphanesi.",
                    Category = StoreCategory.Runtimes,
                    CategoryDisplayName = "Runtimes",
                    IconSymbol = "Speaker224",
                    Publisher = "Creative Labs",
                    SizeText = "1 MB",
                    WingetId = "OpenAL.OpenAL",
                    RegistryDetectKeyword = "OpenAL"
                },
                new()
                {
                    Id = "xna_framework_4",
                    Name = "Microsoft XNA Framework 4.0",
                    Description = "Klasik 2D/3D bağımsız oyunların (Terraria, Stardew vb.) ihtiyaç duyduğu oyun motoru kütüphanesi.",
                    Category = StoreCategory.Runtimes,
                    CategoryDisplayName = "Runtimes",
                    IconSymbol = "Games24",
                    Publisher = "Microsoft Corporation",
                    SizeText = "7 MB",
                    WingetId = "Microsoft.XNAFramework",
                    RegistryDetectKeyword = "Microsoft XNA Framework"
                },
                new()
                {
                    Id = "webview2_runtime",
                    Name = "Microsoft Edge WebView2 Runtime",
                    Description = "Modern masaüstü uygulamalarında gömülü web içeriği çalıştırmak için gerekli sistem bileşeni.",
                    Category = StoreCategory.Runtimes,
                    CategoryDisplayName = "Runtimes",
                    IconSymbol = "Globe24",
                    Publisher = "Microsoft Corporation",
                    SizeText = "120 MB",
                    WingetId = "Microsoft.EdgeWebView2Runtime",
                    RegistryDetectKeyword = "Microsoft Edge WebView2"
                },
                new()
                {
                    Id = "java_temurin_jre",
                    Name = "Java JRE (Eclipse Temurin 17)",
                    Description = "Minecraft ve Java tabanlı masaüstü yazılımları için optimize edilmiş resmi açık kaynak Java çalışma ortamı.",
                    Category = StoreCategory.Runtimes,
                    CategoryDisplayName = "Runtimes",
                    IconSymbol = "Code24",
                    Publisher = "Eclipse Foundation",
                    SizeText = "170 MB",
                    WingetId = "EclipseAdoptium.Temurin.17.JRE",
                    RegistryDetectKeyword = "Eclipse Temurin JRE with Hotspot 17|Eclipse Temurin JDK with Hotspot 17"
                },

                // ==========================================
                // 2. OYUN PLATFORMLARI & İSTEMCİLER
                // ==========================================
                new()
                {
                    Id = "steam",
                    Name = "Steam",
                    Description = "Dünyanın en popüler dijital oyun dağıtım, topluluk ve çok oyunculu platformu.",
                    Category = StoreCategory.Gaming,
                    CategoryDisplayName = "Oyun",
                    IconSymbol = "Games24",
                    Publisher = "Valve Corporation",
                    SizeText = "2 MB (Setup)",
                    WingetId = "Valve.Steam",
                    RegistryDetectKeyword = "Steam"
                },
                new()
                {
                    Id = "epic_games",
                    Name = "Epic Games Launcher",
                    Description = "Haftalık ücretsiz oyunlar, Fortnite, Unreal Engine ve geniş oyun kataloğu platformu.",
                    Category = StoreCategory.Gaming,
                    CategoryDisplayName = "Oyun",
                    IconSymbol = "Games24",
                    Publisher = "Epic Games Inc.",
                    SizeText = "140 MB",
                    WingetId = "EpicGames.EpicGamesLauncher",
                    RegistryDetectKeyword = "Epic Games Launcher"
                },
                new()
                {
                    Id = "discord_gaming",
                    Name = "Discord",
                    Description = "Oyuncular ve topluluklar için düşük gecikmeli kristal netliğinde sesli, görüntülü ve yazılı sohbet.",
                    Category = StoreCategory.Gaming,
                    CategoryDisplayName = "Oyun",
                    IconSymbol = "Headset24",
                    Publisher = "Discord Inc.",
                    SizeText = "90 MB",
                    WingetId = "Discord.Discord",
                    RegistryDetectKeyword = "Discord"
                },
                new()
                {
                    Id = "ea_app",
                    Name = "EA App (Electronic Arts)",
                    Description = "EA oyunları (FIFA, Battlefield, Apex Legends) için yeni nesil resmi Windows istemcisi.",
                    Category = StoreCategory.Gaming,
                    CategoryDisplayName = "Oyun",
                    IconSymbol = "Games24",
                    Publisher = "Electronic Arts",
                    SizeText = "60 MB",
                    WingetId = "ElectronicArts.EADesktop",
                    RegistryDetectKeyword = "EA app"
                },
                new()
                {
                    Id = "battle_net",
                    Name = "Battle.net",
                    Description = "Blizzard ve Activision oyunları (Call of Duty, World of Warcraft, Overwatch) istemcisi.",
                    Category = StoreCategory.Gaming,
                    CategoryDisplayName = "Oyun",
                    IconSymbol = "Games24",
                    Publisher = "Blizzard Entertainment",
                    SizeText = "5 MB (Setup)",
                    WingetId = "Blizzard.BattleNet",
                    RegistryDetectKeyword = "Battle.net"
                },
                new()
                {
                    Id = "ubisoft_connect",
                    Name = "Ubisoft Connect",
                    Description = "Assassin's Creed, Rainbow Six ve tüm Ubisoft oyunları için resmi ekosistem istemcisi.",
                    Category = StoreCategory.Gaming,
                    CategoryDisplayName = "Oyun",
                    IconSymbol = "Games24",
                    Publisher = "Ubisoft",
                    SizeText = "180 MB",
                    WingetId = "Ubisoft.Connect",
                    RegistryDetectKeyword = "Ubisoft Connect"
                },
                new()
                {
                    Id = "gog_galaxy",
                    Name = "GOG Galaxy",
                    Description = "DRM-free oyun kütüphanesi ve tüm platformları tek çatıda toplayan evrensel oyun başlatıcı.",
                    Category = StoreCategory.Gaming,
                    CategoryDisplayName = "Oyun",
                    IconSymbol = "Games24",
                    Publisher = "GOG.com",
                    SizeText = "240 MB",
                    WingetId = "GOG.Galaxy",
                    RegistryDetectKeyword = "GOG GALAXY"
                },
                new()
                {
                    Id = "msi_afterburner",
                    Name = "MSI Afterburner",
                    Description = "Oyun içi anlık FPS/sıcaklık OSD göstergesi, fan eğrisi optimizasyonu ve ekran kartı hız aşırtma aracı.",
                    Category = StoreCategory.Gaming,
                    CategoryDisplayName = "Oyun",
                    IconSymbol = "TopSpeed24",
                    Publisher = "Guru3D / MSI",
                    SizeText = "54 MB",
                    WingetId = "Guru3D.Afterburner",
                    RegistryDetectKeyword = "MSI Afterburner"
                },

                // ==========================================
                // 3. MÜZİK, MEDYA & YAYIN
                // ==========================================
                new()
                {
                    Id = "spotify",
                    Name = "Spotify",
                    Description = "Milyonlarca şarkı, podcast ve çalma listesine anında erişim sağlayan dijital müzik servisi.",
                    Category = StoreCategory.Music,
                    CategoryDisplayName = "Müzik & Medya",
                    IconSymbol = "MusicNote224",
                    Publisher = "Spotify AB",
                    SizeText = "95 MB",
                    WingetId = "Spotify.Spotify",
                    RegistryDetectKeyword = "Spotify"
                },
                new()
                {
                    Id = "vlc_player",
                    Name = "VLC Media Player",
                    Description = "MKV, MP4, AVI dahil neredeyse tüm video ve ses formatlarını harici codec aramadan sorunsuz oynatır.",
                    Category = StoreCategory.Music,
                    CategoryDisplayName = "Müzik & Medya",
                    IconSymbol = "Video24",
                    Publisher = "VideoLAN",
                    SizeText = "42 MB",
                    WingetId = "VideoLAN.VLC",
                    RegistryDetectKeyword = "VLC media player"
                },
                new()
                {
                    Id = "obs_studio",
                    Name = "OBS Studio",
                    Description = "Profesyonel video kaydı, ekran yakalama ve Twitch/YouTube için yüksek performanslı canlı yayın yazılımı.",
                    Category = StoreCategory.Music,
                    CategoryDisplayName = "Müzik & Medya",
                    IconSymbol = "Record24",
                    Publisher = "OBS Project",
                    SizeText = "130 MB",
                    WingetId = "OBSProject.OBSStudio",
                    RegistryDetectKeyword = "OBS Studio"
                },
                new()
                {
                    Id = "audacity",
                    Name = "Audacity",
                    Description = "Açık kaynak, çok kanallı profesyonel ses düzenleyici ve kayıt stüdyosu.",
                    Category = StoreCategory.Music,
                    CategoryDisplayName = "Müzik & Medya",
                    IconSymbol = "MicPulse24",
                    Publisher = "Audacity Team",
                    SizeText = "35 MB",
                    WingetId = "Audacity.Audacity",
                    RegistryDetectKeyword = "Audacity"
                },
                new()
                {
                    Id = "aimp_player",
                    Name = "AIMP Müzik Çalar",
                    Description = "Efsanevi kristal berraklığında 32-bit ses işleme motoruna ve ekolayzere sahip hafif müzik çalar.",
                    Category = StoreCategory.Music,
                    CategoryDisplayName = "Müzik & Medya",
                    IconSymbol = "MusicNote224",
                    Publisher = "Artem Izmaylov",
                    SizeText = "18 MB",
                    WingetId = "AIMP.AIMP",
                    RegistryDetectKeyword = "AIMP"
                },
                new()
                {
                    Id = "handbrake",
                    Name = "HandBrake",
                    Description = "Videoları kalitesini bozmadan sıkıştıran ve tüm cihazlara uygun formatlara dönüştüren açık kaynak araç.",
                    Category = StoreCategory.Music,
                    CategoryDisplayName = "Müzik & Medya",
                    IconSymbol = "ArrowSync24",
                    Publisher = "The HandBrake Team",
                    SizeText = "24 MB",
                    WingetId = "HandBrake.HandBrake",
                    RegistryDetectKeyword = "HandBrake"
                },
                new()
                {
                    Id = "sharex",
                    Name = "ShareX",
                    Description = "Ekran görüntüsü, bölge kaydı, GIF oluşturma ve otomatik yükleme yeteneklerine sahip açık kaynak araç.",
                    Category = StoreCategory.Music,
                    CategoryDisplayName = "Müzik & Medya",
                    IconSymbol = "Screenshot24",
                    Publisher = "ShareX Team",
                    SizeText = "32 MB",
                    WingetId = "ShareX.ShareX",
                    RegistryDetectKeyword = "ShareX"
                },
                new()
                {
                    Id = "screentogif",
                    Name = "ScreenToGif",
                    Description = "Ekranın belirli bir bölgesini, web kamerasını veya çizimleri kaydedip anında optimize edilmiş GIF ve videoya dönüştüren araç.",
                    Category = StoreCategory.Music,
                    CategoryDisplayName = "Müzik & Medya",
                    IconSymbol = "Record24",
                    Publisher = "Nicke Manarin",
                    SizeText = "70 MB",
                    WingetId = "NickeManarin.ScreenToGif",
                    RegistryDetectKeyword = "ScreenToGif"
                },
                new()
                {
                    Id = "lossless_cut",
                    Name = "LosslessCut",
                    Description = "Büyük video ve ses dosyalarını kalite kaybı olmadan ve yeniden kodlamadan saniyeler içinde kırpan süper hızlı araç.",
                    Category = StoreCategory.Music,
                    CategoryDisplayName = "Müzik & Medya",
                    IconSymbol = "Video24",
                    Publisher = "Mikael Finstad",
                    SizeText = "110 MB",
                    WingetId = "ch.LosslessCut",
                    RegistryDetectKeyword = "LosslessCut"
                },

                // ==========================================
                // 4. YAZILIM, SİSTEM & ARŞİV ARAÇLARI
                // ==========================================
                new()
                {
                    Id = "winrar",
                    Name = "WinRAR",
                    Description = "RAR ve ZIP arşivleri oluşturma, açma ve hasarlı arşivleri onarma konusunda dünya standardı.",
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
                    IconSymbol = "FolderZip24",
                    Publisher = "RARLab",
                    SizeText = "3.5 MB",
                    WingetId = "RARLab.WinRAR",
                    RegistryDetectKeyword = "WinRAR"
                },
                new()
                {
                    Id = "seven_zip",
                    Name = "7-Zip",
                    Description = "Yüksek sıkıştırma oranına (7z formatı) sahip, son derece hızlı ve tamamen ücretsiz açık kaynak arşivleyici.",
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
                    IconSymbol = "FolderZip24",
                    Publisher = "Igor Pavlov",
                    SizeText = "1.5 MB",
                    WingetId = "7zip.7zip",
                    RegistryDetectKeyword = "7-Zip"
                },
                new()
                {
                    Id = "anydesk",
                    Name = "AnyDesk",
                    Description = "Düşük gecikmeli ve yüksek kare hızlı uzaktan masaüstü bağlantısı ve teknik destek istemcisi.",
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
                    IconSymbol = "Desktop24",
                    Publisher = "AnyDesk Software GmbH",
                    SizeText = "5 MB",
                    WingetId = "AnyDeskSoftwareGmbH.AnyDesk",
                    RegistryDetectKeyword = "AnyDesk"
                },
                new()
                {
                    Id = "voidtools_everything",
                    Name = "Voidtools Everything",
                    Description = "Tüm sabit disklerinizdeki milyonlarca dosyayı yazdığınız milisaniye içinde anında bulan mucize arama motoru.",
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
                    IconSymbol = "Search24",
                    Publisher = "voidtools",
                    SizeText = "2 MB",
                    WingetId = "voidtools.Everything",
                    RegistryDetectKeyword = "Everything"
                },
                new()
                {
                    Id = "powertoys",
                    Name = "Microsoft PowerToys",
                    Description = "Pencere bölücü (FancyZones), hızlı renk seçici, klavye yöneticisi ve dosya önizleyicileri içeren sistem güç paketi.",
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
                    IconSymbol = "Wrench24",
                    Publisher = "Microsoft Corporation",
                    SizeText = "190 MB",
                    WingetId = "Microsoft.PowerToys",
                    RegistryDetectKeyword = "PowerToys"
                },
                new()
                {
                    Id = "notepad_plus_plus",
                    Name = "Notepad++",
                    Description = "Söz dizimi vurgulama, eklenti desteği ve sekme yönetimi ile donatılmış hafif metin ve kod editörü.",
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
                    IconSymbol = "DocumentEdit24",
                    Publisher = "Don Ho",
                    SizeText = "5 MB",
                    WingetId = "Notepad++.Notepad++",
                    RegistryDetectKeyword = "Notepad++"
                },
                new()
                {
                    Id = "rufus",
                    Name = "Rufus",
                    Description = "Windows ve Linux kurulum USB'leri hazırlamak için güvenli, hızlı ve tek parça önyükleme aracı.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "UsbStick24",
                    Publisher = "Pete Batard",
                    SizeText = "1.7 MB",
                    WingetId = "Rufus.Rufus",
                    RegistryDetectKeyword = "Rufus"
                },
                new()
                {
                    Id = "cpuz",
                    Name = "CPU-Z",
                    Description = "İşlemci mimarisi, çekirdek saat hızları, anakart ve bellek zamanlamalarını gösteren donanım kimlik aracı.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "DeveloperBoard24",
                    Publisher = "CPUID",
                    SizeText = "3 MB",
                    WingetId = "CPUID.CPU-Z",
                    RegistryDetectKeyword = "CPU-Z"
                },
                new()
                {
                    Id = "gpuz",
                    Name = "GPU-Z",
                    Description = "Ekran kartı çipi, VRAM tipi, saat frekansları ve sıcaklık sensörlerini canlı raporlayan grafik aracı.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "Desktop24",
                    Publisher = "TechPowerUp",
                    SizeText = "9 MB",
                    WingetId = "TechPowerUp.GPU-Z",
                    RegistryDetectKeyword = "GPU-Z"
                },
                new()
                {
                    Id = "hwmonitor",
                    Name = "HWMonitor",
                    Description = "İşlemci, ekran kartı ve anakart sıcaklıklarını, fan hızlarını ve güç tüketimini canlı izleme programı.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "HeartPulse24",
                    Publisher = "CPUID",
                    SizeText = "2 MB",
                    WingetId = "CPUID.HWMonitor",
                    RegistryDetectKeyword = "HWMonitor"
                },
                new()
                {
                    Id = "qbittorrent",
                    Name = "qBittorrent",
                    Description = "Reklamsız, açık kaynaklı, temiz ve yüksek hızlı torrent dosya indirme istemcisi.",
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
                    IconSymbol = "ArrowDownload24",
                    Publisher = "The qBittorrent Project",
                    SizeText = "32 MB",
                    WingetId = "qBittorrent.qBittorrent",
                    RegistryDetectKeyword = "qBittorrent"
                },
                new()
                {
                    Id = "hwinfo64",
                    Name = "HWiNFO64",
                    Description = "Dünyanın 1 numaralı donanım teşhis, ayrıntılı bileşen sensörü, voltaj ve sıcaklık izleme yazılımı.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "DeveloperBoard24",
                    Publisher = "REALiX",
                    SizeText = "12 MB",
                    WingetId = "REALiX.HWiNFO",
                    RegistryDetectKeyword = "HWiNFO"
                },
                new()
                {
                    Id = "crystaldiskinfo",
                    Name = "CrystalDiskInfo",
                    Description = "SSD ve sabit disklerin S.M.A.R.T. sağlık durumunu, ömür yüzdesini ve sıcaklığını takip eden teşhis aracı.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "HardDrive20",
                    Publisher = "CrystalDewWorld",
                    SizeText = "6 MB",
                    WingetId = "CrystalDewWorld.CrystalDiskInfo",
                    RegistryDetectKeyword = "CrystalDiskInfo"
                },
                new()
                {
                    Id = "crystaldiskmark",
                    Name = "CrystalDiskMark",
                    Description = "NVMe, SSD ve HDD sürücülerinin sıralı ve rastgele gerçek okuma/yazma hızlarını ölçen benchmark aracı.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "TopSpeed24",
                    Publisher = "CrystalDewWorld",
                    SizeText = "4 MB",
                    WingetId = "CrystalDewWorld.CrystalDiskMark",
                    RegistryDetectKeyword = "CrystalDiskMark"
                },
                new()
                {
                    Id = "furmark2",
                    Name = "Geeks3D FurMark 2",
                    Description = "Ekran kartının sınırlarını zorlayan, aşırı yük altında GPU sıcaklık ve güç kararlılığını test eden benchmark.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "Fire24",
                    Publisher = "Geeks3D",
                    SizeText = "15 MB",
                    WingetId = "Geeks3D.FurMark.2",
                    RegistryDetectKeyword = "FurMark"
                },
                new()
                {
                    Id = "ventoy",
                    Name = "Ventoy",
                    Description = "USB belleği bir kez formatlayıp içine birden fazla Windows/Linux ISO dosyasını kopyalayarak önyükleme yapan dahi araç.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "UsbStick24",
                    Publisher = "Ventoy Team",
                    SizeText = "16 MB",
                    WingetId = "Ventoy.Ventoy",
                    RegistryDetectKeyword = "Ventoy"
                },
                new()
                {
                    Id = "balena_etcher",
                    Name = "balenaEtcher",
                    Description = "SD kart ve USB disklere güvenli, doğrulamalı ve tek tıkla işletim sistemi imajı (ISO/IMG) yazma yazılımı.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "HardDrive20",
                    Publisher = "Balena",
                    SizeText = "140 MB",
                    WingetId = "Balena.Etcher",
                    RegistryDetectKeyword = "balenaEtcher"
                },
                new()
                {
                    Id = "ddu",
                    Name = "Display Driver Uninstaller (DDU)",
                    Description = "Ekran kartı sürücüsü değişimi ve çökme sorunlarında eski sürücüleri güvenli modda kalıntısız ve tertemiz kaldıran araç.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "Desktop24",
                    Publisher = "Wagnardsoft",
                    SizeText = "2 MB",
                    WingetId = "Wagnardsoft.DisplayDriverUninstaller",
                    RegistryDetectKeyword = "Display Driver Uninstaller"
                },
                new()
                {
                    Id = "quicklook",
                    Name = "QuickLook",
                    Description = "Dosya Gezgininde herhangi bir dosyanın üzerine gelip Boşluk (Space) tuşuna basıldığında anında önizleme açan verimlilik aracı.",
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
                    IconSymbol = "Eye24",
                    Publisher = "QL-Win",
                    SizeText = "50 MB",
                    WingetId = "QL-Win.QuickLook",
                    RegistryDetectKeyword = "QuickLook"
                },
                new()
                {
                    Id = "autohotkey",
                    Name = "AutoHotkey",
                    Description = "Klavye ve fare kısayolları atama, hızlı metin genişletme ve Windows masaüstü görevlerini otomatikleştirme motoru.",
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
                    IconSymbol = "Keyboard24",
                    Publisher = "AutoHotkey Foundation",
                    SizeText = "3 MB",
                    WingetId = "AutoHotkey.AutoHotkey",
                    RegistryDetectKeyword = "AutoHotkey"
                },
                new()
                {
                    Id = "eartrumpet",
                    Name = "EarTrumpet",
                    Description = "Windows sistem tepsisinde her açık uygulamanın ses düzeyini ayrı ayrı kontrol etmeyi sağlayan modern ses mikseri.",
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
                    IconSymbol = "Speaker224",
                    Publisher = "File-New-Project",
                    SizeText = "15 MB",
                    WingetId = "File-New-Project.EarTrumpet",
                    RegistryDetectKeyword = "EarTrumpet"
                },

                // ==========================================
                // 5. İNTERNET & WEB TARAYICILARI
                // ==========================================
                new()
                {
                    Id = "chrome",
                    Name = "Google Chrome",
                    Description = "Hızlı, güvenli, Google hesap ve eklenti senkronizasyonuna sahip en popüler web tarayıcısı.",
                    Category = StoreCategory.Browsers,
                    CategoryDisplayName = "Tarayıcılar",
                    IconSymbol = "Globe24",
                    Publisher = "Google LLC",
                    SizeText = "1.5 MB (Setup)",
                    WingetId = "Google.Chrome",
                    RegistryDetectKeyword = "Google Chrome"
                },
                new()
                {
                    Id = "brave",
                    Name = "Brave Browser",
                    Description = "Sitelerdeki izleyicileri ve can sıkıcı reklamları otomatik engelleyen ultra hızlı gizlilik tarayıcısı.",
                    Category = StoreCategory.Browsers,
                    CategoryDisplayName = "Tarayıcılar",
                    IconSymbol = "ShieldCheckmark24",
                    Publisher = "Brave Software Inc.",
                    SizeText = "1.2 MB (Setup)",
                    WingetId = "Brave.Brave",
                    RegistryDetectKeyword = "Brave"
                },
                new()
                {
                    Id = "firefox",
                    Name = "Mozilla Firefox",
                    Description = "Açık web standartlarına bağlı, bağımsız Gecko motorlu güvenilir ve özelleştirilebilir tarayıcı.",
                    Category = StoreCategory.Browsers,
                    CategoryDisplayName = "Tarayıcılar",
                    IconSymbol = "Globe24",
                    Publisher = "Mozilla",
                    SizeText = "60 MB",
                    WingetId = "Mozilla.Firefox",
                    RegistryDetectKeyword = "Mozilla Firefox"
                },
                new()
                {
                    Id = "opera_gx",
                    Name = "Opera GX",
                    Description = "Oyun oynarken tarayıcının sisteminizi yormaması için CPU, RAM ve Ağ limitörlerine sahip oyuncu tarayıcısı.",
                    Category = StoreCategory.Browsers,
                    CategoryDisplayName = "Tarayıcılar",
                    IconSymbol = "TopSpeed24",
                    Publisher = "Opera Norway",
                    SizeText = "3 MB (Setup)",
                    WingetId = "Opera.OperaGX",
                    RegistryDetectKeyword = "Opera GX"
                },
                new()
                {
                    Id = "vivaldi",
                    Name = "Vivaldi",
                    Description = "Çoklu sekme gruplama, dahili notlar ve gelişmiş klavye kısayolları sunan ileri düzey tarayıcı.",
                    Category = StoreCategory.Browsers,
                    CategoryDisplayName = "Tarayıcılar",
                    IconSymbol = "Globe24",
                    Publisher = "Vivaldi Technologies",
                    SizeText = "90 MB",
                    WingetId = "Vivaldi.Vivaldi",
                    RegistryDetectKeyword = "Vivaldi"
                },

                // ==========================================
                // 6. SOSYAL & İLETİŞİM
                // ==========================================
                new()
                {
                    Id = "whatsapp",
                    Name = "WhatsApp Desktop",
                    Description = "Bilgisayarınızdan kesintisiz uçtan uca şifreli mesajlaşma, sesli ve görüntülü arama.",
                    Category = StoreCategory.Social,
                    CategoryDisplayName = "İletişim",
                    IconSymbol = "Chat24",
                    Publisher = "Meta Platforms",
                    SizeText = "140 MB",
                    WingetId = "9NKSQGP7F2NH", // Windows Store / Winget ID
                    RegistryDetectKeyword = "WhatsApp"
                },
                new()
                {
                    Id = "telegram",
                    Name = "Telegram Desktop",
                    Description = "Büyük dosya paylaşımı, kanallar ve bulut tabanlı senkronizasyon sunan ultra hızlı mesajlaşma.",
                    Category = StoreCategory.Social,
                    CategoryDisplayName = "İletişim",
                    IconSymbol = "Send24",
                    Publisher = "Telegram FZ-LLC",
                    SizeText = "45 MB",
                    WingetId = "Telegram.TelegramDesktop",
                    RegistryDetectKeyword = "Telegram Desktop"
                },
                new()
                {
                    Id = "zoom",
                    Name = "Zoom Workplace",
                    Description = "Çevrim içi toplantılar, ekran paylaşımı ve ekip çalışması için kurumsal video konferans istemcisi.",
                    Category = StoreCategory.Social,
                    CategoryDisplayName = "İletişim",
                    IconSymbol = "VideoPerson24",
                    Publisher = "Zoom Video Communications",
                    SizeText = "60 MB",
                    WingetId = "Zoom.Zoom",
                    RegistryDetectKeyword = "Zoom"
                },

                // ==========================================
                // 7. GELİŞTİRİCİ & KODLAMA
                // ==========================================
                new()
                {
                    Id = "vscode",
                    Name = "Visual Studio Code",
                    Description = "Zengin eklenti ekosistemi, yerleşik Git desteği ve hata ayıklama ile modern kod geliştirme ortamı.",
                    Category = StoreCategory.Developer,
                    CategoryDisplayName = "Geliştirici",
                    IconSymbol = "Code24",
                    Publisher = "Microsoft Corporation",
                    SizeText = "95 MB",
                    WingetId = "Microsoft.VisualStudioCode",
                    RegistryDetectKeyword = "Visual Studio Code"
                },
                new()
                {
                    Id = "git_for_windows",
                    Name = "Git for Windows",
                    Description = "Dağıtık versiyon kontrol sistemi, Git Bash terminali ve kimlik doğrulama yöneticisi.",
                    Category = StoreCategory.Developer,
                    CategoryDisplayName = "Geliştirici",
                    IconSymbol = "BranchFork24",
                    Publisher = "Git Community",
                    SizeText = "65 MB",
                    WingetId = "Git.Git",
                    RegistryDetectKeyword = "=Git"
                },
                new()
                {
                    Id = "python_3",
                    Name = "Python 3.12 (Latest)",
                    Description = "Yapay zeka, veri bilimi ve otomasyon betikleri için resmi Python programlama dili ve PIP yöneticisi.",
                    Category = StoreCategory.Developer,
                    CategoryDisplayName = "Geliştirici",
                    IconSymbol = "Code24",
                    Publisher = "Python Software Foundation",
                    SizeText = "26 MB",
                    WingetId = "Python.Python.3.12",
                    RegistryDetectKeyword = "Python 3."
                },
                new()
                {
                    Id = "nodejs_lts",
                    Name = "Node.js (LTS)",
                    Description = "Chrome V8 motoru üzerine kurulu sunucu ve web geliştirme JavaScript çalıştırma platformu ve NPM.",
                    Category = StoreCategory.Developer,
                    CategoryDisplayName = "Geliştirici",
                    IconSymbol = "Code24",
                    Publisher = "OpenJS Foundation",
                    SizeText = "35 MB",
                    WingetId = "OpenJS.NodeJS.LTS",
                    RegistryDetectKeyword = "Node.js"
                },
                new()
                {
                    Id = "windows_terminal",
                    Name = "Windows Terminal",
                    Description = "PowerShell, CMD ve WSL için çok sekmeli, GPU hızlandırmalı ve modern Windows konsolu.",
                    Category = StoreCategory.Developer,
                    CategoryDisplayName = "Geliştirici",
                    IconSymbol = "WindowConsole20",
                    Publisher = "Microsoft Corporation",
                    SizeText = "40 MB",
                    WingetId = "Microsoft.WindowsTerminal",
                    RegistryDetectKeyword = "Windows Terminal"
                },
                // ==========================================
                // GÜVENLİK & GİZLİLİK
                // ==========================================
                new()
                {
                    Id = "malwarebytes",
                    Name = "Malwarebytes",
                    Description = "Casus, fidye ve reklam yazılımlarını tarayıp temizleyen ikinci görüş güvenlik tarayıcısı.",
                    Category = StoreCategory.Security,
                    CategoryDisplayName = "Güvenlik",
                    IconSymbol = "ShieldCheckmark24",
                    Publisher = "Malwarebytes",
                    SizeText = "≈ 250 MB",
                    WingetId = "Malwarebytes.Malwarebytes",
                    RegistryDetectKeyword = "Malwarebytes"
                },
                new()
                {
                    Id = "bitwarden",
                    Name = "Bitwarden",
                    Description = "Açık kaynaklı, uçtan uca şifreli ve cihazlar arasında eşitlenen ücretsiz parola yöneticisi.",
                    Category = StoreCategory.Security,
                    CategoryDisplayName = "Güvenlik",
                    IconSymbol = "Password24",
                    Publisher = "Bitwarden Inc.",
                    SizeText = "1 MB",
                    WingetId = "Bitwarden.Bitwarden",
                    RegistryDetectKeyword = "Bitwarden"
                },
                new()
                {
                    Id = "keepassxc",
                    Name = "KeePassXC",
                    Description = "Parolaları buluta göndermeden yerel ve şifreli bir kasada saklayan açık kaynaklı parola yöneticisi.",
                    Category = StoreCategory.Security,
                    CategoryDisplayName = "Güvenlik",
                    IconSymbol = "LockClosed24",
                    Publisher = "KeePassXC Team",
                    SizeText = "34 MB",
                    WingetId = "KeePassXCTeam.KeePassXC",
                    RegistryDetectKeyword = "KeePassXC"
                },
                new()
                {
                    Id = "protonvpn",
                    Name = "Proton VPN",
                    Description = "İsviçre merkezli, kayıt tutmayan ve ücretsiz planında veri sınırı olmayan VPN istemcisi.",
                    Category = StoreCategory.Security,
                    CategoryDisplayName = "Güvenlik",
                    IconSymbol = "ShieldGlobe24",
                    Publisher = "Proton AG",
                    SizeText = "124 MB",
                    WingetId = "Proton.ProtonVPN",
                    RegistryDetectKeyword = "Proton VPN|ProtonVPN"
                },
                new()
                {
                    Id = "cloudflare_warp",
                    Name = "Cloudflare WARP (1.1.1.1)",
                    Description = "DNS sorgularını ve trafiği şifreleyen, bağlantıyı hızlandıran ücretsiz 1.1.1.1 istemcisi.",
                    Category = StoreCategory.Security,
                    CategoryDisplayName = "Güvenlik",
                    IconSymbol = "GlobeShield24",
                    Publisher = "Cloudflare, Inc.",
                    SizeText = "57 MB",
                    WingetId = "Cloudflare.Warp",
                    RegistryDetectKeyword = "Cloudflare WARP|Cloudflare One"
                },
                new()
                {
                    Id = "wireguard",
                    Name = "WireGuard",
                    Description = "Hızlı ve modern VPN protokolünün resmi istemcisi; kendi VPN sunucunuza bağlanmak için.",
                    Category = StoreCategory.Security,
                    CategoryDisplayName = "Güvenlik",
                    IconSymbol = "Key24",
                    Publisher = "WireGuard LLC",
                    SizeText = "3 MB",
                    WingetId = "WireGuard.WireGuard",
                    RegistryDetectKeyword = "WireGuard"
                },
                new()
                {
                    Id = "tailscale",
                    Name = "Tailscale",
                    Description = "Bilgisayarınızı ve telefonunuzu ayar gerektirmeyen, şifreli özel bir ağda birleştirir.",
                    Category = StoreCategory.Security,
                    CategoryDisplayName = "Güvenlik",
                    IconSymbol = "PlugConnected24",
                    Publisher = "Tailscale Inc.",
                    SizeText = "37 MB",
                    WingetId = "Tailscale.Tailscale",
                    RegistryDetectKeyword = "Tailscale"
                },
                new()
                {
                    Id = "veracrypt",
                    Name = "VeraCrypt",
                    Description = "Diskleri, USB bellekleri ve şifreli kapsayıcıları güçlü şifrelemeyle koruyan açık kaynaklı araç.",
                    Category = StoreCategory.Security,
                    CategoryDisplayName = "Güvenlik",
                    IconSymbol = "LockShield24",
                    Publisher = "IDRIX",
                    SizeText = "50 MB",
                    WingetId = "IDRIX.VeraCrypt",
                    RegistryDetectKeyword = "VeraCrypt"
                },
                new()
                {
                    Id = "portmaster",
                    Name = "Portmaster",
                    Description = "Hangi uygulamanın nereye bağlandığını gösteren ve izleyicileri engelleyen ücretsiz uygulama güvenlik duvarı.",
                    Category = StoreCategory.Security,
                    CategoryDisplayName = "Güvenlik",
                    IconSymbol = "ShieldTask24",
                    Publisher = "Safing",
                    SizeText = "89 MB",
                    WingetId = "Safing.Portmaster",
                    RegistryDetectKeyword = "Portmaster"
                },
                new()
                {
                    Id = "simplewall",
                    Name = "simplewall",
                    Description = "Windows Güvenlik Duvarı ile hangi programın internete çıkabileceğini seçmenizi sağlayan hafif araç.",
                    Category = StoreCategory.Security,
                    CategoryDisplayName = "Güvenlik",
                    IconSymbol = "ShieldProhibited24",
                    Publisher = "Henry++",
                    SizeText = "1 MB",
                    WingetId = "Henry++.simplewall",
                    RegistryDetectKeyword = "simplewall"
                },
                new()
                {
                    Id = "sandboxie",
                    Name = "Sandboxie Plus",
                    Description = "Şüpheli programları sisteme dokunmadan yalıtılmış bir kum havuzunda çalıştırır.",
                    Category = StoreCategory.Security,
                    CategoryDisplayName = "Güvenlik",
                    IconSymbol = "Box24",
                    Publisher = "Xanasoft",
                    SizeText = "24 MB",
                    WingetId = "Sandboxie.Plus",
                    RegistryDetectKeyword = "Sandboxie"
                },

                // ==========================================
                // OFİS & VERİMLİLİK
                // ==========================================
                new()
                {
                    Id = "libreoffice",
                    Name = "LibreOffice",
                    Description = "Word, Excel ve PowerPoint dosyalarını açıp düzenleyen ücretsiz ve açık kaynaklı ofis paketi.",
                    Category = StoreCategory.Productivity,
                    CategoryDisplayName = "Ofis & Verimlilik",
                    IconSymbol = "DocumentText24",
                    Publisher = "The Document Foundation",
                    SizeText = "358 MB",
                    WingetId = "TheDocumentFoundation.LibreOffice",
                    RegistryDetectKeyword = "LibreOffice"
                },
                new()
                {
                    Id = "onlyoffice",
                    Name = "ONLYOFFICE Desktop Editors",
                    Description = "Microsoft Office biçimleriyle yüksek uyumlu, modern arayüzlü ücretsiz ofis paketi.",
                    Category = StoreCategory.Productivity,
                    CategoryDisplayName = "Ofis & Verimlilik",
                    IconSymbol = "DocumentText24",
                    Publisher = "Ascensio System SIA",
                    SizeText = "356 MB",
                    WingetId = "ONLYOFFICE.DesktopEditors",
                    RegistryDetectKeyword = "ONLYOFFICE"
                },
                new()
                {
                    Id = "sumatrapdf",
                    Name = "SumatraPDF",
                    Description = "PDF, EPUB ve CBZ dosyalarını anında açan çok hafif ve reklamsız okuyucu.",
                    Category = StoreCategory.Productivity,
                    CategoryDisplayName = "Ofis & Verimlilik",
                    IconSymbol = "DocumentPdf24",
                    Publisher = "Krzysztof Kowalczyk",
                    SizeText = "11 MB",
                    WingetId = "SumatraPDF.SumatraPDF",
                    RegistryDetectKeyword = "SumatraPDF"
                },
                new()
                {
                    Id = "acrobat_reader",
                    Name = "Adobe Acrobat Reader",
                    Description = "Form doldurma, imzalama ve yorum ekleme destekli standart PDF okuyucu.",
                    Category = StoreCategory.Productivity,
                    CategoryDisplayName = "Ofis & Verimlilik",
                    IconSymbol = "DocumentPdf24",
                    Publisher = "Adobe",
                    SizeText = "822 MB",
                    WingetId = "Adobe.Acrobat.Reader.64-bit",
                    RegistryDetectKeyword = "Adobe Acrobat"
                },
                new()
                {
                    Id = "pdfsam",
                    Name = "PDFsam Basic",
                    Description = "PDF dosyalarını birleştiren, bölen, döndüren ve sayfa ayıklayan ücretsiz araç.",
                    Category = StoreCategory.Productivity,
                    CategoryDisplayName = "Ofis & Verimlilik",
                    IconSymbol = "DocumentSplitHint24",
                    Publisher = "Sober Lemur",
                    SizeText = "94 MB",
                    WingetId = "PDFsam.PDFsam",
                    RegistryDetectKeyword = "PDFsam"
                },
                new()
                {
                    Id = "obsidian",
                    Name = "Obsidian",
                    Description = "Notları kendi bilgisayarınızda Markdown dosyası olarak tutan, bağlantılı not uygulaması.",
                    Category = StoreCategory.Productivity,
                    CategoryDisplayName = "Ofis & Verimlilik",
                    IconSymbol = "Notebook24",
                    Publisher = "Obsidian",
                    SizeText = "316 MB",
                    WingetId = "Obsidian.Obsidian",
                    RegistryDetectKeyword = "Obsidian"
                },
                new()
                {
                    Id = "notion",
                    Name = "Notion",
                    Description = "Not, görev, veritabanı ve wiki’yi tek çalışma alanında birleştiren üretkenlik aracı.",
                    Category = StoreCategory.Productivity,
                    CategoryDisplayName = "Ofis & Verimlilik",
                    IconSymbol = "Notebook24",
                    Publisher = "Notion Labs",
                    SizeText = "104 MB",
                    WingetId = "Notion.Notion",
                    RegistryDetectKeyword = "Notion"
                },
                new()
                {
                    Id = "joplin",
                    Name = "Joplin",
                    Description = "Uçtan uca şifreli eşitleme destekli, açık kaynaklı not ve yapılacaklar uygulaması.",
                    Category = StoreCategory.Productivity,
                    CategoryDisplayName = "Ofis & Verimlilik",
                    IconSymbol = "NoteEdit24",
                    Publisher = "Laurent Cozic",
                    SizeText = "344 MB",
                    WingetId = "Joplin.Joplin",
                    RegistryDetectKeyword = "Joplin"
                },
                new()
                {
                    Id = "google_drive",
                    Name = "Google Drive",
                    Description = "Drive dosyalarınızı bilgisayarda bir sürücü gibi gösterir ve klasörleri otomatik yedekler.",
                    Category = StoreCategory.Productivity,
                    CategoryDisplayName = "Ofis & Verimlilik",
                    IconSymbol = "Cloud24",
                    Publisher = "Google LLC",
                    SizeText = "278 MB",
                    WingetId = "Google.GoogleDrive",
                    RegistryDetectKeyword = "Google Drive"
                },
                new()
                {
                    Id = "dropbox",
                    Name = "Dropbox",
                    Description = "Dosyaları bulutla eşitleyen ve bağlantıyla paylaşmayı kolaylaştıran depolama istemcisi.",
                    Category = StoreCategory.Productivity,
                    CategoryDisplayName = "Ofis & Verimlilik",
                    IconSymbol = "CloudArrowUp24",
                    Publisher = "Dropbox, Inc.",
                    SizeText = "290 MB",
                    WingetId = "Dropbox.Dropbox",
                    RegistryDetectKeyword = "Dropbox"
                },
                new()
                {
                    Id = "localsend",
                    Name = "LocalSend",
                    Description = "Aynı ağdaki telefon ve bilgisayarlar arasında internetsiz dosya aktarımı (AirDrop alternatifi).",
                    Category = StoreCategory.Productivity,
                    CategoryDisplayName = "Ofis & Verimlilik",
                    IconSymbol = "ArrowSwap24",
                    Publisher = "Tien Do Nam",
                    SizeText = "18 MB",
                    WingetId = "LocalSend.LocalSend",
                    RegistryDetectKeyword = "LocalSend"
                },
                new()
                {
                    Id = "deepl",
                    Name = "DeepL",
                    Description = "Seçili metni kısayolla çeviren, doğal sonuçlarıyla bilinen yapay zekâ çevirmeni.",
                    Category = StoreCategory.Productivity,
                    CategoryDisplayName = "Ofis & Verimlilik",
                    IconSymbol = "Translate24",
                    Publisher = "DeepL SE",
                    SizeText = "5 MB",
                    WingetId = "DeepL.DeepL",
                    RegistryDetectKeyword = "DeepL"
                },
                new()
                {
                    Id = "calibre",
                    Name = "calibre",
                    Description = "E-kitap kütüphanesi yöneticisi; EPUB, MOBI ve PDF dönüştürür, e-okuyucuya aktarır.",
                    Category = StoreCategory.Productivity,
                    CategoryDisplayName = "Ofis & Verimlilik",
                    IconSymbol = "Book24",
                    Publisher = "Kovid Goyal",
                    SizeText = "216 MB",
                    WingetId = "calibre.calibre",
                    RegistryDetectKeyword = "calibre"
                },
                new()
                {
                    Id = "flow_launcher",
                    Name = "Flow Launcher",
                    Description = "Alt+Boşluk ile uygulama, dosya ve web aramasını anında yapan hızlı başlatıcı.",
                    Category = StoreCategory.Productivity,
                    CategoryDisplayName = "Ofis & Verimlilik",
                    IconSymbol = "Search24",
                    Publisher = "Flow-Launcher Team",
                    SizeText = "97 MB",
                    WingetId = "Flow-Launcher.Flow-Launcher",
                    RegistryDetectKeyword = "Flow Launcher|Flow-Launcher"
                },
                new()
                {
                    Id = "ditto",
                    Name = "Ditto",
                    Description = "Kopyaladığınız her şeyi saklayan, aranabilir pano geçmişi yöneticisi.",
                    Category = StoreCategory.Productivity,
                    CategoryDisplayName = "Ofis & Verimlilik",
                    IconSymbol = "ClipboardPaste24",
                    Publisher = "Scott Brogden",
                    SizeText = "6 MB",
                    WingetId = "Ditto.Ditto",
                    RegistryDetectKeyword = "Ditto"
                },

                // ==========================================
                // GRAFİK & TASARIM
                // ==========================================
                new()
                {
                    Id = "gimp",
                    Name = "GIMP",
                    Description = "Katman, maske ve eklenti desteğiyle Photoshop’a ücretsiz ve açık kaynaklı alternatif.",
                    Category = StoreCategory.Graphics,
                    CategoryDisplayName = "Grafik & Tasarım",
                    IconSymbol = "PaintBrush24",
                    Publisher = "The GIMP Team",
                    SizeText = "182 MB",
                    WingetId = "GIMP.GIMP.3",
                    RegistryDetectKeyword = "GIMP"
                },
                new()
                {
                    Id = "paintdotnet",
                    Name = "Paint.NET",
                    Description = "Katman ve efekt destekli; hızlı, sade ve hafif fotoğraf düzenleyici.",
                    Category = StoreCategory.Graphics,
                    CategoryDisplayName = "Grafik & Tasarım",
                    IconSymbol = "PaintBrush24",
                    Publisher = "dotPDN LLC",
                    SizeText = "96 MB",
                    WingetId = "dotPDN.PaintDotNet",
                    RegistryDetectKeyword = "paint.net"
                },
                new()
                {
                    Id = "krita",
                    Name = "Krita",
                    Description = "Dijital çizim ve illüstrasyon için profesyonel, ücretsiz boyama programı.",
                    Category = StoreCategory.Graphics,
                    CategoryDisplayName = "Grafik & Tasarım",
                    IconSymbol = "Pen24",
                    Publisher = "Krita Foundation",
                    SizeText = "158 MB",
                    WingetId = "KDE.Krita",
                    RegistryDetectKeyword = "Krita"
                },
                new()
                {
                    Id = "inkscape",
                    Name = "Inkscape",
                    Description = "Logo, ikon ve illüstrasyon için vektör çizim programı (Illustrator alternatifi).",
                    Category = StoreCategory.Graphics,
                    CategoryDisplayName = "Grafik & Tasarım",
                    IconSymbol = "Shapes24",
                    Publisher = "Inkscape",
                    SizeText = "162 MB",
                    WingetId = "Inkscape.Inkscape",
                    RegistryDetectKeyword = "Inkscape"
                },
                new()
                {
                    Id = "blender",
                    Name = "Blender",
                    Description = "3B modelleme, animasyon, render ve video kurgu için ücretsiz profesyonel paket.",
                    Category = StoreCategory.Graphics,
                    CategoryDisplayName = "Grafik & Tasarım",
                    IconSymbol = "Cube24",
                    Publisher = "Blender Foundation",
                    SizeText = "348 MB",
                    WingetId = "BlenderFoundation.Blender",
                    RegistryDetectKeyword = "Blender"
                },
                new()
                {
                    Id = "figma",
                    Name = "Figma",
                    Description = "Arayüz tasarımı ve prototipleme için ekip odaklı tasarım aracının masaüstü uygulaması.",
                    Category = StoreCategory.Graphics,
                    CategoryDisplayName = "Grafik & Tasarım",
                    IconSymbol = "DesignIdeas24",
                    Publisher = "Figma, Inc.",
                    SizeText = "148 MB",
                    WingetId = "Figma.Figma",
                    RegistryDetectKeyword = "Figma"
                },
                new()
                {
                    Id = "irfanview",
                    Name = "IrfanView",
                    Description = "Neredeyse her görüntü biçimini anında açan, toplu dönüştürme destekli hızlı görüntüleyici.",
                    Category = StoreCategory.Graphics,
                    CategoryDisplayName = "Grafik & Tasarım",
                    IconSymbol = "Image24",
                    Publisher = "Irfan Skiljan",
                    SizeText = "4 MB",
                    WingetId = "IrfanSkiljan.IrfanView",
                    RegistryDetectKeyword = "IrfanView"
                },
                new()
                {
                    Id = "xnviewmp",
                    Name = "XnView MP",
                    Description = "500’den fazla biçimi açan fotoğraf tarayıcısı; toplu dönüştürme ve yeniden adlandırma.",
                    Category = StoreCategory.Graphics,
                    CategoryDisplayName = "Grafik & Tasarım",
                    IconSymbol = "ImageMultiple24",
                    Publisher = "Pierre-e Gougelet",
                    SizeText = "58 MB",
                    WingetId = "XnSoft.XnViewMP",
                    RegistryDetectKeyword = "XnView"
                },
                new()
                {
                    Id = "darktable",
                    Name = "darktable",
                    Description = "RAW fotoğraf geliştirme ve düzenleme için açık kaynaklı Lightroom alternatifi.",
                    Category = StoreCategory.Graphics,
                    CategoryDisplayName = "Grafik & Tasarım",
                    IconSymbol = "Camera24",
                    Publisher = "darktable team",
                    SizeText = "133 MB",
                    WingetId = "darktable.darktable",
                    RegistryDetectKeyword = "darktable"
                },
                new()
                {
                    Id = "greenshot",
                    Name = "Greenshot",
                    Description = "Ekran görüntüsü alıp üzerine ok, metin ve bulanıklaştırma ekleyen hafif araç.",
                    Category = StoreCategory.Graphics,
                    CategoryDisplayName = "Grafik & Tasarım",
                    IconSymbol = "Screenshot24",
                    Publisher = "Greenshot",
                    SizeText = "4 MB",
                    WingetId = "Greenshot.Greenshot",
                    RegistryDetectKeyword = "Greenshot"
                },
                new()
                {
                    Id = "flameshot",
                    Name = "Flameshot",
                    Description = "Anında işaretleme araçlarıyla güçlü ve açık kaynaklı ekran görüntüsü aracı.",
                    Category = StoreCategory.Graphics,
                    CategoryDisplayName = "Grafik & Tasarım",
                    IconSymbol = "Screenshot24",
                    Publisher = "flameshot-org",
                    SizeText = "18 MB",
                    WingetId = "Flameshot.Flameshot",
                    RegistryDetectKeyword = "Flameshot"
                },
                new()
                {
                    Id = "upscayl",
                    Name = "Upscayl",
                    Description = "Düşük çözünürlüklü fotoğrafları yapay zekâ ile bilgisayarınızda büyütüp netleştirir.",
                    Category = StoreCategory.Graphics,
                    CategoryDisplayName = "Grafik & Tasarım",
                    IconSymbol = "Sparkle24",
                    Publisher = "Nayam Amarshe",
                    SizeText = "249 MB",
                    WingetId = "Upscayl.Upscayl",
                    RegistryDetectKeyword = "Upscayl"
                },

                // ==========================================
                // SİSTEM & DONANIM
                // ==========================================
                new()
                {
                    Id = "treesize",
                    Name = "TreeSize Free",
                    Description = "Diskte en çok yer kaplayan klasörleri hızla bulan disk alanı çözümleyici.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "DataTreemap24",
                    Publisher = "JAM Software",
                    SizeText = "16 MB",
                    WingetId = "JAMSoftware.TreeSize.Free",
                    RegistryDetectKeyword = "TreeSize"
                },
                new()
                {
                    Id = "wiztree",
                    Name = "WizTree",
                    Description = "Tüm diski saniyeler içinde tarayan, en hızlı disk alanı çözümleyicilerinden biri.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "DataTreemap24",
                    Publisher = "Antibody Software",
                    SizeText = "5 MB",
                    WingetId = "AntibodySoftware.WizTree",
                    RegistryDetectKeyword = "WizTree"
                },
                new()
                {
                    Id = "windirstat",
                    Name = "WinDirStat",
                    Description = "Disk kullanımını renkli ağaç haritasıyla gösteren klasik açık kaynaklı araç.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "DataTreemap24",
                    Publisher = "The WinDirStat Team",
                    SizeText = "3 MB",
                    WingetId = "WinDirStat.WinDirStat",
                    RegistryDetectKeyword = "WinDirStat"
                },
                new()
                {
                    Id = "coretemp",
                    Name = "Core Temp",
                    Description = "İşlemcinin her çekirdeğinin sıcaklığını ve yükünü anlık olarak izler.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "Temperature24",
                    Publisher = "ALCPU",
                    SizeText = "2 MB",
                    WingetId = "ALCPU.CoreTemp",
                    RegistryDetectKeyword = "Core Temp"
                },
                new()
                {
                    Id = "fancontrol",
                    Name = "Fan Control",
                    Description = "Fan hızlarını sıcaklığa göre özel eğrilerle yöneten gelişmiş ve ücretsiz araç.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "WeatherHaze24",
                    Publisher = "Rem0o",
                    SizeText = "19 MB",
                    WingetId = "Rem0o.FanControl",
                    RegistryDetectKeyword = "FanControl|Fan Control"
                },
                new()
                {
                    Id = "nvcleanstall",
                    Name = "NVCleanstall",
                    Description = "NVIDIA sürücüsünü telemetri ve gereksiz bileşenler olmadan, özelleştirerek kurar.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "DeveloperBoard24",
                    Publisher = "TechPowerUp",
                    SizeText = "≈ 2 MB",
                    WingetId = "TechPowerUp.NVCleanstall",
                    RegistryDetectKeyword = "NVCleanstall"
                },
                new()
                {
                    Id = "open_shell",
                    Name = "Open-Shell",
                    Description = "Klasik Windows 7 tarzı Başlat menüsünü geri getirir.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "Navigation24",
                    Publisher = "Open-Shell Team",
                    SizeText = "9 MB",
                    WingetId = "Open-Shell.Open-Shell-Menu",
                    RegistryDetectKeyword = "Open-Shell"
                },
                new()
                {
                    Id = "twinkle_tray",
                    Name = "Twinkle Tray",
                    Description = "Harici monitörlerin parlaklığını görev çubuğundan ayarlamanızı sağlar.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "BrightnessHigh24",
                    Publisher = "Xander Frangos",
                    SizeText = "88 MB",
                    WingetId = "xanderfrangos.twinkletray",
                    RegistryDetectKeyword = "Twinkle Tray"
                },
                new()
                {
                    Id = "flux",
                    Name = "f.lux",
                    Description = "Ekran rengini günün saatine göre ısıtarak göz yorgunluğunu azaltır.",
                    Category = StoreCategory.Hardware,
                    CategoryDisplayName = "Sistem & Donanım",
                    IconSymbol = "WeatherMoon24",
                    Publisher = "f.lux Software",
                    SizeText = "1 MB",
                    WingetId = "flux.flux",
                    RegistryDetectKeyword = "f.lux"
                },

                // ==========================================
                // MÜZİK & MEDYA (EK)
                // ==========================================
                new()
                {
                    Id = "kdenlive",
                    Name = "Kdenlive",
                    Description = "Çok izli zaman çizelgesi ve efektlerle ücretsiz, açık kaynaklı video düzenleyici.",
                    Category = StoreCategory.Music,
                    CategoryDisplayName = "Müzik & Medya",
                    IconSymbol = "VideoClip24",
                    Publisher = "KDE e.V.",
                    SizeText = "129 MB",
                    WingetId = "KDE.Kdenlive",
                    RegistryDetectKeyword = "Kdenlive"
                },
                new()
                {
                    Id = "shotcut",
                    Name = "Shotcut",
                    Description = "Geniş biçim desteğine sahip, öğrenmesi kolay ücretsiz video düzenleyici.",
                    Category = StoreCategory.Music,
                    CategoryDisplayName = "Müzik & Medya",
                    IconSymbol = "VideoClip24",
                    Publisher = "Meltytech",
                    SizeText = "155 MB",
                    WingetId = "Meltytech.Shotcut",
                    RegistryDetectKeyword = "Shotcut"
                },
                new()
                {
                    Id = "mpc_hc",
                    Name = "MPC-HC",
                    Description = "Çok hafif, reklamsız ve hızlı klasik video oynatıcı.",
                    Category = StoreCategory.Music,
                    CategoryDisplayName = "Müzik & Medya",
                    IconSymbol = "Video24",
                    Publisher = "MPC-HC Team",
                    SizeText = "22 MB",
                    WingetId = "clsid2.mpc-hc",
                    RegistryDetectKeyword = "MPC-HC"
                },
                new()
                {
                    Id = "potplayer",
                    Name = "PotPlayer",
                    Description = "Donanım hızlandırmalı, gelişmiş altyazı ve ses ayarlarına sahip güçlü video oynatıcı.",
                    Category = StoreCategory.Music,
                    CategoryDisplayName = "Müzik & Medya",
                    IconSymbol = "Video24",
                    Publisher = "Kakao Corp.",
                    SizeText = "47 MB",
                    WingetId = "Daum.PotPlayer",
                    RegistryDetectKeyword = "PotPlayer"
                },
                new()
                {
                    Id = "foobar2000",
                    Name = "foobar2000",
                    Description = "FLAC ve kayıpsız biçimler için özelleştirilebilir, çok hafif müzik çalar.",
                    Category = StoreCategory.Music,
                    CategoryDisplayName = "Müzik & Medya",
                    IconSymbol = "MusicNote224",
                    Publisher = "Peter Pawlowski",
                    SizeText = "7 MB",
                    WingetId = "PeterPawlowski.foobar2000",
                    RegistryDetectKeyword = "foobar2000"
                },
                new()
                {
                    Id = "klite_codec",
                    Name = "K-Lite Codec Pack",
                    Description = "Açılmayan video ve ses biçimleri için gereken kodekleri tek pakette kurar.",
                    Category = StoreCategory.Music,
                    CategoryDisplayName = "Müzik & Medya",
                    IconSymbol = "Filmstrip24",
                    Publisher = "KLCP",
                    SizeText = "39 MB",
                    WingetId = "CodecGuide.K-LiteCodecPack.Standard",
                    RegistryDetectKeyword = "K-Lite"
                },
                new()
                {
                    Id = "stremio",
                    Name = "Stremio",
                    Description = "Film ve dizileri tek yerde toplayan, eklenti tabanlı medya merkezi.",
                    Category = StoreCategory.Music,
                    CategoryDisplayName = "Müzik & Medya",
                    IconSymbol = "Tv24",
                    Publisher = "Smart Code",
                    SizeText = "117 MB",
                    WingetId = "Stremio.Stremio",
                    RegistryDetectKeyword = "Stremio"
                },
                new()
                {
                    Id = "voicemeeter",
                    Name = "Voicemeeter",
                    Description = "Mikrofon, oyun ve müzik seslerini ayrı ayrı yöneten sanal ses mikseri (yayıncılar için).",
                    Category = StoreCategory.Music,
                    CategoryDisplayName = "Müzik & Medya",
                    IconSymbol = "SlideMicrophone24",
                    Publisher = "VB-Audio",
                    SizeText = "21 MB",
                    WingetId = "VB-Audio.Voicemeeter",
                    RegistryDetectKeyword = "Voicemeeter"
                },
                new()
                {
                    Id = "streamlabs",
                    Name = "Streamlabs Desktop",
                    Description = "Hazır sahneler, uyarılar ve bağış araçlarıyla Twitch/YouTube yayın programı.",
                    Category = StoreCategory.Music,
                    CategoryDisplayName = "Müzik & Medya",
                    IconSymbol = "Live24",
                    Publisher = "Streamlabs",
                    SizeText = "273 MB",
                    WingetId = "Streamlabs.Streamlabs",
                    RegistryDetectKeyword = "Streamlabs"
                },

                // ==========================================
                // OYUN (EK)
                // ==========================================
                new()
                {
                    Id = "playnite",
                    Name = "Playnite",
                    Description = "Steam, Epic, GOG, Riot ve diğer tüm oyunlarınızı tek kütüphanede toplayan başlatıcı.",
                    Category = StoreCategory.Gaming,
                    CategoryDisplayName = "Oyun",
                    IconSymbol = "Library24",
                    Publisher = "Josef Nemec",
                    SizeText = "148 MB",
                    WingetId = "Playnite.Playnite",
                    RegistryDetectKeyword = "Playnite"
                },
                new()
                {
                    Id = "heroic",
                    Name = "Heroic Games Launcher",
                    Description = "Epic, GOG ve Amazon Prime Gaming oyunları için hafif ve açık kaynaklı başlatıcı.",
                    Category = StoreCategory.Gaming,
                    CategoryDisplayName = "Oyun",
                    IconSymbol = "Games24",
                    Publisher = "Heroic",
                    SizeText = "148 MB",
                    WingetId = "HeroicGamesLauncher.HeroicGamesLauncher",
                    RegistryDetectKeyword = "Heroic"
                },
                new()
                {
                    Id = "prism_launcher",
                    Name = "Prism Launcher",
                    Description = "Birden çok Minecraft sürümünü ve mod paketini ayrı ayrı yöneten başlatıcı.",
                    Category = StoreCategory.Gaming,
                    CategoryDisplayName = "Oyun",
                    IconSymbol = "Cube24",
                    Publisher = "Prism Launcher",
                    SizeText = "23 MB",
                    WingetId = "PrismLauncher.PrismLauncher",
                    RegistryDetectKeyword = "Prism Launcher|PrismLauncher"
                },
                new()
                {
                    Id = "parsec",
                    Name = "Parsec",
                    Description = "Düşük gecikmeli uzaktan oyun; arkadaşınızla internet üzerinden yerel çok oyunculu oynayın.",
                    Category = StoreCategory.Gaming,
                    CategoryDisplayName = "Oyun",
                    IconSymbol = "Desktop24",
                    Publisher = "Parsec Cloud",
                    SizeText = "4 MB",
                    WingetId = "Parsec.Parsec",
                    RegistryDetectKeyword = "Parsec"
                },
                new()
                {
                    Id = "moonlight",
                    Name = "Moonlight",
                    Description = "Oyun bilgisayarınızdaki oyunları evdeki başka bir cihaza akıtan istemci (Sunshine ile).",
                    Category = StoreCategory.Gaming,
                    CategoryDisplayName = "Oyun",
                    IconSymbol = "WeatherMoon24",
                    Publisher = "Moonlight",
                    SizeText = "41 MB",
                    WingetId = "MoonlightGameStreamingProject.Moonlight",
                    RegistryDetectKeyword = "Moonlight"
                },
                new()
                {
                    Id = "rtss",
                    Name = "RivaTuner Statistics Server",
                    Description = "FPS sınırlayıcı ve oyun içi performans göstergesi (MSI Afterburner ile birlikte çalışır).",
                    Category = StoreCategory.Gaming,
                    CategoryDisplayName = "Oyun",
                    IconSymbol = "TopSpeed24",
                    Publisher = "Unwinder",
                    SizeText = "≈ 20 MB",
                    WingetId = "Guru3D.RTSS",
                    RegistryDetectKeyword = "RivaTuner Statistics"
                },

                // ==========================================
                // YAZILIM & ARAÇLAR (EK)
                // ==========================================
                new()
                {
                    Id = "peazip",
                    Name = "PeaZip",
                    Description = "200’den fazla arşiv biçimini açan, şifreleme destekli ücretsiz arşiv yöneticisi.",
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
                    IconSymbol = "FolderZip24",
                    Publisher = "Giorgio Tani",
                    SizeText = "11 MB",
                    WingetId = "Giorgiotani.Peazip",
                    RegistryDetectKeyword = "PeaZip"
                },
                new()
                {
                    Id = "teracopy",
                    Name = "TeraCopy",
                    Description = "Dosya kopyalamayı hızlandırır, doğrular ve hatalı dosyada takılmadan devam eder.",
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
                    IconSymbol = "Copy24",
                    Publisher = "Code Sector",
                    SizeText = "15 MB",
                    WingetId = "CodeSector.TeraCopy",
                    RegistryDetectKeyword = "TeraCopy"
                },
                new()
                {
                    Id = "fdm",
                    Name = "Free Download Manager",
                    Description = "İndirmeleri hızlandıran, duraklatıp sürdürebilen ve torrent destekli indirme yöneticisi.",
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
                    IconSymbol = "ArrowDownload24",
                    Publisher = "Softdeluxe",
                    SizeText = "49 MB",
                    WingetId = "SoftDeluxe.FreeDownloadManager",
                    RegistryDetectKeyword = "Free Download Manager"
                },
                new()
                {
                    Id = "jdownloader",
                    Name = "JDownloader 2",
                    Description = "Dosya barındırma sitelerinden toplu indirme ve otomatik arşiv açma yapan indirme yöneticisi.",
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
                    IconSymbol = "ArrowDownload24",
                    Publisher = "AppWork",
                    SizeText = "29 MB",
                    WingetId = "AppWork.JDownloader",
                    RegistryDetectKeyword = "JDownloader"
                },
                new()
                {
                    Id = "teamviewer",
                    Name = "TeamViewer",
                    Description = "Uzaktaki bir bilgisayara bağlanıp destek vermek için yaygın uzaktan erişim aracı.",
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
                    IconSymbol = "Desktop24",
                    Publisher = "TeamViewer",
                    SizeText = "90 MB",
                    WingetId = "TeamViewer.TeamViewer",
                    RegistryDetectKeyword = "TeamViewer"
                },

                // ==========================================
                // İLETİŞİM (EK)
                // ==========================================
                new()
                {
                    Id = "slack",
                    Name = "Slack",
                    Description = "Kanallar, görüntülü toplantı ve entegrasyonlarla ekip iletişim platformu.",
                    Category = StoreCategory.Social,
                    CategoryDisplayName = "İletişim",
                    IconSymbol = "Chat24",
                    Publisher = "Slack Technologies",
                    SizeText = "159 MB",
                    WingetId = "SlackTechnologies.Slack",
                    RegistryDetectKeyword = "Slack"
                },
                new()
                {
                    Id = "signal",
                    Name = "Signal",
                    Description = "Uçtan uca şifreli, gizlilik odaklı mesajlaşma uygulamasının masaüstü sürümü.",
                    Category = StoreCategory.Social,
                    CategoryDisplayName = "İletişim",
                    IconSymbol = "Chat24",
                    Publisher = "Signal Messenger",
                    SizeText = "269 MB",
                    WingetId = "OpenWhisperSystems.Signal",
                    RegistryDetectKeyword = "Signal"
                },
                new()
                {
                    Id = "teamspeak",
                    Name = "TeamSpeak",
                    Description = "Oyuncular için düşük gecikmeli, sunucu tabanlı sesli sohbet.",
                    Category = StoreCategory.Social,
                    CategoryDisplayName = "İletişim",
                    IconSymbol = "Headset24",
                    Publisher = "TeamSpeak Systems",
                    SizeText = "108 MB",
                    WingetId = "TeamSpeakSystems.TeamSpeakClient",
                    RegistryDetectKeyword = "TeamSpeak"
                },
                new()
                {
                    Id = "viber",
                    Name = "Viber",
                    Description = "Ücretsiz sesli/görüntülü arama ve mesajlaşma uygulamasının masaüstü sürümü.",
                    Category = StoreCategory.Social,
                    CategoryDisplayName = "İletişim",
                    IconSymbol = "Call24",
                    Publisher = "Rakuten",
                    SizeText = "142 MB",
                    WingetId = "Rakuten.Viber",
                    RegistryDetectKeyword = "Viber"
                },
                new()
                {
                    Id = "thunderbird",
                    Name = "Thunderbird",
                    Description = "Birden çok e-posta hesabını, takvimi ve kişileri yöneten ücretsiz e-posta istemcisi.",
                    Category = StoreCategory.Social,
                    CategoryDisplayName = "İletişim",
                    IconSymbol = "Mail24",
                    Publisher = "Mozilla",
                    SizeText = "80 MB",
                    WingetId = "Mozilla.Thunderbird",
                    RegistryDetectKeyword = "Thunderbird"
                },

                // ==========================================
                // TARAYICILAR (EK)
                // ==========================================
                new()
                {
                    Id = "librewolf",
                    Name = "LibreWolf",
                    Description = "İzleyicileri varsayılan olarak engelleyen, telemetrisiz ve gizlilik odaklı Firefox sürümü.",
                    Category = StoreCategory.Browsers,
                    CategoryDisplayName = "Tarayıcılar",
                    IconSymbol = "Globe24",
                    Publisher = "LibreWolf",
                    SizeText = "160 MB",
                    WingetId = "LibreWolf.LibreWolf",
                    RegistryDetectKeyword = "LibreWolf"
                },
                new()
                {
                    Id = "zen_browser",
                    Name = "Zen Browser",
                    Description = "Dikey sekmeler ve bölünmüş görünümle sade, Firefox tabanlı modern tarayıcı.",
                    Category = StoreCategory.Browsers,
                    CategoryDisplayName = "Tarayıcılar",
                    IconSymbol = "Globe24",
                    Publisher = "Zen OSS Team",
                    SizeText = "110 MB",
                    WingetId = "Zen-Team.Zen-Browser",
                    RegistryDetectKeyword = "Zen Browser"
                },
                new()
                {
                    Id = "floorp",
                    Name = "Floorp",
                    Description = "Yoğun özelleştirme seçenekleri sunan, Firefox tabanlı tarayıcı.",
                    Category = StoreCategory.Browsers,
                    CategoryDisplayName = "Tarayıcılar",
                    IconSymbol = "Globe24",
                    Publisher = "Ablaze",
                    SizeText = "103 MB",
                    WingetId = "Ablaze.Floorp",
                    RegistryDetectKeyword = "Floorp"
                },

                // ==========================================
                // GELİŞTİRİCİ (EK)
                // ==========================================
                new()
                {
                    Id = "cursor",
                    Name = "Cursor",
                    Description = "Yapay zekâ destekli, VS Code tabanlı kod editörü.",
                    Category = StoreCategory.Developer,
                    CategoryDisplayName = "Geliştirici",
                    IconSymbol = "Code24",
                    Publisher = "Anysphere",
                    SizeText = "196 MB",
                    WingetId = "Anysphere.Cursor",
                    RegistryDetectKeyword = "Cursor"
                },
                new()
                {
                    Id = "jetbrains_toolbox",
                    Name = "JetBrains Toolbox",
                    Description = "IntelliJ IDEA, PyCharm ve Rider gibi JetBrains IDE’lerini kurar ve günceller.",
                    Category = StoreCategory.Developer,
                    CategoryDisplayName = "Geliştirici",
                    IconSymbol = "Toolbox24",
                    Publisher = "JetBrains",
                    SizeText = "142 MB",
                    WingetId = "JetBrains.Toolbox",
                    RegistryDetectKeyword = "JetBrains Toolbox"
                },
                new()
                {
                    Id = "github_desktop",
                    Name = "GitHub Desktop",
                    Description = "Git depolarını komut satırı olmadan yönetmek için görsel istemci.",
                    Category = StoreCategory.Developer,
                    CategoryDisplayName = "Geliştirici",
                    IconSymbol = "BranchFork24",
                    Publisher = "GitHub",
                    SizeText = "293 MB",
                    WingetId = "GitHub.GitHubDesktop",
                    RegistryDetectKeyword = "GitHub Desktop"
                },
                new()
                {
                    Id = "docker_desktop",
                    Name = "Docker Desktop",
                    Description = "Konteyner oluşturma ve çalıştırma için Docker motoru ve arayüzü (WSL 2 gerektirir).",
                    Category = StoreCategory.Developer,
                    CategoryDisplayName = "Geliştirici",
                    IconSymbol = "BoxMultiple24",
                    Publisher = "Docker Inc.",
                    SizeText = "599 MB",
                    WingetId = "Docker.DockerDesktop",
                    RegistryDetectKeyword = "Docker Desktop"
                },
                new()
                {
                    Id = "postman",
                    Name = "Postman",
                    Description = "REST ve GraphQL API’lerini test etmek ve belgelemek için API istemcisi.",
                    Category = StoreCategory.Developer,
                    CategoryDisplayName = "Geliştirici",
                    IconSymbol = "PlugConnected24",
                    Publisher = "Postman",
                    SizeText = "200 MB",
                    WingetId = "Postman.Postman",
                    RegistryDetectKeyword = "Postman"
                },
                new()
                {
                    Id = "dotnet_sdk_10",
                    Name = ".NET SDK 10",
                    Description = "C# ve .NET uygulamaları geliştirmek için derleyici ve komut satırı araçları.",
                    Category = StoreCategory.Developer,
                    CategoryDisplayName = "Geliştirici",
                    IconSymbol = "Code24",
                    Publisher = "Microsoft Corporation",
                    SizeText = "205 MB",
                    WingetId = "Microsoft.DotNet.SDK.10",
                    RegistryDetectKeyword = "Microsoft .NET SDK 10."
                },
                new()
                {
                    Id = "virtualbox",
                    Name = "VirtualBox",
                    Description = "Windows içinde Linux ya da başka bir Windows’u sanal makinede çalıştırır.",
                    Category = StoreCategory.Developer,
                    CategoryDisplayName = "Geliştirici",
                    IconSymbol = "Server24",
                    Publisher = "Oracle",
                    SizeText = "170 MB",
                    WingetId = "Oracle.VirtualBox",
                    RegistryDetectKeyword = "VirtualBox"
                },
                new()
                {
                    Id = "dbeaver",
                    Name = "DBeaver Community",
                    Description = "MySQL, PostgreSQL, SQLite ve daha fazlası için evrensel veritabanı aracı.",
                    Category = StoreCategory.Developer,
                    CategoryDisplayName = "Geliştirici",
                    IconSymbol = "Database24",
                    Publisher = "DBeaver Corp",
                    SizeText = "112 MB",
                    WingetId = "DBeaver.DBeaver.Community",
                    RegistryDetectKeyword = "DBeaver"
                },
                new()
                {
                    Id = "android_studio",
                    Name = "Android Studio",
                    Description = "Android uygulamaları geliştirmek için resmi IDE ve emülatör.",
                    Category = StoreCategory.Developer,
                    CategoryDisplayName = "Geliştirici",
                    IconSymbol = "PhoneLaptop24",
                    Publisher = "Google LLC",
                    SizeText = "1,4 GB",
                    WingetId = "Google.AndroidStudio",
                    RegistryDetectKeyword = "Android Studio"
                },
                new()
                {
                    Id = "winscp",
                    Name = "WinSCP",
                    Description = "SFTP, FTP ve SCP ile sunucuya dosya aktarmak için istemci.",
                    Category = StoreCategory.Developer,
                    CategoryDisplayName = "Geliştirici",
                    IconSymbol = "ArrowSwap24",
                    Publisher = "Martin Prikryl",
                    SizeText = "12 MB",
                    WingetId = "WinSCP.WinSCP",
                    RegistryDetectKeyword = "WinSCP"
                },
                new()
                {
                    Id = "putty",
                    Name = "PuTTY",
                    Description = "Sunuculara SSH ve Telnet ile bağlanmak için klasik terminal istemcisi.",
                    Category = StoreCategory.Developer,
                    CategoryDisplayName = "Geliştirici",
                    IconSymbol = "WindowConsole20",
                    Publisher = "Simon Tatham",
                    SizeText = "4 MB",
                    WingetId = "PuTTY.PuTTY",
                    RegistryDetectKeyword = "PuTTY"
                },

                // ==========================================
                // RUNTIMES (EK)
                // ==========================================
                new()
                {
                    Id = "temurin_21",
                    Name = "Java JRE (Eclipse Temurin 21)",
                    Description = "Güncel Minecraft sürümleri ve modern Java uygulamaları için gereken Java 21 çalışma ortamı.",
                    Category = StoreCategory.Runtimes,
                    CategoryDisplayName = "Runtimes",
                    IconSymbol = "Code24",
                    Publisher = "Eclipse Foundation",
                    SizeText = "33 MB",
                    WingetId = "EclipseAdoptium.Temurin.21.JRE",
                    RegistryDetectKeyword = "Eclipse Temurin JRE with Hotspot 21|Eclipse Temurin JDK with Hotspot 21"
                },
                new()
                {
                    Id = "physx",
                    Name = "NVIDIA PhysX System Software",
                    Description = "Eski oyunların (Borderlands, Mirror’s Edge vb.) fizik efektleri için gereken PhysX bileşeni.",
                    Category = StoreCategory.Runtimes,
                    CategoryDisplayName = "Runtimes",
                    IconSymbol = "DeveloperBoard24",
                    Publisher = "NVIDIA Corporation",
                    SizeText = "33 MB",
                    WingetId = "Nvidia.PhysX",
                    RegistryDetectKeyword = "PhysX"
                }
            };
        }

        public async Task CheckInstalledStatusesAsync(IEnumerable<StoreAppItem> items)
        {
            await Task.Run(() =>
            {
                try
                {
                    var installedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    void ScanKey(RegistryHive hive, string subKey)
                    {
                        try
                        {
                            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                            using var key = baseKey.OpenSubKey(subKey);
                            if (key == null) return;

                            foreach (var name in key.GetSubKeyNames())
                            {
                                try
                                {
                                    using var appKey = key.OpenSubKey(name);
                                    var disp = appKey?.GetValue("DisplayName")?.ToString();
                                    if (!string.IsNullOrWhiteSpace(disp))
                                    {
                                        installedNames.Add(disp);
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }

                    ScanKey(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    ScanKey(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
                    ScanKey(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall");

                    foreach (var app in items)
                    {
                        bool found;
                        if (app.InstallerType == StoreInstallerType.DirectXWeb)
                        {
                            // DirectX çalışma zamanı kaldırma kaydı bırakmaz; "DirectX" anahtarı Windows SDK
                            // bileşenlerini kurulu sayıyordu. Eksik D3DX DLL'lerinin ikisi de yerindeyse kuruludur.
                            found = File.Exists(Path.Combine(Environment.SystemDirectory, "d3dx9_43.dll")) &&
                                    (!Environment.Is64BitOperatingSystem ||
                                     File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "d3dx9_43.dll")));
                        }
                        else
                        {
                            if (string.IsNullOrWhiteSpace(app.RegistryDetectKeyword)) continue;
                            var keywords = app.RegistryDetectKeyword.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            found = installedNames.Any(n => keywords.Any(k => MatchesDetectKeyword(n, k)));
                        }
                        app.IsInstalled = found;
                        if (found && app.Status == StoreInstallStatus.Idle)
                        {
                            app.Status = StoreInstallStatus.Installed;
                            app.StatusMessage = "Sistemde Kurulu";
                            app.ProgressPercentage = 100;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("Kurulu uygulama denetimi sırasında hata oluştu.", ex, nameof(StoreService));
                }
            });
        }

        /// <summary>
        /// Kaldırma kaydındaki ad algılama anahtarıyla eşleşiyor mu. Anahtar "=" ile başlıyorsa ad birebir aynı olmalı:
        /// "Git" gibi kısa adlar alt dize olarak "GitHub Desktop" ya da "Digital…" kayıtlarını da kurulu sayıyordu.
        /// </summary>
        public static bool MatchesDetectKeyword(string displayName, string keyword) =>
            keyword.StartsWith('=')
                ? string.Equals(displayName.Trim(), keyword[1..].Trim(), StringComparison.OrdinalIgnoreCase)
                : displayName.Contains(keyword, StringComparison.OrdinalIgnoreCase);

        public async Task<bool> InstallAppAsync(StoreAppItem app, Action<int, string>? progress = null, CancellationToken cancellationToken = default)
        {
            app.Status = StoreInstallStatus.Downloading;
            app.ProgressPercentage = 0;
            progress?.Report(0, $"{app.Name} hazırlanıyor...");

            try
            {
                switch (app.InstallerType)
                {
                    case StoreInstallerType.DirectXWeb:
                        return await InstallDirectXWebAsync(app, progress, cancellationToken);

                    case StoreInstallerType.Winget:
                    default:
                        return await InstallViaWingetAsync(app, progress, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Kurulum hatası: {app.Name}", ex, nameof(StoreService));
                app.Status = StoreInstallStatus.Failed;
                app.StatusMessage = $"Hata: {ex.Message}";
                progress?.Report(0, $"Hata: {ex.Message}");
                return false;
            }
        }

        #region DirectX End-User Runtime Installer

        /// <summary>
        /// Microsoft'un DirectX web kurulumunu indirir, imzacının "Microsoft Corporation"
        /// olduğunu doğrular ve yönetici olarak çalıştırır (S-10). Dosya rastgele adlı bir
        /// klasöre iner ve çalışırken yazmaya kapalı tutulur; sonuç çıkış kodundan okunur (D-5).
        /// </summary>
        private async Task<bool> InstallDirectXWebAsync(StoreAppItem app, Action<int, string>? progress, CancellationToken ct)
        {
            const string downloadUrl = "https://download.microsoft.com/download/1/7/1/1718CCC4-6315-4D8E-9543-8E28A4E18C4C/dxwebsetup.exe";
            string stagingDir = CreateStagingDirectory();
            string exePath = Path.Combine(stagingDir, "dxwebsetup.exe");

            try
            {
                progress?.Report(10, "Microsoft DirectX Web Kurulumu indiriliyor...");
                await DownloadFileWithProgressAsync(downloadUrl, exePath, (pct, status) =>
                {
                    int overall = 10 + (int)(pct * 0.40);
                    app.ProgressPercentage = overall;
                    app.StatusMessage = $"İndiriliyor: %{pct}";
                    progress?.Report(overall, status);
                }, ct);

                // Doğrulamadan önce kilitle: doğrulama ile çalıştırma arasında dosya değiştirilemesin.
                using var fileLock = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);

                progress?.Report(52, "Dijital imza doğrulanıyor...");
                var signature = Helpers.SignatureInspector.Inspect(exePath);
                if (!signature.IsTrusted || !string.Equals(signature.Signer, "Microsoft Corporation", StringComparison.Ordinal))
                {
                    _log.Warning($"dxwebsetup.exe imzası reddedildi: {signature.Describe()}", null, nameof(StoreService));
                    return Fail(app, progress, $"İndirilen dosyanın imzası doğrulanamadı ({signature.Describe()}). Kurulum yapılmadı.");
                }

                app.Status = StoreInstallStatus.Installing;
                progress?.Report(55, "DirectX kütüphaneleri kuruluyor (sessiz)...");

                var run = await RunElevatedAsync(exePath, "/Q", ct);
                if (run.Cancelled)
                    return Fail(app, progress, "Yönetici izni verilmedi; kurulum iptal edildi.");
                if (!run.Started)
                    return Fail(app, progress, "Kurulum başlatılamadı.");
                if (run.ExitCode != 0)
                    return Fail(app, progress, $"DirectX kurulumu başarısız oldu (çıkış kodu {run.ExitCode}).");

                return Succeed(app, progress, "DirectX Kütüphaneleri Güncel", "DirectX kurulumu tamamlandı.");
            }
            finally
            {
                TryDeleteDirectory(stagingDir);
            }
        }

        #endregion

        #region WinGet güncellemeleri (§5.12)

        public async Task<(IReadOnlyList<Bakım.Core.Store.WingetUpgrade> Upgrades, string? Error)> GetUpgradesAsync(CancellationToken ct = default)
        {
            if (!await IsWingetAvailableAsync())
                return (Array.Empty<Bakım.Core.Store.WingetUpgrade>(), "winget bulunamadı. Microsoft Store'dan 'Uygulama Yükleyicisi'ni kurun.");

            var (exit, stdout, stderr) = await RunWingetCaptureAsync(new[]
            {
                "upgrade", "--source", "winget", "--accept-source-agreements", "--disable-interactivity"
            }, TimeSpan.FromMinutes(2), ct);
            var upgrades = Bakım.Core.Store.WingetTable.ParseUpgrades(stdout);
            if (upgrades.Count == 0 && exit != 0 && exit != WingetNoApplicableUpdate && !string.IsNullOrWhiteSpace(stderr))
                return (upgrades, $"winget listesi alınamadı (0x{exit:X8}).");
            return (upgrades, null);
        }

        public async Task<(bool Success, string Message)> UpgradeAsync(Bakım.Core.Store.WingetUpgrade package, CancellationToken ct = default)
        {
            var (exit, _, _) = await RunWingetCaptureAsync(new[]
            {
                "upgrade", "--id", package.Id, "--exact", "--source", "winget", "--silent",
                "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"
            }, TimeSpan.FromMinutes(30), ct);

            bool reboot = exit == WingetRebootRequiredToFinish || exit == WingetRebootInitiated;
            bool ok = exit == 0 || reboot || exit == WingetNoApplicableUpdate;
            string message = ok
                ? reboot ? $"{package.Name} {package.Available} sürümüne güncellendi; yeniden başlatma gerekiyor."
                         : $"{package.Name} {package.Available} sürümüne güncellendi."
                : $"{package.Name} güncellenemedi (0x{exit:X8}).";

            var activity = App.TryGetService<Activity.IActivityService>();
            if (activity != null) Activity.ActivityRecording.RecordSimple(activity, Core.Activity.ActivityKind.StoreUpdate, "Mağaza",
                ok ? $"\"{package.Name}\" güncellendi" : $"\"{package.Name}\" güncellenemedi",
                $"{package.Version} → {package.Available} · winget ({package.Id})",
                ok ? Core.Activity.ActivityOutcome.Succeeded : Core.Activity.ActivityOutcome.Failed, deepLink: "Store");
            return (ok, message);
        }

        /// <summary>winget'i UTF-8 çıktıyla çalıştırır (tablo sütunları karakter konumuna göre ayrıştırılır).</summary>
        private static async Task<(int ExitCode, string StdOut, string StdErr)> RunWingetCaptureAsync(string[] args, TimeSpan timeout, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            foreach (string a in args) psi.ArgumentList.Add(a);

            using var process = new Process { StartInfo = psi };
            try
            {
                process.Start();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return (-1, string.Empty, ex.Message);
            }
            var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException ex)
                {
                    AppLog.Debug($"winget zaten kapanmış: {ex.Message}", nameof(StoreService));
                }
                return (-1, await stdout, "zaman aşımı");
            }
            return (process.ExitCode, await stdout, await stderr);
        }

        #endregion

        #region WinGet CLI Silent Installer

        // winget çıkış kodları (winget-cli, doc/windows/package-manager/winget/returnCodes.md).
        private const int WingetNoApplicableUpdate = unchecked((int)0x8A15002B);      // zaten kurulu ve güncel
        private const int WingetPackageAlreadyInstalled = unchecked((int)0x8A150061);
        private const int WingetInstallerAlreadyInstalled = unchecked((int)0x8A15010D);
        private const int WingetRebootRequiredToFinish = unchecked((int)0x8A150109);  // kuruldu, yeniden başlatma gerekli
        private const int WingetRebootInitiated = unchecked((int)0x8A15010B);

        private async Task<bool> InstallViaWingetAsync(StoreAppItem app, Action<int, string>? progress, CancellationToken ct)
        {
            var ids = app.BundleWingetIds.Length > 0
                ? app.BundleWingetIds
                : string.IsNullOrWhiteSpace(app.WingetId) ? Array.Empty<string>() : new[] { app.WingetId };
            if (ids.Length == 0)
            {
                throw new InvalidOperationException("Bu uygulama için paket kimliği (WingetId) tanımlanmamış.");
            }

            var failures = new List<string>();
            bool rebootNeeded = false;

            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                int start = i * 100 / ids.Length;
                int span = Math.Max(1, 100 / ids.Length);
                string prefix = ids.Length > 1 ? $"[{i + 1}/{ids.Length}] " : string.Empty;

                app.Status = StoreInstallStatus.Downloading;
                progress?.Report(start, $"{prefix}WinGet üzerinden indiriliyor: {id}...");

                int exitCode = await RunWingetInstallAsync(app, id, prefix, start, span, progress, ct);
                if (exitCode == WingetRebootRequiredToFinish || exitCode == WingetRebootInitiated)
                {
                    rebootNeeded = true;
                }
                else if (exitCode != 0 && exitCode != WingetPackageAlreadyInstalled
                         && exitCode != WingetInstallerAlreadyInstalled && exitCode != WingetNoApplicableUpdate)
                {
                    failures.Add($"{id} (0x{exitCode:X8})");
                    _log.Warning($"winget {id} başarısız: 0x{exitCode:X8}", null, nameof(StoreService));
                }
            }

            if (failures.Count == ids.Length)
            {
                throw new InvalidOperationException(ids.Length == 1
                    ? $"WinGet kurulumu başarısız oldu: {failures[0]}."
                    : $"Hiçbir paket kurulamadı: {string.Join(", ", failures)}");
            }

            if (failures.Count > 0)
            {
                // Bazıları kuruldu: "Kuruldu" demek yanlış olur.
                return Fail(app, progress, $"{ids.Length - failures.Count}/{ids.Length} paket kuruldu. Kurulamayanlar: {string.Join(", ", failures)}");
            }

            string done = rebootNeeded ? "Kuruldu (yeniden başlatma gerekli)" : "Başarıyla Kuruldu";
            return Succeed(app, progress, done, rebootNeeded
                ? $"{app.Name} kuruldu; tamamlanması için bilgisayarı yeniden başlatın."
                : $"{app.Name} başarıyla kuruldu!");
        }

        private static async Task<int> RunWingetInstallAsync(StoreAppItem app, string id, string prefix, int start, int span,
            Action<int, string>? progress, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in new[] { "install", "--id", id, "--exact", "--source", "winget", "--silent",
                                        "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" })
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = psi };
            process.Start();
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(ct)) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var match = Regex.Match(line, @"(\d{1,3})%");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int pct))
                {
                    int overall = Math.Min(99, start + pct * span / 100);
                    app.ProgressPercentage = overall;
                    app.StatusMessage = $"{prefix}İndiriliyor: %{pct}";
                    progress?.Report(overall, prefix + line.Trim());
                }
                else if (line.Contains("Kuruluyor") || line.Contains("Installing") || line.Contains("Starting package install"))
                {
                    app.Status = StoreInstallStatus.Installing;
                    app.StatusMessage = $"{prefix}Kuruluyor...";
                    progress?.Report(Math.Min(99, start + span * 85 / 100), $"{prefix}Kuruluyor...");
                }
            }

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }
            await stderrTask;
            return process.ExitCode;
        }

        #endregion

        #region Helpers

        private sealed record ElevatedRun(bool Started, bool Cancelled, int ExitCode);

        /// <summary>Her indirme için rastgele adlı, kullanıcıya özel bir klasör (sabit %TEMP% adı yerine).</summary>
        private static string CreateStagingDirectory()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Bakim", "Downloads", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void TryDeleteDirectory(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }

        private static async Task<ElevatedRun> RunElevatedAsync(string exePath, string arguments, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
                UseShellExecute = true,
                Verb = "runas"
            };

            Process? process;
            try
            {
                process = Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED (UAC reddi)
            {
                return new ElevatedRun(false, true, -1);
            }

            if (process == null) return new ElevatedRun(false, false, -1);
            using (process)
            {
                await process.WaitForExitAsync(ct);
                return new ElevatedRun(true, false, process.ExitCode);
            }
        }

        private static bool Succeed(StoreAppItem app, Action<int, string>? progress, string status, string message)
        {
            app.Status = StoreInstallStatus.Installed;
            app.IsInstalled = true;
            app.ProgressPercentage = 100;
            app.StatusMessage = status;
            progress?.Report(100, message);
            return true;
        }

        private static bool Fail(StoreAppItem app, Action<int, string>? progress, string message)
        {
            app.Status = StoreInstallStatus.Failed;
            app.StatusMessage = message;
            progress?.Report(app.ProgressPercentage, message);
            return false;
        }

        private static async Task DownloadFileWithProgressAsync(string url, string destinationPath, Action<int, string>? progress, CancellationToken ct)
        {
            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? -1L;
            using var sourceStream = await response.Content.ReadAsStreamAsync(ct);
            using var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true);

            var buffer = new byte[131072]; // 128 KB high-performance buffer
            long totalRead = 0;
            int bytesRead;

            var sw = Stopwatch.StartNew();
            long lastReportMillis = 0;
            long lastReportBytes = 0;
            double currentSpeedMbSec = 0;

            while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await destinationStream.WriteAsync(buffer, 0, bytesRead, ct);
                totalRead += bytesRead;

                long now = sw.ElapsedMilliseconds;
                if (now - lastReportMillis >= 150)
                {
                    double elapsedSec = (now - lastReportMillis) / 1000.0;
                    if (elapsedSec > 0)
                    {
                        currentSpeedMbSec = ((totalRead - lastReportBytes) / (1024.0 * 1024.0)) / elapsedSec;
                    }
                    lastReportMillis = now;
                    lastReportBytes = totalRead;

                    string speedStr = currentSpeedMbSec > 0 ? $" ({currentSpeedMbSec:F1} MB/s)" : "";
                    if (totalBytes > 0)
                    {
                        int pct = (int)((totalRead * 100) / totalBytes);
                        string status = $"İndiriliyor: {FormatBytes(totalRead)} / {FormatBytes(totalBytes)} (%{pct}){speedStr}";
                        progress?.Invoke(pct, status);
                    }
                    else
                    {
                        string status = $"İndiriliyor: {FormatBytes(totalRead)}{speedStr}...";
                        progress?.Invoke(50, status);
                    }
                }
            }

            if (totalBytes > 0)
            {
                progress?.Invoke(100, $"İndirme tamamlandı ({FormatBytes(totalBytes)})");
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F1} MB";
            return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
        }

        #endregion
    }

    internal static class ActionProgressExtensions
    {
        public static void Report(this Action<int, string>? action, int percentage, string status)
        {
            action?.Invoke(percentage, status);
        }
    }
}
