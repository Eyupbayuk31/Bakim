# Bakım v4.20.6 - Sürüm Notları

## Pencere saydamlığı ve Mica malzeme arka planı kararlı hale getirildi

Uygulamanın bazı durumlarda (sistem tepsisinden geri yüklenirken, simge durumundan büyütülürken, pil tasarrufu modunda veya Windows saydamlık efektleri kapalıyken) şeffaf veya içi görünür hayalet pencere olarak açılması sorunu tamamen giderildi.

### Yenilikler ve İyileştirmeler
- **Hayalet Pencere & Şeffaflık Çözümü:** Pencere başlangıç arka plan rengi ve Window Tint fırçası %82 taban opaklık ile güvene alındı. Windows DWM Mica efektini askıya alsa bile pencere asla şeffaf kalmaz.
- **Güvenli Katı Arka Plan Yedeklemesi:** Efektsiz (None) veya Yüksek Karşıtlık modlarında tüm cam katmanları %100 opak Fluent yüzey fırçalarıyla (WindowBackground, ContentLayer, CardBackground) beslenir.
- **Pencere Yaşam Döngüsü Senkronizasyonu:** Sistem tepsisinden geri yükleme (`IsVisibleChanged`) ve simge durumundan normale dönme (`StateChanged`) anlarında malzeme ve arka plan kancaları tetiklenerek DWM kompozisyon kaybı önlenir.
- **Ortam Işığı (Ambient Light) Canlı Senkronizasyonu:** Ayarlar sayfasından ortam ışığı açılıp kapatıldığında ana pencereye anında yansıtılır; radyal ışık yayılım yarıçapı içerik görünürlüğünü ve okunabilirliğini bozmayacak şekilde dengelendi.
- **Test ve Süreç Kararlılığı:** Arka plan dosya dinleyicisi ve tekil örnek (single instance) tüketim testlerindeki yarışma durumu (race condition) deterministik hale getirildi.
