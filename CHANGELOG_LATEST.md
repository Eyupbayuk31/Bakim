# Bakım v4.2.3 - Sürüm Notları

## Windows 11 FluentWindow (Mica) Diyalogları & Başlangıç/Olaylar Tek Kart Mimarisi

Bakım v4.2.3 sürümü; tüm diyalog pencerelerini yerel Windows 11 Mica zeminli FluentWindow standardına taşımakta, Başlangıç ve Olaylar sayfalarındaki dağınık KPI bloklarını tek odak durum kartında birleştirmektedir.

---

### 1. Yerel Windows 11 FluentWindow (Mica) Diyalogları
- **Sahte Kenarlıklar ve Gölgeler Kaldırıldı:** Kaldırıcı, güncelleme, artık temizliği, kurulum nöbetçisi fark inceleme ve hedef eylem diyalogları `AllowsTransparency` tabanlı hantal yapılardan yerel DWM ve Mica zeminli `ui:FluentWindow` mimarisine geçirildi.
- **Doğal Köşeler ve Başlık Çubuğu:** Standart Windows 11 pencere yuvarlamaları, sistem gölgeleri ve yerel başlık çubuğu butonları pürüzsüz çalışır.
- **Gereksiz Topmost İptal:** Diyalogların masaüstündeki diğer uygulamaları zorla kapatması (`Topmost`) engellendi; yalnızca pencereden seç modunda korundu.

---

### 2. Başlangıç Programları Tek Durum Kartı
- **Windows Ölçümlü Açılış Süresi:** 4 ayrık KPI kutusu yerine Windows Event Log (Olay 100/101) tabanlı açılış süresi, son açılışların sütun grafiği ve sade sayımlar tek bir odak kartında toplandı.
- **Kartsız Temiz Komut Satırı:** Filtre çubuğu ve eylemler zarif bir şeride taşındı; program isimleri ve rozetler göz yormayan doğal boyutlara uyarlandı.

---

### 3. Olaylar ve Güvenilirlik Zaman Çizelgesi
- **Birleşik Sağlık & Güvenilirlik Kartı:** Sistem sağlığı ve 30 günlük güvenilirlik eğilimi tek bir Fluent kartında birleştirildi.
- **SelectorBar Filtreleri:** Başlık eylemlerindeki dağınık sekmeler alt çizgili SelectorBar sekme düzenine kavuşturuldu.
