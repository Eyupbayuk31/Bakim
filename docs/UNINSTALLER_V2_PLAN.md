# Kaldırıcı ve Sağ Tık Kaldırma v2: Uygulama Planı

> Bu belge bir yapay zekâ kod asistanının (Gemini vb.) **adım adım, sırayla** uygulaması için yazıldı.
> Her görevde şunlar var: amaç, dokunulacak dosyalar, adımlar, kod iskeleti, yazılacak testler, kabul kriteri.
> Görevler bağımlılık sırasına göre dizildi. **Sırayı değiştirme, görev atlama.**
>
> Kapsam: `UninstallerService`, `DeepUninstallerService`, `ResidualScannerEngine`, `ShellContextMenuService`,
> `ShellUninstallResolverService`, `UninstallerViewModel`, `DeepUninstallWizardViewModel`,
> `DeepUninstallWizardWindow`, `App.xaml.cs` (`--uninstall-target`) ve `Bakim_Setup.iss`.

---

## 0. Uygulayıcı için kurallar (önce bunları oku)

### 0.1 Çalışma biçimi

1. Görevleri **bu belgedeki sırayla** yap. Her görev ayrı bir commit olsun. Commit mesajı biçimi: `fix(uninstaller): A1 - PathSafetyGuard eklendi`.
2. Her görevden sonra şunları çalıştır. Hepsi başarılı olmadan sonraki göreve geçme:
   ```
   dotnet build Bakım.slnx -c Debug
   dotnet test Tests/Bakim.Tests/Bakim.Tests.csproj
   python Tools/verify-tokens.py
   python Tools/verify-symbols.py
   ```
3. Görevin "Dosyalar" listesinde olmayan dosyaya dokunma. Zorunlu kalırsan commit mesajında nedenini yaz.
4. Mevcut kod stilini koru:
   - MVVM için `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`).
   - DI kaydı `App.xaml.cs` içindeki `ConfigureServices` metoduna yapılır.
   - Testler `Tests/Bakim.Tests` altında, xUnit ile yazılır.
   - UI metinleri Türkçe olur.
   - **Emoji kullanılmaz**; ikonlar yalnızca `SymbolRegular` adlarıyla verilir (`verify-symbols.py` bunu denetler).
5. Yeni sınıf eklediğinde:
   - DI gerekiyorsa `ConfigureServices`'e kaydet.
   - `Tests/Bakim.Tests/DependencyInjectionTests.cs` içindeki `[InlineData(typeof(...))]` listesine ekle.
6. Bir adım belirsizse **tahmin etme**. Kodda `// TODO(karar): ...` bırak ve devam et.

### 0.2 KESİN YASAKLAR

Bunlar ihlal edilirse kullanıcı verisi silinir.

- `Directory.Delete(..., recursive: true)`, `File.Delete`, `DeleteSubKeyTree` ve `Process.Kill` **yalnızca** bu belgede tanımlanan `SafeDeleteService`, `SafeRegistryService` ve `SafeProcessService` sınıflarının içinde çağrılabilir. Başka yerde çağırma.
- Silme ya da süreç sonlandırma işlemi önce `PathSafetyGuard` onayından geçmek zorunda. Guard'ı atlatan bir "hızlı yol" yazma.
- Otomatik silme (kullanıcı onayı olmadan, örneğin toplu kaldırmada "otomatik temizle") **yalnızca** `Confidence.Certain` seviyesindeki öğelere uygulanır.
- Yeni kodda `catch { }` ile hata yutma. `ILogService` ile logla ve sonucu kullanıcıya/arayana döndür.
- Mevcut testleri silme ve devre dışı bırakma. Bir test yanlış davranışı doğruluyorsa testi güncelle ve commit mesajında açıkla.
- `C:\Windows\Installer` klasörüne, `ProgramData\Package Cache`'e ve `WindowsApps`'e **hiçbir koşulda** dokunma.
- Kayıt defterinde `Software`, `Software\Microsoft`, `Software\Classes`, `Software\WOW6432Node`, `Software\Policies`, `SYSTEM` ve bunların **doğrudan kendileri** silinemez.

### 0.3 Tamamlanma tanımı (her görev için)

- Derleme uyarısız ya da mevcut uyarı sayısını artırmadan geçiyor.
- Görevdeki testler yazıldı ve geçiyor.
- Kabul kriterindeki her madde sağlanıyor.
- UI değiştiyse `AutomationProperties.Name` eklendi (bkz. `Tools/add-automation-names.py`).

---

## 1. Mevcut durum: bulgular (v3.19.1)

Aşağıdaki satır numaraları incelemenin yapıldığı commit'e (`6cabf07`) göredir.

### 1.1 P0: Veri kaybı ve sistem hasarı riski

| # | Sorun | Yer | Somut senaryo |
|---|---|---|---|
| U-P0-1 | Tek token **alt dize** eşleşmesi %85 güven alıyor, %80 ve üstü "%100 Güvenli" etiketi ve varsayılan seçim alıyor | `Services/ResidualScannerEngine.cs` `CalculateMatchConfidence` (satır 431), `ScanResidualItemsAsync` içindeki `IsSafeToDelete = l.ConfidenceScore >= 80` (satır 552) | "Git" kaldırılırken `git` token'ı "Digital…" klasörüyle eşleşiyor. "VLC media player" kaldırılırken `media` token'ı "Windows Media Player" ile eşleşiyor. Liste bunları **seçili ve "güvenli"** gösteriyor. |
| U-P0-2 | Yayıncı + herhangi bir token eşleşmesi = %100, toplu kaldırmada "otomatik temizle" bunu **onaysız siliyor** | `CalculateMatchConfidence` (`matchesPub && matchesAnyToken`), `DeepUninstallerService.ExecuteAutoCleanResidualsAsync` | "Google Drive" kaldırılırken `%LocalAppData%\Google` (Chrome profili içerir) ve `HKCU\Software\Google` %100 sayılıp otomatik siliniyor. "Adobe Reader" kaldırılırken Photoshop verileri gidiyor. |
| U-P0-3 | Sağ tık ile taşınabilir exe veya klasör seçilince hedefin **üst klasörü** InstallLocation oluyor | `ShellUninstallResolverService.SynthesizeHeuristicApp`, `ResidualScannerEngine.ScanHeuristicResidualsAsync` (üst klasör %100) | İndirilenler'deki bir exe'ye "Bakım ile Kaldır" denince **İndirilenler klasörünün tamamı** %100 güvenle silinecekler listesine giriyor. Masaüstü, Belgeler ve `C:\Program Files` için de aynı durum geçerli. |
| U-P0-4 | Süreç sonlandırma kapsamı doğrulanmıyor | `DeepUninstallWizardViewModel.KillProcessesForApp`, `DeepUninstallerService.KillProcessesForApp` | Sağ tıkla `C:\Windows\explorer.exe` seçilirse InstallLocation `C:\Windows` olur. Sihirbaz varsayılan olarak "ilişkili süreçleri sonlandır" açık geldiği için `C:\Windows` altındaki her şeyi (explorer, svchost…) öldürmeye çalışır. Bakım kurulumda `RUNASADMIN` ile yönetici çalıştığı için birçoğu gerçekten ölür; sistem çöker. Ek olarak önek eşleşmesinde `\` eksik: `C:\App` kaldırılırken `C:\AppData…` süreçleri de öldürülür. |
| U-P0-5 | `IsProtectedDirectory` yalnızca klasörün **adına** bakıyor | `ResidualScannerEngine.IsProtectedDirectory`, `UninstallerService.IsProtectedSystemDirectory` | `Program Files`, `Program Files (x86)`, `ProgramData`, `Users`, kullanıcı profili, Masaüstü, Belgeler, İndirilenler, `AppData\Local` korunmuyor. InstallLocation değeri hatalı olan bir program (bazı kurulumlar `C:\Program Files` yazar) kaldırılınca bu kök klasör silinir. |
| U-P0-6 | Resmi kaldırıcının bitmesi yanlış bekleniyor ve sonucu doğrulanmıyor | `DeepUninstallerService.LaunchUninstallAsync` (`WaitForExitAsync`), `DeepUninstallWizardViewModel.StartUninstallAsync` | Inno (`unins000.exe`) ve NSIS kaldırıcıları kendilerini `%TEMP%`'e kopyalayıp **hemen çıkar**. Bakım kaldırmayı bitmiş sanıp kalıntı taramasına geçer ve hâlâ kurulu programın klasörünü %100 "kalıntı" olarak listeler. Kullanıcı resmi kaldırıcıda **İptal**'e basarsa da tarama başlar ve **kurulu programın klasörü silinmeye önerilir.** |
| U-P0-7 | "Kayıt defteri yedeği" gerçekte yedek değil | `DeepUninstallWizardViewModel.ExportRegistryBackupSafe` | Dosyaya yalnızca `[-Anahtar]` satırları yazılıyor; bu, anahtarı **silen** bir .reg dosyasıdır. Değerler yedeklenmiyor. Yol biçimi de (`CurrentUser\...`) .reg sözdizimine uymuyor. UI ise kullanıcıya "yedek alındı" diyor. |
| U-P0-8 | Hive ayrıştırma bozuk | `DeleteRegistryKeySafe` (3 kopya) | `HKCU\...` biçimindeki yollar (`ScanRegistryFileExts` bunu üretiyor) "Current" içermediği için **HKLM** sanılıyor. 32 bit uygulamaların `RegistryKeyPath`'i görünüm (view) bilgisi taşımadığı için `Registry64` ile silinirken **yanlış anahtar** silinebilir. `FileExts\...\OpenWithProgids\<değer>` bir *değer*dir ama anahtar gibi silinmeye çalışılıyor ve sonuç "silindi" olarak raporlanıyor. |

### 1.2 P1: Yanlış sonuç ve kötü deneyim

| # | Sorun | Yer |
|---|---|---|
| U-P1-1 | Tırnaksız ve boşluklu `UninstallString` bozuluyor: `C:\Program Files (x86)\Foo\uninst.exe /S` ilk boşluktan bölünüp `C:\Program` diye çalıştırılıyor | `DeepUninstallerService.ParseCommandAndArguments`, `UninstallerService.LaunchStandardUninstallAsync` |
| U-P1-2 | Toplu kaldırmada "başarı" yalnızca sürecin başlamış olması. Çıkış kodu ve kaydın silinip silinmediği kontrol edilmiyor. Başarısız olanlar da listeden kaldırılıyor | `ExecuteBatchSilentUninstallAsync`, `UninstallerViewModel.BatchUninstallAsync` |
| U-P1-3 | Toplu kaldırmada **her uygulama için** ayrı geri yükleme noktası açılıyor. Her biri UAC istemi ve 15 sn bekleme demek. Windows 24 saatte bir noktaya izin verdiği için ilk noktadan sonrakiler sessizce başarısız oluyor | `ExecuteBatchSilentUninstallAsync`, `CreateRestorePointAsync` |
| U-P1-4 | "Sessiz" modda GenericExe kaldırıcılar arayüzlü açılıyor. InstallShield için `-s` yanıt dosyası olmadan çalışmaz | `BuildSilentArguments` |
| U-P1-5 | Kurulu uygulama listesi yalnızca `DisplayName` ile tekilleştiriliyor; aynı adlı x86/x64 sürümlerden biri kayboluyor. `EstimatedSize` 0 olan her uygulama için liste yüklenirken klasör boyutu **tüm alt klasörlerle** hesaplanıyor (yavaş açılış) | `UninstallerService.GetInstalledAppsAsync`, `ScanRegistryKey` |
| U-P1-6 | Kalıntı taraması yalnızca **isim benzerliğine** bakıyor. Servisler, zamanlanmış görevler, Run girdileri, firewall kuralları, masaüstü ve görev çubuğu kısayolları, `LocalAppData\Programs\*` (VS Code kullanıcı kurulumu buraya gelir), `Belgeler\<Uygulama>` ve dosya ilişkilendirmeleri (HKCR ProgId) bakılmıyor | `ResidualScannerEngine.ScanResidualsAsync` |
| U-P1-7 | Tamamlandı ekranındaki "temizlenen boyut", **seçilenlerin** toplamı; silinemeyenler de sayılıyor | `DeepUninstallWizardViewModel.CleanSelectedResidualsAsync` |
| U-P1-8 | Aynı iş üç kez yazılmış: `UninstallerService` (tarama, temizleme, başlatma), `DeepUninstallerService`, `ResidualScannerEngine`. `DeleteRegistryKey` 3 kopya, `FormatBytes` 5+ kopya, `KillProcessesForApp` 2 kopya, token mantığı 2 kopya. `LeftoverItem` ile `ResidualItem` aynı işi yapan iki model | `Services/*`, `Models/*` |
| U-P1-9 | ViewModel'lerde doğrudan `MessageBox` çağrılıyor; bu akışlar test edilemiyor | `UninstallerViewModel`, `DeepUninstallWizardViewModel` |

### 1.3 Sağ tık menüsüne özel sorunlar

| # | Sorun | Yer |
|---|---|---|
| S-1 | `--uninstall-target` modunda da tüm arka plan servisleri başlıyor: Kurulum Nöbetçisi polling'i, `BackgroundMaintenanceService`. Tepside zaten çalışan bir Bakım varsa ikinci bir tam kopya açılıyor. Tepsideki nöbetçi, sihirbazın başlattığı `unins000.exe`'yi **kurulum** sanıyor | `App.xaml.cs` ≈ satır 143-149 (servisler), ≈ 169-187 (hedef kontrolü **sonra** yapılıyor) |
| S-2 | Kurulum `AppCompatFlags\Layers = "~ RUNASADMIN"` yazdığı için (`Bakim_Setup.iss:62-63`) **her sağ tık UAC istemi** açıyor | `Bakim_Setup.iss` |
| S-3 | Çoklu seçimde (örneğin 5 kısayol) Explorer 5 ayrı süreç açıyor: 5 UAC istemi, 5 sihirbaz | `ShellContextMenuService` |
| S-4 | Eşleşme sırası tehlikeli. Kural A: hedef klasör bir uygulamanın InstallLocation'ının **üst klasörüyse** de eşleşiyor; `C:\Program Files`'a sağ tıklayınca rastgele bir uygulama seçiliyor. Kural D: "Microsoft Edge" kısayolu "Microsoft Edge WebView2 Runtime" ile eşleşebiliyor. İlk eşleşen seçiliyor; puan yok, belirsizlik durumunda kullanıcıya sorulmuyor | `ShellUninstallResolverService.FindMatchingApp` |
| S-5 | Klasör hedefinde "ana exe" olarak `Directory.GetFiles(...)[0]` alınıyor; bu `unins000.exe` bile olabilir | `ResolveTargetAppAsync` |
| S-6 | MSI "advertised" kısayollarında (Office vb.) `TargetPath` boş ya da `C:\Windows\Installer\{GUID}\...` oluyor ve eşleşme başarısız. `.url` kısayolları (Steam, Epic oyunları) ve `.msi` dosyaları desteklenmiyor | `ResolveTargetAppAsync` |
| S-7 | Menü her klasörde görünüyor (`Directory\shell`); gereksiz kalabalık | `ShellContextMenuService.SubTargetKeys` |
| S-8 | Menü hem HKCU'ya hem HKLM'e yazılıyor. Kayıt kontrolü yalnızca `lnkfile` için yapılıyor. Bakım taşınır ya da güncellenirse komut yolu eskide kalıyor ve kendini onarmıyor | `ShellContextMenuService` |
| S-9 | Windows 11'de klasik menü "Daha fazla seçenek göster" altında kalıyor | (mimari; bkz. Faz E) |

---

## 2. Hedef mimari

```
[Sağ tık] BakimShell.exe (asInvoker, küçük)
     │  named pipe ile ileri gönderir, Bakım çalışmıyorsa Bakim.exe --uninstall-target başlatır
     ▼
[Bakım (tek örnek)] ── TargetResolver → ShellResolveResult (Eşleşti / Belirsiz / Taşınabilir / Korumalı / Bulunamadı)
     ▼
UninstallSession
  1. Onay           : uygulama bilgisi, eşleşme nedeni, kapatılacak süreçler, geri yükleme noktası
  2. Ön iz (Footprint): kaldırmadan ÖNCE kanıt topla (MSI bileşenleri, servis, görev, Run,
                        kısayol, firewall, ProgId, App Paths, Kurulum Nöbetçisi izi)
  3. Resmi kaldırıcı : UninstallCommandParser + JobObject ile izleme
  4. Doğrulama      : Uninstall anahtarı gitti mi? Kaldırıcı iptal mi edildi?
  5. Kalıntı        : ön izde olup hâlâ duranlar (Kesin) + referans taraması (Yüksek)
                      + isim sezgisi (Orta/Düşük, seçili değil)
  6. Güvenlik süzgeci: PathSafetyGuard + RegistrySafetyGuard + "başka uygulamanın klasörü" kuralı
  7. Temizlik       : Geri Dönüşüm Kutusu / gerçek .reg yedeği / servis ve görev silme /
                      yeniden başlatmada sil
  8. Rapor          : geri alma günlüğü (journal), gerçek sonuçlar
```

Temel ilke: **Önce kanıt topla, sonra kaldır, sonra geride kalanı göster.** İsim benzerliği yalnızca yardımcı sinyaldir, tek başına asla "güvenli" etiketi almaz.

---

## 3. Görevler

### FAZ A: Güvenlik (P0). Önce tamamen bitir.

#### A1. `PathSafetyGuard`: merkezi yol güvenliği

**Amaç:** Bir dosya ya da klasörün silinmesine, bir klasör altındaki süreçlerin sonlandırılmasına izin verilip verilmediğine tek noktadan karar vermek.

**Dosyalar:**
- yeni `Helpers/PathSafetyGuard.cs`
- yeni `Tests/Bakim.Tests/PathSafetyGuardTests.cs`

**Kurallar:**
1. Yolu `Path.GetFullPath` ile normalize et ve sondaki `\` işaretini kırp. Göreli, boş, UNC (`\\server\...`) ya da `\\?\` ile başlayan yolları reddet.
2. **Tam korumalı** (yolun kendisi silinemez, altı silinebilir):
   - her sürücünün kökü
   - `ProgramFiles`, `ProgramFilesX86`
   - `CommonProgramFiles`, `CommonProgramFilesX86`
   - `CommonApplicationData` (ProgramData)
   - `C:\Users`, her kullanıcı profil kökü, `C:\Users\Public`
   - `UserProfile`, `Desktop`, `CommonDesktopDirectory`, `MyDocuments`, İndirilenler (`SHGetKnownFolderPath(FOLDERID_Downloads)` ya da `UserProfile\Downloads`), `MyPictures`, `MyMusic`, `MyVideos`
   - `ApplicationData`, `LocalApplicationData`, `LocalApplicationData\Programs`, `UserProfile\AppData`, `UserProfile\AppData\LocalLow`
   - `Path.GetTempPath()`
   - `Programs`, `CommonPrograms`, `StartMenu`, `CommonStartMenu`
   - `%OneDrive%` ortam değişkeni (varsa)
3. **Ağaç korumalı** (kendisi de altındaki her şey de silinemez):
   - `Windows` klasörü (Installer dahil)
   - `ProgramFiles\WindowsApps`, `ProgramData\Package Cache`, `ProgramData\Microsoft`
   - `LocalApplicationData\Microsoft\Windows`
   - `System Volume Information`, `$Recycle.Bin`, `Recovery`
   - Bakım'ın kendi kurulum klasörü (`AppContext.BaseDirectory`) ve veri klasörleri (`ApplicationData\Bakım`, `LocalApplicationData\Bakim`)
4. **Derinlik kuralı** (yalnızca klasör silme için): Klasör, madde 2'deki köklerden birinin **en az 1 seviye altında** olmalı; örneğin `ProgramFiles\Foo` geçer. Hiçbir bilinen kökün altında değilse (örneğin `D:\Oyunlar\X`) `IsDeletionAllowed` ancak `allowOutsideKnownRoots: true` ile ve sürücü kökünün en az 2 seviye altındaysa geçer. Bu bayrağı yalnızca `Confidence.Certain` öğeler için çağıran taraf verir.
5. Reparse point (junction/symlink) olan klasörlerde `IsReparsePoint = true` döndür. Silme servisi bunlarda yalnızca bağlantıyı kaldırır, hedefe inmez.
6. Süreç sonlandırma kapsamı (`IsKillScopeAllowed`): Klasör silme kurallarından (1–4) geçmeli **ve** madde 3'teki hiçbir ağacın içinde olmamalı.

**Kod iskeleti:**
```csharp
namespace Bakım.Helpers
{
    public enum PathVerdict { Allowed, ProtectedExact, ProtectedTree, TooShallow, OutsideKnownRoots, Invalid }

    public readonly record struct PathCheck(PathVerdict Verdict, string NormalizedPath, bool IsReparsePoint, string Reason)
    {
        public bool IsAllowed => Verdict == PathVerdict.Allowed;
    }

    public static class PathSafetyGuard
    {
        public static PathCheck CheckDeletion(string path, bool isDirectory, bool allowOutsideKnownRoots = false);
        public static PathCheck CheckKillScope(string directory);
        public static bool IsUnder(string path, string root); // "C:\App" ile "C:\AppData" ayrımı: root + "\" öneki
        internal static IReadOnlyList<string> ExactProtected { get; }   // testte görünür olsun diye internal
        internal static IReadOnlyList<string> TreeProtected { get; }
    }
}
```
`IsUnder` her zaman `path.Equals(root) || path.StartsWith(root + "\\", OrdinalIgnoreCase)` biçiminde çalışmalı. Bu tek kural U-P0-4'teki önek hatasını kapatır.

**Testler** (`[Theory]` tablosu, en az şu 20 satır):

| Girdi | Beklenen |
|---|---|
| `C:\` | ProtectedExact |
| `C:\Windows\System32\foo.dll` | ProtectedTree |
| `C:\Windows\Installer\abc.msi` | ProtectedTree |
| `%ProgramFiles%` | ProtectedExact |
| `%ProgramFiles%\Foo` | Allowed |
| `%ProgramFiles%\WindowsApps\X` | ProtectedTree |
| `%UserProfile%\Downloads` | ProtectedExact |
| `%UserProfile%\Downloads\app.exe` (dosya) | Allowed |
| `%UserProfile%\Desktop` | ProtectedExact |
| `%LocalAppData%` | ProtectedExact |
| `%LocalAppData%\Programs` | ProtectedExact |
| `%LocalAppData%\Programs\Microsoft VS Code` | Allowed |
| `%LocalAppData%\Microsoft\Windows\X` | ProtectedTree |
| `%AppData%\Bakım\x.json` | ProtectedTree |
| `D:\Oyunlar\X` | OutsideKnownRoots (bayraksız) / Allowed (bayrakla) |
| `D:\X` | OutsideKnownRoots (bayraklı da reddedilir, çünkü 1 seviye) |
| `\\server\share\x` | Invalid |
| `..\x` | Invalid |
| `IsUnder("C:\AppData\x", "C:\App")` | false |
| Kill scope `C:\Windows` | reddedilir |

**Kabul kriteri:** Tüm testler geçiyor. Sınıf başka hiçbir sınıfa bağımlı değil (saf).

---

#### A2. `RegistryPath` + `RegistrySafetyGuard`: kayıt defteri yolu ve güvenliği

**Dosyalar:**
- yeni `Helpers/RegistryPath.cs`
- yeni `Helpers/RegistrySafetyGuard.cs`
- yeni `Tests/Bakim.Tests/RegistryPathTests.cs`

**Kod iskeleti:**
```csharp
public readonly record struct RegistryPath(RegistryHive Hive, RegistryView View, string SubKey, string? ValueName = null)
{
    // Kabul edilen önekler: HKLM, HKEY_LOCAL_MACHINE, LocalMachine | HKCU, HKEY_CURRENT_USER, CurrentUser
    //                      HKCR, HKEY_CLASSES_ROOT, ClassesRoot | HKU, HKEY_USERS, Users
    // Sonek " [32]" veya " [64]" varsa View bundan okunur; yoksa defaultView kullanılır.
    public static bool TryParse(string text, RegistryView defaultView, out RegistryPath result);
    public string ToRegExe();      // "HKEY_LOCAL_MACHINE\Software\Foo"   (reg.exe / .reg biçimi)
    public string ToDisplay();     // "HKLM\Software\Foo [32]"            (UI ve JSON'da kalıcı biçim)
    public RegistryKey OpenBase(); // RegistryKey.OpenBaseKey(Hive, View)
}
```
- **Kalıcı biçim** her yerde `ToDisplay()` olsun. `InstalledAppItem.RegistryKeyPath` bu biçimle doldurulur (A6'da). 32 bit görünümden okunan anahtarlar `[32]` sonekini alır.
- `RegistrySafetyGuard.CheckKeyDeletion(RegistryPath p)` şunları **reddeder**:
  - hive kökü
  - `Software`, `Software\Classes`, `Software\Microsoft`, `Software\WOW6432Node`, `Software\Policies`, `Software\Microsoft\Windows`, `Software\Microsoft\Windows\CurrentVersion` ve `...\CurrentVersion\Run` (yalnızca *anahtarın kendisi*; içindeki değerler silinebilir)
  - `SYSTEM` altında `CurrentControlSet\Services\<ad>` dışındaki her şey
  - `Software\Microsoft\Windows NT\CurrentVersion\Winlogon`
  - `Software\Microsoft\Windows\CurrentVersion\Uninstall` (anahtarın kendisi)
- `Software\<Vendor>` (1 seviye) anahtarı yalnızca çağıran `allowVendorRoot: true` verirse silinebilir. Bu bayrak yalnızca B-fazındaki "başka uygulama yok" kontrolünden sonra verilir.

**Testler:** Her önek ve sonek kombinasyonu. `HKCU\...`'nun HKCU olarak, `LocalMachine\...`'nin HKLM olarak çözüldüğü. `ToRegExe` çıktısı. Korumalı anahtar listesi reddediliyor. `Software\Foo\Bar` izinli.

---

#### A3. Güvenli işlem servisleri

**Amaç:** Bütün yıkıcı işlemleri tek yerde toplamak; guard kontrolü, loglama, yedek ve geri alma günlüğü buradan geçer.

**Dosyalar:**
- yeni `Services/Safety/SafeDeleteService.cs`
- yeni `Services/Safety/SafeRegistryService.cs`
- yeni `Services/Safety/SafeProcessService.cs`
- yeni `Services/Safety/UndoJournal.cs`
- yeni `Helpers/RecycleBin.cs`: `DuplicateFinderService.cs` içindeki `SHFileOperation` P/Invoke'u buraya **taşı**. Diğer iki dosya (`DuplicateFinderService.cs`, `SystemInfoService.cs`) bu helper'ı kullansın.
- `App.xaml.cs` (DI kaydı)
- testler

**Sözleşmeler:**
```csharp
public enum DeleteOutcome { Deleted, Recycled, ScheduledForReboot, Blocked, NotFound, AccessDenied, InUse, Failed }
public sealed record OperationResult(string Target, DeleteOutcome Outcome, string Message, long BytesFreed);

public interface ISafeDeleteService
{
    // Varsayılan: Geri Dönüşüm Kutusu (FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI).
    // permanent=true yalnızca Ayarlar'daki "Kalıcı sil" açıksa kullanılır.
    Task<OperationResult> DeletePathAsync(string path, bool isDirectory, DeletePolicy policy, CancellationToken ct = default);
    // Kullanımda olan dosya için: MoveFileEx(path, null, MOVEFILE_DELAY_UNTIL_REBOOT). Yönetici gerekir.
    OperationResult ScheduleDeleteOnReboot(string path);
}
public sealed record DeletePolicy(bool Permanent, bool AllowOutsideKnownRoots, string JournalId);

public interface ISafeRegistryService
{
    // Silmeden ÖNCE: reg.exe export "<ToRegExe()>" "<journal klasörü>\<n>.reg" /y  (+ /reg:32 veya /reg:64)
    Task<OperationResult> DeleteKeyAsync(RegistryPath key, string journalId, bool allowVendorRoot = false);
    // Değer silmeden önce değeri (tip + veri) journal JSON'una yaz.
    Task<OperationResult> DeleteValueAsync(RegistryPath keyWithValueName, string journalId);
}

public interface ISafeProcessService
{
    IReadOnlyList<ProcessCandidate> FindProcessesUnder(string directory); // önce listele
    // Önce CloseMainWindow + 3 sn bekle, sonra Kill(entireProcessTree: true)
    Task<IReadOnlyList<OperationResult>> TerminateAsync(IEnumerable<ProcessCandidate> processes);
}
public sealed record ProcessCandidate(int Pid, string Name, string ImagePath, DateTime StartTime);
```

**Uygulama ayrıntıları:**
- **Yol okuma:** `Process.MainModule` kullanma. `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION = 0x1000)` + `QueryFullProcessImageNameW` P/Invoke'unu yeni `Helpers/ProcessImagePath.cs` dosyasına yaz. Kurulum Nöbetçisi planı da (`docs/SENTINEL_V2_PLAN.md` görev 0.2) aynı helper'ı kullanacak.
- **Asla sonlandırma:** PID 0 ve 4, `Environment.ProcessId`, ve şu adlar: `csrss, wininit, winlogon, services, lsass, smss, svchost, explorer, dwm, fontdrvhost, sihost, ctfmon, spoolsv, MsMpEng, SearchHost, StartMenuExperienceHost, TextInputHost`. Ayrıca imajı `Windows` klasörü altında olan her süreç. `FindProcessesUnder` bu süreçleri hiç döndürmez.
- **Geri alma günlüğü (journal):**
  - Konum: `%LocalAppData%\Bakim\UninstallJournal\{journalId}\journal.json` ve yanında `.reg` dosyaları.
  - Her işlem için bir kayıt tutulur: zaman, hedef, sonuç, yedek dosyası.
  - `UndoJournal.RestoreRegistryAsync(journalId)` bütün `.reg` dosyalarını `reg.exe import` ile geri yükler.
  - Dosyalar Geri Dönüşüm Kutusu'ndan kullanıcı tarafından geri alınır; UI bu yüzden "Geri Dönüşüm Kutusunu Aç" düğmesi gösterir.
- **Denetim:** Her public metodun **ilk satırı** ilgili guard'ı çağırır; reddedilirse `Blocked` döner ve `ILogService.Warn` ile loglar.

**Testler:**
- Geçici klasörde (`Path.GetTempPath()` + GUID; dikkat: GUID alt klasörü, temp kökünün kendisi korumalıdır) bir dosya ve klasör oluştur. Kalıcı modda sil, sonucu doğrula.
- Korumalı bir yol için `Blocked` döndüğünü test et.
- `HKCU\Software\BakimTest_<guid>` anahtarı oluştur; `DeleteKeyAsync` ile önce .reg yedeği alındığını, sonra silindiğini, `RestoreRegistryAsync` ile geri geldiğini doğrula. Testin sonunda anahtarı temizle.

---

#### A4. Güven (confidence) modelinin yeniden yazımı

**Amaç:** U-P0-1 ve U-P0-2'yi kapatmak. İsim benzerliği tek başına asla yüksek güven vermemeli.

**Dosyalar:**
- yeni `Services/Uninstall/ResidualConfidence.cs`
- yeni `Services/Uninstall/NameMatcher.cs`
- `Models/ResidualItem.cs`
- `Services/ResidualScannerEngine.cs`
- testler

**Model değişikliği** (`ResidualItem`):
```csharp
public enum Confidence { Low = 30, Medium = 60, High = 90, Certain = 100 }
public enum EvidenceKind { Footprint, InstallLocation, SentinelTrace, MsiComponent, PathReference, NameExact, NameVendorApp, NameContains, NameToken }

// ResidualItem'e eklenecek alanlar:
public Confidence Confidence { get; set; }
public EvidenceKind Evidence { get; set; }
public string EvidenceText { get; set; } = string.Empty; // "Kaldırmadan önce 'Foo' servisinin ImagePath değeri bu klasörü gösteriyordu"
public ResidualCategory Category { get; set; }           // bkz. B4
// ConfidenceScore => (int)Confidence  (geriye uyumluluk için hesaplanan özellik)
// IsSafeToDelete  => Confidence >= High
// RiskBadgeText   => Confidence switch { Certain => "Kesin", High => "Yüksek güven", Medium => "İnceleyin", _ => "Düşük güven" }
```
"%100 Güvenli" metni **tamamen kaldırılır.**

**`NameMatcher` kuralları:**
1. Normalize: küçük harfe çevir, harf ve rakam dışındaki her şeyi boşluk yap, CamelCase'i ayır (`VideoLAN` → `video lan`), sürüm ve mimari ifadelerini at (`x64`, `x86`, `64-bit`, `v1.2.3`, `(x64)`, `version`, `sürüm`).
2. Durdurma kelimeleri: mevcut `GenericBlacklistTokens` + `media, player, studio, pro, free, suite, manager, launcher, helper, driver, runtime, redistributable, edition, community, professional, ultimate, home, plus, online, cloud, sync, drive, reader, viewer, editor, converter, and, for, of`.
3. **Kelime bazlı eşleşme:** Token'lar kelime olarak karşılaştırılır. `git` yalnızca `git` kelimesiyle eşleşir, `digital` ile eşleşmez.
4. Seviyeler:
   - `NameExact` (High): Normalize klasör adı, normalize uygulama adına eşit. Ya da uygulama adından sürüm atılmış hâline eşit ("Notepad++ (64-bit x64)" ile "notepad" eşleşir).
   - `NameVendorApp` (High): Yol `<kök>\<Yayıncı>\<UygulamaAdı>` yapısında ve her iki parça eşleşiyor. Örnek: `AppData\Roaming\VideoLAN\VLC`.
   - `NameContains` (Medium): Klasör adı, uygulama adının **tüm anlamlı token'larını** sırasıyla içeriyor.
   - `NameToken` (Low): Tek anlamlı token eşleşiyor ve token uzunluğu ≥ 5.
   - Tek başına yayıncı eşleşmesi **hiçbir seviye vermez.** Yayıncı klasörü (`AppData\Local\Google`) yalnızca *içine bakmak* için kullanılır: altında uygulama adıyla eşleşen bir alt klasör varsa o alt klasör aday olur.
5. `ResidualScannerEngine`'deki `CalculateMatchConfidence` silinir, yerine `NameMatcher` kullanılır. Yayıncı ve token karışımından gelen 100 puan kuralı kaldırılır.
6. **Varsayılan seçim:** yalnızca `Confidence >= High`. Toplu kaldırmada otomatik temizleme yalnızca `Certain`. `DeepUninstallerService.ExecuteAutoCleanResidualsAsync` içindeki `>= 100` filtresi `== Confidence.Certain` olur ve **isim tabanlı kanıtlar hiçbir zaman Certain olamaz.**

**Testler** (`[Theory]`, her satır: uygulama adı, yayıncı, aday yol, beklenen seviye ya da "aday değil"):

| Uygulama | Yayıncı | Aday | Beklenen |
|---|---|---|---|
| Git | The Git Development Community | `%ProgramFiles%\Digital Sound` | aday değil |
| Git | … | `%ProgramFiles%\Git` | High (NameExact) |
| VLC media player | VideoLAN | `%ProgramFiles%\Windows Media Player` | aday değil |
| VLC media player | VideoLAN | `%AppData%\vlc` | High |
| Google Drive | Google LLC | `%LocalAppData%\Google` | aday değil |
| Google Drive | Google LLC | `%LocalAppData%\Google\DriveFS` | Low ya da aday değil (ad eşleşmiyor; kanıt gerekir) |
| Google Drive | Google LLC | `%LocalAppData%\Google\Chrome` | aday değil |
| Adobe Acrobat Reader | Adobe | `%AppData%\Adobe\Photoshop` | aday değil |
| Microsoft Edge | Microsoft | `…\EdgeWebView` | aday değil |
| Notepad++ (64-bit x64) | Notepad++ Team | `%AppData%\Notepad++` | High |

---

#### A5. `UninstallCommandParser`: komut ayrıştırma ve sessiz parametreler

**Dosyalar:**
- yeni `Services/Uninstall/UninstallCommandParser.cs`
- yeni `Services/Uninstall/InstallerKindDetector.cs`
- `Services/DeepUninstallerService.cs`
- `Models/UninstallerModels.cs` (enum genişletme)
- testler

**`InstallerType` enum'una eklenecekler:** `WixBurn, Squirrel, Steam, AdvancedInstaller, Unknown`. `GenericExe` geriye uyumluluk için kalır.

**Ayrıştırma algoritması:**
1. `Environment.ExpandEnvironmentVariables(command.Trim())`.
2. Komut `msiexec` ile başlıyorsa (tırnaklı ya da tırnaksız, `.exe` olsun olmasın): `\{[0-9A-Fa-f-]{36}\}` ile **ProductCode**'u çıkar. Sonuç `("msiexec.exe", "/X{GUID}")`. Eski `/I` → `/X` regex hilesini kullanma; argümanı sıfırdan kur.
3. `"` ile başlıyorsa: ikinci tırnağa kadar dosya, gerisi argüman.
4. Tırnaksızsa: Boşluklara böl. `i = 1..n` için ilk `i` parçayı birleştir. `File.Exists(aday)` ya da `File.Exists(aday + ".exe")` olan **ilk** adayı dosya say, gerisi argümandır. Hiçbiri yoksa `.exe` (büyük/küçük harf duyarsız) ile biten ilk konuma kadar olan kısmı dosya say.
5. `rundll32.exe`, `cmd.exe /c`, `powershell` komutları olduğu gibi geçer. Bunlar sessiz moda **dönüştürülmez**.

**`InstallerKindDetector`** (sırayla dene, ilk uyan kazanır):

| Tür | Tespit ölçütü |
|---|---|
| Msi | ProductCode bulundu ya da `WindowsInstaller = 1` değeri var |
| InnoSetup | Dosya adı `unins\d{3}\.exe` ve yanında aynı numaralı `.dat` dosyası var |
| Squirrel | Dosya adı `Update.exe` ve argümanda `--uninstall` |
| WixBurn | Yol `\Package Cache\` içeriyor ve argümanda `/uninstall` |
| Steam | Uninstall anahtar adı `Steam App <n>` ya da komut `steam://uninstall/` içeriyor |
| Nsis | Dosyanın ilk 1 MB'ında ASCII `Nullsoft` dizesi geçiyor |
| InstallShield | Komut `-uninst`/`-removeonly` içeriyor ya da `InstallShield` dizesi var |
| GenericExe | yukarıdakilerin hiçbiri |

**Sessiz argüman tablosu.** `QuietUninstallString` doluysa **her zaman o kullanılır** ve bu tabloya bakılmaz.

| Tür | Argüman | Başarılı çıkış kodları |
|---|---|---|
| Msi | `/X{GUID} /qn /norestart` | 0, 1605 (zaten yok), 3010 ve 1641 (yeniden başlatma gerekli) |
| InnoSetup | mevcut + `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART` | 0 |
| Nsis | mevcut + `/S _?=<exe'nin klasörü>`. **`_?=` en sonda olmalı.** Bu parametre kaldırıcının kendini temp'e kopyalamasını engeller ve bitene kadar bekler. Bittiğinde `uninstall.exe` ve klasör geride kalır; B fazında kalıntı olarak temizlenir. | 0 |
| Squirrel | `--uninstall -s` | 0 |
| WixBurn | mevcut + `/quiet /norestart` | 0, 3010 |
| Steam | sessiz modu yok; `steam://uninstall/<id>` arayüzlü çalışır | — |
| InstallShield, GenericExe | **Sessiz desteklenmiyor.** `SupportsSilent = false` | — |

`InstalledAppItem.HasSilentUninstall` hesabı bu tabloya göre yeniden yazılır.

**Testler** (en az 15 gerçekçi `UninstallString`):
- `MsiExec.exe /I{23170F69-40C1-2702-2201-000001000000}`
- `"C:\Program Files\Notepad++\uninstall.exe"`
- `C:\Program Files (x86)\Foo Bar\uninst.exe /S`: dosyanın var olması test klasöründe sahte dosyayla sağlanır
- `"C:\Users\x\AppData\Local\Discord\Update.exe" --uninstall`
- `"C:\ProgramData\Package Cache\{guid}\setup.exe" /uninstall`
- `"C:\Program Files (x86)\Steam\steam.exe" steam://uninstall/730`
- `rundll32.exe dfshim.dll,ShArpMaintain ...`
- `%ProgramFiles%\X\unins000.exe`
- tırnaklı ve argümanlı Inno komutu
- boş ve boşluktan oluşan girdi

---

#### A6. İzlenen kaldırma çalıştırıcısı ve doğrulama

**Amaç:** U-P0-6. Kaldırıcının *gerçekten* bitmesini beklemek ve kaldırmanın başarılı olup olmadığını doğrulamak.

**Dosyalar:**
- yeni `Helpers/JobObject.cs`
- yeni `Services/Uninstall/UninstallRunner.cs`
- `Services/DeepUninstallerService.cs`
- `Services/UninstallerService.cs` (`RegistryKeyPath` biçimi)
- testler

**`JobObject.cs`** (P/Invoke):
- `CreateJobObjectW`
- `AssignProcessToJobObject`
- `QueryInformationJobObject` (sınıf `JobObjectBasicAccountingInformation = 1`; `ActiveProcesses` alanı okunur)
- `CloseHandle`

`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` **ayarlanmaz**; Bakım kapanırsa kaldırıcı ölmemeli.

**`UninstallRunner.RunAsync(InstalledAppItem app, bool silent, IProgress<UninstallProgress>, CancellationToken)`:**
1. `UninstallCommandParser` ile dosya ve argümanı çıkar.
2. Bakım yöneticiyse `UseShellExecute = false` ile başlat ve hemen `AssignProcessToJobObject` çağır. Çocuk süreçler otomatik olarak job'a katılır; Inno'nun temp kopyası da bunlara dahil.
3. Yönetici değilse ya da `Win32Exception.NativeErrorCode == 740` (`ERROR_ELEVATION_REQUIRED`) alınırsa `UseShellExecute = true`, `Verb = "runas"` ile başlat. Job kullanılamaz; yerine 500 ms aralıkla ToolHelp32 (`CreateToolhelp32Snapshot` + `Process32FirstW/NextW`) ile **torun süreçleri** izle: `ParentPid` ağaçta **ve** başlangıç zamanı ebeveyninkinden büyük olanlar.
4. Bitiş koşulu: job'daki aktif süreç sayısı 0 ya da izlenen ağaçta canlı süreç kalmadı.
5. Zaman aşımı: sessiz modda 15 dk. Arayüzlü modda zaman aşımı yok ama UI'da "Beklemeyi bırak" düğmesi bulunur (`CancellationToken`). İptal, kaldırıcıyı **öldürmez**, yalnızca beklemeyi bırakır.
6. Bittikten sonra **doğrulama** (en fazla 10 sn, 1 sn aralıkla tekrar):
   - `app.RegistryKeyPath` anahtarı (doğru görünümde, A2) hâlâ var mı?
   - MSI ise `MsiQueryProductState(productCode)` → 5 (`INSTALLSTATE_DEFAULT`) mü?
7. Sonuç:
   ```csharp
   public enum UninstallOutcome { Removed, StillInstalled, RebootRequired, Cancelled, Failed, TimedOut }
   public sealed record UninstallRunResult(UninstallOutcome Outcome, int? ExitCode, TimeSpan Duration, string Detail);
   ```
   - Anahtar gitti → `Removed`.
   - Anahtar duruyor ve çıkış kodu 1602 (kullanıcı iptal etti) ya da arayüzlü mod → `Cancelled`.
   - Diğer durumlarda → `StillInstalled`.
8. `UninstallerService.ScanRegistryKey`, `RegistryKeyPath`'i `RegistryPath.ToDisplay()` biçiminde ve görünüm soneki (`[32]`/`[64]`) ile yazacak şekilde güncellenir.

**Kural:** `Outcome != Removed` ise sihirbaz ve toplu kaldırma **InstallLocation'ı kalıntı olarak asla önermez.** Sihirbaz şunu gösterir: "Kaldırma tamamlanmadı ya da iptal edildi." Seçenekler: [Tekrar dene] [Zorla kaldır…] [Kapat]. Zorla kaldırma ayrıca açık onay ister.

**Testler:**
- `cmd.exe /c ping -n 3 127.0.0.1 >nul` ile job bekleme süresinin ≥ 2 sn olduğu.
- Çocuk süreç başlatan bir komutla (`cmd /c start /b ping -n 3 127.0.0.1`) ebeveyn hemen çıksa da beklendiği.
- Doğrulama mantığı için `IRegistryReader` sahtesi (fake) ile `Removed`, `StillInstalled` ve `Cancelled` durumları.

---

#### A7. Sihirbaz ve toplu kaldırmanın güvenli akışa bağlanması

**Dosyalar:**
- `ViewModels/DeepUninstallWizardViewModel.cs`
- `Services/DeepUninstallerService.cs`
- `ViewModels/UninstallerViewModel.cs`
- `Services/ResidualScannerEngine.cs`

**Adımlar:**
1. Sihirbazdaki ve `DeepUninstallerService` içindeki `KillProcessesForApp` metotlarını sil. Yerine `ISafeProcessService.FindProcessesUnder(InstallLocation)` kullan. Onay ekranında **listeyi göster** ("Kapatılacak: foo.exe (PID 1234)"). Liste boşsa ya da kapsam guard'dan geçmiyorsa seçeneği devre dışı bırak ve nedenini yaz.
2. `ExportRegistryBackupSafe` metodunu **sil**. Yedeği artık `SafeRegistryService` otomatik alır. `RegistryBackupFilePath` alanı journal klasörünü gösterir.
3. `CleanResidualItemsAsync` ve `CleanResidualsAsync`, `SafeDeleteService` ile `SafeRegistryService` üzerinden çalışır. Kendi `Directory.Delete` ve `DeleteSubKeyTree` çağrıları silinir.
4. `CleanedSizeBytes` yalnızca `Outcome ∈ {Deleted, Recycled}` olan öğelerin gerçek boyutlarının toplamıdır.
5. Tamamlandı ekranında başarısız öğeler nedenleriyle listelenir. `InUse` olanlar için "Yeniden başlatmada sil" düğmesi (`ScheduleDeleteOnReboot`) sunulur.
6. **Toplu kaldırma:**
   - Başlamadan **tek** geri yükleme noktası oluştur (bkz. A8).
   - `SupportsSilent = false` olan uygulamaları başta ayır ve kullanıcıya iki seçenek sun: "Bunlar arayüzlü, tek tek açılacak" ya da "Atla".
   - Her uygulama için `UninstallRunner` kullan. Yalnızca `Outcome == Removed` olanları listeden kaldır.
   - Rapor üç grup gösterir: Başarılı, Başarısız (neden), Atlandı.
   - Otomatik temizleme yalnızca `Confidence.Certain` öğelere uygulanır.
7. `ScanHeuristicResidualsAsync` içindeki "üst klasörü %100 ekle" mantığını kaldır. Taşınabilir uygulama akışı C4'e taşınır.

**Kabul kriteri:** `grep -rn "Directory.Delete\|DeleteSubKeyTree\|\.Kill(" Services ViewModels` yalnızca `Services/Safety/*` içinde sonuç veriyor. Tek istisna, Kurulum Nöbetçisi planı kapsamındaki `SetupSentinelService`/`InstallerMonitorService` olabilir; bunlar orada ayrıca ele alınıyor.

---

#### A8. Geri yükleme noktası: dürüst ve tek seferlik

**Dosyalar:**
- `Services/DeepUninstallerService.cs` (`CreateRestorePointAsync`)
- yeni `Services/Safety/RestorePointService.cs`

**Adımlar:**
1. Yöneticiyse PowerShell yerine WMI kullan: `root\default` ad alanındaki `SystemRestore` sınıfının `CreateRestorePoint(Description, RestorePointType = 12 (MODIFY_SETTINGS) veya 1 (APPLICATION_UNINSTALL), EventType = 100)` metodu (`System.Management`, projede zaten var). Yönetici değilse bu özelliği devre dışı göster; runas ile PowerShell açma.
2. Sonuç enum'u: `Created`, `SkippedFrequencyLimit`, `Disabled`, `NotAdmin`, `Failed`.
   - Windows varsayılan olarak 24 saatte bir nokta oluşturur. Son noktanın zamanı `SystemRestore` sınıfından sorgulanabilir. Son nokta < 24 saatse `SkippedFrequencyLimit` döndür ve UI'da "Son 24 saatte zaten bir nokta var (tarih)" yaz.
   - `SystemRestorePointCreationFrequency` kayıt değerini **değiştirme**; bu kullanıcının sistem ayarıdır.
3. Toplu işlemde yalnızca bir kez çağrılır.

---

### FAZ B: Kanıt tabanlı kalıntı motoru (isabet)

#### B1. `FootprintCollector`: kaldırmadan önce iz toplama

**Amaç:** Kaldırıcı çalışmadan önce uygulamaya ait olduğu **kanıtlanabilen** her şeyi kaydetmek. Kaldırmadan sonra hâlâ duranlar `Certain` seviyesinde kalıntıdır.

**Dosyalar:**
- yeni `Services/Uninstall/FootprintCollector.cs`
- yeni `Models/UninstallFootprint.cs`
- yeni `Helpers/MsiInterop.cs`
- testler

**Model:**
```csharp
public sealed class UninstallFootprint
{
    public string AppKey { get; init; } = "";               // RegistryKeyPath
    public string? InstallLocation { get; set; }            // PathSafetyGuard'dan geçmiş ve var olan klasör
    public List<FootprintItem> Items { get; } = new();
}
public sealed record FootprintItem(ResidualCategory Category, string Target, string EvidenceText, Confidence Confidence);
```

**Toplanacaklar.** `installDir` = doğrulanmış InstallLocation. InstallLocation boşsa sırayla `DisplayIcon`'un klasörü, sonra `UninstallString` dosyasının klasörü denenir; bunlar da guard'dan geçmeli.

| Kaynak | Yöntem | Kategori |
|---|---|---|
| Kurulum klasörü | `installDir` | Folder |
| MSI bileşenleri | `MsiInterop.GetComponentPaths(productCode)`: COM `WindowsInstaller.Installer` → `ProductInfo(pc, "LocalPackage")` ile önbellekteki .msi → `OpenDatabase(path, 0)` → `SELECT ComponentId FROM Component` → her biri için `ComponentPath(pc, componentId)`. Dosya yolları doğrudan kullanılır. Registry anahtar yolları `NN:\Anahtar` biçimindedir: `00`=HKCR, `01`=HKCU, `02`=HKLM, `03`=HKU, `+20` 64 bit (ör. `22:`). **Yalnızca okuma; Windows\Installer'daki .msi'ye asla yazma.** | File / RegistryKey |
| Servisler | `HKLM\SYSTEM\CurrentControlSet\Services\*\ImagePath` → `installDir` altındaysa | Service |
| Zamanlanmış görevler | COM `Schedule.Service` → `Connect()` → `GetFolder("\\")` → alt klasörler dahil `GetTasks(1)` → `Definition.Actions` içindeki `Path` `installDir` altındaysa | ScheduledTask |
| Run girdileri | HKCU/HKLM (her iki görünüm) `...\CurrentVersion\Run` ve `RunOnce` değerleri; komut `installDir`'e işaret ediyorsa | StartupEntry |
| Kısayollar | Masaüstü, Ortak Masaüstü, `Programs`, `CommonPrograms` (özyinelemeli), `%AppData%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar`. `.lnk` hedefi (mevcut WScript.Shell çözücü) `installDir` altındaysa | Shortcut |
| Firewall | COM `HNetCfg.FwPolicy2` → `Rules`; `ApplicationName` `installDir` altındaysa | FirewallRule |
| App Paths | `HKLM` ve `HKCU` `Software\Microsoft\Windows\CurrentVersion\App Paths\*` varsayılan değeri `installDir` altındaysa | RegistryKey |
| ProgId / dosya ilişkilendirme | `HKCU\Software\Classes` ve `HKLM\Software\Classes` altındaki ProgId'lerde `shell\open\command` `installDir`'e işaret ediyorsa → ProgId anahtarı + bu ProgId'yi gösteren `.ext\OpenWithProgids` **değerleri** | RegistryKey / RegistryValue |
| COM sunucuları | `Classes\CLSID\*\InprocServer32` ve `LocalServer32` varsayılan değeri `installDir` altındaysa | RegistryKey |
| Kurulum Nöbetçisi izi | `SessionStore` içinde bu uygulamanın Uninstall anahtarına bağlı rapor varsa `Created` öğeleri (bkz. `docs/SENTINEL_V2_PLAN.md` 5.5). Bu görev yalnızca arayüz noktasını ekler (`ISetupTraceProvider`, varsayılan uygulama boş liste döner) | çeşitli |

**Performans bütçesi:** Toplam 5 sn. Registry ve COM taramaları `Task.Run` içinde paralel çalışır. Bütçe aşılırsa kalan kaynaklar atlanır ve `IsPartial = true` işaretlenir.

**Testler:**
- Sahte bir `installDir` altında `.lnk` ve `HKCU` Run değeri oluştur, bunların toplandığını doğrula.
- Aynı `.lnk` başka bir klasöre işaret ederse toplanmadığını doğrula.

---

#### B2. Kaldırma sonrası kalıntı hesabı

**Dosyalar:**
- yeni `Services/Uninstall/ResidualResolver.cs`
- `Services/ResidualScannerEngine.cs`

**Adımlar:**
1. `ComputeResidualsAsync(UninstallFootprint before, InstalledAppItem app, UninstallRunResult run)`:
   - Footprint'teki öğelerden **hâlâ var olanlar** → `Certain` ya da `High`, `EvidenceText` ile.
   - `run.Outcome != Removed` ise **boş liste** döner ve UI A6'daki mesajı gösterir.
2. **Referans taraması** (`High`): `HKCU\Software` ve `HKLM\Software` (her iki görünüm) altında en fazla 4 seviye derinlikte, 3 sn bütçeyle string değerlerde `installDir` geçiyorsa değerin anahtarı aday olur. Aday anahtar bir `Uninstall` anahtarı ya da korumalı anahtar olamaz.
3. **İsim sezgisi** (A4'teki `NameMatcher`): Mevcut `ScanResidualsAsync` dizin listesi + B5'teki ek konumlar. Sonuçlar `Medium` ya da `Low` seviyesindedir ve **seçili gelmez.**
4. **"Başka uygulamanın alanı" süzgeci:** Hâlâ kurulu olan uygulamaların `InstallLocation`'ları listesini al (`GetInstalledAppsAsync`, kaldırılan hariç). Bir aday klasör başka bir uygulamanın InstallLocation'ına eşitse, onu içeriyorsa ya da onun içindeyse → **aday listesinden çıkar.** Kayıt anahtarı adayı `Software\<Vendor>` ise ve altında başka kurulu bir uygulamanın `DisplayName`'iyle eşleşen alt anahtar varsa → çıkar.
5. **Birleştirme:** Aynı hedef birden çok kaynaktan gelirse en yüksek güveni koru, kanıt metinlerini birleştir.

---

#### B3. Tek model, tek motor (yeniden düzenleme)

**Dosyalar:**
- `Services/UninstallerService.cs`
- `Services/DeepUninstallerService.cs`
- `Services/ResidualScannerEngine.cs`
- `Models/UninstallerModels.cs`
- `ViewModels/UninstallerViewModel.cs`
- `Views/Modules/UninstallerModuleView.xaml`
- yeni `Helpers/ByteFormatter.cs`

**Adımlar:**
1. `UninstallerService` yalnızca **listeleme** yapan bir sınıfa indirgenir. `ScanLeftoversAsync`, `CleanLeftoversAsync` ve `LaunchStandardUninstallAsync` silinir; arayüzden de çıkarılır. Yeni adı `IInstalledAppsProvider` olabilir. DI kaydını ve testlerdeki sahte sınıfları buna göre güncelle.
2. `LeftoverItem` kullanımı `ResidualItem`'e taşınır. `UninstallerViewModel.Leftovers`, `ObservableCollection<ResidualItem>` olur ve XAML bağlamaları güncellenir. Sonra `LeftoverItem` ve `LeftoverType` silinir. `IResidualScannerEngine` içindeki `LeftoverItem` dönüşlü metotlar kaldırılır.
3. Bütün `FormatBytes` kopyaları `ByteFormatter.Format(long)` ile değiştirilir.
4. `ResidualScannerEngine.ScanResidualsStaticAsync` (DI'sız örnek oluşturma) silinir; çağıranlar DI'dan alır.

**Kabul kriteri:** Davranış değişmeden (A ve B testleri geçerek) kod tekrarı kalkmış olur. `grep -rn "FormatBytes" --include=*.cs` yalnızca `ByteFormatter` ve alakasız modüllerde (Cleaner vb.) sonuç verir.

---

#### B4. Yeni kalıntı kategorileri ve kaldırma yöntemleri

**Dosyalar:**
- `Models/ResidualItem.cs`
- `Services/Safety/*`
- yeni `Services/Uninstall/ResidualRemover.cs`

```csharp
public enum ResidualCategory { Folder, File, RegistryKey, RegistryValue, Service, ScheduledTask, StartupEntry, Shortcut, FirewallRule }
```

| Kategori | Kaldırma | UI ikonu (SymbolRegular) |
|---|---|---|
| Folder / File / Shortcut | `SafeDeleteService` (Geri Dönüşüm) | `Folder20` / `Document20` / `Link20` |
| RegistryKey | `SafeRegistryService.DeleteKeyAsync` | `Tag20` |
| RegistryValue / StartupEntry | `SafeRegistryService.DeleteValueAsync` | `Tag20` / `Rocket20` |
| Service | Durdur (`sc.exe stop`), ardından `sc.exe delete <ad>`. Öncesinde servis anahtarını .reg olarak yedekle. `ServiceManagerService`'teki `sc.exe` çağrı kalıbını kullan | `Settings20` |
| ScheduledTask | Önce XML yedeği: `schtasks /query /tn "<yol>" /xml` çıktısını journal'a yaz. Sonra `schtasks /delete /tn "<yol>" /f`. Geri alma: `schtasks /create /tn "<yol>" /xml <dosya>` | `CalendarClock20` |
| FirewallRule | COM `HNetCfg.FwPolicy2`. `Rules.Remove(name)` **aynı adlı tüm kuralları** siler; bu yüzden önce aynı adı taşıyan ve farklı `ApplicationName`'e sahip kural var mı kontrol et. Varsa bu öğeyi atla ve "elle kaldırın" de | `Shield20` |

Kullanılan her sembolün `Tools/SymbolRegular.txt` içinde bulunduğunu doğrula (`verify-symbols.py`).

---

#### B5. Tarama konumlarının genişletilmesi

**Dosya:** `Services/ResidualScannerEngine.cs`

Aday köklere eklenecekler (hepsi yalnızca 1 seviye alt klasör olarak taranır):
- `LocalApplicationData\Programs`
- `MyDocuments`
- `UserProfile\Saved Games`
- `UserProfile` (yalnızca `.` ile başlayan klasörler, örn. `.vscode`)
- `CommonApplicationData`
- Masaüstü ve Ortak Masaüstü (`.lnk`/`.url` dosyaları, NameMatcher ile)

**Çıkarılacaklar:** `Path.GetTempPath()` kökü isim taramasından çıkarılır. Temp'teki rastgele adlı klasörler yanlış pozitif üretiyor; yalnızca footprint'ten gelirse aday olur.

---

### FAZ C: Sağ tık menüsü v2

#### C1. Başlangıç yönlendirmesi: hedef modunda hafif açılış

**Dosya:** `App.xaml.cs`

**Adımlar:**
1. `--uninstall-target` (ve C5'teki çoklu hedef biçimi `--uninstall-targets-file <json>`) ayrıştırmasını `OnStartup`'ın en başına, `--register-contextmenu` kontrollerinin yanına taşı.
2. Hedef modunda:
   - DI konteyneri kurulur (sihirbaz servislere ihtiyaç duyar).
   - **Başlatılmayacaklar:** `IBackgroundMaintenanceService.Start()`, `ISetupSentinelService` olay bağlama, tepsi ikonu, sağ tık otomatik kaydı. `ISetupSentinelService` singleton'ı constructor'da `Start()` çağırdığı için bu servis hedef modunda **hiç çözümlenmemeli**; DI'da lazy olduğu sürece çözümlememek yeterli. Bunu bir testle doğrula.
3. Bakım zaten çalışıyorsa hedefi ona ilet (C2); iletme başarılıysa bu süreç `Shutdown(0)` yapar.

#### C2. Tek örnek ve IPC

**Dosyalar:**
- yeni `Services/SingleInstanceService.cs`
- `App.xaml.cs`
- testler

**Tasarım:**
- Mutex: `Local\Bakim.SingleInstance`. Aynı oturumda tek örnek.
- Pipe adı: `Bakim.Ipc.<kullanıcı SID>`. Birincil örnek `NamedPipeServerStream`'i `PipeTransmissionMode.Message` ile açar ve sürekli dinler.
- **Güvenlik:**
  - Pipe ACL'i yalnızca **geçerli kullanıcının SID'ine** ReadWrite verir: `PipeSecurity` + `NamedPipeServerStreamAcl.Create`.
  - Birincil örnek yönetici (yüksek bütünlük) çalışıyorsa, orta bütünlükteki istemcinin yazabilmesi için pipe'a **Medium mandatory label** eklenir: SDDL `S:(ML;;NW;;;ME)`. `SetSecurityInfo` P/Invoke ya da `RawSecurityDescriptor` ile uygulanır.
  - Gelen her mesaj **güvenilmeyen girdi**dir. Yalnızca `{"cmd":"uninstall-target","paths":[...]}` ve `{"cmd":"activate"}` kabul edilir. Yollar `File.Exists`/`Directory.Exists` ile doğrulanır. En fazla 50 yol ve 64 KB mesaj kabul edilir.
  - **Hiçbir IPC mesajı doğrudan yıkıcı işlem yapmaz.** Yalnızca sihirbazı açar; silme her zaman UI onayı ister.
- **Toplama:** İlk mesajdan sonra 400 ms içinde gelen hedefler tek listede toplanır (çoklu seçim).
  - 1 hedef → sihirbaz.
  - N hedef → "Seçilen N programı kaldır" toplu ekranı (C6).

**Testler:** Aynı test süreci içinde sunucu ve istemci çalıştırılır. Geçerli mesaj işlenir; bozuk JSON, bilinmeyen komut ve 51 yol reddedilir.

#### C3. `BakimShell.exe`: UAC'siz küçük başlatıcı

**Neden:** `Bakim_Setup.iss` `Bakim.exe`'ye `RUNASADMIN` uyumluluk katmanı yazıyor. Sağ tık doğrudan `Bakim.exe`'yi çağırdığı için her tıklamada UAC istemi çıkıyor (S-2). Çoklu seçimde de N süreç açılıyor (S-3).

**Dosyalar:**
- yeni proje `Tools/BakimShell/BakimShell.csproj`: `WinExe`, `net10.0-windows`, WPF ve WinForms **yok**, `asInvoker` manifest
- yeni `Tools/BakimShell/Program.cs` (≈ 80 satır)
- `Bakım.slnx`
- `Bakim_Setup.iss`
- `.github/workflows/release.yml`
- `Bakım.csproj`: `<Compile Remove="Tools\**" />` ekle; ana proje bu dosyaları derlemesin

**Davranış:**
1. `BakimShell.exe --uninstall-target "<yol>"` çağrıldığında:
   - Pipe'a bağlanmayı dener (200 ms zaman aşımı). Başarılıysa mesajı gönderip çıkar. Bakım zaten yönetici çalıştığı için **UAC istemi olmaz.**
   - Bakım çalışmıyorsa `Bakim.exe --uninstall-target "<yol>"` başlatır (UAC burada bir kez çıkar) ve çıkar.
2. `BakimShell.exe`'ye **RUNASADMIN yazılmaz.** İmzalama adımına (`Build-Release.ps1`/signtool) eklenir.
3. `ShellContextMenuService`, komutu `BakimShell.exe` varsa onunla, yoksa `Bakim.exe` ile kaydeder (geliştirme ortamı için geri dönüş).

#### C4. Menü kaydı v2

**Dosyalar:**
- `Services/ShellContextMenuService.cs`
- `Models/AppSettingsData.cs`
- `ViewModels/SettingsViewModel.cs`
- `Views/Modules/SettingsModuleView.xaml`
- `Tests/Bakim.Tests/SettingsShellIntegrationTests.cs`

**Kayıt hedefleri:**

| Anahtar (`Software\Classes\...`) | Varsayılan | Not |
|---|---|---|
| `lnkfile\shell\BakimUninstall` | açık | |
| `exefile\shell\BakimUninstall` | açık | |
| `InternetShortcut\shell\BakimUninstall` | açık | `.url`: Steam ve Epic oyunları |
| `Msi.Package\shell\BakimUninstall` | açık | `.msi`: "Bu paketi kaldır" |
| `Directory\shell\BakimUninstall` | açık ama `Extended` değeriyle | Yalnızca Shift + sağ tıkta görünür. Ayarlardan tamamen kapatılabilir |

**Kurallar:**
- Her anahtara şunlar yazılır: varsayılan değer `Bakım ile Kaldır`, `Icon`, `Position = "Bottom"`, `command\(varsayılan)`.
- **Tek kapsam:** Kurulum (`--register-contextmenu`, yönetici) HKLM'e yazar. Ayarlar'daki anahtar HKCU'ya yazar. `IsContextMenuRegistered()` **tüm hedefleri** iki kapsamda da kontrol eder ve bir durum nesnesi döner:
  ```csharp
  public sealed record ContextMenuStatus(bool AnyRegistered, bool AllRegistered, bool CommandPathStale, string Scope);
  ```
- **Kendini onarma:** Açılışta (normal mod) kayıt varsa ve `command` değerindeki exe yolu mevcut kurulumla eşleşmiyorsa (`CommandPathStale`) kayıt yeniden yazılır ve loglanır.
- `UnregisterContextMenu` her iki kapsamdan ve eski sürümün yazdığı tüm hedeflerden siler.
- `AppSettingsData` alanları: `ShellMenuOnFolders` (`None` / `ShiftOnly` / `Always`, varsayılan `ShiftOnly`), `ShellMenuOnUrl` (varsayılan true), `ShellMenuOnMsi` (varsayılan true).

**Testler:** Kayıt HKCU'da gerçek anahtarlarla yapılır (test sonunda silinir). Tüm hedeflerin yazıldığı, `Extended` değerinin yalnızca Directory'de bulunduğu, stale tespiti ve unregister sonrası hiçbir anahtarın kalmadığı doğrulanır.

#### C5. Hedef çözümleyici v2

**Dosyalar:**
- `Services/ShellUninstallResolverService.cs`
- yeni `Models/ShellResolveResult.cs`
- `Helpers/MsiInterop.cs`
- `Tests/Bakim.Tests/ShellUninstallResolverTests.cs`

**Sözleşme:**
```csharp
public enum ResolveStatus { Matched, Ambiguous, Portable, Protected, NotFound }
public sealed record ResolveCandidate(InstalledAppItem App, int Score, string Reason);
public sealed record ShellResolveResult(
    ResolveStatus Status,
    string InputPath,
    string? ResolvedExecutable,
    IReadOnlyList<ResolveCandidate> Candidates,   // skora göre azalan
    string Explanation);                          // UI'da gösterilecek Türkçe açıklama

public interface IShellUninstallResolverService
{
    Task<ShellResolveResult> ResolveAsync(string rawPath);
    [Obsolete] Task<InstalledAppItem?> ResolveTargetAppAsync(string rawPath); // Status==Matched ise Candidates[0].App, değilse null
}
```

**Çözümleme sırası:**
1. **Girdi doğrulama:** Yol yoksa `NotFound`. `PathSafetyGuard` ağaç korumasındaysa (örneğin `C:\Windows\...`) ya da Bakım'ın kendisiyse `Protected`; açıklama: "Windows sistem bileşenleri kaldırılamaz."
2. **`.lnk`:**
   - Önce `MsiGetShortcutTarget(lnk, productCode, featureId, componentCode)` dene. Bu bir P/Invoke'tur ve her tampon 39 karakterdir. Başarılıysa ProductCode ile Uninstall anahtarı bulunur (anahtar adı `{GUID}`) → `Matched`, skor 100, neden "MSI kısayolu (ürün kodu)".
   - Değilse mevcut WScript.Shell ile hedef çözülür.
   - Hedef `Update.exe` ve argüman `--processStart X` ise (Squirrel) klasör hedef kabul edilir.
3. **`.url`:** `URL=` satırı okunur.
   - `steam://rungameid/<n>` → Uninstall anahtarı `Steam App <n>`.
   - `com.epicgames.launcher://apps/<id>` → Epic ile eşleşen Uninstall anahtarı aranır; bulunamazsa `NotFound`.
4. **`.msi`:** COM `WindowsInstaller.Installer.OpenDatabase(path,0)` → `SELECT Value FROM Property WHERE Property='ProductCode'`. Kurulu ürünlerde bu kod varsa → `Matched`.
5. **Klasör:**
   - Klasör `PathSafetyGuard` "tam korumalı" listesindeyse (`C:\Program Files` vb.) → `Protected`: "Bu bir sistem klasörü. Lütfen programın kendi klasörünü seçin."
   - Ana exe seçimi: `unins*`, `uninst*`, `setup*`, `update*`, `crash*`, `*helper*`, `*service*` hariç; klasör adına en çok benzeyen, eşitlikte en büyük exe.
6. **exe:**
   - exe bir kaldırıcıysa (`unins\d{3}`, `uninstall.exe`) sahibi olan uygulama, `UninstallString`'i bu exe olan kayıttır.
7. **Puanlama.** Her kurulu uygulama için hesaplanır:

   | Sinyal | Puan |
   |---|---|
   | exe veya klasör `InstallLocation`'a eşit ya da **altında** (`IsUnder`) | +60 |
   | `DisplayIcon` exe'sine eşit | +50 |
   | `UninstallString`'in klasörü exe klasörüne eşit | +40 |
   | Normalize ad eşitliği (kısayol adı ya da exe `FileDescription` ↔ `DisplayName`) | +30 |
   | Yayıncı ↔ exe `CompanyName` eşitliği | +10 |
   | InstallLocation hedef klasörün **üst klasörüyse** | **puan yok** (eski Kural A'nın tersi kaldırıldı) |

   Sonuç: En yüksek skor ≥ 60 ve ikinci skordan en az 20 fazlaysa `Matched`. Aksi halde aday varsa `Ambiguous`, ilk 5 aday UI'da gösterilir. Aday yoksa ve exe bir kurulum klasöründe değilse `Portable`.
8. **Taşınabilir (`Portable`):** InstallLocation **sentezlenmez.** Sonuç şöyle döner:
   - exe'nin bulunduğu klasör `PathSafetyGuard.CheckDeletion(isDirectory: true)` geçiyorsa ve klasörde başka bir uygulamanın exe'si yoksa: "Klasörü Geri Dönüşüm Kutusuna taşı".
   - Geçmiyorsa (İndirilenler, Masaüstü): yalnızca "Bu dosyayı Geri Dönüşüm Kutusuna taşı".

**Testler:**
- Sahte uygulama listesiyle: InstallLocation eşleşmesi, üst klasör eşleşmemesi, Edge ile WebView2 ayrımı (Ambiguous ya da doğru eşleşme), `C:\Windows\explorer.exe` → `Protected`, İndirilenler'deki exe → `Portable` ve klasör silme önerilmiyor.
- `.url` Steam ayrıştırması (sahte `.url` dosyası).
- Klasörde ana exe seçimi (`unins000.exe` seçilmiyor).

#### C6. Hedef modu arayüzü

**Dosyalar:**
- `ViewModels/DeepUninstallWizardViewModel.cs`
- `Views/Windows/DeepUninstallWizardWindow.xaml`
- yeni `Views/Windows/UninstallTargetPickerWindow.xaml` (+ ViewModel)
- `App.xaml.cs` (`LaunchUninstallTargetMode`)

**Ekranlar:**
- `Matched`: Sihirbazın ilk adımında "Nasıl bulundu" satırı gösterilir (`Candidates[0].Reason`), yanında "Yanlış program mı? [Başka program seç]" bağlantısı.
- `Ambiguous`: Aday listesi (ikon, ad, yayıncı, sürüm, neden) + "Seç ve devam et".
- `Portable`: Taşınabilir uygulama kartı; yalnızca izin verilen işlem (dosya ya da klasörü Geri Dönüşüm'e taşı), ardından isim tabanlı AppData önerileri (Medium/Low, seçili değil).
- `Protected` / `NotFound`: Açıklayıcı mesaj ve "Kapat". `MessageBox` yerine pencere içi `InfoBar`.
- Çoklu hedef (C2 toplama): Hedefler listelenir. Her satırın çözümleme durumu gösterilir. Yalnızca `Matched` olanlar seçili gelir. Onaydan sonra A7'deki toplu akış çalışır.

---

### FAZ D: Kaldırıcı modülü deneyimi

#### D1. Liste doğruluğu ve performans

**Dosyalar:** `Services/UninstallerService.cs` (yeni adıyla `InstalledAppsProvider`), `ViewModels/UninstallerViewModel.cs`

1. Tekilleştirme anahtarı `(DisplayName, Publisher, DisplayVersion)` olur. Kalan aynı adlı kayıtlar ayrı gösterilir ve "32 bit / 64 bit / Kullanıcı" rozeti alır.
2. `EstimatedSize` 0 ise liste yüklenirken klasör boyutu **hesaplanmaz**; "Hesaplanıyor…" gösterilir. Boyutlar yükleme bittikten sonra arka planda, en fazla 2 paralel iş ve uygulama başına 3 sn sınırıyla hesaplanır. Sonuçlar `%LocalAppData%\Bakim\cache\app-sizes.json` dosyasında `(RegistryKeyPath, InstallLocation, klasör LastWriteTime)` anahtarıyla önbelleklenir.
3. `NoRemove = 1` olan kayıtlarda kaldır düğmesi devre dışıdır; ipucu: "Yayıncı kaldırmaya izin vermiyor".
4. `InstallDate` boşsa kaydın anahtar yazım zamanı kullanılır (`RegQueryInfoKey` `lpftLastWriteTime`, P/Invoke) ve "yaklaşık" etiketi eklenir.
5. Liste satırlarına "Kurulum izi var" rozeti eklenir (`ISetupTraceProvider`, B1; şimdilik hep false).

#### D2. Sihirbaz adımları v2

Adımlar:
1. **Onay:** Kapatılacak süreçler, geri yükleme noktası durumu, sessiz/arayüzlü seçimi (destekliyorsa).
2. **Ön iz:** B1 çalışır. Sonuç tek satırla özetlenir: "Bulunan: 1 servis, 2 görev, 3 kısayol…".
3. **Kaldırma:** Canlı süre gösterilir, "Beklemeyi bırak" düğmesi vardır.
4. **Doğrulama:** A6'daki sonuç.
5. **Kalıntılar:** Gruplar "Kesin", "Yüksek güven", "İnceleyin" başlıkları altında; her satırda kategori ikonu ve kanıt metni (ipucu olarak) gösterilir.
6. **Temizlik:** Satır bazında sonuç.
7. **Rapor:** Gerçek boşaltılan alan; "Kayıt defterini geri yükle" (journal), "Geri Dönüşüm Kutusunu Aç", "Günlüğü göster".

`WizardStep` enum'u buna göre genişletilir. XAML'deki adım göstergesi 7 adıma çıkarılır.

#### D3. Diyalog soyutlaması (test edilebilirlik)

**Dosyalar:** yeni `Services/DialogService.cs` (`IDialogService`: `Confirm`, `Info`, `Error`, `Choose`); `UninstallerViewModel`; `DeepUninstallWizardViewModel`

ViewModel'lerdeki bütün `MessageBox.Show` çağrıları `IDialogService` üzerinden yapılır. Testlerde sahte uygulama kullanılır.

#### D4. Kaldırma geçmişi

**Dosyalar:** yeni `ViewModels/UninstallHistoryViewModel.cs` ve Kaldırıcı modülünde yeni bir sekme

- Journal klasöründeki kayıtlar listelenir: tarih, uygulama, sonuç, boşaltılan alan, silinen öğe sayısı.
- Her kayıtta "Kayıt defterini geri yükle" ve "Ayrıntılar" bulunur.
- 90 günden eski kayıtlar açılışta temizlenir (ayar).

#### D5. Kurulum Nöbetçisi ile koordinasyon

**Dosyalar:** `Services/SetupSentinelService.cs` (yalnızca yeni metot), `Services/Uninstall/UninstallRunner.cs`

- `ISetupSentinelService`'e `IDisposable SuppressForProcessTree(int rootPid)` eklenir. `UninstallRunner` kaldırıcıyı başlatırken bunu çağırır; böylece nöbetçi kaldırıcıyı "kurulum" sanmaz (bkz. Sentinel planı P0-9).
- Uygulama: bastırılan kök PID ve torunları `IsInstallerProcess` değerlendirmesinden çıkarılır. Bu görevde nöbetçinin başka bir yerine dokunma.

---

### FAZ E: İsteğe bağlı / ileri seviye

**Kullanıcı onayı olmadan başlama.**

- **E1. Windows 11 yeni sağ tık menüsü:** `IExplorerCommand` uygulayan bir COM sunucusu ve kimlik için **seyrek (sparse) MSIX paketi** (`windows.fileExplorerContextMenus` uzantısı) gerekir. Paket imzalı olmalıdır. Bu, kurulum ve imzalama akışını değiştiren büyük bir iştir. Önce ayrı bir tasarım belgesi yazılıp onay alınmalı.
- **E2. Microsoft Store / MSIX uygulamaları:** `Windows.Management.Deployment.PackageManager` için TFM'nin `net10.0-windows10.0.19041.0` olması gerekir. Bu proje genelinde bir değişikliktir, karar gerekli. Alternatif olarak arka planda `powershell -NoProfile -Command "Get-AppxPackage | Select Name,PackageFullName,InstallLocation,Publisher | ConvertTo-Json"` kullanılabilir (yavaş, ≈ 2–4 sn).
- **E3. Inno Setup günlüğü:** `unins000.dat` dosyasında kurulan dosyaların listesi var ama format ikili ve sürüme göre değişiyor. Uygulanırsa yalnızca okuma yapılmalı ve sürüm denetimi olmalı.
- **E4. Winget ile kaldırma:** `winget uninstall --id <id> --silent` (winget yüklüyse), paket kimliği eşleştirmesiyle.

---

## 4. Test stratejisi

### 4.1 Birim testleri (her görevle birlikte)

A1 guard tablosu, A2 registry ayrıştırma, A4 isim eşleştirme tablosu, A5 komut ayrıştırma tablosu, A6 doğrulama mantığı (sahte registry ile), B2 "başka uygulamanın alanı" süzgeci, C2 IPC doğrulama, C5 çözümleyici puanlama.

### 4.2 "Kanarya" entegrasyon testi (Windows CI, yönetici)

**Dosyalar:**
- yeni `Tests/Fixtures/Installers/CanaryApp.iss`
- yeni `Tests/Bakim.IntegrationTests/`
- yeni iş akışı `.github/workflows/tests.yml`

Adımlar:
1. `CanaryApp` Inno kurulumu şunları kurar:
   - `%ProgramFiles%\BakimCanary\canary.exe`
   - `%AppData%\BakimCanary\settings.json`
   - `HKCU\Software\BakimCanary`
   - Run değeri
   - Masaüstü kısayolu
   - zamanlanmış görev `\BakimCanaryTask`
2. **Kanaryalar:** Kurulumdan önce test şunları oluşturur. Bunlar **silinmemesi gereken** benzer adlı öğelerdir:
   - `%ProgramFiles%\BakimCanaryHelperOther\keep.txt`
   - `%AppData%\BakimCanaryOther\keep.txt`
   - `%AppData%\CanaryDigital\keep.txt`
   - `HKCU\Software\BakimCanaryOther`
   - `%UserProfile%\Downloads\keep_canary.txt`
3. Akış: kurulum → sağ tık çözümleyicisi (`.lnk`) → `Matched` → footprint → sessiz kaldırma → doğrulama `Removed` → kalıntılar → yalnızca `Confidence >= High` temizlenir.
4. **Doğrulamalar:**
   - Kurulumun oluşturduğu her şey gitti ya da raporlandı.
   - **Bütün kanaryalar yerinde.**
   - Journal oluştu ve `.reg` yedeği geri yüklenebiliyor.
5. İptal senaryosu: Arayüzlü kaldırma, süreç sonlandırılarak "iptal" edilir. Sonuç `Cancelled` olmalı ve InstallLocation **önerilmemeli**.

### 4.3 Elle test listesi (sürüm öncesi)

| Senaryo | Beklenen |
|---|---|
| 7-Zip (MSI ve exe), Notepad++ (NSIS), VS Code kullanıcı kurulumu (Inno, `LocalAppData\Programs`), Discord (Squirrel), Steam oyunu (`.url`), Office kısayolu (MSI advertised) | Doğru eşleşme, eksiksiz kaldırma |
| Google Drive kaldırılırken Chrome açık ve profili var | Chrome profiline ve `Software\Google`'a dokunulmuyor |
| Masaüstündeki taşınabilir exe'ye sağ tık | Yalnızca dosya seçeneği, klasör önerisi yok |
| `C:\Windows\notepad.exe` ve `C:\Program Files` klasörüne sağ tık | "Korumalı" mesajı |
| 5 kısayolu birden seçip sağ tık | Tek pencere, 5 satır, UAC en fazla 1 kez |
| Bakım tepside çalışırken sağ tık | UAC yok |
| Resmi kaldırıcıda İptal | "Kaldırma tamamlanmadı" ekranı |
| Kullanımda olan dosya | "Yeniden başlatmada sil" seçeneği |

---

## 5. Sürüm planı

| Sürüm | İçerik | Not |
|---|---|---|
| v3.20.0 | A1–A8 | **Güvenlik sürümü.** Yeni özellik yok; yalnızca P0 düzeltmeleri ve testler |
| v3.21.0 | B1–B5 | Kanıt tabanlı kalıntı motoru ve yeni kategoriler |
| v3.22.0 | C1–C6 | Sağ tık v2: BakimShell, tek örnek, çözümleyici v2, `.url`/`.msi` |
| v3.23.0 | D1–D5 | Liste performansı, sihirbaz v2, geçmiş, nöbetçi koordinasyonu |
| sonra | E1–E4 | Kullanıcı kararıyla |

Not: Kurulum Nöbetçisi planı (`docs/SENTINEL_V2_PLAN.md`) da v3.20.0'dan başlıyor. İkisi aynı anda yapılacaksa **önce bu belgenin A fazı** bitirilsin. `PathSafetyGuard`, `ProcessImagePath` ve `SafeDeleteService` nöbetçinin geri alma özelliği için de gerekli.

---

## 6. Görev başlatma şablonu (asistana verilecek komut)

Her görev için asistana aşağıdaki metni gönder; `<GÖREV>` yerine görev kodunu yaz (örn. `A1`):

```
docs/UNINSTALLER_V2_PLAN.md dosyasını baştan sona oku. Özellikle "0. Uygulayıcı için kurallar"
bölümüne uy. Şimdi yalnızca <GÖREV> görevini uygula:
- Görevde listelenen dosyalar dışına dokunma.
- Görevdeki testleri yaz ve çalıştır; dotnet build, dotnet test, verify-tokens.py ve verify-symbols.py geçmeli.
- Kabul kriterlerini tek tek kontrol et ve sonunda her maddeyi "sağlandı / sağlanmadı (neden)" diye raporla.
- Belirsiz bir nokta varsa tahmin etme; TODO(karar) bırak ve raporda belirt.
- Tek commit at: "fix(uninstaller): <GÖREV> - <kısa açıklama>".
```

Her görevden sonra diff'i gözden geçir. Özellikle **"KESİN YASAKLAR"** maddelerini `grep` ile kontrol et:
```
grep -rn "Directory.Delete\|File.Delete\|DeleteSubKeyTree\|\.Kill(" --include=*.cs Services ViewModels Views Helpers
grep -rn "catch *{ *}" --include=*.cs Services/Safety Services/Uninstall Helpers/PathSafetyGuard.cs Helpers/RegistryPath.cs
```
