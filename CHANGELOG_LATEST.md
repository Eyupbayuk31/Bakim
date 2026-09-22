# Bakım v3.16.3 - Sürüm Notları

## Tüm Modüllerde Küçülen Pencere ve Araç Çubuğu Çakışmalarının Kökten Giderilmesi

- **Global Responsive WrapPanel Revizyonu:**
  - Pencere küçültüldüğünde veya yüksek DPI ölçeklemelerinde yatay filtre buton grupları ile arama/eylem kutularının üst üste binmesi ve metinlerin kesilmesi problemi projedeki tüm modüllerde kökten çözüldü.

- **Temizleyici Modülü (Cleaner):**
  - Kategori hapları (`Tümü`, `Sistem`, `Kullanıcı`, `Uygulama`, `Gelişmiş`) ve Hızlı Hazır Ayarlar (`Güvenli`, `Önerilen`, `Derin Temizlik`) `WrapPanel` yapısına geçirilerek dar pencerede esnekçe alt satıra kayması sağlandı.
  - Eski emojiler (`🛡️`, `🚀`, `🎮`) temizlenerek Windows 11 Fluent 2 sembolleriyle (`ShieldCheckmark20`, `TopSpeed20`, `Games20`) değiştirildi.
  - Taranan dosyalar listesi üstündeki arama kutusu ve boyut filtreleri `WrapPanel` ile duyarlı hale getirildi.

- **Hata Analizi Modülü (Crash Analyzer):**
  - Olay filtre butonları (`Tümü`, `Uygulama Çökmeleri`, `Mavi Ekran (BSOD)`, `Kritik Hatalar`, `Uyarılar`) ile Arama Kutusu ve `Günlükleri Temizle` butonu 2 satırlı esnek hiyerarşiye ayrıldı. Dar pencerelerde arama çubuğunun filtre butonlarının üzerine binmesi tamamen engellendi.

- **Başlangıç Yöneticisi (Startup):**
  - Başlangıç tipi filtre butonları (`Tümü`, `Kayıt Defteri`, `Klasör`, `Zamanlanmış Görev`) `WrapPanel` mimarisine geçirilerek arama kutusuna doğru taşma engellendi.

- **Ağ İzleyici (Network Monitor):**
  - Canlı bağlantılar sekmesindeki protokol ve durum filtre butonları (`Tümü`, `TCP`, `UDP`, `Dış Bağlantılar`, `Dinleme`, `Şüpheli / Riskli`) `WrapPanel` içine alınarak dar pencerelerde arama kutusunun üzerine basması önlendi.

- **Yazılım Kaldırıcı (Uninstaller):**
  - Katman 1 segment butonları (`Kaldırılabilir Yazılımlar`, `Korumalı Sistem`, `Tümü`) ve Katman 2 sıralama/toplu işlem araç çubukları tam responsive `WrapPanel` ile güçlendirildi.

- **Gizlilik & Bloatware (Privacy):**
  - Gizlilik kuralları kategori çipleri (`Tümü`, `Telemetri & Tanılama`, `Reklamlar & Öneriler`, `Konum & İzinler`) `WrapPanel` yapısına geçirilerek arama çubuğuyla çakışması önlendi.
