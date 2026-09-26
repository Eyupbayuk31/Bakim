# Düzen ve Yerleşim Planı — "Windows 11 kalitesinde" (v4.4 hedefi)

> Önceki planlar (`OYUN_MODU_TASARIM_PLANI.md`, `FLUENT_SAYFA_PLANI.md`) renk, metin ve bileşen
> düzeyindeydi. Ekran görüntüsü (Kontrol Paneli, 26.09.2026) asıl sorunun **yerleşim sistemi
> olmaması** olduğunu gösterdi: her sayfa kendi `Grid`'ini, kendi boşluklarını ve kendi kart
> stilini elle kuruyor. Bu plan önce sistemi kurar, sonra her sayfayı o sistemle **yeniden dizer**.

---

## 0. Teşhis — ekran görüntüsündeki 14 kusur ve kök nedenleri

| # | Görülen | Kök neden (kodda) |
|---|---|---|
| 1 | Kartlar zeminden **koyu**, "delik" gibi duruyor | Mica zemin açık/renkli, kart rengi ise opak koyu (`#0D0E12`/`#1E293B`/`#2B2B2B`). Windows 11'de kart zeminden **açıktır** (katman + %5 beyaz). Katman modeli yok. |
| 2 | "Son etkinlikler" ve "Disk" kartları komşularından **kayık** ve kısa | `ui:Card` varsayılan dikey hizası ortalanmış; aynı satırdaki kısa kart ortada kalıyor. 47 yerde `ui:Card`, 26 yerde `Card.Surface` — iki ayrı kart sistemi. |
| 3 | İki vurgu rengi: halka/menü **mor**, düğmeler **mavi** | Halka `Chart.Series1`, seçim göstergesi `Brush.Accent`, düğmeler `AccentFillColorDefault`. Tek vurgu kaynağı yok. |
| 4 | Başlık hiyerarşisi karışık (20 / 18 / 14 SemiBold kart başlıkları) | Kart başlığı için tek stil yok; her kart kendi `FontSize`'ını yazıyor. |
| 5 | Boşluklar tutarsız (satır arası 18, 20, 32; sütun arası 14, 16, 20) | 191 serbest `CornerRadius`, en sık 15 `Margin` değerinin hiçbiri token değil. |
| 6 | Sağlık listesinde renkli **dolgu haplar** ("Dikkat", "İyi"), sütunlar kayık ("Bilinmiyor" satırı sağa kaymış) | Rozet genişliği içeriğe göre; sütun sabit 180 px; durum rengi büyük dolgu olarak kullanılıyor. |
| 7 | Hero kartta dev boş alan; "Hızlı Bakım" sağ üstte yalnız | Üç sütunlu `Auto/*/Auto` grid; eylem dikey olarak başlığa hizalı değil. |
| 8 | "Yenile" hem sayfa başlığında hem hero'da | Sayfa kalıbı yok; her sayfa başlık eylemlerini ayrı seçiyor. |
| 9 | Kart içinde kart (Donanım satırları koyu kutular, süreçler kutu kutu) | Liste satırı bileşeni yok; satırlar `Card.Inset` ile çiziliyor. |
| 10 | Normal değerler **kırmızı** (3,26 GB, 796 MB) | Eşik mantığı yok; bellek değeri doğrudan "kritik" renkte. |
| 11 | Telemetri kartlarında anlamsız köşe rozetleri ("—", "%95", "Aktif", "%52") | Metrik kutucuğu bileşeni yok; her kart kendi başlık satırını kuruyor. |
| 12 | Kenar çubuğu: "BAKIM v4.3.0" büyük harf, alta taşan "Mağaza", görünür kaydırma çubuğu, dipte "Nöbetçi: Etkin" hapı | Kabuk yerleşimi sabit yükseklik; marka bloğu Windows 11 kalıbına uymuyor. |
| 13 | Başlık çubuğu kalabalık (arama + %35 + %95 + Yönetici + Oyun Modu + tema + kullanıcı) | Durum bilgisi başlık çubuğunda tekrarlanıyor (panoda zaten var). |
| 14 | İçerik katmanı yok: sayfa doğrudan pencere zemini üzerinde | Windows 11 Ayarlar/Mağaza'daki yuvarlak köşeli içerik katmanı (`LayerFillColorDefault`) uygulanmamış. |

**Dürüst değerlendirme:** Önceki turda sayfaları tek tek düzelttim ama ortak bir yerleşim
sistemi kurmadım; bu yüzden her sayfa hâlâ el yapımı ve birbirine benzemiyor. Bu plan tersini
yapar: **önce sistem, sonra sayfalar**; sayfalar sistemin dışına çıkamaz (otomatik denetimle).

---

## 1. Hedef görünüm

Referans: **Windows 11 Ayarlar**, **Microsoft Store (2024)**, **Dev Home** panosu.

1. **Katmanlar:** Mica pencere → yuvarlak köşeli içerik katmanı → kartlar (katmandan açık) → kart
   içi alt yüzey yalnızca gerektiğinde. Her katman bir öncekinden **açık**, gölgesiz, 1 px çizgili.
2. **Az kart, güçlü hiyerarşi:** bölüm başlıkları kartın **dışında** (20 SemiBold); kart
   başlıkları küçük (14 SemiBold). Kart içinde kart yok; listeler ayırıcı çizgili satırlar.
3. **Tek ızgara:** her sayfa aynı sayfa kabuğunu, aynı sütun aralığını (12) ve aynı bölüm
   aralığını (28) kullanır; aynı satırdaki kartlar **eşit yükseklikte**.
4. **Tek vurgu rengi:** seçim, birincil düğme, halka, bağlantı, ilerleme — hepsi
   `AccentFillColorDefault`. Durum renkleri yalnızca küçük simgede (12–16 px), dolgu olarak değil.
5. **Sakin veri:** normal değerler birincil metin rengi; renk yalnızca eşik aşılınca
   (bellek > %90, sıcaklık > 85 °C, disk > %90).

---

## 2. Faz 1 — Temel sistem (tüm sayfaları aynı anda düzeltir)

### 2.1 Katman ve yüzey renkleri

Her tema için dört yüzey tanımlanır; kontrol sırası zeminden karta **artan parlaklık**:

| Anahtar | Windows 11 Koyu | Açık | Slate | AMOLED | Neon Mor |
|---|---|---|---|---|---|
| `Layer.Window` (Mica yoksa) | `#202020` | `#F3F3F3` | `#0F172A` | `#000000` | `#090514` |
| `Layer.Content` (içerik katmanı) | `#272727` | `#F9F9F9` | `#131C2E` | `#08080A` | `#0F0920` |
| `Layer.Card` | `#2D2D2D` | `#FFFFFF` | `#1B2537` | `#111115` | `#170E2E` |
| `Layer.CardInset` | `#262626` | `#F6F6F6` | `#162031` | `#0B0B0E` | `#120B25` |
| `Stroke.Card` | `#3A3A3A` | `#E5E5E5` | `#2A3850` | `#26262C` | `#2E1B4F` |

- Mica açıkken içerik katmanı yarı saydam (`#4D3A3A3A` benzeri) → Windows 11 Ayarlar görünümü.
- `ui:Card`/`CardControl`/`CardExpander` WPF-UI anahtarları (`CardBackground`, `CardBorderBrush`)
  bu tokenlara bağlanır; tek kaynak `ThemeService`.
- Denetim: `verify-tokens.py`'ye "kart parlaklığı > içerik katmanı > pencere" kuralı (her tema).

### 2.2 Tek vurgu

- `Brush.Accent`, `Chart.Series1` (halka), seçim göstergesi, `ProgressBar`, bağlantılar →
  `AccentFillColorDefaultBrush` / `AccentTextFillColorPrimaryBrush`.
- "Windows vurgu rengini kullan" açıkken hepsi sistem rengine döner; kapalıyken temanın rengi.
- Grafik serileri vurgu dışı nötr paletten (`Chart.Series2..6`); ilk seri vurgu.

### 2.3 Izgara, boşluk, köşe (sabit ölçek — başka değer yasak)

| Token | Değer | Kullanım |
|---|---|---|
| `Page.Padding` | 36,28,36,36 | Sayfa kenar boşluğu (dar ekranda 24) |
| `Page.MaxWidth` | 1360 (pano) / 1000 (ayar düzenli sayfalar) | İçerik genişliği, sola yaslı |
| `Gap.Section` | 28 | Bölümler arası (bölüm başlığı üstü) |
| `Gap.Grid` | 12 | Kartlar arası yatay ve dikey |
| `Gap.List` | 4 | Ayar kartları arası (Windows 11) |
| `Pad.Card` | 20 | Kart iç boşluğu |
| `Pad.Card.Hero` | 24 | Sayfa başı kartı |
| `Pad.Row` | 12,10 | Liste satırı |
| `Radius.Card` | 8 | Kart, içerik katmanı |
| `Radius.Control` | 4 | Düğme, rozet, satır vurgusu |
| `Row.Height` | 48 / 60 (iki satırlı) | Liste satırları |
| `Tile.Height` | 136 | Metrik kutucuğu |

Serbest `Margin`/`Padding`/`CornerRadius` sayısı cırcırla **sıfıra** iner (`verify-design-debt`
yeni sayaçlar: `free_margin`, `free_radius`, `ui_card_raw`).

### 2.4 Yazı hiyerarşisi (yalnızca bunlar)

| Rol | Stil | Nerede |
|---|---|---|
| Sayfa başlığı | 28 SemiBold | `c:Page.Title` |
| Bölüm başlığı | 20 SemiBold, kartın **dışında** | `c:Section.Header` |
| Kart başlığı | 14 SemiBold | `c:CardHeader` |
| Gövde | 14 Regular | satır başlığı, açıklama |
| Açıklama | 12 Regular, ikincil | alt satır |
| Büyük değer | 28 SemiBold (kutucuk) / 40 (hero sayısı) | `c:MetricTile`, halka |

### 2.5 Yeni yerleşim bileşenleri (`Controls/Layout/`)

| Bileşen | Görev | Yerine geçtiği |
|---|---|---|
| `c:Page` | Başlık, alt başlık, eylemler (en fazla 1 birincil + `⋯`), isteğe bağlı sekme ve komut satırı yuvası, kaydırma, `MaxWidth`, kenar boşluğu, **kırılma noktaları** (Dar < 1000, Orta, Geniş ≥ 1400) | 20 sayfadaki elle `Grid` + `SectionHeader` + `ScrollViewer` |
| `c:Section` | Dışarıda 20 px başlık + isteğe bağlı açıklama + sağda "Tümünü gör ›" bağlantısı; üstte `Gap.Section` | Kart içi büyük başlıklar |
| `Surface.Card` (stil) | `Layer.Card`, 1 px çizgi, 8 px köşe, `Pad.Card`, **Stretch hizası**; varyantlar: `.Hero`, `.Compact`, `.Interactive` (üzerine gelince zemin açılır, çerçeve değişmez) | 47 `ui:Card` + 26 `Card.Surface` |
| `c:CardHeader` | 16 px simge + 14 SemiBold başlık + sağda rozet/bağlantı/`⋯`; tek satır 32 px | Kart başına farklı başlık satırları |
| `c:AdaptiveGrid` (panel) | `MinColumnWidth`, `MaxColumns`, `Gap.Grid`; genişliğe göre sütun sayısı; satırdaki kartlar **eşit yükseklik**; `c:AdaptiveGrid.Span` | 4'lü/2'li sabit `ColumnDefinition` ızgaraları |
| `c:MetricTile` | Etiket, büyük değer + birim, tek satır ikincil bilgi, isteğe bağlı sparkline (alt kenara yaslı), sabit 136 px; köşe rozeti yok | Telemetri kartları, özet şeritleri |
| `c:ListCard` + `c:ListRow` | Kart içinde 1 px ayırıcılı satırlar; satır: 20 px simge/uygulama ikonu, başlık + alt satır, sağda değer, sağda eylem/`⋯`; üzerine gelince hafif zemin | Kart içinde kart listeleri, süreç kutuları |
| `c:StatusGlyph` | 12 px renkli durum işareti + metin (dolgu yok) | Renkli dolgu haplar |
| `c:KeyValueList` | 2 sütun anahtar/değer, ayırıcılı | Donanım satır kutuları |
| `c:SelectionBar` | Alt sabit çubuk: "14 öğe seçili · 3,2 GB  [Birincil eylem] [İptal]" | Sayfa başındaki toplu eylem düğmeleri |
| `c:EmptyState` (var) | Üç boy: satır içi / kart / sayfa | — |

### 2.6 Kabuk

- **İçerik katmanı:** kenar çubuğunun sağındaki alan `Layer.Content`, sol üst köşe 8 px, üst ve
  sol 1 px çizgi (Windows 11 Ayarlar). Sayfa geçişi 167 ms solma + 8 px kayma.
- **Kenar çubuğu:** marka bloğu → uygulama simgesi + "Bakım" (sürüm Hakkında'da); grup başlıkları
  12 SemiBold ikincil; öğe 36 px; kaydırma çubuğu yalnızca üzerine gelince; "Nöbetçi: Etkin" hapı
  kaldırılır (Nöbetçi durumu kenar çubuğunda öğenin yanında küçük nokta).
- **Başlık çubuğu:** ortada arama (en fazla 480 px, Dosya Gezgini/Mağaza gibi); sağda yalnızca
  Oyun Modu anahtar çipi ve yönetici kalkanı. İşlemci/bellek çipleri, tema düğmesi ve kullanıcı
  çipi kaldırılır (panoda ve Ayarlar'da var). Başlık metni "Bakım".

### 2.7 Kural ve denetim

- `verify-design-debt`: `ui_card_raw` (sayfalarda çıplak `ui:Card`), `free_margin`,
  `free_radius`, `card_title_size` (kart içinde 14 dışı başlık), `status_fill` (durum rengiyle
  dolgu), `nested_card` (kart içinde `Surface.Card`) — hepsi hedef 0.
- **Ekran görüntüsü hattı (yeni):** GitHub Actions Windows işi her sayfayı `RenderTargetBitmap`
  ile 1280 ve 1600 px genişlikte, Koyu ve Açık temada PNG'ye çizer ve yapıt olarak yükler. Böylece
  her commit'te görüntüleri ben de inceleyebilirim; senin ekran görüntüsü atman gerekmez.

---

## 3. Sayfa sayfa yeni yerleşim

Gösterim: `[ ]` kart, `──` ayırıcılı satır, `(4→2→1)` geniş→orta→dar sütun sayısı.

### 3.1 Kontrol Paneli (referans sayfa — ilk yapılacak)

```
Genel bakış
Sistem durumu ve hızlı bakım
[ Hero ─────────────────────────────────────────────────────────────── ]
[ (◯ 95)  Sistem iyi durumda                           [Hızlı bakım]  ]
[         1 konu puanı düşürüyor · son kontrol 19:40                  ]
[ ─────────────────────────────────────────────────────────────────── ]
[ ⚠ Son temizlik     Bakım ile henüz temizlik yapılmadı     Temizle › ]
[ ✓ 5 bileşen iyi durumda                                          ⌄  ]   ← iyi olanlar katlanır
Canlı durum                                                       (4→2→1)
[ İşlemci      ][ Bellek       ][ Ağ            ][ Disk C:      ]
[ %35          ][ 15,1 / 15,9GB][ ↓70  ↑89 KB/s ][ %53 dolu     ]
[ 3,50 GHz ▁▂▅ ][ 0,8 GB boş ▁▃][ Ethernet  ▁▁▂ ][ 219 GB boş ▂ ]
Etkinlik ve oyun                                                  (2→1)
[ Son etkinlikler      Tümü › ][ Oyun Modu               [Başlat] ]
[ ── 19:40 Başlangıç 'Medal'…  ][ Son oturum: dün, 2 sa 14 dk      ]
[ ── …                         ][ Ayrıntılar ›                     ]
Kaynaklar                                                         (2→1)
[ En çok bellek     Süreçler › ][ Donanım      Sistem Bilgisi ›   ]
[ ── ▣ VALORANT       3,26 GB ⋯][ İşlemci   AMD Ryzen 5 5600      ]
[ ── ▣ Overwolf        797 MB ⋯][ Ekran k.  Radeon RX 9060 XT 16GB]
[ ── ▣ Discord         466 MB ⋯][ Sıcaklık  Sensör verisi yok     ]
```
- Halka 88 px, vurgu renginde; puan 40 SemiBold. Sayfa başlığındaki "Yenile" kalkar (canlı).
- Sağlık satırları `c:ListRow` + `c:StatusGlyph`; yalnızca sorunlu olanlar açık, iyiler tek satırda.
- Süreç değerleri nötr; `⋯` menüsü: Sonlandır (onaylı), Dosya konumunu aç, Süreçler'de göster.

### 3.2 Etkinlik Merkezi
```
Etkinlik Merkezi                                        [⋯]
[Ara…] [Tür ▾] [Sonuç ▾] [Son 7 gün ▾]   ☐ Yalnızca geri alınabilir
Bugün ───────────────────────────────── (ListCard, gün başına)
 ── 19:40  ▣ Başlangıç  'Medal' devre dışı bırakıldı     Geri al
Dün ─────────────────────────────────────
                                           │ Ayrıntı paneli (360 px, seçilince)
```
Özet şeridi kalkar (sayılar filtre açılır kutularında: "Başlangıç (12)").

### 3.3 Temizleyici
```
Temizleyici                                   [Tara]  ← tarama sonrası [12,4 GB temizle]
[ C:  ▓▓▓▓▓░░░░  219 GB boş / 465 GB ]   (tek satır sürücü şeridi, kart yok)
Ön ayar [Önerilen ▾]
Windows (ListCard)                 Tarayıcılar (ListCard)          (2→1)
 ── ☑ Geçici dosyalar   1,2 GB      ── ☑ Chrome önbelleği  640 MB
 ── ☑ Windows Update    3,4 GB      ── ☐ Edge (açık)  ⓘ
Uygulamalar (ListCard)             Geri Dönüşüm Kutusu (ListCard)
[ SelectionBar: 9 kategori · 12,4 GB                    [Temizle] ]
```

### 3.4 Depolama
- Sekmeler aynı; her sekme: komut satırı → `ListCard` tablo → `SelectionBar`.
- Disk haritası: kart içinde tam yükseklik, kutular 4 px köşe, gezinme yolu kartın başlığında.

### 3.5 Süreçler
```
Süreçler                                                  [⋯]
[ Bellek kullanımı (Hero) ▓▓▓▓▓▓▒▒░░  Kullanımda · Değiştirilmiş · Bekleme · Boş  [Belleği boşalt] ]
[Ara…] [Grupla: Uygulama ▾]                            (tablo başlıkları: Ad · CPU · Bellek · Disk · Ağ)
[ ListCard tablo (grup başlıkları) ]                   │ Ayrıntı paneli
```

### 3.6 Başlangıç
```
Başlangıç                                            [Uygulama ekle] [⋯]
[ Hero: Son açılış 18,4 sn  ▁▂▃▂▁ son 5 açılış   · 3 uygulama geciktiriyor  [Önerileri gör] ]
[Tümü][Etkin][Devre dışı][Yavaşlatan]                     [Ara…] [Sırala ▾]
[ ListCard: ▣ Discord  Discord Inc.  Yüksek etki   ●━ Açık ]   ← Win11 Başlangıç uygulamaları
```

### 3.7 Hizmetler ve sürücüler
Sekmeler; `c:MetricTile` şeridi yerine tek satır sayım metni; "Önerilen profiller" katlanır
`CardExpander`; tablo `ListCard`, başlangıç türü satırda açılır kutu.

### 3.8 Oyun Modu
Referans kalır; yalnızca `c:Page`/`c:Section`/`Surface.Card`'a taşınır, ayar kartları `Gap.List`.

### 3.9 Analizör
```
Analizör                       [Tara]  [⋯ VirusTotal · CSV]
[Tarama][Geçmiş][Değişiklikler]
Tek satır sayım: 214 öğe · 3 imzasız · 0 tespit
[Ara…] [Kategori ▾] ☐ Microsoft öğelerini gizle
[ ListCard tablo: ●━ ▣ Ad / yol · Kategori · İmza · Risk ⋯ ] │ Ayrıntı paneli
```

### 3.10 Kurulum Nöbetçisi
Hero durum kartı (Oyun Modu gibi, anahtar sağda) → `Section "Kurulumlar"` → gün gruplu `ListCard`.

### 3.11 Ağ İzleyici
Sekmeler; Bağlantılar: komut satırı + `ListCard` tablo + ayrıntı paneli; Hız testi: tek büyük
kart, ortada 64 px değer, altında 3 `c:MetricTile` (gecikme · dalgalanma · en yüksek);
Tanılama: ayar kartı listesi (yapıldı, `Gap.List`'e taşınır).

### 3.12 Kaldırıcı (Windows 11 Yüklü uygulamalar birebir)
```
Kaldırıcı                          [Pencereden seç] [Kurulumu izle] [⋯]
[Ara…]                    Sırala [Boyut ▾]   Filtre [Uygulamalar ▾]    214 uygulama · 38 GB
[ ListCard: ▣ Steam        Valve · 2.10 · 12.03.2025        1,2 GB  ⋯ ]
[ SelectionBar: 3 seçili · 4,1 GB                         [Kaldır] ]
```

### 3.13 Mağaza
Kutucuk ızgarası `c:AdaptiveGrid` (MinColumnWidth 300) + `Surface.Card.Interactive`; alt
`SelectionBar` ("3 uygulamayı kur"); İndirmeler sağdan açılan panel.

### 3.14 Sistem Bilgisi
Yapıldı (Hakkında düzeni); `c:Page` MaxWidth 1000 + `Gap.List`'e taşınır, disk kartları `ListCard`.

### 3.15 Olaylar ve çökmeler
Hero: sağlık + 30 günlük güvenilirlik (vurgu çizgi grafik, 120 px yükseklik); sekmeler; listeler
`ListCard`; Onarım sekmesi ayar kartları.

### 3.16 Windows Ayarları + Gizlilik, Ayarlar
Windows 11 Ayarlar düzeni: sol kategori menüsü 280 px (içerik katmanının içinde), sağda
`MaxWidth 1000` ayar kartları, kartlar arası 4 px, kategori başlığı 28 px sayfa başlığı gibi
(Windows 11'deki "Sistem > Ekran" başlığı). Ayarlar sayfası bölünür (1422 satır → kategori başına
UserControl). Tema seçimi: 5 önizleme kutucuğu (mini pencere çizimi), seçili olanda vurgu çerçevesi.

### 3.17 Windows Araçları
`c:AdaptiveGrid` (MinColumnWidth 280, 4→3→2) kutucuklar, 32 px simge; üstte tek ayar kartı.

### 3.18 Diyaloglar
Tek `Dialog.Shell`: başlık 20 SemiBold, içerik `Layer.Content`, alt eylem çubuğu `Layer.Card`
üzerinde sağa yaslı (birincil sağda). Dosya analizi solda bölüm menüsü.

---

## 4. Fazlar ve sıra

| Faz | İçerik | Sonuç |
|---|---|---|
| **1** | §2.1 katman renkleri, §2.2 tek vurgu, §2.3 tokenlar, `Surface.Card` (ui:Card'ın yerine, Stretch), §2.6 içerik katmanı + kenar çubuğu + başlık çubuğu | Bütün sayfalarda "delik kart", kayık kart ve iki renk sorunu bir anda biter |
| **2** | `c:Page`, `c:Section`, `c:CardHeader`, `c:AdaptiveGrid`, `c:MetricTile`, `c:ListCard/ListRow`, `c:StatusGlyph`, `c:KeyValueList`, `c:SelectionBar` + ekran görüntüsü hattı (Actions) | Yerleşim sistemi ve görsel doğrulama |
| **3** | Kontrol Paneli yeniden dizilir (§3.1) — referans | Diğer sayfaların örneği |
| **4** | Liste sayfaları: Etkinlik, Süreçler, Başlangıç, Hizmetler, Analizör (+Geçmiş/Değişiklikler), Kaldırıcı, Nöbetçi, Olaylar | Ortak `ListCard` + ayrıntı paneli |
| **5** | Araç sayfaları: Temizleyici, Depolama, Ağ İzleyici, Mağaza, Windows Araçları | `SelectionBar`, `AdaptiveGrid` |
| **6** | Ayar düzenli sayfalar: Windows Ayarları, Gizlilik, Ayarlar (bölünür), Sistem Bilgisi, Oyun Modu | Windows 11 Ayarlar düzeni |
| **7** | Diyaloglar `Dialog.Shell`; hareket (sayfa girişi, kart hover 83 ms); dar pencere (1000 px) düzenleri | Cila |
| **8** | Cırcırlar 0'a; 5 tema × 2 genişlik ekran görüntüsü karşılaştırması | Kabul |

Her faz ayrı commit dizisi; her sayfa kendi commit'i; her commit'te ekran görüntüsü yapıtı.

---

## 5. Kabul ölçütleri

| Ölçüt | Şimdi | Hedef |
|---|---|---|
| Kart zemini pencere/katmandan açık | Hayır (5 temada) | Evet (otomatik denetim) |
| Vurgu tonu sayısı | 2 (mor + mavi) | 1 |
| Sayfalarda çıplak `ui:Card` | 47 | 0 |
| Aynı satırda farklı yükseklikte kart | Var | 0 (`AdaptiveGrid`) |
| Kart başlığı boyutu | 14/18/20 | yalnızca 14 |
| Kart içinde kart | Var (Pano, Sistem Bilgisi, Ayarlar) | 0 |
| Durum rengiyle dolgu | Var (haplar, kırmızı değerler) | 0 (yalnızca `StatusGlyph`) |
| Serbest Margin / CornerRadius | yüzlerce / 191 | 0 |
| Başlık çubuğu öğesi | 7 | 3 (arama, Oyun Modu, yönetici) |
| Görsel doğrulama | Kullanıcı ekran görüntüsü | Her commit'te otomatik PNG |
