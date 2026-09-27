# Bakım v4.19.1 - Sürüm Notları

## Dışarıdan Kaldırma Algılama Düzeltmeleri

Bir program Windows Ayarlar'dan ya da kendi kaldırıcısıyla kaldırıldığında Bakım'ın "kalıntıları tara" önerisi göstermesi gerekiyordu. Canlı denemede açık Bakım bu kaldırmaları yakalamadı ve günlükte izlemenin çalıştığına dair hiçbir iz yoktu.

### Düzeltmeler
- **İzleme artık görünür:** İzlemenin başlaması, izlenen program sayısı ve her kayıt değişikliği ("+1 / -0") normal günlüğe yazılıyor. Eskiden bunlar yalnızca ayrıntılı günlükte görünüyordu.
- **İzleme kendini toparlıyor:** Tek bir tur hata verirse izleme artık durmuyor. Başlangıç okuması başarısız olursa 30 saniye sonra yeniden deneniyor.
- **Oyun sırasında kaldırma:** Oyun Modu açıkken yakalanan kaldırmanın bildirimi eskiden tamamen kayboluyordu. Artık oyun bitince gösteriliyor.

**Testler:** Tüm birim testleri yeşil. "Bakım açıkken kurulup sonra dışarıdan kaldırılan program" senaryosu için yeni uçtan uca test eklendi. 4 denetim betiği tam geçti.
