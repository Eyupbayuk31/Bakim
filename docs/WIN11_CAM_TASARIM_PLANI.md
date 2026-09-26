# Bakım — Windows 11 Cam Tasarım Planı

> Hedef: Uygulama, Windows 11'in kendi uygulamaları (Ayarlar, Dosya Gezgini, Microsoft Store) gibi görünmeli.
> Kenar çubuğu, başlık çubuğu ve içerik **tek bir cam yüzeyin katmanları** olmalı.
> Üstüne hafif bir glassmorphism katmanı gelir: yarı saydam kartlar, ışıklı cam kenarı, arkada yumuşak bir ortam ışığı.
> Bu plan `docs/FLUENT_DUZEN_PLANI.md` planının devamıdır. Oradaki yerleşim sistemi (ModulePage, AdaptiveGrid, ListCard…) aynen kalır.
> Bu plan **malzemeyi** değiştirir: renk, saydamlık, derinlik ve ışık.

---

## 0. Ekran görüntüsünden teşhis

| # | Sorun | Neden | Etki |
|---|---|---|---|
| 1 | Sol menü griyken içerik simsiyah: iki ayrı uygulama gibi | Kenar çubuğu saydam, altında sistemin gri **Mica**'sı var. İçerik katmanı ise temanın **opak** rengi (#08080B) | En büyük kopukluk budur |
| 2 | Başlık çubuğu da gri, içerikle uyumsuz | Aynı neden: başlık çubuğu Mica, içerik opak | Üst kenarda keskin renk sınırı oluşuyor |
| 3 | Kartlar düz ve ölü | Opak dolgu ile tek renk gri kenar var; ışık ve derinlik yok | "Web paneli" hissi veriyor, Windows hissi vermiyor |
| 4 | Sağlık kartı diğer kartlarla aynı ağırlıkta | Kahraman kartın farklı bir malzemesi yok | Sayfanın odağı yok |
| 5 | "Tümü", "Süreçler", "Sistem Bilgisi", "Temizle" çerçeveli | Bağlantı düğmesinin kenarlığı görünüyor | Win11'de bunlar çerçevesiz köprü düğmesidir |
| 6 | Grafik altı dolguları sert | Sparkline dolgusu tek renk, kenara yapışık | Kartın alt kenarı kırık görünüyor |
| 7 | Kenar çubuğunda seçili öğe koyu bir blok | Seçim dolgusu opak | Win11'de seçim hafif beyaz bir sistir (%6) |
| 8 | Kaydırma çubuğu içerik katmanının dışına taşıyor | Kaydırıcı kenara yapışık | Sağ kenarda ince bir çizgi görünüyor |
| 9 | Temalar (AMOLED, Neon, Slate) Mica ile çelişiyor | Mica her zaman sistemin grisi; temanın rengini almıyor | Özel temalar "yama" gibi duruyor |

**Kök neden:** Uygulamada bir *malzeme sistemi* yok, yalnız bir *renk paleti* var.
Windows 11'de yüzeyler renk değil **malzemedir** (Mica, Akrilik, katman, kart). Her malzemenin bir saydamlığı, kenar ışığı ve gölgesi vardır.

---

## 1. Windows 11 malzeme modeli (referans)

```
┌──────────────────────────────────────────────────────────────┐
│ MICA (pencere tabanı) — masaüstü duvar kağıdından renk alır   │
│  ├─ Başlık çubuğu ........ saydam, doğrudan Mica              │
│  ├─ Kenar çubuğu ......... saydam, doğrudan Mica              │
│  └─ İÇERİK KATMANI ....... %30 gri örtü, sol üst köşe 8 px    │
│       └─ KART ............ %5 beyaz örtü + cam kenarı         │
│            └─ DENETİM .... %6 beyaz örtü + ışıklı alt kenar   │
│                                                              │
│ AKRİLİK (geçici yüzeyler) — arkasını bulanıklaştırır          │
│  ├─ Açılır menü, sağ tık menüsü, ipucu, komut paleti         │
│  └─ Bildirim (toast)                                         │
│ DUMAN (smoke) — diyalog arkası %30 siyah perde                │
└──────────────────────────────────────────────────────────────┘
```

Kurallar:
1. **Kenar çubuğu ve başlık çubuğu asla opak olmaz.** Tabanın kendisidir.
2. **İçerik katmanı** tabandan bir ton ayrılır. Ayrım renkle değil, yarı saydam bir örtüyle yapılır. Böylece taban ne renkteyse katman da onun tonunu taşır.
3. **Kartlar** katmanın üstünde hafif beyaz bir örtüdür. Kenar ışığı üstte parlak, altta sönüktür.
4. **Geçici yüzeyler** (menü, palet, ipucu) akriliktir: arkası bulanık görünür, gölgesi vardır.
5. **Vurgu rengi** yalnız etkileşim için kullanılır: seçim çizgisi, birincil düğme, ilerleme, odak. Yüzey boyamak için kullanılmaz; tek istisna kahraman kartın hafif tonudur.

---

## 2. Bakım malzeme sistemi (token'lar)

### 2.1 Taban: "tonlu Mica"

Özel temaların Mica ile çelişmemesi için pencere köküne **temanın renginde yarı saydam bir ton** konur. Mica duvar kağıdından biraz renk sızdırır, tema kimliği korunur.

| Tema | Taban rengi | Ton opaklığı (Mica açık) | Mica kapalı |
|---|---|---|---|
| Windows 11 koyu | #202020 | %0 (saf Mica) | #202020 |
| Windows 11 açık | #F3F3F3 | %0 (saf Mica) | #F3F3F3 |
| Slate | #0B1220 | %78 | opak |
| AMOLED | #000000 | %92 | opak |
| Neon mor | #090514 | %80 | opak |
| Yüksek karşıtlık | sistem | — | opak (cam kapalı) |

`Surface.WindowTint` pencere kök `Grid`'inin zeminidir. Kenar çubuğu ve başlık çubuğu **saydam kalır**; tonu kökten alırlar.

### 2.2 Katman ve yüzeyler (örtü mantığı)

Yüzeyler renk yerine **örtü**dür. Koyu temada beyaz, açık temada beyaz ya da siyah alfa kullanılır. Aynı örtü her tabanın üstünde doğru tonu verir; ayrı ayrı renk ayarlamak gerekmez.

| Token | Koyu tema | Açık tema | Kullanım |
|---|---|---|---|
| `Layer.Fill` | #0AFFFFFF + taban tonu | #80FFFFFF | İçerik katmanı |
| `Layer.Stroke` | #14FFFFFF | #0F000000 | Katman sol/üst kenarı |
| `Card.Fill` | #0DFFFFFF | #B3FFFFFF | Standart kart |
| `Card.Fill.Secondary` | #08FFFFFF | #80F6F6F6 | İç içe bölge, liste başlığı |
| `Card.Fill.Hover` | #12FFFFFF | #D9FFFFFF | Tıklanabilir kart üzeri |
| `Card.Stroke.Glass` | üst #24FFFFFF → alt #0AFFFFFF | üst #FFFFFFFF → alt #14000000 | Cam kenarı (degrade) |
| `Card.Shadow` | yok | 0 2 4 #0A000000 | Açık temada hafif taban gölgesi |
| `Control.Fill` | #0FFFFFFF | #B3FFFFFF | Düğme, giriş kutusu |
| `Control.Stroke.Elevation` | üst #18FFFFFF → alt #0AFFFFFF | üst #0F000000 → alt #29000000 | Düğmenin alt kenarı biraz koyu |
| `Subtle.Fill.Hover` | #0FFFFFFF | #09000000 | Liste satırı, nav öğesi üzeri |
| `Subtle.Fill.Selected` | #0FFFFFFF | #06000000 | Seçili nav öğesi |
| `Acrylic.Fill` | #D92C2C2C | #D9FCFCFC | Menü, palet, ipucu |
| `Smoke.Fill` | #4D000000 | #4D000000 | Diyalog perdesi |

Özel temalarda örtü rengi saf beyaz değil, **temanın metin rengi** olur. Örneğin Slate'te #CBD5E1, Neon'da #DDD6FE kullanılır. Böylece kartlar temanın tonunda açılır.

### 2.3 Hafif glassmorphism: ortam ışığı

Cam ancak arkasında bir şey varsa cam gibi görünür. Düz bir katmanın üstündeki yarı saydam kart yalnızca "biraz açık gri" durur. Bu yüzden içerik katmanının arkasına **çok hafif, statik bir ortam ışığı** konur:

- İçerik katmanının sağ üst köşesinde vurgu renginde büyük bir **radyal degrade** olur: yarıçap 900 px, merkez %10, kenar %0 opaklık.
- Sol alt köşede ikinci bir ışık olur: temanın ikincil rengi, %6.
- Açık temada opaklıklar yarıya iner.
- Hesaplanmış bulanıklık **yoktur** (`BlurEffect` pahalıdır). Degrade zaten yumuşaktır; maliyeti sıfıra yakındır.
- Işık kaydırmayla **kaymaz**; katmana sabittir. Kartlar üstünden geçerken cam etkisi görünür.
- Ayarlar'dan açılıp kapanır: "Ortam ışığı" (varsayılan açık).
- Windows'ta **saydamlık efektleri kapalıysa** ışık da kapanır.

### 2.4 Kahraman malzemesi (sayfa başı kartı)

- Dolgu: `Card.Fill` üstüne **vurgu renginde %8 tonlama** eklenir; üst sol köşeden sağ alta sönen bir degrade kullanılır.
- Kenar: `Card.Stroke.Glass`, üst ışık biraz daha güçlü (%30).
- Yarıçap 8, iç boşluk 24.
- Yalnız sayfa başında bir tane olur (Kontrol Paneli sağlık kartı, Çökmeler güvenilirlik kartı, Oyun Modu durum kartı).

### 2.5 Köşe, gölge, çizgi

| Öğe | Yarıçap | Gölge |
|---|---|---|
| Pencere | 8 (DWM) | sistem |
| İçerik katmanı | 8 (yalnız sol üst) | yok |
| Kart | 8 | koyu: yok · açık: 2 px |
| Denetim (düğme, giriş) | 4 | yok |
| Açılır menü, palet | 8 | 0 8 16 #24000000 |
| Diyalog | 8 | 0 32 64 #38000000 |
| Toast | 8 | 0 8 16 #24000000 |

Ayırıcı çizgiler `Divider.Stroke` ile çizilir: koyu temada #0FFFFFFF, açıkta #0F000000. Bugünkü opak gri çizgiler kalkar.

### 2.6 Saydamlık erişilebilirliği

`HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\EnableTransparency = 0` ise uygulama otomatik olarak **opak paletle** çizilir. Bu durumda Mica kapanır, ton opak olur, ortam ışığı kalkar ve akrilik opak dolguya döner. Yüksek karşıtlık teması her zaman opaktır.

---

## 3. Kabuk (MainWindow)

```
┌─────────────────────────────────────────────────────────────────────────┐
│ ▣ Bakım            [ 🔍 Modül veya komut ara   Ctrl K ]    ○ Oyun  — ▢ ✕ │  ← saydam başlık (Mica)
├───────────────┬─────────────────────────────────────────────────────────┤
│ ☰             │╭────────────────────────────────────────────────────────│
│ Genel bakış   ││   ✦ ortam ışığı (sağ üst, çok hafif)                    │
│ ▌▦ Kontrol P. ││  Kontrol Paneli                                         │
│   ⟲ Etkinlik  ││  Sistem durumu, canlı kaynak kullanımı ve hızlı bakım   │
│ Temizlik      ││  ╭─────────── kahraman cam kart ──────────────────────╮│
│   🧹 Temizley. ││  │ (95)  Sistem iyi durumda          [⟳] [Hızlı bakım] ││
│   …           ││  ╰────────────────────────────────────────────────────╯│
│               ││  Canlı durum                                           │
│ saydam, Mica  ││  ╭cam╮ ╭cam╮ ╭cam╮ ╭cam╮                                │
│               ││                                                        │
│ ⚙ Ayarlar     ││           içerik katmanı: Layer.Fill (yarı saydam)      │
└───────────────┴┴────────────────────────────────────────────────────────┘
```

| Parça | Değişiklik |
|---|---|
| Kök | `Surface.WindowTint` (2.1) |
| Başlık çubuğu | Saydam kalır. Arama kutusu `Control.Fill` + ışıklı alt kenar, genişlik 420; Oyun Modu çipi çerçevesiz `Subtle` düğme |
| Kenar çubuğu | Saydam. Seçili öğe `Subtle.Fill.Selected` (opak blok yerine); vurgu çizgisi 3×16 px, yarıçap 1.5, seçim değişince 167 ms kayar. Grup başlıkları 12 px, ikincil metin, üstte 16 px boşluk |
| Kenar çubuğu daraltma | 48 px genişlik, yalnız simgeler, ipucu ile ad; genişlik animasyonu 250 ms |
| İçerik katmanı | `Layer.Fill` + `Layer.Stroke` (yalnız sol ve üst, 1 px) + ortam ışığı. Kaydırma çubuğu katmanın içinde, sağdan 4 px içeride, yalnız üzerine gelince genişler |
| Toast | Akrilik, sağ altta, 8 yarıçap, gölge; girişte alttan 16 px kayar |
| Komut paleti | Ayrı akrilik pencere (gerçek DWM akriliği, §6.2), arkada `Smoke.Fill` |

---

## 4. Bileşen kataloğu (cam sürümleri)

| Bileşen | Malzeme | Ayrıntı |
|---|---|---|
| `Card.Surface` | Card.Fill + Card.Stroke.Glass | Bütün kartların temeli; 48 kart otomatik yenilenir |
| `Card.Surface.Hero` | Kahraman malzemesi (2.4) | Vurgu tonlu cam |
| `Card.Surface.Interactive` | + hover `Card.Fill.Hover`, basınca %98 ölçek | 83 ms geçiş |
| `MetricTile` | Cam kart | Sparkline dolgusu yukarıdan aşağı %24 → %0 degrade, kartın alt köşelerine göre kırpılır (sert kenar kalkar) |
| `ListCard` / `ListRow` | Cam kart, satırlar Subtle | Ayırıcı `Divider.Stroke`, 16 px içeriden başlar (Win11 liste stili) |
| `Link.Button` | **Çerçevesiz** köprü düğmesi | Vurgu metni, üzerine gelince `Subtle.Fill.Hover`, yarıçap 4 |
| Birincil düğme | Vurgu dolgusu + `AccentControlElevationBorder` (alt kenar biraz koyu) | Metin temanın "TextOnAccent" rengi |
| İkincil düğme | `Control.Fill` + `Control.Stroke.Elevation` | Win11 standart düğme |
| Giriş kutusu | `Control.Fill`, odakta alt kenar 2 px vurgu | WPF-UI TextBox şablonu token'larla |
| `SelectorBar` (sekmeler) | Metin sekmeleri, seçilide altta 3×16 vurgu çizgisi | Dolgulu sekme yok |
| `InfoBar` | Durum renginde %8 dolgu + sol simge | Opak kırmızı/sarı bant yerine |
| `StatusGlyph` | Nokta + metin | Hap yok (zaten yapıldı) |
| `SelectionBar` | Akrilik, altta, gölge | Katmanın üstünde yüzer |
| `ToggleSwitch`, `CheckBox`, `ProgressRing` | WPF-UI + vurgu | PublishAccent zaten tek kaynak |
| ComboBox açılır listesi, ContextMenu, ToolTip | **Akrilik** + gölge + 8 yarıçap | WPF-UI stilleri token'larla yeniden bağlanır |
| Diyalog (`Dialog.Shell`) | Mica tabanlı ayrı pencere, arkada Smoke | Başlık 20 SemiBold, altta eylem şeridi `Card.Fill.Secondary` |

---

## 5. Sayfa sayfa uygulama

Hepsi `ModulePage` üzerinde. Değişiklik yalnızca malzemede; yerleşim `FLUENT_DUZEN_PLANI` ile aynı.

| Sayfa | Kahraman | Cam dokunuşları |
|---|---|---|
| Kontrol Paneli | Sağlık (vurgu tonlu) | Metrik kutucuklarda degrade sparkline; bağlantılar çerçevesiz; sorun satırları InfoBar dilinde |
| Etkinlik Merkezi | — | Zaman çizelgesi tek cam kart; gün başlıkları yapışkan, akrilik |
| Temizleyici | Taranabilir alan özeti | Kategori kutucukları etkileşimli cam; seçilide vurgu kenarı |
| Depolama | Disk doluluk halkası | Klasör listesi cam ListCard; sekmeler SelectorBar |
| Kaldırıcı | — | Liste cam kart; seçim çubuğu akrilik |
| Süreçler | CPU/RAM özeti | Tablo başlığı `Card.Fill.Secondary`, satırlar Subtle |
| Başlangıç | Açılış süresi | Win11 "Başlangıç uygulamaları" listesi: satırda anahtar (toggle) |
| Hizmetler | — | Filtre çipleri Subtle; tablo cam |
| Oyun Modu | Durum (vurgu tonlu) | Ayar satırları Win11 SettingsCard |
| Analizör | Tarama durumu | Bulgular ListCard, risk StatusGlyph |
| Kurulum Nöbetçisi | Koruma durumu | Değişiklik listesi cam |
| Ağ İzleyici | Canlı hız | Bağlantı tablosu cam; hız grafiği degrade |
| Sistem Bilgisi | Cihaz kartı (Win11 "Hakkında" gibi) | Bilgi satırları SettingsCard, kopyala düğmesi |
| Olaylar ve çökmeler | Güvenilirlik | Sekmeler SelectorBar, olay listesi cam |
| Windows Ayarları | — | 1000 px, SettingsCard grupları (Win11 Ayarlar birebir) |
| Windows Araçları | — | Araç kutucukları etkileşimli cam ızgara |
| Mağaza | Öne çıkanlar şeridi | Uygulama kutucukları cam; seçim çubuğu akrilik |
| Ayarlar | — | Tema kutucukları + yeni "Malzeme" grubu (§7) |

---

## 6. Teknik yaklaşım ve WPF sınırları

### 6.1 WPF'te "cam" nasıl yapılır

- **Pencere içi bulanıklık yoktur.** WPF aynı pencere içinde arkadaki öğeleri bulanıklaştıramaz. `BlurEffect` + `VisualBrush` hilesi her karede yeniden çizim demektir; 18 sayfada kasma yapar. **Kullanılmayacak.**
- Cam hissi şu üçünün birleşiminden gelir:
  1. Mica (DWM, bedava, duvar kağıdından renk),
  2. yarı saydam örtüler (§2.2),
  3. statik ortam ışığı (§2.3) ve degrade cam kenarı.
- Bütün fırçalar `Freeze` edilir; degradeler kod tarafında `ThemeService` içinde bir kez üretilir.

### 6.2 Gerçek akrilik (geçici yüzeyler)

- WPF `Popup` ve ayrı `Window`'lar kendi HWND'lerine sahiptir. Bunlara Windows 11 22H2+ üzerinde `DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE = 38, DWMSBT_TRANSIENTWINDOW = 3)` ile **gerçek akrilik** uygulanabilir.
- `Helpers/AcrylicPopup.cs` adlı ekli özellik (attached property) popup açılınca HWND'yi bulur, akrilik ve 8 px köşe (`DWMWA_WINDOW_CORNER_PREFERENCE`) uygular. Desteklenmiyorsa (Windows 10, eski 11) `Acrylic.Fill` opak yedeğe düşer.
- Uygulanacak yerler: komut paleti, ComboBox açılır listesi, ContextMenu, ToolTip, toast, SelectionBar (ayrı pencere değilse yedek dolgu).

### 6.3 ThemeService değişiklikleri

- `ThemeDefinition` alanları:
  - yeni: `OverlayTint` (örtü rengi), `TintOpacity`, `AmbientPrimary`, `AmbientSecondary`;
  - mevcut: `ContentLayer`, `CardBackground` (bunlar opak yedek olarak kalır).
- `MaterialMode` (Mica / Mica Alt / Akrilik / Kapalı) ile `Transparency` (Windows'u izle / her zaman / hiç) ayarları eklenir.
- `PublishMaterials(res, def, mode)` fonksiyonu:
  - bütün §2 token'larını yazar;
  - tema, malzeme ya da Windows saydamlık ayarı değişince yeniden çağrılır (`SystemEvents.UserPreferenceChanged`).
- WPF-UI'nin kendi anahtarları da aynı değerlerle ezilir: `LayerFillColorDefaultBrush`, `CardBackgroundFillColorDefaultBrush`, `ControlFillColorDefaultBrush`, `ControlElevationBorderBrush`, `CardStrokeColorDefaultBrush`, `SubtleFillColor*`, `FlyoutBackground`, `AcrylicBackgroundFillColorDefaultBrush`. Böylece hazır denetimler de cam olur.

### 6.4 Performans bütçesi

| Ölçüt | Hedef |
|---|---|
| Sayfa geçişi | < 1 kare takılma, 167 ms giriş animasyonu |
| Boştayken GPU | Mica + statik degrade; sürekli animasyon yok (`infinite_animation` cırcırı 0'da kalır) |
| Pencere boyutlandırma | Degrade katmanı `CacheMode=BitmapCache` |
| Bellek | Donmuş fırçalar paylaşılır; tema başına tek set |

### 6.5 Testler ve cırcırlar

- Katman sırası testi, örtüleri tabanın üstüne **birleştirip (compositing)** ölçecek şekilde güncellenir: taban < katman < kart parlaklığı.
- Yeni cırcır `opaque_card_fill`: `Views` içinde kartlara elle verilen opak `Background` sayılır ve azalmalıdır.
- `link_button_border`: çerçeveli bağlantı düğmesi sayısı 0 olur.

---

## 7. Ayarlar → Görünüm (yeni grup)

```
Görünüm
├─ Tema                  [önizlemeli kutucuklar]                     (var)
├─ Pencere malzemesi     ( Mica ▾ )   Mica · Mica Alt · Akrilik · Kapalı
├─ Saydamlık             ( Windows'u izle ▾ )
├─ Ortam ışığı           [●━━] Açık   Sayfa arkasında hafif vurgu ışığı
└─ Vurgu rengi           ( Tema rengi ▾ ) Tema · Windows vurgusu
```

Tema önizleme kutucukları da cam çizimi alır: mini pencerede saydam kenar çubuğu, tonlu katman ve cam kart.

---

## 8. Fazlar

| Faz | Kapsam | Teslim | Kabul ölçütü |
|---|---|---|---|
| **C1 Malzeme token'ları** | §2.1–2.2, ThemeService `PublishMaterials`, WPF-UI anahtarlarının ezilmesi, Palette.Bootstrap | Tonlu Mica + yarı saydam katman ve kartlar | Kenar çubuğu, başlık ve içerik aynı yüzeyin tonları; her 6 temada katman sırası korunur |
| **C2 Kabuk** | §3: kök ton, saydam kenar çubuğu, Subtle seçim, kaydırıcı içeride, katman kenarı | MainWindow | Ekran görüntüsündeki 1, 2, 7, 8 numaralı sorunlar kapanır |
| **C3 Cam kenarı ve kahraman** | §2.4–2.5, `Card.Surface*`, `Link.Button` çerçevesiz, MetricTile degrade sparkline | Fluent.xaml + Layout.xaml | 3, 4, 5, 6 numaralı sorunlar kapanır |
| **C4 Ortam ışığı** | §2.3, ModulePage arkasına statik ışık, Ayarlar anahtarı | Layout.xaml + ThemeService | Kartların arkasında ışık seçilir; kapalıyken düz katman |
| **C5 Akrilik geçici yüzeyler** | §6.2 `AcrylicPopup`, komut paleti, menüler, ipuçları, toast, SelectionBar | Helpers + stiller | Windows 11'de gerçek bulanıklık; Windows 10'da opak yedek |
| **C6 Denetimler** | §4: düğme yükseklik kenarı, giriş kutusu alt çizgisi, SelectorBar, InfoBar | Fluent.xaml | WPF-UI denetimleri ile yerel stiller tek dilde |
| **C7 Sayfalar** | §5 tablosu; 18 sayfa için sayfa başına bir commit | Views/Modules | Her sayfada en çok 1 kahraman; opak kart dolgusu 0 |
| **C8 Ayarlar ve erişilebilirlik** | §7 Görünüm grubu, `EnableTransparency` izleme, Yüksek karşıtlık | Settings + ThemeService | Saydamlık kapalıyken tamamen opak, düzgün görünüm |
| **C9 Diyaloglar** | `Dialog.Shell`: Mica pencere + Smoke + eylem şeridi | Views/Dialogs, Windows | Bütün diyaloglar aynı kabukta |
| **C10 Cırcır ve kabul** | §6.5 testler, sayaçlar, ekran görüntüsü iş akışı, bu belgeye durum bölümü | Tools + Tests | CI yeşil; 6 tema × 2 genişlik görüntü seti |

Sıra C1 → C2 → C3 → C4. Bu dört faz kullanıcının gördüğü kopukluğu tek başına çözer. C5–C10 cilalama ve kapsamdır.

---

## 9. Yapılmayacaklar

- Pencere içinde canlı bulanıklık (`BlurEffect`): performans maliyeti yüksek, §6.1.
- Neon parıltı, sürekli dönen ya da akan degradeler: Windows 11 dili değil.
- Vurgu rengiyle boyanmış büyük yüzeyler; tek istisna kahraman tonu (%8).
- Kart içinde kart. İç bölgeler `Card.Fill.Secondary` olur, ikinci bir cam kenarı eklenmez.

---

## 10. Not: yarım kalan başlangıç

C1'in bir kısmının taslağı çalışma ağacında **commitlenmemiş** duruyor. Bu plan onaylanınca C1 bu belgeye göre baştan ve eksiksiz yapılacak:

- `ThemeService.PublishGlass`,
- `Surface.WindowTint`, `Surface.GlassStroke`,
- MainWindow kök tonu.
