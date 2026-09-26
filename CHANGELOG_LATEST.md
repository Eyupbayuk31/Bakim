# Bakım v4.1.2 - Sürüm Notları

## Fluent UI İyileştirmeleri, Görünmeyen Açıklamalar & Başlıklar Onarımı ve Modern Kenar Çubuğu

Bakım v4.1.2 sürümü; sayfa/bölüm başlıklarında ve kart bileşenlerinde açıklamaların gizli kalmasına yol açan dönüştürücü hatasını gidermekte, bozuk glifleri standartlaştırarak sistem genelinde Windows 11 Fluent 2 NavigationView deneyimini mükemmelleştirmektedir.

---

### 1. Görünmeyen Sayfa Başlıkları ve Açıklamaları Onarımı (StringToVisConverter)
- **Parametresiz Kullanım Desteği:** `StringToVisConverter` dönüştürücüsünün parametresiz kullanımda her zaman `Collapsed` döndürmesi sorunu çözüldü ("dolu dize -> görünür" semantiği getirildi).
- **Açıklama ve Simgeler Geri Geldi:** 66 sayfa ve bölüm başlığının simgesi ve açıklaması, `StatCard` ve `MetricChip` değerleri/alt yazıları, `StatusBadge` biçim rozetleri ve komut paleti klavye kısayol hapları artık kusursuz görüntülenmektedir.
- **Kapsamlı Birim Testleri:** Dönüştürücü davranışını garantiye alan `StringToVisConverterTests` eklendi.

---

### 2. Fluent Sembol ve Glif Standardizasyonu
- **0xFFFF Üstü Gliflerin Onarımı:** WPF-UI ortamında bozuk/eksik çizilen (örneğin "Depolama" sayfası simgesi gibi) 0xFFFF üzeri semboller (`HardDrive24`, `ArrowRouting20`, `PlayCircleHint24`) BMP içi geçerli Fluent 2 eşlenikleriyle yenilendi.
- **Sembol Doğrulama Kapısı:** `verify-symbols.py` ve `SymbolValidator` araçları bu hatalı glifleri engelleyecek kural setiyle güçlendirildi.

---

### 3. Windows 11 NavigationView Uyumlu Kenar Çubuğu
- **Modern Menü Öğeleri:** Kenar çubuğu butonları (Nav.ItemButton) Windows 11 standartlarına uygun şeffaf zemin, hafif hover katmanı ve 3x16 px yuvarlatılmış sol accent vurgu hapı ile donatıldı.
- **Kaydırma Kenar Solması (`ScrollFade`):** Kenar çubuğunda kaydırma yapıldığında liste uçlarında zarif bir solma efekti sağlayan yardımcı bileşen eklendi.
- **Büyük Harfsiz Sade Başlıklar:** Menü grubu başlıkları Windows 11 Fluent tipografisine (`Body Strong`) uyarlandı.

---

### 4. Başlık Çubuğu & Arayüz Dili İnce Ayarları
- **Dengeli Başlık Çubuğu:** Tek birleşik arama alanı ve şeffaf hızlı butonlar oluşturuldu; Oyun Modu anahtarı aşırı dolgu vurgusu yerine durum noktası ve metinle dengelendi.
- **Abartısız Profesyonel Metinler:** Sayfa açıklamalarındaki jenerik ve abartılı ifadeler ayıklanarak sade ve güven veren Fluent 2 tasarım diline dönüştürüldü.
- **Linux CI Çapraz Platform Desteği:** `WindowsSandboxConfiguration` içinde Linux CI derleme ortamlarında yaşanan dosya ayracı uyumsuzluğu giderildi.
