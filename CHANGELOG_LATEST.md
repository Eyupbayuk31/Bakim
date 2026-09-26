# Bakım v4.2.1 - Sürüm Notları

## Windows 11 SelectorBar Sekmeleri, Tema Vurgu Senkronizasyonu & Temizleyici Bağlama Onarımı

Bakım v4.2.1 sürümü; kullanıcı arayüzünde tespit edilen bağlama hatalarını gidermekte, 27 sayfa sekmesini modern Windows 11 Fluent SelectorBar stiline kavuşturmakta ve WPF-UI bileşenlerinin tema vurgu renkleriyle %100 senkronize çalışmasını sağlamaktadır.

---

### 1. Temizleyici Kategori Düğmeleri Bağlama Onarımı
- **Doğru Filtreleme Özelliği:** Temizleyici modülünde ViewModel'de yer almayan `SelectedCategoryGroup` özelliği yerine doğru `CategoryGroupFilter` bağlaması yapıldı.
- **Mavi Buton Düşmesi Giderildi:** Hatalı bağlama nedeniyle butonların WPF-UI varsayılan rengine (`Primary` mavi) düşmesi engellendi; seçili kategori zarifçe vurgulanmaktadır.

---

### 2. Sessiz XAML Bağlama Denetçisi (`Tools/verify-bindings.py`)
- **CI Doğrulama Kapısı:** XAML görünümleri ile C# ViewModel'leri arasındaki tüm veri bağlamalarını statik olarak denetleyen yeni doğrulama aracı geliştirildi ve CI hattına eklendi.
- **Sıfır Sessiz Hata:** ViewModel'de karşılığı bulunmayan veya yanlış yazılan bağlamalar artık derleme öncesi otomatik yakalanır.

---

### 3. Windows 11 SelectorBar Sekme Tasarımı (`Tab.Item`)
- **Modern Alt Çizgili Vurgu:** 27 sayfa sekmesi (Ağ İzleyici, Çökme Analizörü, Donanım & S.M.A.R.T., Hizmetler, Depolama vb.) eski dolu kutu butonlardan modern Windows 11 SelectorBar (alt accent çizgili) stiline taşındı.
- **Sade ve Ferah:** Sekmeleri çevreleyen hantal kutu çerçeveler kaldırıldı.

---

### 4. WPF-UI Tema Vurgu Rengi Senkronizasyonu (`ThemeService`)
- **Bileşen Düzeyinde Eşitleme:** Uygulama içi buton, onay kutusu (CheckBox) ve ToggleSwitch bileşenlerinin Windows sistem mavisi yerine seçili tema vurgu rengini alması sağlandı.

---

### 5. Depolama & Disk Haritası İlk Başlatma İyileştirmesi
- **Otomatik Sistem Sürücüsü Seçimi:** Disk haritası açıldığında taranacak sürücünün boş başlaması engellendi; sistem sürücüsü (C:) otomatik seçili getirildi.
- **Format Düzeltmesi:** Depolama alanındaki `143,2 GB Boş Boş` metin çiftleme hatası giderildi; `StatCard` renkli çerçeveleri sadeleştirildi.
