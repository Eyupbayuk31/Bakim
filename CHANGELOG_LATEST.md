# Bakım v3.20.0 - Sürüm Notları

## Kurulum Nöbetçisi v2 (Faz 0): Stabilizasyon, Derin Sensörler, Güvenli Geri Alma & EDR Entegrasyonu

### 1. Güvenli Geri Alma ve Veri Kaybı Önleme (P0-1 Kökten Çözüldü)
- **Net Dosya Ayrımı:** Sistem olayları artık `CreatedFiles`, `ModifiedFiles`, `DeletedFiles` ve `RenamedFiles` olarak kesin kategorilere ayrıştırılmaktadır.
- **Değiştirilen Dosyalar Koruma Altında:** Önceden var olan veya kurulum sürecinde değiştirilen (`ModifiedFiles`) dosyalar geri alma (revert) işleminde **asla silinmez**.
- **Geri Dönüşüm Kutusu (Recycle Bin) Güvencesi:** Kalıcı silme yerine yeni üretilen dosyalar güvenle Geri Dönüşüm Kutusu'na gönderilir; kazara dosya kaybı riski sıfıra indirilmiştir.

### 2. Standart Haklarla UAC Yönetici Süreçlerini Yakalama (ProcessInfoReader, P0-2)
- **PROCESS_QUERY_LIMITED_INFORMATION:** UAC ile yetki yükselten kurulumlarda `Process.MainModule` erişim reddi (Error 5) hatası P/Invoke katmanıyla aşıldı. Standart kullanıcı haklarında dahi tam yürütülebilir dosya yolu hatasız okunur.
- **ToolHelp32 ile Ebeveyn PID Analizi:** Süreçlerin ebeveyn kimlikleri (Parent PID) yetki istemeden tespit edilerek gerçek kurulum ağaçları oluşturulur.
- **PID Yeniden Kullanım (Reuse) Koruması:** `GetProcessTimes` ile süreç oluşturma zamanı (Creation Time) milisaniye hassasiyetinde takip edilerek sistemdeki PID geri dönüşüm çakışmaları engellenir.

### 3. Derin Registry Hotspot Sensörü (P0-4, P0-5)
- **Değer Düzeyinde Run & RunOnce Takibi:** Başlangıç girdilerinin anahtar değil *değer* olduğu gerçeğiyle; HKLM ve HKCU altındaki `Run`, `RunOnce` ve WOW6432Node değerleri anlık fark (delta) analiziyle tam olarak yakalanır.
- **Windows Servisleri (`SYSTEM\CurrentControlSet\Services`):** Kurulumların arkada bıraktığı yeni Windows servisleri, sürücüler ve servis ikili yolları anında tespit edilir.
- **Hive Biçim Normalizasyonu:** `HKLM` ve `HKCU` etiketleme hataları giderildi; 64-bit ve 32-bit kayıt defteri görünümleri bir arada taranır.

### 4. Tekil ve Standart Depolama Motoru (SessionStore, P0-6)
- **Şema Versiyonlu Raporlama (`SchemaVersion: 2`):** Eski parçalı JSON dosyaları yerine oturum başına standartlaştırılmış ve indekslenebilir rapor yapısına geçildi.
- **Geriye Dönük Tam Uyumluluk:** Eski v1 raporları ve anlık durum dosyaları sessiz ve hatasız biçimde yeni depolama yapısına uyarlanır.

### 5. Kararlı Eşzamanlılık ve msiexec Servis Koruması (P0-7, P0-8)
- **PeriodicTimer & Asenkron Kilitler:** `async void` zamanlayıcılar kaldırılarak `PeriodicTimer`, iptal jetonu (`CancellationToken`) ve `SemaphoreSlim` ile tek seferlik güvenli tamamlama (finalize) garantisi sağlandı.
- **msiexec /V Servis Ayrıştırması:** Kurulum bittikten sonra 10 dakika boyunca arka planda yaşayan `msiexec /V` servis sürecinin raporu asılı bırakması engellendi.
- **MsiInstaller Olay Günlüğü Takibi:** Windows Application günlüğündeki 1040, 1042, 11707 ve 11708 numaralı olaylar dinlenerek MSI kurulumlarının gerçek bitiş anı tespit edilir.

### 6. Kaldırıcı Tespiti & Gürültü Filtresi (P0-9, P0-10)
- **`SessionKind.Uninstall` Ayrımı:** `unins000.exe`, `uninstall.exe` ve `msiexec /x` gibi kaldırıcı süreçleri kurulum sayılmaz; gereksiz bildirim açılmaz.
- **64 KB FSW Arabelleği & Taşma Koruması:** `FileSystemWatcher` arabelleği 8 KB'den 64 KB'ye çıkarıldı; taşma durumunda `IsPossiblyIncomplete` bayrağı ile yeniden tarama tetiklenir.
- **Akıllı Gürültü Filtresi:** Chrome, Edge, Firefox önbellekleri, `thumbcache_*`, `Prefetch` ve Bakım'ın kendi günlük dosyaları kurulum raporundan elenir.

### 7. Flyout v2 Hızlı Risk Özeti (Hızlı Kazanım)
- **Anlık Durum Kartı:** Kurulum bittiğinde eklenen yürütülebilir (.exe/.dll), başlangıç girdisi ve yeni servis sayılarını gösteren modern Fluent 2 risk rozeti sunulur; kullanıcı "Analizör ile Tara" butonuna neden basması gerektiğini tek bakışta anlar.

### 8. Kalite Güvencesi ve CI Test Hattı
- **194/194 Birim Testi:** Kopya testler kaldırıldı; gerçek `InstallerClassifier`, `RollbackPlanner` değişmezleri, `RegistryHotspotSensor` ve `SessionStore` sınıfları doğrudan test edildi.
- **GitHub Actions CI:** Her `push` ve `pull_request` için Windows üzerinde `dotnet test`, sembol ve token doğrulamasını otomatik koşan `.github/workflows/ci.yml` hattı devreye alındı.
