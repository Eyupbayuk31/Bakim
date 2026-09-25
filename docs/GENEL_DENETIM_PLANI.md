# Bakım v3.19.1: Genel Denetim ve Geliştirme Planı

> Kapsam: Uygulamanın tüm modülleri. İncelenen kod ≈ 43.000 satır C# ve ≈ 18.000 satır XAML.
>
> Yöntem: Kaynak kod statik olarak okundu, risk kalıpları için tüm depoda tarandı (hata yutma, sync-over-async, süreç/registry/dosya silme, dış indirme, zamanlayıcılar, sabit renkler) ve kritik akışlar satır satır izlendi. İnceleme bir Linux konteynerinde yapıldı; uygulama **Windows'ta çalıştırılmadı**. Bu yüzden her bulgu iki etiketten birini taşır:
> - **Kesin:** Kodun yaptığı iş okunarak doğrulandı.
> - **Muhtemel:** Windows davranışına bağlı; cihazda doğrulanmalı.
>
> Bu belge iki ayrıntılı planla birlikte okunmalı:
> - `docs/SENTINEL_V2_PLAN.md`: Kurulum Nöbetçisi
> - `docs/UNINSTALLER_V2_PLAN.md`: Kaldırıcı ve sağ tık kaldırma
>
> Bu belgede o iki modülün yalnızca özeti var. Ayrıntılı görevler kendi belgelerinde.

---

## 0. Özet

Uygulama çok geniş bir özellik yelpazesine sahip ve arayüz tarafı olgun (Fluent 2, token dosyaları, DI, bazı testler). Asıl sorun **güven**. Birçok modül kullanıcıya olduğundan daha güvenli, daha başarılı ya da daha gerçek görünüyor:

- Kaldırıcı, isim benzerliğiyle **başka programların verisini** silebiliyor.
- Kontrol Paneli'ndeki CPU ve GPU **sıcaklıkları ölçülmüyor**; CPU yükünden bir formülle üretiliyor.
- İnce ayarlar (tweak) yazma başarısız olsa bile "uygulandı" diyor.
- Temizleyicinin Firefox kategorisi pratikte hiçbir şey silmiyor.
- "RAM temizleme" ve "Oyun Modu" performansı artırmak yerine düşürüyor.

Önerilen sıra:
1. **Güvenlik ve veri kaybı** (Sprint 1–2)
2. **Dürüstlük: yanlış başarı mesajları ve sahte veriler** (Sprint 2)
3. **Kesin hatalar** (Sprint 3)
4. **Mimari temizlik ve performans** (Sprint 4–5)
5. **Tasarım ve UX** (Sprint 5–6)
6. **Nöbetçi ve Kaldırıcı v2 özellikleri** (paralel hat)

### En kritik 12 bulgu

| # | Bulgu | Modül | Tür |
|---|---|---|---|
| 1 | Güncelleyicide ölü ve riskli ikinci motor (`GitHubUpdateService`); asset seçimi "herhangi bir .exe"ye düşebiliyor. İmza kontrolü **ürün kararıyla yok** (bkz. G-1) | Güncelleyici | Temizlik |
| 2 | Kaldırıcı isim benzerliğiyle başka uygulamaların klasör ve registry anahtarlarını "%100 güvenli" siliyor; sağ tıkla İndirilenler/Masaüstü klasörünün tamamı silinecekler listesine giriyor | Kaldırıcı | Veri kaybı |
| 3 | Kaldırıcı `C:\Windows` altındaki süreçleri öldürebiliyor (sağ tık → explorer.exe) | Kaldırıcı | Sistem hasarı |
| 4 | Kurulum Nöbetçisi'nin geri alma fonksiyonu, kurulumdan önce var olan dosyaları da siliyor (şu an UI'a bağlı değil) | Nöbetçi | Veri kaybı (gizli) |
| 5 | Mağaza, üçüncü taraf ikilileri (abbodi1406 VC++ AIO, TechPowerUp) sabit `%TEMP%` adına indirip imza kontrolü olmadan yönetici olarak çalıştırıyor | Mağaza | Güvenlik |
| 6 | "Yönetici kısayolu" özelliği, kullanıcının yazabildiği bir exe'ye `/rl HIGHEST` görev açıyor. Exe değiştirilirse sessiz UAC atlatma olur | Bağlam Menüsü Kısayolları | Güvenlik |
| 7 | "Güvenilir yayıncı" kontrolü alt dize eşleşmesiyle yapılıyor (`"AMD"` "Hamdi Yazılım" ile eşleşir); sahte imzacı "güvenilir" görünür | Tehdit Analizörü | Güvenlik |
| 8 | CPU ve GPU sıcaklığı uyduruluyor: `38 + CPU% × 0.38` | Kontrol Paneli / Telemetri | Yanıltıcı veri |
| 9 | İnce ayar servisleri `SetRegistry*` hatalarını yutuyor ve `Apply` her durumda `true` dönüyor. Yönetici değilken HKLM yazılamasa da UI "uygulandı" diyor | Windows Tweaker (7 servis) | Kesin hata |
| 10 | Başlangıç Yöneticisi 32 bit HKLM girdilerini yanlış anahtara yazıyor (`WOW6432Node\...\StartupApproved\Run`; doğrusu `StartupApproved\Run32`); devre dışı bırakma çalışmıyor | Başlangıç | Kesin hata |
| 11 | Optimizer her 3 sn'de ~300 `Process` nesnesi oluşturuyor ve dispose etmiyor → handle sızıntısı. `CpuTracker` sözlüğü hiç temizlenmiyor | Optimizer | Kaynak sızıntısı |
| 12 | Hiçbir CI iş akışı testleri çalıştırmıyor; yalnızca tag'de yayın derleniyor | Altyapı | Kalite |

---

## 1. Güvenlik bulguları

### G-1 Güncelleyici (ürün kararı: imza kontrolü YOK)

- **Karar:** Güncelleme paketinde imza ya da sertifika doğrulaması **yapılmayacak.** Bu, v3.17.2'deki "sürtünmesiz güncelleme" kararının devamıdır. Uygulayıcı bu maddeyi imza kontrolü eklemek için **kullanmamalı.**
- **Kabul edilen risk:** GitHub hesabı ya da bir release asset'i ele geçirilirse güncelleme kanalından gelen dosya doğrulanmadan yönetici olarak çalışır. Tek koruma alan adı kontrolü (`AutoUpdateService.IsTrustedDownloadUrl`). Bu nedenle GitHub hesabında 2FA açık olmalı, release yayınlama yetkisi yalnızca CI'da olmalı.
- **Kararla çelişmeyen, yapılacak küçük işler:**
  1. `Services/GitHubUpdateService.cs` içindeki `DownloadAndApplyUpdateAsync` hiçbir yerden çağrılmıyor. Alan adı kontrolü yok ve PowerShell komutuna yolu doğrudan gömüyor. Metot silinsin, servis yalnızca sürüm sorgusu için kalsın ya da `AutoUpdateService` ile birleştirilsin.
  2. Asset seçimindeki "herhangi bir .exe" yedeği (`AutoUpdateService.cs:176-185`) kaldırılsın. Yalnızca `Bakim-v{X}-Setup.exe` deseni kabul edilsin; release'e yanlışlıkla eklenen başka bir exe çalıştırılmasın.
  3. İndirme boyutu API'deki `size` alanıyla karşılaştırılsın; eksik ya da yarım indirilen dosya çalıştırılmasın. Bu bir güvenlik kontrolü değil, bozuk indirme kontrolüdür.
- **İsteğe bağlı (sürtünme eklemez):** CI her release'e `SHA256SUMS.txt` ekleyebilir; güncelleyici indirilen dosyanın hash'ini bununla karşılaştırır. Sertifika gerektirmez ve kullanıcıya hiçbir şey sormaz. **Kullanıcı onayı olmadan uygulanmasın.**

### G-2 Mağaza: üçüncü taraf ikililer doğrulanmadan yönetici olarak çalışıyor (Kesin, Yüksek)

- **Yer:** `Services/StoreService.cs:960-1100` (VC++ AIO: `%TEMP%\VisualCppRedist_AIO_x86_x64.exe`, `/ai /gm2`), `:1120-1160` (TechPowerUp sayfa kazıma + POST ile 302 yönlendirme yakalama), `:1178-1215` (`dxwebsetup.exe`, `runas`).
- **Sorunlar:**
  - Dosya adları **sabit** ve `%TEMP%`'te. Aynı kullanıcı bağlamındaki bir zararlı, indirme ile yönetici çalıştırma arasındaki boşlukta dosyayı değiştirebilir (TOCTOU). Kullanıcı UAC'ye "Bakım istiyor" diye onay verir.
  - İmza kontrolü yok.
  - Kurulum başarı durumu çıkış koduna bakılmadan `Installed` yapılıyor.
- **Çözüm:**
  - Rastgele adlı ve yalnızca kullanıcıya ACL'li bir staging klasörü kullanılsın (`%LocalAppData%\Bakim\Downloads\{guid}`).
  - Çalıştırmadan önce imza doğrulansın: `dxwebsetup` için imzacı "Microsoft Corporation". İmzasız üçüncü taraf paketler için bilinen SHA-256 sabitlensin ya da winget'e yönlendirilsin.
  - Çıkış kodu değerlendirilsin.
  - TechPowerUp kazıma kaldırılsın; kırılgan ve güvenilmez bir kaynak.

### G-3 "Yönetici olarak çalıştır" görev kısayolları UAC'yi deliyor (Kesin, Yüksek)

- **Yer:** `Services/ContextMenuShortcutsService.cs:195-235`.
- **Sorunlar:**
  - Herhangi bir exe için `/rl HIGHEST` zamanlanmış görev oluşturuluyor ve masaüstüne bu görevi tetikleyen bir `.vbs` bırakılıyor. Hedef exe kullanıcının yazabildiği bir yerdeyse (İndirilenler, AppData), orta bütünlükteki bir zararlı exe'yi değiştirip `schtasks /run` ile **UAC istemi olmadan** yönetici kodu çalıştırabilir.
  - VBScript, Windows 11 24H2 itibarıyla isteğe bağlı bir özellik ve kaldırılma sürecinde; kısayollar ileride çalışmayacak.
- **Çözüm:**
  - Hedef yalnızca yöneticiye yazılabilir konumlarda olabilsin (`Program Files`, `Windows`). ACL kontrolü `FileSystemAclExtensions.GetAccessControl` ile yapılsın; `Users`/`Authenticated Users` grubunda yazma hakkı varsa reddedilsin.
  - `.vbs` yerine `schtasks.exe /run /tn "…"` hedefli bir `.lnk` oluşturulsun.
  - Oluşturulan görevler Ayarlar'da listelensin ve toplu silinebilsin.

### G-4 Güvenilir yayıncı alt dize eşleşmesiyle belirleniyor (Kesin, Orta)

- **Yer:** `Services/FileThreatAnalyzerService.cs:32-36`, `:182`.
- **Sorun:** `signerName.Contains("AMD")`, `Contains("Intel")` ve `Contains("Apple")` gibi kontroller "Hamdi Yazılım", "Intellisoft" ya da "Applet Games" imzacılarını da güvenilir sayıyor ve risk puanını düşürüyor.
- **Çözüm:** İmzacı CN değeri tam eşitlikle karşılaştırılsın. Liste: "Microsoft Corporation", "Microsoft Windows", "Google LLC", "NVIDIA Corporation", "Intel Corporation", "Advanced Micro Devices, Inc.", "Valve Corp.", "Apple Inc.", "Adobe Inc.", "Mozilla Corporation", "Discord Inc.", "Spotify AB", "Oracle America, Inc.", "GitHub, Inc.", "Epic Games Inc.". Mümkünse yayıncı CA zinciri de kontrol edilsin. Tam eşleşme listesi için birim testi yazılsın.
- **Ek temizlik:** Aynı dosyadaki `WINTRUST_*` yapıları artık kullanılmıyor (imza denetimi `SignatureInspector`'a taşınmış). Ölü kod silinsin. `dwProvFlags = 0x40 | 0x10` çelişkili bayraklar içeriyor (CHAIN + NONE).

### G-5 Güvenliği azaltan ince ayarlar uyarısız (Kesin, Orta)

- **Yer:** `Services/BehaviorTweaksService.cs:123-135` (`SaveZoneInformation`: indirilen dosyaların MOTW'si kapanır), `:168-181` (SmartScreen kapatma), `:202-212` ve `:885-903` (Windows Update ve BITS'i tamamen durdurma).
- **Sorun:** Bu ayarlar diğer ince ayarlarla aynı görünümde ve risk etiketi taşımıyor. Windows Update kapatılınca `WaaSMedicSvc` bunu geri açar, ya da açamazsa sistem yamasız kalır.
- **Çözüm:**
  - `SystemTweakItem`'a `SecurityImpact` (Yok/Düşük/Yüksek) alanı eklensin. Yüksek olanlar kırmızı "Güvenliği azaltır" rozeti ve ayrı bir onay diyaloğu alsın.
  - "Tümünü önerilenle uygula" akışı bu ayarları **asla** içermesin.
  - Windows Update için tamamen kapatma yerine "güncellemeleri 5 hafta duraklat" (`PauseUpdatesExpiryTime`) seçeneği sunulsun.
  - MOTW'nin kapatılması Kurulum Nöbetçisi'nin "indirildiği site" özelliğini de bozar; bu durum UI'da belirtilsin.

### G-6 TrustedInstaller çalıştırıcısı sessizce yöneticiye düşüyor (Kesin, Düşük)

- **Yer:** `Services/SystemToolsService.cs:163-200`.
- **Sorun:** TrustedInstaller süreci bulunamazsa program yalnızca yönetici olarak başlatılıyor ve kullanıcı TI yetkisiyle çalıştığını sanıyor.
- **Çözüm:** Yedek yola düşüldüğünde UI'da açıkça belirtilsin.

### G-7 Kurulum yapılandırması

- **Yer:** `Bakim_Setup.iss:62-63`.
- **Sorun:** `AppCompatFlags\Layers = "~ RUNASADMIN"` hem HKLM'e hem HKCU'ya yazılıyor. Bu yüzden Bakım'ın her açılışı (sağ tık dahil) UAC istiyor. Manifest ise `asInvoker` ve `AdminElevationService` "Yönetici Ol" akışıyla tasarlanmış; iki model çelişiyor.
- **Çözüm (karar gerekli):**
  - (a) RUNASADMIN kaldırılsın; açılış otomatik başlatma görevi (`/rl HIGHEST`) ile yönetici olsun, sağ tık `BakimShell.exe` üzerinden iletilsin (bkz. Kaldırıcı planı C3). **Önerilen seçenek bu.**
  - (b) Manifest `requireAdministrator` yapılsın ve model tekleştirilsin.

---

## 2. Dürüstlük: yanlış başarı ve uydurma veri

Bir bakım aracında en değerli şey güvendir. Bu başlıktaki her madde kullanıcıya yanlış bilgi veriyor.

| # | Bulgu | Yer | Çözüm |
|---|---|---|---|
| D-1 | CPU ve GPU sıcaklığı formülle üretiliyor (Kesin) | `Services/TelemetryService.cs:424-446`; gösterim `Views/Modules/DashboardModuleView.xaml:323`, `:555` | Gerçek kaynak kullanılsın: GPU için NVAPI/ADL ya da `GpuInfoProvider` (varsa). CPU için `MSAcpi_ThermalZoneTemperature` (WMI, yönetici, çoğu cihazda yok) ya da LibreHardwareMonitor. Ölçüm yoksa "—" ve "Sensör okunamadı" gösterilsin. **Tahmin asla ölçüm gibi gösterilmesin.** |
| D-2 | Disk bilgisi alınamazsa sahte değer dönüyor: `"C:"`, `%60` (Kesin) | `Services/SystemCleanService.cs:86-95` | Hata durumu model üzerinden UI'a taşınsın |
| D-3 | 7 ince ayar servisi yazma hatalarını yutuyor ve `true` dönüyor (Kesin) | `BehaviorTweaksService.cs:927-945` ve aynı yardımcılar `BootLogon`, `DesktopTaskbar`, `Edge`, `FileExplorer`, `PrivacyDebloat`, `SettingsControlPanel` | Ortak bir `RegistryWriter` yazılsın: `bool` + hata nedeni dönsün. `ApplyTweakAsync` uygulamadan sonra değeri **okuyup doğrulasın** ("yaz → oku → karşılaştır"). HKLM gerektiren ayarlar yönetici değilken devre dışı görünsün ve "Yönetici gerekli" rozeti alsın. |
| D-4 | Bloatware kaldırma 10 sn sonra sonuca bakmadan `IsInstalled=false` ve `true` dönüyor (Kesin) | `Services/PrivacyDebloatService.cs:338-362` | Çıkış kodu ve ardından `Get-AppxPackage` ile doğrulama yapılsın. Yalnızca geçerli kullanıcıdan kaldırıldığı (provisioned paketin yeni kullanıcılar ve büyük güncellemelerde geri geleceği) UI'da belirtilsin. İsteğe bağlı olarak `Remove-AppxProvisionedPackage -Online` eklensin. |
| D-5 | Mağaza kurulumları çıkış koduna bakılmadan "Kuruldu" işaretleniyor (Kesin) | `StoreService.cs:1210-1215` ve benzerleri | Çıkış kodu ile winget sonucu değerlendirilsin; mümkünse Uninstall kaydı kontrol edilsin |
| D-6 | RAM "serbest bırakıldı" değeri `EmptyWorkingSet` sonrasındaki anlık boş RAM farkı; saniyeler içinde geri dolar (Kesin) | `SystemCleanService.cs:805-845`, `GameModeService.cs:41-48` | Metrik kaldırılsın ya da "geçici" olarak etiketlensin. Bkz. P-3 |
| D-7 | Oyun Modu logu "arka plan servisleri donduruldu" diyor; gerçekte yalnızca Bakım'ın kendi bakım döngüsü duruyor (Kesin) | `GameModeService.cs:36` | Metin düzeltilsin ya da gerçekten servis/görev yönetimi eklensin |
| D-8 | Kaldırıcı: "%100 Güvenli" etiketi isim eşleşmesine veriliyor; "kayıt defteri yedeği" gerçekte yedek değil (Kesin) | bkz. `UNINSTALLER_V2_PLAN.md` U-P0-1, U-P0-7 | Planda |
| D-9 | Yeniden TRIM komutu 10 sn sonra `ExitCode` okuyor; komut sürüyorsa istisna yakalanıp "başarısız" sayılıyor ama işlem arka planda devam ediyor. HDD'de `-ReTrim` zaten desteklenmiyor (Kesin) | `Services/SystemInfoService.cs:800-815` | Medya türü (`MSFT_PhysicalDisk.MediaType`) kontrol edilsin, zaman aşımı uzatılsın, sonuç asenkron raporlansın |

---

## 3. Kesin hatalar (işlevsel)

| # | Hata | Yer | Düzeltme |
|---|---|---|---|
| H-1 | Başlangıç: 32 bit HKLM Run girdileri yanlış anahtara yazılıyor, devre dışı bırakma etkisiz | `Services/StartupService.cs:95-99` | `StartupApproved\Run32` kullanılsın (HKLM\Software\Microsoft\...\Explorer\StartupApproved\Run32). Ayrıca Windows'un yazdığı biçimde bayt 4-11'e FILETIME zaman damgası yazılsın. |
| H-2 | Temizleyici: Firefox kategorisi hiçbir şey bulmuyor | `SystemCleanService.cs:266-275` + `IsSafeTarget` `:877-886` | Kategori `Profiles` altındaki tüm dosyaları listeliyor, fakat güvenli hedef listesi `\cache\` istiyor. Firefox'un klasör adı ise `cache2`. Hedef `Profiles\*\cache2`, `startupCache`, `thumbnails` olarak açıkça tanımlansın ve `IsSafeTarget`'e `\cache2\` eklensin. Test yazılsın. |
| H-3 | Temizleyici: Spotify "Storage" varsayılan olarak seçili; **çevrimdışı indirilmiş şarkılar** silinir | `SystemCleanService.cs:331-340` | Varsayılan seçim kaldırılsın, açıklama "Çevrimdışı indirilen şarkılar da silinir" olsun |
| H-4 | Temizleyici: `SoftwareDistribution\Download` `wuauserv` ve `BITS` durdurulmadan temizleniyor; süren bir güncelleme bozulabilir. Temp dosyalarında yaş filtresi yok; o anda çalışan bir kurulumun temp dosyaları silinebilir | `SystemCleanService.cs:126-150`, `:380-440` | Servisler durdurulsun ve sonra yeniden başlatılsın. Temp için "24 saatten eski" filtresi eklensin (`LastWriteTimeUtc`). |
| H-5 | Temizleyici `IsSafeTarget`: `\temp\` ve `\cache\` alt dizelerini **herhangi bir yerde** kabul ediyor; `\telegram desktop\` altındaki her şey güvenli sayılıyor (tdata oturum anahtarları dahil) | `SystemCleanService.cs:852-887` | İzin kontrolü kategori kök yoluna bağlansın: dosya, **kendi kategorisinin** `TargetPath`/`AdditionalPaths` kökleri altında olmalı. Genel alt dize allowlist'i kaldırılsın. |
| H-6 | Optimizer: `Process` nesneleri dispose edilmiyor (handle sızıntısı), `CpuTracker` PID'e göre tutuluyor ve temizlenmiyor | `ViewModels/OptimizerViewModel.cs:585-700` | `try/finally p.Dispose()` eklensin. Anahtar `(PID, StartTime)` olsun; her turda artık yaşamayan süreçler temizlensin. |
| H-7 | Optimizer ve Temizleyici: korunan süreç listeleri eksik ve birbirinden farklı (`winlogon`, `fontdrvhost`, `sihost`, `lsaiso`, `Registry`, `Memory Compression`, `audiodg` yok). Kullanıcı winlogon'u askıya alabilir | `OptimizerViewModel.cs:589`, `SystemCleanService.cs:657`, `:753`, `:809` | Tek bir `CriticalProcessPolicy` sınıfı yazılsın (Kaldırıcı planı A3 ile ortak). Görüntü yolu `Windows` altında olan her süreç "sistem" sayılsın. |
| H-8 | Oyun Modu: kapatıldığında güç planı her zaman "Dengeli"ye dönüyor; kullanıcının önceki planı kayboluyor. Bakım çökerse Yüksek Performans açık kalıyor. Modern Standby cihazlarda Yüksek Performans planı yok, hata sessizce yutuluyor | `Services/GameModeService.cs:39`, `:65` | Etkinleştirmeden önce `powercfg /getactivescheme` ile mevcut plan kaydedilsin (ayar dosyasına da yazılsın) ve kapatırken geri yüklensin. Açılışta "Oyun Modu yarım kalmış" durumu onarılsın. Plan yoksa kullanıcıya bildirilsin. |
| H-9 | Boş klasör avcısı: sonuçlar varsayılan seçili; AppData/ProgramData altındaki uygulama klasörleri de siliniyor. Kök klasör boşsa kökün kendisi de siliniyor. OneDrive yer tutucuları ve junction'lar ayırt edilmiyor | `Services/DuplicateFinderService.cs:380-440`, `:480-505` | Varsayılan seçim yalnızca kullanıcı veri klasörlerinde yapılsın. Gizli klasörler, `.` ile başlayanlar, AppData ve ProgramData hariç tutulsun. Kök asla silinmesin. `FileAttributes.ReparsePoint` ve `Offline`/`RecallOnDataAccess (0x400000)` olan girdiler atlansın. |
| H-10 | Yinelenen dosya taraması OneDrive yalnızca-bulut dosyalarını okurken **hepsini indiriyor** | `DuplicateFinderService.cs:238-300` | `RecallOnDataAccess` ve `Offline` öznitelikli dosyalar hash'lenmesin; "bulutta" olarak gösterilsin |
| H-11 | Gizlilik ve Kaldırıcı geri yükleme noktası: 3 ayrı uygulama var; `UseShellExecute=true` + `CreateNoWindow` birlikte kullanıldığı için PowerShell penceresi görünüyor; 24 saat sınırı sessizce başarısızlığa yol açıyor | `PrivacyDebloatService.cs:371-395`, `DeepUninstallerService.cs:77-105` | Tek bir `RestorePointService` (Kaldırıcı planı A8) |
| H-12 | Geri yükleme noktası PowerShell ile `runas` açıyor; zaten yöneticiyken de UAC'yi tekrar tetikliyor | aynı | WMI `SystemRestore.CreateRestorePoint` |
| H-13 | İnce ayar anlık görüntüsü (`TweaksSnapshotService`) yalnızca bool durumu tutuyor. Geri yükleme "değeri sil" yapıyor ve kullanıcının orijinal (varsayılan olmayan) değeri kayboluyor | `Services/TweaksSnapshotService.cs:142-230` | Değer düzeyinde snapshot tutulsun: anahtar, değer adı, tip ve veri, "yoktu" bilgisi dahil. Geri yükleme bu kayda göre yazsın ya da silsin. |
| H-14 | `LoginWindow`, `AuthService` ve `CredentialStorageService` DI'da kayıtlı ama uygulama hiç giriş ekranı göstermiyor (ölü özellik). Giriş ekranındaki "Hesapları sıfırla" parolasız çalışıyor; özellik açılırsa koruma anlamsız | `App.xaml.cs:415`, `ViewModels/LoginViewModel.cs:225` | Karar gerekli: kaldırılsın **(önerilen)** ya da gerçek bir amaçla (örn. ayarları kilitleme) yeniden tasarlansın. "Beni hatırla" parolanın kendisini saklıyor; token yaklaşımı gerekir. |
| H-15 | Sürüm numarası 7 yerde sabit yazılı (`csproj`, `app.manifest`, `iss`, `AutoUpdateService` ×2, `GitHubUpdateService`, `SettingsViewModel`); `GitHubUpdateService.UpdateCheckResult.CurrentVersion = "2.5.0"` | çeşitli | Tek kaynak: `Directory.Build.props` → `Version`. `iss` için CI `/DMyAppVersion=` geçsin. Manifest sürümü derlemede üretilsin ya da `0.0.0.0` bırakılsın. Kod `Assembly.GetName().Version` okusun. |
| H-16 | `App.xaml.cs` `--uninstall-target` modunda da Kurulum Nöbetçisi ve arka plan bakımı başlıyor | `App.xaml.cs:143-149` vs `:169-187` | Kaldırıcı planı C1 |
| H-17 | Kurulum Nöbetçisi ve Kaldırıcı'daki P0 listeleri | ilgili planlar | ilgili planlar |

---

## 4. Performans ve kaynak kullanımı

| # | Bulgu | Yer | Öneri |
|---|---|---|---|
| P-1 | Ana penceredeki mini telemetri, pencere gizliyken (tepside) de 3 sn'de bir WMI `Win32_PerfFormattedData` sorgusu yapıyor. Kontrol Paneli açıkken aynı örnekleme ikinci kez yapılıyor | `ViewModels/MainViewModel.cs:74-80`, `Services/TelemetryService.cs:378` | Tek bir `TelemetryHub` her N sn'de bir örnekleyip abonelere yayınlasın. Pencere gizliyken ve tepsi menüsü kapalıyken örnekleme dursun. WMI perf sınıfları yerine `PdhCollectQueryData` (PDH) ya da `GetSystemTimes` + `GlobalMemoryStatusEx` kullanılsın; çok daha ucuz. |
| P-2 | Timer tick'lerinde yeniden girme koruması tutarsız: Optimizer'da `IsBusy` var, Kontrol Paneli, Sistem Bilgisi ve Ağ'da yok. Yavaş bir tick bir sonrakiyle üst üste binebiliyor | `DashboardViewModel.cs:56`, `SystemInfoViewModel.cs:63`, `NetworkMonitorViewModel.cs:41` | Ortak bir `AsyncTimer` yardımcısı yazılsın: tick sürerken yeni tick atlansın |
| P-3 | "RAM temizleme" ve "Oyun Modu", `EmptyWorkingSet` ile **tüm** süreçlerin çalışma kümesini boşaltıyor. Windows bu sayfaları hemen geri yüklüyor (sert sayfa hatası), sonuç takılma ve disk I/O. Oyun Modu bunu oyun başlamadan hemen önce yapıyor | `SystemCleanService.cs:805-845`, `GameModeService.cs:41-48`, `BackgroundMaintenanceService` | Varsayılan olarak kapatılsın. Gerçek bir fayda isteniyorsa yalnızca **standby list** temizliği (`NtSetSystemInformation` `MemoryPurgeStandbyList`, yönetici) ve yalnızca kullanıcının seçtiği süreçler için yapılsın. Oyun Modu için daha faydalı adımlar: bildirim odağını açmak, belirli arka plan uygulamalarını (kullanıcı listesi) askıya almak, güç planı. |
| P-4 | Kaldırıcı listesi, `EstimatedSize=0` olan her uygulama için klasör boyutunu özyinelemeli hesaplıyor; açılış yavaş | `Services/UninstallerService.cs:78-82` | Kaldırıcı planı D1 |
| P-5 | Kurulum Nöbetçisi: 1.5 sn'de bir tüm süreçleri listeleme + her süreçte `MainModule` | bkz. Nöbetçi planı | Nöbetçi planı Faz 1 |
| P-6 | Temizleyici silme döngüsünde her 10 dosyada bir `Thread.Sleep(3)` var ve her dosya için `IProgress` raporu gönderiliyor (UI dispatcher'ını boğar) | `SystemCleanService.cs:470-480` | İlerleme 100 ms'de bir toplanarak raporlansın; `Sleep` kaldırılsın |
| P-7 | Büyük XAML görünümleri (Sistem Bilgisi 2152 satır, Mağaza 1285, Ağ 1255) tek parça; bazı listelerde sanallaştırma kontrol edilmeli | `Views/Modules/*` | `VirtualizingStackPanel.IsVirtualizing="True"` + `VirtualizationMode="Recycling"`; görünümler alt `UserControl`'lere bölünsün |
| P-8 | Hız testi paralel 3 × 50 MB indiriyor (test başına 150 MB) | `Services/NetworkMonitorService.cs:845-847` | Ölçülü bağlantı uyarısı eklensin (`NetworkInformation.GetInternetConnectionProfile().GetConnectionCost()`). Süre tabanlı durdurma yapılsın (ör. 8 sn). |

---

## 5. Mimari ve kod kalitesi

| # | Bulgu | Öneri |
|---|---|---|
| M-1 | **Hata yutma:** ≈ 150+ `catch { }` (en çok `SetupSentinelService` 16, `AutorunsScannerEngine` 13, `SystemInfoService` 11, `StoreService` 11, `FileThreatAnalyzerService` 11). Hatalar sessizce "başarılı" ya da "boş" sonuca dönüşüyor. | Kural: yeni kodda `catch { }` yasak. Mevcutlar modül modül `ILogService.Debug/Warn` + sonuç nesnesine dönüştürülsün. Bir Roslyn analyzer ya da `Tools/` altında basit bir betik ile CI'da sayı artışı engellensin. |
| M-2 | **Kopya yardımcılar:** `SetRegistryDword/String` 7 kopya, `DeleteRegistryKey` 4+, `FormatBytes` 6+, geri yükleme noktası 3, `KillProcessesForApp` 2, `SHFileOperation` 2, korunan süreç listesi 4. | Ortak `Helpers/` katmanı: `RegistryWriter`, `RegistryPath`, `ByteFormatter`, `RestorePointService`, `CriticalProcessPolicy`, `RecycleBin`, `ProcessImagePath` (Kaldırıcı planı A2/A3 ile aynı sınıflar). |
| M-3 | **Çift motorlar:** `AutoUpdateService` + `GitHubUpdateService`; `StartupService` + `AutorunsScannerEngine` (ikisi de Run girdilerini yönetiyor); `UninstallerService` + `DeepUninstallerService` + `ResidualScannerEngine`; `LeftoverItem` + `ResidualItem`. | Her alan için tek servis kalsın. `StartupService`, Autoruns motorunun Run/StartupFolder kategorilerini kullanan ince bir görünüm olsun. |
| M-4 | **ViewModel'lerde UI:** 120+ `MessageBox.Show` (Kaldırıcı 21, Tehdit 17, Analizör 15, Optimizer 13, Gizlilik 12, Ayarlar 12…). Diyalog ve pencere nesneleri VM içinde oluşturuluyor. | `IDialogService` + `IWindowService`. Toast/InfoBar için mevcut `ToastNotificationItem` modeli kullanılsın. Böylece VM'ler test edilebilir hale gelir. |
| M-5 | **Dev sınıflar:** `SettingsViewModel` 1600 satır (içinde 42 sürümlük değişiklik günlüğü metni gömülü), `SystemInfoViewModel` 1582, `StoreService` 1375, `FileThreatAnalyzerService` 1316, `NetworkMonitorService` 1209. | Değişiklik günlüğü `Assets/changelog.json` (gömülü kaynak) dosyasına taşınsın; CHANGELOG_LATEST.md'den CI ile üretilebilir. Sistem Bilgisi bölümlere ayrılsın (CPU/GPU/Disk/Ağ/OS alt VM'leri). `StoreService` katalog, indirici ve kurucu olarak üçe bölünsün. |
| M-6 | **Statik servisler:** `AutoUpdateService` statik, `CredentialStorageService` statik, `ResidualScannerEngine.ScanResidualsStaticAsync` DI dışı örnek oluşturuyor, `ShellContextMenuService` `App` içinde `new` ile oluşturuluyor. | DI'a taşınsın (`--register-contextmenu` gibi komut modları için hafif bir provider yeterli). |
| M-7 | **Yönetici gereksinimi dağınık:** Her servis kendi bildiği gibi `runas` açıyor (PowerShell, schtasks, sc, powercfg…). | `IElevationBroker`: yönetici değilken yönetici gerektiren işlemler **tek** bir UAC istemiyle toplu çalıştırılsın (`Bakim.exe --elevated-batch <json>`). UI "Yönetici gerekli" rozetleriyle bunu önceden göstersin. |
| M-8 | **Süreç çalıştırma kalıbı dağınık:** 40+ yerde `ProcessStartInfo` elle kuruluyor; çıkış kodu kontrolü, zaman aşımı ve çıktı okuma tutarsız. Bazı yerlerde `WaitForExit(ms)` sonrası `ExitCode` okunuyor ve süreç bitmemişse istisna atılıyor. | `ProcessRunner.RunAsync(file, args[], timeout, captureOutput)` → `ProcessResult(ExitCode, StdOut, StdErr, TimedOut)`. Argümanlar `ArgumentList` ile verilsin (tırnak ve enjeksiyon güvenliği). |
| M-9 | **Sabit renkler:** C# modellerinde ve VM'lerde 83 hex renk (ör. `ResidualItem.RiskBadgeBackground = "#1510B981"`); `SystemInfoModuleView.xaml` 55 hex; `TrayFlyoutWindow.xaml` sabit koyu arka plan. Açık temada kontrast bozuluyor. | Anlamsal `Intent` → brush eşleşmesi zaten var (`Models/Intent.cs`); tüm hex değerler token'lara taşınsın. `Tools/verify-tokens.py`'ye "C# içinde hex renk yok" ve "Modules/*.xaml içinde hex yok" kuralları eklensin. |
| M-10 | **Yazı boyutları:** 1.200+ sabit `FontSize` değeri; en sık kullanılanlar 11 (368 yer) ve 10 (112 yer). `Themes/Tokens/Typography.xaml` var ama az kullanılıyor. | Tip ölçeği (Caption 12, Body 14, BodyStrong 14, Subtitle 20, Title 28) stillerle uygulansın. 10–11 px yalnızca rozetlerde kalsın. `verify-tokens.py`'ye `FontSize="10"` yasağı eklensin. |
| M-11 | **Test ve CI:** Testler var (≈ 20 dosya) ama CI hiçbirini çalıştırmıyor. Bazı testler servis yerine mantığın kopyasını test ediyor (`SetupSentinelTests`). | `.github/workflows/ci.yml`: push ve PR'da `windows-latest` üzerinde build, `dotnet test`, `verify-tokens`, `verify-symbols` ve `UiSmokeTests`. Kapsam raporu (coverlet) eklensin. |
| M-12 | **Günlükleme:** Hatalar çoğu yerde loglanmıyor (M-1). Kullanıcıya "Tanılama paketi oluştur" seçeneği yok. | Ayarlar'a "Tanılama paketini dışa aktar" eklensin: son 3 günün logları, ayarlar (sırlar maskelenmiş), sistem özeti; hepsi zip. |

---

## 6. Tasarım ve UX geliştirmeleri

### 6.1 Genel ilkeler

1. **Güven arayüzü:** Her yıkıcı işlem şu sırayı izlesin: önizleme → onay → sonuç raporu → geri al. Temizleyici, Kaldırıcı, Boş Klasör, İnce Ayar ve Gizlilik modüllerinin hepsi aynı "Sonuç Kartı" bileşenini kullansın: başarılı / atlanan / başarısız sayıları, nedenleri ve Geri Al düğmesi.
2. **Risk dili tek olsun:** "%100 Güvenli" gibi mutlak ifadeler kaldırılsın. Ortak rozet seti: *Güvenli, Dikkat, Güvenliği azaltır, Yönetici gerekli, Yeniden başlatma gerekli, Geri alınabilir, Geri alınamaz*.
3. **Ölçüm ile tahmin ayrımı:** Tahmin edilen her değer "≈" ve "tahmini" etiketi taşısın; ölçülemeyen değer "—" olarak gösterilsin.
4. **Yönetici durumu görünür olsun:** Başlık çubuğunda kalkan simgesi. Yönetici gerektiren kartlarda kilit rozeti ve "Yönetici olarak yeniden başlat" düğmesi.
5. **Sessiz arka plan:** Tepsideyken örnekleme durur. Bildirimler odak çalmaz. Tam ekran uygulama ya da oyun varken bildirimler ertelenir (`SHQueryUserNotificationState`).

### 6.2 Modül bazlı UX önerileri

| Modül | Öneri |
|---|---|
| **Kontrol Paneli** | Gerçek sıcaklık yoksa kart gizlensin ya da "sensör yok" gösterilsin. "Sağlık puanı" gibi özet metrikler neyin ölçüldüğünü açıklayan bir ipucu içersin. Önerilen eylemler (Temizle, Başlangıcı düzenle) ölçüme dayalı olsun: "Başlangıçta 14 program, 6'sı imzasız". |
| **Temizleyici** | Kategori kartlarında "neyi siler / neyi silmez" açıklaması olsun. Varsayılan seçimler temkinli olsun (Spotify, Prefetch, WU indirmeleri seçili gelmesin). Silme işlemi Geri Dönüşüm'e gidebilsin (büyük temp hariç). Yaş filtresi (24 saat) ayarlanabilir olsun. Tarama sonucu dosya listesi klasöre göre gruplansın. |
| **Optimizer** | "RAM temizle" düğmesi kaldırılsın ya da "Standby belleği boşalt (yönetici)" olarak dürüstleştirilsin. Süreç listesinde imza ve yayıncı sütunu, "Dosya konumunu aç" ve "Analizörle tara" seçenekleri olsun. Sistem süreçleri gri ve kilitli gösterilsin. |
| **Oyun Modu** | Etkinleştirme ekranı neyin değişeceğini listelesin (güç planı, bildirimler, askıya alınacak uygulamalar). Kapatınca her şey önceki haline dönsün. Oyun başlatıldığında otomatik açılma seçeneği olsun (kullanıcının oyun listesi). |
| **Windows Tweaker** | Kartlarda "Mevcut değer → Yeni değer", güvenlik etkisi rozeti ve doğrulama sonucu (uygulandı / uygulanamadı: neden) gösterilsin. "Önerilenleri uygula" önce değişiklik listesini göstersin. Değer düzeyinde anlık görüntü ve "Fabrika ayarına dön" olsun. Ayar değişiminden sonra gerekiyorsa Explorer yeniden başlatma önerilsin. |
| **Gizlilik ve Bloatware** | Kaldırılan uygulamalar için "Microsoft Store'dan geri yükle" bağlantısı. "Yalnızca bu kullanıcı / tüm kullanıcılar" seçimi. Sonuç doğrulaması. |
| **Başlangıç** | Autoruns motoru ile birleşik tek liste: Run, Görevler, Servisler, Klasör. Etki tahmini: gerçek `BootTime` verisi, ya da Windows'un `StartupApproved` ve olay günlüğü (Diagnostics-Performance 100) verileri. İmzasız ve Temp'ten çalışan girdiler için uyarı. |
| **Kaldırıcı** | Bkz. `UNINSTALLER_V2_PLAN.md` (7 adımlı sihirbaz, kanıt tabanlı kalıntılar, kaldırma geçmişi). |
| **Analizör / Tehdit** | Risk puanının dökümü ("Neden 65?" → faktör listesi) zaten var; güvenilir yayıncı düzeltmesiyle birlikte "imzacı doğrulandı: tam eşleşme" gösterilsin. VirusTotal yüklemesinden önce gizlilik uyarısı ("dosya herkese açık olur") gösterilsin. |
| **Kurulum Nöbetçisi** | Bkz. `SENTINEL_V2_PLAN.md` (risk özetli bildirim, Kurulum Geçmişi). |
| **Ağ İzleyici** | Engellenen uygulamalar listesi ve tek tıkla kaldırma. Hız testi öncesi veri kullanımı uyarısı. Bağlantı satırlarında imza ve süreç yolu. |
| **Mağaza** | Kaynak rozeti (winget / resmi site / üçüncü taraf). Kurulum sonucunda çıkış kodu ve log bağlantısı. Üçüncü taraf paketlerde uyarı. |
| **Sistem Bilgisi** | Bölümlere ayrılmış gezinme (CPU, GPU, Bellek, Disk, Ağ, OS). "Kopyala" ve "Rapor olarak dışa aktar (HTML/TXT)". Sabit renkler token'a taşınsın. |
| **Ayarlar** | Değişiklik günlüğü ayrı bir sayfaya (dosyadan). Ayarları dışa ve içe aktarma. Tanılama paketi. Oluşturulan zamanlanmış görevler ve sağ tık kayıtlarının yönetimi. |
| **Tepsi** | Oyun Modu ve telemetri mini kartı zaten var; nöbetçi durumu ve son olaylar eklensin. Tepsi penceresi açık temaya uysun (sabit `#EE1E293B` kaldırılsın). |

### 6.3 Erişilebilirlik

- Tüm etkileşimli öğelerde `AutomationProperties.Name` bulunsun (`Tools/add-automation-names.py` CI'da kontrol modunda çalışsın).
- Klavye ile gezinme: sekme sırası, `AccessKey`, odak görselleri.
- Minimum yazı boyutu 12 px. Kontrast testi `Tests/Bakim.Tests/ThemeContrastTests.cs` mevcut; token dışı renklere de genişletilsin.
- Yüksek kontrast teması desteği (`SystemParameters.HighContrast`).

---

## 7. Yol haritası

Her sprint yaklaşık 1–2 hafta. "Nöbetçi" ve "Kaldırıcı" kodları kendi planlarındaki görev kodlarına işaret eder.

### Sprint 0: Altyapı (önce bu)

- CI iş akışı: push/PR → build, test, verify-tokens, verify-symbols (M-11).
- Tek sürüm kaynağı (H-15).
- Ortak yardımcılar: `ProcessRunner`, `RegistryWriter`, `RegistryPath`, `ByteFormatter`, `CriticalProcessPolicy`, `RecycleBin`, `ProcessImagePath`. Kaldırıcı A1–A3 ile aynı iş; tek seferde yapılsın.
- `catch { }` sayısı için CI eşiği (M-1); sayı artarsa build kırılsın.

### Sprint 1: Güvenlik

- G-1: Yalnızca ölü `GitHubUpdateService.DownloadAndApplyUpdateAsync`'in silinmesi, asset deseni sıkılaştırması, boyut kontrolü. (İmza kontrolü yok: ürün kararı.)
- G-2: Mağaza staging klasörü, imza/hash kontrolü, çıkış kodu.
- G-3: Yönetici kısayolu ACL kontrolü ve `.lnk`.
- G-4: Tam eşleşmeli güvenilir yayıncı listesi.
- G-7: RUNASADMIN kararı.
- Kaldırıcı A1–A8 (P0'lar).
- Nöbetçi 0.1 (revert güvenliği).

**Sürüm: v3.20.0 "Güvenlik Sürümü"**

### Sprint 2: Dürüstlük

- D-1 … D-9 (sahte sıcaklık, sahte disk, yazma doğrulamalı tweak'ler, gerçek kurulum sonuçları).
- G-5: Güvenlik etkisi rozetleri.
- 6.1'deki ortak "Sonuç Kartı" bileşeni.

**Sürüm: v3.20.x**

### Sprint 3: Kesin hatalar

- H-1 … H-14.
- Nöbetçi Faz 0 (kalan P0'lar).

**Sürüm: v3.21.0**

### Sprint 4: Performans ve mimari

- P-1 … P-8.
- M-2 … M-8: tek motorlar, `IDialogService`, `IElevationBroker`, `ProcessRunner` yayılımı.
- Kaldırıcı B3 (tek model).

**Sürüm: v3.22.0**

### Sprint 5: Tasarım sistemi

- M-9 ve M-10 (renk ve tipografi token'ları, doğrulayıcı kuralları).
- 6.3 Erişilebilirlik.
- Büyük görünümlerin bölünmesi (P-7, M-5).

**Sürüm: v3.23.0**

### Sprint 6: Modül UX'leri

- 6.2 tablosundaki öneriler; öncelik sırası Temizleyici, Tweaker, Başlangıç, Optimizer, Oyun Modu.

**Sürüm: v3.24.0**

### Paralel hat: Nöbetçi ve Kaldırıcı v2 özellikleri

- Kaldırıcı B1–B5, C1–C6, D1–D5.
- Nöbetçi Faz 1–7.
- Nöbetçi Faz 8 (servis mimarisi) → v4.0.

---

## 8. Doğrulama ve test planı

1. **Birim testleri** (her düzeltmeyle birlikte):
   - `StartupApproved\Run32` yol seçimi
   - Firefox `cache2` kapsamı
   - `IsSafeTarget` kategori köküne bağlı davranış
   - Güvenilir yayıncı tam eşleşmesi
   - Güncelleyici asset seçimi (yalnızca `Bakim-v{X}-Setup.exe`) ve boyut uyuşmazlığında reddetme
   - Tweak yaz-oku doğrulaması (HKCU test anahtarıyla)
   - Oyun Modu güç planı geri yükleme (`powercfg` çağrısı `IProcessRunner` sahtesiyle)
   - `CriticalProcessPolicy` tablosu
   - `ByteFormatter`
2. **Kanarya testleri** (Windows CI, yönetici): Kaldırıcı planı 4.2'deki kanarya yaklaşımı Temizleyici ve Boş Klasör için de uygulansın. Benzer adlı ve silinmemesi gereken dosya/klasörler oluşturulur, işlem çalıştırılır, kanaryaların durduğu doğrulanır.
3. **UI duman testleri:** Mevcut `Tests/Bakim.UiSmokeTests` CI'a eklensin. Her modül açılıp kapanır ve binding hatası (`PresentationTraceSources`) sayısı 0 olmalı.
4. **Elle test listesi** (sürüm öncesi): Windows 10 22H2 ve Windows 11 24H2, açık ve koyu tema, %100 ve %150 DPI, yönetici ve standart kullanıcı, dizüstü (Modern Standby) ve masaüstü.
5. **Performans ölçümü:** Tepsideyken 10 dk boyunca CPU ortalaması < %0,5 ve handle sayısı sabit olmalı (Optimizer sızıntı testi).

---

## 9. Karar bekleyen konular

| Konu | Seçenekler | Öneri |
|---|---|---|
| Güncelleme imza kontrolü (G-1) | Yok / SHA256SUMS / Authenticode | **Karar verildi: yok** |
| Yönetici modeli (G-7) | RUNASADMIN katmanı / `requireAdministrator` / asInvoker + görev + broker | asInvoker + otomatik başlatma görevi + `IElevationBroker` |
| Giriş ekranı (H-14) | Kaldır / yeniden tasarla | Kaldır |
| Sıcaklık kaynağı (D-1) | LibreHardwareMonitorLib (sürücü içerir; AV uyarısı riski) / yalnızca GPU vendor API / göstermeme | Kısa vadede göstermeme; orta vadede GPU vendor API |
| RAM temizleme (P-3) | Kaldır / standby list / olduğu gibi bırak ama dürüst etiketle | Standby list (yönetici) + varsayılan kapalı |
| Üçüncü taraf Mağaza paketleri (G-2) | Hash sabitleme / winget'e yönlendirme / kaldırma | winget'e yönlendirme; winget yoksa resmi Microsoft kaynağı |

---

## 10. Bu belgeyi bir yapay zekâ asistanına (Gemini vb.) uygulatırken

`docs/UNINSTALLER_V2_PLAN.md` bölüm 0'daki kurallar bu belgedeki bütün işler için de geçerli: sırayla ilerlenir, görev başına bir commit atılır, her görevden sonra build + test + doğrulayıcılar çalıştırılır, yasaklı API'ler yalnızca Safe servislerde kullanılır, `catch { }` yazılmaz.

Görev şablonu:

```
docs/GENEL_DENETIM_PLANI.md ve docs/UNINSTALLER_V2_PLAN.md bölüm 0'ı oku ve kurallara uy.
Şimdi yalnızca <KOD> maddesini uygula (örn. "G-1", "H-2", "D-3").
- Maddede belirtilen dosyalar dışına dokunma; gerekirse nedenini commit mesajına yaz.
- Maddenin "Çözüm" kısmındaki her adımı uygula ve bölüm 8'deki ilgili birim testini yaz.
- dotnet build, dotnet test, verify-tokens.py, verify-symbols.py geçmeli.
- Sonunda yaptıklarını ve yapamadıklarını madde madde raporla.
- Tek commit: "fix(<modül>): <KOD> - <kısa açıklama>".
```

Görevleri Sprint 0 → 1 → 2 sırasıyla ver. Sprint 1 bitmeden yeni özellik ekletme.
