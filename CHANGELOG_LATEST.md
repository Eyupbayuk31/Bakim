# Bakım v3.17.4 - Sürüm Notları

## Performans İyileştirmesi (CA2024) & Sıfır Uyarı Derleme Hattı 🚀⚡

- **Asenkron Akış ve Thread İyileştirmesi (CA2024 Düzeltmesi):**
  - `StoreService.cs` içerisindeki WinGet ve süreç çıktılarını okuyan asenkron döngüde `process.StandardOutput.EndOfStream` kontrolünün olası senkron kilitlenmelere (deadlock) yol açmasını önlemek adına, standart `while ((line = await process.StandardOutput.ReadLineAsync(ct)) != null)` desenine geçildi.
  - Derleyici analiz uyarısı CA2024 tamamen giderildi; arka plan indirme ve konsol çıktı akışı 100% non-blocking hale getirildi.

- **Sıfır Çakışma & Kusursuz Tip İzolasyonu:**
  - `StoreViewModel.cs` içerisindeki `System.Windows.MessageBox` çağrıları tam isim alanı ile izole edildi; hiçbir ambigous reference (CS0104) hatası kalmadı.

- **Tamamlanan Mağaza Çift Sekmeli Arayüzü:**
  - Uygulama Kataloğu ve Hazır Paketler & Format Kurtarıcı sekmeleri zengin kartları ve canlı kurulu durum sayaçlarıyla 100% kararlı çalışmaktadır.
