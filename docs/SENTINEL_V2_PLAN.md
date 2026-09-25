# Kurulum Nöbetçisi v2 — Mühendislik Planı

> Kapsam: `SetupSentinelService`, `InstallerMonitorService`, `SetupDetectedFlyoutWindow`,
> `InstallationDeltaInspectionDialog` ve Analizör entegrasyonunun (v3.19.1) bir sonraki nesle taşınması.
> Hedef: "kurulumu yakala → ne yaptı söyle → riskliyse tara → gerekirse geri al" zincirini
> antivirüs/EDR ciddiyetinde, fakat kullanıcıyı rahatsız etmeden çalıştırmak.

---

## 0. Özet

Bugünkü motor fikir olarak doğru ama üç yerden kör:

1. **Tespit geç ve eksik.** 1.5 sn polling, `Process.MainModule` ile yol okuma (UAC ile yükselen kurulumlarda erişim reddedilir), isim bazlı çocuk süreç takibi.
2. **Yakalama sığ.** Registry'de yalnızca *anahtar adları* ve `Software` altında 2 seviye tutuluyor. Run girdileri *değer* olduğu, servisler de `SYSTEM\...` altında olduğu için **başlangıç kayıtları ve servisler pratikte hiç yakalanmıyor.** Bir antivirüs açısından en önemli iki sinyal bunlar.
3. **Geri alma tehlikeli.** `Changed` olayları "eklenen dosya" sayılıyor. Revert bunları siliyor. Kurulumdan önce var olan dosyalar ve o sırada başka uygulamaların yazdığı dosyalar silinebilir. Şu an UI'a bağlı değil, bağlanmadan önce düzeltilmesi şart.

v2 mimarisi: **Tetikleyici → Oturum → Sensörler → Tek olay akışı → Gürültü filtresi & atıf → Delta → Risk motoru → Rapor/UI → Aksiyonlar.**
Sıra: önce P0 hatalar ve test edilebilirlik (Faz 0), sonra tespit (Faz 1), yakalama (Faz 2), atıf (Faz 3), risk (Faz 4), aksiyonlar (Faz 5), UX (Faz 6). Yönetici gerektiren güçlü sensörler (USN Journal, ETW) en sonda, ayrı bir Windows servisiyle geliyor (Faz 8).

---

## 1. Mevcut durum röntgeni (v3.19.1)

Akış: `Timer(1.5 sn)` → `Process.GetProcesses()` + anahtar kelime → `FileSystemWatcher` (7 dizin) + pre-snapshot → takip edilen PID'ler ölünce 2 sn bekle → post-snapshot → `SetupDeltaReport` → flyout → "Analizörle Tara" → `AnalyzerViewModel.AnalyzeSpecificFilesAsync`.

### 1.1 Kritik hatalar (P0), önce bunlar

| # | Sorun | Yer | Etkisi |
|---|---|---|---|
| P0-1 | `Changed` olayları `AddedFiles`'a giriyor, `RevertReportAsync` bunları siliyor | `Services/SetupSentinelService.cs:354`, `:495`, `:666` | Önceden var olan (yalnızca değiştirilen) dosyalar ve aynı anda tarayıcı cache'i, OneDrive gibi başka uygulamaların yazdığı dosyalar silinebilir. **Veri kaybı.** |
| P0-2 | Yükseltilmiş (UAC) kurulumların yolu okunamayabiliyor | `SetupSentinelService.cs:214` (`proc.MainModule`) | Manifest `asInvoker` (`app.manifest:14`). Kurulum betiği `Bakim.exe`'ye `RUNASADMIN` uyumluluk katmanı yazdığı için (`Bakim_Setup.iss:62-63`) kurulu sürüm genellikle yönetici çalışır ve orada sorun çoğunlukla görünmez. Ama geliştirme derlemelerinde, katman yokken ve korumalı ya da farklı mimarideki süreçlerde `MainModule` erişim reddi verir. O durumda yalnızca adı tam olarak `setup`/`installer`/`msiexec`/`kurulum` olanlar yakalanır. `QueryFullProcessImageName` her durumda çalışır ve daha ucuzdur. |
| P0-3 | Pre-snapshot kurulum başladıktan sonra alınıyor | `:142` (polling) + `:315` | Tespit gecikmesi ve snapshot süresi boyunca yazılan registry anahtarları "zaten vardı" sayılıyor. Hızlı veya sessiz kurulumlarda (`msiexec /qn`) delta neredeyse boş çıkar. |
| P0-4 | Run girdileri ve servisler yakalanmıyor | `Services/InstallerMonitorService.cs:54-55` | Run kayıtları anahtar değil *değer*. `SYSTEM\CurrentControlSet\Services` hiç taranmıyor. `AddedServices` ve `AddedStartupEntries` pratikte hep boş. |
| P0-5 | Hive etiketi yanlış | `SetupSentinelService.cs:547` | Anahtarlar `LocalMachine\...` biçiminde kaydediliyor, kontrol ise `HKEY_LOCAL_MACHINE`/`HKLM` arıyor. Sonuçta her kayıt HKCU olarak etiketleniyor. |
| P0-6 | İki servis aynı klasöre farklı şemayla JSON yazıyor | `SetupSentinelService.cs:611` ve `InstallerMonitorService.cs:215` | `LoadSavedReports`, `SnapshotDelta` dosyalarını `SetupDeltaReport` olarak okumaya çalışıyor (ve tersi). Her kurulum iki dosya üretiyor. Aynı uygulama yeniden kurulunca eski delta ezilir. |
| P0-7 | Eşzamanlılık | `_alreadyHandledProcessIds`, `TrackedProcessIds` (kilitsiz `HashSet`), `async void OnPollingTick` | Timer thread'leri kilitsiz erişiyor. Tick'ler üst üste binebiliyor, finalize iki kez çağrılabiliyor, `PreSnapshot` henüz null iken oturum kapanabiliyor. |
| P0-8 | `msiexec` oturumu uzatıyor ve kirletiyor | `:422` + anahtar kelime listesi `:60` | Windows Installer servis süreci (`msiexec /V`) kurulumdan sonra ~10 dk yaşar. İsim eşleşmesiyle ağaca eklendiğinde rapor dakikalarca gecikir ve o arada sistemde yazılan her şey kuruluma atfedilir. Ebeveyn PID yerine isim eşleşmesi kullanıldığı için alakasız süreçler de ağaca giriyor. |
| P0-9 | Kaldırıcılar kurulum sanılıyor | `:60` (`"unins"`) | `unins000.exe` bir kurulum oturumu açıyor ve rapor "kurulum tamamlandı" diyor. |
| P0-10 | FileSystemWatcher taşması | `:346-356` | Varsayılan 8 KB buffer kullanılıyor, `Error` olayı dinlenmiyor. Büyük kurulumlarda (oyunlar, Visual Studio, Office) olaylar sessizce kayboluyor. LocalAppData'nın tamamı izlendiği için tarayıcı cache'i de rapora giriyor. |

### 1.2 Önemli eksikler (P1)

- Silinen ve değiştirilen dosyalar ayrılmıyor (`Deleted` olayı izlenmiyor).
- Kapsam dar: `C:\Windows` (System32\drivers, System32\Tasks), `%TEMP%`, kullanıcı profil kökü (`.vscode`, `.npm`), diğer sürücüler (`D:\Games`) izlenmiyor.
- Registry: değerler yok. HKLM yalnızca `Registry64` görünümünde okunuyor (WOW6432Node yalnızca Uninstall için var). COM/CLSID (derinlik 3+), dosya ilişkilendirmeleri, shell extension'lar yok.
- Zamanlanmış görevler, sürücüler, firewall kuralları, kök sertifikalar, Defender istisnaları, hosts, proxy, PATH, tarayıcı eklentileri ve politikaları hiç izlenmiyor.
- Tek aktif oturum var. Eşzamanlı kurulumlar (Ninite, toplu winget) tek rapora karışıyor.
- FSW anında yakalanan `Created` dosyanın boyutu genellikle 0.
- PID yeniden kullanımı: `_alreadyHandledProcessIds` hiç temizlenmiyor. PID'i yeniden kullanılan yeni bir kurulum görmezden gelinir.
- Flyout yalnızca sayı gösteriyor. "Neden tarayayım?" sorusuna cevap vermiyor, risk özeti yok.
- `Tests/Bakim.Tests/SetupSentinelTests.cs` servis kodunu değil, mantığın bir kopyasını test ediyor. Kopya liste servisle aynı bile değil (`update` var, `dxsetup` yok).
- CI'da (`.github/workflows/release.yml`) testler hiç çalıştırılmıyor, yalnızca tag'de yayın derleniyor.
- Kurulum yeniden başlatma isterse (`PendingFileRenameOperations`/`RunOnce`) oturum kopuyor. Bakım çökerse aktif oturum tamamen kayboluyor.

### 1.3 Korunacak iyi taraflar

- DI + `ISetupSentinelService` cephesi ve `App.xaml.cs` üzerindeki olay bağlama. v2 bu arayüzün arkasına oturabilir.
- "Sessiz izle, yalnızca sonuçta bildir" kararı doğru bir UX kararı.
- `FileThreatAnalyzerService` (PE, imza, konum, hash/VT) ve `AutorunsScannerEngine` (7 kalıcılık kategorisi) hazır ve olgun. v2'de sensör ve triage katmanı olarak yeniden kullanılacaklar.
- Rapor JSON'u ve dışa aktarma.

---

## 2. Hedef mimari

```
 ┌──────────────────────────── TETİKLEYİCİLER ────────────────────────────┐
 │ WMI süreç olayı │ Polling (yedek) │ "Bakım ile İzleyerek Kur" │ MSI olay günlüğü │
 └──────────────────────────────────┬──────────────────────────────────────┘
                                    ▼
          InstallerDetector (puanlı) ──► InstallerFingerprinter
          (Inno / NSIS / MSI / WiX Burn / InstallShield / Squirrel / MSIX, MOTW kaynağı, imza)
                                    ▼
          SessionManager (N eşzamanlı oturum, disk günlüğü, reboot sonrası devam)
                                    ▼
          ProcessTreeTracker (ebeveyn PID + oluşturma zamanı, Job Object, msiexec servis köprüsü)
                                    ▼
 ┌─────────────────────── SENSÖRLER (IChangeSensor) ───────────────────────┐
 │ Dosya     : FSW v2  │ USN Journal*  │ ETW Kernel-File*                    │
 │ Registry  : hotspot baseline + değer diff │ LastWriteTime taraması │ ETW*  │
 │ Kalıcılık : AutorunsScannerEngine önce/sonra diff                          │
 │ Sistem    : servis/sürücü, görev, firewall, sertifika, Defender istisnası,  │
 │             hosts, proxy, PATH, tarayıcı eklenti/politika, kısayollar       │
 │ Ağ        : yeni dinleyen portlar, kurulumun dış bağlantıları             │
 └──────────────────────────────────┬──────────────────────────────────────┘
                                    ▼          (* = Tam Koruma Modu / servis)
          Tek olay akışı: SensorEvent → events.jsonl (write-ahead)
                                    ▼
          NoiseFilter → AttributionEngine (güven puanı) → DeltaBuilder
                                    ▼
          RiskEngine (JSON kural seti) → Bulgular + Kurulum Risk Skoru + hızlı triage
                                    ▼
          SessionStore (rapor + indeks) ──► UI: Flyout v2 / İnceleme v2 / Kurulum Geçmişi
                                    ▼
          Aksiyonlar: Analizörle tara │ Tek tık devre dışı bırak │ Karantina │ Güvenli geri alma │ Güven listesi
```

Temel ilkeler:

- **Yakalama ile yorumlama ayrı.** Sensörler yalnızca olay üretir. Filtreleme, atıf ve risk, olay akışının üzerinde çalışan saf (test edilebilir) katmanlardır.
- **Kanıt olmadan silme yok.** Geri alma yalnızca "Oluşturuldu + baseline'da yoktu + güven ≥ Yüksek" öğelere uygulanır.
- **Zaman damgası baseline'dan güçlüdür.** Süreç oluşturma zamanı bilindiği sürece geç tespit veri kaybettirmemeli (USN kaydı zamanı, registry anahtarının `LastWriteTime`'ı, dosyanın `CreationTime`'ı).
- **Kademeli yetenek.** Temel Mod yönetici gerektirmez. Tam Koruma Modu servisle gelir. UI hangi modda olunduğunu gösterir.

---

## 3. Fazlar

### Faz 0: Stabilizasyon ve test edilebilirlik (v3.20.0)

Amaç: P0'ları kapatmak ve motoru test edilebilir parçalara ayırmak.

| İş | Detay |
|---|---|
| 0.1 Güvenli dosya delta'sı | `SetupFileEvent.ChangeType` rapora taşınır: `CreatedFiles`, `ModifiedFiles`, `DeletedFiles`, `TransientFiles` ayrı listeler olur. `Deleted` olayı dinlenir. Revert yalnızca şu koşulları sağlayan dosyalara uygulanır: `Created`, baseline'da yok, oturum sonunda hâlâ var ve hash eşleşiyor. Kalıcı silme yerine Geri Dönüşüm Kutusu (`Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(..., RecycleOption.SendToRecycleBin)`) veya karantina kullanılır. |
| 0.2 `ProcessInfoReader` yardımcısı | `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `QueryFullProcessImageName`: yükseltilmiş süreçlerde de çalışır (korumalı süreçler hariç). `GetProcessTimes` ile oluşturma zamanı alınır. `CreateToolhelp32Snapshot` ile ebeveyn PID okunur, yetki gerektirmez. Komut satırı (WMI `Win32_Process.CommandLine`) yükseltilmiş süreçlerde null dönebilir; bu "ek bilgi" olarak ele alınır. |
| 0.3 Registry normalizasyonu | Anahtar yolları tek biçime çekilir (`HKLM\...`, `HKCU\...`). HKLM için `Registry64` ve `Registry32` birlikte okunur. Asgari olarak Run/RunOnce *değerleri* ve `SYSTEM\CurrentControlSet\Services` anahtarları eklenir. Tam kapsam Faz 2'de. |
| 0.4 Tek depolama | `InstallerMonitorService` yalnızca snapshot/diff üreten bir `SnapshotEngine`'e indirgenir. Kalıcı kayıt tek noktada toplanır (`SessionStore`). Oturum başına klasör açılır: `InstallationLogs/{yyyyMMdd_HHmmss}_{app}_{id}/report.json`. `SchemaVersion` alanı eklenir. Tanınmayan eski dosyalar `legacy/` altına taşınır. `UninstallerViewModel`'deki "izleyerek kur" akışı da bu depoya yazar. |
| 0.5 Eşzamanlılık | `async void` timer kaldırılır, yerine `PeriodicTimer` + tek tüketici döngüsü gelir. Olaylar `System.Threading.Channels` ile akar. Paylaşılan setler `ConcurrentDictionary` olur. Finalize `SemaphoreSlim` ile tek sefere kilitlenir. PreSnapshot hazır olmadan finalize yapılmaz, bekleme süresi sınırlıdır. |
| 0.6 msiexec özel durumu | `msiexec` yalnızca iki durumda ağaca eklenir: ebeveyni ağaçtaysa ya da komut satırı `/i`/`/package` içeriyorsa. Servis tarafındaki `msiexec /V` ağaca eklenmez. MSI işleminin bitişi Application günlüğündeki `MsiInstaller` kaynaklı olaylardan izlenir: 1040/1042 işlem başı/sonu, 11707 başarılı kurulum, 11708 başarısız kurulum, 1033 ürün adı + durum. Bu günlük yönetici olmadan okunabilir. |
| 0.7 Kaldırıcı ayrımı | Şunlar kaldırma sayılır: `unins*`, `uninstall*`, `msiexec /x`, ya da yolu herhangi bir `UninstallString` ile eşleşen süreç. Bunlar `SessionKind.Uninstall` olarak açılır. Kurulum bildirimi gösterilmez; Faz 5.5'teki "kalıntı taraması" akışına bağlanır. |
| 0.8 FSW v2 | `InternalBufferSize = 65536`. `Error` olayında taşma sayacı artar, rapora "eksik olabilir" bayrağı konur ve hedef dizin `LastWriteTimeUtc ≥ oturum başı` ile yeniden taranır. Gürültü dizinleri olay anında elenir (bkz. Faz 3). |
| 0.9 PID yeniden kullanımı | PID seti yerine `(PID, CreationTime)` anahtarı kullanılır ve kayıtlar süre dolunca silinir. |
| 0.10 Test altyapısı | Arayüzler: `IProcessSource`, `IFileEventSource`, `IRegistryReader`, `IClock`. `InstallerClassifier` saf bir sınıf olur. Testler gerçek sınıfları çağırır; kopya-mantık testleri silinir. `dotnet test` çalıştıran push/PR iş akışı eklenir (`windows-latest`). |

**Kabul kriteri:** Tüm P0'lar kapalı. 7-Zip ve Notepad++ kurulumlarında Run, Services ve Uninstall doğru raporlanıyor, hive etiketleri doğru. "Önceden var olan dosya revert ile silinmez" testi geçiyor.

---

### Faz 1: Tespit motoru v2 (v3.21)

**1.1 Olay tabanlı süreç tespiti.**
Temel modda `ManagementEventWatcher` ile `SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_Process'` sorgusu kullanılır; yönetici gerektirmez. Tam Koruma Modunda anlık bildirim için `Win32_ProcessStartTrace` kullanılır; bu yönetici ister. Polling yalnızca yedek olarak ve 5 sn aralıkla kalır.

**1.2 Puanlı sınıflandırıcı.** Boolean karar yerine 0–100 arası puan:

| Sinyal | Ağırlık |
|---|---|
| PE'de kurulum çatısı izi (Inno, NSIS, WiX Burn, InstallShield, Advanced Installer, 7z SFX, Squirrel) | +50 |
| `msiexec` + `/i` veya `/package` | +60 |
| Dosya adı / ürün adı / açıklamada anahtar kelime | +20 |
| Manifest `requireAdministrator` + İndirilenler/Temp/Masaüstü konumu | +15 |
| Mark-of-the-Web (`Zone.Identifier`, ZoneId=3) | +10 |
| Pencere başlığında "Setup / Kurulum / Wizard / Sihirbaz" | +10 |
| Program Files'tan çalışan, zaten kurulu uygulama | −40 |
| Bilinen güncelleyici (imzacı + yol deseni) | ayrı sınıf: "Güncelleme" |

Puan ≥ 50 ise oturum açılır. 30–50 arası "sessiz aday" sayılır: delta toplanır ama bildirim eşiği yükselir. Reddedilen adaylar puanlarıyla birlikte tanılama günlüğüne yazılır; yanlış pozitif ayıklarken çok işe yarar.

**1.3 `InstallerFingerprinter`.** Kurulum çatısı şu izlerden tespit edilir:
- Inno: `Inno Setup Setup Data` dizesi
- NSIS: overlay'de `NullsoftInst`
- WiX Burn: `.wixburn` PE bölümü
- InstallShield: sürüm bilgisi
- Squirrel: `Update.exe --install` ve `%LocalAppData%\SquirrelTemp`
- MSIX: `AppInstaller` / `Add-AppxPackage`
- ClickOnce: `dfsvc.exe`

Çatı bilgisi üç yerde kullanılır: rapor, sessiz kaldırma stratejisi ve "İzleyerek Kur" modunda ayrıntılı kurulum günlüğü açmak (Inno `/LOG=`, MSI `/L*v`).

**1.4 Kaynak bilgisi.** MOTW içindeki `HostUrl` ve `ReferrerUrl` raporda "İndirildiği site" olarak gösterilir. Kurulum dosyasının SHA-256'sı, imzacısı ve imza durumu da rapora eklenir.

**1.5 Süreç ağacı.**
- Çocuk süreç koşulu: `ParentPid ∈ ağaç` ve `CreationTime ≥ ebeveyn.CreationTime`. İkinci koşul PID yeniden kullanımına karşı korur.
- Dolaylı köprüler "dolaylı" etiketiyle ayrıca izlenir: msiexec servisi (MSI günlüğüyle), `TrustedInstaller`, `dllhost` (COM), Görev Zamanlayıcı.
- Kurulum sonunda "uygulamayı başlat" seçeneğiyle açılan uygulama ağaçtan ayrılır. Ölçüt: yolu kurulum dizininde olan ve kurulum çatısı izi taşımayan süreç. Aksi halde oturum, kullanıcı uygulamayı kapatana kadar açık kalır.

**1.6 "Bakım ile İzleyerek Kur".** Sağ tık menüsünden, sürükle-bıraktan ve Mağaza modülünden erişilir. En güvenilir yol budur:
- Baseline zaten hazırdır.
- Kurulum Bakım'dan başlar, dolayısıyla kök PID kesin bilinir.
- Çalıştırmadan önce ön tarama yapılır: imza, VT hash, kurulum çatısı, MOTW kaynağı.
- Çocuk süreçler Job Object ile kesin izlenir.

Kısıt: `requireAdministrator` kurulumlar orta bütünlük seviyesindeki bir süreçten `CreateProcess` ile başlatılamaz (`ERROR_ELEVATION_REQUIRED`). `runas` ile başlatılan süreç de job'a atanamaz. Bunun için kısa ömürlü, yükseltilmiş bir başlatıcı (`Bakim.exe --sentinel-launch <yol>`) ya da Faz 8'deki servis gerekir. `UninstallerViewModel`'deki mevcut "izleyerek kur" akışı bu yola taşınır.

**1.7 Güncelleyici ayrımı.** Sabit `ExcludedProcessNames` listesinin yerini "bilinen güncelleyici" veritabanı alır (imzacı + yol deseni + süreç adı). Güncellemeler oturum açmaz; istenirse (ayar) düşük öncelikli "Güncelleme" günlüğüne yazılır.

**Kabul kriteri:** Test matrisindeki (Faz 9.4) ≥ %95 kurulum tespit ediliyor, kaldırıcılar ayrı sınıflanıyor, günlük kullanımda haftada ≤ 1 yanlış pozitif oturum açılıyor.

---

### Faz 2: Değişiklik yakalama v2, sensörler (v3.22)

**2.1 Sürekli baseline ("sıcak önbellek").** Bakım açılışta ve sonra boştayken 30 dakikada bir hafif bir baseline alır: hotspot registry değerleri, kalıcılık öğeleri, servisler, görevler, sertifikalar ve firewall kuralları. Değerler hash'lenerek saklanır. Kurulum tespit edildiğinde pre-snapshot zaten hazır olduğu için P0-3 kökten çözülür.

**2.2 Zaman damgası tabanlı delta.**
- Registry: `RegQueryInfoKey` ile her anahtarın `LastWriteTime` değeri okunur; `≥ süreç oluşturma zamanı` olanlar "dokunulan anahtar" sayılır. .NET bunu sunmadığı için P/Invoke gerekir. Böylece baseline dışındaki alanlarda da değişiklik bulunur.
- Dosyalar: hedef dizinlerde `CreationTimeUtc`/`LastWriteTimeUtc ≥ başlangıç` ile yeniden tarama yapılır. FSW taşmasından kurtarma bu yolla olur.

**2.3 Registry hotspot listesi.** Değer düzeyinde, 32 ve 64 bit görünümlerde:
- `Run`, `RunOnce`, `RunServices`, `Policies\Explorer\Run` (HKLM, HKCU, Wow6432Node)
- `SYSTEM\CurrentControlSet\Services` (`ImagePath`, `Start`, `Type`, `ServiceDll`)
- `Uninstall` (her iki görünüm + HKCU)
- `Winlogon` (`Shell`, `Userinit`, `Notify`), `Image File Execution Options` (`Debugger`, `SilentProcessExit`), `AppInit_DLLs`, `AppCertDlls`
- `Classes`: `CLSID\*\InprocServer32`, `*\shellex\ContextMenuHandlers`, `Directory\shell`, uzantı→ProgID eşleşmeleri, URL protokol işleyicileri, `ShellIconOverlayIdentifiers`
- `Browser Helper Objects`, `ShellServiceObjectDelayLoad`
- Ortam değişkenleri: `Session Manager\Environment` ve `HKCU\Environment` (özellikle PATH)
- `Internet Settings` (`ProxyServer`, `ProxyEnable`, `AutoConfigURL`)
- Tarayıcı politikaları: `Policies\Google\Chrome`, `Policies\Microsoft\Edge`, `Policies\Mozilla\Firefox` (`ExtensionInstallForcelist`, `HomepageLocation`, `DefaultSearchProvider*`)
- `Windows Defender\Exclusions` (`Paths`, `Processes`, `Extensions`); okuma yönetici gerektirebilir
- `SystemCertificates\Root\Certificates` (HKLM + HKCU)
- `Session Manager\PendingFileRenameOperations`
- `SharedAccess\Parameters\FirewallPolicy\FirewallRules`
- İleri seviye kalıcılık: `Lsa` (Authentication/Notification/Security Packages), Print Monitors, Netsh helpers

**2.4 Kalıcılık diff'i.** `AutorunsScannerEngine.ScanAllAsync()` oturum öncesinde ve sonrasında çalıştırılır. Çıktı `PersistenceDelta` olur: kategori bazında eklenen, değişen ve silinen öğeler. Neredeyse sıfır yeni kodla 7 kategori kapsanır.

**2.5 Sistem durumu sensörü.**
- Servisler ve sürücüler: SCM listesi.
- Zamanlanmış görevler: `Schedule.Service` COM, tetikleyici türü ve hedef komutu.
- Firewall: `INetFwPolicy2`.
- Sertifika depoları: `X509Store` Root, CA ve TrustedPublisher (CurrentUser + LocalMachine).
- `hosts` dosyası: hash + satır diff'i.
- WinHTTP proxy: `WinHttpGetDefaultProxyConfiguration`.
- DNS ayarları.
- Defender tercihleri: WMI `MSFT_MpPreference`, yönetici ister.
- Tarayıcı eklenti klasörleri: Chrome/Edge `User Data\*\Extensions`, Firefox `profiles\*\extensions`.
- Başlat Menüsü ve Masaüstü kısayolları: hedefler `ShellUninstallResolverService`'teki kısayol çözücüyle çözülür.

**2.6 Ağ sensörü.**
- Kurulumdan sonra ortaya çıkan yeni dinleyen TCP/UDP portları. `GetExtendedTcpTable` kullanılır; `NetworkMonitorService` yeniden kullanılır.
- Oturum boyunca ağaçtaki PID'lerin açtığı dış bağlantılar. İndirici kurulumlarda neyi nereden indirdiğini gösterir.

**2.7 Dosya sensörü v2.**
- Kapsam genişler: `Windows\System32\drivers`, `System32\Tasks`, `%TEMP%`, profil kökü ve tüm sabit sürücülerin kökü (seviye 1). Kurulum dizini belli olunca o dizin derin izlenir.
- Dosya başına tutulan bilgiler:
  - değişiklik türü (oluştur / değiştir / sil / yeniden adlandır) ve son boyut
  - SHA-256: yalnızca yürütülebilirler ve ≤ 200 MB dosyalar için, arka planda
  - uzantıdan bağımsız "PE mi?" kontrolü (MZ başlığı). `.dat` ya da `.jpg` adıyla bırakılmış exe'ler bu yolla yakalanır.
  - gizli/sistem özniteliği ve ADS varlığı

**2.8 USN Journal (Tam Koruma Modu).**
- Oturum başında USN noktası kaydedilir, bitişte `FSCTL_READ_USN_JOURNAL` ile okunur.
- Bu yöntem kayıpsızdır ve tüm birimi kapsar. Silinen ve yeniden adlandırılan dosyalar da gelir.
- Her kaydın zaman damgası olduğu için "süreç oluşturma zamanından itibaren" filtresiyle tespit öncesi saniyeler de kapsanır.
- FRN → yol çözümü `OpenFileById` ile yapılır. Silinen dosyalar için ebeveyn FRN zinciri kullanılır.
- Sonuç: FSW'nin taşma ve kapsam sorunları tamamen ortadan kalkar.

**2.9 ETW (Tam Koruma Modu, ileri seviye).**
- Sağlayıcılar: `Microsoft-Windows-Kernel-Process`, `Kernel-File`, `Kernel-Registry` (TraceEvent kütüphanesiyle).
- Her olay PID taşıdığı için atıf kesin olur.
- Maliyetli olduğundan yalnızca aktif oturum süresince açılır.

**Kabul kriteri:** Test matrisinde Run, servis, görev, Uninstall, CLSID/shell extension, firewall, sertifika ve PATH değişikliklerinin %100'ü raporda yer alıyor. FSW taşmasında rapor "eksik olabilir" uyarısı veriyor ve yeniden taramayla tamamlanıyor.

---

### Faz 3: Gürültü filtresi ve atıf (v3.22–v3.23)

**3.1 Gürültü kuralları** (`Rules/sentinel-noise.json`):
- Yollar: tarayıcı `Cache`/`Code Cache`/`GPUCache`, `INetCache`, `Explorer\thumbcache_*`, `Prefetch`, `SoftwareDistribution`, `Windows Defender\Scans`, Bakım'ın kendi `AppData\Bakım` klasörü, `$Recycle.Bin`, OneDrive önbelleği, `*.etl`, `*.tmp` ve `*.log` (son ikisi ayara bağlı).
- Registry: `MuiCache`, `UserAssist`, `BagMRU`, `RecentDocs`, `Explorer\FeatureUsage`, `CloudStore`, `bam\State`, `AppCompatFlags\Compatibility Assistant`.
- Süreçler (ETW modunda): `SearchIndexer`, `MsMpEng`, `TiWorker`, `OneDrive` vb.

**3.2 Güven puanı.** Her değişiklik bir güven seviyesi alır:

| Seviye | Ölçüt |
|---|---|
| Kesin | ETW PID'i ağaçta ya da Job Object içinde |
| Yüksek | Kurulum dizini altında, yeni Uninstall kaydının `InstallLocation`'ı altında ya da ağaçtaki bir ikiliyi işaret eden registry değeri |
| Orta | Zaman penceresinde + hotspot alanında |
| Düşük | Zaman penceresinde + genel alanda |

UI varsayılan olarak Orta ve üstünü gösterir. Geri alma yalnızca Yüksek ve Kesin seviyelere uygulanır.

**3.3 Geçici dosyalar.** Oturum içinde oluşup silinen dosyalar "Geçici / Bırakılıp silinen" sekmesinde gösterilir. Dropper tespiti için değerlidir. Küçük yürütülebilirler `Created` anında hash'lenir; silinmeden önce iz kalır.

**3.4 Ana kurulum konumu çıkarımı.** Uninstall `InstallLocation` değeri, en çok dosya yazılan kök klasör ve kısayol hedefleri birlikte değerlendirilir.

**3.5 Paketlenmiş yazılım (bundleware/PUP) tespiti.** Tek oturumda birden fazla yeni Uninstall kaydı oluşursa rapor bunu söyler: "Bu kurulum 3 program yükledi: X, Y (yayıncı: …), Z." İstenmeyen program tek tıkla kaldırılabilir (`DeepUninstallerService`). Kullanıcı açısından en görünür antivirüs benzeri özellik budur.

**Kabul kriteri:** Chrome açıkken yapılan bir kurulumda tarayıcı cache dosyaları raporda yer almıyor. 7-Zip kurulumunda "Ana konum: C:\Program Files\7-Zip" doğru çıkıyor.

---

### Faz 4: Risk ve triage motoru (v3.23)

**4.1 Kural motoru.** Kurallar `Rules/sentinel-rules.json` dosyasında tutulur. Dosya şema versiyonludur, GitHub release ile güncellenebilir ve hash'i doğrulanır. Her kuralın alanları:
- `id`
- koşul: olay türü + desen + bağlam (imza durumu, konum, hedef)
- şiddet ve puan
- Türkçe açıklama ve öneri
- ilgili MITRE ATT&CK tekniği

**4.2 Başlangıç kural kataloğu.**

| Şiddet | Kural | ATT&CK |
|---|---|---|
| Kritik | Kök sertifika deposuna sertifika eklendi | T1553.004 |
| Kritik | Defender istisnası eklendi | T1562.001 |
| Kritik | IFEO `Debugger` / `SilentProcessExit` | T1546.012 |
| Kritik | Winlogon `Shell`/`Userinit`/`Notify` değişti | T1547.004 |
| Kritik | `AppInit_DLLs` | T1546.010 |
| Kritik | LSA paketleri | T1547.002 / .005 |
| Kritik | `hosts` dosyasına yönlendirme satırı | — |
| Kritik | System32/SysWOW64'e imzasız PE bırakıldı; sistem dosyası adı taklidi (mevcut `CriticalSystemExes` yeniden kullanılır) | T1036 |
| Yüksek | İmzasız veya geçersiz imzalı servis ya da sürücü | T1543.003 |
| Yüksek | Oturum açılışında veya sık aralıkla çalışan, imzasız hedefli zamanlanmış görev | T1053.005 |
| Yüksek | WMI event consumer | T1546.003 |
| Yüksek | Tarayıcı politikasıyla zorunlu eklenti / ana sayfa / arama motoru | T1176 |
| Yüksek | Proxy/PAC ayarlandı | — |
| Yüksek | Gelen bağlantıya izin veren firewall kuralı | — |
| Yüksek | `%TEMP%`, ProgramData kökü veya Roaming kökündeki bir exe Run'a eklendi | T1547.001 |
| Yüksek | Uzantısı yanlış PE (`.dat`, `.jpg` içinde MZ) | T1036 |
| Orta | İmzalı Run/RunOnce girdisi | T1547.001 |
| Orta | İmzalı, otomatik başlayan yeni servis | T1543.003 |
| Orta | Yeni dinleyen port | — |
| Orta | Shell extension / sağ tık menüsü | — |
| Orta | Birden fazla Uninstall kaydı (bundle) | — |
| Orta | Varsayılan ilişkilendirmenin ele geçirilmesi (`.pdf`, `.html`, `http`) | — |
| Orta | PATH'e ekleme | — |
| Bilgi | Kısayollar, dosya sayısı/boyutu, telemetri görevleri | — |

Puanı düşüren güven sinyalleri:
- Kurulum dosyası ile bırakılan ikililerin aynı geçerli imzacıya sahip olması.
- `FileThreatAnalyzerService`'teki bilinen güvenilir yayıncı listesi (`KnownTrustedPublishers`).
- Kullanıcının güven listesi.

Puanı artıran sinyal, **imzacı tutarsızlığıdır**: kurulum "Foo Ltd" imzalıyken servis ikilisi imzasız ya da başka bir imzacıya ait.

**4.3 Hızlı triage.** Bildirimden önce arka planda otomatik çalışır:
- Yeni yürütülebilirlerde imza, hash ve PE kontrolü yapılır. `FileThreatAnalyzerService.AnalyzeFileAsync` için hafif bir "Quick" modu eklenir.
- Öncelik sırası: kalıcılık hedefleri > servis/sürücü > Run > diğer exe > dll.
- Bütçe: ≤ 50 dosya veya ≤ 10 sn. Kalan dosyalar "Analizörde derin tara" adımına bırakılır.

**4.4 Kurulum Risk Skoru.** 0–100 arası puan ve bir karar üretilir: **Temiz / Bilgi / Dikkat / Şüpheli / Tehlikeli**. Buna en önemli 3 bulgu eşlik eder. Flyout ve rapor bu karara göre renklenir.

**4.5 İtibar.**
- Yerel hash deposu: kullanıcının güvendiği hash'ler ve önceki tarama sonuçları.
- VT hash sorgusu: API anahtarı varsa, 4 istek/dk sınırıyla bir kuyruk üzerinden.
- **Dosya yükleme asla otomatik yapılmaz**, yalnızca kullanıcının açık onayıyla.

**4.6 İkinci görüş: Microsoft Defender.** Defender etkinse `MpCmdRun.exe -Scan -ScanType 3 -File "<yol>" -DisableRemediation` çalıştırılır. Çıkış kodu 2 tehdit bulunduğu anlamına gelir; sonuç bulgu olarak eklenir. Standart kullanıcıyla çalışıp çalışmadığı doğrulanmalı.

**Kabul kriteri:** Zararsız referans kurulumlar (7-Zip, VS Code, Notepad++) "Temiz" ya da "Bilgi" çıkıyor. Simülatör kurulum (Faz 9.3) "Tehlikeli" çıkıyor ve beklenen bulguları içeriyor.

---

### Faz 5: Aksiyonlar (v3.24)

**5.1 Analizörle Tara v2.** `AnalyzeSpecificFilesAsync`'e triage bağlamı da geçilir: dosyayı hangi Run girdisinin ya da servisin işaret ettiği ve `LocationSource = "Kurulum Nöbetçisi: <uygulama>"`. Tek dosyada modal pencere açılmaz; dosya listeye eklenir ve ilerleme gösterilir. Kullanıcıya "Yalnızca şüphelileri tara" ve "Hepsini tara" seçenekleri sunulur.

**5.2 Tek tık müdahaleler.** Bulgu kartından yapılır ve hepsi geri alınabilir (undo kaydı tutulur):
- Run girdisini devre dışı bırak
- Servisi Manuel ya da Devre Dışı yap
- Görevi devre dışı bırak (`AutorunsScannerEngine.ToggleItemAsync`)
- Firewall kuralını kapat
- Kök sertifikayı kaldır
- Defender istisnasını kaldır
- Proxy'yi sıfırla
- `hosts` satırını geri al

**5.3 Karantina.** Dosyalar `%ProgramData%\Bakım\Quarantine\{id}\` altına taşınır. İçerik XOR ya da AES ile nötrlenir; böylece yanlışlıkla çalıştırılamaz ve AV karantinayı tekrar tekrar işaretlemez. Meta veri saklanır: orijinal yol, ACL, hash ve zaman. Geri yükleme desteklenir.

**5.4 Güvenli Geri Alma v2.**
1. Önce resmi kaldırıcı önerilir (`UninstallString`); kalıntılar daha sonra rapora göre temizlenir.
2. Planlayıcı kuru çalıştırma önizlemesi gösterir, kullanıcı onay kutularıyla seçer.
3. Uygulama sırası: süreçleri durdur → servisi durdur/sil → görevi sil → Run değerlerini sil → registry (değiştirilmiş değerlere baseline'daki eski değer geri yazılır) → dosyalar (Geri Dönüşüm Kutusu ya da karantina) → boş klasörler.
4. Yalnızca güveni ≥ Yüksek olan öğeler geri alınır.
5. İsteğe bağlı: kurulum başında Sistem Geri Yükleme Noktası oluşturulur. Yönetici ister; Windows varsayılan olarak 24 saatte bir noktaya izin verir (`SystemRestorePointCreationFrequency`).

**5.5 Rapor ↔ Kaldırıcı bağı.**
- Rapor, oluşturduğu Uninstall kaydının anahtar adına bağlanır.
- Kaldırıcı modülünde bu uygulamalar "Kurulum izi mevcut: eksiksiz kaldırma" rozeti alır.
- `DeepUninstallerService` / `ResidualScannerEngine` kalıntı ararken tahmin yerine bu izi kullanır.
- Kaldırma oturumu (0.7) bitince kullanıcıya sorulur: "Kaldırma tamamlandı. Kurulum izine göre 14 kalıntı bulundu, temizleyelim mi?"

**5.6 Güven listesi.** "Bu yayıncıya güven" (imzacı bazlı) ya da "bu uygulamaya güven" seçilebilir. Sonraki güncellemeler için bildirim gösterilmez, yalnızca günlüğe yazılır.

**Kabul kriteri:** Özellik tabanlı testte önceden var olan hiçbir dosya geri alma ile silinmiyor. Her müdahalenin undo'su çalışıyor.

---

### Faz 6: UX (v3.23–v3.25)

**6.1 Flyout v2** (kurulum bittiğinde).
- İçerik: uygulama adı, risk rozeti (tasarım token'ları), en önemli 3 bulgu (tek satır) ve sayaçlar (dosya / registry / kalıcılık / ağ).
- Düğmeler: **[Analizörle Tara]** (birincil; Şüpheli ve üstünde vurgulu), [Raporu Aç], [Güven], [Kapat].
- `ShowActivated=false`: odak çalmaz.
- Tam ekran oyun veya sunum sırasında bildirim ertelenir (`SHQueryUserNotificationState` + `GameModeService`) ve çıkınca gösterilir.
- Temiz sonuçlarda isteğe bağlı olarak yalnızca tepsi bildirimi gösterilir.

**6.2 Ayarlar.**

| Ayar | Seçenekler |
|---|---|
| Bildirim seviyesi | Hepsi / Yalnızca riskli / Hiçbiri |
| Otomatik tarama | Sor / Şüpheliyse otomatik / Her zaman otomatik |
| İzleme göstergesi | Yok / Tepsi rozeti / Küçük hap |
| Kapsam | Ek sürücüler |
| Saklama süresi | 30 / 90 gün / Sınırsız |
| Gelişmiş | Tam Koruma Modu (servis) |

**6.3 İnceleme penceresi v2.** Sekmeler:
- **Özet:** risk, bulgular, kaynak URL, imza, kurulum çatısı, süre.
- **Dosyalar:** klasör ağacına göre gruplu; Oluşturuldu / Değişti / Silindi / Geçici filtreleri; PE ve imza sütunları.
- **Kayıt Defteri:** önce/sonra değer diff'i.
- **Kalıcılık:** Run, servis, görev, sürücü, WMI; yerinde devre dışı bırakma.
- **Sistem:** sertifika, firewall, proxy, hosts, PATH, tarayıcı.
- **Ağ.**
- **Zaman Çizelgesi:** süreç ağacı + kronolojik olaylar.

Büyük listeler sanallaştırılır (`VirtualizingStackPanel`, 100k satır).

**6.4 "Kurulum Geçmişi" modülü** (ana navigasyonda ya da Analizör alt sekmesinde).
- Tüm oturumlar listelenir; arama ve filtre (risk, tarih, yayıncı) vardır.
- İki rapor karşılaştırılabilir, örneğin aynı uygulamanın iki sürümü.
- Dışa aktarma: JSON ve tek dosyalık HTML rapor.

**6.5 Canlı gösterge.** Tepsi ikonunda nokta rozeti ve "Nöbetçi: X kurulumu izleniyor (123 değişiklik)" ipucu gösterilir. `TrayFlyoutWindow`'da mini bir kart yer alır.

**6.6 Komut paleti.** `CommandPaletteService`'e şu girişler eklenir: "Son kurulum raporu", "Kurulum geçmişi", "Bakım ile izleyerek kur…".

**6.7 Kalite kuralları.** `AutomationProperties.Name` (`Tools/add-automation-names.py`), `verify-symbols.py`, `verify-tokens.py` ve sıfır emoji standardı uygulanır.

---

### Faz 7: Dayanıklılık ve performans (sürekli)

**7.1 Write-ahead günlük.** Olaylar `sessions/active/{id}/events.jsonl` dosyasına akıtılır. Bakım çöker ya da yeniden başlarsa oturum "kurtarıldı" etiketiyle sonlandırılıp raporlanır.

**7.2 Yeniden başlatma sonrası devam.** Kurulum `PendingFileRenameOperations` ya da `RunOnce` eklediyse oturum "yeniden başlatma bekliyor" durumunda kapanır. Açılışta (autostart) RunOnce'ın çalışması izlenir ve ek delta aynı rapora eklenir.

**7.3 Kaynak bütçesi.**
- Boşta CPU < %0.3, ek RAM < 25 MB.
- Oturum başına olay sınırı (örneğin 500k). Aşılırsa klasör bazlı özetlemeye geçilir; oyun kurulumları 100k+ dosya üretebilir.
- Hash'leme `BelowNormal` öncelikli bir thread'de yapılır ve I/O kısıtlanır.

**7.4 Eşzamanlı oturumlar.** `SessionManager` N oturumu aynı anda yönetir. Olaylar süreç ağacına göre ayrılır; atıf belirsizse "paylaşımlı" işaretlenir.

**7.5 Tanılama sayfası.** Son tespitler, reddedilen adaylar ve puanları, FSW taşmaları ve sensör süreleri gösterilir.

---

### Faz 8: Tam Koruma Modu, servis mimarisi (v4.0)

**Neden gerekli:** USN Journal, ETW, `Win32_ProcessStartTrace`, Defender tercihleri, geri yükleme noktası, SCM kontrolü ve yükseltilmiş süreç ayrıntıları yönetici ister. UI ise `asInvoker` kalmalı.

- **Servis:** `BakimSentinelSvc` (LocalSystem). Aynı çözümde ayrı bir proje olarak yazılır (`Microsoft.Extensions.Hosting.WindowsServices`). Inno Setup ile kurulur, kurulumda seçenek olarak sunulur.
- **IPC:** `\\.\pipe\Bakim.Sentinel` named pipe'ı. ACL yalnızca etkileşimli yerel kullanıcılara ve SYSTEM'e izin verir. Mesajlar JSON ve versiyonludur. Servis istemciyi doğrular: `GetNamedPipeClientProcessId` → yol + imza kontrolü.
- **Güvenlik (yerel yetki yükseltme yüzeyi):**
  - Servis istemciden gelen bir yolu asla çalıştırmaz.
  - Yalnızca beyaz listeli komutlar kabul edilir: oturum başlat/bitir, rapor al, karantinaya al. Karantina hedefi doğrulanır.
  - Bir tehdit modeli dokümanı yazılır ve pipe mesajları fuzz testinden geçirilir.
- **Kademeli düşüş:** Servis yoksa Temel Mod çalışır (Faz 0–7'deki kullanıcı düzeyi sensörler). UI hangi modda olunduğunu bir rozetle gösterir.
- **AV yanlış pozitifi:** Süreç izleme, ETW ve servis birleşimi başka antivirüslerin sezgisel tespitlerini tetikleyebilir. Mevcut signtool akışıyla tüm ikililer imzalanmalı. EV sertifika değerlendirilmeli ve Microsoft'a false-positive gönderim süreci hazır tutulmalı.

---

### Faz 9: Test ve kalite stratejisi (her fazla birlikte)

**9.1 Birim testleri:** `InstallerClassifier` (puan tablosu), `NoiseFilter`, `AttributionEngine`, `DeltaBuilder`, `RiskEngine` (her kural için pozitif ve negatif test), `RollbackPlanner`. `RollbackPlanner` için "önceden var olan dosya asla silinmez" değişmezi özellik tabanlı testle doğrulanır.

**9.2 Kayıttan oynatma (golden) testleri:** Gerçek kurulumlardan kaydedilmiş `events.jsonl` fixture'ları pipeline'dan geçirilir ve beklenen rapor snapshot'ıyla karşılaştırılır.

**9.3 Windows CI entegrasyon testleri** (`windows-latest`, yönetici var). `Tests/Fixtures/Installers/*.iss` altında Inno Setup ile derlenen sentetik kurulumlar:
- (a) temiz uygulama
- (b) Run + servis + görev
- (c) bundle (2 Uninstall kaydı)
- (d) "kötü davranış simülatörü": test kök sertifikası, test Defender istisnası, hosts satırı, IFEO. Yalnızca CI VM'inde çalışır ve sonrasında temizlenir.

Akış: kurulum çalıştırılır → rapor üretilir → beklenen bulgular doğrulanır.

**9.4 Gerçek dünya matrisi** (her sürüm öncesi, elle). Her satır için tespit, tür, Run/servis/görev doğruluğu, süre ve gürültü kaydedilir.

| Uygulama | Neyi sınar |
|---|---|
| 7-Zip (exe + MSI) | NSIS ve MSI |
| VS Code (user + system) | Inno Setup, HKCU ve HKLM |
| Notepad++ | Shell extension |
| Chrome | Omaha güncelleyici, görevler, servis |
| Firefox | Servis |
| Discord | Squirrel, AppData, Run |
| Steam + bir oyun | Büyük dosya hacmi |
| Node.js MSI | PATH |
| Python | PATH, dosya ilişkilendirme |
| Git for Windows | PATH, sağ tık menüsü |
| VLC | Dosya ilişkilendirme |
| Office C2R | Çoklu süreç, servis |
| `winget install` | Paket yöneticisi |
| Portable zip | Yanlış pozitif **olmamalı** |
| `unins000` ile kaldırma | Kaldırma oturumu |

**9.5 Performans:** 50k dosyalık sentetik kurulumda rapor ≤ 10 sn içinde hazır olmalı, bellek tepe noktası ≤ 150 MB kalmalı.

---

### Faz 10: İleri seviye / vizyon (v4.x)

- **Sandbox Önizleme:** Kurulum Windows Sandbox (`.wsb`: `MappedFolder` + `LogonCommand`) içinde çalıştırılır ve Bakım toplayıcısı orada koşar. Sonuç: "Kurmadan önce ne yapacağını gör" raporu. Pro/Enterprise gerektirir; özellik kontrolü `Containers-DisposableClientVM` ile yapılır.
- **MSI statik önizleme:** `.msi` kurulmadan `WindowsInstaller.Installer` COM ile okunur (`File`, `Registry`, `ServiceInstall`, `CustomAction` tabloları) ve "bu MSI şunları yapacak" diye gösterilir. Script ya da exe içeren Custom Action'lar için uyarı verilir.
- **YARA** taraması: topluluk kuralları + Bakım kuralları.
- **AMSI:** Bırakılan `.ps1`/`.vbs`/`.js` dosyaları `AmsiScanBuffer` ile taranır.
- **Sürüm farkı:** Aynı uygulamanın yeni sürümü kurulunca "önceki sürüme göre yeni eklenenler" gösterilir. Güncellemeyle reklam yazılımına dönüşen uygulamalar böyle yakalanır.
- **Paket yöneticileri:** winget, choco ve scoop komutları tanınır, paket kimliği rapora eklenir.
- **Topluluk itibarı** (isteğe bağlı, açık gizlilik onayıyla): yalnızca hash ve bulgu kimlikleri paylaşılır.

---

## 4. Veri modeli v2

```csharp
public enum SessionKind { Install, Uninstall, Update, Unknown }
public enum ChangeKind  { FileCreated, FileModified, FileDeleted, FileRenamed,
                          RegKeyCreated, RegKeyDeleted, RegValueSet, RegValueDeleted,
                          ServiceCreated, ServiceModified, DriverCreated,
                          TaskCreated, TaskModified, FirewallRuleAdded,
                          CertificateAdded, DefenderExclusionAdded, HostsModified,
                          ProxyChanged, EnvironmentChanged, BrowserPolicyChanged,
                          ShortcutCreated, ListeningPortOpened, UninstallEntryCreated }
public enum Confidence  { Low, Medium, High, Certain }
public enum Verdict     { Clean, Info, Caution, Suspicious, Dangerous }

public sealed record SensorEvent(
    DateTime TimestampUtc, ChangeKind Kind, string Target,          // yol ya da HKLM\...\Değer
    string? OldValue, string? NewValue, int? Pid, string? ProcessImage,
    string Sensor, Confidence Confidence);

public sealed record InstallerInfo(
    string Path, string Sha256, string? Framework, string? Signer, SignatureStatus Signature,
    string? DownloadUrl, string? ReferrerUrl, string? CommandLine);

public sealed record Finding(
    string RuleId, Verdict Severity, int Score, string Title, string Explanation,
    string Recommendation, string? MitreId, IReadOnlyList<int> EvidenceEventIndexes);

public sealed class SetupSessionReport
{
    public int SchemaVersion { get; init; } = 2;
    public string SessionId { get; init; } = "";
    public SessionKind Kind { get; init; }
    public string AppName { get; set; } = "";
    public InstallerInfo? Installer { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string? PrimaryInstallLocation { get; set; }
    public List<string> CreatedUninstallKeys { get; } = new();   // bundle tespiti + kaldırıcı bağı
    public List<ProcessNode> ProcessTree { get; } = new();
    public ChangeSummary Summary { get; set; } = new();          // sayaçlar, boyut
    public List<Finding> Findings { get; } = new();
    public int RiskScore { get; set; }
    public Verdict Verdict { get; set; }
    public bool IsPossiblyIncomplete { get; set; }               // FSW taşması, Temel Mod vb.
    public string EventsFile { get; set; } = "events.jsonl.gz";
}
```

Depolama:
- Oturum klasörü: `InstallationLogs/{yyyyMMdd_HHmmss}_{app}_{id}/` içinde `report.json` ve `events.jsonl.gz`.
- Hızlı listeleme için `InstallationLogs/index.json`.
- Saklama politikası ayarlardan belirlenir.
- v1 → v2 geçişi: `SetupDeltaReport` okunup `SetupSessionReport`'a dönüştürülür.

---

## 5. Kod organizasyonu

```
Services/Sentinel/
  SetupSentinelService.cs            // mevcut ISetupSentinelService cephesi (App.xaml.cs bozulmaz)
  SessionManager.cs
  Detection/
    InstallerDetector.cs             // WMI + polling, puanlama
    InstallerClassifier.cs           // saf, test edilebilir
    InstallerFingerprinter.cs        // Inno/NSIS/WiX/MSI/Squirrel/MSIX, MOTW
    ProcessTreeTracker.cs
    ProcessInfoReader.cs             // QueryFullProcessImageName, ToolHelp32, GetProcessTimes
    MsiEventLogWatcher.cs
  Sensors/
    IChangeSensor.cs
    FileSystemWatcherSensor.cs
    UsnJournalSensor.cs              // Tam Koruma Modu
    RegistryHotspotSensor.cs         // baseline + değer diff + LastWriteTime
    PersistenceSensor.cs             // AutorunsScannerEngine sarmalayıcı
    SystemStateSensor.cs             // sertifika, firewall, Defender, hosts, proxy, PATH, tarayıcı
    NetworkSensor.cs
    BaselineCache.cs
  Analysis/
    NoiseFilter.cs
    AttributionEngine.cs
    DeltaBuilder.cs
    RiskEngine.cs
    QuickTriage.cs
    Rules/ (RuleModel.cs, RuleLoader.cs)
  Actions/
    RollbackPlanner.cs
    QuarantineService.cs
    RemediationService.cs            // tek tık devre dışı bırakma + undo
  Storage/
    SessionStore.cs
    ReportMigrator.cs
Rules/
  sentinel-rules.json
  sentinel-noise.json
  known-updaters.json
Views/Dialogs/SetupDetectedFlyoutWindow.xaml      // v2
Views/Dialogs/InstallationDeltaInspectionDialog.xaml  // v2 sekmeleri
Views/Modules/SetupHistoryModuleView.xaml         // yeni
ViewModels/SetupHistoryViewModel.cs               // yeni
ViewModels/SetupReportViewModel.cs                // code-behind'dan MVVM'e taşıma
Tests/Bakim.Tests/Sentinel/...                    // birim + golden testler
Tests/Fixtures/Installers/*.iss                   // sentetik kurulumlar
```

`InstallerMonitorService`, `SnapshotEngine` olarak `Sensors/` altına indirgenir. `IInstallerMonitorService` arayüzü, `UninstallerViewModel` taşınana kadar bir adaptörle korunur.

---

## 6. Sürüm yol haritası

| Sürüm | İçerik |
|---|---|
| v3.20.0 | Faz 0: P0 düzeltmeleri, tek depolama, test altyapısı, CI'da `dotnet test` |
| v3.21.0 | Faz 1: WMI tespiti, puanlı sınıflandırıcı, fingerprint, kaldırma oturumu, "Bakım ile İzleyerek Kur" |
| v3.22.0 | Faz 2 + 3: registry hotspot, kalıcılık diff'i, sistem durumu sensörü, sürekli baseline, gürültü filtresi, güven puanı, bundle tespiti |
| v3.23.0 | Faz 4 + Flyout v2: kural motoru, risk skoru, hızlı triage, Defender ikinci görüş |
| v3.24.0 | Faz 5: tek tık müdahale, karantina, güvenli geri alma v2, rapor ↔ kaldırıcı bağı |
| v3.25.0 | Faz 6: İnceleme v2, Kurulum Geçmişi modülü, zaman çizelgesi, HTML rapor |
| v4.0.0 | Faz 8: Tam Koruma Modu (servis, USN Journal, ETW), reboot devamı |
| v4.x | Faz 10: Sandbox önizleme, MSI statik önizleme, YARA, AMSI, sürüm farkı |

---

## 7. Başarı metrikleri

| Metrik | Hedef |
|---|---|
| Test matrisinde tespit oranı | ≥ %95 (Temel Mod), ≥ %99 (Tam Koruma) |
| Yanlış pozitif oturum | Tipik kullanımda haftada ≤ 1 |
| Kaçırılan Run / servis / görev / Uninstall | 0 (matriste) |
| Geri almada silinen önceden var olan dosya | **0 (değişmez)** |
| Boşta CPU / ek RAM | < %0.3 / < 25 MB |
| Rapor hazır olma süresi (< 5k dosya) | Kurulum bitiminden sonra ≤ 5 sn |
| Referans temiz kurulumların "Temiz/Bilgi" oranı | ≥ %90 |

---

## 8. Riskler ve dikkat edilecekler

- **Gizlilik:** Raporlarda kullanıcı adı ve yollar bulunur. Raporlar yerelde kalır, dışa aktarırken isteğe bağlı olarak maskelenir. VT'ye dosya yükleme yalnızca açık onayla yapılır; hash sorgusu da ayardan kapatılabilir olmalıdır.
- **Başka AV'lerin Bakım'ı işaretlemesi:** Süreç izleme, servis ve ETW birleşimi sezgisel tespitleri tetikler. Kod imzalama ve temiz bir davranış profili gerekir (ağ çağrılarını en aza indirmek, şifrelenmemiş karantina tutmamak).
- **Servisin yerel yetki yükseltme yüzeyi:** Faz 8'deki beyaz liste komutlar, pipe ACL'i ve istemci doğrulaması zorunludur.
- **Windows sürüm farkları:** Win10 ile Win11 arasında WMI/ETW sağlayıcıları, Sandbox erişilebilirliği ve Defender platform yolu değişebilir. Sensörler "mevcut değil" durumunu zarifçe ele almalıdır.
- **HDD performansı:** Baseline ve hash'leme I/O kısıtlamasıyla yapılır.
- **Yanlış güven hissi:** UI "Temiz" yerine "Şüpheli bir davranış bulunmadı" demeli. Motor bir antivirüsün yerini tutmaz, onu tamamlar.

---

## 9. İlk 10 iş (hemen başlanacaklar)

1. **P0-1:** `ChangeType` ayrımı yapılsın ve `RevertReportAsync` yalnızca `Created` + baseline'da olmayan dosyaları Geri Dönüşüm Kutusu'na göndersin. Önceden var olan dosyanın korunduğunu doğrulayan test eklensin.
2. **P0-2:** `ProcessInfoReader` (`QueryFullProcessImageName`, ToolHelp32 ebeveyn PID, `GetProcessTimes`) yazılsın ve `MainModule` kullanımı kaldırılsın.
3. **P0-4 + P0-5:** Run/RunOnce *değerleri* ve `SYSTEM\CurrentControlSet\Services` snapshot'a eklensin. Hive biçimi normalize edilsin (her iki görünüm).
4. **P0-6:** Tek `SessionStore`, oturum başına klasör ve `SchemaVersion` getirilsin. `InstallerMonitorService` diske yazmayı bıraksın.
5. **P0-7:** `PeriodicTimer` + `Channel` ile tek döngü kurulsun; `async void` kaldırılsın, eşzamanlı koleksiyonlara geçilsin.
6. **P0-8:** Ebeveyn PID tabanlı ağaç ve msiexec kuralı uygulansın; bitiş MSI olay günlüğünden (11707/11708/1033) izlensin.
7. **P0-9:** `SessionKind.Uninstall` eklensin; kaldırıcılar kurulum bildirimi göstermesin.
8. **P0-10:** FSW buffer 64 KB olsun; `Error` olayında yeniden tarama yapılsın ve `IsPossiblyIncomplete` bayrağı konsun. İlk gürültü filtresi (tarayıcı cache, Bakım'ın kendi klasörü) eklensin.
9. **Test:** `InstallerClassifier`'ı saf bir sınıf olarak ayırıp gerçek testler yazılsın; kopya-mantık testleri silinsin. `push`/`pull_request` için `dotnet test` iş akışı eklensin.
10. **Hızlı kazanım:** Flyout'a basit bir risk özeti eklensin: "2 imzasız yürütülebilir, 1 yeni başlangıç kaydı". Bu, "Analizörle Tara" düğmesine tıklamak için sebep verir. Tam risk motoru Faz 4'te gelir.
