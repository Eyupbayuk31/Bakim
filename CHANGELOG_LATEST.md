# Bakım v3.18.1 - Sürüm Notları

## Gizlilik & Debloat Master-Detail Entegrasyonu, Kesintisiz Sol Menü & Fluent 2 Tasarım Devrimi 🛡️✨

### 1. Kesintisiz Sol Kategori Menüsü (Master-Detail Persistence)
- **Sol Menünün Yok Olması Kökten Çözüldü:** Windows Tweaker sol rayındaki ("KATEGORİLER 14") "Gizlilik & Debloat" seçeneğine tıklandığında uygulamanın harici sayfaya atlayıp sol menüyü yok etmesi sorunu giderildi.
- **Birinci Sınıf Entegre Kategori:** "Gizlilik & Debloat" doğrudan Tweaker Master-Detail çalışma alanının yerel bir kategorisi haline getirildi. Tıklandığında sol menü sabit kalarak aktif kategori neon mavi vurgu kazanır ve kullanıcı dilediği an diğer 13 kategoriye (Windows 11, Dosya Gezgini vb.) tek tıkla pürüzsüzce geri dönebilir.
- **Canlı Sayaç Rozeti (Dynamic Badge):** Sol menüdeki "Gizlilik & Debloat" satırı artık statik veya boş değil; aktif ve toplam koruma kuralını (örn: `6/12`) canlı olarak yansıtır.

### 2. Dörtlü Canlı KPI Gösterge Kartları
- **Gizlilik Koruma Skoru (%50):** Koruma yüzdesini dinamik renk kodlu (yeşil/sarı/kırmızı) başarı rozetiyle sunan modern metrik kartı.
- **Aktif Koruma Kuralı (6 / 12):** Telemetri ve veri toplama engellerinin anlık durumunu raporlar.
- **Önerilen Korumalar:** Güvenli ve stabil Microsoft temel koruma seviyesi.
- **Yüklü Bloatware:** Sistemde gereksiz RAM ve disk tüketen kurulu UWP paket sayısı.

### 3. Segmented Sub-Tab Switcher & Filtre Çipleri
- **Akıcı Mod Seçici:** Kart yüzeyinde yerel Windows 11 Fluent 2 segmented hap kontrolü ile "Gizlilik & Telemetri" ve "Bloatware Kaldırıcı" arasında tek tıkla geçiş.
- **Kategori Filtre Çipleri:** "Tümü", "Telemetri & Tanılama", "Reklamlar & Öneriler", "Konum & İzinler" butonları; her kategorinin kural adet rozetleri ve seçili çipte `#38BDF8` mavi vurgu.

### 4. Solid Border Tweak & Bloatware Kartları (Sıfır Hipnoz, Yüksek Netlik)
- **Sağlam Kart Mimarisi:** `#1E293B` Slate Dark kart tabanı, belirgin `#334155` gri sınır çizgisi, `CornerRadius="10"` ve fareyle üzerine gelindiğinde parıldayan neon mavi hover efekti.
- **İç Ayırıcı Çizgi (Card Separator):** Başlık, rozetler ve açıklama ile alt eylem/durum butonları arasına yatay ayrık çizgi çekilerek görsel karmaşa önlendi.
- **Risk Seviyesi Rozetleri:** "Önerilen Koruma" (yeşil), "Gelişmiş Kural" (kehribar) ve "İsteğe Bağlı" (mavi) rozetleriyle kullanıcıya net güvenlik bilgisi.
- **Durum Rozeti:** "Koruma Aktif" (yeşil onay) ve "Pasif / Açık" (gri çarpı) göstergeleri.

### 5. Toplu Güvenli Bloatware Temizliği
- **Tek Tıkla Toplu Kaldırma:** "Tüm Güvenli Bloatware'leri Kaldır" eylemi ile Hesap Makinesi ve Windows Mağazası gibi temel sistem bileşenleri güvenle korunarak, gereksiz tüm reklam ve OEM bloatware paketleri tek seferde temizlenir.
- **Gelişmiş Korumalı Rozeti:** Kritik sistem uygulamalarında sarı kilitli "Korumalı" rozeti ile yanlışlıkla silinme engellenir.

### 6. Pre-flight & Test Doğrulaması
- **147/147 Birim Testi:** Tüm testler %100 başarıyla geçti.
- **UI Smoke Testleri:** Tüm modül görünümleri, 4 tema ve diyaloglar sıfır hatayla doğrulandı.
- **9.235 Sembol & Token %100 Geçerli:** Sıfır geçersiz sembol ve eksiksiz token haritası.
- **Yerel .NET 10 SDK Release Derlemesi:** 0 Hata ve 0 Uyarı.
