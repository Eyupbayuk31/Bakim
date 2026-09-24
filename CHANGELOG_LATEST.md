# Bakım v3.18.3 - Sürüm Notları

## Çift GPU Laptop Mimari Çözümü, 0 GB VRAM Koruması & Genel Bakış Senkronizasyonu

### 1. Çift GPU Laptop Keşif & Önceliklendirme Motoru
- **Hibrit Grafik Mimarisinde Otomatik Önceliklendirme:** Çift ekran kartı barındıran dizüstü bilgisayarlarda (AMD Radeon / NVIDIA harici grafik kartı + Intel HD Graphics / UHD / Iris entegre grafik yongası) sistemdeki tüm adaptörler DXGI 1.1, Kayıt Defteri ve WMI üzerinden eksiksiz taranır.
- **İlk Adaptörde Erken Kesilme Kusurunun Giderilmesi:** Önceki sürümlerde DXGI döngüsünün ilk bulunan adaptörde (0000 nolu dahili Intel çipi) erken sonlanmasına neden olan mantıksal kusur giderilerek, harici kartların (AMD Radeon, NVIDIA GeForce, Intel Arc) dedicated VRAM kapasitelerine göre puanlanıp birincil (Primary) GPU olarak atanması sağlandı.

### 2. Sıfır GB VRAM Kusurunun Kökten Çözümü
- **Standartlara Tam Uyumlu Bellek Biçimlendirmesi:** 128 MB DVMT belleğe sahip dahili yongaların veya küçük bellek bloklarının matematiksel olarak 0.1 GB'a ve ardından hatalı biçimde "0 GB VRAM" metnine yuvarlanması engellendi.
- **Akıllı Bellek Metni:** 512 MB altı veya entegre bellekler için "Paylaşımlı VRAM", harici kartlar için tam megabayt veya gigabayt ("2 GB VRAM", "8 GB VRAM") gösterimi garanti altına alındı.

### 3. Genel Bakış & Donanım Modülü Yüzde Yüz Veri Senkronizasyonu
- **Tek Doğruluk Kaynağı (Single Source of Truth):** `TelemetryService` ve `SystemInfoService` içindeki ayrışık ve mükerrer GPU arama mantığı kaldırılarak, her iki servis de `GpuInfoProvider.GetGpuConfiguration()` çekirdek motoruna bağlandı.
- **Sıfır Tutarsızlık:** "Genel Bakış" (Dashboard) ve "Donanım & Disk" (SystemInfo) sayfalarının aynı ekran kartını, aynı VRAM miktarını ve aynı sürücü sürümünü milisaniyesinde senkronize göstermesi güvenceye alındı.

### 4. Şık Çift GPU Rozet ve Zengin Bilgi Kartı
- **Fluent 2 Harici Rozeti:** Çift GPU tespit edilen sistemlerde Genel Bakış ekranındaki ekran kartı başlığının yanına "Harici" durum rozeti eklendi.
- **Zengin Açıklama Alanı (Tooltip):** Ekran kartı alanının üzerine gelindiğinde harici ve dahili kartların model adı, VRAM kapasitesi ve sürücü sürümlerini ayrı ayrı listeleyen bilgilendirici panel sunuldu.
- **Donanım Sayfası Genişletmesi:** Donanım ve Disk sekmesindeki GPU kartında dahili ekran kartının modeli ve paylaşımlı bellek durumu ek bir teknik satır olarak görüntülendi.

### 5. Kalite, Test ve Doğrulama
- **162/162 Birim Testi:** Eklenen 15 yeni GPU tespit ve VRAM biçimlendirme testi dahil olmak üzere tüm birim testleri yüzde yüz başarıyla geçti.
- **UI Smoke Testleri:** Tüm modül görünümleri, 4 tema, diyaloglar ve mağaza sekmeleri hatasız doğrulandı.
- **9.235 Sembol & Token Geçerli:** `verify-symbols.py` ve `verify-tokens.py` ile sıfır geçersiz sembol kanıtlandı.
- **Sıfır Uyarı / Sıfır Hata:** .NET 10 Release derlemesi temiz tamamlandı.
