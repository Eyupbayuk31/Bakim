# Analizör v3 — Ultra Mega Geliştirme Planı

> Tarih: 27 Eylül 2026 · Temel sürüm: v4.7.1 · Önceki kapsam: `MASTER_PLAN.md` §5.13, §5.14, §6
>
> Kapsam: `ViewModels/AnalyzerViewModel.cs`, `Services/AutorunsScannerEngine.cs`,
> `Services/FileThreatAnalyzerService.cs`, `Services/VirusTotalCheckService.cs`, `Models/PersistenceItem.cs`,
> `ViewModels/ThreatAnalysisViewModel.cs`, `Views/Modules/AnalyzerView.xaml`, `Views/Dialogs/ThreatAnalysisDialog.*`,
> `Core/History/**`, `Services/History/**`.

---

## 0. Kısa cevap: Analizör güvenilir mi?

**Hayır.** Geçmiş/Değişiklikler altyapısı sağlam, ama tarama ve puanlama katmanı bugün yanlış sonuç
üretiyor. Bunu tahminle değil, motoru bu makinede çalıştırıp ölçerek söylüyorum.

### 0.1 Kalıcılık taraması (bu makine, 607 girdi, 4,96 sn)

| Kategori | Girdi | "Dosya bulunamadı" görünen | Gerçek neden |
|---|---|---|---|
| Zamanlanmış görev | 365 | **363 (%99)** | Tırnaksız `C:\Program Files\…` yolu `C:\Program` diye kesiliyor; `schtasks` çıktısı yanlış kod sayfasıyla okunuyor ("Bak�m") |
| Windows hizmeti | 213 | **135 (%63)** | `\SystemRoot\…`, `System32\drivers\…` gibi sürücü yolları çözülmüyor |
| Run anahtarları | 14 | 5 | Aynı tırnaksız yol hatası (`C:\Riot`, `C:\Program`) |
| Winlogon | 2 | **2** | Sağlam Windows değerleri: `explorer.exe` (göreli), `userinit.exe,` (sondaki virgül) |

Ek bulgular:
- **Görev durumu okunamıyor:** Türkçe "Devre Dışı" bozuk karakterle geldiği için **bütün görevler etkin** görünüyor.
- **Başlık satırları görev sanılıyor:** `schtasks /V` her klasörde başlığı tekrarlıyor; 41 görev adı tekrar ediyor, "TaskName" adlı sahte görevler var.
- **Hizmet DLL'leri görünmüyor:** 61 hizmet yalnızca `svchost.exe` olarak görünüyor. Asıl yük olan `ServiceDll` hiç incelenmiyor; kötü amaçlı yazılımların en sevdiği kalıcılık yollarından biri tamamen kör.
- **Katalogla imzalı Windows dosyaları "imzasız" sayılıyor:** `SecurityHealthSystray.exe`, `spoolsv.exe` gibi dosyalar yalnızca "C:\Windows altında" kuralıyla gizleniyor.

### 0.2 Derin analiz (temiz, bilinen dosyalar)

| Dosya | Puan | Öneri metni | Neden |
|---|---|---|---|
| `C:\Windows\explorer.exe` (Microsoft imzalı) | **80** | **"KRİTİK UYARI … derhal kalıcı olarak silinmesi önerilir"** | explorer.exe System32'de değil `C:\Windows`'ta durur; kural bunu "sistem dosyası taklidi" (+40) sayıyor. Ayrıca +30 enjeksiyon, +20 "keylogger" API'si |
| Bakım'ın kendisi (`Bakim.exe`) | **65** | "KRİTİK UYARI" | İmzasız (+25), enjeksiyon API'leri (+30), VT kaydı yok (+10) |
| Notepad++ (imzalı) | 40 | "DİKKAT" | `GetKeyState`/`GetClipboardData` "keylogger" (+20), enjeksiyon (+30) |
| OBS (imzalı) | 20 | Güvenli | Enjeksiyon API'leri (+30) |
| Riot Client, GUP.exe (imzalı) | 10 | Güvenli | "Keylogger" API'leri |

Ters yöndeki hata daha tehlikeli: **VirusTotal tespiti en fazla +25 puan ekliyor.** 60 antivirüsün zararlı
dediği ama geçerli bir sertifikayla (çalıntı sertifika vakaları) imzalanmış dosya **25 puan → "GÜVENLİ"**
önerisi alıyor.

Silme tarafı: `PathSafetyGuard` Windows klasörünü tümüyle koruduğu için explorer.exe gerçekten silinemez.
Ama Program Files altındaki meşru uygulamalar ("DİKKAT" alan Notepad++ gibi) korunmuyor.

---

## 1. Röntgen (v4.7.1)

### 1.1 Bugünkü akış

```
AnalyzerViewModel ctor ─► ScanAsync (UI iş parçacığında başlar)
   └► AutorunsScannerEngine.ScanAllAsync — 7 kategori, sırayla
        Run / Startup / Winlogon+IFEO / Shell  → UI iş parçacığında (imza + SHA-256 + simge)
        Görevler: schtasks /Query /FO CSV /V     → Task.Run
        Hizmetler: Services anahtarı (Start ≤ 2) → Task.Run
        WMI: yalnızca CommandLineEventConsumer
   └► her girdi için Dispatcher.Invoke(Items.Add) → sıralama → anlık görüntü → fark
Satır risk puanı: PersistenceItem.RiskScore (imza + kategori + yaş)
"İncele": FileThreatAnalyzerService (PE, imza, konum, entropi, API, MOTW, VT) → ThreatAnalysisDialog
```

### 1.2 İyi taraflar (korunacak)

- **Geçmiş altyapısı:** `Core/History` (JSONL + sıkıştırma, 50 anlık görüntü sınırı, kullanıcı adı maskeli dışa aktarma, bozuk satır toleransı) sağlam ve testli.
- **Güvenli değiştirme ve silme:** Başlangıç girdileri StartupApproved ile açılıp kapanıyor (Görev Yöneticisi ile aynı yöntem). Kayıt defteri silmeleri yedekli, kısayollar Geri Dönüşüm Kutusu'na gidiyor, her işlem Etkinlik Merkezi'ne yazılıyor.
- **`FileThreatAnalyzerService`:** Katalog destekli `SignatureInspector`, PE başlığı ve mitigasyon okuması, MOTW, hash itibar önbelleği.
- **VirusTotal:** Anahtar DPAPI ile saklanıyor; dosya yükleme açık onayla yapılıyor.
- **Silme koruması:** `PathSafetyGuard`, `SafeDeleteService`, `SafeProcessService` doğru kullanılıyor.

### 1.3 Hatalar ve açıklar

Önem: **K** kritik (yanlış sonuç, güvenlik ya da sistem bütünlüğü riski), **Y** yüksek, **O** orta.

| # | Önem | Sorun | Yer | Etkisi |
|---|---|---|---|---|
| AN-01 | K | Her satırda açma/kapama anahtarı var; hizmetler ve **açılış/sistem sürücüleri** dahil. `CriticalServicePolicy` atlanıyor. Yeniden açma her zaman `Automatic` yazıyor, özgün başlangıç türünü (Boot/System/Gecikmeli) kaybediyor | `AnalyzerView.xaml:309`, `AutorunsScannerEngine.cs:632-638` | Bir disk ya da filtre sürücüsünü kapatıp açmak sistemi açılmaz hale getirebilir (INACCESSIBLE_BOOT_DEVICE) |
| AN-02 | K | "Microsoft'u gizle" (varsayılan açık) kuralı: yayıncı metninde "Microsoft" geçiyor **ya da** dosya `C:\Windows` altında. Metin, **geçersiz** imzadaki sertifika adından da okunuyor | `AutorunsScannerEngine.cs:799-803` | `C:\Windows` altına bırakılan imzasız zararlı ve sahte "Microsoft Corporation" sertifikalı dosya **varsayılan görünümde gizli** |
| AN-03 | K | explorer.exe "sistem dosyası taklidi" sayılıyor (yasal konum listesinde yalnızca System32/SysWOW64 var); öneri metni "kalıcı olarak silin" diyor | `FileThreatAnalyzerService.cs:32-35`, `:187-201`, `:458-461` | Windows'un kendi dosyasına "KRİTİK, silin" öneriliyor |
| AN-04 | K | VirusTotal ağırlığı tavanlı (≥4 motor +25). `suspicious` sayılmıyor | `FileThreatAnalyzerService.cs:405-426`, `VirusTotalCheckService.cs:188-194` | İmzalı ve yaygın biçimde tespit edilen zararlı "GÜVENLİ" önerisi alıyor |
| AN-05 | Y | Komut satırı ayrıştırıcısı ilk boşlukta kesiyor; göreli yol, sondaki virgül, `\SystemRoot\`, `\??\`, `System32\drivers` göreli yolu desteklenmiyor | `AutorunsScannerEngine.cs:903-928` | Tablo 0.1: yüzlerce sahte "yetim kayıt"; imza, hash ve risk yanlış dosya için hesaplanıyor |
| AN-06 | Y | Görevler `schtasks` CSV'sinden okunuyor: kod sayfası yanlış (UTF-8 sanılıyor), başlıklar tekrar ediyor, yerelleştirilmiş sütun adları, tek eylem, COM işleyicili görevler yok | `AutorunsScannerEngine.cs:366-449` | Tüm görevler "etkin", 41 tekrar, 363 sahte yetim. Oysa depoda COM tabanlı `Helpers/TaskSchedulerReader.cs` var |
| AN-07 | Y | `ServiceDll` okunmuyor, sürücüler ve hizmetler aynı listede, `FailureCommand` yok | `AutorunsScannerEngine.cs:455-508` | svchost ile barınan hizmet kalıcılığı görünmez |
| AN-08 | Y | Yaygın API'ler kötü sayılıyor: `GetKeyState`, `GetClipboardData` → "Keylogger"; tek başına `VirtualAllocEx`/`WriteProcessMemory` → "Kritik enjeksiyon" | `FileThreatAnalyzerService.cs:597-636`, `:1098-1122` | Notepad++, explorer, OBS, Riot yanlış alarm |
| AN-09 | Y | Üç ayrı ve birbiriyle tutarsız risk modeli: liste (`PersistenceItem.RiskScore`, 60/30), derin analiz (61/26), Nöbetçi (`SetupRiskEngine`). Derin analizin puanı listeye yansımıyor | `PersistenceItem.cs`, `FileThreatAnalyzerService.cs:455-470`, `Core/Sentinel/SetupRisk.cs` | Pencere "KRİTİK" derken liste "DÜŞÜK" gösterebiliyor |
| AN-10 | Y | Kapsam dar: WMI'da yalnızca CommandLineEventConsumer (ActiveScriptEventConsumer ve filtre/bağlama zinciri yok); IFEO'da yalnızca Debugger (SilentProcessExit ve WOW6432 yok); Active Setup, AppInit/AppCert, LSA paketleri, yazdırma izleyicileri, Winsock, BootExecute, KnownDLLs, komut istemi AutoRun, tarayıcı eklentileri, Office eklentileri yok | `AutorunsScannerEngine.cs` | Kalıcılık tekniklerinin önemli bir kısmı kör nokta |
| AN-11 | Y | Toplu VirusTotal taraması istekler arasında 400 ms bekliyor; ücretsiz API dakikada 4 istek. 429 yanıtı ayrıca ele alınmıyor | `AnalyzerViewModel.cs:602`, `VirusTotalCheckService.cs:176-179` | 5. girdiden sonra hepsi "VT HTTP 429" |
| AN-12 | Y | Tarama UI iş parçacığında başlıyor: Run/Başlangıç/Winlogon/Shell için imza + SHA-256 + simge UI iş parçacığında; her girdi için ayrı `Dispatcher.Invoke` | `AnalyzerViewModel.cs:154`, `:314-320` | Sayfa açılışında ~1,2 sn donma; kurulum sayısı arttıkça büyür |
| AN-13 | O | "AI" iddiası: "Sezgisel AI tehdit analizi" metni; yapay zekâ kullanılmıyor | `AnalyzerViewModel.cs:709`, `:822`, `UninstallerViewModel.cs:860` | Yanıltıcı ürün dili (Nöbetçi'deki "Tam Koruma" sorununun aynısı) |
| AN-14 | O | Nöbetçi'den gelen dosyalar `PersistenceCategory.StartupFolder` olarak listeye ekleniyor | `AnalyzerViewModel.cs:908-917` | "Başlangıç" süzgecini kirletiyor; bu satırlardaki sil/kapat düğmeleri yanlış yolla çalışıyor |
| AN-15 | O | Yazılabilir yapılamayan kategoriler (WMI, Winlogon, Shell) için de anahtar gösteriliyor; hata metni "yönetici izni gerekebilir" | `AnalyzerViewModel.cs:389-403` | Kullanıcı yanlış yönlendiriliyor |
| AN-16 | O | CSV dışa aktarmada formül enjeksiyonu kaçışı yok (`=`, `+`, `-`, `@`); risk sütunu yok | `AnalyzerViewModel.cs:684-690` | Excel'de açılan raporda komut enjeksiyonu riski |
| AN-17 | O | `TotalScanners` hiç doldurulmuyor | `AnalyzerViewModel.cs:480`, `:541-543` | "VirusTotal'da aç" her zaman arama sayfasını açıyor, "yükle" yolu hiç çalışmıyor |
| AN-18 | O | Güven listesi (`IsTrustedHash`) yalnızca geçmiş kaydını etkiliyor; listeyi ve puanı etkilemiyor | `AnalysisHistoryService.cs:106`, `:187` | "Güvenli işaretle" listede hiçbir şey değiştirmiyor |
| AN-19 | O | Risk puanı hesaplanan bir özellik: her okumada `File.Exists`; sıralama ve istatistik yüzlerce kez disk sorguluyor. İmza değişince `PropertyChanged` yok | `PersistenceItem.cs` (`HasValidFile`, `RiskScore`) | Gereksiz G/Ç; derin analiz sonrası satır güncellenmiyor |
| AN-20 | O | 44 `MessageBox.Show` çağrısı (Fluent tasarım dışı, engelleyici, klavye erişimi zayıf) | `AnalyzerViewModel.cs` (15), `ThreatAnalysisViewModel.cs` (21), `HunterActionDialog` (6), geçmiş (2) | Tasarım sistemiyle çelişiyor |
| AN-21 | O | Sayfa bir `IModuleViewModel` değil: tarama yalnızca ilk açılışta yapılıyor; Nöbetçi'den gelen dosya ile ilk tarama yarışabiliyor | `AnalyzerViewModel.cs:20`, `:154`, `:862-960` | Eski liste; aynı anda iki işlem `IsScanning` bayrağını paylaşıyor |
| AN-22 | O | Argüman sezgileri alt dize: `/s` her `/s…` ile, `-enc` `-encoding` ile eşleşiyor; entropi dosyanın ilk 2 MB'ı üzerinden (kurulum ve kaynak paketleri "packer" sanılıyor) | `FileThreatAnalyzerService.cs:254-256`, `:339-355` | Yanlış pozitif |
| AN-23 | O | `WINTRUST_DATA.dwProvFlags = 0x40 \| 0x10`: zincir iptal denetimi ile "denetim yok" bayrakları birlikte; tarama motoru katalog imzasını görmüyor, derin analiz görüyor (iki ayrı imza yolu) | `AutorunsScannerEngine.cs:65`, `:822-871` | Tutarsız imza sonucu, olası ağ gecikmesi |
| AN-24 | O | Kabuk uzantıları `*`, `Directory` ve `Folder` altında ayrı ayrı tekrar ediyor | `AutorunsScannerEngine.cs:558-597` | Aynı DLL listede 2-3 kez |

### 1.4 Kök nedenler

1. **Ham metin → yol çevirisi yok.** Windows'un bir komut satırını nasıl çözdüğünü (tırnak, boşluk, `PATH`,
   ortam değişkeni, `\SystemRoot`, `rundll32`/`regsvr32`/`cmd /c`/`powershell`/`wscript` sarmalayıcıları)
   taklit eden tek bir çözümleyici olmadığı için her kategori yolu kendince kesiyor. Tablo 0.1'in tamamı buradan çıkıyor.
2. **Görünürlük ile güven karıştırılmış.** "Microsoft'u gizle" bir güven kararı; ama doğrulanmış imzaya
   değil, yola ve metne bakıyor.
3. **Kanıt modeli yok, toplama puanı var.** Kurallar birbirinden habersiz puan ekliyor; güçlü kanıt (VT
   uzlaşısı, geçersiz imza) ile zayıf sinyal (`GetKeyState`) aynı terazide. Üç ayrı motor aynı işi farklı yapıyor.
4. **Eylem politikası yok.** "Bu kategori/öğe için hangi eylem güvenli?" sorusu arayüzde (her satıra anahtar)
   ve motorda (switch) dağınık; `CriticalServicePolicy` gibi mevcut korumalar devreye girmiyor.

---

## 2. Hedef mimari (v3)

```
 ┌────────────────────────── TOPLAYICILAR (IAutorunCollector) ──────────────────────────┐
 │ Run/RunOnce/RunOnceEx (tüm kovanlar + görünümler) · Politikalar · Başlangıç klasörleri  │
 │ Görevler (COM) · Hizmetler (+ServiceDll, FailureCommand) · Sürücüler (ayrı)             │
 │ WMI (CommandLine + ActiveScript + Filter/Binding zinciri) · Winlogon · IFEO (+SPE)      │
 │ AppInit/AppCert · LSA · Yazdırma · Winsock · BootExecute · KnownDLLs · Active Setup     │
 │ Explorer (ContextMenu, IconOverlay, SSODL, ShellExecuteHooks, BHO) · Komut istemi AutoRun│
 │ Tarayıcı eklentileri (Chromium/Firefox + politikalar) · Office eklentileri              │
 └──────────────────────────────────────┬─────────────────────────────────────────────────┘
                                        ▼  RawEntry {konum, ham komut, bağlam}
   CommandLineResolver (Core, saf): tırnak/boşluk, ortam, \SystemRoot, \??\, göreli → PATH,
                                   sarmalayıcı açma (rundll32, regsvr32, cmd, powershell, wscript, mshta)
                                        ▼  ResolvedTarget {exe, gerçek hedef, argümanlar, betik}
   Enricher (önbellekli: yol+boyut+mtime): SignatureInspector (katalog dahil), hash (tembel),
                                   sürüm bilgisi, MOTW, oluşturma zamanı, çalışan süreç
                                        ▼
   Evidence (Core, saf): tipli kanıtlar {tür, ağırlık, güven, açıklama}
                                        ▼
   UnifiedRiskEngine (Core/Risk; Nöbetçi ile ORTAK): kalibre puan + karar + "neden"
                                        ▼
   ActionPolicy (Core, saf): kategori + öğe → izinli eylemler (kapat/sil/karantina/yok) ve gerekçe
                                        ▼
   UI v3 (ana-ayrıntı, kanıt paneli)  ·  Geçmiş/Değişiklikler (ayrıştırıcı sürümlü)  ·  Canlı izleme
```

İlkeler:

1. **Önce doğru yol, sonra hüküm.** Hiçbir kural, çözümlenmemiş bir hedef üzerinde çalışmaz; çözümlenemeyen
   hedef "yetim" değil "çözümlenemedi" olarak ayrı gösterilir.
2. **Gizleme yalnızca doğrulanmış güvene dayanır.** "Microsoft'u gizle" = geçerli imza (katalog dahil) + imzalayan
   Microsoft kök zinciri. Geçersiz imza hiçbir süzgeçle gizlenmez.
3. **Kanıtın gücü türüne göre.** VT uzlaşısı ve geçersiz imza baskın kanıttır; API içe aktarımları, entropi ve
   konum destekleyici kanıttır ve tek başına "Kritik" üretemez.
4. **Eylem politikası kodda, arayüz politikadan okur.** Açılış/sistem sürücüleri ve kritik hizmetler için anahtar
   gösterilmez; her eylem özgün durumu geri alınabilir biçimde kaydeder.
5. **Tek risk dili.** Analizör, Nöbetçi ve Kaldırıcı aynı karar etiketlerini ve eşikleri kullanır.

---

## 3. Fazlar

### Faz 0 — Acil güvenlik ve doğruluk (v4.8.0, ~1,5 hafta)

| İş | İçerik | Kapattığı |
|---|---|---|
| Z1 | **Eylem politikası:** Sürücüler (Type 1/2), Start 0/1 ve `CriticalServicePolicy` listesindekiler için anahtar gizlenir. Kapatırken özgün `Start` ve `DelayedAutostart` UndoJournal'a yazılır; açma özgün değeri geri yükler (`Automatic` sabiti kalkar) | AN-01 |
| Z2 | **Güvenilir gizleme:** `IsMicrosoft` = `SignatureInspector` doğrulaması (katalog dahil) + Microsoft imzacısı. Yol kuralı kalkar. Geçersiz imza asla gizlenmez. Süzgeç etiketi "Doğrulanmış Microsoft girdilerini gizle" olur | AN-02, AN-23 |
| Z3 | **Sistem dosyası konum tablosu:** Kritik exe → yasal dizinler (`explorer.exe` → `%windir%`; `svchost.exe` → System32/SysWOW64 …). Öneri metinleri yumuşatılır: "kalıcı olarak silin" yerine "kaynağını doğrulayın, gerekirse karantinaya alın" | AN-03 |
| Z4 | **VirusTotal baskınlığı:** ≥ 10 motor (ya da toplamın ≥ %15'i) → puan en az 75 ("Tehlikeli"); 3–9 → en az 50; `suspicious` yarım ağırlıkla sayılır; imza bunu düşüremez | AN-04 |
| Z5 | **API kataloğu:** `GetKeyState`, `GetClipboardData`, tek başına `OpenProcess` → bilgi. "Enjeksiyon" yalnızca üçlü birlikteyse (`VirtualAllocEx` + `WriteProcessMemory` + `CreateRemoteThread`/`QueueUserAPC`/`SetThreadContext`) ve dosya güvenilir imzalı değilse kritik; güvenilir imzalıysa bilgi | AN-08 |
| Z6 | **`CommandLineResolver` (Core, saf):** `Core.Uninstall.UninstallCommandParser` genişletilir. Tırnaksız boşluklu yol (kısa yoldan uzun yola deneme), sondaki virgül/noktalı virgül, ortam değişkenleri, `\SystemRoot\`, `\??\`, `System32\drivers\x.sys`, göreli ad → `%windir%`, System32, `PATH` araması | AN-05 |
| Z7 | **Görevler COM ile:** `Helpers/TaskSchedulerReader` kullanılır: doğru kodlama, etkin/devre dışı, gizli görev, çoklu eylem, COM işleyicili görev, yazar, tetikleyiciler. `schtasks` CSV'si tamamen kalkar | AN-06 |
| Z8 | **Arka plan tarama:** Tüm toplama `Task.Run` içinde, sonuçlar 50'lik gruplar halinde UI'ya aktarılır. İptal belirteci eklenir. Sayfa `IModuleViewModel` olur: ilk açılışta ve 10 dakikadan eski listede yeniden tarar | AN-12, AN-21 |
| Z9 | **VT hız sınırı:** Belirteç kovası (ücretsiz: 4/dk, 500/gün; ücretli anahtar ayarı), 429'da `Retry-After`'a uyma, kuyruk ilerlemesi ("3 dk kaldı"), `TotalScanners` doldurulur | AN-11, AN-17 |
| Z10 | **Dürüst dil:** "AI" ifadeleri kaldırılır ("Sezgisel dosya analizi"); öneri metinleri kanıt düzeyiyle orantılı | AN-13 |
| Z11 | **Küçük düzeltmeler:** CSV formül kaçışı ve risk/karar sütunları (AN-16); Nöbetçi dosyaları ayrı "İncelenen dosyalar" kategorisi (AN-14); yazılamayan kategorilerde anahtar yerine kilit simgesi ve açıklama (AN-15); kabuk uzantısı tekilleştirme (AN-24) | AN-14/15/16/24 |

> **Durum (27 Eylül 2026, v4.8.0): Z1–Z11 uygulandı**, Faz 7'nin `ParserVersion` parçası ve Faz 1'in ucuz
> konumları (HKLM Politika Run, WOW RunOnce, IFEO 32 bit, SilentProcessExit, Userinit çoklu girdi, WMI
> ActiveScript) ile birlikte. Saf çekirdek: `Core/Analyzer/CommandLineResolver.cs`, `AutorunActionPolicy.cs`,
> `ThreatRules.cs`. Testler: `AnalyzerFaz0Tests` (42, Core), `AnalyzerFaz0WindowsTests` (8).
> Ölçüm: sahte yetim 505 → 2 gerçek + 6 Mağaza COM görevi; explorer/Notepad++/OBS 80/40/20 → 0/0/0;
> Bakım 65 → 35; sıcak tarama 0,8 sn; anahtarlı sürücü 136 → 0.

**Kabul (bu makinede ölçülerek):**
- Görevlerde "çözümlenemedi" < %2, hizmetlerde sıfıra yakın, Winlogon sıfır.
- Görev durumları Görev Zamanlayıcı ile birebir.
- explorer.exe, notepad.exe, Notepad++, OBS, Steam, Riot ≤ 25 (Temiz/Bilgi). Bakım imzasız olduğu için ≤ 40.
- VT 60/72 fikstürü, imzalı olsa bile ≥ 75.
- Açılış sürücüsü satırında anahtar yok; kapatıp açınca `Start` özgün değerine dönüyor (test).
- Sayfa açılışında UI donması < 50 ms.

### Faz 1 — Kapsam: Autoruns düzeyi (v4.9.0, ~3 hafta)

Her konum ayrı bir `IAutorunCollector`. Hepsinin ortak üst verisi: görünen ad, "Bu konum nedir?" açıklaması,
temel risk ağırlığı, izinli eylemler, kaynak belge bağlantısı.

| Grup | Konumlar |
|---|---|
| Oturum açılışı | Run / RunOnce / RunOnceEx (HKLM, HKCU, WOW6432Node), Policies\Explorer\Run (her iki kovan), Başlangıç klasörleri, Active Setup, Winlogon (Shell, Userinit, Taskman, AppSetup, GPExtensions), `Command Processor\AutoRun` |
| Hizmet ve sürücü | Hizmetler (`ServiceDll`, `FailureCommand`, hesap), **Sürücüler ayrı sekme** (Boot/System), BootExecute, KnownDLLs |
| Enjeksiyon | AppInit_DLLs, AppCertDlls, IFEO (Debugger, GlobalFlag + SilentProcessExit\MonitorProcess, WOW6432Node), LSA (Authentication/Notification/Security Packages), yazdırma izleyicileri ve sağlayıcıları, Winsock LSP/NSP, Netsh yardımcıları |
| Zamanlama | Görevler (COM; eylem = exe ya da COM işleyici CLSID → DLL), WMI (`__EventFilter` → `__FilterToConsumerBinding` → CommandLine/ActiveScript consumer zinciri, betik metni) |
| Explorer | ContextMenu/DragDrop/PropertySheet işleyicileri (tekilleştirilmiş), ShellIconOverlayIdentifiers, ShellServiceObjectDelayLoad, ShellExecuteHooks, SharedTaskScheduler, BHO |
| Tarayıcı ve Office | Chromium tabanlılar (Chrome, Edge, Brave, Opera: kurulu eklentiler + `ExtensionInstallForcelist` politikaları), Firefox eklentileri ve `policies.json`, Office COM ve VSTO eklentileri |
| Profil | PowerShell profil betikleri, `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup` dışındaki `.lnk` hedef kaymaları |

Sarmalayıcı açma: `rundll32 x.dll,Giriş` → x.dll; `regsvr32 /s /i:url scrobj.dll` → uzak betik bayrağı;
`cmd /c`, `powershell -enc` (base64 çözülüp gösterilir), `wscript/cscript x.vbs`, `mshta` → betik dosyası
ya da satır içi betik ayrı kanıt olur.

> **Durum (v4.9.0): uygulandı.** Oturum açılışı (Active Setup, cmd AutoRun, HKCU Shell, AppSetup), sistem
> kancaları (AppInit/AppCert, LSA, yazdırma, Winsock LSP/NSP, Netsh, BootExecute, PowerShell profilleri),
> Gezgin kancaları, tarayıcı eklentileri (Chromium ailesi + Firefox + zorunlu politika) ve Office COM/VSTO
> eklentileri. Kurallar: `Core/Analyzer/ExtensionRules.cs`. Bu makinede: 498 girdi, 11 eklenti, 37 kanca.
> Autoruns ile birebir karşılaştırma henüz yapılmadı.

**Kabul:** Sysinternals Autoruns ile aynı makinede karşılaştırma. Ortak konumlarda sayım farkı ≤ %3,
her fark açıklanabilir.

### Faz 2 — Birleşik kanıt ve risk motoru (v4.10.0, ~2 hafta)

> **Durum (v4.10.0): uygulandı.** `Core/Risk/RiskScale` (tek ölçek; Analizör, dosya analizi, geçmiş ve
> Nöbetçi), `Core/Analyzer/PersistenceRisk` (puan + gerekçeler), güven kararı listeye bağlandı. Nöbetçi
> ağırlıkları 40/20/8 → 60/30/12. Kalibrasyon korpusu (fikstür PE'ler) ve tam kanıt modeline geçiş Faz 8'e kaldı.

- **`Core/Risk/Evidence`:** `{Kind, Weight, Confidence, Dominant, Title, Detail}`. Kanıt türleri: imza
  (geçerli/katalog/geçersiz/yok/süresi dolmuş), yayıncı güveni, VT uzlaşısı, konum, sarmalayıcı/betik,
  gizleme (RLO, çift uzantı), PE (mitigasyon, bölüm başına entropi, içe aktarım birleşimleri), yaş, MOTW,
  kullanıcı güven listesi, "bu makinede ilk görülüş".
- **`UnifiedRiskEngine`:** Baskın kanıt tabanı (en güçlü kanıt kararı belirler) + destekleyici kanıtlarla
  sınırlı artış. Çıktı: 0–100 puan, `Temiz/Bilgi/Dikkat/Şüpheli/Tehlikeli`, en fazla 3 cümlelik "neden".
  Nöbetçi'nin `SetupRiskEngine`'i aynı karar etiketlerine ve eşiklere taşınır.
- **Tek puan:** Liste satırı, derin analiz ve Nöbetçi aynı motoru kullanır. Derin analiz sonucu satıra geri
  yazılır; satır "yüzeysel" ya da "derin" rozetini taşır.
- **Güven listesi etkin:** Güvenilen hash/yayıncı → "Güvenilir (sizin kararınız)" kanıtı, puanı tabana çeker,
  listede ayrı süzgeç.
- **Kalibrasyon korpusu (`Tests/Fixtures/Analyzer`):** Temiz Windows ikilileri, yaygın uygulamalar ve
  sentetik PE'ler (çift uzantı, RLO, W+X bölüm, enjeksiyon üçlüsü, packer bölüm adı). Sahte VT yanıt
  JSON'ları. Her sürümde "temizde yanlış pozitif ≤ %2" ve "sentetik kötüde kaçırma 0" testi.

### Faz 3 — Derinlik ve ikinci görüş (v5.0.0, ~3 hafta)

> **Durum (v4.11.0): uygulandı (YARA hariç).** `Core/Analyzer/SecondOpinion` (MpCmdRun çıktısı; Defender
> kapalıyken "temiz" sayılmaz), `ScriptRules` (birleşim kuralları + karartma temizliği), AMSI,
> `Services/Analyzer/QuarantineService` (çalıştırma yasağı + Etkinlik Merkezi geri alma). Bu makinede Defender
> kapalı olduğu için gerçek MpCmdRun tespiti elle doğrulanmadı; ayrıştırıcı örnek çıktılarla test edildi.
> YARA yerel kütüphane gerektirdiği için ertelendi; süreç bağlamı (bellek/ağ) Faz 4 ile birlikte ele alınacak.

- **Defender ikinci görüşü:** `MpCmdRun.exe -Scan -ScanType 3 -File <yol> -DisableRemediation`
  (Nöbetçi NÖB 4.6 ile ortak). Sonuç kanıt olarak girer; Defender kapalıysa bunu açıkça söyler.
- **Kural motoru:** İsteğe bağlı YARA (yerleşik küçük kural seti + kullanıcı kuralları klasörü). Kural
  isabeti kanıt olur, kural adı ve eşleşen dize gösterilir.
- **Betik analizi:** PowerShell/VBS/JS/HTA için kod gizleme göstergeleri (base64 blokları, `IEX`,
  `FromBase64String`, `-bxor`, uzun tek satır), indirme adresleri; AMSI ile tarama (`AmsiScanBuffer`) mümkünse.
- **Süreç bağlamı:** Çalışan süreçte imzasız modül, bellekteki görüntü ile disktekinin farkı (yalnızca
  bayrak), ağ bağlantısı (Ağ İzleyicisi altyapısı).
- **Karantina:** Dosya ACL'si kısıtlanmış karantina klasörüne taşınır ve kalıcılık girdisi devre dışı
  bırakılır; tek düğmeyle geri alınabilir. "Sil" yerine varsayılan eylem budur.

### Faz 4 — Canlı kalıcılık izleme (v5.1.0, ~2 hafta)

> **Durum (v4.13.0; v4.12.0 yayın testinde yarış durumu yakalandı, düzeltildi): uygulandı.** `Services/Analyzer/PersistenceWatchService` (RegNotifyChangeKeyValue: Run +
> politika + hizmet adları; FSW: başlangıç klasörleri; 5 dk yoklama: görevler + WMI), `Core/Analyzer/PersistenceWatch`
> (fark, bildirim kararı, `ProvenanceIndex`). Uçtan uca test: gerçek HKCU Run değeri 30 sn içinde yakalanıyor.

- Run anahtarları, hizmetler, görevler ve WMI aboneliği için değişim bildirimi (`RegNotifyChangeKeyValue`,
  görev klasörü izleyicisi, `__InstanceCreationEvent` on `__EventConsumer`).
- Yeni kalıcılık → anında değerlendirme → risk ≥ Dikkat ise bildirim ("X yeni bir başlangıç girdisi ekledi").
- **Nöbetçi korelasyonu:** Girdi bir kurulum oturumunda eklendiyse satırda "Notepad++ kurulumu ekledi
  (26 Eylül)" bağlantısı; Nöbetçi raporundan Analizör satırına ve tersine geçiş.

### Faz 5 — Arayüz v3 (v5.1–v5.2, ~2 hafta, Fluent 2 cam sistemiyle)

> **Durum (v4.13.0): kısmen.** Yapıldı: Fluent onay/bilgi pencereleri (`Helpers/FluentDialog`, Analizör kapsamında
> 44 MessageBox), sürükle-bırak, Gezgin "Bakım ile analiz et" (BakimShell + IPC `AnalyzeTarget`), satır risk
> rozeti ve gerekçe ipucu (Faz 2), kaynak satırı (Faz 4). Kalan: ana-ayrıntı düzeni ve kanıt paneli, sayfa içi
> analiz görünümü, toplu eylemler, uygulamanın geri kalanındaki ~145 MessageBox.

- **Ana-ayrıntı düzeni:** Solda gruplanmış liste (kategori/yayıncı/karar), sağda kanıt paneli (kanıtlar,
  ham komut ↔ çözümlenmiş hedef, imza zinciri, VT, geçmiş, izinli eylemler).
- **Sayfa içi analiz:** `ThreatAnalysisDialog` sayfa içi görünüme taşınır; pencere yalnızca dış çağrılar için kalır.
- **Fluent geri bildirim:** 44 `MessageBox` → `ContentDialog`/InfoBar/Snackbar (onay, hata, bilgi ayrımıyla).
- **Toplu eylemler:** Seçime göre kapat/karantina, önizleme ve tek geri alma kaydı.
- **Giriş yolları:** Sürükle-bırak, Gezgin sağ tık "Bakım ile analiz et" (`BakimShell`), komut paleti.
- **Klavye ve erişilebilirlik:** Tam klavye gezintisi, renkten bağımsız karar simgeleri (mevcut) her yere.

### Faz 6 — Performans (sürekli, v4.8'den itibaren)

- **Zenginleştirme önbelleği:** `(yol, boyut, mtime)` → imza/sürüm/hash; diske kalıcı (`History/Analyzer/enrich-cache.json`).
- **Tembel hash:** SHA-256 yalnızca VT, geçmiş ya da kullanıcı istediğinde; MD5/SHA-1 yalnızca "Hash'leri kopyala"da.
- **Sınırlı paralellik:** Zenginleştirme `Parallel.ForEachAsync` (derece 4); UI grup aktarımı.
- **`RiskScore` önbelleği:** `HasValidFile` tarama anında bir kez hesaplanır, özellik değişince `PropertyChanged`.
- **Hedef:** Sıcak tarama < 1,5 sn, soğuk < 5 sn (bugün 4,96 sn), UI donması < 50 ms.

> **Durum (v4.14.0): kalıcı önbellek uygulandı.** `PersistentFacts` (`AutorunsScannerEngine.cs`): `(yol, boyut,
> mtime)` → imza durumu, imzalayan, SHA-256, oluşturma zamanı; `History/Analyzer/enrich-cache.json`, en çok 6000
> kayıt, atomik yazma, tarama sonunda yalnızca değiştiyse. Ölçüm (bu makine): uygulamanın ilk soğuk taraması
> 4,52 sn → önbellekle yeni süreçte 1,47 sn; sıcak tarama 0,8 sn. Kalan: tembel hash, UI donma ölçümü.

### Faz 7 — Geçmiş ve Değişiklikler v2 (v4.9 ile birlikte)

- **Ayrıştırıcı sürümü:** Anlık görüntüye `ParserVersion` yazılır. Faz 0 sonrası ilk tarama eski görüntüyle
  karşılaştırıldığında yüzlerce sahte "değişen" çıkmaması için sürüm farkında fark bastırılır, kullanıcıya
  "Tarama motoru güncellendi; karşılaştırma bir sonraki taramadan itibaren" denir.
- **Kararlı kimlik:** Anahtar = kategori + konum + değer adı/görev yolu/hizmet adı (şu an ad da anahtarda).
- **Zaman çizelgesi:** Girdi başına ilk görülüş, değişiklikler, kullanıcı kararları.

> **Durum (v4.15.0): uygulandı.** Kararlı kimlik `SnapshotDiff.StableKey`: hizmette hizmet adı, görevde görev yolu,
> kayıt defterinde anahtar yolu + değer adı (`ParserVersion` 3). Eskiden hizmet konum metni başlangıç türünü
> içerdiği için kapatılan hizmet "kaldırıldı + eklendi" görünüyordu; artık tek "Değişen" girdi. Zaman çizelgesi
> `Core/History/EntryTimeline` (ilk görülüş/eklendi, değişen alanlar ve eski → yeni yol, kapatma/açma, kaldırılma,
> geri dönüş) + kurulum kaynağı + kullanıcı kararları; Analizör satırı "Diğer eylemler → Zaman çizelgesi".

### Faz 8 — Test ve kalite (her fazla birlikte)

- `CommandLineResolver` için ≥ 150 gerçek komut satırı (bu makineden ve Autoruns örneklerinden, kişisel yollar maskelenmiş).
- Toplayıcılar için kayıt defteri/görev XML fikstürleri (sanal kaynak arayüzü; gerçek kayıt defterine dokunmaz).
- Risk kalibrasyonu (Faz 2 korpusu) ve eylem politikası tabloları (her kategori × durum).
- UI bağlama testleri (`verify-bindings.py`) ve ekran görüntüsü testleri.

> **Durum (v4.14.0): kısmen.** `CommandLineCorpusTests`: Run, görev, hizmet ImagePath, sürücü (`System32\drivers`,
> `\SystemRoot`, `\??\`, DriverStore), Winlogon, 8.3 kısa ad, sarmalayıcı ve yetim desenlerinden 29 durum; Faz 0
> testleriyle birlikte çözümleyici için toplam ~77 durum (hedef 150). Risk kalibrasyonu ve eylem politikası
> tabloları Faz 2/0 testlerinde. Kalan: toplayıcı fikstürleri (sanal kayıt defteri), derlemi 150'ye çıkarmak.

---

## 4. Veri modeli v3 (özet)

```csharp
record AutorunEntry(
    string Id,                 // kararlı: kategori|konum|ad
    AutorunLocation Location,  // toplayıcı üst verisi (grup, açıklama, izinli eylemler)
    string RawCommand,
    ResolvedTarget Target,     // Exe, EffectiveTarget (sarmalayıcı açılmış), Args, Script?, Resolution (Ok/Unresolved/Orphan)
    EntryState State,          // Enabled/Disabled/NotApplicable + özgün değer (geri alma için)
    FileFacts? File,           // imza, yayıncı, sürüm, zamanlar, MOTW, hash? (tembel)
    IReadOnlyList<Evidence> Evidence,
    RiskResult Risk,           // puan, karar, neden, derinlik (Yüzeysel/Derin)
    Provenance? Provenance);   // Nöbetçi oturumu, ilk görülüş

record ActionDecision(ActionKind Kind, bool Allowed, string Reason);   // ActionPolicy çıktısı
```

---

## 5. Kod organizasyonu

```
Core/Analyzer/
  Resolution/   CommandLineResolver, WrapperUnwrapper, DriverPathResolver        (saf)
  Policy/       ActionPolicy, TrustedVisibility                                (saf)
Core/Risk/      Evidence, UnifiedRiskEngine, Verdicts (Nöbetçi ile ortak)        (saf)
Services/Analyzer/
  Collectors/   RunKeys, StartupFolders, Tasks, Services, Drivers, Wmi, Winlogon, Ifeo, Lsa, Winsock,
                Explorer, Browser, Office, ActiveSetup, BootExecute …
  Enrichment/   FileFactsProvider (+önbellek), SignatureInspector köprüsü
  Intel/        VirusTotalClient (+hız sınırı), DefenderSecondOpinion, YaraScanner
  Actions/      AutorunActionExecutor (UndoJournal), Quarantine
AutorunsScannerEngine.cs → ince cephe (IAutorunsScannerEngine korunur), sonra kaldırılır
```

---

## 6. Sürüm yol haritası

| Sürüm | Faz | Kullanıcının gördüğü |
|---|---|---|
| v4.8.0 | 0 (+6 başlangıcı) | Doğru yollar, doğru görev durumları, Windows dosyalarına yanlış alarm yok, VT'ye göre doğru karar, sürücüler güvende, donmayan sayfa |
| v4.9.0 | 1 + 7 | Autoruns düzeyinde kapsam, ServiceDll, WMI zinciri, tarayıcı eklentileri, sağlam Değişiklikler sekmesi |
| v4.10.0 | 2 | Tek risk dili (Analizör = Nöbetçi), açıklamalı kararlar, çalışan güven listesi |
| v5.0.0 | 3 | Defender ikinci görüşü, YARA, betik analizi, karantina |
| v5.1.0 | 4 + 5 | Canlı kalıcılık bildirimi, Nöbetçi korelasyonu, yeni arayüz |

---

## 7. Başarı ölçütleri (bugünkü değerler bu makinede ölçüldü)

| Ölçüt | Bugün | Hedef |
|---|---|---|
| Görevlerde sahte "dosya yok" | 363 / 365 | < %2 |
| Hizmetlerde sahte "dosya yok" | 135 / 213 | ≈ 0 (yalnızca gerçekten yetim olanlar) |
| Görev durumu doğruluğu | Hepsi "etkin" | Görev Zamanlayıcı ile birebir |
| explorer.exe puanı / önerisi | 80 · "KRİTİK, silin" | ≤ 10 · Temiz |
| Temiz uygulama korpusunda yanlış pozitif (≥ Dikkat) | notepad++ 40, Bakım 65, explorer 80 | ≤ %2 |
| İmzalı + VT 60/72 | 25 · "GÜVENLİ" | ≥ 75 · Tehlikeli |
| Açılış sürücüsünü kapatabilme | Mümkün | İmkânsız (politika + test) |
| Tarama süresi / UI donması | 4,96 sn / ~1,2 sn | < 1,5 sn sıcak, < 5 sn soğuk / < 50 ms |
| Kapsanan kalıcılık konumu | 7 kategori, ~15 konum | 25+ grup, Autoruns ortak konumlarında fark ≤ %3 |
| VT toplu tarama (ücretsiz anahtar, 20 dosya) | 5.'den sonra 429 | 20/20 tamam, kalan süre gösterilir |

---

## 8. Riskler

- **Ayrıştırıcı değişince geçmiş farkı patlar.** → Faz 7'deki `ParserVersion` ile Faz 0'da birlikte yayınlanmalı.
- **Kapsam genişleyince gürültü artar.** → Yeni toplayıcılar önce "yalnızca bilgi" modunda, korpus
  kalibrasyonundan sonra puana katılır.
- **Eylem politikası fazla katı olabilir** (ileri düzey kullanıcı sürücü kapatmak isteyebilir). → "Gelişmiş mod"
  yalnızca Hizmet Yöneticisi'nde, açık uyarı ve geri yükleme noktasıyla; Analizör'de asla.
- **Defender/YARA/AMSI ortam bağımlı.** → Her biri kanıt sağlayıcı; yoksa karar yine verilir, eksik görüş açıkça yazılır.
- **VT gizliliği.** → Hash sorgusu varsayılan, dosya yükleme her seferinde onaylı (bugünkü gibi); toplu yükleme yok.

---

## 9. İlk 10 iş (hemen başlanacaklar)

1. **Z1** — Sürücü ve kritik hizmet anahtarlarını kaldır, özgün `Start` değerini geri yükle (en yüksek risk).
2. **Z6** — `CommandLineResolver` + 50 gerçek komut satırı testi (tablo 0.1'in çoğunu tek başına kapatır).
3. **Z7** — Görevleri `TaskSchedulerReader` ile oku.
4. **Z2** — "Microsoft'u gizle"yi doğrulanmış imzaya bağla.
5. **Z3 + Z5** — explorer.exe konum tablosu ve API kataloğu düzeltmesi; temiz korpus testi (explorer, notepad, Notepad++, OBS, Steam).
6. **Z4** — VT baskınlığı ve `suspicious` sayımı, fikstür testi.
7. **Z8** — Taramayı arka plana al, grup aktarımı, `IModuleViewModel`.
8. **Z9** — VT hız sınırlayıcı ve 429 işleme.
9. **Faz 7 / ParserVersion** — Z6/Z7 ile aynı sürümde.
10. **Z10 + Z11** — "AI" dili, CSV kaçışı, Nöbetçi dosyaları için ayrı kategori, tekilleştirme.
