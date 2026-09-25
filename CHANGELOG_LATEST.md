# Bakım v4.1.0 - Sürüm Notları

## Bakım 4.1: Yeni Nesil Fluent 2 UI & Slate Dark Tasarım Revizyonu, İnteraktif KPI ve Segmentli Kontroller

Bakım v4.1.0 sürümü, uygulamanın tüm görsel arayüzünü modern Microsoft Windows 11 Fluent 2 tasarım dili ve Slate Dark (`#0F172A` / `#1E293B`) estetiği ile baştan aşağı yenilemektedir. Dağınık butonlar, uyumsuz başlık çubuğu öğeleri ve eski stil tab anahtarları tek bir tutarlı tasarım sistemi altında toplanmıştır.

---

### 1. Yeni Nesil Başlık Çubuğu & Birleşik Sistem Sağlığı
- **Spotlight Arama Hapı (Ctrl+K):** Dağınık arama kutusu yerine başlık çubuğunun merkezine oturan, klavye kısayolu etiketli modern Fluent Spotlight arama hapı entegre edildi.
- **Birleşik Sistem Sağlığı & Yetki Hapı:** CPU yükü, RAM tüketimi ve UAC (Yönetici / Standart Kullanıcı) yetki durumu ayrık 3 dağınık etiket yerine tek bir şık sağlık hapı içinde birleştirildi.
- **Kompakt Hızlı Aksiyonlar:** Oyun Modu anahtarı, tema değiştirici ve kullanıcı profili kompakt ikon haplarına dönüştürülerek pencere kontrolleri için ferah bir alan açıldı.

---

### 2. Fluent 2 Kenar Çubuğu Sol Vurgu Göstergesi (Navigation Accent Pill)
- **Dikey Accent Vurgu Hapı:** Kenar çubuğu menü öğelerine (NavSidebar) Windows 11 Ayarlar uygulamasındaki gibi dikey yuvarlatılmış 3px accent vurgu hapı eklendi.
- **Akıcı Seçim Hissi:** Aktif sayfa değiştiğinde sol hap yumuşak geçişle belirir, aktif olmayan öğeler sade ve minimalist kalarak göz yormaz.

---

### 3. SegmentedControl Tasarım Sistemi (Themes/Tokens/SegmentedControl.xaml)
- **Yeniden Kullanılabilir Tasarım Bileşeni:** `FluentSegmentedContainer` ve `FluentSegmentedTabItem` stilleri geliştirilerek merkezi tema kütüphanesine eklendi.
- **Tüm Alt Sekmelerde Tutarlılık:** 
  - *Sistem Bilgisi* (Donanım / S.M.A.R.T.),
  - *Ağ & Bağlantı Merkezi* (5 sekmeli anahtarlayıcı),
  - *Gizlilik & Debloat* (Gizlilik & Telemetri / Bloatware Kaldırıcı),
  - *Çökme & Mavi Ekran Analizörü* (BSOD / Olaylar / Onarım),
  - *Sistem Temizliği* (Filtre ve önayar sekmeleri)
  tümü bu modern kapsayıcıya geçirildi.
- **Çökme Analizörü Aktif Sekme Onarımı:** Çökme Analizörü'nde aktif sekmenin görsel olarak vurgulanmaması sorunu `StringToNavAppearanceConverter` ile giderildi.

---

### 4. İnteraktif KPI Kartları (StatCard Overhaul)
- **Mikro Yükselme & Kenarlık Işıltısı:** `StatCard` bileşeni fare üzerine gelindiğinde -2px dikey yükselme (`CubicEaseOut`) ve yumuşak accent kenarlık parlamasıyla etkileşimli hale getirildi.
- **Program Kaldırıcı Akıllı Filtreleme:** Kaldırılabilir Programlar ve Sistem Bileşenleri KPI kartları `IsInteractive="True"` ve `IsSelected` desteğiyle donatılarak tıklanabilir akıllı filtrelere dönüştürüldü.
- **Sistem Çapında Standartlaşma:** Başlangıç Programları, Bellek Optimizasyonu (RAM mimari kartları), Gizlilik & Debloat ve Temizleyici modüllerindeki ad-hoc kutucuklar `StatCard` ile standartlaştırıldı.

---

### 5. Sıfır AI Hissiyatı & Titiz Tipografi
- **Kod Tabanı Temizliği:** Ağ İzleyici modülündeki 100 satıra yakın eski buton şablonları tamamen temizlendi.
- **Sıfır Geçersiz Sembol:** Tüm semboller ve renk token'ları `verify-symbols.py` ve `verify-tokens.py` ile %100 doğrulandı; 725+ birim test ve 19 UI duman testi eksiksiz geçti.
