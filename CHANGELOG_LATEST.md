# Bakım v3.19.1 - Sürüm Notları

## Koşulsuz Kurulum Entegrasyonu, Sessiz Arka Plan Nöbetçisi & Arka Plan Güncelleyici Filtresi

### 1. Koşulsuz ve Zahmetsiz Varsayılan Kurulum Deneyimi
- **Kutudan Çıktığı Gibi Hazır:** Kurulum sihirbazında (`Inno Setup`) kullanıcının ek onay kutuları seçmesine gerek kalmadan Windows Gezgini Sağ Tık Menüsü ("Bakım ile Kaldır") ve Kurulum Nöbetçisi (Sentinel Guard) varsayılan olarak etkinleştirilir.
- **Kullanıcı İradesi:** Ayarlar sekmesine eklenen "Windows Gezgini Sağ Tık Menüsü" anahtarı ile kullanıcı istediği an sağ tık menüsünü tek tıkla açabilir veya kapatabilir; tercihler anında `appsettings.json` içerisine kalıcı olarak yazılır.

### 2. %100 Sessiz Arka Plan İzleme & Akıllı Bildirim
- **Sıfır Arayüz Kesintisi:** Kurulum süreçleri başladığında ekranda beliren ve kullanıcının odağını bölen izleme bildirimi tamamen sessizleştirildi. Kurulum süreci ve alt süreçler arka planda sıfır rahatsızlıkla izlenir.
- **Yalnızca Somut Sonuçlarda Bildirim:** İptal edilen, yarıda bırakılan veya sisteme yeni dosya/yürütülebilir bırakmayan süreçlerde bildirim penceresi bastırılır. Bildirim penceresi yalnızca kurulum fiilen tamamlandığında ve yeni dosya/yürütülebilirler üretildiğinde tetiklenir.

### 3. Arka Plan Güncelleyici (False Positive) ve Dizin Koruması
- **Arka Plan Servis & Güncelleyici Ayrıştırması:** Java Platform SE Auto Updater (`jusched.exe`), `jucheck.exe`, `googleupdate.exe`, `microsoftedgeupdate.exe`, `onedrive.exe` gibi sistemde zaten kurulu olan arka plan zamanlayıcılarının nöbetçiyi yanıltması engellendi.
- **Kurulu Dizin Koruması:** `Program Files`, `Program Files (x86)` ve `Windows` dizinlerinde çalışan mevcut yazılımlar (`msiexec.exe` hariç) kurulum sürecinden muaf tutularak sistem kararlılığı garanti altına alındı.

### 4. Kalite, Test ve Doğrulama
- **Tüm Birim Testleri:** `SetupSentinelTests` ve bağımlılık enjeksiyon testleri güncellenen parametrelerle %100 başarıyla doğrulandı.
- **Fluent 2 Sembol & Tasarım Doğrulaması:** `verify-symbols.py` ve `verify-tokens.py` araçlarıyla tüm XAML ve C# dosyaları sıfır geçersiz sembol ve sıfır AI emojisi kuralıyla test edildi.
