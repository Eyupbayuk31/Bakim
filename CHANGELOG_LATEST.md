# Bakım v4.4.0 - Sürüm Notları

## Fluent v2 Yerleşim Sistemi, Birleşik Sayfa Kabuğu (ModulePage) & Canlı Tema Önizleme

Bakım v4.4.0 sürümü; tüm uygulama sayfalarını birleşik bir Fluent v2 yerleşim sistemine (`ModulePage`) kavuşturmakta, Kontrol Paneli'ni referans mimariyle yeniden dizayn etmekte, Windows 11 Kişiselleştirme tarzı canlı tema seçim kutucuklarını entegre etmekte ve otomatik ekran görüntüsü doğrulama hattını devreye almaktadır.

---

### 1. Fluent v2 Yerleşim Mimarisi (Layout Controls)
- **Yeni Yerleşim Bileşenleri (`Controls/Layout/`):**
  - `AdaptiveGrid`: Pencere boyutuna ve ekran çözünürlüğüne göre sütun sayılarını otomatik hesaplayan akıcı ızgara.
  - `CardHeader`: Başlık, açıklama ve durum gliflerini standart Windows 11 hiyerarşisinde sunan kart başlığı.
  - `ListCard`: Liste görünümündeki öğeler için sınır ve dolgu standartlarını belirleyen liste kartı.
  - `MetricTile`: Donanım ve sistem ölçümlerini net sayı ve trend göstergeleriyle özetleyen metrik kutusu.
  - `ModulePage`: Sayfa başlığı, birincil eylemler, alt seçim çubuğu ve kaydırma alanını standartlaştıran birleşik sayfa kabuğu.
  - `Section`: Mantıksal bölümleri başlık ve boşluklarıyla gruplayan bölüm yapısı.
  - `SelectionBar`: Windows 11 Fluent alt seçim çubuğu kontrolü.
  - `StatusGlyph`: Sistem durumu ve önem derecesini nötr ve zarif gliflerle belirten durum simgesi.
  - `ThemePreview`: Canlı tema ve renk önizleme bileşeni.

---

### 2. Birleşik Sayfa Kabuğu (`ModulePage`) Dönüşümü
- **Tüm Sayfalarda Standart Düzen:**
  - **Kontrol Paneli (Dashboard):** Referans sayfa olarak sıfırdan dizayn edildi; CPU, RAM ve Disk ölçümleri `MetricTile` ve donmuş grafik noktalarıyla yeniden kurgulandı.
  - **Temizleyici, Başlangıç & Sistem Bilgisi:** Kart içinde kart kalabalığı arındırıldı; `ModulePage` kabuğunda duyarlı ızgaraya geçirildi.
  - **Ağ İzleyici, Mağaza & Kaldırıcı:** Alt seçim çubuğu (`SelectionBar`) ve duyarlı kutucuk ızgarasıyla zenginleştirildi.
  - **Çökme Analizcisi:** Güvenilirlik kahramanı ve zaman çizelgesi yeni düzene adapte edildi.
  - **Windows Tweaker:** 12 kategori ayar kartı yeni layout mimarisine uyarlandı.

---

### 3. Windows 11 Canlı Tema Önizleme Kutucukları (`ThemePreview`)
- **Windows 11 Kişiselleştirme Deneyimi:** Ayarlar sayfasındaki tema butonları yerine; pencere tabanı, katman rengi, kart dolgusu ve vurgu rengini birebir yansıtan interaktif `ThemePreview` kutucukları getirildi.
- **Tek Vurgu ve Katman Derinliği:** Koyu ve açık mod renk kontrastları, yüzey derinlikleri ve sınır çizgileri WCAG ve Fluent standartlarına tam uyarlandı.

---

### 4. Otomatik Ekran Görüntüsü Hattı (Screenshots Pipeline)
- **UI Duman Testi Entegrasyonu (`Screenshots.cs`):** Tüm modüllerin ve temaların pencereleri izole ortamda ayağa kaldırılarak ekran görüntüleri test hattında yakalanır.
- **GitHub Actions İş Akışı (`screenshots.yml`):** Dağıtım ve PR süreçlerinde arayüzün piksel bazında bozulmadığını garanti altına alan otomatik CI hattı kuruldu.

---

### 5. Sıfır Hata Güvencesi & Test Başarısı
- **Sıkılaştırılmış Tasarım Cırcırları:** Çıplak `ui:Card`, durum dolgulu haplar ve eski sayfa başlığı sayaçları sıfır toleransla denetlendi; `verify-design-debt.py` ve `verify-tokens.py` kontrolleri %100 yeşil tamamlandı.
- **783 Birim Test & UI Duman Testi:** 462 çekirdek iş mantığı ve 321 WPF/UI testi olmak üzere 783 testin tamamı sıfır hatayla geçti.
