# Bakım v4.1.1 - Sürüm Notları

## Kurulum Nöbetçisi Asenkron Donma & Kilitlenme Onarımı, Bellek İçi Filtreleme ve Güvenlik Güçlendirmesi

Bakım v4.1.1 sürümü, Kurulum Nöbetçisi modülüne giriş yapıldığında diskteki oturum raporlarının UI iş parçacığında senkron taranması sonucu ortaya çıkan arayüz donmalarını ve kilitlenmelerini (sync-over-async deadlock) tamamen ortadan kaldırmaktadır. Mimari baştan sona asenkron ve savunmacı yapıya kavuşturulmuştur.

---

### 1. Kurulum Nöbetçisi UI Donma & Deadlock Onarımı (Async Non-Blocking Architecture)
- **Sıfır Senkron Disk Beklemesi:** Disk üzerindeki JSON oturum raporlarını senkron bekleyen (`.GetAwaiter().GetResult()`) çağrılar `ISetupSentinelService.LoadSavedReportsAsync()` ve `SessionStore.LoadAllReportsAsync()` üzerinden tamamen dikey asenkron mimariye dönüştürüldü.
- **Akıcı Sayfa Açılışı:** Nöbetçi sayfasına geçildiğinde UI iş parçacığı asla kilitlenmez ("Yanıt Vermiyor" durumuna düşmez); raporlar arka planda taranarak hazır olduğunda arayüze pürüzsüz aktarılır.
- **Arka Plan Raporlama Olayları:** Kurulum tamamlandığında tetiklenen `SetupFinished` olayları UI thread'ini dondurmadan arka planda asenkron yenilenir.

---

### 2. Bellek İçi Akıllı ve Anlık Filtreleme (In-Memory Filter Engine)
- **Arama Kutusunda Sıfır Disk I/O:** Arama çubuğuna yazılan her karakterde diskteki JSON dosyalarını yeniden okuyup ayrıştırma davranışı iptal edildi.
- **Yüksek Performans:** Yüklenen raporlar bellekte tutularak arama ve filtreleme sorguları CPU bellek havuzunda anlık olarak çalıştırılır; binlerce kayıt bile olsa yazma anında sıfır gecikme sağlanır.

---

### 3. Defansif Koruma Rozet Güvenliği (Defensive Protection Badge)
- **Null-Coalescing Güvenliği:** `ProtectionStatus` nesnesi için `?.` null denetimleri ve güvenli fallback metinleri ("Temel Mod", "Standart mod devrede.") tanımlandı.
- **Çökme Önleme:** Sensör başlatma sırasında oluşabilecek istisnalarda veya geçiş anlarında arayüzün `NullReferenceException` ile çökmesi kesin olarak engellendi.

---

### 4. Asenkron İlerleme Çubuğu & StatCard Optimizasyonu
- **Zarif İlerleme Göstergesi:** Kurulum Nöbetçisi geçmişi yüklenirken liste üzerinde parlayan modern Fluent `ProgressBar` (`IsLoading` tetiklemeli) konumlandırıldı.
- **StatCard Veri Uyumluluğu:** KPI sayaçları (`TotalCount`, `Last30Count`, `PersistenceCount`, `RiskyCount`) için `StringFormat` bağlamaları eklenerek tip dönüştürme performansı artırıldı.

---

### 5. Kapsamlı Test ve Doğrulama
- **Yeni Birim Testleri:** `SentinelService_LoadSavedReportsAsync_ReturnsWithoutDeadlock` ve `SentinelViewModel_OnActivatedAsync_LoadsAsyncAndFiltersInMemory` testleri eklendi.
- **727 Birim Testi:** Tüm testler %100 başarıyla geçti (`Bakim.Core.Tests` 440, `Bakim.Tests` 287).
- **UI Duman ve Tasarım Doğrulaması:** 20 modül görünümü, tema geçişleri, DI konteyneri, semboller (`verify-symbols.py`) ve tokenlar (`verify-tokens.py`) sıfır hatayla doğrulandı.
