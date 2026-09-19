---
trigger: always_on
description: "XAML Layout & Alignment Rules for WPF"
---

# XAML Layout & Hizalama Kuralları (Kritik Standartlar)

Bu kurallar bu projedeki ve tüm WPF/XAML tasarımlarındaki zorunlu mimari standartlarıdır:

1. **Sabit Ölçü Yasağı (Responsive Tasarım):**
   - Elemanlara (kartlar, butonlar, paneller, form kutuları) rastgele sabit `Width` veya `Height` verme.
   - Sadece esneklik gerektiğinde `MinWidth`, `MaxWidth`, `MinHeight`, `MaxHeight` sınırları kullanılabilir.
   - İçerik boyutunu ve buton yüksekliklerini `Padding` ve `LineHeight`/`FontSize` ile doğal olarak yönet.

2. **Grid Mimarisi:**
   - Hizalama ve düzen sorunlarını önlemek için tüm kart ve sayfa düzenlerini `Grid` (sütunlar ve satırlar için `*` ve `Auto`) veya yönlü akışlar için `StackPanel` ile yapılandır.
   - Orantılı dağılımlarda `*` ve `2*`, içerik kadar büyümesi gereken yerlerde `Auto` kullan.

3. **Margin Kaymalarını Önleme:**
   - Bir elemanı sayfada konumlandırmak veya hizalamak için kesinlikle büyük `Margin` değerleri (örneğin: `Margin="120,40,0,0"`) kullanma.
   - Konumlandırmayı her zaman `HorizontalAlignment="Stretch/Center/Left/Right"`, `VerticalAlignment="Center/Top/Stretch"`, ve `Grid.Row` / `Grid.Column` ile sağla.
   - Margin yalnızca elemanlar arasındaki bitişik mikro boşlukları (örn: `Margin="0,0,0,12"` veya `Margin="8"`) belirlemek için kullanılabilir.

4. **Tema Desteği (DynamicResource):**
   - XAML içinde asla hard-coded HEX renk kodları (ör. `#0F172A`, `#1E293B`, `White` vb.) doğrudan kontrollere yazılmamalıdır.
   - Renk ve fırça referansları `App.xaml` veya ResourceDictionary içindeki semantik anahtarlardan `{DynamicResource Brush.Window.Background}` formatında çekilmelidir.
   - Windows Koyu / Açık tema geçişlerine tam uyumlu olmalıdır.
