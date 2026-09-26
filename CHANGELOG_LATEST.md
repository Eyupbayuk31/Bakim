# Bakım v4.2.0 - Sürüm Notları

## Bakım 4.2: Yeni Nesil Oyun Modu, Windows 11 Fluent Ayarlar Mimarisi & Akıllı Oyun Kütüphanesi

Bakım v4.2.0 sürümü, Oyun Modu ve Ayarlar modüllerini Microsoft Windows 11 Fluent 2 standartlarına ve resmi tasarım ilkelerine göre baştan inşa etmektedir. Dağınık, geniş ve göz yoran listeler yerine kart bazlı eylem planı (SettingsCard), akıllı oyun kütüphanesi tarayıcısı ve yeniden kullanılabilir modern kontrol seti sunulmuştur.

---

### 1. Windows 11 Fluent 2 Oyun Modu Mimarisi
- **Yenilenen Sayfa Hiyerarşisi:** 1500 px boyunca uzanan dağınık satırlar yerine sol-sağ dengesi kurulmuş, gözü yormayan modern Fluent kartlar (`ui:CardControl`) ve eylem alanları konumlandırıldı.
- **Kompakt Eylem Planı (StepList):** Oyun Modu açıldığında devreye giren adımlar (güç planı, bellek temizleme, servisler, pencereli uygulamalar) zarif aşama göstergeleriyle sıralandı.
- **Etkileşimli Çip Düzenleyicisi (ChipListEditor):** Askıya alınacak arka plan uygulamaları ve kapatılacak süreçler tıklanabilir, dinamik çip listesiyle düzenlenebilir hale getirildi.

---

### 2. Akıllı Oyun Kütüphanesi Entegrasyonu (GameLibraryService)
- **Çoklu Platform Oyun Tespiti:** Sistemde yüklü Steam (`libraryfolders.vdf`, `appmanifest_*.acf`), Epic Games Launcher ve Xbox Game Pass oyunları otomatik olarak algılanır.
- **Tek Tıkla Otomatik Tetikleme:** Algılanan oyunlar listelenir ve kullanıcı tek tıkla dilediği oyunu otomatik Oyun Modu tetikleyicisi olarak ekleyebilir.

---

### 3. Ayarlar Modülü Tam Revizyonu (Fluent Settings)
- **Windows 11 Ayarlar Tasarımı:** Tüm ayar sekmeleri (`Genel & Görünüm`, `Sistem & Başlangıç`, `Modül Tercihleri`, `Depolama`, `Otomatik Güncelleme`, `Sürüm Geçmişi`, `Hakkında`) Windows 11 Ayarlar uygulamasının birebir Fluent 2 kart düzenine kavuşturuldu.
- **Tutarlı Tipografi ve İkonografi:** Menü başlıkları `Body Strong`, ayar kartları `ui:CardControl`, kompakt `ToggleSwitch` ve anlamsal simgelerle standartlaştırıldı.

---

### 4. Windows 11 Standart Renk Paleti (Themes/Tokens/Palette.Bootstrap.xaml)
- **Slate Dark & Mica Derinliği:** Yüzey renkleri, kenarlık vurguları (stroke) ve kart arka planları Microsoft Fluent 2 renk tokenlarına (`#0F172A` / `#1E293B`) tam uyarlandı.
- **Yüksek Kontrast & Erişilebilirlik:** Metin hiyerarşisi ve simge okunurluğu artırıldı.

---

### 5. Yeni Yeniden Kullanılabilir Fluent Bileşenleri
- **`KeyCap`:** Klavye kısayollarını (Ctrl+Shift+G vb.) Windows 11 tarzında gösteren donanım görünümlü tuş başlığı bileşeni.
- **`StepList`:** Süreç ve optimizasyon adımlarını görselleştiren aşama bileşeni.
- **`PickListDialog`:** Uygulama ve süreç seçimlerinde kullanılan arama filtreli modern diyalog penceresi.
