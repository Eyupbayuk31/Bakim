# Kurulum Nöbetçisi v3 — Mega Geliştirme Planı

> Tarih: 26 Eylül 2026 · Temel sürüm: v4.6.3 · Önceki plan: `docs/SENTINEL_V2_PLAN.md` (v3.19.1)
>
> Kapsam: `Services/SetupSentinelService.cs`, `Services/Sentinel/**`, `Core/Sentinel/**`,
> `ViewModels/SentinelViewModel.cs`, `Views/Modules/SentinelModuleView.xaml`,
> `Views/Dialogs/SetupDetectedFlyoutWindow.*`, `Views/Dialogs/InstallationDeltaInspectionDialog.*`.

---

## 0. Kısa cevap: nöbetçi normal çalışıyor mu?

**Çalışıyor ama güvenilir değil.** v2 planının P0 maddelerinin çoğu kapatılmış: puanlı sınıflandırıcı,
değer düzeyinde Run/hizmet yakalama, sistem alanı sensörü, risk motoru ve MOTW/imza incelemesi var.
Ancak motorun temelinde bir tasarım açığı duruyor: **bir dosyanın ya da kaydın kurulum tarafından
yazıldığını kanıtlayan bir süreç atfı yok.** Kurulum süresince 7 klasörde olan her değişiklik
kuruluma yazılıyor.

Makinedeki gerçek veri bunu doğruluyor. `%APPDATA%\Bakım\InstallationLogs` altında 7 günlük
kullanımda **tek bir rapor** var ve bu rapor yanlış pozitif:

| Alan | Raporda | Gerçekte |
|---|---|---|
| Uygulama adı | "7-Zip SFX" | Kurulum çatısının adı. Süreç Riot Client'ın `DirectX_9_SDK_Install.exe` ön koşul denetimi (`C:\ProgramData\Riot Games\Metadata\…`) |
| Süre | 1,5 sn | Kullanıcının başlattığı bir kurulum değil, arka plan güncelleyicisinin çocuğu |
| Değiştirilen dosyalar | Claude çerezleri, WinGet COM günlüğü, Riot sentry dosyası … | Kurulumla ilgisiz, aynı anda çalışan uygulamaların yazdıkları |
| Silinen dosyalar | 42 (`%TEMP%\DX587D.tmp\…`) | Kurulumun kendi oluşturup sildiği geçici dosyalar. "Silindi" değil, "geçici" |
| Yeni programlar | League of Legends, Teamfight Tactics | Riot Client'ın o anda yeniden yazdığı Uninstall kayıtları |
| Karar | "Bilgi · Bu kurulum 2 program yükledi" | Yanlış paket yazılım uyarısı |

Aynı günlüklerde `Kaldırma süreci saptandı: Bakım (PID 14208)` gibi satırlar da var: Bakım'ın kendi
güncellemesi bile sınıflandırıcıdan geçiyor.

---

## 1. Röntgen (v4.6.3)

### 1.1 Bugünkü akış

```
PeriodicTimer 1,5 sn ─► NativeProcess.Snapshot ─► InstallerClassifier (puan ≥ 50)
        │ boşta: 60 sn'de bir kayıt defteri tabanı, 3 dk'da bir sistem tabanı
        ▼
StartSessionAsync ─► 7 klasörde FileSystemWatcher ─► taban seçimi ─► InstallerInspector (arka plan)
        │                                             └► InstallerMonitorService ön görüntüsü (kullanılmıyor)
        ▼
CheckActiveSessionProcessesAsync (1,5 sn) ─► ağaçta canlı süreç yok + MSI bitti ─► Finalize
        ▼
Delta (FSW olayları + USN + kayıt defteri) ─► EvaluateRiskAsync ─► SessionStore JSON ─► bildirim
```

### 1.2 İyi taraflar (korunacak)

- `ISetupSentinelService` cephesi, DI kaydı ve olay bağlama düzeni.
- Saf ve test edilmiş çekirdek: `SetupRiskEngine`, `SystemStateSnapshot.Diff`, `InstallerFingerprint`,
  `MarkOfTheWeb`, `SentinelNoise`, `SetupTrace`.
- `SystemStateSensor`: okunamayan alanı "okunamadı" olarak işaretleyip sahte "eklendi" üretmemesi doğru bir karar.
- `RollbackPlanner`: yalnızca oluşturulan dosyalar, PathSafetyGuard, Geri Dönüşüm Kutusu, kalıcı silme yok.
- `SentinelSuppression`: Bakım'ın başlattığı kaldırıcı ağacını bastırma.
- Sessiz izleme ve yalnızca sonuçta bildirim verme (UX kararı).
- `FindingActions` ile bulgu başına tek tık aksiyon ve `RootCertificateUndoHandler` ile geri alma.

### 1.3 Hatalar ve açıklar

Önem: **K** kritik (yanlış sonuç ya da güvenlik açığı), **Y** yüksek, **O** orta.

| # | Önem | Sorun | Yer | Etkisi |
|---|---|---|---|---|
| H-01 | K | Süreç atfı yok. Oturum boyunca izlenen klasörlerde olan her dosya olayı kuruluma yazılıyor | `SetupSentinelService.cs:512-515`, `:542` | Rapor, aynı anda çalışan tarayıcı, Claude, Riot, OneDrive gibi uygulamaların yazdıklarıyla kirleniyor. Geri alma bu listeye dayanırsa ilgisiz dosyalar silinir |
| H-02 | K | Kurulum sonunda uygulamayı başlatan kurulumlarda oturum kapanmıyor. Kurulan uygulama ağaçtaki bir çocuk süreç olarak izlenmeye devam ediyor | `:596-617`, `:634` | Oturum saatlerce açık kalıyor, o süredeki bütün sistem etkinliği kuruluma yazılıyor. `ExcludedProcessNames` içindeki `code`, `discord`, `spotify`, `steam` gibi girdiler bu sorunun isim listesiyle yamanmış hali |
| H-03 | K | Oturum yalnızca dosya olayı yoksa bildirim göstermiyor. Riskli ama dosya bırakmayan kurulumlar da susturuluyor | `App.xaml.cs:382` | Yalnızca Run girdisi, kök sertifika ya da Defender istisnası ekleyen bir kurulum "Tehlikeli" kararı alsa bile bildirim çıkmaz |
| H-04 | K | USN entegrasyonu işlevsiz | `SetupSentinelService.cs:727-753`, `UsnJournalSensor.cs:199` | USN imleci uygulama açılışında sabitleniyor; finalize'da 64 KB okunuyor, yani saatler önceki ilk birkaç yüz kayıt geliyor. Kayıtta yalnızca dosya adı var, yol `ProgramFiles\<ad>` diye uyduruluyor. Eklenen yollar da `report.CreatedFiles` kopyalandıktan sonra ekleniyor ve rapora hiç girmiyor. Arayüz bu sırada "Tam Koruma: NTFS USN Değişiklik Günlüğü" diyor |
| H-05 | Y | 32-bit kayıt görünümü, 64-bit anahtar adıyla yazılıyor | `RegistryHotspotSensor.cs:42`, `:52` | `Registry32` görünümünde `Software\Microsoft\…\Run` aslında `WOW6432Node\…\Run`, ama harita anahtarı 64-bit yolla oluşuyor. 64-bit değerler eziliyor, 32-bit girdiler iki kez ve biri yanlış yolla raporlanıyor. Tek tık devre dışı bırakma yanlış görünüme gidebiliyor |
| H-06 | Y | `key.Contains(@"\Run")` gevşek | `RegistryHotspotSensor.cs:82`, `:109` | `…\Uninstall\RuneLite`, `…\Uninstall\RunAsDate` gibi Uninstall anahtarları "başlangıç girdisi" sayılıyor, risk motoruna Run girdisi olarak gidiyor |
| H-07 | Y | Oturum içinde oluşturulup silinen dosyalar "Silinen" listesine giriyor | `SetupSentinelService.cs:685-689` | Örnek rapordaki 42 "silinen" dosyanın hepsi kurulumun geçici dosyası |
| H-08 | Y | `Renamed` olayı delta hesabında hiç işlenmiyor | `:683-709` | Geçici adla yazıp yeniden adlandıran kurulumların (Squirrel, Chromium tabanlılar, pek çok güncelleyici) asıl dosyaları rapora girmez, geri almada da unutulur |
| H-09 | Y | Finalize, `IsActive=false` yaptıktan sonra 1,5 sn bekliyor | `:657-664` | Bekleme amacına ulaşmıyor: bu sürede gelen olaylar `RecordFileEvent` başında atılıyor |
| H-10 | Y | `C:\Windows` izlenmiyor | `:487-496` | Risk motorundaki "System32'ye imzasız exe" kuralı (`:882`) pratikte hep boş. Sürücüler, `System32\Tasks` ve diğer sürücüler (D:\Oyunlar) kapsam dışı |
| H-11 | Y | Uygulama adı yanlış kaynaktan alınıyor | `InstallerClassifier.cs:175-178` | ProductName "7-Zip SFX", "Setup/Uninstall", "Windows Installer - Unicode" gibi çatı adları uygulama adı oluyor |
| H-12 | Y | Güncelleyici çocukları kullanıcı kurulumu sayılıyor | `SetupSentinelService.cs:334-414` | Riot, Steam, Epic, Battle.net gibi başlatıcıların arka plan kurulumları oturum açıyor. Ebeveyn bilgisi elde olduğu halde kullanılmıyor |
| H-13 | O | Eşzamanlılık: `TrackedProcessIds` bir `HashSet`. WMI iş parçacığı kilitle, döngü kilitsiz yazıyor; `IsActive` volatile değil | `:991-996`, `:601-614`, `SetupSentinelModels.cs:51` | Seyrek ama gerçek bozulma riski |
| H-14 | O | Tek oturum. Oturum sürerken yeni kurulum taranmıyor | `:221-229` | Ninite/winget toplu kurulumlarında ikinci kurulum ilkine karışır ya da hiç görülmez |
| H-15 | O | Boyut, `Created` anındaki uzunluk. Dosya o anda genellikle 0 bayt | `:554-558`, `:695` | "1,0 KB" gibi anlamsız toplamlar |
| H-16 | O | Olay iş parçacığında G/Ç: her FSW olayında `File.Exists` ve `FileInfo` | `:555-557` | Büyük kurulumlarda arabellek taşmasını hızlandırır |
| H-17 | O | Kayıt defteri farkı silinen anahtarları görmüyor | `RegistryHotspotSensor.cs:70-122` | Başka ürünün Run girdisini ya da hizmetini silen kurulum fark edilmez |
| H-18 | O | Boşta sürekli iş: 60 sn'de bir tüm `Services` + Uninstall anahtarlarının okunması, 3 dk'da bir zamanlanmış görevler (COM), sertifikalar ve güvenlik duvarı | `:416-435` | Bakım bir bakım aracı. Boşta maliyeti ölçülmüş değil |
| H-19 | O | Ölü ya da yanıltıcı kod: `InstallerMonitorService` ön görüntüsü (alınıyor, hiç okunmuyor, oturum başını geciktiriyor), `RevertReportAsync` (arayüzden çağrılmıyor), `KernelTraceSensor` (aslında WMI), `SetupSentinelTests` içindeki kopya mantık | `:473`, `:969`, `KernelTraceSensor.cs` | Bakım maliyeti ve yanlış güven |
| H-20 | O | Oturum yalnızca bellekte. Bakım çökerse ya da kurulum yeniden başlatma isterse oturum kaybolur | tüm servis | Uzun kurulumlarda (Visual Studio, Office) veri kaybı |
| H-21 | O | Rapor geçmişi sınırsız ve her açılışta tamamı JSON olarak okunuyor | `SessionStore.cs:81-110` | Büyük raporlarla (on binlerce yol) sayfa yavaşlar |
| H-22 | O | `Dispose`, çalışan döngüyü beklemeden `_finalizeLock`'u atıyor | `:1002-1011` | Kapanışta `ObjectDisposedException` olasılığı |
| H-23 | O | Güncelleme ve yeniden kurulum ayrılmıyor. `Kind` her zaman `Install` | `:446` | Var olan Uninstall kaydının `DisplayVersion` değişimi "yeni program" gibi davranabilir |

### 1.4 Kök neden

H-01, H-02, H-07, H-11, H-12 ve H-14 aynı eksiklikten doğuyor: **olayların süreçle ilişkisi yok,
oturum bir zaman penceresi.** Gürültü listesini uzatmak ve isim listesine süreç eklemek bu sorunu
çözmez, yalnızca belirtilerini erteler. v3'ün merkezinde **süreç atıflı yakalama** duruyor.

Yetki durumu buna elverişli: kurulum betiği `Bakim.exe` için `RUNASADMIN` katmanı yazıyor
(`Bakim_Setup.iss`), yani kurulu sürüm pratikte her zaman yönetici olarak çalışıyor. ETW çekirdek
sağlayıcıları bu modda kullanılabilir.

---

## 2. Hedef mimari (v3)

```
 ┌───────────────────────────── TETİKLEYİCİLER ─────────────────────────────┐
 │ ETW Kernel-Process (anında) │ Polling (yönetici değilse) │ "İzleyerek kur"  │
 │ MSI olay günlüğü │ AppX dağıtım günlüğü (MSIX/Store/winget)                  │
 └───────────────────────────────────┬──────────────────────────────────────┘
                                     ▼
   InstallerDetector: puan + ebeveyn bağlamı (güncelleyici mi, kullanıcı mı?) + imza/MOTW
                                     ▼
   SessionManager: N eşzamanlı oturum · disk günlüğü (write-ahead) · çökme/yeniden başlatma sonrası devam
                                     ▼
   ProcessTree: PID + oluşturma zamanı · MSI sunucu köprüsü · "kurulan uygulama başlatıldı" ayrımı
                                     ▼
 ┌──────────────────────── SENSÖRLER (ISentinelSensor) ────────────────────────┐
 │ Tam mod : ETW Kernel-File + Kernel-Registry (PID atıflı, tüm sürücüler)      │
 │ Temel   : FSW v3 (kuyruklu, G/Ç'siz) + zaman penceresi + güven puanı          │
 │ Kayıt   : hotspot tabanı (görünüm düzeltilmiş, silme dahil) + LastWriteTime    │
 │ Sistem  : SystemStateSensor (değişim bildirimiyle tetiklenen tabanlar)          │
 │ Kesin   : MSI bileşen sorgusu (ürün kodu → dosya/kayıt listesi), Inno unins*.dat │
 └───────────────────────────────────┬─────────────────────────────────────────┘
                                     ▼
   Olay günlüğü: SensorEvent → session.events.jsonl
                                     ▼
   DeltaBuilder (saf): Created/Modified/Deleted/Renamed/Temp · kesinlik: Kanıtlı / Olası / Zayıf
                                     ▼
   AppIdentity (ad, yayıncı, sürüm, Install/Update/Repair) → RiskEngine → Bulgular
                                     ▼
   SessionStore v4 (indeks + gzip rapor + saklama) → UI v3 (canlı kart, zaman çizelgesi, klasör ağacı)
                                     ▼
   Aksiyonlar: tara │ devre dışı bırak │ karantina │ TAM GERİ AL (kaldırıcı + kalıntı + kayıt) │ güven listesi
```

İlkeler (v2'den devralınanlar dahil):

1. **Kanıt yoksa silme yok.** Geri alma yalnızca "Kanıtlı" öğelere uygulanır. "Olası" öğeler
   kullanıcıya gösterilir ama varsayılan olarak seçilmez.
2. **Yakalama ile yorumlama ayrı.** Sensörler yalnızca olay üretir. Delta, kimlik ve risk, olay
   listesi üzerinde çalışan saf fonksiyonlardır ve kaydedilmiş olay akışlarıyla test edilir.
3. **Arayüz yeteneği dürüstçe söyler.** Rozet, gerçekten çalışan sensörleri gösterir. "Tam Koruma"
   yalnızca ETW atfı etkinse görünür.
4. **Boşta bütçe.** Nöbetçi boşta ortalama %0,3 CPU'nun ve 40 MB özel belleğin altında kalır.

---

## 3. Fazlar

Her faz kendi başına yayınlanabilir. Kabul ölçütleri sağlanmadan sonraki faza geçilmez.

### Faz A — Acil düzeltmeler ve dürüstlük (v4.7.0, ~1 hafta)

Motor mimarisine dokunmadan bugünkü raporları doğru hale getirir.

> **Durum (26 Eylül 2026): A1–A12 uygulandı.** Saf mantık Core'a taşındı: `SetupDeltaBuilder`
> (`Core/Sentinel/SetupDelta.cs`), `SetupAppName`, `SetupSessionPolicy`. Testler:
> `SentinelFazATests` (27, Core) ve `SentinelFazAWindowsTests` (Windows). A10'dan sonra
> `ExcludedProcessNames` içindeki `code`/`discord`/`spotify` yamaları bilerek yerinde bırakıldı;
> fikstürlerle (B4) doğrulandıktan sonra kaldırılacak. Gerçek kurulumlarla elle deneme henüz yapılmadı.

| İş | İçerik | Kapattığı |
|---|---|---|
| A1 | Bildirim koşulu: dosya yoksa bile risk ≥ Dikkat ya da `SystemChanges`/`AddedServices`/`AddedStartupEntries` doluysa bildir | H-03 |
| A2 | USN yolunu devre dışı bırak, `ProtectionStatus` metninden USN'yi çıkar (Faz C'de ETW ile doğru haliyle dönecek) | H-04 |
| A3 | `CaptureRunValues`/`CaptureUninstallKeys`: `Registry32` geçişini kaldır. `WOW6432Node` yolları 64-bit görünümde zaten açık okunuyor | H-05 |
| A4 | Run tespitini tam anahtar eşleşmesine çevir: `RunSubKeyPaths` içindeki bir yolun doğrudan değeri mi? | H-06 |
| A5 | Delta: oturumda oluşturulup silinen dosyalar `TempFiles` sayısına; `Renamed` → eski yol oluşturulduysa yeni yolu `Created` yap, değilse `RenamedFiles` | H-07, H-08 |
| A6 | Finalize: önce 1,5 sn bekle, sonra `IsActive=false` ve FSW'yi durdur | H-09 |
| A7 | Boyutu finalize'da `FileInfo` ile hesapla (en fazla 20.000 dosya, fazlası tahmini) | H-15 |
| A8 | Ebeveyn bağlamı: ebeveyni `ExcludedProcessNames`'teki bir güncelleyici/başlatıcı olan aday "Arka plan güncellemesi" olarak işaretlenir ve varsayılan olarak oturum açmaz (ayar: "Başlatıcıların arka plan kurulumlarını da izle") | H-12 |
| A9 | Uygulama adı önceliği: yeni Uninstall `DisplayName` → MSI ProductName → ProductName/FileDescription (çatı adı değilse) → pencere başlığı → dosya adı. Çatı adları listesi `InstallerFingerprint.DisplayName` değerlerinden türetilir | H-11 |
| A10 | "Kurulan uygulama başlatıldı" kuralı: kök süreç çıktıysa ve ağaçta kalan süreçlerin görüntü yolu bu oturumun `Created` dosyalarından biriyse bu süreçler ağaçtan ayrılır ve oturum kapanır. Ardından `code`/`discord`/`spotify` gibi yamalar isim listesinden çıkarılabilir | H-02 |
| A11 | `TrackedProcessIds` → `ConcurrentDictionary<int, byte>`, `IsActive` → volatile; `Dispose` döngüyü en fazla 2 sn bekler | H-13, H-22 |
| A12 | Ölü kod: `InstallerMonitorService` çağrısını oturum başından kaldır, `RevertReportAsync`'i arayüzden sil ya da Faz E'ye kadar `[Obsolete]` yap, `KernelTraceSensor` → `WmiProcessSensor` | H-19 |

**Kabul:** Riot DirectX denetimi oturum açmıyor. Örnek rapordaki 42 geçici dosya "Silinen" yerine
"42 geçici dosya" olarak görünüyor. Yalnızca kök sertifika ekleyen test kurulumu bildirim veriyor.
Mevcut testlerin hepsi geçiyor ve her madde için en az bir yeni test var.

### Faz B — Saf delta motoru ve test altyapısı (v4.8.0, ~2 hafta)

Faz C'deki büyük değişiklik güvenle yapılabilsin diye önce ölçüm ve test zemini kurulur.

- **B1 `DeltaBuilder` (Core, saf):** `IEnumerable<SensorEvent>` + zaman + dosya varlık sorgusu →
  `SetupDelta`. Bugün `FinalizeActiveSessionAsync` içindeki 120 satırlık mantık buraya taşınır.
- **B2 `SensorEvent` modeli:** `{Ts, Pid, ProcessCreateTicks, Kind(File|Registry|Process|System), Op, Path, OldPath, Source(Etw|Fsw|Snapshot|Msi), Confidence}`.
- **B3 Oturum günlüğü:** her oturum `%APPDATA%\Bakım\Sentinel\sessions\<id>\events.jsonl` dosyasına
  yazar (her 250 olayda ya da 2 sn'de bir). Açılışta yarım kalmış oturum bulunursa "Kurulum yarıda
  kaldı" raporu üretilir ya da oturum devam ettirilir (H-20).
- **B4 Tekrar oynatma testleri:** gerçek kurulumlardan kaydedilmiş `events.jsonl` fikstürleri ve
  beklenen raporları (altın dosyalar):
  - 7-Zip (NSIS), Notepad++ (NSIS), VS Code (Inno, "uygulamayı başlat" işaretli)
  - Chrome (Omaha, yeniden adlandırma ağırlıklı), Discord (Squirrel)
  - Riot DirectX denetimi (yanlış pozitif), sessiz MSI (`msiexec /qn`), WiX Burn paketi
  - winget ile 3 paketin art arda kurulumu
- **B5 `SessionLifecycle` testleri:** sahte saat, sahte süreç görüntüsü ve sahte sensörlerle oturum
  açma/kapama, MSI bekleme, "uygulama başlatıldı" ayrımı, 2 saat zaman aşımı.
- **B6 Performans ölçümü:** `SentinelMetrics` (tur süresi, taban süresi, olay/sn, arabellek taşması,
  bellek). Boşta 30 dakikalık ölçüm raporu `docs/`a eklenir, H-18 için hedefler buna göre belirlenir.
- **B7 Kopya mantık testlerini temizle:** `SetupSentinelTests` yalnızca gerçek sınıfları test eder.

**Kabul:** Delta ve oturum yaşam döngüsü kod kapsamı ≥ %85. 9 fikstürün hepsi altın dosyayla eşleşiyor.

### Faz C — Süreç atıflı yakalama: ETW (v4.9.0, ~3 hafta)

Planın merkezi. H-01, H-10 ve H-14'ü kökten kapatır.

- **C1 `EtwSentinelSession`:** tek gerçek zamanlı oturum (`Bakim-Sentinel`). Sağlayıcılar:
  `Microsoft-Windows-Kernel-Process` (süreç başlangıç/bitiş, görüntü yolu, ebeveyn),
  `Microsoft-Windows-Kernel-File` (Create/Write/Rename/Delete/SetInformation),
  `Microsoft-Windows-Kernel-Registry` (CreateKey/SetValue/DeleteKey/DeleteValue).
  Kütüphane: `Microsoft.Diagnostics.Tracing.TraceEvent` (ya da P/Invoke ile ince sarmalayıcı;
  karar B6 ölçümlerinden sonra verilir). Açılışta aynı adlı artık oturum kapatılır.
- **C2 Olay süzgeci çekirdekte değil, geri çağırmada:** yalnızca izlenen PID'lerin dosya ve kayıt
  olayları kuyruğa alınır. Boştayken yalnızca Kernel-Process etkin, File/Registry sağlayıcıları
  oturum açılınca etkinleştirilir. Böylece boşta maliyet neredeyse sıfır olur ve polling kapanır.
- **C3 MSI sunucu köprüsü:** Windows Installer dosyaları kurulum ağacından değil `msiexec /V`
  (SYSTEM) sürecinden yazar. Oturumda bir MSI işlemi başladığında (olay 1040) sunucu süreci ve onun
  `-Embedding` özel eylem çocukları işlem bitene (1042) kadar oturuma bağlanır. Windows Installer
  işlemleri sistem genelinde sıralı olduğu için bu atıf güvenlidir.
- **C4 Hizmet köprüsü:** kurulumun oluşturduğu hizmet (`services.exe` çocuğu) ve kurulumun
  başlattığı zamanlanmış görev süreçleri, oturum süresince oturuma bağlanır.
- **C5 N eşzamanlı oturum:** `SessionManager` her kök süreç için ayrı oturum tutar. Olay, PID'e göre
  doğru oturuma gider. Toplu kurulumlar ayrı raporlar üretir.
- **C6 Kesinlik düzeyleri:** ETW atıflı olay = Kanıtlı. FSW + zaman penceresi = Olası (Temel mod).
  Geri alma ve risk motoru bu düzeyi kullanır.
- **C7 Temel mod (yönetici değilse):** FSW v3 (olay iş parçacığında G/Ç yok, kanala yaz, arka
  planda işle — H-16), taban aralıkları değişim bildirimiyle (`RegNotifyChangeKeyValue`)
  tetiklenir (H-18). Rapor başlığında "Temel mod: bazı değişiklikler başka uygulamalara ait olabilir"
  uyarısı gösterilir.
- **C8 Kayıt defteri farkı v2:** silinen anahtar/değer (H-17), `KeyDeleted`/`ValueDeleted`,
  ETW varsa kayıt anahtarlarının tamamı, yoksa hotspot + `LastWriteTime > oturum başı` taraması.
- **C9 USN (isteğe bağlı, doğru haliyle):** oturum başında imleç alınır, sonunda o imleçten itibaren
  okunur, `ParentFileReferenceNumber` → yol çözümü `OpenFileById` ile yapılır. Yalnızca ETW
  kullanılamadığında yedek olarak.

**Kabul:** Fikstürlerde başka uygulamalara ait tek bir dosya bile "Kanıtlı" listesine girmiyor.
Chrome açıkken VS Code kurulumu yalnızca VS Code dosyalarını içeriyor. Boşta CPU < %0,3.
winget ile 3 paket art arda kurulunca 3 ayrı rapor çıkıyor.

### Faz D — Kimlik, sınıflandırma ve risk v3 (v4.10.0, ~2 hafta)

- **D1 `AppIdentity`:** ad, yayıncı (imzalayan / Uninstall `Publisher`), sürüm, ürün kodu,
  kurulum konumu. Kaynaklar: Uninstall kaydı, MSI API, kurulum dosyası, ilk yürütülebilir dosya.
- **D2 İşlem türü:** Uninstall kaydı önceden varsa `Update` (sürüm değişti) ya da `Repair` (değişmedi),
  yoksa `Install` (H-23). Geçmişte aynı uygulamanın önceki raporlarıyla karşılaştırma yapılır.
- **D3 Kesin dosya listeleri:** MSI kurulumlarında ürün kodu ile `MsiEnumComponents` /
  `MsiGetComponentPath` üzerinden Windows Installer'ın kendi listesi alınır ve ETW verisiyle
  çapraz doğrulanır. Inno Setup için `unins*.dat` okunabilirse aynı işlem yapılır.
- **D4 Risk motoru eklemeleri:**
  - Kurulum sonrası Autoruns farkı (`AutorunsScannerEngine` önce/sonra) → kaçan kalıcılık türleri
    (WMI abonelikleri, COM ele geçirme, LSA/SSP, yazdırma izleyicisi …)
  - Yeni dinleyen portlar ve kurulumun kurduğu dış bağlantılar (NetworkMonitor altyapısı)
  - Oturumda yeni tarayıcı eklentisi ya da arama motoru/ana sayfa değişikliği
  - Güvenilir yayıncı: aynı imzalayanın daha önce temiz kurulumları varsa puan düşer
  - Bilinen PUP paketleyicileri (OpenCandy, InstallCore imzaları) ve paket içi teklif tespiti
- **D5 Karar açıklaması:** her bulguda "neden" (kural, kanıt, kesinlik düzeyi) ve "ne yapmalı".

**Kabul:** Fikstürlerde ad/tür %100 doğru. Paket yazılım fikstürü Dikkat, temiz imzalı kurulumlar Temiz/Bilgi.

### Faz E — Aksiyonlar: gerçek geri alma ve izleyerek kurma (v5.0.0, ~3 hafta)

- **E1 "Bakım ile izleyerek kur":** Gezgin sağ tık menüsü (`.exe`, `.msi`, `.msix`), sürükle-bırak
  ve Nöbetçi sayfasındaki düğme. Kurulum Bakım'ın çocuğu olarak başlar, taban başlamadan önce
  alınır, atıf kusursuzdur. Seçenekler: geri yükleme noktası oluştur (`CreateRestorePointAsync`
  mevcut), önce Windows Sandbox'ta dene (`WindowsSandboxService` mevcut).
- **E2 "Kurulumu tamamen geri al":** tek düğmede sırasıyla
  1. Uygulamanın kendi kaldırıcısını çalıştır (`UninstallRunner`, `SentinelSuppression` ile),
  2. Kalıntıları rapordaki "Kanıtlı" oluşturulanlar listesiyle temizle (`ResidualScannerEngine` +
     `RollbackPlanner`, Geri Dönüşüm Kutusu),
  3. Eklenen Run girdilerini, hizmetleri, görevleri, güvenlik duvarı kurallarını ve sertifikaları
     `FindingActions` ve `UndoJournal` ile geri al,
  4. Değiştirilen sistem ayarlarını (PATH, proxy, hosts) önceki değerine döndür (taban değeri
     rapora kaydedilir).
  Her adım önizlenir ve geri alma işlemi Etkinlik Merkezi'nde geri alınabilir.
- **E3 Karantina:** şüpheli yürütülebilir dosyalar ACL'si kısıtlanmış karantina klasörüne taşınır
  ve geri yüklenebilir.
- **E4 Güven listesi:** yayıncı ya da SHA-256 bazında "bu yayıncının kurulumlarını bildirme".

**Kabul:** 9 fikstür kurulumunun hepsi tam geri alma sonrası taban durumuna döner (Sandbox
içinde otomatik test). Kanıtsız hiçbir dosya silinmez.

### Faz F — Arayüz v3 (v5.1.0, ~2 hafta, Fluent 2 cam sistemiyle)

- **F1 Canlı oturum kartı:** Nöbetçi sayfasının üstünde ve tepsi açılır penceresinde; süreç ağacı,
  anlık dosya/kayıt sayaçları, "şu an yazıyor: …", kesinlik rozeti, "izlemeyi bırak" düğmesi.
- **F2 İnceleme penceresi v3:** Özet / Zaman çizelgesi / Dosyalar (klasör ağacı, kesinlik süzgeci,
  "geçici dosyaları göster") / Kayıt defteri / Sistem / Risk / Aksiyonlar sekmeleri.
- **F3 Geçmiş:** yayıncıya ve karara göre süzgeç, aynı uygulamanın iki kurulumunu karşılaştırma,
  HTML/JSON dışa aktarma, rapor silme.
- **F4 Bildirim v3:** karar rengi, en önemli bulgu, üç aksiyon (İncele / Tara / Geri al).
  Oyun Modu ve tam ekran sırasında kuyruğa al.
- **F5 Ayarlar:** izleme kapsamı (sürücüler), arka plan güncellemeleri, saklama süresi, bildirim
  düzeyi, güven listesi, "Tam Koruma" durumu ve eksik yetenekler için açıklama.

### Faz G — Depolama, performans, dayanıklılık (sürekli, v4.8'den itibaren)

- **G1 SessionStore v4:** `index.json` (liste için özet satırlar) + `reports/<id>.json.gz`. Liste
  açılışında yalnızca indeks okunur (H-21). Şema v3 → v4 göçü ve eski dosyaların okunması.
- **G2 Saklama:** varsayılan 180 gün / 300 rapor, riskli raporlar süresiz. Tek raporda 50.000 yol
  sınırı ve "kırpıldı" işareti.
- **G3 Boşta bütçe testleri:** B6 ölçümleri CI'da gerileme testi olarak çalışır.
- **G4 Yeniden başlatma isteyen kurulumlar:** `PendingFileRenameOperations` ve `RunOnce` farkı
  rapora "yeniden başlatmadan sonra tamamlanacak" olarak girer. Sonraki açılışta oturum tamamlanır.
- **G5 Hata dayanıklılığı:** ETW oturumu düşerse Temel moda sessiz geçiş, arayüzde rozet değişimi, günlük kaydı.

### Faz H — Vizyon (v5.x)

- **H1 Çalıştırma öncesi kapı (isteğe bağlı):** ETW süreç başlangıcında imzasız ve İnternet'ten
  indirilmiş bir kurulum görülürse süreç askıya alınır (`SuspendProcessAsync` altyapısı mevcut)
  ve kullanıcıya "Devam et / Sandbox'ta dene / Engelle" sorulur. Varsayılan kapalı. Yanlış
  pozitif ve zaman aşımı (30 sn sonra otomatik devam) kuralları Faz D verisiyle belirlenir.
- **H2 MSIX / Store / winget:** `Microsoft-Windows-AppXDeploymentServer/Operational` günlüğünden
  paket kurulumları, kapsayıcı içinde olduğu için hafif "paket kuruldu" raporu.
- **H3 İtibar:** SHA-256 ile isteğe bağlı VirusTotal sorgusu (`FileThreatAnalyzerService` altyapısı),
  yerel "bu makinede daha önce görüldü" veritabanı.
- **H4 Kural seti dosyası:** risk kuralları ve gürültü listeleri JSON olarak, sürümlenmiş, testli.

---

## 4. Veri modeli v4 (özet)

```csharp
record SensorEvent(DateTime Ts, int Pid, long ProcTicks, EventKind Kind, string Op,
                   string Path, string? OldPath, EventSource Source, Certainty Certainty);

enum Certainty { Proven, Likely, Weak }          // ETW / FSW+pencere / yalnızca zaman

class SetupDeltaReport // SchemaVersion = 4
{
    AppIdentity Identity;                         // ad, yayıncı, sürüm, ürün kodu, tür
    SensorCoverage Coverage;                      // hangi sensörler çalıştı, taşma, kırpma
    List<FileChange> Files;                       // yol, işlem, boyut, kesinlik, pid, exe mi
    int TempFileCount;
    List<RegistryChange> Registry;                // görünüm dahil, silme dahil
    List<SystemChange> SystemChanges;             // + önceki değer (geri alma için)
    List<ProcessNode> ProcessTree;
    InstallerInfo Installer;
    RiskResult Risk;
    RebootPending? Reboot;
}
```

v3 alanları (`CreatedFiles`, `AddedExecutables` …) okuma tarafında hesaplanan özellikler olarak
korunur, eski raporlar göç sırasında `Certainty.Likely` alır.

---

## 5. Kod organizasyonu

```
Core/Sentinel/
  Events/        SensorEvent, EventKind, Certainty
  Delta/         DeltaBuilder, TempChurnFilter, RenameResolver          (saf, Faz B)
  Identity/      AppIdentityResolver, FrameworkNames                    (saf, Faz D)
  Risk/          SetupRiskEngine (+ yeni kurallar)
Services/Sentinel/
  Session/       SessionManager, SessionJournal, ProcessTree, MsiBridge
  Sensors/       EtwSentinelSession, FswSensor, RegistryHotspotSensor, SystemStateSensor, UsnSensor
  Detection/     InstallerDetector (+ ebeveyn bağlamı), InstallerInspector, MsiEventLogWatcher
  Actions/       FullRollbackOrchestrator, RollbackPlanner, FindingActions, Quarantine
  Storage/       SessionStore v4, ReportIndex, Migration
Services/SetupSentinelService.cs → ince cephe (ISetupSentinelService korunur)
```

`SetupSentinelService.cs` bugün 1013 satır. Hedef: 250 satırın altında bir cephe.

---

## 6. Sürüm yol haritası

| Sürüm | Faz | Kullanıcının gördüğü |
|---|---|---|
| v4.7.0 | A | Doğru bildirimler, doğru adlar, başlatıcı gürültüsü yok, dürüst koruma rozeti |
| v4.8.0 | B + G1-G2 | Çökme sonrası devam, hızlı geçmiş, altyapı |
| v4.9.0 | C | "Tam Koruma" gerçekten tam: yalnızca kurulumun yaptıkları, eşzamanlı kurulumlar |
| v4.10.0 | D | Güncelleme/onarım ayrımı, daha isabetli risk, açıklamalı kararlar |
| v5.0.0 | E | İzleyerek kur, tek tık tam geri alma, karantina |
| v5.1.0 | F | Canlı oturum kartı, yeni inceleme penceresi |
| v5.x | H | Çalıştırma öncesi kapı, MSIX, itibar |

---

## 7. Başarı ölçütleri

| Ölçüt | Bugün (tahmini) | Hedef |
|---|---|---|
| Yanlış pozitif oturum (kullanıcının başlatmadığı) | Gözlenen tek rapor yanlış pozitif | < %5 |
| Rapordaki ilgisiz dosya oranı (Tam mod) | Yüksek, ölçülmüyor | 0 (Kanıtlı listede) |
| Gözden kaçan kurulum (fikstür seti) | Ölçülmüyor | 0 / 9 |
| Oturum kapanış gecikmesi (uygulamayı başlatan kurulum) | Uygulama kapanana dek | < 5 sn |
| Boşta CPU / bellek | Ölçülmüyor | < %0,3 / < 40 MB |
| Tam geri alma sonrası kalıntı | Özellik yok | 0 (Sandbox testleri) |
| Sentinel kod kapsamı | Çekirdek kısmen | ≥ %85 (Core), ≥ %60 (Services) |

---

## 8. Riskler

- **ETW olay hacmi:** Kernel-File çok gürültülü. Süzgeç geri çağırmada ve yalnızca oturum açıkken
  etkin olmalı. Kuyruk dolarsa olay düşürülür ve `Coverage.Dropped` artar, uygulama donmaz.
- **ETW oturum sınırı ve artık oturumlar:** sistemde en fazla 64 oturum var. Sabit ad, açılışta
  temizleme ve `Dispose` içinde kapatma zorunlu.
- **Geri almanın yıkıcılığı:** yalnızca Kanıtlı öğeler, önizleme, Geri Dönüşüm Kutusu, UndoJournal.
  PathSafetyGuard her zaman devrede.
- **Çalıştırma öncesi kapı:** kullanıcının işini bölebilir, varsayılan kapalı kalır.
- **Yetki:** kurulu sürüm yönetici çalışıyor, ama geliştirme derlemesi ve katmanın silindiği durumlar
  Temel modda kalır. Arayüz bunu açıkça söyler.
- **Bağımlılık:** TraceEvent yaklaşık 3–5 MB ekler. Alternatif olan ince P/Invoke sarmalayıcının
  bakım maliyeti daha yüksek. Karar B6'dan sonra verilecek.

---

## 9. İlk 10 iş (hemen başlanacaklar)

1. A1 — Riskli ama dosyasız kurulumlarda bildirimi aç (`App.xaml.cs:382`).
2. A3 + A4 — Kayıt görünümü hatası ve `\Run` eşleşmesi, testleriyle birlikte.
3. A5 + A6 — Geçici dosya, yeniden adlandırma ve finalize sırası.
4. A8 — Ebeveyn bağlamı: başlatıcı ve güncelleyici çocuklarını sustur (Riot fikstürü).
5. A9 — Uygulama adı önceliği ve çatı adı kara listesi.
6. A10 — "Kurulan uygulama başlatıldı" kuralı ve isim yamalarının kaldırılması.
7. A2 + A12 — USN ve ölü kodun kaldırılması, dürüst koruma rozeti.
8. B1 + B2 — `DeltaBuilder`'ı Core'a taşı, mevcut davranışı testle dondur.
9. B6 — Boşta performans ölçümü ve raporu.
10. B4 — İlk 3 gerçek kurulum fikstürünü kaydet (7-Zip, VS Code, Riot DirectX).
