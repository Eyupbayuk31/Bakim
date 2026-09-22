# Bakım v3.16.2 - Sürüm Notları

## Büyük Dosya Analizörü Araç Çubuğu Tam Responsive Revizyonu

- **Tam Responsive WrapPanel Araç Çubuğu Düzeni:**
  - Pencere küçültüldüğünde veya farklı DPI ölçeklemelerinde Hedef Sürücü ComboBox'ı, Boyut Eşiği butonları ve Tarama/Durdurma butonlarının üst üste binmesi ve elemanların kesilme sorunu kökten çözüldü.
  - Sürücü seçimi ve birincil eylemler (`Büyük Dosyaları Tara` ve `Durdur`) en üst satıra bağımsız olarak taşındı.
  - Boyut Eşiği ve Tür Filtresi butonları `WrapPanel` içine alınarak ekran daraldığında alt satıra pürüzsüzce kayması sağlandı (sıfır taşma, sıfır çakışma).

- **Kompakt Boyut Eşikleri & Genişletilmiş Sıralama Seçici:**
  - Boyut eşiği butonları kompakt ve eşit genişlikte düzenlendi (`100 MB+`, `500 MB+`, `1 GB+`, `2 GB+`, `5 GB+`).
  - Sıralama seçici genişletilerek `Boyut (Büyükten)` metninin `Boyut (Büyükt...` olarak kırpılması engellendi.

- **Dinamik Tarama İlerleme Çubuğu:**
  - Tarama yapılmadığı bekleme durumlarında altta boş gri çizgi oluşturan ilerleme çubuğu gizlenerek kart içi yükseklik ve alan ferahlığı optimize edildi.
