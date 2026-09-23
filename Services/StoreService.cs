using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
            return await Task.Run(() =>
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = "winget",
                        Arguments = "--version",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true
                    });
                    p?.WaitForExit(3000);
                    return p?.ExitCode == 0;
                }
                catch
                {
                    return false;
                }
            });
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
                    Id = "tpu_vcredist_aio",
                    Name = "Visual C++ All-in-One (TechPowerUp)",
                    Description = "2005'ten 2022'ye kadar (x86 & x64) tüm Microsoft Visual C++ kütüphanelerini tek seferde kurar.",
                    Category = StoreCategory.Runtimes,
                    CategoryDisplayName = "Runtimes",
                    IconSymbol = "DeveloperBoard24",
                    Publisher = "TechPowerUp / abbodi1406",
                    SizeText = "82.4 MB",
                    InstallerType = StoreInstallerType.TechPowerUpVcAio,
                    RegistryDetectKeyword = "Visual C++ 2015-2022"
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
                    RegistryDetectKeyword = "Eclipse Temurin"
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
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
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
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
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
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
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
                    Category = StoreCategory.Software,
                    CategoryDisplayName = "Yazılım & Araçlar",
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
                    RegistryDetectKeyword = "Git"
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
                        if (string.IsNullOrWhiteSpace(app.RegistryDetectKeyword)) continue;

                        bool found = installedNames.Any(n => n.IndexOf(app.RegistryDetectKeyword, StringComparison.OrdinalIgnoreCase) >= 0);
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

        public async Task<bool> InstallAppAsync(StoreAppItem app, Action<int, string>? progress = null, CancellationToken cancellationToken = default)
        {
            app.Status = StoreInstallStatus.Downloading;
            app.ProgressPercentage = 0;
            progress?.Report(0, $"{app.Name} hazırlanıyor...");

            try
            {
                switch (app.InstallerType)
                {
                    case StoreInstallerType.TechPowerUpVcAio:
                        return await InstallTechPowerUpVcAioAsync(app, progress, cancellationToken);

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

        #region TechPowerUp Visual C++ All-in-One Downloader & Silent Installer

        private async Task<bool> InstallTechPowerUpVcAioAsync(StoreAppItem app, Action<int, string>? progress, CancellationToken ct)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "Bakim_VCRedist_AIO");
            string zipFile = Path.Combine(Path.GetTempPath(), "Visual-C-Runtimes-All-in-One.zip");

            try
            {
                progress?.Report(5, "TechPowerUp sunucuları taranıyor...");
                string downloadUrl = await ResolveTechPowerUpUrlAsync();

                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    // Fallback to official GitHub Release of abbodi1406
                    progress?.Report(10, "Ayna sunucuya geçiliyor (GitHub Mirror)...");
                    downloadUrl = "https://github.com/abbodi1406/vcredist/releases/latest/download/Visual-C-Runtimes-All-in-One-May-2026.zip";
                }

                progress?.Report(15, "Paket indiriliyor (82.4 MB)...");
                await DownloadFileWithProgressAsync(downloadUrl, zipFile, (pct, status) =>
                {
                    // Map 0-100% to 15-70% overall progress
                    int overall = 15 + (int)(pct * 0.55);
                    app.ProgressPercentage = overall;
                    app.StatusMessage = $"İndiriliyor: %{pct}";
                    progress?.Report(overall, status);
                }, ct);

                if (!File.Exists(zipFile))
                {
                    throw new FileNotFoundException("İndirilen zip dosyası bulunamadı.");
                }

                app.Status = StoreInstallStatus.Installing;
                progress?.Report(75, "Arşiv çıkartılıyor...");
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                Directory.CreateDirectory(tempDir);

                ZipFile.ExtractToDirectory(zipFile, tempDir, true);

                progress?.Report(80, "Tüm Visual C++ kütüphaneleri kuruluyor (Sessiz Mod)...");

                string batFile = Path.Combine(tempDir, "install_all.bat");
                if (!File.Exists(batFile))
                {
                    var foundBat = Directory.GetFiles(tempDir, "*.bat").FirstOrDefault();
                    if (foundBat != null) batFile = foundBat;
                }

                if (!File.Exists(batFile))
                {
                    throw new FileNotFoundException("Kurulum betiği (install_all.bat) bulunamadı.");
                }

                // Run install_all.bat silently in background as administrator
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batFile}\" /y",
                    WorkingDirectory = tempDir,
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using var p = Process.Start(psi);
                if (p != null)
                {
                    await p.WaitForExitAsync(ct);
                }

                progress?.Report(98, "Geçici dosyalar temizleniyor...");
                try { if (File.Exists(zipFile)) File.Delete(zipFile); } catch { }
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }

                app.Status = StoreInstallStatus.Installed;
                app.IsInstalled = true;
                app.ProgressPercentage = 100;
                app.StatusMessage = "Tüm Visual C++ Kütüphaneleri Kurulu";
                progress?.Report(100, "Tebrikler! 2005-2022 tüm C++ kütüphaneleri başarıyla kuruldu.");
                return true;
            }
            catch
            {
                try { if (File.Exists(zipFile)) File.Delete(zipFile); } catch { }
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                throw;
            }
        }

        private async Task<string> ResolveTechPowerUpUrlAsync()
        {
            try
            {
                string pageUrl = "https://www.techpowerup.com/download/visual-c-redistributable-runtime-package-all-in-one/";
                var req = new HttpRequestMessage(HttpMethod.Get, pageUrl);
                var pageResp = await HttpClient.SendAsync(req);
                var html = await pageResp.Content.ReadAsStringAsync();

                // Find active id: <input type="hidden" name="id" value="3150" />
                var idMatch = Regex.Match(html, @"name=""id""\s+value=""(\d+)""");
                string id = idMatch.Success ? idMatch.Groups[1].Value : "3150";

                // Server IDs: 27 (DE), 25 (NL), 5 (UK-1), 22 (UK-2)
                var servers = new[] { "27", "25", "5", "22", "12" };

                foreach (var serverId in servers)
                {
                    try
                    {
                        var postData = new Dictionary<string, string>
                        {
                            { "id", id },
                            { "server_id", serverId }
                        };

                        using var postReq = new HttpRequestMessage(HttpMethod.Post, pageUrl)
                        {
                            Content = new FormUrlEncodedContent(postData)
                        };

                        // Send with auto-redirect disabled to catch the 302 Location header
                        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
                        client.DefaultRequestHeaders.UserAgent.ParseAdd(HttpClient.DefaultRequestHeaders.UserAgent.ToString());

                        using var resp = await client.SendAsync(postReq);
                        if ((int)resp.StatusCode == 302 && resp.Headers.Location != null)
                        {
                            return resp.Headers.Location.ToString();
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"TechPowerUp indirme adresi çözülemedi: {ex.Message}", ex, nameof(StoreService));
            }

            return string.Empty;
        }

        #endregion

        #region DirectX End-User Runtime Installer

        private async Task<bool> InstallDirectXWebAsync(StoreAppItem app, Action<int, string>? progress, CancellationToken ct)
        {
            string tempExe = Path.Combine(Path.GetTempPath(), "dxwebsetup.exe");
            string downloadUrl = "https://download.microsoft.com/download/1/7/1/1718CCC4-6315-4D8E-9543-8E28A4E18C4C/dxwebsetup.exe";

            try
            {
                progress?.Report(10, "Microsoft DirectX Web Kurulumu indiriliyor...");
                await DownloadFileWithProgressAsync(downloadUrl, tempExe, (pct, status) =>
                {
                    int overall = 10 + (int)(pct * 0.40);
                    app.ProgressPercentage = overall;
                    app.StatusMessage = $"İndiriliyor: %{pct}";
                    progress?.Report(overall, status);
                }, ct);

                app.Status = StoreInstallStatus.Installing;
                progress?.Report(55, "DirectX kütüphaneleri taranıyor ve kuruluyor (/Q Sessiz)...");

                var psi = new ProcessStartInfo
                {
                    FileName = tempExe,
                    Arguments = "/q",
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using var p = Process.Start(psi);
                if (p != null)
                {
                    await p.WaitForExitAsync(ct);
                }

                try { if (File.Exists(tempExe)) File.Delete(tempExe); } catch { }

                app.Status = StoreInstallStatus.Installed;
                app.IsInstalled = true;
                app.ProgressPercentage = 100;
                app.StatusMessage = "DirectX Kütüphaneleri Güncel";
                progress?.Report(100, "DirectX kurulumu başarıyla tamamlandı.");
                return true;
            }
            catch (Exception)
            {
                try { if (File.Exists(tempExe)) File.Delete(tempExe); } catch { }
                throw;
            }
        }

        #endregion

        #region WinGet CLI Silent Installer

        private async Task<bool> InstallViaWingetAsync(StoreAppItem app, Action<int, string>? progress, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(app.WingetId))
            {
                throw new InvalidOperationException("Bu uygulama için paket kimliği (WingetId) tanımlanmamış.");
            }

            app.Status = StoreInstallStatus.Downloading;
            progress?.Report(10, $"WinGet üzerinden indiriliyor: {app.Name} ({app.WingetId})...");

            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = $"install --id \"{app.WingetId}\" --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var readOutputTask = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync(ct);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        if (line.Contains("%"))
                        {
                            var match = Regex.Match(line, @"(\d{1,3})%");
                            if (match.Success && int.TryParse(match.Groups[1].Value, out int pct))
                            {
                                app.ProgressPercentage = Math.Min(95, pct);
                                app.StatusMessage = $"İndiriliyor: %{pct}";
                                progress?.Report(pct, line.Trim());
                            }
                        }
                        else if (line.Contains("Kuruluyor") || line.Contains("Installing") || line.Contains("Starting package install"))
                        {
                            app.Status = StoreInstallStatus.Installing;
                            app.StatusMessage = "Kuruluyor...";
                            progress?.Report(85, "Kuruluyor...");
                        }
                    }
                }
            }, ct);

            await process.WaitForExitAsync(ct);
            await readOutputTask;

            if (process.ExitCode == 0 || process.ExitCode == -1978335189) // 0x8A15002B: Already installed
            {
                app.Status = StoreInstallStatus.Installed;
                app.IsInstalled = true;
                app.ProgressPercentage = 100;
                app.StatusMessage = "Başarıyla Kuruldu";
                progress?.Report(100, $"{app.Name} başarıyla kuruldu!");
                return true;
            }

            throw new InvalidOperationException($"WinGet kurulumu başarısız oldu (Hata Kodu: {process.ExitCode}).");
        }

        #endregion

        #region Helpers

        private static async Task DownloadFileWithProgressAsync(string url, string destinationPath, Action<int, string>? progress, CancellationToken ct)
        {
            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? -1L;
            using var sourceStream = await response.Content.ReadAsStreamAsync(ct);
            using var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await destinationStream.WriteAsync(buffer, 0, bytesRead, ct);
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    int pct = (int)((totalRead * 100) / totalBytes);
                    string status = $"İndiriliyor: {FormatBytes(totalRead)} / {FormatBytes(totalBytes)} (%{pct})";
                    progress?.Invoke(pct, status);
                }
                else
                {
                    string status = $"İndiriliyor: {FormatBytes(totalRead)}...";
                    progress?.Invoke(50, status);
                }
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
