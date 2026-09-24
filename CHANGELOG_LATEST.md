# Bakım v3.18.4 - Sürüm Notları

## Gelişmiş Yinelenen Dosya Avcısı (Duplicate File Finder) & Sahipsiz Boş Klasör Temizleyici (Empty Folder Purger)

### 1. 3 Aşamalı Ultra Hızlı Kriptografik Tarama Motoru
- **Aşama 1 (Boyut Eşleme):** Dosya sistemi derinlemesine taranarak aynı bayt boyutuna sahip dosyalar anında filtrelenir; tekil dosyalar elenir.
- **Aşama 2 (4 KB Başlık Hash'i):** Disk I/O yükünü en aza indirmek için dosyanın yalnızca ilk 4 kilobaytlık başlık bloğu okunarak hızlı SHA-256 ön elemesi yapılır.
- **Aşama 3 (Tam SHA-256 Parmak İzi):** Başlığı eşleşen aday dosyalar tam SHA-256 kriptografik hash doğrulamasına tabi tutulur. Farklı isimde veya farklı klasörlerde olsalar dahi birebir aynı içeriğe sahip dosyalar sıfır hata payıyla tespit edilir.

### 2. Sahipsiz Boş Klasör Temizleyici (Empty Folder Purger)
- **Derinlemesine Dizin Taraması:** Kaldırılmış yazılımlardan, eski kurulum paketlerinden ve taşınmış klasörlerden arta kalan 0 baytlık boş dizinler tespit edilir.
- **Güvenli Temizlik:** Sahipsiz boş klasörler sistemden arındırılarak dosya sistemi hiyerarşisi düzenlenir ve dizin dağınıklığı ortadan kaldırılır.

### 3. Savunmacı Mimari ve Sistem Güvenlik Kalkanı
- **Kritik Sistem Dizin Koruması:** `C:\Windows`, `ProgramData\Microsoft`, `System Volume Information`, `$Recycle.Bin`, `Recovery` ve kritik sistem dizinleri tarama ve silme kapsamından otomatik olarak muaf tutulur.
- **Windows Shell Geri Dönüşüm Kutusu Entegrasyonu:** Silme işlemi varsayılan olarak `SHFileOperation` (`FOF_ALLOWUNDO`) API'siyle Windows Geri Dönüşüm Kutusu'na taşınır; böylece kullanıcı dilediğinde dosyaları geri kurtarabilir. İsteğe bağlı kalıcı silme seçeneği sunulur.

### 4. Akıllı Seçim ve Gruplandırma Yetenekleri
- **Orijinal Dosya Koruması:** Her yinelenen dosya grubunda en eski oluşturulma tarihine sahip dosya "Orijinal" rozetiyle işaretlenir ve silinmeye karşı korunur; kopyalar otomatik olarak seçilir.
- **Tek Tıkla Seçim Aksiyonları:** "Kopyaları Otomatik Seç", "En Yenileri Seç", "En Eskileri Seç" ve "Seçimi Temizle" eylemleriyle binlerce dosya saniyeler içinde yönetilebilir.
- **Grup Bazında Boşa Harcanan Alan Analizi:** Her grupta tekil dosya boyutu, grup dosya sayısı, SHA-256 hash özeti ve boşa harcanan toplam disk alanı ayrıntılı olarak raporlanır.

### 5. Fluent 2 Modern Slate Dark Yönetim Arayüzü
- **Donanım & Depolama Modülü 4. Sekme Entegrasyonu:** Donanım ve Depolama sayfasına "Yinelenen & Boş Klasörler" adıyla 4. modern alt sekme kazandırıldı.
- **4 Canlı KPI Metrik Kartı:** Boşa harcanan toplam disk alanı, tespit edilen grup sayısı, sahipsiz boş klasör sayısı ve seçili temizlenecek alan canlı olarak hesaplanır.
- **Esnek Tür Filtreleri:** Resimler, Videolar, Belgeler, Arşivler ve Ses formatları için anında filtre çipleri.
- **Özel Klasör & Sürücü Seçimi:** İster tüm sabit sürücüler, ister tek bir sürücü, isterse masaüstü veya indirilenler gibi özel bir klasör taranabilir.

### 6. Kalite, Test ve Doğrulama
- **166/166 Birim Testi:** Eklenen 4 yeni yinelenen dosya ve boş klasör testi dahil tüm birim testleri yüzde yüz başarıyla geçti.
- **UI Smoke Testleri:** Tüm modüller, 4 tema, diyaloglar ve gezinmeler hatasız doğrulandı.
- **9.235 Sembol & Token Doğrulandı:** `verify-symbols.py` ve `verify-tokens.py` ile sıfır geçersiz sembol ve geçerli tasarım tokenları kanıtlandı.
- **Sıfır Uyarı / Sıfır Hata:** .NET 10 Release derlemesi temiz şekilde tamamlandı.
