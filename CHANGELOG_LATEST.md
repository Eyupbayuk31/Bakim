# Bakım v4.20.5 - Sürüm Notları

## Sürüm geçmişi sayfalandı, çökme ve olay kayıtları silinebiliyor

Ayarlar'daki sürüm geçmişi v4.6.3'te kalmıştı ve sonsuza kadar uzuyordu. Olaylar ve çökmeler sayfasında ise eski kayıtlar hiçbir zaman kalkmıyordu.

### Yenilikler
- **Sürüm geçmişi güncel:** v4.7.0'dan v4.20.x'e kadar tüm sürümler listeye eklendi, en yeni sürüm en üstte ve "En son güncellemede ne geldi?" kartında görünüyor.
- **Sayfalama:** Sürüm geçmişi 8'erli sayfalara bölündü. Altta 1 2 3 ... sayfa düğmeleri ile önceki/sonraki okları var.
- **Mavi ekran kayıtlarını sil:** "Tümünü sil" ve kayıt başına "Sil" düğmeleri. Minidump dosyası diskten silinir, kayıt listeden ve sağlık puanından çıkar. Dosya silinemezse (izin gerekirse) kayıt listede kalır ve durum çubuğu bunu bildirir.
- **Olay listesini temizle:** Olaylar sekmesinde "Listeyi temizle". Windows olay günlüğü silinmez, yalnızca temizleme anından önceki olaylar listeden ve sağlık puanından çıkarılır. Seçim uygulama yeniden açılınca da korunur.
