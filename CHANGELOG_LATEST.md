# Bakım v3.17.3 - Sürüm Notları

## Yazılım & Runtimes Mağazası Arayüz Revizyonu & Çift Sekmeli Mimari 🎨📦

- **Görsel Kalabalıktan Arındırılmış Ferah Açılış:**
  - Mağaza açılışını boğan ve ekranın yarısını işgal eden devasa *"Format Sonrası Can Kurtaran All-in-One Runtimes"* hero banner'ı varsayılan katalog görünümünden tamamen kaldırıldı.
  - Açılışta kullanıcıyı doğrudan temiz, nefes alan, arama ve kategorilere odaklı **Uygulama Kataloğu** karşılar.

- **Çift Modlu / Sekmeli Fluent 2 Navigasyon:**
  - Mağazanın üst kısmına şık bir Segmented Mode Switcher entegre edildi:
    - `[ 📦 Uygulama Kataloğu (46 Yazılım) ]`: 46+ popüler masaüstü uygulamasını geniş kart ızgarasıyla gezin, arayın ve tek tek veya toplu seçip kurun.
    - `[ ⚡ Hazır Paketler & Format Kurtarıcı (4 Hazır Set + VC++ AIO) ]`: Yalnızca ihtiyaç duyulduğunda açılan, zengin kartlara ve detaylara sahip küratörlü paket merkezi.

- **4 Adet Zenginleştirilmiş Akıllı Hazır Paket Kartı:**
  - Sıkışık buton çubuğu kaldırıldı; yerine her biri içerdiği yazılımların rozetlerini, sistemdeki kurulu durumunu (Örn: *5/8 Kurulu*) ve çoklu eylemleri barındıran 4 adet büyük interaktif kart eklendi:
    1. 🚀 **Format Sonrası Temel Paket** (VC++ AIO, DirectX Web, Chrome, WinRAR, 7-Zip, Spotify, VLC, Discord)
    2. 🎮 **Oyuncu & Gaming Platformları** (Steam, Epic Games, Discord, Spotify, VC++ AIO, DirectX, WinRAR)
    3. 💼 **Ofis, Üretkenlik & İletişim** (Chrome, WhatsApp, Telegram, 7-Zip, Notepad++, PowerToys, VLC)
    4. 💻 **Yazılımcı & Geliştirici Ortamı** (VS Code, Git, Python 3, Node.js LTS, Windows Terminal, PowerToys, 7-Zip)
  - Her kart üzerinden:
    - *"Bu Paketi Seç"*: Uygulamaları kuyruğa işaretler.
    - *"Seç & Kataloğa Git"*: Seçimi yapıp doğrudan uygulama listesine döner.
    - *"Hemen Kur"*: Seçilen paketi tek tıkla arka planda kurmaya başlar.

- **Runtimes Hub Konumlandırması:**
  - TechPowerUp Visual C++ (2005-2022 x86/x64) AIO, DirectX Web Setup ve .NET Desktop çalışma ortamları, doğal yeri olan *Hazır Paketler & Format Kurtarıcı* sekmesinin en tepesinde şık bir vitrin kartı olarak konumlandırıldı.

- **Sıfır Geçersiz Sembol & Tasarım Doğrulaması:**
  - `Tools/verify-symbols.py` ve `Tools/verify-tokens.py` araçlarıyla tüm sembol ve tokenlar 100% doğrulandı.
