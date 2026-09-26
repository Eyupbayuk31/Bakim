# Bakım v4.6.0 - Sürüm Notları

## Windows 11 Cam Tasarımı Tamamlaması, Dinamik Malzeme Seçimi & Kusursuz Sol Menü

Bakım v4.6.0 sürümü; Windows 11 Fluent 2 cam (Mica + Glassmorphism) malzeme modelini uygulamanın tüm sayfalarına, panellerine ve bileşenlerine eksiksiz yaymakta; DWM malzeme seçiciyi, ortam ışığı denetimini ve sol menü iyileştirmelerini kullanıcılara sunmaktadır.

---

### 1. Dinamik DWM Pencere Malzemesi Seçici (Mica, Mica Alt, Akrilik, Opak)
- **Windows 11 DWM Entegrasyonu:**
  - Kullanıcılar Ayarlar modülünden pencere arka plan malzemesini anında değiştirebilir:
    - **Mica:** Klasik Windows 11 masaüstü duvar kağıdı rengini yumuşak yansıtan dinamik katman.
    - **Mica Alt (Tabbed):** Sekmeli ve çok katmanlı pencereler için daha zengin ve kontrastlı cam malzemesi.
    - **Akrilik (Acrylic):** Arka plandaki pencereleri ve masaüstünü bulanıklaştıran derin buzlu cam etkisi.
    - **Kapalı (None):** Saydamlık efektlerini kapatarak donanım dostu opak pencere zemini.
  - Seçim `AppSettingsData` içine kalıcı olarak kaydedilir ve uygulama açılışında anında geri yüklenir.

---

### 2. Ortam Işığı (Ambient Light) Denetimi & Genişletilmiş Auralar
- **Kişiselleştirilebilir Işık Aurası:**
  - Ayarlar sayfasına "Ortam ışığı (Ambient Light)" açma/kapatma anahtarı eklendi.
  - Işık auraları pencere boyutuna göre optimize edildi: sağ-üst köşe 1050×750 px, sol-alt köşe 850×600 px boyutlarına büyütülerek cam kartların arkasından süzülen ışık derinliği belirginleştirildi.
  - Düşük donanımlı sistemler veya minimalist kullanıcılar için tek tıkla gizlenebilir mimari sağlandı.

---

### 3. Kusursuz Sol Menü (Sidebar) Modernizasyonu
- **Başlık Alanı & Hamburger Hizalaması:**
  - Menü başındaki gereksiz çift "Bakım" başlığı kaldırılarak modern "Gezinme" etiketine dönüştürüldü.
  - Hamburger daraltma butonu alt menü öğeleriyle milimetrik olarak dikey eksende hizalandı (`HorizontalAlignment="Left"`).
- **Simge ve Metin Parlaklık Senkronizasyonu:**
  - NavItem şablonunda simge (`ui:SymbolIcon`) ve metin (`TextBlock`) ön plan renkleri doğrudan butonun `Foreground` özelliğine bağlandı; hover ve seçim durumlarında senkronize parlama sağlandı.
  - Ayarlar öğesinin sağ-alt marjin farkı (`0,0,0,2`) düzeltilerek tüm menüyle görsel ritim eşitlendi.
- **Seçili Öğe Kararma Hatasının Giderilmesi:**
  - Seçili bir menü öğesinin üzerine gelindiğinde rengin koyulaşması hatası, `IsSelected` + `IsMouseOver` MultiTrigger'ına `Card.Fill` (buzlu cam parlaması) atanarak çözüldü.

---

### 4. Bütün Sayfalarda %100 Cam Dönüşümü
- **Tüm Modüller Cam Katmanlarıyla Donatıldı:**
  - **Temizleyici (Cleaner):** Sayaç kartı, durum rozeti ve detay paneli cam tokens (`Card.Fill`, `Card.Stroke.Glass`).
  - **Etkinlik Merkezi (ActivityCenter):** Zaman çizelgesi listesi ve detay kartı.
  - **Analizör (Analyzer):** Sekmeler, seçenekler araç çubuğu, sonuç kartları, kategori rozetleri ve API anahtarı modali.
  - **Kaldırıcı (Uninstaller):** İstatistik kartı, kaldırıcı listesi ve detay çekmecesi.
  - **Mağaza (Store):** AIO Runtimes kahraman banner'ı, paket kartları ve canlı kuyruk / konsol çekmecesi.
  - **Çökme Analizörü (CrashAnalyzer):** Seviye rozetleri ve alt durum çubuğu.
  - **Gizlilik & Debloat (PrivacyDebloat):** Bilgilendirme kartı, ayar kartları, bloatware listesi ve durum çubuğu.
  - **Optimizatör (Optimizer):** CPU ve durum rozetleri.
  - **Başlangıç Yöneticisi (Startup):** Sağ teftiş çekmecesi ve simge kutuları.
  - **Windows Tweaker:** Kategori rozetleri ve ayar kartları.
  - **Sentinel (Kurulum Nöbetçisi):** Geçmiş listesi cam çerçevesi.
  - **Hizmet Yöneticisi (ServiceManager):** Hizmetler ve sürücüler liste arka planları.
  - **Ağ Panelleri (Network):** Bağlantılar listesi, teftiş çekmecesi, DNS hız kıyaslama ve dinleme portları.

---

### 5. Sıkı Doğrulama, Sıfır Tasarım Borcu & %100 Test Başarısı
- **Sıfır Legacy Token:** Kod tabanındaki tüm eski `CardBackgroundFillColorDefaultBrush` referansları temizlendi.
- **4 Gatekeeper Tam Başarı:** `verify-tokens.py`, `verify-symbols.py`, `verify-design-debt.py` ve `verify-bindings.py` hatasız geçti.
- **783 Birim Test Yeşil:** Tüm çekirdek iş mantığı, WPF arayüz testleri ve duman testleri 0 hata ile tamamlandı.
