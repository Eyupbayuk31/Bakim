# Bakım v4.18.0 - Sürüm Notları

## Analizör v3 · Faz 5 tamamlandı: Kanıt Paneli, Toplu Eylemler, Sayfa İçi Analiz · MSI Derin Kalıntı Taraması

### Kanıt paneli (ana-ayrıntı)
- **Nasıl açılır:** Analizör'de bir satıra tıklayınca sağda kanıt paneli açılıyor.
- **İçeriği:**
  - Neden bu puan.
  - Ham komut ve çözümlenen hedef (yetim girdiler açıkça belirtiliyor).
  - Konum ve girdiyi ekleyen kurulum.
  - Yayıncı, imza, VirusTotal ve SHA-256.
  - İzin verilen eylemler; kilitliyse nedeni.
- **Eylemler:** Analiz et, zaman çizelgesi, VirusTotal, dosya konumu ve başlangıç kaydını silme.
- **Satır görünümü:** Panel açıkken satırlar sadeleşiyor; ad ve yol tam görünüyor.

### Toplu eylemler
- **Seçim:** Her satırda onay kutusu var; "Görünenlerin tümünü seç" filtredeki tüm girdileri seçiyor.
- **Seçilenleri kapat:**
  - Önce etkilenecek girdilerin önizlemesiyle onay isteniyor.
  - Run ve Başlangıç klasörü girdileri Etkinlik Merkezi'ne **tek bir geri alma kaydı** olarak yazılıyor.
  - Hizmet ve görevler her biri kendi kaydıyla geri alınabiliyor.
  - Kilitli (sürücü, kritik hizmet, Windows görevi) ve zaten kapalı girdiler atlanıyor; sayıları söyleniyor.
- **Karantinaya al:**
  - Seçilen girdilerin dosyaları karantinaya alınıyor. Doğrulanmış Microsoft dosyaları hariç; aynı dosyaya bağlı girdiler bir kez işleniyor.
  - Her dosya Etkinlik Merkezi'nden geri alınabiliyor.

### Sayfa içi analiz
- **Analizör içinde:** "Analiz et", "Dosya incele", sürükle-bırak ve Gezgin sağ tık analizleri artık ayrı pencere açmıyor. Tam dosya analizi Analizör sayfasının içinde açılıyor; "Listeye dön" ile listeye dönülüyor.
- **Diğer modüller:** Ağ İzleyicisi, Kaldırıcı ve Kurulum Nöbetçisi'nden gelen analizler pencerede açılmaya devam ediyor. İki yer aynı görünümü kullanıyor.

### Kaldırıcı: MSI derin kalıntı taraması
- **MSI kurulumlarında:** Windows Installer'ın bıraktığı bileşen, ürün, yükseltme kodu ve klasör kayıtları ile önbellekteki kurulum paketleri taranıyor.
- **Sağ tık menüsü kalıntıları:** Kaldırılan programın sağ tık menüsüne eklediği uzantılar da bulunuyor.
- **Otomatik temizlik:** Kesin kanıtlı öğelere ek olarak yüksek güvenli uygulama kalıntılarını da kapsıyor: AppData ve ProgramData altındaki uygulama klasörleri, kısayollar ve yazılım ayar anahtarları. Belgeler ve oyun kayıtları korunuyor.

**Testler:** Tüm birim testleri yeşil, 4 denetim betiği tam geçti, XAML duman testi tamamen yeşil.
