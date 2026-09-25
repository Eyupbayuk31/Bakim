# Bakım v4.0.0 - Sürüm Notları

## Bakım 4.0: Tam Koruma Modu, NTFS USN Değişiklik Günlüğü, Kernel Süreç Takibi & Windows Sandbox Önizleme Motoru

Bakım v4.0.0 sürümü, sistem güvenliği ve izleme mimarisinde çığır açan bir dönüm noktasıdır. Düşük seviye Windows çekirdek ve dosya sistemi sensörleri, sıfır UAC sürtünmeli sağ tık yürütücüsü ve izole Windows Sandbox önizleme motoru doğrudan kullanıma sunulmuştur.

---

### 1. Kurulum Nöbetçisi: Tam Koruma Modu & Düşük Seviye Sensörler (NÖB Faz 8)
- **NTFS USN (Update Sequence Number) Değişiklik Günlüğü:** Win32 `FSCTL_QUERY_USN_JOURNAL` ve `FSCTL_READ_USN_JOURNAL` API'leri ile doğrudan disk sürücüsü günlüğünü tarayarak kurulumların oluşturduğu tüm dosyaları donanım seviyesinde sıfır kaçırma garantisiyle yakalar.
- **Kernel Süreç Başlatma İzleyicisi (WMI Win32_ProcessStartTrace):** Kurulumların arka planda sessizce çatallandırdığı (fork/spawn) geçici alt süreçleri sub-millisecond hızla tespit eder; süreç ağacına ekleyerek atıf doğruluğunu %100'e çıkarır.
- **Kademeli Koruma Mimarisi:** Yönetici izinleri ve USN/Kernel sensörleri mevcutsa otomatik olarak **"Tam Koruma Modu"** devreye girer. Standart kullanıcı kipi senaryolarında ise 64 KB genişletilmiş FSW tamponu ve Kayıt Defteri Hotspot sensörleriyle **"Temel Mod"** kusursuz çalışır.
- **Canlı Sensör Telemetrisi:** Kurulum Nöbetçisi başlığında ve inceleme ekranlarında hangi sensörlerin aktif olduğunu gösteren modern durum rozeti ve sensör dökümü yer alır.

---

### 2. Sıfır UAC Sürtünmeli Sağ Tık: BakimShell (KAL C3)
- **Hafif `asInvoker` Shell Yürütücüsü:** Masaüstü veya Windows Gezgini'nde bir kısayola, klasöre veya yürütülebilir dosyaya sağ tıklayıp *"Bakım ile Kaldır"* seçildiğinde hiçbir UAC onay penceresi açılmaz.
- **Yüksek Hızlı Yerel IPC Mailbox:** `BakimShell.exe`, halihazırda çalışan yönetici yetkili Bakım sürecini yerel Mutex ve dosya tabanlı güvenli IPC gelen kutusu (`%LocalAppData%\Bakim\ipc`) üzerinden <200 milisaniye içinde haberdar eder.
- **Kusursuz Otomatik Başlatma:** Bakım açık değilse `BakimShell`, tek bir UAC adımıyla ana Bakım uygulamasını doğru parametrelerle ayağa kaldırır.

---

### 3. Windows Sandbox Önizleme Motoru (NÖB Faz 10)
- **İzole Sıfır-Risk Ortamı:** Şüpheli, imzasız veya riskli görünen kurulum paketleri ana işletim sistemine temas etmeden tek tıkla izole Windows Sandbox içerisinde çalıştırılabilir.
- **Dinamik `.wsb` Profil Üreticisi:** Kurulum dosyasını otomatik olarak salt-okunur (read-only) geçici bir dizine eşleyen ve Sandbox açıldığında kurulumu otomatik başlatan optimize edilmiş XML profil dosyası oluşturulur.
- **Tehdit Analizi Entegrasyonu:** Tehdit Analiz ekranında ve Kurulum İnceleme diyaloglarında "Sandbox'ta Önizle" eylemi ile tek tıkla güvenli laboratuvar başlatılır.

---

### 4. Gelişmiş Tehdit Analiz ve İnceleme Diyalogları
- **Tehdit Analiz Diyaloğu:** Yeni "Sandbox'ta Önizle" butonu ve taşma menüsü eylemleriyle zenginleştirildi; Sandbox desteği sistemde yoksa kullanıcıyı nazikçe bilgilendirir.
- **Kurulum Fark İnceleme Diyaloğu (Delta Inspection):** Tam Koruma Modu durum rozeti eklendi; oturum verilerini Sandbox üzerinden yeniden simüle etme imkanı sağlandı.
- **Kurulum Algılandı Bildirim Penceresi (Flyout v2):** Anlık koruma seviyesi durumu ("Tam Koruma Modu" / "Temel Mod") kullanıcıya şeffafça sunuldu.

---

### 5. Inno Setup & CI/CD Pipeline Entegrasyonu
- **Tek Sürüm Kaynağı (H-15):** Tüm sürüm meta verileri `Directory.Build.props` üzerinden 4.0.0 olarak yönetilir; derleme zinciri ve Inno Setup installer otomatik olarak `BakimShell.exe`'yi paketler.
- **Eksiksiz Dağıtım:** GitHub Releases ve Inno Setup kurulum paketlerine `Bakim.exe`'nin yanı sıra bağımsız `BakimShell.exe` dahil edilmiştir.
