# Bakım v3.17.8 - Sürüm Notları

## Araçlar Ekosistemi Genişletmesi: 24+ Klasik Sistem Konsolu & 13 Yeni Güçlü Yazılım ⚡🛠️

### 1. WPF-UI Hata Çözümü (ControlAppearance.Transparent)
- **Kritik XAML Düzeltmesi:** Mağaza kartlarındaki geçersiz `Appearance="Subtle"` değeri WPF-UI standartlarına uygun `Appearance="Transparent"` olarak düzeltildi.
- **Sıfır Çökme Güvencesi:** XAML ayrıştırıcı hatası giderilerek `Bakim.Tests.SymbolValidator.All_Xaml_ControlAppearance_Values_Must_Be_Valid` birim testi ile %100 doğrulandı.

### 2. 24 Adet Güçlü Windows Yönetim Konsolu (Sistem Araçları)
Windows Tweaker modülü altındaki Klasik Sistem Araçları vitrini 8 araçtan 24 tam teşekküllü yönetim konsoluna çıkarıldı ve kategorize edildi:
- **Sistem:** Aygıt Yöneticisi (`devmgmt.msc`), Bilgisayar Yönetimi (`compmgmt.msc`), Windows Hizmetleri (`services.msc`), Kayıt Defteri Düzenleyicisi (`regedit.exe`), Yerel Grup İlkesi (`gpedit.msc`), Olay Görüntüleyici (`eventvwr.msc`), Sistem Özellikleri (`sysdm.cpl`), Sistem Yapılandırması (`msconfig`).
- **Donanım & Disk:** Disk Yönetimi (`diskmgmt.msc`), Disk Temizleme (`cleanmgr.exe`), DirectX Teşhis Aracı (`dxdiag.exe`), Kaynak İzleyicisi (`resmon.exe`).
- **Ağ & Güvenlik:** Ağ Bağlantıları / Adaptörler (`ncpa.cpl`), Sertifika Yöneticisi (`certmgr.msc`), Yerel Kullanıcılar ve Gruplar (`lusrmgr.msc`), Yerel Güvenlik İlkesi (`secpol.msc`).
- **Hızlı Erişim & Yardımcılar:** Windows God Mode (`explorer.exe shell:::{...}`), Klasik Denetim Masası (`control.exe`), Windows Terminal (`wt.exe` / fallback `powershell`), Karakter Eşlem (`charmap.exe`), Hesap Makinesi, Not Defteri, WordPad, Klasik Windows Fotoğraf Görüntüleyici.
- **Gelişmiş Kart Tasarımı:** Her araç için kategori rozeti (`Sistem`, `Donanım & Disk`, `Ağ & Güvenlik`, `Hızlı Erişim`), Fluent ikon squircle'ı ve modern etkileşimli kart stili eklendi.

### 3. 13 Yeni Popüler Donanım, Medya & Verimlilik Aracı (Mağaza Vitrini)
Bakım'ın yerleşik Sistem Temizliği, Uygulama Kaldırıcısı ve Ağ Trafiği İzleyicisi ile çakışmayacak, piyasada kendini kanıtlamış dünya standardı araçlar Mağaza Hub'ına eklendi:
- **Donanım & Benchmark:**
  - 📊 **HWiNFO64** (`REALiX.HWiNFO`): Kapsamlı donanım analizi, canlı sensör telemetrisi ve sıcaklık izleme.
  - 💽 **CrystalDiskInfo** (`CrystalDewWorld.CrystalDiskInfo`): Disk SMART sağlık, yıpranma ve sıcaklık durumu takibi.
  - ⚡ **CrystalDiskMark** (`CrystalDewWorld.CrystalDiskMark`): SSD/NVMe/HDD okuma-yazma hız testleri.
  - 🔥 **Geeks3D FurMark 2** (`Geeks3D.FurMark.2`): Ekran kartı OpenGL/Vulkan stres testi ve kıyaslama.
  - 🎮 **MSI Afterburner** (`Guru3D.Afterburner`): GPU hız aşırtma (overclock), fan eğrisi yönetimi ve RTSS FPS OSD.
- **Format & USB Medya:**
  - 💾 **Ventoy** (`Ventoy.Ventoy`): Tek bir USB belleğe birden fazla ISO kopyalayarak multiboot başlatma aracı.
  - 📀 **balenaEtcher** (`Balena.Etcher`): Hızlı, güvenli ve doğrulamalı USB / SD kart kalıp yazdırma aracı.
- **Masaüstü, Ses & Video Verimliliği:**
  - 👁️ **QuickLook** (`QL-Win.QuickLook`): macOS tarzı Space tuşuyla dosya, görsel ve arşiv önizleme.
  - ⌨️ **AutoHotkey** (`AutoHotkey.AutoHotkey`): Güçlü klavye/fare kısayolları ve masaüstü otomasyon betiği motoru.
  - 🎧 **EarTrumpet** (`File-New-Project.EarTrumpet`): Windows için gelişmiş per-app ses mikseri ve ses seviyesi kontrolü.
  - 🎬 **ScreenToGif** (`NickeManarin.ScreenToGif`): Ekran kaydı alıp GIF/video olarak düzenleme ve dışa aktarma aracı.
  - ✂️ **LosslessCut** (`ch.LosslessCut`): Yeniden kodlama yapmadan (kayıpsız) anında video kesme ve birleştirme aracı.
- **Sürücü Kurtarma:**
  - 🧹 **Display Driver Uninstaller (DDU)** (`Wagnardsoft.DisplayDriverUninstaller`): GPU sürücülerini kalıntısız temizleyip temiz kurulum yapma aracı.

### 4. Kusursuz Savunmacı Mimari & Pre-flight Doğrulama
- **9.235 Sembol & Token %100 Geçerli:** `verify-symbols.py` ve `verify-tokens.py` ile sıfır hata doğrulandı.
- **143 Birim Testi:** `dotnet test` ile tüm testler başarıyla geçti.
- **Yerel .NET 10 SDK Derlemesi:** Release modunda 0 hata ve 0 uyarı ile derleme tamamlandı.
