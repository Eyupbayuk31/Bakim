# Bakım v3.17.6 - Sürüm Notları

## Mağaza UI/UX Devrimi: Kompakt Metrik Şeridi, Aktif Kategori Vurgusu & Ferah Kart Alanı 💎⚡

### 1. Tek Satır Entegre Gezinti & Metrik Şeridi (Vertical Space Reclaim)
- **120+ Piksel Dikey Alan Tasarrufu:** Sayfa dikey alanının yaklaşık %40'ını tüketen ve kartları ekran dışına iten 4 devasa KPI kutucuğu (~110px) tamamen kaldırıldı.
- **Entegre Üst Şerit:** Sol tarafta ferah sekme değiştirici (`📦 Uygulama Kataloğu` / `⚡ Hazır Paketler`), sağ tarafta ise 4 adet şık ve kompakt telemetri rozeti (`Katalog: 45`, `Kurulu: X`, `Eksik: Y`, `Seçili: Z`) tek bir satırda kusursuzca harmanlandı.

### 2. Çift Buton Karmaşasının & Mantıksız Gezintinin Kaldırılması
- **Mükerrer Buton Temizliği:** Arama ve filtreleme araç çubuğunda yer alan ve üst sekmeyle çelişen "⚡ Hazır Paketler" butonu kaldırıldı.
- **Odaklanmış Arama ve Seçim Araçları:** Arama çubuğu satırı artık yalnızca hızlı arama kutusuna ve toplu seçim aksiyonlarına (`Görünenleri Seç`, `Seçimi Temizle`) odaklandı.

### 3. Dinamik Aktif Kategori Vurgusu (Visual Feedback)
- **Akıllı Kategori Durumu:** Kategori hap butonları (`Tümü`, `Runtimes`, `Oyun & İstemciler`, `Müzik & Medya`, `Yazılım`, `Tarayıcılar`, `İletişim`, `Geliştirici`) `StringToNavAppearanceConverter` ile bağlandı.
- **Fluent Primary Glow:** Seçili olan aktif kategori Windows 11 Fluent Primary vurgu rengi ve belirgin kontrastla parlayarak kullanıcının anlık nerede olduğunu kristal netliğinde gösterir.

### 4. Ferah Uygulama Kartı Alanı & Pürüzsüz Kaydırma
- **Sıfır Kart Kesilmesi:** Konsol çekmecesi açıldığında dahi uygulama kartlarının rahatça görülebilmesi için çalışma alanı genişletildi ve `ScrollViewer` düzeni optimize edildi.
- **Kesintisiz Fare Tekerleği Deneyimi:** Donanım ivmeli akıcı kaydırma ve `PreviewMouseWheel` ile menüler arası pürüzsüz geçiş sağlandı.

### 5. Sıfır Sertifika & Engelsiz Otonom Güncelleme & Yerel SDK Doğrulaması
- GitHub Releases üzerinden doğrudan HTTPS indirme ve sıfır imza sürtünmesiyle otonom kurulum standardı korunmuştur.
- Tüm XAML sembolleri (`verify-symbols.py`), renk tokenları (`verify-tokens.py`) ve Release derlemesi yerel .NET 10 SDK ile %100 sıfır hata/uyarı ile doğrulanmıştır.
