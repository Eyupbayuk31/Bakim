# Bakım v3.17.0 - Sürüm Notları

## 13. Bağımsız Modül: Yazılım & All-in-One Runtimes Mağazası (Store Hub)

- **TechPowerUp Visual C++ All-in-One Entegrasyonu:**
  - 2005'ten 2022'ye kadar olan tüm x86 ve x64 Microsoft Visual C++ Redistributable paketlerini TechPowerUp resmi CDN sunucularından (DE/NL sunucu yük dengelemesi ve 302 yönlendirme takibi ile) doğrudan indiren, ZIP arşivini otomatik açan ve `install_all.bat` ile tüm paketleri arka planda sessizce kuran otonom kurulum motoru entegre edildi.
  - Olası ağ engellerine karşı doğrudan GitHub (abbodi1406) vcredist yedeğine geçiş yapan çift katmanlı failover koruması eklendi.

- **DirectX End-User Web Setup Otomasyonu:**
  - Microsoft resmi sunucularından `dxwebsetup.exe` dosyasını indirerek `/Q` sessiz parametresi ile kullanıcı müdahalesine gerek kalmadan tüm eski ve yeni DirectX kütüphanelerini kuran bağımsız runtime kurulum mekanizması sağlandı.

- **45+ Popüler Uygulama Kataloğu & 7 Kategori:**
  - **Runtimes:** Visual C++ All-in-One, DirectX End-User Runtime, .NET Desktop Runtime 8/9/10, Java OpenJDK, OpenAL.
  - **Oyun & İstemciler:** Steam, Epic Games Launcher, EA App, Ubisoft Connect, Battle.net, GOG Galaxy.
  - **Müzik & Medya:** Spotify, Discord, VLC Media Player, OBS Studio, HandBrake, Foobar2000, Audacity.
  - **Yazılım & Sistem Araçları:** WinRAR, 7-Zip, Notepad++, CPU-Z, GPU-Z, HWMonitor, CrystalDiskInfo, qBittorrent, Everything Search, ShareX, Rufus.
  - **Web Tarayıcıları:** Google Chrome, Mozilla Firefox, Brave Browser, Opera GX, Microsoft Edge, Vivaldi, Tor Browser.
  - **İletişim & Sosyal:** Telegram Desktop, WhatsApp, Signal, Zoom, Slack.
  - **Geliştirici & Kodlama:** Visual Studio Code, Git for Windows, Node.js LTS, Python, Docker Desktop, Postman.

- **4 Akıllı Hazır Paket (Quick Presets):**
  - **Format Kurtarıcı:** VC++ AIO, DirectX, Chrome, 7-Zip, WinRAR, VLC, Spotify tek tıkla seçilip kuruluma hazır hale getirilir.
  - **Oyuncu Paketi:** VC++ AIO, DirectX, Steam, Discord, Epic Games, OBS Studio, 7-Zip tek tıkla sıraya alınır.
  - **Ofis & Medya:** Chrome, Spotify, VLC, Discord, Telegram, 7-Zip, Notepad++ tek tıkla seçilir.
  - **Geliştirici Paketi:** VS Code, Git, Node.js, Python, Chrome, 7-Zip, Notepad++ tek tıkla seçilir.

- **Kayıt Defteri (Registry) Tabanlı Akıllı Kurulu Yazılım Algılama:**
  - `HKLM` ve `HKCU` 32-bit & 64-bit Uninstall kayıt anahtarlarını tarayarak sistemde zaten kurulu olan uygulamaları otomatik tespit eden, arayüzde yeşil 'Yüklü' rozeti ile belirten ve çift kuruluma engel olan koruma mekanizması.

- **Toplu Kurulum Sırası & Canlı Konsol Çekmecesi:**
  - Seçilen çoklu uygulamaları kuyruğa alıp sırayla indiren, kuran, anlık indirme yüzdesini ve kurulum durumunu (`İndiriliyor`, `Yükleniyor`, `Tamamlandı`, `Hata`) raporlayan interaktif konsol.
  - Windows Package Manager (`winget`) CLI motoru ve doğrudan kurulum desteği.

- **Kusursuz Fluent 2 Tasarım:**
  - Modern Slate Dark paleti, Hero Banner, 4 KPI telemetri kartı, animasyonlu ilerleme çubukları, durum rozetleri ve responsive WrapPanel mimarisi.
