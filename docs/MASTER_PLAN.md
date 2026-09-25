# Bakım 4.0 Master Plan

> **Tasarım, her modülün geliştirilmesi, hata ve güvenlik düzeltmeleri, Analizör Geçmişi ve Etkinlik Merkezi.**
>
> Bu belge projenin **tek yol haritasıdır.** Önceki üç belge alt planlar olarak geçerliliğini korur ve burada kodlarıyla anılır:
>
> | Belge | Kapsam | Buradaki kısaltma |
> |---|---|---|
> | `docs/GENEL_DENETIM_PLANI.md` | Tüm modüllerin denetimi (G-*, D-*, H-*, P-*, M-* kodları) | **DEN** |
> | `docs/UNINSTALLER_V2_PLAN.md` | Kaldırıcı ve sağ tıkla kaldırma (A*, B*, C*, D*, E* görevleri) | **KAL** |
> | `docs/SENTINEL_V2_PLAN.md` | Kurulum Nöbetçisi (Faz 0–10) | **NÖB** |
>
> **Uygulama modeli:** Kodu Claude yazar. Her faz ayrı bir branch'te geliştirilir; her adım Linux'ta derlenerek (`dotnet build -p:EnableWindowsTargeting=true`) ve Linux'ta koşabilen saf mantık testleriyle doğrulanır, sonra GitHub'a push'lanır. Proje sahibi `git pull` ile kendi PC'sine alır, Windows'ta çalıştırıp her fazın sonundaki **PC test listesini** uygular. Liste temiz geçince PR main'e merge edilir.
>
> **Ürün kararları (değişmez):**
> - Güncelleyicide imza ve sertifika kontrolü **yapılmayacak** (DEN G-1).
> - Uygulama dili Türkçe; arayüzde emoji kullanılmaz.

---

## İçindekiler

0. [Durum özeti](#0-durum-özeti)
1. [Vizyon ve ilkeler](#1-vizyon-ve-ilkeler)
2. [Bilgi mimarisi v2 (yeni navigasyon)](#2-bilgi-mimarisi-v2)
3. [Tasarım sistemi v2](#3-tasarım-sistemi-v2)
4. [Ortak çekirdek (altyapı)](#4-ortak-çekirdek)
5. [Modül planları (21 modül)](#5-modül-planları)
6. [Analizör Geçmişi (yeni modül, tam spesifikasyon)](#6-analizör-geçmişi)
7. [Etkinlik Merkezi (tüm geçmişlerin tek çatısı)](#7-etkinlik-merkezi)
8. [Birleşik hata ve güvenlik kataloğu](#8-birleşik-hata-kataloğu)
9. [Fazlar ve sürümler](#9-fazlar-ve-sürümler)
10. [Test stratejisi ve PC test listeleri](#10-test-stratejisi)
11. [Karar bekleyen konular](#11-karar-bekleyen-konular)
12. [İlerleme tablosu](#12-ilerleme-tablosu)

---

## 0. Durum özeti

**Güçlü yanlar:**
- Geniş özellik yelpazesi: 14 modül, tepsi, komut paleti, avcı modu, nöbetçi.
- DI altyapısı, `AppModuleRegistry`, WPF-UI/Fluent tabanı.
- Tasarım token'ları başlatılmış (`Themes/Tokens`: Palette, Typography, Spacing, Motion).
- Bileşen kütüphanesinin çekirdeği hazır (`Controls/`: SectionHeader, StatCard, MetricChip, StatusBadge, EmptyState).
- Sağlam bir tehdit analiz motoru ve Autoruns motoru.

**Ana sorunlar:**
1. **Güven:**
   - Kaldırıcı başka programların verisini silebiliyor.
   - Temizleyici güvenli hedefleri alt dize eşleşmesiyle belirliyor.
   - Mağaza doğrulanmamış üçüncü taraf ikilileri yönetici olarak çalıştırıyor.
   - "Yönetici kısayolu" özelliği UAC'yi atlatmaya açık.
2. **Dürüstlük:**
   - CPU ve GPU sıcaklığı uyduruluyor.
   - "Tek tık hızlandır" hiçbir şey boşaltmasa bile **450 MB** gösteriyor (`DashboardViewModel.cs:342`).
   - RAM fonksiyonları başarısız olsa bile **50 ve 100 MB** döndürüyor (`SystemCleanService.cs:579`, `:610`, `:641`).
   - İnce ayarlar yazılamasa bile "uygulandı" diyor.
   - Görsel etki için eklenmiş 11 yapay bekleme (`Task.Delay`) var.
3. **Hatalar:** 32 bit başlangıç girdileri, Firefox temizliği, Oyun Modu güç planı, handle sızıntısı ve benzerleri (§8).
4. **Bilgi mimarisi:**
   - Yinelenen Dosya ve Boş Klasör araçları "Sistem Bilgisi" içinde.
   - Büyük Dosyalar da orada.
   - Klasik Windows araçları "Mağaza" içinde.
   - Gizlilik ayrı bir modül ama Tweaker'a yönlendiriliyor.
   - Kurulum ve Analizör geçmişi hiç yok.
5. **Tasarım tutarlılığı:**
   - 1.200+ sabit `FontSize` değeri; en sık 10–11 px.
   - 150'yi aşkın sabit hex renk; açık temada kontrast bozuluyor.
   - Her modül kendi düzenini icat etmiş.
6. **Mimari:**
   - ≈150 `catch { }` ile yutulan hata.
   - Çift motorlar (iki güncelleyici, iki başlangıç motoru, üç kaldırma motoru).
   - ViewModel'lerde 120+ `MessageBox` çağrısı.
   - Testler CI'da çalıştırılmıyor.

---

## 1. Vizyon ve ilkeler

**Vizyon:** Windows için **güvenilir** bir bakım ve güvenlik merkezi. Kullanıcı sistemine ne olduğunu görür, her değişikliği anlar ve geri alabilir.

Aşağıdaki beş ilke bütün modüller için bağlayıcıdır. Tasarım incelemesinde her ekran bu listeyle kontrol edilir.

| # | İlke | Pratikte |
|---|---|---|
| 1 | **Önce güvenlik** | Silme ve sonlandırma işlemleri yalnızca `Safe*` servislerinden geçer. Belirsiz durumda silinmez, sorulur. |
| 2 | **Dürüstlük** | Ölçülmeyen değer gösterilmez ("—"). Tahmin edilen değer "≈" ve "tahmini" etiketi taşır. Başarısızlık açıkça söylenir. Yapay bekleme ve uydurma sayı yoktur. |
| 3 | **Geri alınabilirlik** | Her değişiklik Etkinlik Merkezi'ne kaydedilir. Mümkün olan her işlemin "Geri al" düğmesi vardır; geri alınamayan işlem önceden **"Geri alınamaz"** rozetiyle gösterilir. |
| 4 | **Sessizlik** | Arka plan işleri kaynak bütçesine uyar (boşta CPU < %0,5). Bildirimler odak çalmaz, oyun ve sunum sırasında ertelenir. |
| 5 | **Anlaşılırlık** | Her ekran tek bir birincil eyleme sahiptir. Teknik terimlerin yanında Türkçe açıklama bulunur. "Neden?" sorusunun cevabı her bulguda yer alır. |

---

## 2. Bilgi mimarisi v2

### 2.1 Yeni navigasyon

Kenar çubuğu gruplu hale gelir. Daraltılmış modda yalnızca simgeler görünür; grup başlıkları ayırıcı çizgiye dönüşür.

```
┌───────────────────────────────┐
│  Bakım                        │
│                               │
│  GENEL BAKIŞ                  │
│   ▣ Kontrol Paneli            │
│   ◷ Etkinlik Merkezi   (YENİ) │  ← tüm geçmişler + geri al
│                               │
│  TEMİZLİK & DEPOLAMA          │
│   ✦ Temizleyici               │
│   ▤ Depolama           (YENİ) │  ← Büyük Dosyalar + Yinelenenler + Boş Klasörler + Disk Haritası
│                               │
│  PERFORMANS                   │
│   ⚡ Süreçler (Optimizer)      │
│   ⏻ Başlangıç                 │
│   ⚙ Hizmetler & Sürücüler     │
│   ◉ Oyun Modu          (ayrı) │
│                               │
│  GÜVENLİK                     │
│   ⌕ Analizör                  │  ← Tarama | Geçmiş (YENİ) | Değişiklikler (YENİ)
│   ⛨ Kurulum Nöbetçisi  (YENİ) │  ← Kurulum Geçmişi + ayarlar
│   ⇅ Ağ İzleyici               │
│                               │
│  YAZILIM                      │
│   ⊟ Kaldırıcı                 │
│   ⬇ Mağaza                    │
│                               │
│  SİSTEM                       │
│   ⓘ Sistem Bilgisi            │
│   ⚠ Olaylar & Çökmeler        │
│   ✎ Windows Ayarları (Tweaker)│  ← Gizlilik alt kategorisi burada
│   ⌘ Windows Araçları   (taşındı: Mağaza'dan) │
│                               │
│  ─────────────                │
│   ⚙ Ayarlar                   │
│   [Nöbetçi: Aktif ● ]         │  ← alt durum kartı
└───────────────────────────────┘
```

Simgeler yalnızca şema içindir. Uygulamada `SymbolRegular` kullanılır: `Board24`, `History24`, `Broom24`, `HardDrive24`, `DataUsage24`, `Rocket24`, `Settings24`, `Games24`, `DocumentSearch24`, `ShieldCheckmark24`, `NetworkCheck24`, `AppsList24`, `ArrowDownload24`, `Info24`, `Warning24`, `Wrench24`, `Toolbox24`. Her sembol `verify-symbols.py` ile doğrulanır.

### 2.2 Taşımalar ve yeni modüller

| Değişiklik | Neden | Teknik not |
|---|---|---|
| Büyük Dosyalar, Yinelenenler ve Boş Klasörler **Sistem Bilgisi'nden Depolama'ya** taşınır | Sistem Bilgisi salt okunur bir ekran olmalı; dosya silen araçlar orada beklenmiyor | `SystemInfoViewModel`'den `StorageViewModel` ayrılır (≈700 satır azalır) |
| Klasik Windows Araçları **Mağaza'dan "Windows Araçları"na** taşınır | Mağaza yazılım yükler; mmc, regedit ve benzerlerini açmak ayrı bir iş | `ClassicAppsService` + `SystemToolsService` birleşir |
| **Etkinlik Merkezi** (yeni) | Tüm geçmişler ve geri al tek yerde | §7 |
| **Analizör Geçmişi** (yeni sekme) | Yapılan analizler kaybolmasın, karşılaştırılabilsin | §6 |
| **Kurulum Nöbetçisi** (yeni sayfa) | Kurulum geçmişi ve nöbetçi ayarları bir yerde toplansın | NÖB Faz 6 |
| **Oyun Modu** ayrı sayfa | Şu an Kontrol Paneli'nde gömülü bir kart; profil, uygulama listesi ve otomatik tetikleme için yer lazım | §5.5 |
| Gizlilik, Tweaker'ın alt kategorisi olarak **resmîleşir** | `AppModule.PrivacyDebloat` yönlendirmesi zaten var; ayrı nav öğesi kaldırılır | `MainViewModel.Navigate` özel durumu silinir |

### 2.3 Navigasyon davranışı

- **Son sayfa hatırlanır.** Açılışta son açık modül yüklenir (ayar: "Her zaman Kontrol Paneli ile aç").
- **Derin bağlantılar:** Her modül `Navigate("Analyzer/History?sha256=…")` biçiminde alt sayfa ve parametre alabilir. Komut paleti, bildirimler ve Etkinlik Merkezi bu bağlantıları kullanır. `AppModuleRegistry`'ye yol ve parametre ayrıştırma eklenir.
- **Geri/ileri:** Alt+Sol ve Alt+Sağ ile modüller arası gezinme geçmişi.
- **Rozetler:** Nav öğelerinde sayaç rozetleri: Analizör'de "3 yeni başlangıç girdisi", Nöbetçi'de "1 riskli kurulum", Etkinlik Merkezi'nde okunmamış olay sayısı.

---

## 3. Tasarım sistemi v2

Mevcut token dosyaları genişletilir ve **zorunlu** hale gelir. Doğrulayıcılar CI'da kuralları uygular.

### 3.1 Renk

**Katmanlar:** Primitive (ham palet) → Semantic (anlam) → Component (bileşen). XAML yalnızca **Semantic** anahtarları kullanır.

**Semantic anahtar seti.** Mevcut `Brush.*` anahtarları korunur ve genişletilir:

| Grup | Anahtarlar |
|---|---|
| Yüzey | `Surface.Base`, `Surface.Raised`, `Surface.Overlay`, `Surface.Sunken`, `Surface.Hover`, `Surface.Pressed`, `Surface.Selected` |
| Kenarlık | `Border.Subtle`, `Border.Default`, `Border.Strong`, `Border.Focus` |
| Metin | `Text.Primary`, `Text.Secondary`, `Text.Tertiary`, `Text.Disabled`, `Text.OnAccent`, `Text.Link` |
| Durum (her biri Solid, Subtle, Text, Border) | `Status.Success`, `Status.Caution`, `Status.Critical`, `Status.Info`, `Status.Neutral` |
| Risk (Analizör, Nöbetçi, Kaldırıcı) | `Risk.Clean`, `Risk.Low`, `Risk.Medium`, `Risk.High`, `Risk.Critical` |
| Veri görselleştirme | `Chart.Series1..6`, `Chart.Grid`, `Chart.Axis` (renk körlüğü dostu sıra; CPU, RAM, Disk ve Ağ için sabit atama) |

**Temalar:**
- Mica Koyu (varsayılan)
- Koyu (düz)
- AMOLED
- Açık
- **Yüksek Kontrast** (yeni; `SystemParameters.HighContrast` açıksa otomatik; sistem renkleri kullanılır)

Mevcut "Cyberpunk" teması kalır, ancak kontrast testinden geçmesi şart.

**Vurgu rengi:** Windows vurgu rengini takip etme seçeneği (`UISettings.GetColorValue(Accent)` ya da `DwmGetColorizationColor`).

**Kurallar:**
- Modellerde ve ViewModel'lerde renk **yok**. Modeller `Intent`/`RiskLevel` gibi anlamsal değerler döner, renge XAML'de karar verilir. Mevcut `Models/Intent.cs` bu yaklaşımın temelidir.
- Her metin ve arka plan çifti WCAG AA sağlar: gövde metni 4,5:1, büyük metin 3:1. `ThemeContrastTests` tüm temalar ve tüm Semantic çiftler için genişletilir.

### 3.2 Tipografi

- **Font:** Segoe UI Variable (Windows 11), yoksa Segoe UI. Rakamlar için `Typography.NumeralAlignment="Tabular"` kullanılır; KPI ve tablolarda sayılar kaymaz.
- **Yeni ölçek:** Minimum okunur boyut 12 px. 10 ve 11 px yalnızca `Font.Micro` (rozet) için kalır.

| Anahtar | Boyut / Ağırlık | Kullanım |
|---|---|---|
| `Font.Micro` | 11 / SemiBold | Rozet, etiket |
| `Font.Caption` | 12 / Regular | Meta veri, tablo ikinci satırı |
| `Font.Body` | 14 / Regular | Gövde (Windows 11 standardı) |
| `Font.BodyStrong` | 14 / SemiBold | Liste öğesi başlığı |
| `Font.Subtitle` | 16 / SemiBold | Kart başlığı |
| `Font.Title` | 20 / SemiBold | Bölüm başlığı |
| `Font.TitleLarge` | 28 / SemiBold | Sayfa başlığı |
| `Font.Display` | 40 / SemiBold | KPI, skor |

- **Hazır stiller:** `Text.PageTitle`, `Text.SectionTitle`, `Text.CardTitle`, `Text.Body`, `Text.Secondary`, `Text.Caption`, `Text.Mono` (hash, yol, registry için Cascadia Mono / Consolas), `Text.KPI`.
- **Geçiş:** Mevcut 1.200+ sabit `FontSize` değeri betikle (`Tools/migrate-font-sizes.py`) stillere çevrilir. 10 ve 11 px'lik gövde metni 12 ya da 14'e yükselir. Görsel inceleme PC test listesinde.

### 3.3 Boşluk, yarıçap, yükselti, hareket

- **Boşluk:** 4 px ızgara, mevcut `Space.*`/`Pad.*`/`Gap.*`. Modül dış dolgusu 24, kartlar arası 12, kart içi 16–20.
- **Yarıçap:**
  - `Radius.SM` = 4 (rozet)
  - `Radius.MD` = 8 (kart, kontrol; Windows 11)
  - `Radius.LG` = 12 (diyalog, flyout)
- **Yükselti:** Yalnızca `Surface.Overlay` (flyout, diyalog, komut paleti) gölge alır. Kartlar gölge yerine kenarlıkla ayrılır.
- **Hareket:**
  - Süreler: `Motion.Fast` 120 ms, `Motion.Normal` 200 ms, `Motion.Slow` 300 ms.
  - Eğri: CubicEaseOut.
  - **"Animasyonları azalt"** ayarı ve Windows animasyon ayarı (`SystemParameters.ClientAreaAnimation`) açıkken bütün animasyonlar 0 ms olur.
  - İlerleme göstergeleri **gerçek ilerlemeyi** gösterir. Bilinmiyorsa belirsiz (indeterminate) çubuk kullanılır; sahte yüzde yoktur.

### 3.4 Simgeler

- Yalnızca Fluent `SymbolRegular`. Boyutlar `Icon.*` ölçeğinden. Seçili nav öğesinde `Filled` varyantı kullanılır.
- Kategori simgeleri tek bir sözlükte (`Themes/Icons.xaml`) toplanır; aynı kavram her yerde aynı simgeyi alır. Örnek: "başlangıç" her yerde `Rocket24`.

### 3.5 Bileşen kütüphanesi (`Controls/`)

Mevcut 5 bileşene aşağıdakiler eklenir. Her bileşenin bir "galeri" sayfası olur (yalnızca Debug derlemesinde, Ayarlar > Geliştirici).

| Bileşen | Amaç | Özellikler |
|---|---|---|
| `PageShell` | Bütün modüllerin kabı | Başlık, açıklama, sağda eylem alanı, isteğe bağlı sekme şeridi, içerik, alt durum çubuğu. Her modül bununla başlar. |
| `TabStrip` | Sayfa içi sekmeler | Seçili gösterge, klavye (Ctrl+Tab), rozet sayacı |
| `ActionBar` | Seçim tabanlı eylemler | "N öğe seçili · 1,2 GB" + birincil/ikincil eylem + tümünü seç/temizle. Listelerin altında yapışkan durur. |
| `FilterBar` | Arama + filtre çipleri + sıralama | Ortak arama kutusu (Ctrl+F), temizle düğmesi, sonuç sayısı |
| `ScanPanel` | Tarama sayfası durum makinesi | Durumlar: Boş (açıklama + "Tara") → Taranıyor (gerçek ilerleme, aşama, iptal) → Sonuç → Eylem sürüyor → Rapor. Temizleyici, Depolama, Analizör, Kaldırıcı ve Gizlilik aynı akışı kullanır. |
| `ResultCard` | İşlem sonucu | Başarılı / atlanan / başarısız sayıları, genişletilebilir ayrıntı listesi (neden), "Geri al", "Etkinlik Merkezi'nde aç" |
| `ConfirmDialog` | Yıkıcı işlem onayı | Başlık, **değişecekler listesi** (önizleme), risk rozetleri, "Geri alınabilir/alınamaz" bilgisi, birincil düğme metni eylemin kendisi ("3 öğeyi sil") |
| `RiskBadge` | Risk seviyesi | `RiskLevel` → renk + metin + simge; tek kaynak |
| `StatusPill` | Durum | Etkin / Devre dışı / Çalışıyor / Durdu / Yönetici gerekli / Yeniden başlatma gerekli |
| `SensorValue` | Ölçüm gösterimi | `Value`, `Unit`, `Quality` (Measured / Estimated / Unavailable). Tahminde "≈" ve ipucu, yokta "—" ve neden. **D-1'in UI tarafı.** |
| `AdminGate` | Yönetici gerektiren bölge | Yönetici değilse içerik soluk görünür, üstünde "Yönetici olarak yeniden başlat" düğmesi. Yöneticiyse görünmez. |
| `KeyValueGrid` | Özellik listesi | Etiket/değer, kopyala düğmesi, monospace seçeneği |
| `Timeline` | Zaman çizelgesi | Güne göre gruplu olaylar, simge + başlık + ayrıntı + eylem. Etkinlik Merkezi, Analizör Geçmişi ve Nöbetçi kullanır. |
| `DetailPane` | Ana-ayrıntı paneli | Sağdan açılan (çekmece) panel. Mevcut "drawer"ların ortak hali; Esc ile kapanır. |
| `DiffView` | İki durumu karşılaştırma | Eklendi / Kaldırıldı / Değişti satırları, önce/sonra değerleri |
| `UndoToast` | Geri alma bildirimi | "12 dosya silindi · Geri al" (10 sn) |
| `EmptyState` (mevcut) | Boş durum | "İlk kez" ve "sonuç yok" varyantları eklenir |
| `SkeletonRow` | Yükleniyor iskeleti | Liste ilk yüklenirken dönen simge yerine gri satırlar |

### 3.6 Sayfa şablonları

**A. Tarama sayfası.** Temizleyici, Depolama, Analizör, Gizlilik, Kaldırıcı kalıntıları:

```
┌ PageShell ──────────────────────────────────────────────────────────┐
│ Temizleyici                                     [Ön ayar ▾] [Tara]  │
│ Gereksiz dosyaları güvenle bulur ve Geri Dönüşüm Kutusu'na taşır.   │
├─────────────────────────────────────────────────────────────────────┤
│ ┌KPI┐ ┌KPI┐ ┌KPI┐ ┌Disk C: ██████░░ 68%┐                           │
│ ScanPanel:  [Boş] → [Taranıyor 43% · Tarayıcı önbellekleri · İptal] │
│             → [Sonuç: kategori listesi (sol) | dosya listesi (sağ)] │
│ ─────────────────────────────────────────────────────────────────── │
│ ActionBar: 1.284 dosya seçili · 2,4 GB        [Seçimi temizle] [Temizle] │
└─────────────────────────────────────────────────────────────────────┘
      → ConfirmDialog (önizleme) → ResultCard (+ Geri al) → Etkinlik Merkezi kaydı
```

**B. Liste yönetimi sayfası.** Başlangıç, Hizmetler, Süreçler, Kaldırıcı, Mağaza:

```
┌ PageShell ─────────────────────────────────────────────────────────────┐
│ Başlangıç                                              [Yenile] [Ekle] │
│ TabStrip: Tümü (42) | Kayıt Defteri | Klasör | Görevler | Hizmetler     │
│ FilterBar: [Ara…] (İmzasız) (Microsoft'u gizle) (Yeni eklenen)  Sırala ▾│
├───────────────────────────────────────────────┬────────────────────────┤
│ Sanallaştırılmış liste                        │ DetailPane (seçili öğe)│
│ ● ikon  Ad / yol (ikinci satır)  rozetler  ⋯  │ KeyValueGrid, eylemler │
└───────────────────────────────────────────────┴────────────────────────┘
```

**C. Sihirbaz:** Kaldırıcı ve Kurulum inceleme. Adım göstergesi, ileri/geri, her adımda iptal.

**D. Pano:** Kontrol Paneli ve Sistem Bilgisi. Duyarlı ızgara: ≥1400 px'te 4 sütun, 1000–1400 px'te 3, <1000 px'te 2. Kartlar sürüklenerek sıralanabilir (ileri faz).

### 3.7 Etkileşim kuralları

1. **Yıkıcı eylem akışı:** Önizleme → `ConfirmDialog` → işlem (iptal edilebilir) → `ResultCard` → Etkinlik Merkezi kaydı → `UndoToast`. Birincil düğme metni eylemi söyler: "Tamam" değil "4 programı kaldır".
2. **Durumlar:** Her liste ve kart için Yükleniyor (`SkeletonRow`), Boş (`EmptyState`), Hata (`InfoBar` + "Tekrar dene" + "Ayrıntılar") ve Kısmi (uyarı şeridi) tasarlanır.
3. **Bildirimler:**
   - Sayfa içi sonuçlar: `InfoBar`.
   - Arka plan olayları (nöbetçi, zamanlanmış tarama): tepsi toast'u.
   - Kritik güvenlik bulguları: flyout.
   - Hiçbir bildirim odak çalmaz; tam ekranda ertelenir.
4. **Klavye:**
   - Ctrl+K komut paleti, Ctrl+F arama, F5 yenile, Esc panel/diyalog kapat, Del seçiliyi sil (onaylı), Ctrl+A tümünü seç, Alt+1…9 nav grupları.
   - Her ekranda sekme sırası mantıklı ve odak halkası görünür olmalı.
5. **Mikro metin rehberi:**
   - Kısa, etken çatı, "siz" yerine nötr dil ("Dosyalar Geri Dönüşüm Kutusu'na taşındı").
   - Teknik terimin yanında sade açıklama ("Prefetch: Windows'un uygulamaları hızlı açmak için tuttuğu önbellek").
   - "%100", "tamamen güvenli", "ultra", "mega" gibi mutlak ve abartılı ifadeler **yasak.** Mevcut "Ultra Oyun Modu" → "Oyun Modu".
6. **Yönetici:** Yönetici gerektiren her eylemde kalkan simgesi bulunur. Yönetici değilken bu eylemler `IElevationBroker` üzerinden **tek UAC istemiyle** toplu yapılır.

### 3.8 Erişilebilirlik

- Tüm etkileşimli öğelerde `AutomationProperties.Name` bulunur. `Tools/add-automation-names.py --check` CI'da çalışır.
- Liste satırlarında `AutomationProperties.HelpText` yer alır: ikincil satırın metni.
- Yüksek kontrast teması, yakınlaştırma (%100–%200, `LayoutTransform` ile, Ayarlar'dan) ve ekran okuyucu için canlı bölgeler (`AutomationProperties.LiveSetting`, ilerleme metinlerinde) sağlanır.

### 3.8.1 Uygulama notları (Faz 4)

- **Tipografi eşlemesi:** Plan ölçeği (Micro 11, Caption 12, Body 14, Subtitle 16, Title 20, TitleLarge 28, Display 40) uygulandı. Sabit değerler `Tools/migrate-font-sizes.py` ile taşındı: 8–10 → Micro, 10.5–12.5 → Caption, 13–14 → Body, 15 → BodyLarge, 16 → Subtitle, 17–18 → SectionTitle, 19–22 → Title, 23–28 → TitleLarge, 29–47 → Display. Böylece en küçük gövde metni 12 oldu; 12'lik metinler aynı kaldı (düzen riski düşük). Simge boyutları metin değildir, taşınmadı.
- **Doğrulayıcılar tek betikte:** `verify-code-colors` ve `verify-catch` ayrı betikler yerine `Tools/verify-design-debt.py` içinde; eşikler `Tools/design-debt-baseline.json`'da, borç azaldıkça `--update` ile düşürülür.
- **Stil adı çakışması:** "Text.Secondary" fırça anahtarı olduğu için ikincil gövde stili `Text.BodySecondary` adını aldı.
- **Hareket:** "Animasyonları azalt" (ya da Windows'ta animasyonlar kapalı) açılışta `Motion.*`/`Duration.*` token'larını sıfırlar; döngüsel ilerleme animasyonları etkilenmez.

### 3.9 Doğrulayıcılar (CI'da zorunlu)

| Betik | Kural |
|---|---|
| `verify-tokens.py` (genişletilir) | `Views/**/*.xaml` ve `Controls/**/*.xaml` içinde hex renk yok (yalnızca `Themes/` hariç). Sabit `FontSize` yok (`Font.*` ya da `Text.*` stili zorunlu). Sabit `CornerRadius` yok. |
| `verify-code-colors.py` (yeni) | `Models/`, `ViewModels/`, `Services/` içinde `"#RRGGBB"` yok |
| `verify-symbols.py` (mevcut) | Geçersiz sembol ve emoji yok |
| `verify-catch.py` (yeni) | `catch { }` sayısı eşik değerini geçemez; eşik her fazda düşer |
| `add-automation-names.py --check` | Adı olmayan etkileşimli öğe yok |

---

## 4. Ortak çekirdek

Bütün modüller aşağıdaki servisleri kullanır. İlk fazda yazılır. KAL A1–A3 ile **aynı sınıflardır**, bir kez yazılır.

```
Core/                                   (yeni klasör; saf C#, WPF bağımlılığı yok → Linux'ta test edilebilir)
  Safety/
    PathSafetyGuard.cs                  korumalı klasörler, IsUnder, derinlik kuralı         (KAL A1)
    RegistryPath.cs, RegistrySafetyGuard.cs                                                    (KAL A2)
    CriticalProcessPolicy.cs            sonlandırılamaz/askıya alınamaz süreçler (tek liste) (DEN H-7)
  Text/
    ByteFormatter.cs                    tek FormatBytes                                       (DEN M-2)
    NameMatcher.cs                      kelime bazlı ad eşleştirme                            (KAL A4)
  Uninstall/
    UninstallCommandParser.cs           tırnaksız/boşluklu komutlar, MSI ProductCode          (KAL A5)
  Startup/
    StartupApprovedPaths.cs             Run / Run32 / StartupFolder anahtar seçimi            (DEN H-1)
  Telemetry/
    SensorReading.cs                    Value + Quality (Measured/Estimated/Unavailable)       (DEN D-1)
  History/
    AnalysisRecord.cs, PersistenceSnapshot.cs, SnapshotDiff.cs                                  (§6)
    ActivityEntry.cs                                                                             (§7)
Services/Safety/
  SafeDeleteService.cs  SafeRegistryService.cs  SafeProcessService.cs  UndoJournal.cs          (KAL A3)
  RestorePointService.cs                                                                         (KAL A8)
Services/Infrastructure/
  ProcessRunner.cs         ArgumentList, zaman aşımı, çıktı, çıkış kodu                        (DEN M-8)
  RegistryWriter.cs        yaz → oku → doğrula, hata nedeni döner                               (DEN D-3)
  ElevationBroker.cs       yönetici gerektiren işleri tek UAC ile toplu çalıştırma              (DEN M-7)
  DialogService.cs         MessageBox yerine test edilebilir diyaloglar                         (DEN M-4)
  TelemetryHub.cs          tek örnekleyici, abone modeli, gizliyken durur                       (DEN P-1)
  ActivityJournal.cs       Etkinlik Merkezi kayıt ve geri alma altyapısı                        (§7)
  AnalysisHistoryService.cs                                                                      (§6)
  BackgroundJobScheduler.cs  zamanlanmış taramalar, bütçe, oyun modunda erteleme
Tests/
  Bakim.Core.Tests/        net10.0 (WPF yok) → Linux ve Windows'ta koşar; Core/ kaynaklarını bağlar
  Bakim.Tests/             mevcut (Windows)
  Bakim.UiSmokeTests/      mevcut (Windows)
```

**Diğer altyapı işleri:**
- **Tek sürüm kaynağı:** `Directory.Build.props` (DEN H-15).
- **CI:** `.github/workflows/ci.yml`. `windows-latest` üzerinde build, iki test projesi, doğrulayıcılar ve UI duman testi; `ubuntu-latest` üzerinde `Bakim.Core.Tests` (hızlı geri bildirim).
- **Tanılama paketi:** Ayarlar > "Tanılama paketini dışa aktar" (DEN M-12).
- **Ayarlar v2:** Şema sürümü, göç ve modül bazlı alt nesneler. `appsettings.json` bozulursa yedekten dönülür.

---

## 5. Modül planları

Her modül için aynı başlıklar kullanılır: **Hatalar** (§8'deki kodlar), **Geliştirmeler** (yeni özellikler), **Tasarım**, **Kabul**.

### 5.1 Kontrol Paneli

**Hatalar:** D-1 (sahte sıcaklık), D-10 (sahte 450 MB), D-11 (yapay bekleme), D-6, P-1.

**Geliştirmeler:**
1. **Sağlık puanı yeniden tanımlanır.** Anlık CPU yüküne bağlı olmaz (şu an CPU %90 olunca "sağlık" düşüyor). Kalıcı sinyallerle hesaplanır:
   - disk doluluğu
   - başlangıç girdisi sayısı ve imzasız girdiler
   - son 7 gündeki çökme ve BSOD sayısı (Olaylar modülü)
   - Windows Update durumu
   - Defender durumu (`root\SecurityCenter2`)
   - son güvenlik taraması tarihi
   - temizlenebilir alan

   Her bileşen bir satırda gösterilir ve tıklanınca ilgili modüle derin bağlantıyla gider.
2. **"Tek tık hızlandır" yeniden tanımlanır: "Hızlı Bakım".** Gerçek ve ölçülebilir işler yapar:
   - güvenli temp ve önbellek temizliği (yaş filtresiyle)
   - DNS önbelleğini temizleme
   - geri dönüşüm kutusu (seçenekli)

   Sonuç `ResultCard` ile gösterilir: "1,2 GB boşaltıldı · 3.140 dosya · 12 atlandı". RAM trim bu akıştan **çıkar.**
3. **Öneriler şeridi (ölçüme dayalı):**
   - "Başlangıçta 6 imzasız program var"
   - "C: %91 dolu, Depolama'da 14 GB yinelenen dosya bulundu"
   - "Son analizden bu yana 2 yeni başlangıç girdisi"
   - "Nöbetçi dün 1 riskli kurulum gördü"
4. **Son etkinlikler kartı:** Etkinlik Merkezi'nden son 5 olay.
5. **Canlı grafikler:** CPU, RAM, Disk ve Ağ için son 60 sn (TelemetryHub). Sıcaklık yalnızca gerçek sensör varsa gösterilir (`SensorValue`).
6. **Oyun Modu kartı** kısa bir özet + "Oyun Modu sayfasına git" bağlantısına dönüşür.

**Tasarım:** Pano şablonu (§3.6-D). Üstte sağlık kartı (puan + 5 bileşen), altında öneriler, canlı grafikler, son etkinlikler ve en çok kaynak kullananlar.

**Kabul:** Hiçbir sayı uydurma değil. Sensör yoksa "—". Tepsideyken örnekleme duruyor.

### 5.2 Temizleyici

**Hatalar:** H-2 (Firefox), H-3 (Spotify), H-4 (WU ve temp yaşı), H-5 (`IsSafeTarget`), P-6 (ilerleme gürültüsü), D-11 (`CleanerViewModel.cs:403` yapay bekleme).

**Geliştirmeler:**
1. **Kategori tanımları veri dosyasına taşınır.** `Assets/cleaner-rules.json` alanları: kök yollar, desenler, minimum yaş, yönetici gereksinimi, varsayılan seçim, açıklama, "silinirse ne olur". Kod içindeki 370 satırlık liste kalkar. Yeni kategori eklemek kod değişikliği gerektirmez.
2. **Güvenli silme:** Varsayılan Geri Dönüşüm Kutusu. Tek seferlik > 2 GB işlemlerde kalıcı silme önerilir ve kullanıcıya sorulur.
3. **Yeni kategoriler:**
   - Windows Update temizliği (DISM `StartComponentCleanup`, yönetici, uzun süreli)
   - Teslim Optimizasyonu
   - eski Windows kurulumu (`Windows.old`; yalnızca 10 günden eskiyse; uyarılı)
   - Geri Dönüşüm Kutusu (`SHEmptyRecycleBin`)
   - tarayıcı profilleri: Chrome, Edge ve Brave için **tüm profiller** (şu an yalnızca `Default`)
   - Microsoft Teams, VS Code önbellekleri, geliştirici önbellekleri (npm, pip, nuget, gradle; ayrı grup ve varsayılan kapalı)
4. **Açık uygulama farkındalığı:** Tarayıcı açıksa kategori "Tarayıcı açık, bazı dosyalar atlanacak" uyarısı alır ve "Tarayıcıyı kapat" seçeneği sunulur.
5. **Zamanlanmış temizlik:** Haftalık, yalnızca güvenli kategoriler. `BackgroundJobScheduler` + Etkinlik Merkezi kaydı.
6. **Hariç tutma listesi yönetimi:** Ayarlar'da görünür ve düzenlenebilir.

**Tasarım:** Tarama sayfası şablonu. Solda kategori ağacı (grup > kategori; her kategoride boyut ve dosya sayısı), sağda sanallaştırılmış dosya listesi (klasöre göre gruplu). Altta `ActionBar`.

**Kabul:** Kanarya testi. Benzer adlı korunan dosyalar silinmiyor, Firefox önbelleği bulunuyor, Spotify seçili gelmiyor.

### 5.3 Depolama (yeni modül)

Sistem Bilgisi'nden taşınan araçlar ve yeni disk haritası burada toplanır.

**Hatalar:** H-9 (boş klasör), H-10 (OneDrive yer tutucuları).

**Sekmeler:**
1. **Genel Bakış:** Sürücüler (kapasite, doluluk, sağlık/SMART özeti, TRIM durumu), en büyük 10 klasör, kategori dağılımı (video, resim, belge, arşiv, uygulama, sistem).
2. **Disk Haritası (yeni):**
   - Klasör boyutları **treemap** olarak gösterilir.
   - Tarama MFT ile yapılır: yöneticiyken `FSCTL_ENUM_USN_DATA` ya da `NtQueryDirectoryFile` ile hızlı sayım; değilse paralel `EnumerationOptions`.
   - Tıklayarak içine girilir, üstte breadcrumb bulunur, sağ tık menüsünde "Klasörü aç" ve "Geri Dönüşüm'e taşı" yer alır.
3. **Büyük Dosyalar:** Mevcut özellik taşınır. Eşik, tür filtresi, yaş ("1 yıldır açılmamış", `LastAccessTime` güvenilirse) eklenir.
4. **Yinelenenler:** Mevcut 3 aşamalı hash taşınır. Ek olarak:
   - bulut yer tutucuları atlanır (H-10)
   - hardlink'ler tanınır (aynı dosya kimliği, `GetFileInformationByHandle`) ve kopya sayılmaz
   - "orijinal" önerisi: en eski ya da kullanıcının seçtiği klasördeki
   - resimler için küçük resim önizlemesi
5. **Boş Klasörler:** Güvenli kapsam, varsayılan seçimsiz (H-9).

**Tasarım:** TabStrip + tarama sayfası. Treemap için `Chart.Series*` token'ları kullanılır; renk kategoriye göre atanır.

**Kabul:** Sistem Bilgisi'nde artık dosya silen bir araç yok. OneDrive klasörü taranırken indirme tetiklenmiyor.

### 5.4 Süreçler (Optimizer)

**Hatalar:** H-6 (handle sızıntısı, CpuTracker), H-7 (korunan süreç listesi), P-3 (RAM trim), D-6.

**Geliştirmeler:**
1. **Süreç ağacı görünümü:** ebeveyn → çocuk; Chrome gibi çok süreçli uygulamalar gruplanır. Toplam CPU, RAM, disk ve ağ **uygulama bazında** gösterilir.
2. **Sütunlar:** CPU, bellek (private working set), disk I/O (`GetProcessIoCounters`), GPU (ileri faz), imza durumu, yayıncı, başlama zamanı, komut satırı (DetailPane'de).
3. **Eylemler:**
   - Sonlandır, ağacı sonlandır.
   - Askıya al / devam ettir. Askıya alınan süreçler Bakım kapanınca **otomatik devam ettirilir**; askıda kalmış süreç bırakılmaz.
   - Öncelik ayarı.
   - Dosya konumunu aç, Analizörle tara (tek tık; sonuç Analizör Geçmişi'ne kaydedilir).
4. **"RAM temizle" kaldırılır.** Yerine yöneticiyken **"Bekleme belleğini boşalt"** gelir (`NtSetSystemInformation(MemoryPurgeStandbyList)`). Açıklaması: "Windows bu belleği zaten gerektiğinde boşaltır; yalnızca sorun yaşıyorsanız kullanın". Varsayılan otomatik çalışma yok.
5. **Kaynak geçmişi:** Seçili süreç için 60 sn'lik grafik.

**Tasarım:** Liste yönetimi şablonu + DetailPane. Sistem süreçleri gri ve kilit simgeli.

**Kabul:** 10 dk açık kalınca handle sayısı sabit kalıyor. `winlogon` ve `csrss` askıya alınamıyor.

### 5.5 Oyun Modu (ayrı sayfa)

**Hatalar:** H-8 (güç planı), D-7 (yanlış log), P-3.

**Geliştirmeler:**
1. **Profil:** Kullanıcı hangi adımların uygulanacağını seçer:
   - güç planı (Yüksek/Nihai performans ya da seçili plan)
   - Windows Odak Yardımı / Rahatsız Etmeyin
   - askıya alınacak arka plan uygulamaları (kullanıcı listesi; örn. OneDrive, Teams, tarayıcı)
   - Bakım'ın arka plan işlerini durdurma (mevcut)
   - bildirimleri erteleme
2. **Geri dönüş garantisi:**
   - Etkinleştirmeden önce mevcut durum (plan, odak modu, askıya alınan PID'ler) `settings/gamemode-state.json` dosyasına yazılır.
   - Kapatınca birebir geri yüklenir.
   - Bakım çöker ya da PC yeniden başlarsa, açılışta yarım kalan durum algılanıp onarılır.
3. **Otomatik tetikleme:**
   - Kullanıcının oyun listesindeki bir exe başlayınca otomatik açılır, kapanınca kapanır.
   - Liste kaynakları: Steam, Epic ve Xbox kütüphaneleri + elle ekleme.
   - Süreç başlangıç olayı TelemetryHub'daki süreç izleyiciden alınır.
4. **Oturum raporu:** Süre, ortalama CPU ve GPU yükü, askıya alınan uygulamalar, geri yükleme sonucu. Etkinlik Merkezi'ne kaydedilir.

**Tasarım:** Büyük durum kartı (Açık/Kapalı + tek düğme), profil ayarları kartı, oyun listesi, son oturumlar.

**Kabul:** Önceki güç planı birebir geri geliyor. Çökme sonrası açılışta onarım çalışıyor.

### 5.6 Başlangıç

**Hatalar:** H-1 (Run32), M-3 (çift motor).

**Geliştirmeler:**
1. **Tek motor:** `AutorunsScannerEngine` kullanılır. Başlangıç sayfası onun Run, StartupFolder, logon tetiklemeli ScheduledTask ve otomatik başlayan Service kategorilerinin kullanıcı dostu görünümüdür. Analizör ise uzman görünümüdür (tüm kategoriler).
2. **Devre dışı bırakma Windows standardıyla yapılır:** `StartupApproved\Run`, `Run32` ve `StartupFolder`. Görev Yöneticisi ile tutarlı olur. `.disabled` uzantısıyla yeniden adlandırma bırakılır; eski `.disabled` dosyaları için göç yapılır.
3. **Gerçek etki:** Windows'un ölçtüğü değer okunur (`Microsoft-Windows-Diagnostics-Performance/Operational` olay günlüğü, EventID 100 ve 101: önyükleme süresi ve yavaşlatan uygulamalar). Veri yoksa "Ölçüm yok" gösterilir; **tahmini "Yüksek etki" etiketi kullanılmaz.**
4. **Önyükleme süresi geçmişi:** Son 10 açılışın süresi (EventID 100) grafikle gösterilir. "Değişikliklerinizden sonra açılış 8 sn kısaldı" gibi geri bildirim verilir.
5. **Güvenlik sinyalleri:** İmzasız, Temp'ten çalışan ve son 7 günde eklenen girdiler rozetlerle vurgulanır. "Analizörle tara" seçeneği ve Analizör Geçmişi bağlantısı bulunur.
6. **Yeni başlangıç girdisi bildirimi:** Arka plan izleme `RegNotifyChangeKeyValue` ile Run anahtarlarını ve Startup klasörünü izler. Yeni girdi eklenince tepsi bildirimi gösterilir: "X başlangıca eklendi · İncele". Ayar ile açılıp kapatılır; Nöbetçi ile ortak altyapıdır.

**Tasarım:** Liste yönetimi şablonu. Satırda aç/kapa anahtarı, ad, yayıncı, imza rozeti, etki ve konum çipi.

### 5.7 Hizmetler ve Sürücüler

**Geliştirmeler:**
1. **Güvenli önerilen profiller:** "Yazıcı kullanmıyorum" (Spooler), "Xbox kullanmıyorum" gibi seçenekler. Her profil değişecek hizmetleri önizlemeyle gösterir ve Etkinlik Merkezi üzerinden geri alınabilir.
2. **Kritik hizmet koruması:** Sistem kritik listesi durdurulamaz ve başlangıç türü değiştirilemez. Liste tek kaynak (`CriticalServicePolicy`).
3. **Başlangıç türü değişikliği** `sc.exe` yerine `ChangeServiceConfig` (P/Invoke) ya da `ProcessRunner` ile yapılır; sonuç doğrulanır.
4. **Sürücüler:** imza durumu, sağlayıcı, tarih, **eski sürücü uyarısı** (5 yıldan eski üçüncü taraf), ilgili aygıt. "Aygıt Yöneticisinde aç" bağlantısı.
5. **Hizmet bağımlılıkları** ağacı (DetailPane).

### 5.8 Sistem Bilgisi

**Hatalar:** M-9 (55 sabit renk), D-9 (TRIM), M-5 (dev VM).

**Geliştirmeler:**
1. **Yeniden düzenleme:** Salt okunur bir bilgi sayfası olur. Dosya araçları Depolama'ya taşınır. Sekmeler: Özet, İşlemci, Bellek, Grafik, Depolama (SMART sağlık dahil), Ağ, İşletim Sistemi, Pil (dizüstü).
2. **Gerçek ölçümler:**
   - Sıcaklıklar `SensorValue` ile gösterilir.
   - GPU: mevcut `GpuInfoProvider` + NVAPI/ADLX (varsa).
   - Pil: tasarım kapasitesi / tam şarj kapasitesi (`powercfg /batteryreport` XML ya da WMI `BatteryFullChargedCapacity`).
3. **Rapor:** "Kopyala" (düz metin, forum paylaşımı için) ve "HTML olarak dışa aktar" (mevcut) korunur. HTML şablonu token renklerini kullanır.
4. **Sürücü ve BIOS sürümleri**, "Windows sürümü desteği bitiyor mu?" bilgisi (build numarasından).

### 5.9 Ağ İzleyici

**Hatalar:** P-8 (hız testi veri kullanımı).

**Geliştirmeler:**
1. **Bağlantılar:** Uygulama bazında gruplu. İmza, uzak IP, ülke (yerel GeoIP veritabanı; çevrimdışı; opsiyonel indirme) ve ters DNS gösterilir.
2. **Güvenlik duvarı:**
   - Bakım'ın oluşturduğu engelleme kuralları ayrı listelenir, tek tıkla kaldırılır.
   - `netsh` yerine `INetFwPolicy2` kullanılır.
   - Her kural Etkinlik Merkezi'ne kaydedilir.
3. **Veri kullanımı:** Uygulama başına (Windows'un SRUM verisi `SRUDB.dat`; yönetici, ileri faz) ya da oturum boyunca ölçülen değer.
4. **Hız testi:** Ölçülü bağlantı uyarısı, süreye dayalı durdurma, sonuç geçmişi.
5. **Araçlar:** ping, DNS temizleme, IP yenileme, port kontrolü (mevcut). Traceroute ve DNS sunucusu karşılaştırması eklenir.
6. **Analizör entegrasyonu:** Şüpheli bağlantı satırında "Süreci Analizörle tara" seçeneği.

### 5.10 Olaylar ve Çökmeler (Crash Analyzer)

**Geliştirmeler:**
1. **Olay günlüklerini temizleme özelliği kaldırılır** ya da dışa aktarım + çift onay arkasına alınır. Günlük temizlemek adli iz yok eder ve zararlı yazılım davranışına benzer.
2. **BSOD analizi:** Minidump'lardan hata kodu, parametreler ve **sorumlu sürücü** çıkarılır (dump başlığı ve modül listesi `MINIDUMP_MODULE_LIST`). Her bulgu Türkçe açıklama ve önerilen adımlarla eşleşir.
3. **Uygulama çökmeleri:** WER raporları (`ReportArchive`), en çok çöken uygulamalar, sürüm bilgisi.
4. **Güvenilirlik zaman çizelgesi:** `Win32_ReliabilityRecords` ile Windows Güvenilirlik İzleyicisi verisi günlük grafikle gösterilir.
5. **SFC ve DISM:** İlerleme ve çıktı canlı akar (`ProcessRunner`), sonuç yorumlanır ("Bozuk dosya bulunamadı" / "Onarıldı" / "Onarılamadı → DISM önerisi").

### 5.11 Kaldırıcı

**Ayrıntılı plan: KAL.** Özet:
- A fazı (güvenlik): PathSafetyGuard, güven modeli, doğru bekleme ve doğrulama, gerçek registry yedeği, Geri Dönüşüm Kutusu.
- B fazı: kanıt tabanlı kalıntılar (MSI bileşenleri, servis, görev, kısayol, firewall).
- C fazı: sağ tık v2 (BakimShell, tek örnek, `.url` ve `.msi` desteği).
- D fazı: liste performansı, 7 adımlı sihirbaz, kaldırma geçmişi.

**Tasarım ekleri:**
- Liste satırında boyut çubuğu (göreli), kurulum tarihi ve "Kurulum izi var" rozeti.
- Kullanılmayan uygulamalar filtresi: "90 gündür açılmadı" (Prefetch zamanları; yönetici).
- Toplu kaldırma kuyruğu paneli.

### 5.12 Mağaza

**Hatalar:** G-2 (doğrulanmamış ikililer), D-5 (sahte "Kuruldu").

**Geliştirmeler:**
1. **Tek kaynak: winget.** Katalog `winget` kimlikleriyle tutulur. Kurulum, güncelleme ve kaldırma winget üzerinden yapılır. Winget yoksa "App Installer yükle" yönlendirmesi gösterilir.
2. **Güncellemeler sekmesi:** `winget upgrade` listesi; tek tek ya da toplu güncelleme. Güncellemeler Etkinlik Merkezi'ne kaydedilir.
3. **Üçüncü taraf özel yükleyiciler** (VC++ AIO, TechPowerUp kazıma) kaldırılır. VC++ çalışma zamanları için winget'teki `Microsoft.VCRedist.*` paketleri kullanılır. DirectX için Microsoft'un imzalı paketi korunur ve imzacı "Microsoft Corporation" olarak doğrulanır; bu Mağaza'ya özel bir kontrol olup güncelleyici kararından bağımsızdır.
4. **Kurulum Nöbetçisi entegrasyonu:** Mağaza'dan kurulan paketler otomatik olarak "izleyerek kur" moduyla kurulur ve kurulum raporu oluşur.
5. **Hazır paketler** (Format sonrası, Oyuncu, Ofis, Geliştirici) korunur. Kurulum öncesi toplam boyut ve liste önizlemesi eklenir.

### 5.13 Analizör

**Hatalar:** G-4 (güvenilir yayıncı alt dizesi), ölü WINTRUST kodu.

**Sekmeler:**
1. **Tarama** (mevcut kalıcılık listesi): Tasarım olarak liste yönetimi şablonuna geçer. Risk puanı sütunu ve "neden?" açılır panelini alır.
2. **Dosya Analizi:** Dosya seç, sürükle-bırak ya da sağ tık ("Bakım ile analiz et" bağlam menüsü: `*\shell` için, ayarla açılır). Sonuç ekranı mevcut `ThreatAnalysisDialog`'dan sayfa içi bir görünüme dönüşür; diyalog kalabilir.
3. **Geçmiş (YENİ):** §6.
4. **Değişiklikler (YENİ):** Son iki kalıcılık taraması arasındaki fark (§6.4).

**Motor geliştirmeleri:**
- Tam eşleşmeli güvenilir yayıncı listesi (G-4).
- İmzacı tutarsızlığı sinyali.
- Hash itibar önbelleği (§6.5).
- Defender ile ikinci görüş: `MpCmdRun -Scan -ScanType 3 -File` (NÖB 4.6 ile ortak).
- İsteğe bağlı YARA kuralları (ileri faz).

### 5.14 Analizör Geçmişi

§6'da tam spesifikasyon.

### 5.15 Kurulum Nöbetçisi

**Ayrıntılı plan: NÖB.**
- Faz 0: P0 düzeltmeleri.
- Faz 1–7: tespit v2, sensörler, gürültü filtresi ve atıf, risk motoru, aksiyonlar, UX, dayanıklılık.
- Faz 8: servis mimarisi (v4.0).

Nöbetçinin "Kurulum Geçmişi" ekranı Etkinlik Merkezi altyapısını kullanır (§7). Kurulum sırasında bırakılan yürütülebilirlerin analiz sonuçları **Analizör Geçmişi'ne** kaynak "Kurulum Nöbetçisi" etiketiyle yazılır.

### 5.16 Windows Ayarları (Tweaker) ve Gizlilik

**Hatalar:** D-3 (doğrulanmayan yazma), H-13 (bool snapshot), G-3 (yönetici kısayolu), G-5 (güvenlik etkisi), G-6 (TrustedInstaller), D-4 (bloatware sonucu).

**Geliştirmeler:**
1. **Veri tabanlı ince ayarlar:** 7 servisteki ≈200 ince ayar `Assets/tweaks/*.json` dosyalarına taşınır. Her ayar şunları tanımlar: kimlik, kategori, başlık, açıklama, **değişiklik listesi** (registry değerleri ya da komutlar), yönetici gereksinimi, yeniden başlatma veya Explorer yenileme gereksinimi, güvenlik etkisi, Windows sürüm aralığı. Tek bir `TweakEngine` bunları uygular, doğrular ve geri alır. Kod 7 servisten tek motora iner; yaklaşık 4.000 satır azalır.
2. **Değer düzeyinde anlık görüntü:** Her uygulamadan önce önceki değer (ya da "yoktu") saklanır. Geri alma birebir yapılır.
3. **Kart tasarımı:**
   - Başlık, açıklama, "Mevcut: Açık → Yeni: Kapalı" satırı.
   - Rozetler: Yönetici, Yeniden başlatma, **Güvenliği azaltır**.
   - Uygulama sonrası doğrulama sonucu gösterilir.
   - "Neyi değiştirir?" açılır bölümünde gerçek registry yolları listelenir (şeffaflık).
4. **Önerilen profiller:** Gizlilik (temel), Performans (temel), Temiz arayüz. Profil uygulanmadan önce değişiklik listesi önizlemeyle gösterilir. Güvenliği azaltan ayarlar profillerde **asla** yer almaz.
5. **Arama:** Tüm ayarlar arasında tam metin arama (Ctrl+F). Komut paletine de eklenir.
6. **Windows sürüm uyumu:** 24H2'de çalışmayan ayarlar gizlenir ya da işaretlenir.
7. **Gizlilik alt modülü:** Bloatware kaldırma sonucunun doğrulanması, "yalnızca bu kullanıcı / tüm kullanıcılar" seçimi, Store'dan geri yükleme bağlantısı.

### 5.17 Windows Araçları (Mağaza'dan taşınır)

- Klasik konsollar (mmc, regedit, gpedit, services, devmgmt…), gizli Windows araçları (God Mode klasörü, Güvenilirlik İzleyicisi, Kaynak İzleyicisi) ve Bakım araçları (TrustedInstaller ile çalıştır, yönetici kısayolu; G-3 ve G-6 düzeltmeleriyle).
- Kategori ızgarası, arama, favoriler.

### 5.18 Ayarlar

**Geliştirmeler:**
1. **Kategoriler:** Genel, Görünüm, Bildirimler, Nöbetçi, Güvenlik (VirusTotal, Defender), Gizlilik (geçmiş saklama süreleri), Gelişmiş (tanılama, geliştirici), Hakkında.
2. **Değişiklik günlüğü** koddan `Assets/changelog.json` dosyasına taşınır (DEN M-5); ayrı sayfada gösterilir.
3. **Yönetim listeleri:**
   - Bakım'ın oluşturduğu zamanlanmış görevler
   - sağ tık kayıtları
   - firewall kuralları
   - hariç tutmalar
   - güven listesi (yayıncılar ve hash'ler)

   Her biri görüntülenebilir ve silinebilir.
4. **Veri:** Ayarları dışa ve içe aktarma (mevcut), tüm geçmişi temizleme, tanılama paketi.
5. **Görünüm:** Tema, vurgu rengi (Windows'u takip et), yazı boyutu ölçeği, animasyonları azalt, yoğunluk (rahat/kompakt).

### 5.19 Tepsi, Komut Paleti, Bildirimler

- **Tepsi flyout'u:**
  - Tema token'larına geçer (sabit `#EE1E293B` kaldırılır).
  - Kartlar: CPU/RAM (TelemetryHub), Oyun Modu, Nöbetçi durumu (izleniyor / son kurulum), son 3 etkinlik, Hızlı Bakım.
- **Komut paleti:** Bütün modüllerin eylemleri ve **derin bağlantılar** komut olarak kaydedilir (`ICommandContributor` arayüzü; her modül kendi komutlarını bildirir). Son kullanılanlar, bulanık arama, Analizör Geçmişi'nde hash ve ad araması.
- **Bildirim merkezi:** Uygulama içi bildirimler Etkinlik Merkezi'ne düşer. Tepsi toast'ları için Windows bildirimleri (`ToastNotificationManagerCompat` yerine WPF-UI Snackbar + `Shell_NotifyIcon` balonları; ileri fazda Windows App SDK) kullanılır.

### 5.20 Avcı Modu (Hunter)

- Pencere seçildiğinde süreç bilgileri gösterilir: imza, konum, ilgili uygulama. Eylemler: "Analizörle tara", "Kaldır" (KAL çözümleyicisiyle), "Süreci sonlandır" (CriticalProcessPolicy ile).
- Sistem süreçleri ve Bakım'ın kendisi seçilemez; bu durum görsel olarak belirtilir.

### 5.21 Kurulum paketi ve güncelleyici

- **Güncelleyici** (imza kontrolü yok; ürün kararı):
  - `GitHubUpdateService`'teki ölü indirme metodu silinir.
  - Asset deseni sıkılaştırılır.
  - Boyut kontrolü eklenir.
  - Güncelleme öncesinde "Şimdi / Kapanırken / Sonra" seçeneği sunulur.
  - Sürüm notları uygulama içinde biçimli gösterilir.
- **Kurulum (`Bakim_Setup.iss`):**
  - Yönetici modeli kararı (DEN G-7).
  - `BakimShell.exe` eklenir (KAL C3).
  - Kaldırma sırasında Bakım'ın bıraktığı her şey temizlenir: görevler, sağ tık kayıtları, firewall kuralları, geçmiş klasörü (kullanıcıya sorularak).
- **Tek sürüm kaynağı** (H-15).

---

## 6. Analizör Geçmişi

Yeni modül, tam spesifikasyon.

### 6.1 Amaç

Bugün bir dosya analiz edildiğinde sonuç yalnızca diyalog açık kaldığı sürece var; kapanınca kayboluyor. Kalıcılık taramaları da her seferinde sıfırdan yapılıyor ve "neyin değiştiği" bilinmiyor. Analizör Geçmişi:

1. Yapılan **her** dosya analizini kalıcı olarak kaydeder. Analizin nereden başlatıldığı fark etmez: Analizör, sağ tık, Nöbetçi, Kaldırıcı, Süreçler, Ağ İzleyici, Başlangıç.
2. Her kalıcılık taramasının **anlık görüntüsünü** saklar ve iki tarama arasındaki farkı gösterir (yeni başlangıç girdisi, değişen yol, değişen imza).
3. Aynı dosyanın (hash ya da yol) önceki analizleriyle **karşılaştırma** yapar.
4. VirusTotal ve analiz sonuçlarını **önbelleğe** alır; aynı hash için gereksiz sorgu yapılmaz.
5. Kullanıcının kararlarını (güvendi, karantinaya aldı, sildi) hatırlar.

### 6.2 Veri modeli (`Core/History`)

```csharp
public enum AnalysisSource { Analyzer, ContextMenu, DragDrop, SetupSentinel, Uninstaller, Processes, NetworkMonitor, Startup, Scheduled, Unknown }
public enum AnalysisVerdict { Clean, Info, Caution, Suspicious, Dangerous, Missing }
public enum UserDecision { None, Trusted, Quarantined, Deleted, Disabled, Ignored }

public sealed record AnalysisRecord
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime AnalyzedAtUtc { get; init; }
    public AnalysisSource Source { get; init; }
    public string? SourceDetail { get; init; }                 // "Kurulum: 7-Zip 24.08", "PID 1234"
    public string FilePath { get; init; } = "";
    public string FileName { get; init; } = "";
    public long FileSizeBytes { get; init; }
    public string Sha256 { get; init; } = "";
    public string? Md5 { get; init; }
    public string? Sha1 { get; init; }
    public DateTime? FileLastWriteUtc { get; init; }
    public string SignatureStatus { get; init; } = "";          // Verified / Unsigned / InvalidOrTampered
    public string? Signer { get; init; }
    public bool IsCatalogSigned { get; init; }
    public string? CompanyName { get; init; }
    public string? ProductName { get; init; }
    public string? FileVersion { get; init; }
    public int RiskScore { get; init; }                         // 0-100
    public AnalysisVerdict Verdict { get; init; }
    public IReadOnlyList<AnalysisFactorSummary> Factors { get; init; } = [];   // başlık, şiddet, puan etkisi
    public string? Recommendation { get; init; }
    public int? VirusTotalMalicious { get; init; }
    public int? VirusTotalTotal { get; init; }
    public DateTime? VirusTotalCheckedAtUtc { get; init; }
    public string? MotwHostUrl { get; init; }
    public string? PersistenceLocation { get; init; }           // kalıcılık girdisinden geldiyse
    public UserDecision Decision { get; init; }
    public DateTime? DecisionAtUtc { get; init; }
    public string? Note { get; init; }                          // kullanıcı notu
}

public sealed record AnalysisFactorSummary(string Title, string Severity, int ScoreImpact);

public sealed record PersistenceSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime TakenAtUtc { get; init; }
    public string Trigger { get; init; } = "Manual";           // Manual / Scheduled / AfterInstall / Startup
    public IReadOnlyList<PersistenceEntry> Entries { get; init; } = [];
}

public sealed record PersistenceEntry(
    string Key,                // Kategori + Konum + Ad: kararlı kimlik
    string Category, string Name, string Location, string FilePath, string Arguments,
    bool IsEnabled, string SignatureStatus, string? Signer, string? Sha256);

public enum DiffKind { Added, Removed, Changed }
public sealed record SnapshotDiffItem(DiffKind Kind, PersistenceEntry? Before, PersistenceEntry? After, IReadOnlyList<string> ChangedFields);
```

### 6.3 Depolama

- **Konum:** `%LocalAppData%\Bakim\History\Analyzer\`
- **Kayıtlar:** `records.jsonl`, satır başına bir `AnalysisRecord`.
  - Yalnızca ekleme yapılır (append-only); bozulmaya dayanıklıdır, bozuk satır atlanır ve loglanır.
  - Karar ya da not güncellemesi aynı `Id` ile yeni satır olarak eklenir; okurken son satır geçerli sayılır.
  - Periyodik sıkıştırma (compaction): 5 MB'ı geçince eski ve güncellenmiş satırlar birleştirilip yeniden yazılır (önce temp dosya, sonra `File.Replace`).
- **Anlık görüntüler:** `snapshots/{yyyyMMdd_HHmmss}_{id}.json.gz` (GZip).
- **İndeks:** Açılışta `records.jsonl` belleğe yüklenir; kayıt sayısı sınırı 20.000'dir. SHA-256 ve yol için sözlükler tutulur.
- **Saklama:** Varsayılan 365 gün, en fazla 20.000 kayıt ve 50 anlık görüntü (ayarlanabilir). Karar verilmiş kayıtlar (Güvenildi, Karantina) süre dolsa da korunur.
- **Gizlilik:** Tüm veri yereldir. Ayarlar > Gizlilik > "Analizör geçmişini temizle". Dışa aktarımda kullanıcı adı maskelenebilir (`C:\Users\***\`).

### 6.4 Servis (`IAnalysisHistoryService`)

```csharp
public interface IAnalysisHistoryService
{
    Task<AnalysisRecord> RecordAsync(ThreatAnalysisResult result, AnalysisSource source, string? sourceDetail = null);
    Task SetDecisionAsync(string recordId, UserDecision decision, string? note = null);
    IReadOnlyList<AnalysisRecord> Query(AnalysisHistoryFilter filter);           // tarih, risk, kaynak, imza, VT, metin
    IReadOnlyList<AnalysisRecord> GetByHash(string sha256);
    IReadOnlyList<AnalysisRecord> GetByPath(string path);
    AnalysisRecord? GetLatestByHash(string sha256);

    Task<PersistenceSnapshot> SaveSnapshotAsync(IEnumerable<PersistenceItem> items, string trigger);
    IReadOnlyList<PersistenceSnapshot> ListSnapshots();                          // yalnızca başlık bilgisi
    IReadOnlyList<SnapshotDiffItem> Diff(string olderSnapshotId, string newerSnapshotId);
    IReadOnlyList<SnapshotDiffItem> DiffLatest();                               // son iki

    Task<int> PurgeAsync(TimeSpan olderThan);
    Task ClearAllAsync();
    Task<string> ExportAsync(string path, ExportFormat format, AnalysisHistoryFilter filter);  // Csv / Json / Html
    event Action<AnalysisRecord>? RecordAdded;
}
```

**Otomatik kayıt:** `IFileThreatAnalyzerService` bir **dekoratör** ile sarılır (`RecordingFileThreatAnalyzer`). Böylece `AnalyzeFileAsync` hangi modülden çağrılırsa çağrılsın sonuç kaydedilir; modüllerin tek tek değiştirilmesi gerekmez. Kaynak bilgisi `AnalysisContext` (AsyncLocal) ile taşınır:

```csharp
using (AnalysisContext.Begin(AnalysisSource.SetupSentinel, "Kurulum: 7-Zip"))
    await analyzer.AnalyzeFileAsync(path);
```

**Diff anahtarı:** `Category|Location|Name` normalize edilmiş, küçük harf. Değişen alanlar: `FilePath`, `Arguments`, `IsEnabled`, `SignatureStatus`, `Signer`, `Sha256`.

`AnalyzerViewModel.ScanAsync` bittiğinde `SaveSnapshotAsync` çağrılır. Zamanlanmış tarama (ayar: günlük / haftalık / kapalı) `BackgroundJobScheduler` ile çalışır.

### 6.5 Hash itibar önbelleği

- `GetLatestByHash`: Aynı SHA-256 son 7 gün içinde analiz edildiyse Analizör listesinde "Önceki sonuç: Temiz (3 gün önce)" rozeti gösterilir.
- VirusTotal sorgusu yapılmadan önce önbelleğe bakılır. Sonuç 7 günden yeniyse API kotası harcanmaz; "Yeniden sorgula" ile zorlanabilir.
- Kullanıcı "Güvendi" kararı verdiyse aynı hash'e sahip dosyalar her yerde (Analizör, Nöbetçi, Başlangıç) "Güvenilen" rozeti alır ve risk puanı gösterilse bile uyarı üretilmez.

### 6.6 Arayüz

**Analizör > Geçmiş sekmesi:**

```
┌ Analizör ───────────────────────────────────────────────────────────────────┐
│ TabStrip:  Tarama | Dosya Analizi | Geçmiş (1.284) | Değişiklikler (3)        │
├─────────────────────────────────────────────────────────────────────────────┤
│ FilterBar: [Ada, yola, SHA-256'ya göre ara…]                                │
│   Tarih: (Bugün) (7 gün) (30 gün) (Tümü)   Risk: (Tehlikeli) (Şüpheli) (Dikkat) │
│   Kaynak ▾  İmza ▾  (VirusTotal tespiti var)  (Kararsız)      [Dışa aktar ▾] │
├──────────────────────────────────────────────┬──────────────────────────────┤
│ Timeline (güne göre gruplu, sanallaştırılmış)│ DetailPane                   │
│ BUGÜN                                         │ setup_helper.exe             │
│ ● 14:32 setup_helper.exe      [Şüpheli 64]    │ C:\Users\…\AppData\Local\…   │
│         AppData\Local\…  · Nöbetçi · VT 3/72  │ Risk 64 · Şüpheli            │
│ ● 11:05 vlc.exe               [Temiz 5]       │ Nedenler:                    │
│         Program Files\VideoLAN · Analizör     │  ▲ İmzasız yazılım      +25  │
│ DÜN                                           │  ▲ Temp dizininde       +25  │
│ ● 22:14 update.exe            [Dikkat 38]     │  ■ Yüksek entropi       +14  │
│         … · Başlangıç                         │ SHA-256  3f2a…  [Kopyala]    │
│                                               │ VirusTotal 3/72 (2 gün önce) │
│                                               │ Bu dosyanın geçmişi (3):     │
│                                               │  • 12 Eyl · 58 · Analizör    │
│                                               │  • 20 Eyl · 64 · Nöbetçi     │
│                                               │ [Yeniden analiz] [Farkı göster]│
│                                               │ [Konumu aç] [Güven] [Karantina]│
│                                               │ Not: [____________________]  │
└──────────────────────────────────────────────┴──────────────────────────────┘
```

- **Satır:** Dosya simgesi (ilişkili ikon, önbellekli), ad, kısaltılmış yol, `RiskBadge` + puan, kaynak çipi, VT rozeti, karar rozeti (Güvenildi / Karantina / Silindi), saat.
- **Farkı göster:** Aynı yolun ya da hash'in önceki kaydıyla `DiffView`. Örnek: "İmza: Geçerli → İmzasız", "Boyut 1,2 MB → 3,4 MB", "Risk 12 → 64", "Yeni faktör: Temp dizininde".
- **Yeniden analiz:** Dosya hâlâ duruyorsa yeni bir analiz yapar ve yeni kayıt oluşturur. Dosya yoksa bu durum "Dosya artık yok" olarak gösterilir.
- **Toplu eylemler** (ActionBar): Seçilenleri dışa aktar, VT'de sorgula, güven listesine ekle, geçmişten kaldır.

**Analizör > Değişiklikler sekmesi:**

```
┌ Değişiklikler: 24 Eyl 21:00 (zamanlanmış) ↔ 25 Eyl 09:12 (elle)    [Taramaları seç ▾] │
│ ● Eklendi (2)                                                                        │
│   + Run: "OneDriveSetup"  C:\Users\…\OneDriveSetup.exe   İmzalı: Microsoft  [İncele]  │
│   + Görev: \UpdaterTask   C:\ProgramData\x\up.exe        İmzasız  [Şüpheli]  [İncele] │
│ ● Değişti (1)                                                                        │
│   ~ Hizmet: FooSvc  ImagePath: C:\Foo\svc.exe → C:\Users\Public\svc.exe  [İncele]     │
│ ● Kaldırıldı (1)                                                                     │
│   − Run: "Discord"                                                                   │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

**Ek arayüz noktaları:**
- **Kontrol Paneli:** "Son analizler" kartı (son 3 kayıt) ve "Yeni başlangıç girdileri" uyarısı (Değişiklikler sekmesine derin bağlantı).
- **Nav rozeti:** Analizör öğesinde okunmamış "Eklendi" sayısı.
- **Tepsi bildirimi:** Zamanlanmış taramada imzasız ya da riskli yeni girdi varsa: "Başlangıca 1 yeni imzasız program eklendi · İncele".
- **Komut paleti:** `geçmiş <ad|hash>` ile arama; "Son analizi aç".
- **ThreatAnalysisDialog:** Başlığa "Bu dosya daha önce 2 kez analiz edildi · Geçmişi gör" bağlantısı.

### 6.7 Kabul kriterleri

1. Analizör, Nöbetçi, Kaldırıcı, Süreçler ve Başlangıç'tan yapılan analizlerin hepsi doğru kaynak etiketiyle Geçmiş'te görünüyor.
2. Uygulama yeniden başlatıldığında geçmiş korunuyor. Bozuk bir satır geçmişin tamamını bozmuyor.
3. Aynı hash 7 gün içinde yeniden VT'ye sorulmuyor (önbellek).
4. İki kalıcılık taraması arasında eklenen, kaldırılan ve değişen girdiler doğru listeleniyor (birim testleri: `SnapshotDiff`).
5. 20.000 kayıtta Geçmiş sekmesi 1 sn içinde açılıyor ve arama 100 ms altında sonuç veriyor.
6. "Geçmişi temizle" her şeyi siliyor; dışa aktarma CSV, JSON ve HTML üretiyor.

---

## 7. Etkinlik Merkezi

### 7.1 Amaç

Bakım'ın sistemde yaptığı **her değişikliğin** tek bir kaydı ve geri alma noktası. Kapsam:
- Temizlik
- Kaldırma
- İnce ayar
- Hizmet ve başlangıç değişikliği
- Firewall kuralı
- Karantina
- Oyun Modu oturumu
- Kurulum (Nöbetçi)
- Analiz (Analizör Geçmişi'ne bağlantı)
- Güncelleme

Her modülün kendi "geçmiş" ekranı, bu altyapının filtrelenmiş bir görünümüdür:
- Kaldırma Geçmişi = `Kind == Uninstall`
- Kurulum Geçmişi = `Kind == SetupSession`

### 7.2 Model

```csharp
public enum ActivityKind { Clean, Uninstall, SetupSession, Tweak, StartupChange, ServiceChange, FirewallRule,
                           Quarantine, Restore, GameModeSession, StoreInstall, StoreUpdate, Analysis, PersistenceScan,
                           ScheduledJob, AppUpdate, Other }
public enum ActivityOutcome { Succeeded, PartiallySucceeded, Failed, Cancelled }
public enum UndoState { NotUndoable, Undoable, Undone, UndoFailed, Expired }

public sealed record ActivityEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime AtUtc { get; init; }
    public ActivityKind Kind { get; init; }
    public string Module { get; init; } = "";                  // "Temizleyici"
    public string Title { get; init; } = "";                   // "Tarayıcı önbellekleri temizlendi"
    public string Summary { get; init; } = "";                 // "3.140 dosya · 1,2 GB · 12 atlandı"
    public ActivityOutcome Outcome { get; init; }
    public UndoState Undo { get; init; }
    public string? UndoHandler { get; init; }                  // "registry-reg-import", "recycle-bin", "tweak-snapshot", "service-config"
    public string? PayloadPath { get; init; }                  // journal klasörü (yedekler, ayrıntılar)
    public string? DeepLink { get; init; }                     // "Uninstaller/History?id=…"
    public IReadOnlyList<ActivityItem> Items { get; init; } = [];   // ilk 200 öğe; tamamı payload'da
}
public sealed record ActivityItem(string Target, string Action, string Result, string? Detail);
```

- **Depolama:** `%LocalAppData%\Bakim\History\Activity\activity.jsonl` + `journals/{id}/` (yedekler: `.reg`, görev XML'leri, eski değerler JSON). KAL A3'teki `UndoJournal` bu klasör yapısını kullanır.
- **Geri alma işleyicileri** (`IUndoHandler`, anahtar ile kayıtlı):

  | İşleyici | Ne yapar |
  |---|---|
  | `registry-reg-import` | `.reg` dosyalarını geri yükler |
  | `registry-values` | Değer düzeyinde eski değerleri yazar (ince ayarlar) |
  | `service-config` | Başlangıç türünü geri alır |
  | `scheduled-task-xml` | Görevi XML'den yeniden oluşturur |
  | `startup-approved` | Başlangıç durumunu geri alır |
  | `firewall-rule` | Kuralı kaldırır |
  | `quarantine-restore` | Karantinadan geri yükler |
  | `recycle-bin` | Otomatik geri yükleme yapılamaz; Geri Dönüşüm Kutusu açılır, dosyalar listelenir |

- Geri alma işlemi de yeni bir `ActivityEntry` olarak kaydedilir (`Kind = Restore`).

### 7.3 Arayüz

```
┌ Etkinlik Merkezi ───────────────────────────────────────────────────────────┐
│ FilterBar: [Ara…] Tür ▾  Sonuç ▾  (Geri alınabilir)  Tarih ▾   [Dışa aktar]  │
├─────────────────────────────────────────────────────────────────────────────┤
│ BUGÜN                                                                         │
│ ✦ 14:40 Temizleyici · Tarayıcı önbellekleri temizlendi                         │
│         3.140 dosya · 1,2 GB · 12 atlandı                     [Ayrıntı]        │
│ ⊟ 13:02 Kaldırıcı · "Foo Toolbar" kaldırıldı · 14 kalıntı temizlendi           │
│         Kayıt defteri yedeği var                          [Geri al] [Ayrıntı]  │
│ ✎ 12:30 Windows Ayarları · 6 gizlilik ayarı uygulandı                          │
│                                                           [Geri al] [Ayrıntı]  │
│ ⛨ 10:15 Nöbetçi · "7-Zip 24.08" kuruldu · Temiz                  [Raporu aç]   │
│ ⌕ 09:12 Analizör · Kalıcılık taraması · 2 yeni girdi         [Farkları gör]   │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Kabul:**
- Faz 3 sonunda Temizleyici, Kaldırıcı, Tweaker, Başlangıç ve Hizmetler kayıt yazıyor.
- Registry içeren her değişiklik tek tıkla geri alınabiliyor.
- Geri alma sonucu dürüstçe raporlanıyor.

---

## 8. Birleşik hata kataloğu

Tüm belgelerdeki bulgular burada tek listede toplanır. **Durum** sütunu uygulandıkça güncellenir: Açık / Devam / Tamam / Karar.

### 8.1 Güvenlik ve veri kaybı (Faz 1)

| Kod | Özet | Yer | Kaynak | Durum |
|---|---|---|---|---|
| S-01 | Kaldırıcı: tek token alt dize eşleşmesi %85; "%100 Güvenli" etiketi | `ResidualScannerEngine.cs:431`, `:552` | KAL U-P0-1 | Tamam |
| S-02 | Kaldırıcı: yayıncı + token = %100, toplu modda onaysız silme | `CalculateMatchConfidence`, `ExecuteAutoCleanResidualsAsync` | KAL U-P0-2 | Tamam |
| S-03 | Sağ tık: hedefin üst klasörü (İndirilenler, Masaüstü) %100 silinecek | `ShellUninstallResolverService.SynthesizeHeuristicApp`, `ScanHeuristicResidualsAsync` | KAL U-P0-3 | Tamam |
| S-04 | Süreç sonlandırma kapsamı doğrulanmıyor (`C:\Windows`), önek hatası | Sihirbaz ve DeepUninstaller `KillProcessesForApp` | KAL U-P0-4 | Tamam |
| S-05 | Korumalı klasör kontrolü yalnızca ada bakıyor | `IsProtectedDirectory` ×2 | KAL U-P0-5 | Tamam |
| S-06 | Kaldırıcı bitişi yanlış bekleniyor; iptalde kurulu programın klasörü öneriliyor | `LaunchUninstallAsync`, `StartUninstallAsync` | KAL U-P0-6 | Tamam |
| S-07 | Sahte registry yedeği (`[-Anahtar]`) | `ExportRegistryBackupSafe` | KAL U-P0-7 | Tamam |
| S-08 | Hive ayrıştırma (`HKCU` → HKLM), 32/64 görünüm, değerin anahtar gibi silinmesi | `DeleteRegistryKeySafe` ×4 | KAL U-P0-8 | Tamam |
| S-09 | Nöbetçi geri alması `Changed` dosyaları siliyor | `SetupSentinelService.cs:354`, `:495`, `:666` | NÖB P0-1 | Tamam (Gemini f670790; doğrulandı) |
| S-10 | Mağaza: doğrulanmamış üçüncü taraf ikililer yönetici olarak, sabit temp adı | `StoreService.cs:960-1215` | DEN G-2 | Tamam |
| S-11 | Yönetici kısayolu: kullanıcının yazabildiği exe'ye HIGHEST görev (UAC atlatma), VBS | `ContextMenuShortcutsService.cs:195-235` | DEN G-3 | Tamam |
| S-12 | Güvenilir yayıncı alt dize eşleşmesi | `FileThreatAnalyzerService.cs:32`, `:182` | DEN G-4 | Tamam |
| S-13 | Temizleyici `IsSafeTarget` alt dize allowlist'i (herhangi bir `\temp\`, `\cache\`) | `SystemCleanService.cs:852-887` | DEN H-5 | Tamam |
| S-14 | Güncelleyici: ölü `DownloadAndApplyUpdateAsync` (alan adı kontrolü yok, PowerShell'e yol gömme), "herhangi bir .exe" asset'i | `GitHubUpdateService.cs:180-240`, `AutoUpdateService.cs:176-185` | DEN G-1 | Tamam (imza kontrolü yok: G-1) |
| S-15 | Olay günlüklerini temizleme özelliği (adli iz yok etme) | `CrashAnalyzerViewModel.ClearEventLogsAsync` | yeni | Tamam |
| S-16 | Güvenliği azaltan ayarlarda rozet/onay yok | `BehaviorTweaksService.cs:123-212` | DEN G-5 | Tamam (WU "5 hafta duraklat" seçeneği Faz 8'e) |
| S-17 | TrustedInstaller sessizce yöneticiye düşüyor | `SystemToolsService.cs:163-200` | DEN G-6 | Açık (bu turda ele alınmadı) |

### 8.2 Dürüstlük (Faz 1)

| Kod | Özet | Yer | Kaynak | Durum |
|---|---|---|---|---|
| D-1 | CPU/GPU sıcaklığı formülle uyduruluyor | `TelemetryService.cs:424-446` | DEN D-1 | Tamam |
| D-2 | Disk okunamazsa sahte "C: %60" | `SystemCleanService.cs:86-95` | DEN D-2 | Tamam |
| D-3 | 7 tweak servisi yazma hatasını yutup `true` dönüyor | `*TweaksService.cs` | DEN D-3 | Tamam |
| D-4 | Bloatware kaldırma sonucu doğrulanmıyor | `PrivacyDebloatService.cs:338-362` | DEN D-4 | Tamam |
| D-5 | Mağaza "Kuruldu" çıkış koduna bakmıyor | `StoreService.cs` | DEN D-5 | Tamam |
| D-6 | RAM "serbest bırakıldı" metriği yanıltıcı | `SystemCleanService.cs:805-845` | DEN D-6 | Tamam |
| D-7 | Oyun Modu logu "servisler donduruldu" diyor | `GameModeService.cs:36` | DEN D-7 | Tamam |
| D-8 | Kaldırıcı "%100 Güvenli" ve sahte yedek | KAL | DEN D-8 | Tamam |
| D-9 | TRIM: 10 sn sonra `ExitCode`, HDD kontrolü yok | `SystemInfoService.cs:800-815` | DEN D-9 | Tamam |
| **D-10** | **Tek tık hızlandır: boşaltılan 0 ise "450 MB" gösteriliyor** | `DashboardViewModel.cs:342` | **yeni** | Tamam |
| **D-11** | **RAM fonksiyonları başarısızlıkta 50/100 MB döndürüyor** | `SystemCleanService.cs:579`, `:610`, `:641` | **yeni** | Tamam |
| **D-12** | **"Görsel kadans" için yapay `Task.Delay` (11 yer); "Çöp toplayıcı çalıştırılıyor" aşaması Bakım'ın kendi GC'si** | `DashboardViewModel.cs:313-345`, `CleanerViewModel.cs:403` vd. | **yeni** | Tamam |
| **D-13** | **Hızlı eylemler işlem bitmeden "başarılı" bildirimi gösteriyor** | `MainViewModel.ExecuteQuickAction`, tepsi | **yeni** | Tamam |
| **S-18** | **"Kalıntıları İncele" hâlâ kurulu programın klasörünü öneriyor ve programı kaldırılmış sayıyordu** | `UninstallerViewModel.ShowResidualCleanupDialogAsync` | **yeni** | Tamam |

### 8.3 Kesin hatalar (Faz 2)

| Kod | Özet | Yer | Durum |
|---|---|---|---|
| H-1 | Başlangıç: 32 bit HKLM için `StartupApproved\Run32` yerine yanlış yol | `StartupService.cs:95-99` | Açık |
| H-2 | Firefox kategorisi `cache2`'yi bulmuyor | `SystemCleanService.cs:266-275` | Tamam |
| H-3 | Spotify çevrimdışı şarkılar varsayılan seçili | `SystemCleanService.cs:331-340` | Tamam |
| H-4 | WU indirmeleri servis durdurulmadan; temp'te yaş filtresi yok | `SystemCleanService.cs` | Tamam (WU için servis durdurma yerine 24 sa yaş filtresi) |
| H-6 | Optimizer handle sızıntısı, `CpuTracker` büyümesi | `OptimizerViewModel.cs:585-700` | Açık |
| H-7 | Korunan süreç listeleri eksik ve 4 farklı kopya | çeşitli | Tamam |
| H-8 | Oyun Modu önceki güç planını geri yüklemiyor | `GameModeService.cs:39`, `:65` | Tamam |
| H-9 | Boş klasörler varsayılan seçili, AppData dahil, kök silinebilir | `DuplicateFinderService.cs:380-505` | Açık |
| H-10 | Yinelenenler OneDrive yer tutucularını indiriyor | `DuplicateFinderService.cs:238-300` | Açık |
| H-11 | 3 ayrı geri yükleme noktası uygulaması, görünür PowerShell | `PrivacyDebloatService`, `DeepUninstallerService` | Açık |
| H-13 | Tweak anlık görüntüsü değer düzeyinde değil | `TweaksSnapshotService.cs` | Açık |
| H-14 | Kullanılmayan giriş ekranı; parolasız hesap sıfırlama | `LoginViewModel.cs:225` | Karar |
| H-15 | Sürüm 7 yerde sabit | çeşitli | Tamam |
| H-16 | `--uninstall-target` modunda arka plan servisleri başlıyor | `App.xaml.cs:143-187` | Açık |
| H-17 | Nöbetçi P0-2…P0-10 (tespit, snapshot yarışı, Run/servis yakalanmıyor, hive, depolama çakışması, eşzamanlılık, msiexec, kaldırıcı ayrımı, FSW taşması) | `SetupSentinelService.cs`, `InstallerMonitorService.cs` | Açık |
| H-18 | Kaldırıcı P1'leri (tırnaksız komut, toplu başarı, geri yükleme noktası, sessiz arg, liste performansı) | KAL U-P1-* | Açık |
| H-19 | Sağ tık S-1…S-9 (UAC, çoklu seçim, eşleşme puanı, MSI kısayolu, stale komut yolu) | KAL 1.3 | Açık |
| H-20 | `DefaultCurrentVersion`, `UpdateInfo.CurrentVersion` gibi alanlarda sabit sürüm; `GitHubUpdateService.UpdateCheckResult.CurrentVersion = "2.5.0"` | güncelleyici | Tamam |

### 8.4 Performans ve mimari (Faz 2–3)

DEN P-1…P-8 ve M-1…M-12. Özet:
- tek telemetri örnekleyici
- timer yeniden girme koruması
- RAM trim'in kaldırılması
- `catch { }` azaltma
- kopya yardımcıların birleştirilmesi
- çift motorlar
- `IDialogService`
- dev sınıfların bölünmesi
- statik servisler
- `IElevationBroker`
- `ProcessRunner`
- sabit renkler ve yazı boyutları
- CI
- tanılama paketi

---

## 9. Fazlar ve sürümler

Her faz ayrı bir PR'dır. PR ancak §10'daki PC test listesi temiz geçince merge edilir.

| Faz | Sürüm | İçerik | Bağımlılık |
|---|---|---|---|
| **1: Güvenlik ve Dürüstlük** | v3.21.0 | §4 çekirdeğinin güvenlik kısmı (PathSafetyGuard, RegistryPath, CriticalProcessPolicy, ByteFormatter, NameMatcher, UninstallCommandParser, Safe servisleri, RestorePointService, RegistryWriter); S-01…S-17; D-1…D-12; `Bakim.Core.Tests`; CI; H-15 | — |
| **2: Kesin hatalar ve altyapı** | v3.22.0 | H-1…H-20; ProcessRunner, ElevationBroker, DialogService, TelemetryHub; P-1…P-8; `catch { }` azaltma (1. tur) | Faz 1 |
| **3: Geçmiş altyapısı** | v3.23.0 | **Analizör Geçmişi** (§6), **Etkinlik Merkezi** (§7), UndoJournal işleyicileri; Temizleyici, Kaldırıcı, Tweaker, Başlangıç ve Hizmetler kaydı | Faz 1 |
| **4: Tasarım sistemi v2 ve IA** | v3.24.0 | §3 token'ları, bileşenler, şablonlar, doğrulayıcılar; yeni navigasyon (§2); Depolama ve Windows Araçları taşımaları; FontSize ve renk göçü | Faz 2 |
| **5: Modül yenilemeleri I** | v3.25.0 | Kontrol Paneli, Temizleyici (JSON kurallar), Depolama (Disk Haritası), Süreçler, Oyun Modu | Faz 4 |
| **6: Kaldırıcı v2** | v3.26.0 | KAL B, C, D fazları | Faz 1, 3 |
| **7: Nöbetçi v2** | v3.27.0 | NÖB Faz 1–7 | Faz 1, 3 |
| **8: Modül yenilemeleri II** | v3.28.0 | Tweaker (veri tabanlı TweakEngine), Başlangıç, Hizmetler, Ağ, Olaylar, Mağaza (winget), Sistem Bilgisi, Ayarlar, Tepsi, Komut Paleti | Faz 4 |
| **9: Tam Koruma Modu** | v4.0.0 | NÖB Faz 8 (servis, USN, ETW), BakimShell ve yönetici modeli kararı, Sandbox önizleme (NÖB Faz 10) | Faz 7 |

Paralel çalışılabilecek fazlar: 3 ile 4, 6 ile 7.

> **Sürüm kayması:** Gemini'nin Nöbetçi Faz 0 commit'i (f670790) main'e v3.20.0 olarak girdiği için Faz 1 v3.21.0 oldu; sonraki fazlar birer kaydı. O commit bazı dosyalarda Türkçe karakterleri bozmuştu (ör. "Klasr"); RollbackPlanner, SessionStore, RegistryHotspotSensor ve ProcessInfoReader düzeltildi.

---

## 10. Test stratejisi

### 10.1 Otomatik testler

| Katman | Proje | Nerede koşar | İçerik |
|---|---|---|---|
| Saf mantık | `Tests/Bakim.Core.Tests` (net10.0) | Linux + Windows CI | PathSafetyGuard, RegistryPath, NameMatcher, UninstallCommandParser, CriticalProcessPolicy, StartupApprovedPaths, ByteFormatter, SnapshotDiff, AnalysisRecord JSONL okuma/yazma, sağlık puanı hesabı, güvenilir yayıncı tam eşleşmesi |
| Windows servisleri | `Tests/Bakim.Tests` (mevcut) | Windows CI | Registry (HKCU test anahtarları), Safe servisleri, RegistryWriter doğrulaması, DI grafiği |
| UI duman | `Tests/Bakim.UiSmokeTests` (mevcut) | Windows CI | Her modül açılıyor, binding hatası 0 |
| Kanarya | `Tests/Bakim.IntegrationTests` (yeni) | Windows CI (yönetici) | Temizleyici, Boş Klasör ve Kaldırıcı benzer adlı kanaryalara dokunmuyor |

### 10.2 PC test listeleri (her faz sonunda, proje sahibi)

**Faz 1:**
- [ ] Uygulama açılıyor; bütün modüller hatasız geziliyor.
- [ ] Kontrol Paneli'nde sıcaklık "—" (sensör yoksa). "Hızlı Bakım" gerçek sonucu gösteriyor, uydurma MB yok.
- [ ] Kaldırıcı: Masaüstündeki taşınabilir bir exe'ye sağ tık → "Bakım ile Kaldır" → İndirilenler ya da Masaüstü klasörü **listede yok.**
- [ ] Kaldırıcı: Küçük bir program (örn. Notepad++) kaldırılıyor. Resmi kaldırıcıda **İptal**'e basılırsa "Kaldırma tamamlanmadı" ekranı çıkıyor ve klasör önerilmiyor.
- [ ] Kaldırıcı: `C:\Windows\notepad.exe`'ye sağ tık → "Korumalı" mesajı. Hiçbir süreç kapanmıyor.
- [ ] Kaldırıcı: Temizlik sonrası kayıt defteri yedeği `.reg` dosyası açıldığında **değerleri içeriyor.**
- [ ] Tweaker: Yönetici değilken HKLM gerektiren bir ayar "Yönetici gerekli" gösteriyor. Uygulanamazsa "uygulandı" demiyor.
- [ ] Mağaza: VC++ paketi winget üzerinden kuruluyor. Hata olursa "Kuruldu" demiyor.
- [ ] Analizör: "Hamdi Yazılım" gibi imzacılar artık "Güvenilir Yayıncı" sayılmıyor (varsa test dosyasıyla).
- [ ] Kenar çubuğunda "BAKIM v3.21.0" yazıyor; Ayarlar > Sürüm Geçmişi en üstte v3.21.0.
- [ ] Temizleyici: Firefox önbelleği bulunuyor, Spotify seçili gelmiyor; `%TEMP%`'e bugün kopyalanan bir dosya taramada çıkmıyor.
- [ ] Gizlilik: yönetici olmadan "Telemetri verileri" → hata ve nedeni gösteriliyor, anahtar açık kalmıyor.
- [ ] Gizlilik: bir bloatware kaldır → gerçekten kalkmışsa "kaldırıldı", kalkmamışsa nedeni.
- [ ] Hizmetler: bir hizmeti yeniden başlat → tek UAC (yönetici değilken), sonuç doğru.
- [ ] Sistem Bilgisi: HDD'de TRIM → "HDD; TRIM yalnızca SSD'de"; SSD'de başarılı mesajı.
- [ ] İnce Ayarlar: "SmartScreen'i kapat" kırmızı "Güvenliği azaltır" rozeti taşıyor ve açarken ayrı onay soruyor.
- [ ] Yönetici kısayolu: `%LocalAppData%` altındaki bir exe için reddediliyor; `C:\Program Files\...` altındaki bir exe için masaüstünde `.lnk` oluşuyor ve UAC sormadan açılıyor.
- [ ] Analizör: bir test dosyasını "Dosyayı Kaldır" → Geri Dönüşüm Kutusu'nda. `svchost.exe` analizinde "Süreci Sonlandır" reddediliyor.
- [ ] Oyun Modu: güç planını not al → aç → kapat → aynı plan geri geliyor.
- [ ] Çökme Analizi: "Olay Görüntüleyici" düğmesi eventvwr'ı açıyor; günlük silme düğmesi yok.

**Faz 2:**
- [ ] Başlangıç: 32 bit bir programı (örn. Steam) kapat → yeniden başlat → açılmıyor; Görev Yöneticisi de "Devre dışı" gösteriyor.
- [ ] Temizleyici: Firefox önbelleği bulunuyor. Spotify seçili gelmiyor.
- [ ] Oyun Modu: Önceki güç planı (örn. Dengeli / özel plan) kapatınca birebir geri geliyor.
- [ ] Süreçler sayfası 10 dk açık kalınca Görev Yöneticisi'nde Bakım'ın handle sayısı artmıyor.
- [ ] Tepsideyken 10 dk boyunca Bakım'ın CPU kullanımı ≈ %0.

**Faz 3:**
- [ ] Analizör'de bir dosya analiz et → uygulamayı kapat/aç → Geçmiş sekmesinde görünüyor.
- [ ] Kurulum Nöbetçisi → "Analizörle tara" → dosyalar ekrandaki Analizör listesine geliyor; Geçmiş'te kaynak "Nöbetçi", ayrıntıda "Kurulum: <ad>".
- [ ] Geçmiş'te bir kayda "Güven" → aynı dosyayı yeniden analiz et → yeni kayıt da "Güvenildi"; analiz penceresi "daha önce N kez analiz edildi" diyor.
- [ ] Geçmiş > Dışa aktar → CSV Excel'de Türkçe karakterlerle açılıyor; HTML tarayıcıda açılıyor.
- [ ] VirusTotal anahtarı varken aynı dosyayı 2. kez sorgula → sonuç anında geliyor ("… gün önce"/"bugün" ibaresiyle).
- [ ] İki kalıcılık taraması arasında test amaçlı bir Run girdisi ekle → Değişiklikler sekmesinde "Eklendi".
- [ ] Temizlik, kaldırma ve ince ayar işlemleri Etkinlik Merkezi'nde görünüyor. İnce ayar "Geri al" ile eski haline dönüyor.

**Faz 4 ve sonrası:** Tema (Mica Koyu, AMOLED, Açık, Yüksek Kontrast) × DPI (%100, %150) matrisinde her modül kontrol edilir; ekran görüntüleri PR'a eklenir.

---

## 11. Karar bekleyen konular

| Konu | Seçenekler | Öneri | Etkilediği faz |
|---|---|---|---|
| Yönetici modeli | RUNASADMIN (mevcut) / `requireAdministrator` / asInvoker + görev + broker | asInvoker + görev + `IElevationBroker` | 2, 9 |
| Giriş ekranı | Kaldır / yeniden tasarla | Kaldır | 2 |
| Sıcaklık kaynağı | Göster(me) / GPU vendor API / LibreHardwareMonitor | Faz 1'de "—", Faz 8'de GPU vendor API | 1, 8 |
| RAM temizleme | Kaldır / bekleme belleği (yönetici) | Bekleme belleği, varsayılan kapalı, otomatik değil | 1, 5 |
| Mağaza üçüncü taraf paketleri | winget / kaldır | winget | 1 |
| Olay günlüğü temizleme | Kaldır / dışa aktarım + çift onay | Kaldır | 1 |
| Tweak'lerin JSON'a taşınması | Evet / hayır | Evet (Faz 8) | 8 |
| Windows App SDK (toast, pencere) | Ekle / ekleme | İleri faz; şimdilik WPF-UI | 8 |

**Kararlar gelene kadar:** Faz 1'de "Öneri" sütunu uygulanır. Geri dönüşü kolay olan seçenekler seçilmiştir (özellik gizlenir ya da varsayılan kapatılır, kod silinmez).

---

## 12. İlerleme tablosu

Bu tablo her PR ile güncellenir.

| Faz | Durum | Branch / PR | Not |
|---|---|---|---|
| Planlama | Tamam | Eyupbayuk31/Bakim#1 (planlar) | DEN, KAL, NÖB |
| Master plan | Tamam | bu belge | |
| Faz 1 | Tamam (PC testi bekliyor) | `claude/charming-feynman-g2crt8` | v3.21.0. S-17 açık. Core testleri: 218 (Linux) |
| Faz 2 | Tamam (PC testi bekliyor) | `claude/charming-feynman-g2crt8` | H-1, H-6, H-9, H-10, H-13, H-16, H-17 (P0-3), H-18, H-19 (S-6/7/8); msiexec yanlış kurulum bildirimi; tek telemetri örnekleyici, zamanlayıcı koruması, tanılama paketi |
| Faz 3 | Tamam (PC testi bekliyor) | `claude/charming-feynman-g2crt8` | Analizör Geçmişi + **Etkinlik Merkezi**: Temizleyici, Kaldırıcı, Windows Ayarları, Gizlilik, Başlangıç, Analizör, Hizmetler, Güvenlik duvarı, Nöbetçi ve Oyun Modu kayıt yazıyor; registry-values / reg-import / service-config / firewall-rule / recycle-bin geri alma; UndoToast |
| Faz 4 | Tamam (PC testi bekliyor) | `claude/charming-feynman-g2crt8` | Gruplu kenar çubuğu, rozetler, son sayfa, Alt+←/→; Depolama, Windows Araçları, Oyun Modu, Kurulum Nöbetçisi sayfaları; v2 anlamsal token'lar (Surface/Border/Text/Status/Risk/Chart), Yüksek Kontrast, Windows vurgu rengi, animasyonları azalt; tipografi ölçeği + 1.059 FontSize ve 330 CornerRadius göçü; RiskBadge, StatusPill, SensorValue, AdminGate, KeyValueGrid, ResultCard, SkeletonRow; `verify-design-debt.py` cırcırı |
| Faz 5 | Tamam (PC testi bekliyor) | `claude/charming-feynman-g2crt8` | Kontrol Paneli: kalıcı sinyallerle sağlık puanı (uydurma girdiler kaldırıldı), Hızlı Bakım, son etkinlikler. Temizleyici: tüm tarayıcı profilleri, Teams/VS Code/Geri Dönüşüm Kutusu, açık uygulama uyarısı, haftalık güvenli temizlik. Depolama: Disk Haritası (treemap), hardlink tanıma. Süreçler: askıya alınan süreç defteri, uygulamaya göre gruplama, dürüst bellek eylemi. Oyun Modu: profil ve otomatik tetikleme. Kalan: Temizleyici kurallarının JSON'a taşınması (Faz 8 TweakEngine ile birlikte) |
| Faz 6 | Tamam (PC testi bekliyor) | `claude/charming-feynman-g2crt8` | Kaldırıcı v2: kaldırma öncesi iz toplama (hizmet/sürücü, zamanlanmış görev, Run/RunOnce, App Paths, güvenlik duvarı kuralı, kısayol; kurulum klasörü InstallLocation yoksa kaldırıcı yolundan çıkarılır), kaldırma sonrası hâlâ duranlar kesin kalıntı; hizmet/görev/kural yedekli tek UAC silme ve Etkinlik Merkezi'nden geri alma; nöbetçi koordinasyonu (D5); NoRemove; tek örnek + sağ tık isteğinin açık örneğe iletilmesi (C2); tarama kökleri (Kayıtlı Oyunlar, profil nokta klasörleri); Kaldırıcı → Geçmiş (Etkinlik Merkezi filtreli). Kalan: BakimShell (C3, Faz 9), 7 adımlı sihirbaz görseli (D2), MSI bileşen kanıtı |
| Faz 7 | Tamam (PC testi bekliyor) | `claude/charming-feynman-g2crt8` | Nöbetçi v2: tur başına tek süreç görüntüsü + aday penceresi (eskiden her süreç için ayrı Toolhelp görüntüsü), kurulum çatısı parmak izi (Inno/NSIS/WiX/InstallShield/Advanced/7z/Squirrel/MSI) ve MOTW kaynağıyla puanlama; kurulum dosyası SHA-256 + imza + indirildiği site; sistem sensörü (IFEO, Winlogon, AppInit, PATH, proxy, tarayıcı politikaları, Defender istisnası, fiziksel kök sertifika depoları, hosts, güvenlik duvarı, görevler, sağ tık uzantıları); gürültü kuralları; kural tabanlı risk motoru (Temiz/Bilgi/Dikkat/Şüpheli/Tehlikeli, ATT&CK etiketli bulgular), paket yazılım tespiti; rapor "Özet" ve "Sistem" sekmeleri, bildirimde ilk 3 bulgu, sayfada karar rozeti, bildirim seviyesi ayarı, Oyun Modu'nda sessiz; Kaldırıcı'da "Kurulum izi var" rozeti ve rapordaki klasörlerin kalıntı adayı olması. Kalan: tek tık müdahaleler (5.2), karantina (5.3), ağ sensörü (2.6), USN/ETW (Faz 9) |
| Faz 8–9 | Bekliyor | | |
