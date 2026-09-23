---
trigger: always_on
description: "Kullanıcı Kalite ve Profesyonellik İlkesi: Sıfır Basite Kaçma, Tam Teşekküllü Özellikler, Kusursuz Tasarım ve Sıfır Ölü Kod"
---

# Kullanıcı Kalite, Tasarım ve Gelişmiş Mimari İlkesi (Mandate)

Bu kural, projede yapılacak HER tasarım, XAML bileşeni, C# ViewModel ve servis geliştirme sürecinde KESİNTİSİZ ve TAVİZSİZ uygulanır:

---

### 1. ASLA BASİTE KAÇMAMA (No Simplistic / MVP Shortcuts)
- İstenen bir özellik veya sekme asla yüzeysel, "örnek olsun diye yapılmış" veya yarım bırakılmış olamaz.
- Bir modül eklendiğinde veya geliştirildiğinde sadece tek bir buton/fonksiyon değil; **arama, filtreleme, canlı istatistik/KPI kartları, detay çekmecesi/paneli, sıralama, hata toleransı, dışa aktarma (export) ve çoklu aksiyonlar** gibi profesyonel bir aracın sahip olması gereken tüm yeteneklerle bir bütün olarak tasarlanır ve kodlanır.

---

### 2. SIFIR ÖLÜ KOD VE SIFIR YALANCI YORUM (Zero Dead Code & Full Delivery)
- Kod tabanında kullanılmayan, ölü, yetim veya yorum satırına alınmış atıl kod bırakılamaz.
- `// TODO:`, `// buraya kod gelecek`, `// kalan kısım aynı` gibi tembel kısaltmalar KESİNLİKLE YASAKTIR.
- Her metodun, komutun ve UI bileşeninin çalışan, üretime hazır (Production-Ready) tam gövdesi yazılır.

---

### 3. KUSURSUZ VE GELİŞMİŞ TASARIM (Senior Fluent 2 & Slate Dark UI)
- Windows 11 Fluent 2 standartları (`#0F172A` Slate Dark, `#1E293B` Card, `#38BDF8` Accent) milimetrik olarak uygulanır.
- UI bileşenleri donuk ve statik olamaz; hover animasyonları, durum rozetleri (badges), progress ring'ler ve net tipografi hiyerarşisi içerir.
- Responsive esneklik zorunludur: Ekran boyutuna göre kırılmayan `WrapPanel`, esnek `Grid` (`*` ve `Auto`) kullanılır; sabit piksel taşmaları engellenir.
- UI Thread asla kilitlenmez (100% Async / Non-Blocking, arka plan `Task.Run` ve `ObservableCollection` iş parçacığı güvenliği).

---

### 4. SAĞLAM VE KAPSAMLI SAVUNMACI PROGRAMLAMA (Defensive Architecture)
- Tüm sistem çağrıları (Registry, WMI, Process, Win32 P/Invoke, File System) detaylı `try-catch`, null-coalescing (`?.`, `??`) ve yetki denetimleriyle donatılır.
- Hata durumunda uygulama çökmez; kullanıcı dostu hata durum rozeti veya bilgilendirmesi sunulur.

---

### 5. OTOMATİK SÜRÜM & DEPLOY DİSİPLİNİ (Auto-Deploy Mandate)
- Yeni bir özellik, sekme veya düzeltme tamamlandığında kullanıcıya sormadan:
  - Sürüm numaralarını ilgili tüm dosyalarda (`.csproj`, `.iss`, `.manifest`, `AutoUpdateService`, `GitHubUpdateService`, `SettingsViewModel`, `MainWindow.xaml`) eksiksiz artır.
  - `CHANGELOG_LATEST.md` dosyasını yeni sürümün zengin notlarıyla güncelle.
  - Değişiklikleri commit'le, yeni sürüm tag'ini (`vX.X.X`) oluştur ve doğrudan GitHub'a pushlayarak CI/CD deploy'unu otomatik tetikle.

---

### 6. SIFIR GEÇERSİZ SEMBOL VE ZORUNLU ÖN TEST DİSİPLİNİ (Zero Invalid Symbols & Pre-flight Testing)
- Hiçbir XAML veya C# dosyasında rastgele, uydurma veya tanımsız sembol (`SymbolRegular`) kullanılamaz!
- Her deploy öncesinde `Tools/verify-symbols.py` ve `Tools/verify-tokens.py` çalıştırılarak tüm sembollerin ve tokenların %100 geçerli olduğu kanıtlanmadan ASLA commit ve push yapılamaz.
- CI/CD derleme hattına sembol ve token doğrulama adımları zorunlu kontrol (gatekeeper) olarak eklenir.

---

### 7. HER ZAMAN DİJİTAL İMZA (Authenticode Code Signing Mandate)
- Tüm derlenen `.exe` dosyaları ve Inno Setup kurulum paketleri (`Bakim-*-Setup.exe`, `Bakim.exe`) CI/CD sürecinde ve her sürüm dağıtımında resmi sertifika ile dijital olarak imzalanmalıdır (`signtool.exe` Authenticode Code Signing).
- İmzasız paket hiçbir zaman yayınlanamaz.
- `AutoUpdateService` ve `AuthenticodeVerifier` tarafından imzasız uyarısı veya güvenlik engeli çıkmayacak şekilde proje sertifikası imzalama ve doğrulama kuralına tavizsiz uyulacaktır.

