# Bakım v3.19.0 - Sürüm Notları

## Kurulum Nöbetçisi (Sentinel Setup Guard) & Sezgisel EDR Analizör Tehdit Taraması

### 1. Otonom Kurulum Yakalama Motoru (Setup Sentinel Watchdog Service)
- **Gerçek Zamanlı Süreç ve Alt Süreç İzleme:** Sisteme yeni bir program kurulurken (`.exe`, `.msi`) 1.5 saniyelik mikro aralıklarla süreci ve alt süreç ağacını (`TrackedProcessIds`) otomatik tespit eder. Kurulum tamamlanana kadar (tüm kurulum süreçleri kapanana kadar) oturumu açık tutar.
- **7 Kritik Dizin Dosya Sistemi Nöbetçisi (`FileSystemWatcher`):** `Program Files`, `Program Files (x86)`, `ProgramData`, `AppData\Local`, `AppData\Roaming`, `Desktop` ve `Public Desktop` dizinlerinde oluşturulan tüm dosyaları (`Created`, `Changed`, `Renamed`) anlık yakalar.
- **Kayıt Defteri Çift Snapshot (Delta Analizi):** Kurulum başlamadan önce ve tamamlandıktan sonra Kayıt Defteri'nin kritik anahtarlarının (`Uninstall`, `Run`, `Services`, `Classes`) fotoğrafını çekip farkı sıfır hata payıyla çıkarır.

### 2. Canlı Masaüstü Bildirim HUD'ı (`SetupDetectedFlyoutWindow`)
- **Fluent 2 Masaüstü Bildirimi:** Ekranın sağ alt köşesinde (sistem tepsisi hizasında) modern Slate Dark temalı, yarı saydam ve hafif gölgeli floating bildirim penceresi.
- **Canlı İzleme Durumu:** Kurulum çalışırken kurulmakta olan uygulamanın adı ve sürecin canlı izlendiği bilgisini sunar.
- **Kurulum Tamamlandı & Güvenlik Taraması Önerisi:** Kurulum bittiğinde eklenen dosya adedi, yürütülebilir (`.exe`/`.dll`) adedi, kayıt defteri girdi adedi ve toplam boyut özetini rozetlerle sunarak *"Kurulan dosyalar Analizör ile güvenlik, dijital imza ve VirusTotal taramasından geçirilsin mi?"* sorusunu yöneltir.

### 3. Analizör Modülü ile Kusursuz EDR Entegrasyonu
- **Tek Tıkla Tehdit Taraması:** Bildirimden veya detay penceresinden *"Analizör ile Tara"* tıklandığında ana pencere otomatik öne getirilir, Analizör sekmesine geçilir ve kurulan tüm yürütülebilir dosyalar listeye enjekte edilerek derin AI tehdit analizinden geçirilir.
- **Dijital İmza & PE Güvenlik Doğrulaması:** Dosyaların Microsoft/üçüncü parti dijital imzaları, PE başlıkları, entropi oranları ve SHA-256 kriptografik hash'leri anında hesaplanır.
- **VirusTotal API v3 Entegrasyonu:** Taranan dosyalar tek tıkla 70+ antivirüs motoruna karşı doğrulanır.

### 4. Kurulum Değişiklikleri İnceleme Penceresi (`InstallationDeltaInspectionDialog`)
- **3 Sekmeli Kapsamlı Rapor:**
  1. *Eklenen Dosyalar:* Her dosyanın yolu, boyutu, uzantı rozeti, klasörde gösterme ve tek tıkla dosya özelinde derin analiz yapma butonu.
  2. *Kayıt Defteri Değişiklikleri:* Eklenen veya değişen hive, anahtar yolu ve değer dökümü.
  3. *Servisler & Başlangıç:* Sisteme yeni kaydolan arka plan Windows servisleri ve otomatik başlangıç (`Run`) girdileri.
- **Anlık Canlı Arama:** Arama kutusu üzerinden yüzlerce dosya ve kayıt arasında anında filtreleme.
- **JSON Formatında Dışa Aktarma:** Sistem değişiklik raporunu sıfır kalıntı bırakmadan incelemek veya arşivlemek için tek tıkla JSON çıktısı alma.

### 5. Modül Tercihleri & Ayarlar Yönetimi
- **Kullanıcı Kontrollü Koruma:** Ayarlar modülü içerisinden Kurulum Nöbetçisi tek tıkla açılıp kapatılabilir; sistem performansına sıfır etkiyle çalışır.

### 6. Kalite, Test ve Doğrulama
- **197/197 Birim Testi:** Yeni `SetupSentinelTests` ve `DependencyInjectionTests` ile model serileştirmesi, heuristik süreç eşleme ve konteyner bağımlılıkları %100 doğrulandı.
- **9.235 Sembol & Tasarım Tokeni:** `verify-symbols.py` ve `verify-tokens.py` ile sıfır hata ve sıfır emoji kuralına tam uyum sağlandı.
