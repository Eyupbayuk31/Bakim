# Bakım v3.13.0 - Sürüm Notları

## ⚡ Bellek ve Süreçler (Optimizer V2.0) - Devrimsel Güncelleme

- **Çok Katmanlı Segmente Bellek Barı (Memory Breakdown Bar):** Windows 11 Görev Yöneticisi mimarisinde Kullanımda (Mavi), Değiştirilmiş (Mor), Bekleme Listesi/Önbellek (Yeşil) ve Boş RAM (Gri) olmak üzere 4 renkli dinamik oransal dağılım çubuğu eklendi.
- **4'lü Teknik Donanım KPI Kartı:** RAM çalışma hızı ve takılı yuvalar (`MT/s`), Sanal Bellek (Pagefile / Commit Charge), Çekirdek Bellek Havuzları (Paged & Non-Paged Pool) ve Donanıma Ayrılmış bellek metrikleri sisteme kazandırıldı.
- **Sysinternals RAMMap Temizleme Motorları:**
  - 🧹 **Bekleme Listesini Boşalt (Clear Standby List):** `NtSetSystemInformation` ile Windows önbelleğindeki eski sayfaları sıfırlar; oyunlardaki ani FPS düşüşlerini (drop) ve mikro takılmaları (stutter) yok eder.
  - 💾 **Sayfaları Diske Yaz (Flush Modified Pages):** Diske yazılmayı bekleyen değiştirilmiş sayfaları anında diske flush ederek fiziksel RAM'i boşaltır.
  - ⚡ **Derin Boşaltma (Purge All):** Çalışma kümeleri, değiştirilmiş sayfalar ve bekleme listesini tek tıkla boşaltır.
- **Orijinal Win32 İkonlu Süreç Tablosu:** Çalışan uygulamaların (Valorant, Discord, Chrome, Riot vb.) kendi yerel `.exe` simgeleri Win32 GDI API ile bellek sızıntısız şekilde arayüze aktarıldı. Anlık arama çubuğu, kategori hapları (`Tümü`, `Kullanıcı`, `Sistem`, `>500 MB`) ve süreç başına anlık CPU % tüketim ölçümü eklendi.
- **Sağ Teftiş Çekmecesi (Process Inspector):** Herhangi bir sürece tıklandığında açılan detaylı panel:
  - Çalıştırılabilir dosya yolu, panoya kopyalama ve tek tıkla "Dosya Konumunu Aç (Explorer)".
  - Yayıncı ve ürün açıklaması (Discord Inc., Riot Games, Microsoft Corporation vb.).
  - Uptime (Çalışma Süresi), Çalışma Kümesi (RAM) ve Özel Ayrılmış Bellek (Private Bytes) dökümü.
  - ⏸️ **İşlemi Dondur / Devam Et (Suspend / Resume):** `NtSuspendProcess` ile kilitlenen veya sistemi kasan süreçleri geçici olarak dondurma yeteneği.
  - 🛡️ **VirusTotal Güvenlik Analizi:** Sürecin SHA-256 özetini çıkarıp tek tıkla VirusTotal veritabanında doğrulama ve tarayıcıda rapor açma.
