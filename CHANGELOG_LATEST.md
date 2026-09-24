# Bakım v3.18.6 - Sürüm Notları

## Windows Gezgini Bağlam Menüsü Taşınması, Canlı Tray Mini HUD, Power-User Kısayolları & Sıfır Emoji Standardı

### 1. Windows Gezgini Sağ Tık Menüsü Ayarlara Taşındı (Mimari Düzeltme)
- **Kök Neden & Mimari Sorun:** Windows bağlam menüsü kaydı (`HKCU\Software\Classes\...`) bir sistem ve kabuk (Shell) entegrasyonu ayarı olmasına rağmen, geçmişte sehven Program Kaldırıcı (`UninstallerModuleView.xaml`) başlık çubuğuna durum bildirmeyen statik bir buton olarak konulmuştu.
- **Kalıcı Mimari Çözüm:** 
  - `IShellContextMenuService` doğrudan `SettingsViewModel`'a enjekte edildi ve çift yönlü `IsShellContextMenuEnabled` özelliğiyle bağlandı.
  - `SettingsModuleView.xaml` içerisinde hem **"Sistem & Başlangıç"** hem de **"Modül Tercihleri -> Program Kaldırıcı"** bölümlerine canlı aktif/pasif durum rozetine sahip modern `ui:ToggleSwitch` kartı eklendi.
  - Program Kaldırıcı başlığındaki gereksiz buton temizlenerek ekran tamamen program kaldırma operasyonlarına (Avcı Modu, Kurulum İzleyici, Yenile) odaklandı.

### 2. Sistem Tepsisi (Tray Flyout) Canlı Mini HUD Yenilendi
- **Canlı Donanım Telemetrisi:** Görev çubuğundaki simgeye tıklandığında açılan Tray Flyout penceresi baştan tasarlandı; ana pencereyi açmaya gerek kalmadan canlı CPU ve RAM kullanım yüzdeleri, mini progress barlar ve serbest bellek dökümü eklendi.
- **Hızlı RAM Temizleme & Oyun Modu:** Tek tıkla arka planda bellek boşaltma (`AutoTrimWorkingSetsAsync`) ve Ultra Oyun Modu geçişi canlı durum göstergeleriyle entegre edildi.

### 3. Power-User Global Klavye Kısayolları
- **Hızlı Modül Navigasyonu (`Ctrl + 1..9`):** 1: Dashboard, 2: Cleaner, 3: Optimizer, 4: Startup, 5: SystemInfo, 6: NetworkMonitor, 7: ServiceManager, 8: Tweaker, 9: Settings.
- **Evrensel Modül Yenileme (`F5`):** Hangi sayfada olunursa olunsun aktif modül verilerini anında tazeleyen global `RefreshActiveModuleCommand`.
- **Hızlı Aksiyonlar (`Ctrl + Shift + R` & `Ctrl + Shift + G`):** Hızlı RAM boşaltma ve Ultra Oyun Modunu klavyeden anında tetikleme.

### 4. Sıfır Emoji Standardı (Strict Zero-Emoji Cleanliness)
- Kod tabanındaki tüm kalan yapay zeka emojileri (`⚡`, `🎮`, `🛡️`, `🔍`, `📁`, `📋`, `❌`, `⚠️`, `✅`, `📦`) temizlendi.
- Arayüzde yalnızca resmi Windows 11 Fluent 2 `SymbolRegular` sembolleri ve profesyonel tipografi uygulandı.

### 5. Kalite, Test ve Doğrulama
- **173/173 Birim Testi:** Yeni `SettingsShellIntegrationTests` testleriyle bağlam menüsü entegrasyonu, DI yapılandırması ve durum güncellemeleri %100 doğrulandı.
- **9.235 Sembol & Tasarım Tokeni:** `verify-symbols.py` ve `verify-tokens.py` ile sıfır geçersiz sembol ve token onayı alındı.
- **Sıfır Hata:** .NET 10 mimarisi üzerinde tam kararlılık kanıtlandı.
