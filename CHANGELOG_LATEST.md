# Bakım v3.16.0 - Sürüm Notları

## Büyük Dosya Analizörü Kapsamlı Yenilenmesi & Profesyonel Depolama Yönetimi

- **Kusursuz Responsive Araç Çubuğu (2 Satırlı Modern Düzen):**
  - Hedef Sürücü seçimi, eşik butonları (100 MB+, 500 MB+, 1 GB+, 2 GB+, 5 GB+), tür filtreleri, canlı arama kutusu ve sıralama menüsü 2 satırlı esnek Grid düzenine kavuşturuldu.
  - 1280x840 ve daha dar pencerelerde buton metinlerinin ("5 G...") ve başlığın ("Donanım, Depolama & S.M...") kırpılma sorunu tamamen giderildi.
  - Modül başlığı `Donanım & Depolama` olarak sadeleştirildi, alt sekme butonları kompakt hale getirildi.

- **Sürücü Depolama Röntgeni & Hero Boş Durum (Hero Empty State):**
  - Tarama öncesinde arayüzü kaplayan devasa karanlık boşluk kaldırıldı.
  - Seçili sürücünün toplam boyutu, kullanılan alanı, boş kapasitesi, doluluk yüzdesi ve görsel renkli kullanım çubuğu eklendi.
  - Kullanıcıya rehberlik eden 4 adet görsel kılavuz kartı (Videolar, Disk İmajları, Arşivler, Kurulum/Oyun Verileri) ve belirgin birincil tarama çağrısı entegre edildi.

- **Çoklu Seçim & Toplu İşlemler (Batch Operations):**
  - "Tümünü Seç" onay kutusu ve dinamik `Seçilen: X dosya (Y GB)` bilgi rozeti eklendi.
  - Seçilen tüm büyük dosyaları tek tıkla toplu olarak güvenle Geri Dönüşüm Kutusuna gönderme veya kalıcı olarak silme yetenekleri sisteme kazandırıldı.

- **Canlı Arama & Çift Yönlü Sıralama (Live Search & Multi-Sort):**
  - Dosya adı ve uzantıya (.iso, .mp4, .zip vb.) göre anlık filtreleme yapan arama kutusu eklendi.
  - Boyuta (büyükten/küçükten), tarihe (yeni/eski) ve isme (A-Z/Z-A) göre çok kriterli sıralama seçici entegre edildi.

- **Kategori Dağılım İstatistikleri:**
  - Taranan dosyaların türlerine göre (Videolar, Disk İmajları, Arşivler, Kurulumlar, Diğer) boyut ve oran dağılımı görselleştirildi.

- **Gelişmiş Tarama Kapasitesi:**
  - Taranan en büyük dosya listesi sınırı 100'den 250 dosyaya çıkarıldı.

- **Otomatik Test Güvencesi:**
  - Canlı arama, çoklu sıralama, toplu seçim ve kategori istatistiklerini doğrulayan yeni xUnit testleri entegre edildi (143/143 test başarılı).
