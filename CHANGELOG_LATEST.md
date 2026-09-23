# Bakım v3.17.7 - Sürüm Notları

## Mağaza Kartları Fluent 2 Tasarım Devrimi: Kategori Vurguları, Akıllı Seçim & Ayrık Eylem Barı 💎🎨

### 1. Dinamik Kategori Vurgulu İkon Squirclerı (Category Aesthetics)
- **45 Uygulama İçin Özel Renk Kodlaması:** Mağazadaki tüm uygulamaların aynı koyu camgöbeği/mavi kare içinde monoton ve ayırt edilemez görünmesi sorunu kökten çözüldü.
- **Kategori Paleti:**
  - 🌐 **Runtimes & Sistem:** Gökyüzü Mavisi / Cyan (`#38BDF8`)
  - 🎮 **Oyun & İstemciler:** Neon Mor / Indigo (`#A78BFA`)
  - 🎵 **Müzik & Medya:** Kehribar Sarısı / Gold (`#FBBF24`)
  - 🛠️ **Yazılım & Araçlar:** Zümrüt Yeşili / Mint (`#34D399`)
  - 🧭 **Web Tarayıcıları:** Canlı Mavi (`#60A5FA`)
  - 💬 **İletişim & Sosyal:** Mercan Pembe / Rose (`#FB7185`)
  - 💻 **Geliştirici & Kodlama:** Fuşya Mor (`#C084FC`)
- Her ikon kutusu kendi kategorisinin %15 saydam renk tonu, zarif dış çerçevesi ve keskin Fluent sembolüyle canlandırıldı.

### 2. Akıllı Kart Seçim Işıltısı (Visual Selection State)
- **Aktif Seçim Geri Bildirimi:** Sağ üstteki seçim kutucuğu (`CheckBox`) işaretlendiğinde kart çerçevesi parlak Fluent Accent (`#0078D4`) rengine bürünür ve kart zemini hafif vurgu tonuyla aydınlanır.
- **Korumalı Dokunmatik Alan:** Checkbox ögesi rastgele havada duran ham bir kontrol yerine kartın sağ üst köşesine sabitlenmiş şık yuvarlatılmış bir rozet içine alındı.

### 3. Sadeleştirilmiş Ayrık Alt Eylem Barı (Eliminated Clunky Buttons)
- **Hantal Blok Butonlara Son:** Kartın altını kaplayan ve ekranı devasa gri kalıplarla dolduran 100% genişlikteki kaba buton kaldırıldı.
- **Dengeli Ayrık Düzen:**
  - Sol tarafta indirme ikonu ve dosya boyutu hapı (`[ 💾 32.1 MB ]`).
  - Sağ tarafta ise şık, kompakt ve modern `[ ⬇ Yükle ]` (`Appearance="Primary"`, 30px) eylem butonu.

### 4. Mükerrer "Kurulu" Karmaşasına Son
- Sistemde halihazırda kurulu olan uygulamalarda hem "Kurulu" rozeti hem de altında ikinci bir devasa "Kurulu" butonunun yer alması kafa karışıklığını giderildi.
- Artık kurulu uygulamalar sağ altta zarif yeşil durum rozeti (`[ ✔ Kurulu ]`) ve hemen yanında tek tıkla onarım/yeniden kurulum yapmayı sağlayan kompakt bir simge butonu (`[ 🔄 ]`) olarak görüntülenir.

### 5. Kararlı Kart Yüksekliği & Akıcı Kaydırma
- Kart açıklamaları sabit satır yüksekliğine (`LineHeight="16"`, `Height="34"`) oturtularak farklı metin uzunluklarının kart hizalarını bozması engellendi; 3 ve 4 sütunlu ızgara düzeni kusursuz simetriye kavuştu.
- .NET 10 SDK ile Release derlemesi yerel ortamda %100 sıfır hata/uyarı ile test edildi.
