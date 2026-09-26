# Bakım v4.3.0 - Sürüm Notları

## Modüler Depolama & Ağ Panelleri, Akıllı Oyun Kütüphanesi & Fluent Tasarım Revizyonu

Bakım v4.3.0 sürümü; monolitik Depolama ve Ağ sayfalarını yüksek performanslı dikey modüler alt panellere dönüştürmekte, Steam/Epic/GOG akıllı oyun kütüphanesi tarayıcısını entegre etmekte ve Windows 11 Fluent 2 tasarım standartlarını yeni arayüz bileşenleriyle taçlandırmaktadır.

---

### 1. Modüler Depolama Mimarisi (Storage Panels)
- **Ayrık Alt Panel Mimarisi:** Büyük monolitik depolama sayfası bağımsız ve optimize 4 alt panele bölündü:
  - `StorageDiskMapPanel`: Görsel disk kullanım haritası, blok dağılımı ve hızlı erişim paneli.
  - `StorageDuplicatesPanel`: MD5/SHA256 hash tabanlı yinelenen dosya tarayıcısı ve güvenli temizleme.
  - `StorageLargeFilesPanel`: Boyut filtresine göre diskteki devasa dosyaları listeleyen ve yöneten panel.
  - `StorageEmptyFoldersPanel`: Sistemdeki atıl ve boş klasörleri tespit eden temizlik aracı.
- **Asenkron Performans & Sıfır UI Kilitlenmesi:** Tüm dosya sistemi I/O işlemleri arka plan iş parçacıklarına taşınarak akıcı bir arayüz deneyimi sağlandı.

---

### 2. Modüler Ağ İzleyici (Network Monitor Panels)
- **5 Odaklı Ağ Bileşeni:**
  - `NetworkAdaptersPanel`: Fiziksel ve sanal tüm ağ adaptörlerinin IP, MAC, durum ve hız bilgileri.
  - `NetworkConnectionsPanel`: Gerçek zamanlı TCP/UDP bağlantıları, uzak adresler ve ilişkili süreçler.
  - `NetworkDiagnosticsPanel`: Entegre Ping, Traceroute, DNS çözümleme ve ağ sağlık testleri.
  - `NetworkListeningPanel`: Yerel makinede açık olan tüm dinleme portları ve soket dökümleri.
  - `NetworkSpeedTestPanel`: Gerçek zamanlı indirme, yükleme ve gecikme (latency) ölçüm motoru.

---

### 3. Akıllı Oyun Kütüphanesi & Oyun Modu Entegrasyonu
- **Otomatik Kütüphane Taraması (`GameLibraryService`):**
  - **Steam:** `libraryfolders.vdf` ve `appmanifest_*.acf` dosyalarını doğrudan ayrıştıran `GameLibraryParser`.
  - **Epic Games & GOG:** Kurulu oyun manifestslerini sistem kayıtlarından ve varsayılan dizinlerden otomatik tespit.
- **Oyun Modu Eylem Planı (`GameModePlan`):** Algılanan oyunlar tek tıkla Oyun Modu tetikleyici listesine eklenebilir; oyun başladığında arka plan gereksiz servisleri otomatik askıya alınır.

---

### 4. Yeni Windows 11 Fluent 2 Kontrolleri & Tipografi
- **PickListDialog:** Arama ve filtreleme yetenekli modern çoklu seçim diyalogu.
- **KeyCap:** Klavye kısayollarını Windows 11 standartlarında görselleştiren tuş rozeti.
- **StepList:** Süreç adımlarını hiyerarşik ve durum ikonlarıyla listeleyen aşama kontrolü.
- **SummaryStrip:** 4'lü hantal kart blokları yerine tek satırda şık ve derli toplu özet şeridi.
- **ScrollFade:** Uzun listelerde üst ve alt kenarları yumuşakça solduran Fluent görsel efekti.
- **Kenar Çubuğu & Cümle Düzeni:** 5 mantıksal gruba ayrılmış gezinme çubuğu ve Windows 11 yerel cümle düzeni (sentence-case) standartları tamamlandı.

---

### 5. Kalite, Test & Güvenlik Güvencesi
- **%100 Doğrulanmış Tasarım:** `verify-tokens.py`, `verify-symbols.py`, `verify-design-debt.py` ve `verify-bindings.py` kontrollerinin tümü sıfır hatayla geçti.
- **777 Birim Test & UI Duman Testleri:** Çekirdek iş mantığı ve tüm WPF görünümleri eksiksiz doğrulandı.
