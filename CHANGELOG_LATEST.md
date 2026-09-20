# Bakım v3.15.0 - Sürüm Notları

## Donanım, Depolama & S.M.A.R.T. Modülü Kapsamlı Görsel & Fonksiyonel Revizyonu

- **Modern Fluent 2.0 Segmented Alt Sekme Çubuğu:**
  - Sayfa üstündeki sade ve farksız butonlar kaldırıldı; aktif sekme durumunu pürüzsüz gösteren modern Fluent Segmented Control entegre edildi (`Donanım & Telemetri`, `S.M.A.R.T. Sağlık`, `Büyük Dosya Analizörü`).

- **Bozuk CPU Göstergesinin Giderilmesi & Zengin 6 KPI Kartı:**
  - CPU kartındaki bozuk, eksik görünen dairesel ilerleme halkası kaldırıldı; yerine yük durumuna göre dinamik renk alan (Yeşil / Sarı / Kırmızı) kristal netliğinde modern **CPU Yük Göstergesi Rozeti** (`%15 YÜK`) ve çekirdek/izlek dökümü getirildi.
  - Kartlar 6 ana bileşene genişletildi: İşlemci (CPU), Ekran Kartı (GPU & Monitör Çözünürlüğü/Hz), Fiziksel Bellek (RAM), Anakart & Bellenim (Firmware), Ağ Bağdaştırıcısı (LAN/Wi-Fi Hızı & Yerel IP), Cihaz & Uptime (Sistem Çalışma Süresi).

- **Etkileşimli Sürücü & Depolama Kartları:**
  - Tek satırlık pasif çubuk yerine zengin sürücü panelleri tasarlandı.
  - Kartlar üzerine doğrudan eylem butonları eklendi:
    - *Disk Temizleme* (Windows cleanmgr o sürücü için başlatılır)
    - *Büyük Dosyaları Tara* (Doğrudan analizör sekmesine o sürücü seçili olarak geçer ve taramayı başlatır)
    - *Gezginde Aç* (Sürücü kök dizinini açar)

- **Platform Teşhis & Donanım Güvenlik Paneli:**
  - Sayfa altındaki boş alan değerlendirildi; Secure Boot (Güvenli Önyükleme), TPM 2.0 Donanım Çipi, CPU Sanallaştırma (VT-x / AMD-V) ve UAC Yetki Düzeyi durumlarını anlık denetleyen 4 sütunlu teşhis paneli inşa edildi.

- **Tek Tıkla Donanım Raporu & Sistem Yönetim Araçları:**
  - Tek tıkla tüm donanım, sürücü ve güvenlik envanterini koyu temalı profesyonel bir HTML raporuna dönüştürme ve tarayıcıda açma imkanı getirildi.
  - Tüm sistem özetini temiz metin olarak panoya kopyalama ve Disk Yönetimi (`diskmgmt.msc`) ile Aygıt Yöneticisi (`devmgmt.msc`) açma kısayolları eklendi.

- **Gelişmiş S.M.A.R.T. Sağlık & Büyük Dosya Analizörü:**
  - Fiziksel disk kartlarına tek tıkla SSD hücre bloklarını optimize eden **TRIM Komutu Gönder** özelliği eklendi.
  - Büyük Dosyalar sekmesine boyut eşik hapları (`500MB+`, `1GB+`, `2GB+`, `5GB+`), anında filtreleyen dosya türü filtreleri (`Videolar`, `Disk İmajları`, `Arşivler`, `Kurulum / Oyun`), toplam kaplanan alan göstergesi ve **Geri Dönüşüm Kutusuna Taşı (Güvenli Silme)** özelliği eklendi.
