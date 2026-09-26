# Bakım v4.5.0 - Sürüm Notları

## Windows 11 Cam (Mica + Glassmorphism) Malzeme Sistemi & Saydam Gezinme Bölmesi

Bakım v4.5.0 sürümü; Windows 11'in yerel tasarım felsefesini (Mica, Akrilik, katman ve cam malzeme modeli) uygulamanın tüm görsel omurgasına entegre etmekte; kenar çubuğu, başlık çubuğu ve içerik alanını tek bir uyumlu cam yüzeyin katmanlarına dönüştürmektedir.

---

### 1. Windows 11 Malzeme Modeli & Tonlu Mica Tabanı
- **Tek Yüzey Bütünlüğü:**
  - Kenar çubuğu ve başlık çubuğunun opak arka planları kaldırılarak tamamen saydam hale getirildi; altındaki yerel Windows 11 Mica tabanıyla doğrudan bütünleşmesi sağlandı.
  - Kök zemin için `Surface.WindowTint` token'ı tanımlandı. 6 temanın tamamında (MicaDark, SlateDark, AmoledBlack, CyberpunkPurple, HighContrast, FluentLight) pencere tabanına temanın kimliğini yansıtan yarı saydam bir örtü katmanı serildi.
- **Katmanlı Yüzey Hiyerarşisi:**
  - `Layer.Fill` ve `Layer.Stroke`: İçerik katmanı, pencere tabanından yumuşak yarı saydam bir örtü ve sol/üst 1 px ince cam kenarlıkla ayrıldı.
  - Sol üst köşesi yuvarlatılmış (`Radius.ContentLayer`, 8 px) içerik katmanı sayesinde Windows 11 Ayarlar uygulaması hissiyatı eksiksiz yakalandı.

---

### 2. Hafif Glassmorphism & Statik Ortam Işığı (Ambient Lighting)
- **Sıfır GPU Maliyeti ile Cam Derinliği:**
  - Arayüzü yavaşlatan ve GPU'yu tüketen yapay `BlurEffect` / `VisualBrush` hileleri yerine, WPF'in donmuş fırça (Frozen Brushes) mimarisiyle çalışan statik radyal degradeler kurgulandı.
  - İçerik katmanının sağ üst köşesine temanın vurgu renginde geniş bir ortam ışığı (`Surface.AmbientLight`), sol alt köşesine ise ikincil temanın yumuşak ışıltısı (`Surface.AmbientSecondary`) yerleştirildi.
  - Sayfa içerikleri kaydırılırken cam kartlar bu ışığın üzerinden geçerek gerçekçi bir derinlik ve saydamlık sunar.

---

### 3. Zarif Cam Kartlar & Kahraman Malzemesi (`Card.Surface.Hero`)
- **Cam Kenar Işığı (`Card.Stroke.Glass`):**
  - Standart kartlar (`Card.Surface`), yarı saydam dolgu (`Card.Fill`) ve yukarıdan aşağıya sönen degrade cam kenarlıkla donatıldı.
  - Kart içi ikincil bölgeler `Card.Fill.Secondary` ve `Divider.Stroke` ile yapılandırıldı.
- **Kahraman Kart Malzemesi (`Card.Surface.Hero`):**
  - Sayfa başı ana odak kartları için (Kontrol Paneli Sağlık Kartı, Oyun Modu Durum Kartı, Sistem Bilgisi Cihaz Kartı) vurgu rengi tonlu cam dolgu ve belirgin cam kenar ışığı uygulandı.

---

### 4. Saydam Kenar Çubuğu (Gezinme Bölmesi) & İnce Sis Seçimi
- **Doğal Windows 11 Seçim Katmanı:**
  - Kenar çubuğundaki hantal ve opak gri seçim blokları kaldırıldı.
  - Windows 11 standartlarına uygun, hafif beyaz sis (%6 opaklıkta `Subtle.Fill.Hover` ve `Subtle.Fill.Selected`) katmanı uygulandı.
  - Seçili öğe için sol kenarda 3×16 px yuvarlatılmış Fluent vurgu çubuğu (Indicator) konumlandırıldı.
  - Menü ayırıcıları `Divider.Stroke` yarı saydam çizgileriyle modernize edildi.

---

### 5. Başlık Çubuğu & Arama Alanı Fluent Entegrasyonu
- **Saydam Başlık Alanı:** Pencere başlık çubuğu Mica zeminine açıldı; yapay kutu sınırları temizlendi.
- **Hızlı Arama Alanı:** `Control.Fill` ve ışıklı alt kenarlık (`Control.Stroke.Elevation`) ile 420 px genişliğinde zarif bir arama çubuğuna dönüştürüldü.

---

### 6. Sıkı Doğrulama, Sıfır Tasarım Borcu & %100 Test Başarısı
- **177 Tasarım Token'ı:** `ThemeService` ve `Palette.Bootstrap.xaml` arasındaki 177 semantik anahtar %100 birebir senkronize edildi.
- **Sıfır Geçersiz Sembol & Tasarım Borcu:** Tüm XAML dosyalarında `SymbolRegular` sembolleri, renk token'ları ve cırcır (ratchet) limitleri doğrulandı.
- **783 Birim Test & UI Duman Testi:** 462 çekirdek iş mantığı, 321 UI/WPF testi ve 20 modül UI duman testinin tamamı başarıyla geçti.
