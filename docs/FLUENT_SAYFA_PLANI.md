# Tüm Sayfalar — Windows 11 Fluent Tasarım Planı (v4.3 hedefi)

> Kapsam: 20 modül sayfası, kabuk (başlık çubuğu, kenar çubuğu, komut paleti), 11 pencere/diyalog.
> Dayanak: `docs/OYUN_MODU_TASARIM_PLANI.md` §1.1 Fluent kuralları (bağlayıcı), MASTER_PLAN §3 ve §310
> (abartılı dil yasak). Oyun Modu ve Ayarlar sayfaları bu planın örnek aldığı referans sayfalardır.
>
> Yöntem: Her XAML dosyası otomatik ölçüldü (aşağıdaki tablo), ardından sayfalar tek tek okundu.
> Uygulama bu ortamda çalıştırılamadığı için görsel ayrıntılar Windows ekran görüntüleriyle doğrulanmalı.

---

## 0. Ölçüm (26.09.2026, `c799bc4`)

| Sayfa | Satır | 4'lü KPI kartı | Vurgu metni | Birincil düğme | Tehlike düğmesi | Serbest boşluk | Sonsuz animasyon | Başlık Büyük Harf | "&" |
|---|---|---|---|---|---|---|---|---|---|
| Kontrol Paneli | 655 | 4 kart + 4 telemetri | 11 | 1 | 0 | 36 | 0 | 18 | 2 |
| Etkinlik Merkezi | 318 | 4 | 1 | 1 | 0 | 9 | 0 | 1 | 0 |
| Temizleyici | 774 | 3 + disk | 11 | 1 | 2 | 40 | **1 (tarama ışını)** | 22 | 4 |
| Depolama | 1556 | — | 14 | **7** | **6** | **108** | **1 (radar ışını)** | 63 | 5 |
| Süreçler | 699 | 4 | 13 | 0 | 2 | 47 | 0 | 31 | 1 |
| Başlangıç | 521 | 3 + açılış | 4 | 1 | 1 | 33 | 0 | 18 | 3 |
| Hizmetler & Sürücüler | 723 | 4 + 4 | 8 | 0 | 1 | 45 | 0 | 29 | 3 |
| Analizör | 666 | 5 | 8 | 2 | 1 | 37 | 0 | 25 | 1 |
| Kurulum Nöbetçisi | 190 | 4 | 1 | 0 | 0 | 6 | 0 | 1 | 0 |
| Ağ İzleyici | 1398 | 4 | **24** | **6** | 3 | 75 | 0 | **67** | **13** |
| Kaldırıcı | 739 | 4 | 8 | 1 | 1 | 47 | 0 | 15 | 0 |
| Mağaza | 1129 | şerit | 13 | **6** | 0 | 81 | 0 | 26 | **10** |
| Sistem Bilgisi | 810 | — | **25** | 1 | 0 | 71 | 0 | 34 | 7 |
| Olaylar & Çökmeler | 566 | 4 | 5 | 1 | 0 | 35 | 0 | 17 | 1 |
| Windows Ayarları | 931 | 4 | 16 | **5** | 3 | 83 | 0 | 42 | 9 |
| Gizlilik (Windows Ayarları içinde) | 703 | 4 | 10 | 1 | 2 | 38 | 0 | 14 | 5 |
| Windows Araçları | 233 | — | 4 | 1 | 0 | 23 | 0 | 11 | 5 |
| Ayarlar | 1458 | — | 17 | 3 | 0 | 70 | 0 | 57 | 12 |
| Oyun Modu (referans) | 399 | — | 2 | 1 | 0 | 27 | 0 | 1 | 0 |

Uygulama geneli: **52 birincil düğme** (sayfa başına Fluent'te 1), **26 tehlike düğmesi**, **4 "Success"
düğmesi** (Fluent'te yok), **622 Büyük Harfli başlık**, **~80 "&"**, **9 kendi kabuğunu çizen pencere**
(`WindowStyle=None` + `AllowsTransparency` + çoğu `Topmost`), 2 sonsuz dekoratif animasyon, 4 gradyan fırça.

---

## 1. Uygulama geneli bulgular

| # | Bulgu | Kanıt | Fluent kuralı |
|---|---|---|---|
| G1 | **Ortak sayfa kalıbı yok.** Kök boşluk sayfadan sayfaya 0 / 24 / 28 / `Pad.Module`; Temizleyici'nin sayfa başlığı hiç yok; sekmeler kimi sayfada başlığın içinde, kimi altında. | `CleanerModuleView.xaml:12`, `PrivacyDebloatModuleView.xaml:14` | Her sayfa: başlık → (sekmeler) → komut çubuğu → içerik. Aynı kenar boşluğu. |
| G2 | **"4 KPI kartı" refleksi.** 13 sayfa 3–4 büyük sayı kartıyla açılıyor; ekranın üçte biri sayıya gidiyor, asıl iş (liste, eylem) aşağı itiliyor. Bazıları sabit ve bilgi taşımıyor: Temizleyici "Sistem Koruması: Kalkan Aktif", Kontrol Paneli "Canlı Telemetri Aktif". | `CleanerModuleView.xaml:53`, `DashboardModuleView.xaml:36` | Windows 11 Ayarlar > Depolama gibi: tek satırlık **özet şeridi**, yalnızca karar verdiren sayılar. |
| G3 | **Vurgu rengi her yerde.** Ağ İzleyici 24, Sistem Bilgisi 25 vurgu renkli metin/simge. Vurgu anlamını yitiriyor. | ölçüm tablosu | Vurgu yalnızca birincil eylem, seçim göstergesi, bağlantı ve etkin durum için. |
| G4 | **Düğme renk karnavalı.** Temizleyici'de yan yana mavi "Analiz Et", yeşil "Güvenle Temizle", kırmızı "İptal". Depolama'da 7 birincil + 6 tehlike düğmesi. | `CleanerModuleView.xaml:158-181` | Görünüm başına **tek** vurgu düğmesi; "İptal" standart; kırmızı yalnızca onay diyaloğundaki yıkıcı eylemde; "Success" düğmesi yok. |
| G5 | **Dekoratif animasyonlar.** "Cyber Scan Beam" ve "Radar Scan Beam" sonsuz döngüde kayan gradyan ışınlar; sparkline'larda gradyan dolgu; Avcı Modu'nda "Glowing Neon" çerçeve. | `CleanerModuleView.xaml:184-215`, `StorageModuleView.xaml:433-460` | İlerleme Fluent `ProgressBar` (belirsiz ya da yüzdeli); grafikler düz. |
| G6 | **Metin dili.** 622 Büyük Harfli başlık ("Temizlik Hedefleri (20 Kategori)"), ~80 "&", İngilizce parantezler ("Reset Cache Engine", "Flush DNS", "System Tray"), metaforlar ("Röntgen", "Avcı Modu", "Dosya Teftiş Röntgeni"), abartı ("1000 Mbps Ultra Gigabit", "Harika!", "tek tıkla"). | §0 | Windows 11: **cümle düzeni** ("Temizlik hedefleri"), "ve", Türkçe terim, sakin ve dürüst ton. |
| G7 | **Tablolar elle yapılmış.** Her liste sayfası kendi başlık satırını, satır yüksekliğini, hover ve seçim görünümünü yazıyor; sütun başlıkları farklı boyut/renkte. | Hizmetler, Başlangıç, Kaldırıcı, Analizör, Ağ | Tek **tablo kiti**: 40 px satır, hafif hover, seçili satırda sol vurgu çubuğu, Caption başlıklar. |
| G8 | **Ayrıntı panelleri tutarsız.** "Röntgen" çekmecesi Ağ, Başlangıç, Temizleyici, Süreçler'de ayrı ayrı yazılmış; genişlik, kapatma düğmesi, bölüm başlıkları farklı. | `NetworkMonitorModuleView.xaml:467-504`, `StartupModuleView.xaml:417` | Tek **ayrıntı paneli** bileşeni (Fluent "details pane"). |
| G9 | **Diyaloglar Windows 11'e benzemiyor.** 9 pencere kendi kabuğunu çiziyor: `AllowsTransparency`, `DropShadowEffect`, 2 px vurgu kenarlığı, 16 px köşe, çoğu `Topmost`. Mica yok, sistem gölgesi yok, sürüklenemez/klavyeyle kapanmaz olanlar var. | `HunterActionDialog.xaml:1-20`, `UpdateDialogView.xaml` | `ui:FluentWindow` + **ContentDialog düzeni** (başlık, içerik, altta sağa yaslı Birincil/İptal şeridi). `Topmost` yalnızca Nöbetçi bildirimi gibi gerçekten gereken yerde. |
| G10 | **Sekme ile filtre aynı görünüyor.** Sayfa sekmeleri (Donanım / S.M.A.R.T.) ve liste filtreleri (Tümü / TCP / UDP) aynı dolu vurgu düğmesi. | 70 `BoolToNavAppearance` kullanımı | Sekme: **SelectorBar** (metin + alt vurgu çizgisi). Filtre: **ToggleButton / çip**. |
| G11 | **Boş / yükleniyor / hata durumları dağınık.** `EmptyState` yalnızca 7 yerde; kimi sayfa "Harika! Hiçbir Yinelenen Dosya Bulunamadı" gibi özel metin, kimi boş liste gösteriyor; yükleniyor durumu kimi yerde halka, kimi yerde ışın. | `StorageModuleView.xaml:1329` | Her liste için üç durum: iskelet satırlar (`SkeletonRow`), `EmptyState`, `InfoBar` hata. |
| G12 | **Serbest boşluklar.** ~1.300 sabit Margin/Padding değeri (10, 14, 18, 22, 26...). | ölçüm tablosu | 4 px ızgarası ve `Gap.*` / `Pad.*` token'ları. |
| G13 | **Üçüncül metin fazla.** Ağ 31, Hizmetler 15, Kontrol Paneli 19 üçüncül metin; açıklamalar soluk ve okunmuyor. | ölçüm tablosu | Açıklama = ikincil; üçüncül yalnızca zaman damgası / yer tutucu. |
| G14 | **Sayfa sonuçları farklı yollarla bildiriliyor.** Kimi sayfa InfoBar, kimi kart içi yazı, kimi toast, kimi rozet. | Temizleyici "Operasyon Tamamlandı" kartı, Süreçler sonuç şeridi | Sayfa düzeyi sonuç = `InfoBar` (sayfa başlığının altında); geri alınabilir işlem = toast + "Geri al". |

---

## 2. Faz A — Ortak Fluent bileşenleri ve kurallar (her şey bunun üstüne kurulur)

### 2.1 Sayfa kalıbı

```
┌─ Sayfa başlığı (Title 28 SB) ──────────────────────── [ikincil eylemler] [BİRİNCİL] ┐
│  Tek cümle açıklama (Body, ikincil)                                                 │
├─ SelectorBar: Sekme 1  Sekme 2  Sekme 3   (yalnızca sekmeli sayfalarda)             │
├─ InfoBar (sonuç / uyarı; yalnızca varsa)                                            │
├─ Özet şeridi (isteğe bağlı, tek kart, 2–4 değer)                                    │
├─ Komut çubuğu: arama · filtre çipleri · sıralama ······ [⋯ taşma menüsü]             │
└─ İçerik: liste + (seçilince) ayrıntı paneli  |  ya da ayar kartları                 ┘
```

- **İki düzen türü:** `Layout.Page` (ayar/form sayfaları, 1000–1180 px, ortalı) ve `Layout.Workspace`
  (veri sayfaları, tam genişlik, 24 px kenar). Kök boşluk yalnızca bu stillerden gelir.
- `c:SectionHeader` her sayfada zorunlu; birincil eylem başlığın sağında, **tek** tane.

### 2.2 Yeni / güncellenecek bileşenler

| Bileşen | Amaç | Yerini aldığı |
|---|---|---|
| `c:SelectorBar` | Sayfa sekmeleri: metin + seçili sekmede 3 px vurgu alt çizgisi, klavye ←/→ | 70 `BoolToNavAppearance` sekme düğmesi (sekme olanlar) |
| `Style ToggleChip` | Filtre çipleri (ToggleButton): seçili = vurgu dolgusu, değil = hafif çerçeve, 32 px | Filtre düğmeleri |
| `c:SummaryStrip` | Tek kart, 2–4 değer, aralarında ince ayırıcı; değer Subtitle 20, etiket Caption | 13 sayfadaki 4'lü StatCard satırları |
| `c:CommandBar` | Arama + filtreler + sağda ikincil eylemler + `⋯` taşma menüsü (ui:DropDownButton) | Sayfalardaki 3–6 düğmelik araç çubukları |
| Tablo kiti (`Table.HeaderText`, `Table.Row` ListViewItem stili, `Table.Cell`) | 40 px satır, hover `SubtleFillColorSecondary`, seçili satır `Surface.Selected` + sol çubuk, başlık Caption SB ikincil | Elle yazılmış başlık satırları |
| `c:DetailsPane` | Sağ ayrıntı paneli: başlık + kapat (Esc), `KeyValueGrid` bölümleri, altta eylemler; 360 px, dar pencerede alt sayfa | 5 farklı "Röntgen" çekmecesi |
| `c:ProgressHeader` | Liste üstünde ince Fluent ProgressBar + durum metni ("1.204 dosya tarandı") | Tarama ışınları |
| `Dialog.Shell` (FluentWindow şablonu) | ContentDialog düzeni: başlık (Subtitle 20), içerik, alt şerit (Birincil / İkincil / İptal), Esc = İptal, Enter = Birincil | 9 özel kabuklu pencere |
| `c:EmptyState` genişletme | `Kind`: İlk kez (eylem düğmeli), Sonuç yok, Filtre sonuç vermedi (filtreyi temizle) | Sayfa sayfa özel boş metinler |
| `c:StatusBadge` → Fluent InfoBadge görünümü | 20 px yükseklik, hap, simge + metin | — |
| Grafik stili | Sparkline: 1.5 px düz çizgi `Chart.SeriesN`, dolgu yok ya da %12 düz dolgu; eksen/ızgara `Chart.Grid` | Gradyanlı sparkline'lar |

### 2.3 Kurallar (yazılı + otomatik denetim)

1. **Düğme anlamı:** Görünüm başına tek `Primary`. `Danger` yalnızca onay diyaloğundaki yıkıcı eylem.
   `Success`, `Caution`, `Info` görünümleri kullanılmaz. İptal/Durdur = standart.
2. **Vurgu rengi:** yalnızca birincil eylem, seçim göstergesi, bağlantı, "etkin" durum simgesi.
3. **Metin:** cümle düzeni; "&" yerine "ve"; İngilizce terim yalnızca Windows'un kendi arayüzünde öyle
   geçiyorsa ve parantez içinde değil ipucunda; "Röntgen" → "Ayrıntılar", "Avcı Modu" → "Pencereden seç".
4. **Hareket:** sonsuz döngü yok (ProgressRing/ProgressBar hariç); gradyan yok (Mica dışında).
5. **Boşluk:** yalnızca `Gap.*`, `Pad.*`, `Space.*`.
6. **Üçüncül metin:** yalnızca zaman damgası, yer tutucu, devre dışı.

`Tools/verify-design-debt.py` yeni sayaçlar (cırcır): `primary_per_view>1`, `danger_outside_dialog`,
`success_appearance`, `infinite_animation`, `gradient_brush`, `title_case_text`, `ampersand_text`,
`literal_spacing`, `custom_chrome_window`, `accent_text`. Her faz sonunda eşik düşürülür.

---

## 3. Sayfa sayfa plan

Her sayfa için: **Şimdi** → **Sorunlar** → **Hedef**. Numaralar §1'deki genel bulgulara bağlanır.

### 3.1 Kabuk (başlık çubuğu, kenar çubuğu, komut paleti)
- **Şimdi:** Fluent kenar çubuğu, arama alanı, tuş kapakları (v4.2).
- **Sorunlar:** Kenar çubuğunda 6 grup + 20 öğe kaydırma gerektiriyor; daraltılmış hâlde grup ayırıcıları
  ilk grubun üstünde de çiziliyor; alt bölümdeki "Nöbetçi: Etkin" durumu düğme gibi görünmüyor.
  Komut paletinin kendi kabuğu (16 px köşe, gölge) Fluent değil; sonuç satırlarında kategori hapları renkli.
- **Hedef:** Gruplar 5'e (Genel bakış · Temizlik · Performans · Güvenlik · Sistem); "Mağaza" ve "Kaldırıcı"
  "Uygulamalar" altında. Komut paleti: 8 px köşe, `Surface.Overlay`, sistem gölgesi, tek renkli kategori
  etiketi, seçili satırda sol vurgu çubuğu. Başlık çubuğundaki "Yönetici ol" uyarı rengi yerine
  kalkan simgeli standart düğme.

### 3.2 Kontrol Paneli
- **Şimdi:** Sağlık halkası + bileşen listesi + Hızlı Bakım; son etkinlikler + Oyun Modu; 4 telemetri kartı
  (gradyanlı sparkline); donanım sensörleri + "kaynak canavarları" tablosu.
- **Sorunlar:** G2 (4 telemetri kartı + "Canlı Telemetri Aktif" rozeti), G3, G5 (gradyan), sağlık satırında
  180 px sabit başlık sütunu dar pencerede kesiliyor, "Yenile" düğmesi telemetri zaten canlıyken gereksiz.
- **Hedef:** Üstte **sağlık kartı** (halka 96 px, başlık, 3 madde, tek birincil eylem "Hızlı bakım");
  altında iki sütun: solda "Önerilenler" (sağlık satırları eylem düğmeli, ayar kartı gibi), sağda
  "Son etkinlikler". Telemetri: tek **SummaryStrip** (CPU · Bellek · Disk · Ağ; her birinde düz sparkline).
  Sensörler ve en çok kaynak kullananlar ayrı bir "Canlı izleme" bölümüne, tablo kitiyle. Rozet ve
  Yenile düğmesi kaldırılır.

### 3.3 Etkinlik Merkezi
- **Şimdi:** 4 özet kartı, filtre çubuğu, zaman çizelgesi + ayrıntı, durum çubuğu.
- **Sorunlar:** G2, ayrıntı paneli özel yazılmış (G8), gün başlıkları BÜYÜK HARF (`ToUpper`).
- **Hedef:** SummaryStrip (bugün · bu hafta · geri alınabilir); CommandBar (arama, tür çipleri, tarih
  aralığı açılır kutusu); gün başlıkları Body Strong cümle düzeni; ayrıntı `DetailsPane`
  ("Geri al" birincil eylem, yalnızca geri alınabilir kayıtlarda).

### 3.4 Temizleyici
- **Şimdi:** 4 KPI kartı (biri sabit "Kalkan Aktif"), sonuç kartı, 20 kategori ızgarası + ön ayar çipleri,
  mavi/yeşil/kırmızı üçlü düğme, "Cyber Scan Beam", sanal dosya listesi + "Dosya Teftiş Röntgeni".
- **Sorunlar:** Sayfa başlığı yok (G1), G2, G4, G5, G6, G8.
- **Hedef:**
  1. Başlık "Temizleyici" + birincil eylem durumla değişir: **Tara** → (tarama sonrası) **12,4 GB temizle**;
     tarama sırasında standart **Durdur**.
  2. Disk doluluğu başlığın altında tek satır (C: çubuğu + "82 GB boş").
  3. Kategoriler **ayar kartı listesi** olarak gruplu (Windows, Tarayıcılar, Uygulamalar, Geri Dönüşüm
     Kutusu): her kartta onay kutusu, ad, tahmini boyut, "açık uygulama var" uyarısı. Ön ayarlar
     SelectorBar yerine tek açılır kutu ("Güvenli", "Önerilen", "Tümü").
  4. Tarama `ProgressHeader`; sonuçlar kategori başına genişletilebilir kart (`ui:CardExpander`),
     dosyalar içeride tablo kitiyle; dosya ayrıntısı `DetailsPane`.
  5. Sonuç `InfoBar` ("12,4 GB temizlendi · Geri Dönüşüm Kutusu'ndan geri alınabilir" + Etkinlik bağlantısı).

### 3.5 Depolama (Disk Haritası · Büyük dosyalar · Yinelenenler/Boş klasörler)
- **Şimdi:** 1556 satır; 3 sekme; her sekmede kendi araç kartı; 7 birincil, 6 tehlike düğmesi; radar ışını;
  108 serbest boşluk; "Yinelenen Dosya Avcısı", "Harika!".
- **Sorunlar:** G1, G4 (en kötü sayfa), G5, G6, G10, G11, G12.
- **Hedef:** SelectorBar (Disk haritası · Büyük dosyalar · Yinelenenler · Boş klasörler — 4 sekme, bugünkü
  3. sekme ikiye ayrılır). Her sekme aynı iskelet: CommandBar (sürücü seçimi, eşik/tür çipleri, arama,
  sırala) → ProgressHeader → sonuç tablosu → seçim çubuğu ("14 dosya seçili · 3,2 GB · [Geri Dönüşüm
  Kutusu'na taşı]" tek birincil). Silme onayı `Dialog.Shell` içinde, kırmızı yalnızca orada. Disk haritası:
  treemap renkleri `Chart.Series`, kutu köşeleri 4 px, gezinme yolu Fluent `BreadcrumbBar` görünümünde.
  Dosya sınıfı başına tek dosya: 1556 → ~4 × 250 satır (UserControl'lere bölünür).

### 3.6 Süreçler
- **Şimdi:** Bellek mimarisi kartı (4 renkli çubuk + gösterge + 4 KPI + "Progress Beam"), süreç tablosu +
  sağ çekmece; "Canlı İzleme" başlığı iki kez.
- **Sorunlar:** G2, G3 (çubuk renkleri serbest), G6 ("RAMMap" gibi teknik jargon), G7, G8.
- **Hedef:** Üstte **bellek şeridi**: tek yatay yığılmış çubuk (Kullanımda / Değiştirilmiş / Bekleme /
  Boş; renkler `Chart.Series1-4`), altında gösterge ve tek birincil "Belleği boşalt" (yönetici değilse
  ipuçlu devre dışı). Tablo kiti: ad + simge, CPU, bellek, disk, ağ sütunları; uygulamaya göre gruplama
  (`CardExpander` yerine ListView grup başlıkları). Ayrıntı `DetailsPane`: yol, imza, komut satırı,
  "Sonlandır" (onaylı), "Askıya al", "Dosya konumunu aç".

### 3.7 Başlangıç
- **Şimdi:** 3 KPI + son açılış kartı, filtre çipleri, tablo + "Başlatıcı Röntgeni".
- **Sorunlar:** G2, G6 ("Açılışı Hızlandır" ipucu "tek tıkla optimize"), G7, G8.
- **Hedef:** Başlık altında **açılış süresi kartı** (Windows ölçümü, son 5 açılışın düz çubuk grafiği)
  — tek anlamlı sayı. Liste, Windows 11 Ayarlar > Uygulamalar > Başlangıç gibi: satırda simge, ad,
  yayıncı, etki rozeti, **ToggleSwitch**. Filtre çipleri (Tümü · Etkin · Devre dışı · Yüksek etki).
  Ayrıntı `DetailsPane` (konum, komut, imza, "Konumu aç", "Kaldır").

### 3.8 Hizmetler ve sürücüler
- **Şimdi:** İki sekme, sekme başına 4 KPI (8 kart), önerilen profiller, elle yazılmış tablo başlığı.
- **Sorunlar:** G2, G7, G10 (sekmeler dolu düğme), "PnP Çekirdek Aktif" gibi teknik başlıklar.
- **Hedef:** SelectorBar (Hizmetler · Sürücüler); SummaryStrip (çalışan · otomatik · devre dışı ·
  "güvenle kapatılabilir"); **önerilen profiller** üstte tek `CardExpander` ("3 profil öneriliyor");
  tablo kiti; başlangıç türü satırda açılır kutu; güvenlik rozeti StatusBadge.

### 3.9 Oyun Modu (referans, küçük rötuşlar)
- Hedef: sayfa kalıbına tam uyum (SectionHeader eylemi yerine durum kartındaki düğme kalır), ayar
  kartlarının `Card.Setting` ile aynı yüksekliğe getirilmesi (askıya alma/otomatik başlatma kartları
  `ui:CardExpander`'a çevrilir).

### 3.10 Analizör (Tarama · Geçmiş · Değişiklikler)
- **Şimdi:** Canlı VirusTotal göstergesi, 3 sekme (dolu düğme), 5 KPI kartı, kategori çipleri, 6 sütunlu
  satırlar (ToggleSwitch, simge, ad/yol, kategori, imza, VT skoru), "Tümünü Derinlemesine Analiz Et".
- **Sorunlar:** G2 (5 kart), G6, G7, G10; satırda iki ayrı kalkan simgesi (test bunun için var);
  "Sysinternals'ı gizle" dili teknik.
- **Hedef:** SelectorBar; SummaryStrip (toplam · imzasız · VT tespitli · devre dışı); CommandBar (arama,
  kategori çipleri, "Microsoft öğelerini gizle" ToggleChip, `⋯` içinde "VirusTotal anahtarı", "CSV dışa
  aktar"); tablo kiti; risk sütunu tek `RiskBadge`; ayrıntı `DetailsPane` (imza, karma, VT, "Devre dışı
  bırak", "Dosya analizini aç"). Geçmiş ve Değişiklikler panelleri aynı tablo kitine.

### 3.11 Kurulum Nöbetçisi
- **Şimdi:** Durum kartı, 4 özet, arama, geçmiş listesi (190 satır, en temiz sayfalardan).
- **Hedef:** Oyun Modu gibi **durum kartı** (İzleniyor / Kapalı + tek eylem + son kurulum); özet →
  SummaryStrip; geçmiş satırları karar rozetiyle; rapor açma `Dialog.Shell` içindeki rapor görünümüne.

### 3.12 Ağ İzleyici
- **Şimdi:** 1398 satır; "Ağ & Bağlantı Merkezi"; 4 KPI; 5 sekme; bağlantı tablosu + "Bağlantı & Süreç
  Röntgeni"; "1000 Mbps Ultra Gigabit Hız Testi"; tanılama araçları 5 ayrı büyük kart; 24 vurgu metni,
  67 Büyük Harfli başlık.
- **Sorunlar:** G2, G3 (en kötü), G4, G6 (en kötü), G7, G8, G10, G12.
- **Hedef:** Başlık "Ağ İzleyici"; SelectorBar (Bağlantılar · Dinleyen portlar · Bağdaştırıcılar ·
  Hız testi · Tanılama). Bağlantılar: CommandBar (arama, TCP/UDP/Dış/Şüpheli çipleri, otomatik yenile
  ToggleSwitch), tablo kiti, `DetailsPane`. Hız testi: Windows 11 benzeri tek büyük gösterge (indirme Mbps)
  + ping/jitter satırı, tek birincil "Testi başlat". Tanılama: **ayar kartı listesi** (Ping, DNS önbelleğini
  temizle, Port denetimi, Rota izleme, DNS karşılaştırma) — her biri `CardExpander`, içinde form ve sonuç.
  Güvenlik duvarı kuralları tablo kiti. Dosya 5 UserControl'e bölünür.

### 3.13 Kaldırıcı
- **Şimdi:** "Avcı Modu", "Kurulum İzle", geçmiş, yenile başlık düğmeleri; 4 tıklanabilir KPI; 2 katmanlı
  araç çubuğu; uygulama listesi / kalıntı görünümü.
- **Sorunlar:** G2 (KPI'lar filtre olarak da çalışıyor — iki iş bir yerde), G6 ("Avcı Modu"), G7.
- **Hedef:** Windows 11 Ayarlar > Yüklü uygulamalar düzeni: başlıkta arama + sırala + filtre; satırda
  simge, ad, yayıncı · sürüm · tarih, boyut, `⋯` menüsü (Kaldır / Değiştir / Kalıntıları tara / Konumu aç).
  KPI'lar yerine filtre çipleri (Uygulamalar · Sistem bileşenleri · Kalıntı var). "Avcı Modu" →
  **"Pencereden seç"** (standart düğme, ipucunda açıklama). Kaldırma akışı `Dialog.Shell` sihirbazı (§3.21).

### 3.14 Mağaza (Katalog · Hazır paketler · Güncellemeler)
- **Şimdi:** "Yazılım & Runtimes Mağazası"; 3 sekme + metrik şeridi; kategori hapları; ızgara/liste
  görünümü; alt konsol çekmecesi; 6 birincil düğme; "Format Sonrası Can Kurtaran: All-in-One".
- **Sorunlar:** G4, G6, G10.
- **Hedef:** Microsoft Store / winget benzeri: SelectorBar; katalog kartları 4 px köşe, simge 32 px, ad,
  kısa açıklama, "Kur" standart düğme (kuruluysa "Kurulu" rozeti); seçimli toplu kurulum için alt **seçim
  çubuğu** (tek birincil "3 uygulamayı kur"). Kuyruk ve konsol: `DetailsPane` benzeri alt panel
  ("İndirmeler" başlığı, her öğede ProgressBar). Hazır paketler: 4 kart, sade adlar ("Temel çalışma
  zamanları", "Oyuncu", "Ofis", "Geliştirici").

### 3.15 Sistem Bilgisi (Donanım · S.M.A.R.T.)
- **Şimdi:** 810 satır, 25 vurgu metni, donanım kartları, disk kartları.
- **Sorunlar:** G3, G6, G10, G12.
- **Hedef:** Windows 11 Ayarlar > Sistem > Hakkında düzeni: üstte cihaz kartı (cihaz adı, işlemci,
  bellek, Windows sürümü, "Kopyala" düğmesi); altta **ayar kartı listesi** biçiminde bölümler (İşlemci,
  Bellek, Ekran kartı, Anakart, Depolama, Ağ) — her biri `CardExpander`, içinde `KeyValueGrid`.
  S.M.A.R.T.: disk başına kart, sağlık rozeti + sıcaklık + kalan ömür; öznitelik tablosu tablo kitiyle.
  Vurgu rengi yalnızca uyarı durumlarında.

### 3.16 Olaylar ve çökmeler
- **Şimdi:** "Olay Günlüğü & Sistem Sağlığı"; 4 KPI (güvenilirlik endeksi dahil); alt sekmeler;
  çökme listesi; olay filtresi; SFC/DISM onarım kartları.
- **Hedef:** SelectorBar (Mavi ekranlar · Olaylar · Onarım); üstte **güvenilirlik grafiği** (son 30 gün,
  düz çizgi, `Chart.Series1`) tek kartta; çökme satırları tablo kitiyle, ayrıntıda hata kodu açıklaması;
  Onarım sekmesi ayar kartları (SFC, DISM) — her birinde durum, son çalışma zamanı, tek "Çalıştır".

### 3.17 Windows Ayarları (İnce ayarlar + Gizlilik)
- **Şimdi:** "Windows Tweaker & İnce Ayarlar"; 4 KPI; kategori menüsü + ayar listesi; "Önbellek Sıfırlama
  Motoru (Reset Cache Engine)", "OEM Bilgileri & Kayıtlı Kullanıcı Değiştirici"; Gizlilik alt modülü ayrı
  4 KPI + kart listesi; 5 birincil, 3 tehlike düğmesi; 12 İngilizce parantez.
- **Sorunlar:** G2, G4, G6 (en kötü), kategori menüsü Ayarlar sayfasındaki Fluent menüden farklı.
- **Hedef:** Tam **Windows 11 Ayarlar** klonu: solda `Nav.ItemButton` kategori menüsü (Ayarlar sayfasıyla
  aynı), sağda her ince ayar `Card.Setting` (başlık, açıklama, risk rozeti, ToggleSwitch / açılır kutu);
  geri alınabilirlik rozetle. Araçlar (önbellek sıfırlama, TrustedInstaller çalıştırma, GPO sıfırlama)
  "Araçlar" kategorisinde `Card.Setting.Action` kartları, onay `Dialog.Shell`'de. Gizlilik ve
  Uygulama kaldırma (bloatware) aynı menüde iki kategori; KPI'lar kalkar, üstte tek satır "12 ayar
  önerilen durumda değil · [Önerilenleri uygula]".

### 3.18 Windows Araçları
- **Şimdi:** "Klasik Windows Fotoğraf Görüntüleyicisi (Photo Viewer)" kahraman afişi; kategori hapları;
  2 sütunlu araç ızgarası.
- **Hedef:** Afiş → ilk sırada tek `Card.Setting` (ToggleSwitch ile etkinleştir). Araçlar Windows 11
  "Windows Araçları" gibi: 3–4 sütunlu kutucuk ızgarası (32 px simge, ad, tek satır açıklama; tüm kutucuk
  tıklanabilir, `Card.Setting.Action` hover'ı). Kategori çipleri + arama.

### 3.19 Ayarlar
- **Şimdi (v4.2):** Fluent kategori menüsü ve ayar kartları.
- **Kalan:** Kategori başlıkları hâlâ vurgu simgeli + Subtitle; tema kartları dolu vurgu; "Hakkında"
  bölümü yoğun (4 sütunlu meta ızgarası, sürüm geçmişi akordiyonu); İngilizce parantezler.
- **Hedef:** Kategori başlığı = sayfa alt başlığı (Subtitle 20, simgesiz); tema seçimi Windows 11
  Kişiselleştirme gibi **önizlemeli kutucuklar** (küçük pencere önizlemesi, seçili olanda vurgu çerçevesi);
  Hakkında: uygulama kartı (simge, sürüm, "Güncellemeleri denetle" birincil), "Sürüm notları"
  `CardExpander`, günlük eylemleri `Card.Setting.Action`.

### 3.20 Giriş penceresi
- **Hedef:** FluentWindow + Mica (var); ortada 360 px form kartı, uygulama simgesi 48 px, Title 28 SB,
  alanlar `ui:TextBox`/`ui:PasswordBox`, tek birincil "Giriş yap", hata `InfoBar`; tema düğmesi başlık
  çubuğuna.

### 3.21 Pencereler ve diyaloglar (G9)

| Pencere | Hedef |
|---|---|
| Dosya analizi (ThreatAnalysisDialog, 1275 satır) | `Dialog.Shell` + solda bölüm menüsü (Özet · İmza · İçe aktarmalar · Dizeler · VirusTotal), sağda `KeyValueGrid`/tablo; "Dosya Röntgeni" → "Dosya analizi" |
| Kurulum değişiklikleri raporu | Aynı kabuk; karar rozeti başlıkta; değişiklik türleri SelectorBar; "Geri al" birincil |
| Kalıntı temizleme / Derin kaldırma sihirbazı | Tek sihirbaz kabuğu: üstte adım göstergesi (1 Kaldır · 2 Tara · 3 Seç · 4 Temizle), altta Geri / İleri (birincil) / İptal; `Topmost` kaldırılır |
| Güncelleme | ContentDialog: "Bakım 4.3 hazır", sürüm notları listesi, "Şimdi güncelle" birincil / "Sonra" |
| Avcı Modu (3 pencere) | "Pencereden seç": Windows'un ekran alıntısı aracı gibi yarı saydam kaplama + imleç altında tek satır bilgi; sonra ContentDialog eylem listesi. Neon çerçeve → 2 px vurgu çerçeve |
| Kurulum algılandı bildirimi | Windows bildirim (toast) görünümü: sağ alt, 360 px, 8 px köşe, sistem gölgesi; `Topmost` burada kalır |
| Tepsi menüsü | Windows 11 Hızlı Ayarlar görünümü: Mica/Acrylic, 8 px köşe, üstte durum kutucukları (Oyun Modu, Nöbetçi) ToggleButton kutucuk olarak |
| Seçim diyaloğu (PickListDialog) | Referans kabuk — diğerleri bundan türetilir |

---

## 4. Fazlar, sıra, boyut

| Faz | İçerik | Boyut | Bağımlılık |
|---|---|---|---|
| **A** | Ortak bileşenler (SelectorBar, ToggleChip, SummaryStrip, CommandBar, tablo kiti, DetailsPane, ProgressHeader, Dialog.Shell, EmptyState türleri), kurallar ve yeni cırcır sayaçları | L (3–4 gün) | — |
| **B** | En çok görülen sayfalar: Kontrol Paneli, Temizleyici, Süreçler, Başlangıç | L | A |
| **C** | Liste sayfaları (tablo kiti + ayrıntı paneli): Hizmetler, Kaldırıcı, Analizör (+Geçmiş/Değişiklikler), Etkinlik Merkezi, Olaylar | L | A |
| **D** | Büyük sayfalar (bölünerek): Depolama, Ağ İzleyici, Mağaza, Sistem Bilgisi | XL | A, C |
| **E** | Ayar düzenli sayfalar: Windows Ayarları + Gizlilik, Windows Araçları, Nöbetçi, Ayarlar rötuşu, Oyun Modu rötuşu | M | A |
| **F** | Pencereler ve diyaloglar `Dialog.Shell`'e; Avcı Modu → Pencereden seç; tepsi | L | A |
| **G** | Metin geçişi: cümle düzeni, "&", İngilizce parantezler, metaforlar (sözlükle, betik destekli + elle gözden geçirme) | M | B–F ile paralel |
| **H** | Kalite: cırcır eşikleri, kenar çubuğu gruplaması, tema matrisi (5 tema), 1020/1280/1920 px, %100–150 ölçek, klavye ve Narrator, ekran görüntüleri `docs/screens/` | M | her faz sonunda |

Her faz ayrı commit dizisi; her sayfa kendi commit'i. Büyük dosyalar (Depolama 1556, Ağ 1398, Analiz
diyaloğu 1275, Ayarlar 1458, Mağaza 1129 satır) sekme başına UserControl'e bölünür.

---

## 5. Başarı ölçütü

| Ölçüt | Şimdi | Hedef |
|---|---|---|
| Görünüm başına birincil düğme | 7'ye kadar (toplam 52) | ≤ 1 |
| Diyalog dışı tehlike düğmesi | 26 | 0 |
| "Success"/"Caution" düğme görünümü | 5 | 0 |
| Sonsuz dekoratif animasyon | 2 | 0 |
| Gradyan fırça (Mica dışı) | 4 | 0 |
| 4'lü KPI kartı satırı | 13 sayfa | 0 (özet şeridi yalnızca gereken yerde) |
| Büyük Harfli başlık | 622 | 0 |
| "&" içeren metin | ~80 | 0 |
| Kendi kabuğunu çizen pencere | 9 | 1 (kurulum bildirimi) |
| Elle yazılmış tablo başlığı | 8+ | 0 (tablo kiti) |
| Ayrıntı paneli uygulaması | 5 farklı | 1 bileşen |
| Serbest Margin/Padding | ~1.300 | cırcırla sürekli azalan |
| En uzun görünüm dosyası | 1556 satır | < 500 |

---

## 6. Durum (26.09.2026, `claude/awesome-pascal-am378r`)

Uygulandı (her madde ayrı commit; görsel doğrulama Windows'ta yapılacak):

| Alan | Yapılan |
|---|---|
| Ortak | `SummaryStrip`, `Tab.Item` (SelectorBar), `Table.HeaderRow`, `Progress.Header`, `EqualsToBool`, `PositiveToBool`; `IntToVis` parametresiz kullanımda artık "0'dan büyükse görünür" (önceden her zaman gizliydi) |
| Kontrol Paneli, Etkinlik, Temizleyici, Süreçler | §3.2–3.6: özet şeridi, düz sparkline, tek birincil eylem, komut satırı filtreleri |
| Başlangıç | Açılış süresi kartı + son açılışlar grafiği; KPI kartları kalktı |
| Olaylar | Sağlık + 30 günlük güvenilirlik tek kartta; SelectorBar; filtre çiplerine seçili durum |
| Depolama | 1519 satır → kabuk + 3 panel; 4 sekme (Boş klasörler ayrı); komut satırı; EmptyState; tablo satırları |
| Ağ İzleyici | 1379 satır → kabuk + 5 panel; tanılama açılır ayar kartları; hız testi InfoBar |
| Mağaza | SelectorBar, nötr kartlar/rozetler, sabit RGB kategori renkleri kaldırıldı |
| Sistem Bilgisi | Hakkında düzeni: cihaz kartı + açılır donanım bölümleri, Aygıt güvenliği, İlgili bağlantılar; disk sağlığı rozeti gerçek durumu gösteriyor |
| Windows Ayarları + Gizlilik | Nav.ItemButton kategori menüsü, ayar kartları, `⋯` menüsü, KPI'lar kalktı |
| Windows Araçları | Ayar kartı + 3 sütunlu tıklanabilir kutucuklar |
| Kaldırıcı, Nöbetçi | Yüklü uygulamalar düzeni: filtre çipleri, sıralama açılır kutusu, nötr rozetler |
| Kabuk | 5 grup kenar çubuğu; komut paleti Fluent açılır yüzey; tepsi menüsü Hızlı Ayarlar kutucukları |
| Pencereler | 5 diyalog FluentWindow + Mica (G9); kaplama ve bildirim pencereleri bilerek saydam kaldı |
| Metin | Cümle düzeni dedektörü bağlaçları atlıyor; komut paleti adları sayfa adlarıyla aynı |
| Araçlar | `Tools/*.py` Windows cp1252 konsolunda Türkçe karakterle çökmüyor (stdout UTF-8) |

Ölçüler (başarı ölçütü §5):

| Ölçüt | Başlangıç | Şimdi |
|---|---|---|
| Diyalog dışı tehlike düğmesi | 26 | 0 (cırcır) |
| Success/Caution düğme | 5 | 0 (cırcır) |
| Sonsuz animasyon / gradyan | 2 / 4 | 0 / 0 (cırcır) |
| Büyük Harfli metin, "&" | 622 / ~80 | 0 / 0 (cırcır) |
| Kendi kabuğunu çizen diyalog | 9 | 0 (yalnızca kaplama/bildirim pencereleri) |
| En uzun görünüm | 1556 | 1422 (Ayarlar; Depolama ve Ağ bölündü) |
| Serbest köşe yarıçapı | 11 | 6 |

Kalan: Ayarlar'ın önizlemeli tema kutucukları ve dosyanın bölünmesi (§3.19), Analiz diyaloğunun bölüm
menüsü (§3.21), ayrıntı panellerinin tek `DetailsPane` bileşenine toplanması, 5 tema × 3 genişlik ekran
görüntüsü matrisi (Faz H — Windows gerekir).
