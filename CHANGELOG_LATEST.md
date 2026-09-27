# Bakım v4.20.0 - Sürüm Notları

## Mağaza: 59'dan 147 Uygulamaya

Mağaza kataloğu 88 yeni uygulama, 4 yeni kategori ve 4 yeni hazır paketle genişledi. Her uygulamanın winget kimliği tek tek doğrulandı; gösterilen boyutlar tahmin değil, yükleyicinin gerçek boyutu.

### Yeni kategoriler
- **Güvenlik ve gizlilik:** Malwarebytes, Bitwarden, KeePassXC, Proton VPN, Cloudflare WARP, WireGuard, Tailscale, VeraCrypt, Portmaster, simplewall, Sandboxie Plus.
- **Ofis ve verimlilik:** LibreOffice, ONLYOFFICE, SumatraPDF, Adobe Acrobat Reader, PDFsam, Obsidian, Notion, Joplin, Google Drive, Dropbox, LocalSend, DeepL, calibre, Flow Launcher, Ditto.
- **Grafik ve tasarım:** GIMP, Paint.NET, Krita, Inkscape, Blender, Figma, IrfanView, XnView MP, darktable, Greenshot, Flameshot, Upscayl.
- **Sistem ve donanım:** CPU-Z, GPU-Z, HWiNFO, CrystalDisk araçları, DDU, Rufus ve Ventoy buraya taşındı. Yeni: TreeSize, WizTree, WinDirStat, Core Temp, Fan Control, NVCleanstall, Open-Shell, Twinkle Tray, f.lux.

### Mevcut kategorilere eklenenler
- **Oyun:** Playnite, Heroic Games Launcher, Prism Launcher (Minecraft), Parsec, Moonlight, RivaTuner Statistics Server.
- **Müzik ve medya:** Kdenlive, Shotcut, MPC-HC, PotPlayer, foobar2000, K-Lite Codec Pack, Stremio, Voicemeeter, Streamlabs.
- **Yazılım ve araçlar:** PeaZip, TeraCopy, Free Download Manager, JDownloader 2, TeamViewer.
- **İletişim:** Slack, Signal, TeamSpeak, Viber, Thunderbird.
- **Tarayıcılar:** LibreWolf, Zen Browser, Floorp.
- **Geliştirici:** Cursor, JetBrains Toolbox, GitHub Desktop, Docker Desktop, Postman, .NET SDK 10, VirtualBox, DBeaver, Android Studio, WinSCP, PuTTY.
- **Çalışma zamanları:** Java 21 (güncel Minecraft için), NVIDIA PhysX.

### Yeni hazır paketler
- **Güvenlik & Gizlilik**
- **Yayıncı & İçerik Üreticisi**
- **Donanım Teşhis & İzleme**
- **Öğrenci & Ücretsiz Ofis**

### Düzeltmeler
- **Git yanlış algılanıyordu:** Adında "Git" geçen her program (GitHub Desktop gibi) Git'i "Kurulu" gösteriyordu. Artık ad birebir eşleşmeli.
- **Java 17 yanlış algılanıyordu:** Başka bir Java sürümü kuruluyken de "Kurulu" görünüyordu.
- **DirectX yanlış algılanıyordu:** Windows SDK bileşenleri yüzünden "Kurulu" görünebiliyordu. Artık gerçek DirectX dosyalarına bakılıyor.

**Testler:** Yeni katalog testleri; yinelenen kimlik, hazır paketteki geçersiz uygulama, geçersiz ikon ve başka programları da yakalayan kısa algılama anahtarlarını denetliyor. Tüm birim testleri ve XAML duman testi yeşil; 4 denetim betiği tam geçti.
