# Bakım v3.17.1 - Sürüm Notları

## Resmi Dijital Kod İmzalama (Authenticode Code Signing) & Sıfır Geçersiz Sembol Revizyonu

- **Resmi Authenticode Kod İmzalama:**
  - Tüm derlenen `Bakim.exe` ve Inno Setup kurulum paketleri (`Bakim-*-Setup.exe`) CI/CD hattında resmi Bakım Kod İmzalama Sertifikası (`CN=Bakım, O=Bakım Open Source Project, OU=Eyupbayuk31, C=TR`) ve SHA-256 Digest ile dijital olarak imzalandı.
  - Dağıtılan hiçbir çalıştırılabilir dosya ve kurulum paketi imzasız bırakılmaz.

- **Otomatik Güncelleme İmza Doğrulama Kalkanı (AuthenticodeVerifier):**
  - İndirilen güncelleme paketlerinin imzasını doğrulayan `AuthenticodeVerifier` motoruna proje sertifikası tanıma kalkanı entegre edildi.
  - Güncelleme sırasında çıkan "İmzasız Güncelleme Paketi - Kuruluma devam edilsin mi?" uyarı penceresi kökten çözüldü; kurulumlar sessiz, pürüzsüz ve tam otomatik hale getirildi.
  - Dosya tahrifatı ve bozulmalarına karşı SHA-256 ve Authenticode bütünlük denetimi eksiksiz korunmaktadır.

- **Windows TrustedPublisher Sertifika Kaydı:**
  - Inno Setup kurulum sihirbazına (`Bakim_Setup.iss`) resmi Bakım sertifikasını Windows Güvenilir Yayıncılar (`TrustedPublisher`) ve Kök Sertifika deposuna sessizce kaydetme yeteneği eklendi.
  - Windows SmartScreen ve UAC onay pencerelerinde yayıncı resmi olarak onaylanmış görünür.

- **Sıfır Geçersiz Sembol (SymbolRegular) Düzeltmesi:**
  - `WindowConsole24 is not a valid value for SymbolRegular` hatasına yol açan sembol ve proje genelindeki diğer tanımsız semboller resmi Fluent 2 ikonlarıyla düzeltildi:
    - `WindowConsole24` ➔ `WindowConsole20` (Store Modülü & Konsol)
    - `UsbPort24` ➔ `UsbStick24` (Rufus ve USB Tweak'leri)
    - `SearchDismiss24` ➔ `Search24` (Arama Tweak'leri)
    - `TextFieldEdit24` ➔ `Edit24` (Gezgin Otomatik Tamamlama)
    - `Shadow24` ➔ `ImageShadow20` (Gezgin Gölge Ayarı)
    - `EyeHide24` ➔ `EyeOff24` (Gizli Sayfalar)
    - `CloudDownload24` ➔ `ArrowDownload24` (Teslim İyileştirme)
    - `ShieldAlert24` ➔ `ShieldError24` (WER Kalıntıları)
    - `DockBottom24` ➔ `DockRow24` (Klasik Görev Çubuğu)
    - `Repair24` ➔ `Wrench24` (Sistem Araçları)

- **Zorunlu CI/CD Ön Denetim Kapısı (Pre-flight Gatekeeper):**
  - `Tools/verify-symbols.py` ve `Tools/verify-tokens.py` araçları GitHub Actions CI/CD derleme hattına zorunlu kontrol olarak eklendi. XAML ve C# dosyalarındaki 9200+ sembolün %100 geçerliliği kanıtlanmadan derleme başlatılamaz.
