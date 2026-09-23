# Bakım v3.17.9 - Sürüm Notları

## Windows Sağ Tık Menüsü "Bakım ile Kaldır" Entegrasyonu & Aşırı Gelişmiş Kalıntı Yönetim Sihirbazı 🚀🧹

### 1. Windows Sağ Tık Menüsü Entegrasyonu ("Bakım ile Kaldır")
- **Doğrudan Masaüstünden Kaldırma:** Masaüstündeki kısayollara (`.lnk`), `.exe` dosyalarına ve uygulama klasörlerine (`Directory`) sağ tıklandığında beliren resmi Bakım ikonlu **"Bakım ile Kaldır"** menü girdisi eklendi.
- **Tek Tıkla Yönetim:** Program Kaldırıcı (Uninstaller) modülünün üst araç çubuğuna eklenen **"Sağ Tık Menüsü"** butonu veya Inno Setup kurulum sihirbazı üzerinden tek tıkla aktif/pasif edebilme desteği sağlandı (`--register-contextmenu` & `--unregister-contextmenu`).

### 2. Akıllı Kısayol & Hedef Çözücü Motoru (ShellUninstallResolverService)
- **Kısayol Hedefini Çözümleme:** Kullanıcı bir `.lnk` kısayoluna tıkladığında Windows WScript COM katmanı ile asıl hedef `.exe` ve çalışma dizini saniyeler içinde çözülür.
- **Kayıt Defteri Eşleme:** Çözümlenen yol üzerinden HKLM/HKCU 32/64-bit Windows Uninstall veritabanı taranarak resmi kaldırma dizesi (`UninstallString`), sürüm, yayıncı ve ikon bilgileri otomatik eşleştirilir.
- **Heuristik Kurtarma:** Kayıt defterinde kaydı bulunmayan veya taşınabilir (portable) uygulamalar için `FileVersionInfo` analizinden anında akıllı kaldırma profili oluşturulur.

### 3. Aşırı Gelişmiş Kaldırma ve Kalıntı Sihirbazı (DeepUninstallWizardWindow)
Windows 11 Fluent 2 Slate Dark tasarım dilinde 880x680 boyutlarında, 5 aşamalı akıllı durum makinesi ile yönetilen yeni pencere:
- **Aşama 1: Program Analiz & Hazırlık:**
  - Yüksek çözünürlüklü program simgesi, adı, yayıncısı, versiyonu, kurulum konumu ve disk boyutu kartı.
  - Güvenlik seçenekleri: "Windows Geri Yükleme Noktası Oluştur", "İlişkili Çalışan Süreçleri Otomatik Kapat" ve "Kayıt Defteri Yedeği (.reg) Al".
- **Aşama 2: Resmi Kaldırıcı Yürütme & Canlı İzleme:**
  - Programın kendi uninstaller penceresi başlatılır ve arka planda kapanana kadar canlı süreç takibi yapılır.
- **Aşama 3: Derin Kalıntı Taraması:**
  - Kaldırıcı kapandığı anda otomatik olarak HKLM/HKCU Registry, AppData, LocalAppData, ProgramData, Program Files, Temp ve Masaüstü/Başlat Menüsü kısayollarında derin kalıntı taraması yürütülür.
- **Aşama 4: "Kaldırma Başarılı!" & İnteraktif Kalıntı Yönetim Paneli:**
  - Büyük yeşil başarı rozeti: `✔ [Program Adı] Başarıyla Kaldırıldı!`
  - Canlı Arama Kutusu: Kalıntı adı veya yoluna göre anında filtreleme.
  - Kategori Filtre Sekmeleri: `Tümü`, `Kayıt Defteri`, `Klasörler`, `Dosyalar`.
  - **"Şunu Silme" Seçimi:** Listelenen her kalıntının yanında onay kutusu (CheckBox) yer alır; kullanıcı dilediği kalıntının işaretini kaldırarak silinmesini engelleyebilir.
  - Güvenlik Rozetleri: `%100 Güvenli`, `İnceleyin` etiketleri.
  - Sağ Tık Menüsü: Dosya Gezgininde / Regedit'te Göster ve Yolu Kopyala.
  - Alt Bar: Toplam seçilen kalıntı sayısı ve temizlenecek disk alanı özeti.
- **Aşama 5: Temizlik Tamamlandı Raporu:**
  - Silinen kalıntı adedi, kurtarılan alan ve alınan `.reg` yedeğine tek tıkla erişim butonu.

### 4. Savunmacı Kayıt Defteri Yedeği (.reg)
- Kalıntı temizleme öncesinde `AppData\Local\Bakim\Backups` klasörüne `Windows Registry Editor Version 5.00` formatında tam uyumlu silme yedeği alınır.

### 5. Pre-flight & Test Doğrulaması
- **147/147 Birim Testi:** `ShellUninstallResolverTests` dahil tüm testler başarıyla geçti.
- **9.235 Sembol & Token %100 Geçerli:** Sıfır geçersiz sembol ve token.
- **Yerel .NET 10 SDK Release Derlemesi:** 0 Hata ve 0 Uyarı ile başarıyla tamamlandı.
