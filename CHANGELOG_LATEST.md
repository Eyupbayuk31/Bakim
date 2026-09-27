# Bakım v4.17.0 - Sürüm Notları

## Tasarım: Disk Haritası, Etkinlik Merkezi, Analizör Geçmişi ve Kurulum Nöbetçisi

### Disk haritası
- **İç içe harita:** Büyük kutuların içinde kendi alt klasörleri de çiziliyor. Hangi klasörün neden şiştiği, içine girmeden görülüyor (örneğin Android → Sdk, Programs → VS Code).
- **Kutular:**
  - Her kutunun üstünde renkli bir şerit var; ad, boyut ve yüzde yazıyor.
  - Birleştirilmiş öğeler ("Bu klasördeki dosyalar") nötr renkte, klasörlerle karışmıyor.
  - Üzerine gelince ve klavyeyle odaklanınca kutu vurgulanıyor.
- **Özet şeridi:** Geçerli klasörün boyutu, dosya sayısı, alt klasör sayısı ve en büyük öğesi.
- **"En büyükler" paneli:** Sağda oran çubuklu sıralı liste. Tıklayınca o klasörün içine giriliyor.
- **Yol çubuğu:** Klasör simgeleri ve oklarla daha okunaklı.

### Etkinlik Merkezi
- **Özet şeridi hızlı filtre:** Toplam işlem, bugün, geri alınabilir ve başarısız sayıları gösteriliyor. Karta tıklayınca o filtre uygulanıyor.
- **Kart liste:** Her etkinlik ayrı bir kart. Solda türüne göre simge kutusu; başlık, saat ve modül, özet; sağda durum rozetleri ve "Geri al".
- **Ayrıntı paneli:**
  - Simgeli başlık ve bilgi tablosu (tarih, modül, tür).
  - Etkilenen her öğe ayrı iç kartta; yollar eş aralıklı yazı tipinde.
  - Boş durumdaki kart içinde kart görünümü düzeltildi.

### Analizör Geçmişi
- **Filtre çipleri:** Dağınık radyo düğmeleri ve onay kutularının yerine Fluent 2 filtre çipleri geldi: Tarih (tek seçim), Risk (çoklu), VirusTotal tespiti, Karar verilmemiş.
- **Kart liste:** Seçili öğe artık baştan sona mora boyanmıyor. Vurgu kenarı ve soldaki gösterge çizgisiyle belirtiliyor.
- **Ayrıntı paneli:**
  - Nedenler iç kartta. Puana etkisi olmayan nedenlerde "+0" yazmıyor.
  - İmza, VirusTotal ve SHA-256 tek bir kanıt tablosunda.
  - İşlem düğmeleri panelin altında sabit ve artık alt satıra taşmıyor. "Konumu aç", "Yok say", "Kararı kaldır" ve "Geçmişten kaldır" "…" menüsünde.
- **Düzeltme:** Geçmiş listesi bazı durumlarda satırları iki kez gösterebiliyordu ("34 / 17 analiz"). Liste yenileme artık iç içe çalışamıyor.

### Kurulum Nöbetçisi
- **Kart liste:** Kurulum geçmişindeki her kayıt kenarlı ayrı bir kart; grup başlıkları kartlarla hizalı.

### Ortak tasarım dili
- **Yeni ortak stiller:** `ListItem.Card` (liste kartı) ve `Chip.Filter` (filtre çipi) üç sayfada aynı görünümü sağlıyor.
- **Klavye erişimi:** Liste kartları ve filtre çipleri klavyeyle odaklanınca görünür bir odak halkası gösteriyor.

### Test altyapısı
- **Tema testleri artık yeşil:** Aylardır başarısız görünen 5 tema testinin sebebi bulundu. Mica için pencere zemini bilerek yarı saydam, ama test alfayı da karşılaştırıyordu. Artık yalnız RGB karşılaştırılıyor; XAML duman testi ilk kez tamamen yeşil.
- **Ekran görüntüleri:** Test düzeneği artık Bakım'ın gerçek açılışını tetiklemiyor; açık Bakım'a mesaj gitmiyor ve istenen sayfa ezilmiyor. Zemin opak çiziliyor. Disk haritası ve Analizör Geçmişi örnek verilerle çekilebiliyor.
- **Kapsam:** Etkinlik Merkezi, Depolama panelleri, Analizör Geçmişi/Değişiklikler, Oyun Modu ve Windows Araçları sayfaları da duman testinde yükleniyor.

**Testler:** Tüm birim testleri yeşil, 4 denetim betiği tam geçti, XAML duman testi tamamen yeşil.
