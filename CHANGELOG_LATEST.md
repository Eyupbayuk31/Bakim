• **Analizör — Yeniden Tasarlanmış Dosya Röntgeni**:
  - "Kalıcılık & Autoruns" modülünün adı **Analizör** oldu; sayfa baştan aşağı tasarım sistemine taşındı (bölüm başlığı, KPI kartları, boş durum yüzeyi).
  - Tehdit analizi penceresi tamamen yeniden yazıldı: Windows 11 Mica arka planı, dört sekmeli teftiş arayüzü ve taşmayan eylem çubuğu.
  - **Sekme 1 — Genel Bakış**: risk halkası, sertifika/VirusTotal/entropi/mimari kartları, Mark-of-the-Web kaynağı ve gerekçeli tespit listesi.
  - **Sekme 2 — PE & Kalkanlar**: mimari, alt sistem, derleme zamanı, giriş noktası; ASLR / DEP / CFG / High-Entropy VA / SafeSEH kalkan matrisi ve güvenlik notu; bölüm tablosu ile bölüm başına Shannon entropisi (packer uyarısı dahil).
  - **Sekme 3 — API Çağrıları**: içe aktarılan DLL'ler ve şüpheli Win32 fonksiyonları; bellek enjeksiyonu, klavye dinleme, C2 iletişimi ve kalıcılık kategorileri ayrı simgelerle işaretlenir.
  - **Sekme 4 — Kimlik & Hash**: SHA-256, MD5, SHA-1 ve ImpHash değerleri tek tıkla kopyalanır; imza kaynağı ve sertifika bitişi ayrıntılı gösterilir.

• **Analizör Listesi — Risk Odaklı Sıralama ve Zaman Çizelgesi**:
  - Girdiler artık risk skoruna göre sıralanır: en tehlikeli kayıt her zaman listenin en üstündedir.
  - **Son 7 günde eklenen** kalıcılık girdileri ayrı bir kartta sayılır — taze başlangıç kaydı, bulaşmanın en erken sinyallerinden biridir.
  - Tüm görünen girdileri tek seferde PE ve imza motorundan geçiren **toplu derin analiz** eklendi.

• **Kritik Doğruluk Düzeltmeleri**:
  - **Katalog imzaları artık görülüyor**: Windows sistem dosyalarının büyük kısmı gömülü imza yerine `.cat` kataloglarıyla imzalıdır. Önceki sürüm bunları "İmzasız" sanıp `notepad.exe`, `cmd.exe` gibi meşru bileşenlere risk puanı yazıyordu.
  - **Exploit kalkanları artık doğru okunuyor**: PE başlığındaki sürüm bloğu hatalı uzunlukla atlandığı için ASLR, DEP ve CFG bayrakları çöp veriden okunuyordu; `notepad.exe` bile "0/5 kalkan" görünüyordu. Şimdi 4/5 olarak doğru raporlanıyor.
  - **Tweaker kategori geçişindeki çökme giderildi**: alt menü ilk kez açılırken oluşan iç döngü hatası ("ValueFactory attempted to access the Value property") tamamen ortadan kaldırıldı.
  - Tarama sürerken devre dışı kalması gereken düğmeler artık gerçekten kilitleniyor.

• **Güvenlik Sertleştirmesi**:
  - Dosya analizi güvenilmeyen girdi okur; bozuk veya kasıtlı hatalı hazırlanmış dosyalara karşı bölüm sayısı, akış sınırı ve dosya boyutu denetimleri eklendi.
  - Süresi dolmuş imzalama sertifikaları artık ayrıca bildiriliyor (zaman damgalı imzalarda risk puanı yazılmadan).

• **Arayüz Tutarlılığı**:
  - Analiz penceresindeki tüm sabit renk kodları kaldırıldı; renkler dört temanın da fırçalarından gelir ve tema değiştirince anında uyum sağlar.
  - Risk ve kategori rozetleri artık anlamı yalnızca renkle taşımıyor: her seviyenin kendi simgesi var (renk körü kullanıcılar için erişilebilirlik).
  - Emoji butonlar Fluent simge sistemiyle değiştirildi; ekran okuyucu etiketleri tamamlandı.
