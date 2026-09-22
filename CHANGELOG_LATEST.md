# Bakım v3.16.1 - Sürüm Notları

## Donanım & Telemetri Yönetim Paneli Yenilenmesi ve Arayüz Düzeltmeleri

- **Çift Kartlı Modern Alt Panel Mimarisi (Dual-Card Responsive Layout):**
  - Donanım ve Telemetri sekmesinin en altındaki tek ve sıkışık kart yapısı, amacına göre 2 bağımsız modern karta ayrıldı:
    - **Sistem Raporu & Dışa Aktarım:** HTML Raporu oluşturma ve sistem özetini panoya kopyalama araçları.
    - **Windows Yönetim Konsolları:** Disk Yönetimi, Aygıt Yöneticisi ve Görev Yöneticisi hızlı başlatıcıları.
  - Küçük pencere boyutlarında veya yüksek DPI ölçeklemelerinde başlık ve açıklamaların kırpılması (`Sistem Donanım Raporu & Yönetim Araç...`) tamamen engellendi.

- **Font Sembol Glitch Onarımı (Anti-Artifact Fix):**
  - "HTML Raporu Oluştur" butonunda font eşleme hatasından dolayı çıkan bozuk `"R"` harfi glitçi kaldırıldı; yerine yerel Fluent `DocumentBulletList20` sembolü entegre edildi.

- **Hızlı Windows Konsolları & Görev Yöneticisi Entegrasyonu:**
  - Windows yönetim araçlarına tek tıkla `taskmgr.exe` (Görev Yöneticisi) başlatıcı butonu dahil edildi.

- **Dinamik Buton Yerleşimi:**
  - Butonlar `WrapPanel` içine alınarak ekran daraldığında metinlerin üstüne binmesi yerine pürüzsüzce alt satıra geçmesi sağlandı.
