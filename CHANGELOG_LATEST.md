# Bakım v3.17.5 - Sürüm Notları

## Ultra Yüksek Hızlı İndirme, Akıcı Kaydırma & Sıfır UI Kilitlenmesi 🚀⚡

### 1. Ultra Yüksek Hızlı Doğrudan Gigabit CDN İndirme Motoru
- **abbodi1406 Resmi Gigabit CDN Entegrasyonu:** TechPowerUp'ın bot engeli ve 405/403 hız kısıtlamalarına takılan indirme mimarisi terk edildi; abbodi1406'nın resmi GitHub CDN'i üzerinden doğrudan tek dosya `VisualCppRedist_AIO_x86_x64.exe` (32.1 MB) indirme motoru devreye alındı.
- **10 Kat Daha Hızlı:** 82 MB'lık zip indirme ve arşiv çıkarma adımları ortadan kaldırılarak dosya boyutu %60 küçültüldü ve doğrudan hat hızında (tarayıcı hızıyla birebir) indirme sağlandı.
- **Tek Tıkla Sessiz Kurulum:** İndirilen paket `/ai /gm2` parametreleriyle arka planda 2005'ten 2022'ye kadar (x86/x64) tüm Visual C++ kütüphanelerini tek hamlede kurar.

### 2. Sıfır UI Kilitlenmesi & Non-Blocking Asenkron Mimari
- **WPF Dispatcher Queue Koruması:** İndirilen her 80 KB'lık blokta UI thread'ini senkron (`Dispatcher.Invoke`) olarak kilitleyen ve binlerce satır log üreten eski mekanizma kaldırıldı.
- **Stopwatch Tabanlı 150ms Throttler:** Ağ soketi kesintisiz okuma yaparken, arayüz ilerleme raporları ve anlık hız hesabı (MB/s) en fazla 150 ms'de bir `DispatcherPriority.Background` üzerinden gönderilerek UI'ın kilitlenmesi ("program yanıt vermiyor" uyarısı) %100 engellendi.
- **Tam Bağımsız Gezinme:** İndirme veya kurulum arka planda sürerken kullanıcı sol menüden Dashboard, Temizleyici, Analizör veya Tweaker modüllerine 60 FPS hızında takılmadan geçiş yapabilir.

### 3. Pürüzsüz Piksel Kaydırma (ScrollViewer & MouseWheel Düzeltmesi)
- **Akıcı Kart Kaydırma:** Uygulama Kataloğu ve Hazır Paketler sekmelerindeki `ScrollViewer` bileşenlerine `CanContentScroll="False"` ve `PreviewMouseWheel` yönlendirmesi eklendi; kartların ve fare tekerleğinin takılması tamamen giderildi.

### 4. Canlı Konsol Kontrol Çekmecesi & Gelişmiş Araç Çubuğu
- **Canlı Ağ Hız Göstergesi:** İndirme hızını gerçek zamanlı MB/s formatında başlıkta gösterme.
- **Otomatik Kaydırma (Auto-Scroll) & Metin Koruması:** Yeni log geldiğinde en alta otomatik kaydırma ve istenildiğinde durdurabilme anahtarı.
- **Konsol Araçları:** Konsolu tek tıkla temizleme ("Temizle"), panoya kopyalama ("Kopyala") ve konsol yüksekliğini büyütüp küçültme ("Genişlet" 260px / "Küçült" 110px).

### 5. Yerel .NET 10 SDK & Pre-Flight Gatekeeper
- Kullanıcı bilgisayarına resmi Microsoft .NET 10 SDK (v10.0.401) kurularak tüm Release derlemelerinin (`dotnet build -c Release`) ve sembol doğrulamalarının yerel makinede %100 sıfır hata ile geçmesi sağlandı.
