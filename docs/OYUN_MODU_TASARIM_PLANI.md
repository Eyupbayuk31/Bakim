# Oyun Modu — Tasarım Yenileme Planı (v4.2 hedefi)

> Kapsam: `Views/Modules/GameModeModuleView.xaml` sayfasının baştan tasarlanması, bu sayfada
> görünen **uygulama geneli** tasarım hatalarının (kenar çubuğu, başlık bileşeni, dönüştürücüler)
> düzeltilmesi ve ortaya çıkan yeni bileşenlerin diğer sayfalara yayılması.
>
> Dayanak: v4.1.1 ekran görüntüsü (1896×1010, MicaDark) + kod incelemesi.
> Kurallar: MASTER_PLAN §3 (token'lar, `verify-design-debt.py` cırcırı), §310 (abartılı dil yasak:
> arayüzde "ultra", "mega", "%100" yok).

---

## 0. Teşhis — sayfa neden "kötü" görünüyor?

### 0.1 Uygulama geneli bir HATA (en önemli bulgu)

`Converters/ValueConverters.cs:199` — `StringToVisConverter` yalnızca `ConverterParameter`
verildiğinde çalışıyor (eşitlik karşılaştırması). Parametresiz kullanımda **her zaman
`Collapsed`** döndürüyor. Oysa 14 yerde "dize boş değilse göster" anlamında parametresiz kullanılıyor:

| Yer | Sonuç |
|---|---|
| `Controls/SectionHeader.xaml:28, :61` | **66 sayfa/bölüm başlığının simgesi ve açıklaması hiç görünmüyor.** Ekran görüntüsünde "Oyun Modu" başlığının altında açıklama ve simge olmamasının nedeni bu. |
| `Controls/StatCard.xaml:109, :142, :204` | KPI kartlarında simge, trend rozeti ve alt yazı gizli |
| `Controls/MetricChip.xaml:52, :78, :87` | Çiplerin simgesi, etiketi **ve değeri** gizli |
| `Controls/StatusBadge.xaml:53, :78` | Rozette ne simge ne nokta görünüyor (WCAG 1.4.1 için eklenen biçim ipucu kayıp; `Inverse` de `Collapsed` döndürüyor) |
| `Controls/EmptyState.xaml:44` | Boş durum açıklamaları gizli |
| `MainWindow.xaml:778` | Komut paletinde kısayol hapları gizli |
| `StorageModuleView.xaml:411`, `ThreatAnalysisDialog.xaml:1094` | İlerleme metni / ImpHash satırı gizli |

Bu tek hata, tüm uygulamayı "boş ve yarım" gösteriyor. **Faz 0'ın ilk işi.**

### 0.2 Oyun Modu sayfasının kendi sorunları

| # | Sorun | Kanıt |
|---|---|---|
| S1 | **Hiyerarşi yok.** Durum kartı, ayar kartı ve bilgi kartı aynı ağırlıkta; göz nereye bakacağını bilmiyor. | Üç kart da `CardBackgroundFillColorDefaultBrush` + 1px çerçeve + `Pad.Card` |
| S2 | **Açık/Kapalı durumu görsel olarak neredeyse aynı.** Tek fark simgenin `Filled` olması ve metin. Renk, çerçeve, rozet, süre yok. | `GameModeModuleView.xaml:34` |
| S3 | **Satırlar çok uzun.** 1500 px'lik içerikte etiket solda, anahtar en sağda; göz 1400 px yol alıyor. `MaxWidth` yok. | Ekran: "Çalışma kümelerini kırp" ↔ anahtar arası ~1430 px |
| S4 | **Tekrar eden içerik.** "Çalışma kümelerini kırp" hem Profil'de hem "Açıldığında"da; "kapatınca önceki haline döner" 4 kez yazıyor. | Durum metni, güç planı açıklaması, "Açıldığında" 1. ve 3. madde |
| S5 | **"Açıldığında" kartı yalan söyleyebilir.** Statik metin; `TrimMemory` kapalıyken de "Çalışma kümeleri kırpılır" diyor, güç planı "Değiştirme" iken de "Seçili güç planı" diyor, askıya alınacak uygulamalardan hiç bahsetmiyor. | `GameModeModuleView.xaml:137-185` |
| S6 | **Virgüllü metin kutusu** ile liste düzenleme: yazım hatasına açık, öğe silmek zor, "hangi süreç adı?" belirsiz. Önizleme 12 px üçüncül renkte, görünmüyor. | `:107-111`, `:127-133` |
| S7 | **Bölüm etiketleri silik.** "Profil", "Açıldığında" 11 px Medium üçüncül gri; bölüm başlığı gibi değil dipnot gibi. | `Text.Label` |
| S8 | **Ayraçlar tutarsız.** Profil kartında bir yerde `Border Height=1 Margin=0,14`, diğer satırlar arasında ayraç yok. | `:113` |
| S9 | **Birincil düğme her iki durumda aynı.** "Aç" da "Kapat" da aynı dolu vurgu düğmesi; kısayol yalnızca araç ipucunda. | `:47-53` |
| S10 | **"Son oturumlar" ekranın altında kayboluyor**; 1010 px yükseklikte hiç görünmüyor. Oturum satırı tarih + özet; süre, serbest bırakılan bellek, askıya alınan uygulama sayısı yok. | `:189-215` |
| S11 | **Güç planı açılır kutusu** "Nihai Performans (yoksa Yüksek Performans)" gibi uzun metin taşıyor; seçeneklerin açıklaması yok. | `GameModeViewModel.cs:66` |
| S12 | **Otomatik başlatma kapalıyken** oyun listesi sadece `IsEnabled=false`; bağlam (neden gri?) yok. | `:130` |

### 0.3 Kabuk (kenar çubuğu / başlık çubuğu) sorunları — her sayfada görünür

| # | Sorun | Kanıt |
|---|---|---|
| K1 | **Kenar çubuğu "kart yığını" gibi.** Seçili olmayan her öğe `Secondary` düğme (dolu gri blok + çerçeve). 20 gri blok üst üste = görsel gürültü. Fluent'te seçili olmayan öğe şeffaftır. | `BoolToNavAppearanceConverter` → `Secondary` (`ValueConverters.cs:250`) |
| K2 | **Seçili öğe aşırı parlak.** Tam dolu açık mavi zemin + beyaz metin, düşük kontrast; ayrıca sol vurgu çubuğu ile aynı renkte olduğu için çubuk kayboluyor. | `Primary` görünümü |
| K3 | **"GENEL BAKIŞ" grup başlığı** marka bloğunun altına sıkışıp üstten kırpılıyor. | Ekran; `MainWindow.xaml:296` `Margin="0,0,0,14"` + grup başlığı `Margin="10,10,0,4"` |
| K4 | **"Depolama" öğesinin simgesi bozuk** (yerine "˅" benzeri glif, metin sola kaymış). | `Navigation.cs:71` `HardDrive24` — `verify-symbols.py` ile doğrulanmalı |
| K5 | Kaydırma alanının altında **yarım kesilmiş öğe** (Kaldırıcı'nın altı) ipucu vermeden kesiliyor; alt kenarda solma (fade) yok. | Ekran |
| K6 | **Başlık çubuğunda 5 farklı hap**, 3 farklı stil (Secondary düğme, Card border, Primary/Secondary değişen düğme). "Oyun Modu" hapı açıkken `Primary`, kapalıyken `Secondary`: sayfadaki durumla dil birliği yok. | `MainWindow.xaml:111-236` |
| K7 | Sayfa başlığı 28 px Bold, ama altındaki durum kartı başlığı 20 px — iki başlık yarışıyor; sayfa başlığı ile ilk kart arası 18 px, kartlar arası 12 px, etiket-kart arası 8 px: ritim yok. | `Spacing.xaml` |

---

## 1. Tasarım ilkeleri (bu planın pusulası)

1. **Tek bakışta durum.** Sayfaya giren kullanıcı 1 saniyede "açık mı, kapalı mı, ne yapıyor" görmeli.
2. **Ne ayarlıyorsan onu gör.** "Açınca ne olacak" listesi profilden **canlı** üretilir; yalan söylemez.
3. **Bir şeyi bir kez söyle.** Her açıklama tek yerde; tekrarlar silinir.
4. **Okunabilir satır uzunluğu.** İçerik en fazla ~1180 px, iki sütun; geniş ekranda ortalanır.
5. **Fluent 2 ayar kalıbı.** Simge + başlık + açıklama + sağda denetim; ayraçlarla gruplu satırlar.
6. **Dürüst dil.** Ölçülmeyen fayda vaat edilmez ("FPS artar" yok); ölçülen sonuç gösterilir.
7. **Sistem önce.** Her yeni görsel parça önce `Controls/` veya `Themes/Tokens/` altında bileşen/token olur, sonra sayfada kullanılır.

---

## 2. Hedef düzen

### 2.1 Geniş ekran (≥ 1280 px içerik)

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ 🎮 Oyun Modu                                                                           │
│ Oyun sırasında güç planını yükseltir, arka plan yükünü azaltır; kapatınca geri alır.   │
├──────────────────────────────────────────────────────────────────────────────────────┤
│ ╔══════════════════════════════════════════════════════════════════════════════════╗ │
│ ║ ┌──────┐  ● KAPALI                                            ┌──────────────────┐ ║ │
│ ║ │  🎮  │  Oyun Modu hazır                                     │ ▶ Oyun Modunu    │ ║ │
│ ║ │ 64px │  4 adım uygulanacak · kapatınca hepsi geri alınır    │   başlat         │ ║ │
│ ║ └──────┘                                                      └──────────────────┘ ║ │
│ ║                                                                  Ctrl + Shift + G  ║ │
│ ╚══════════════════════════════════════════════════════════════════════════════════╝ │
│                                                                                        │
│  ┌─ AYARLAR (sol, ~62%) ──────────────────────────┐  ┌─ AÇINCA NE OLACAK (sağ) ──────┐ │
│  │ PERFORMANS                                      │  │ ✓ Güç planı → Yüksek Perf.    │ │
│  │ ⚡ Güç planı              [Yüksek Performans ▾] │  │ ✓ Bakım arka plan işleri dur. │ │
│  │    Oyun süresince kullanılacak plan             │  │ ✓ Bellek kırpılır             │ │
│  │ ───────────────────────────────────────────────│  │ ✓ 2 uygulama askıya alınır    │ │
│  │ 🧠 Arka plan belleğini kırp              [ ●] │  │   OneDrive · Teams             │ │
│  │    Kullanılmayan belleği Windows'a verir       │  │ – Otomatik başlatma kapalı     │ │
│  │                                                 │  │ ↺ Kapatınca hepsi geri alınır │ │
│  │ ARKA PLAN UYGULAMALARI                          │  └───────────────────────────────┘ │
│  │ ⏸ Askıya alınacaklar                            │  ┌─ SON OTURUMLAR ───────────────┐ │
│  │  [OneDrive ×] [Teams ×] [+ Ekle…] [Çalışanlardan seç] │ Bugün 21:14 · 1 sa 42 dk  ✓   │ │
│  │                                                 │  │  1,2 GB bellek · 2 uygulama    │ │
│  │ OTOMATİK BAŞLATMA                               │  │ Dün 23:02 · 38 dk        ✓     │ │
│  │ ▶ Oyun açılınca otomatik başlat          [ ●] │  │ …                 Tümünü gör → │ │
│  │  [cs2 ×] [valorant ×] [eldenring ×] [+ Ekle…]  │  └───────────────────────────────┘ │
│  └─────────────────────────────────────────────────┘                                    │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

### 2.2 Açık durum (hero kartı dönüşür)

```
╔═══════════════════════════════════════════════════════════════════════════════════════╗
║ ┌──────┐  ● AÇIK · 00:42:13                                         ┌────────────────┐ ║
║ │ 🎮 ◌ │  Oyun Modu çalışıyor                                        │ ■ Oyun Modunu  │ ║
║ │ nabız│  cs2 algılandı · otomatik açıldı                            │   kapat        │ ║
║ └──────┘                                                            └────────────────┘ ║
║ ┌───────────────┐ ┌───────────────┐ ┌───────────────┐ ┌───────────────┐               ║
║ │ Güç planı     │ │ Serbest bellek│ │ Askıda        │ │ Bakım işleri  │               ║
║ │ Yüksek Perf.  │ │ 1,2 GB        │ │ 2 uygulama    │ │ Duraklatıldı  │               ║
║ └───────────────┘ └───────────────┘ └───────────────┘ └───────────────┘               ║
╚═══════════════════════════════════════════════════════════════════════════════════════╝
```

- Kart zemini: `Status.Success.Subtle` üzerine 1 px `Status.Success.Border`; sol üstte yumuşak vurgu parıltısı (`Brush.Glow.Soft`, yalnızca açıkken).
- Simge kutusu: 64×64, `Radius.LG`, açıkken yavaş nabız halkası (2,4 sn, `Ease.Gentle`; "Animasyonları azalt" açıksa sabit halka).
- Durum hapı: nokta + büyük harf etiket + canlı süre (`mm:ss` → `sa dk`), rakamlar `Tabular`.
- Düğme: kapalıyken `Primary` "Oyun Modunu başlat" (Play24), açıkken `Secondary` "Oyun Modunu kapat" (Stop24). Kapatmak yıkıcı değil, bu yüzden kırmızı değil.
- Açıkken ayarlar kartı üstünde bilgi şeridi: "Değişiklikler bir sonraki açılışta uygulanır."

### 2.3 Dar ekran (< 1100 px içerik, pencere MinWidth 1020)

- İki sütun → tek sütun: Hero → "Açınca ne olacak" → Ayarlar → Son oturumlar.
- Hero'daki 4 metrik `UniformGrid` 4 → 2 sütun.
- Uygulama: `SizeChanged` ile `IsCompact` görsel durumu (VisualStateManager) ya da `Grid` sütun genişliklerini tetikleyici ile değiştiren küçük bir `ResponsiveColumns` yardımcı sınıfı (`Helpers/`).

---

## 3. Fazlar

### Faz 0 — Uygulama geneli hata düzeltmeleri (önce bu; tek başına büyük görsel kazanç)

| İş | Dosya | Ayrıntı |
|---|---|---|
| 0.1 `StringToVis` düzelt | `Converters/ValueConverters.cs:199` | Parametre yoksa: `!string.IsNullOrWhiteSpace(s)` → Visible; `Invert` her iki modda da uygulanır; `value` dize değilse (null) boş sayılır. Eşitlik modu (`ConverterParameter`) aynen kalır — `ServiceManagerModuleView` bozulmaz. |
| 0.2 Birim testi | `Tests/` | 6 durum: boş/dolu × param yok/var × Invert. |
| 0.3 Görsel regresyon turu | 20 modül | Açıklamaları görünür olan başlıklar artık 1-2 satır ekliyor; taşan/çok uzun açıklamaları kısalt (özellikle sayfa başlıklarında tek cümle kuralı). |
| 0.4 Kenar çubuğu görünümü | `ValueConverters.cs:241`, `MainWindow.xaml:348-402` | Seçili değil → `Transparent`; seçili → `Transparent` + `Surface.Selected` zemin + sol vurgu çubuğu + SemiBold metin. Hover: `Surface.Hover`. Yani özel bir `NavItemButton` stili (`Themes/Controls/NavItem.xaml`), `BoolToNavAppearance` kaldırılır. |
| 0.5 Grup başlığı kırpılması | `MainWindow.xaml:296, :336` | Marka bloğu alt boşluğu 14 → 8; ilk grup başlığının üst boşluğu 4; başlık 11 px SemiBold, harf aralığı +0.4 (Typography: `Text.NavGroup` stili). |
| 0.6 Depolama simgesi | `ViewModels/Navigation.cs:71` | `python Tools/verify-symbols.py` ile doğrula; font'ta yoksa `Storage24` / `HardDrive20`'ye geç. `verify-symbols.py` CI'ya bağlanır. |
| 0.7 Kaydırma kenar solması | `MainWindow.xaml:327` | Nav `ScrollViewer`'a alt/üst `OpacityMask` (LinearGradient) — kesik öğe "devamı var" gibi okunur. |
| 0.8 Başlık çubuğu hap dili | `MainWindow.xaml:111-236` | Tüm haplar tek stil: `TitleBarPill` (Height 32, `Radius.Pill`, `Surface.Raised`, `Border.Subtle`). Oyun Modu hapı: kapalıyken nötr, açıkken yeşil nokta + "Oyun Modu · 42 dk" — renk değil nokta+metin ile durum. |

**Kabul:** Oyun Modu başlığının altında açıklama ve simge görünüyor; kenar çubuğunda seçili olmayan öğelerin zemini yok; hiçbir sayfada boş rozet/boş çip kalmadı.

---

### Faz 1 — Tasarım sistemi eklemeleri (yeniden kullanılabilir parçalar)

| Bileşen / token | Yer | Amaç |
|---|---|---|
| `Style Card.Surface` (Border) | `Themes/Tokens/Surfaces.xaml` (yeni) | Sayfalarda 6 satırlık tekrar eden `Background/BorderBrush/Thickness/CornerRadius/Padding` bloğunu tek stile indirir. `Card.Hero`, `Card.Hero.Active`, `Card.Inset` türevleri. |
| `c:SettingRow` | `Controls/SettingRow.xaml` (yeni) | Fluent ayar satırı: `Icon`, `Header`, `Description`, sağda `Content` (denetim), altta isteğe bağlı `Footer` (genişletilmiş içerik). `ShowDivider`. MinHeight 64, `Pad.Row` 16,12. Açıklama `MaxWidth=560` → satır uzunluğu sorunu (S3) biter. |
| `c:SettingGroup` | `Controls/SettingGroup.xaml` (yeni) | Başlıklı `Card.Surface` + `ItemsControl`; satırlar arasında otomatik ayraç (S8). |
| `Text.SectionLabel` | `Typography.xaml` | 12 px SemiBold, `Text.Secondary`, harf aralığı +0.3, üst boşluk 24 / alt 8 (S7). `Text.Label` yalnızca form etiketi kalır. |
| `c:ChipListEditor` | `Controls/ChipListEditor.xaml` (yeni) | Öğeler kaldırılabilir çip (`×`), sonda "+ Ekle" giriş kutusu (Enter/virgül ile ekler, Backspace ile son çipi siler), yinelenen ve geçersiz adı reddeder, `.exe` uzantısını otomatik temizler. `ItemsSource` = `ObservableCollection<string>`. Erişilebilirlik: her çip `AutomationProperties.Name="{0} öğesini kaldır"`. (S6) |
| `c:KeyCap` | `Controls/KeyCap.xaml` (yeni) | "Ctrl" "Shift" "G" tuş kapakları; başlık çubuğu arama hapı ve komut paleti de bunu kullanır (S9). |
| `c:StatusPill` genişletme | `Controls/` | Nokta + etiket + isteğe bağlı canlı alt metin (süre). MASTER_PLAN'daki `StatusPill` varsa genişletilir, yoksa `StatusBadge`'e `Size=Large` eklenir. |
| `c:StepList` | `Controls/StepList.xaml` (yeni) | Durumlu adım listesi: `Pending` (boş halka), `WillApply` (✓ vurgu), `Skipped` (– üçüncül, üstü çizili değil), `Applied` (✓ yeşil), `Failed` (! kırmızı). Hem "Açınca ne olacak" hem açık-durum metrikleri için. |
| `c:PageContainer` / `Layout.Page` stili | `Themes/Tokens/Spacing.xaml` | `MaxWidth=1180`, `HorizontalAlignment=Center`, `Pad.Module 32,24`. Tüm modüller kademeli geçer; ilk kullanıcı Oyun Modu. |
| Boşluk ritmi | `Spacing.xaml` | `Gap.Section` (32), `Gap.Block` (16), `Gap.Inline` (8): başlık→ilk blok 24, bloklar arası 16, bölüm arası 32 (K7). |
| Motion | `Motion.xaml` | `Motion.Pulse` (2.4 sn), `Motion.StateChange` (0.25 sn renk/zemin geçişi); `MotionPolicy` ile sıfırlanır. |

Her bileşen için: tasarım zamanı örneği (`d:`), açık/koyu/Yüksek Kontrast kontrolü, `AutomationProperties`. `verify-design-debt.py` sayaçları artmamalı (sabit renk/FontSize/CornerRadius yok).

---

### Faz 2 — Oyun Modu sayfasının yeniden yazımı

#### 2.1 XAML (`Views/Modules/GameModeModuleView.xaml`)

1. Kök: `ScrollViewer` → `PageContainer` → `StackPanel`.
2. `SectionHeader` (açıklama kısaltılır, bkz. Faz 4).
3. **Hero kartı** (`Card.Hero` / `Card.Hero.Active`, `DataTrigger IsActive`):
   - Sol: 64 px simge kutusu (`Radius.LG`, `Brush.Accent.Subtle` / `Status.Success.Subtle`), açıkken nabız halkası.
   - Orta: `StatusPill` (KAPALI / AÇIK · süre), başlık (`Text.SectionTitle` 18 → hero için 22 SemiBold `Font.HeroTitle` yeni token), tek satır alt metin.
   - Sağ: büyük düğme (MinWidth 200, Height 44) + altında `KeyCap` kısayol; meşgulken düğme içinde `ProgressRing` ve metin "Açılıyor…/Kapatılıyor…", düğme devre dışı (şu an halka düğmenin yanında, düğme tıklanabilir kalıyor).
   - Alt (yalnızca açıkken, `Expander` benzeri yumuşak açılma): 4 metrik kutusu (`StatCard` compact).
4. **İki sütunlu gövde** (`Grid` 62* / 38*, 16 px ara; dar modda tek sütun):
   - Sol: üç `SettingGroup` — **Performans** (Güç planı, Bellek kırpma), **Arka plan uygulamaları** (ChipListEditor + "Çalışanlardan seç"), **Otomatik başlatma** (anahtar + ChipListEditor; kapalıyken liste gizlenmez, üstünde "Anahtarı açınca bu oyunlar izlenir" satırı ile soluk).
   - Sağ: **"Açınca ne olacak"** `StepList` (canlı, profilden) + altında "Kapatınca: önceki güç planı geri yüklenir, askıdaki uygulamalar devam eder." tek satırı. Altında **Son oturumlar** (en fazla 5, "Tümünü Etkinlik Merkezi'nde gör →" bağlantısı; boşsa `EmptyState` küçük boy).
5. `InfoBar "Son işlem"` kaldırılır: sonuç, hero'nun alt metnine ve oturum listesinin ilk satırına taşınır (tekrarı önler). Hata olursa hero içinde `Severity=Error` şerit.

#### 2.2 ViewModel (`ViewModels/GameModeViewModel.cs`)

| Yeni üye | Açıklama |
|---|---|
| `ObservableCollection<string> SuspendAppItems`, `AutoStartGameItems` | ChipListEditor kaynakları. Değişince mevcut `SuspendApps` / `AutoStartExes` dizelerine yazılır → **ayar dosyası biçimi değişmez**, göç gerekmez. |
| `ObservableCollection<GameModeStep> Steps` | `PowerPlan`, `TrimMemory`, `SuspendAppItems`, `AutoStart` değişince yeniden üretilir (S5). `GameModeStep { Icon, Title, Detail, StepState State }`. |
| `ElapsedText`, `StartedAt` | `DispatcherTimer` (1 sn; sayfa `OnDeactivatedAsync`'te durur — arka planda boşuna çalışmaz). |
| `FreedMemoryText`, `SuspendedCountText`, `PowerPlanActiveText` | Açık durum metrikleri. |
| `TriggerText` | "Elle açıldı" / "cs2 algılandı · otomatik açıldı". |
| `StatusCaption` | KAPALI / AÇIK. |
| `BusyText` | "Açılıyor…" / "Kapatılıyor…". |
| `PickRunningAppCommand` | Çalışan kullanıcı süreçlerinden seçim diyaloğu (`Views/Dialogs/ProcessPickerDialog.xaml`, yeni; kritik süreçler listelenmez — `GameModeService`'in koruma listesi yeniden kullanılır). |
| `OpenActivityCenterCommand` | "Tümünü gör" → `ActivityCenter` filtre `GameModeSession`. |
| `PowerPlanOptions` | `KeyValuePair` yerine `PowerPlanOption { Key, Title, Description }`; açılır kutuda iki satırlı şablon: "Nihai Performans" + "Yoksa Yüksek Performans kullanılır" (S11). |

#### 2.3 Servis (`Services/IGameModeService.cs`, `GameModeService.cs`)

Şu an yalnızca `LastActionSummary` (düz dize) ve `Task<long>` (serbest bırakılan bayt) var. Görsel için yapılandırılmış veri gerekli:

```csharp
public sealed record GameModeSessionInfo(
    DateTime StartedAt,
    string Trigger,                  // "manual" | "auto:<exe>"
    string? PowerPlanName,           // uygulanan plan, "Değiştirme" ise null
    long FreedBytes,
    IReadOnlyList<string> SuspendedApps,
    IReadOnlyList<string> Warnings); // askıya alınamayanlar vb.

GameModeSessionInfo? CurrentSession { get; }
```

- `LastActionSummary` geriye uyumluluk için kalır (tepsi, Kontrol Paneli kullanıyor).
- Etkinlik Merkezi kaydına süre + bayt + uygulama sayısı alanları eklenir (oturum satırı zenginleşir, S10).
- **Davranış değişmez**; yalnızca zaten hesaplanan veriler dışarı verilir.

---

### Faz 3 — Canlılık ve mikro etkileşimler

1. Kapalı → Açık geçişi: hero zemini/çerçevesi `Motion.StateChange` ile renk geçişi; metrik satırı yukarıdan 8 px kayarak belirir; adım listesindeki ✓'ler sırayla (40 ms arayla) `Applied` olur — **servis gerçekten adımı uyguladıkça** (servis olayı: `StepCompleted`), sahte animasyon değil.
2. Nabız halkası yalnızca açıkken; "Animasyonları azalt" açıksa kapalı.
3. Çip ekleme/silme: 120 ms ölçek+opaklık.
4. Kısayol ile (Ctrl+Shift+G) başka sayfadayken açılınca: sağ alt toast "Oyun Modu açıldı · Ctrl+Shift+G ile kapat" (mevcut toast altyapısı).
5. Tepsi menüsü (`TrayFlyoutWindow.xaml`) ve Kontrol Paneli kartı aynı `StatusPill` + süreyi kullanır — üç yerde tek görsel dil.

---

### Faz 4 — Metin (mikro kopya) sadeleştirme

| Yer | Şimdi | Öneri |
|---|---|---|
| Sayfa açıklaması | "Oyun oturumu boyunca performans güç planını açar ve Bakım'ın arka plan işlerini duraklatır. Kapatınca her şey önceki haline döner." | "Oyun sırasında güç planını yükseltir ve arka plan yükünü azaltır. Kapatınca her şey geri alınır." |
| Hero başlık (kapalı) | "Oyun Modu kapalı" | Hap: **KAPALI** · Başlık: "Oyun Modu hazır" |
| Hero alt metin (kapalı) | "Açtığınızda aşağıdaki adımlar uygulanır; kapatınca her şey önceki haline döner." | "{n} adım uygulanacak · kapatınca geri alınır" (n canlı) |
| Hero alt metin (açık) | "Kapatınca önceki güç planı birebir geri yüklenir…" | "Elle açıldı" / "{oyun} algılandı · otomatik açıldı" |
| Düğme | "Oyun Modunu aç" / "kapat" | "Oyun Modunu başlat" / "Oyun Modunu kapat" |
| Güç planı açıklaması | "Kapatınca her zaman önceki plan geri yüklenir." | "Oyun süresince kullanılacak plan." (geri alma sağ panelde bir kez söyleniyor) |
| Bellek | "Çalışma kümelerini kırp" + 2 ayrı açıklama | "Arka plan belleğini boşalt" · "Arka plandaki uygulamaların kullanmadığı belleği Windows'a geri verir. Etkisi geçicidir." |
| Askıya alma | 3 cümlelik açıklama | "Oyun süresince duraklatılacak uygulamalar. Oyun Modu kapanınca devam ederler; Windows'un kritik süreçleri listeye eklenemez." + çökme kurtarma bilgisi araç ipucuna. |
| Otomatik | "Listedeki bir oyun çalışınca Oyun Modu açılır; hepsi kapanınca kapanır. Elle açtıysanız otomatik kapatılmaz." | "Listedeki bir oyun açılınca başlar, hepsi kapanınca durur." + "Elle başlattıysanız otomatik kapanmaz." ikinci satır üçüncül |
| Boş oturum | "Henüz kayıtlı oturum yok. Oyun Modunu kapattığınızda…" | EmptyState: "Henüz oturum yok" · "İlk oturumdan sonra süre ve sonuç burada görünür." |

---

### Faz 5 — İşlevsel zenginleştirme (tasarımı tamamlayan, MASTER_PLAN §5.5 ile uyumlu)

1. **Oyun kütüphanesi bulucu:** Steam (`libraryfolders.vdf` + `appmanifest_*.acf`), Epic (`%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item`), Xbox/MS Store (`GamingServices` paketleri) → "Kütüphaneden ekle" diyaloğu, kapak yerine exe simgesi. `Services/GameLibraryService.cs` (yeni, salt okuma).
2. **Çalışanlardan seç** diyaloğu (Faz 2'de tanımlı) — arama kutusu, simge, süreç adı, bellek kullanımı.
3. **Önerilen askıya alma adayları:** o an çalışan ve bilinen arka plan uygulamaları (OneDrive, Teams, Dropbox, Adobe güncelleyicileri…) çip önerisi olarak "+ OneDrive" biçiminde soluk görünür; tıklayınca eklenir. Liste JSON'da, sabit kod değil.
4. **Odak Yardımı / Rahatsız Etmeyin** adımı (MASTER_PLAN §5.5 madde 1) — ayrı `SettingRow` ve `StepList` adımı.
5. **Oturum raporu:** ortalama CPU/GPU yükü (TelemetryHub), Etkinlik Merkezi'nde ayrıntı paneli.

> Faz 5 tasarımdan bağımsız sürümlenebilir; Faz 0-4 bunlar olmadan tamamlanmış sayılır.

---

### Faz 6 — Diğer sayfalara yaygınlaştırma

| Hedef | Kullanılacak bileşen |
|---|---|
| Ayarlar sayfası | `SettingGroup` + `SettingRow` (en büyük kazanç: şu an serbest Grid'ler) |
| Kurulum Nöbetçisi, Ağ İzleyici | Hero kartı kalıbı (durum + tek eylem) |
| Kontrol Paneli Oyun Modu kartı | `StatusPill` + süre + "Sayfaya git" |
| Tüm modüller | `PageContainer` (MaxWidth), `Text.SectionLabel`, `Card.Surface` stili → XAML satır sayısında ~%15 azalma beklenir |
| Başlık çubuğu, komut paleti | `KeyCap` |

Her geçiş ayrı küçük commit; `verify-design-debt.py --update` ile eşik düşürülür.

---

### Faz 7 — Kalite kapısı

- [ ] `python Tools/verify-design-debt.py` — yeni borç yok, eşik düştü.
- [ ] `python Tools/verify-tokens.py`, `verify-symbols.py` temiz.
- [ ] `dotnet build` + `dotnet test` (yeni `StringToVis`, `ChipListEditor` ayrıştırma, `Steps` üretimi testleri).
- [ ] Temalar: MicaDark, Slate Dark, açık tema, Yüksek Kontrast — ekran görüntüleri `docs/screens/gamemode/` altına.
- [ ] Pencere genişlikleri: 1020 (MinWidth), 1280, 1920; ölçek %100/%125/%150.
- [ ] Klavye: Tab sırası hero düğmesi → ayarlar → sağ panel; çip silme Delete/Backspace; odak halkası görünür.
- [ ] Ekran okuyucu (Narrator): hero durumu "Oyun Modu açık, 42 dakikadır" okunur; adım listesi durumları okunur.
- [ ] Kontrast: tüm metin ≥ 4.5:1, büyük metin ve simge ≥ 3:1 (özellikle seçili nav öğesi ve düğme metni).
- [ ] "Animasyonları azalt" açıkken hiçbir sürekli animasyon yok.
- [ ] Davranış regresyonu: MASTER_PLAN §1121-1127 Oyun Modu test adımları (güç planı birebir geri geliyor, çökme sonrası onarım).

---

## 4. Sıra, boyut ve dağıtım

| Faz | Boyut | Bağımlılık | Önerilen sürüm |
|---|---|---|---|
| 0 — Genel hatalar | S (½ gün) | — | **v4.1.2** (hemen, yama) |
| 1 — Bileşenler | M (1-2 gün) | 0 | v4.2.0 |
| 2 — Oyun Modu sayfası | M (1-2 gün) | 1 | v4.2.0 |
| 3 — Canlılık | S (½-1 gün) | 2 | v4.2.0 |
| 4 — Metin | XS | 2 ile birlikte | v4.2.0 |
| 5 — İşlevsel | L (2-3 gün) | 2 | v4.3.0 |
| 6 — Yaygınlaştırma | L (kademeli) | 1 | v4.2.x → v4.3 |
| 7 — Kalite | her fazın sonunda | — | — |

**Dokunulacak dosyalar (Faz 0-4):**
`Converters/ValueConverters.cs`, `MainWindow.xaml`, `ViewModels/Navigation.cs`,
`Themes/Tokens/{Typography,Spacing,Motion}.xaml`, `Themes/Tokens/Surfaces.xaml` (yeni),
`Themes/Controls/NavItem.xaml` (yeni), `App.xaml`,
`Controls/{SettingRow,SettingGroup,ChipListEditor,KeyCap,StepList}.xaml(.cs)` (yeni),
`Controls/StatusBadge.xaml`, `Views/Modules/GameModeModuleView.xaml`,
`ViewModels/GameModeViewModel.cs`, `Services/{IGameModeService,GameModeService}.cs`,
`Views/Windows/TrayFlyoutWindow.xaml`, `Views/Modules/DashboardModuleView.xaml`, `Tests/`.

## 5. Başarı ölçütü (önce / sonra)

| Ölçüt | Şimdi | Hedef |
|---|---|---|
| Durumu anlama süresi | Metni okumak gerekiyor | Renk + hap + süre, < 1 sn |
| Etiket ↔ denetim mesafesi (1920 px) | ~1430 px | ≤ 700 px |
| Tekrarlanan açıklama | 4× "geri döner", 2× "kırpılır" | Her bilgi 1× |
| "Açınca ne olacak" doğruluğu | Statik, profille çelişebilir | Profilden canlı |
| Liste düzenleme | Virgüllü metin | Çip + öneri + seçici |
| Görünmeyen başlık açıklaması / rozet noktası | 14 bağlamada gizli | 0 |
| Kenar çubuğu dolu blok sayısı | 20 | 1 (seçili öğe, soluk zemin) |
