# Bakım v3.18.0 - Sürüm Notları

## Yazılım Mağazası Kart Mimarisi Revizyonu, Ayrık Çizgiler, Çift Görünüm Modu & Fluent İkonlar 🛒🎨

### 1. Kusursuz Kenarlıklı Kart Mimarisi (Solid Border Card Overhaul)
- **Stil Çakışması Kökten Giderildi:** Mağaza kartlarındaki sınır çizgisi kaybına ve havada asılı durma ("hipnoz") hissiyatına neden olan stil şablonu çakışması native `<Border>` mimarisi ile çözüldü.
- **Slate Dark Yüzey & Derinlik:** Kart arka planı `#1E293B`, belirgin `#334155` ince kenarlık çizgisi ve `CornerRadius="10"` kavisleri ile tam kurumsal Windows 11 Fluent 2 derinliğine kavuşturuldu.
- **Akıllı Hover ve Seçim Efektleri:** Fare ile üzerine gelindiğinde `#38BDF8` parlak mavi kenarlık ve arka plan aydınlatması; seçildiğinde ise kalıcı zümrüt/mavi kenarlık vurgusu sağlandı.

### 2. İç Ayırıcı Çizgi (Card Interior Separator)
- **Net Ayrım Çizgisi:** Her kartın gövdesinde yer alan uygulama açıklaması ile alt eylem/durum butonları arasına yatay ayrık çizgi (`#334155`) eklendi.
- **Göz Yormayan Netlik:** Başlık, kategori rozeti, açıklama ve alt eylem çubuğu birbirinden net şekilde ayrılarak uzun süreli gezinmede göz yorulması ve karmaşa tamamen engellendi.

### 3. Çift Görünüm Modu (Izgara / Kompakt Çizgili Liste)
- **Görünüm Değiştirici (View Switcher):** Arama ve filtre çubuğunun sağına eklenen görünüm butonları ile kullanıcılar iki farklı mod arasında tek tıkla geçiş yapabilir:
  - **Izgara (Kart) Modu:** Zengin ikonlar, renkli kategori etiketleri, açıklamalar ve ayrık butonlarla donatılmış ferah görsel kartlar.
  - **Kompakt Çizgili Liste Modu:** Yüksek yoğunluklu, zebra arka planlı, satır içi durum rozetleri ve doğrudan eylem butonları içeren modern tablo görünümü.

### 4. Resmi Windows 11 Fluent 2 İkon Standardı (Sıfır Emoji)
- **Çocuksu Emojiler Temizlendi:** Kategori haplarında bulunan emojiler (`🌟`, `⚡`, `🎮`, `🎵`, `🛠️`, `🌐`, `💬`, `💻`) tamamen kaldırıldı.
- **Doğrulanmış Fluent Sembolleri:** Yerine resmi Windows 11 Fluent 2 sembolleri entegre edildi:
  - Tümü: `Grid24`
  - Runtimes: `Flash24`
  - Oyun & GPU: `Games24`
  - Medya & Grafik: `MusicNote224`
  - Sistem & Güvenlik: `Wrench24`
  - Tarayıcılar: `Globe24`
  - İletişim: `Chat24`
  - Geliştirici: `Code24`

### 5. Format Kurtarıcı & Hazır Paket Kartlarının Standartlaştırılması
- Tab 1'deki "Hazır Paketler & Format Kurtarıcı" kartları da aynı sağlam kenarlık, Slate Dark kart derinliği ve ayırıcı çizgi mimarisine yükseltildi.

### 6. Pre-flight & Test Doğrulaması
- **147/147 Birim Testi:** Tüm testler %100 başarıyla geçti.
- **9.235 Sembol & Token %100 Geçerli:** `verify-symbols.py` ve `verify-tokens.py` ile sıfır hata doğrulandı.
- **Yerel .NET 10 SDK Release Derlemesi:** 0 Hata ve 0 Uyarı ile başarıyla tamamlandı.
