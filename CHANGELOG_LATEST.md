# Bakım v3.15.4 - Sürüm Notları

## Donanım & Disk Modülü Kök Onarımı ve Akıllı Filtreleme Deneyimi

- **Komut Parametresi Kök Onarımı (String -> Int64 ArgumentException):**
  - Donanım ve Disk modülünde Büyük Dosya Analizörü açıldığında eşik butonlarının tip uyuşmazlığı (`ArgumentException: Parameter 'parameter' cannot be of type System.String, requires System.Int64`) nedeniyle patlaması sorunu kökten giderildi.
  - `SetThresholdAsync` komutu evrensel `object?` toleranslı ve güvenli dönüştürücülü hale getirilerek her türlü sayı ve metin girdisine karşı çökme korumalı (crash-proof) yapıldı.

- **Dinamik Eşik ve Filtre Vurgulaması (Active Chip Highlighting):**
  - Boyut Eşiği butonları (500 MB+, 1 GB+, 2 GB+, 5 GB+) artık seçili duruma göre anlık olarak parlıyor (`Primary` vurgusu), diğerleri sade kalıyor (`Secondary`).
  - Tür Filtresi butonları ("Tümü", "Videolar", "Disk İmajları", "Arşivler", "Kurulum / Oyun") seçili kategoriye göre dinamik olarak vurgulanıyor.

- **Dosya Yolunu Kopyalama:**
  - Büyük dosyalar listesine tek tıkla dosya yolunu panoya kopyalama aksiyonu (`CopyFilePathCommand`) eklendi.

- **Otomatik Test Güvencesi:**
  - `SystemInfoRevampTests` içerisine komut parametre toleransı ve dinamik seçim durumlarını doğrulayan yeni xUnit testleri entegre edildi (139/139 test başarılı).
