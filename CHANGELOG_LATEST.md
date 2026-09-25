# Bakım v3.21.0 - Sürüm Notları

## Güvenlik ve Dürüstlük Sürümü: Veri Kaybı Riskleri Kapatıldı, Uydurma Değerler Kaldırıldı & Analizör Geçmişi

### 1. Güvenli Kaldırıcı (Uninstaller v2 Temelleri)
- **PathSafetyGuard & RegistrySafetyGuard:** Yayıncı klasörü veya kurulu başka bir programın klasörü artık kalıntı sayılmaz; sistem kökleri ve kullanıcı dizinleri tavizsiz koruma altındadır.
- **Kanıt Tabanlı Temizlik:** Yalnızca kesin kalıntılar (`Certain`) otomatik temizlenir. Her dosya silme işlemi Geri Dönüşüm Kutusu'na gider (`FOF_ALLOWUNDO`).
- **Kayıt Defteri Geri Alma Günlüğü (UndoJournal):** Silinen kayıt defteri anahtarları ve değerleri silinmeden önce otomatik `.reg` yedeğine alınır ve tek tıkla geri yüklenebilir.
- **İzlenen Kaldırma & Doğrulama (UninstallRunner):** Kaldırıcı ve alt süreçleri Job Object ve süreç ağacıyla sonuna kadar izlenir. Sonuç Uninstall kaydından doğrulanır; iptal, yeniden başlatma gereksinimi ve başarısızlıklar dürüstçe raporlanır.
- **Tek Seferlik Geri Yükleme Noktası (RestorePointService):** Kaldırma öncesi WMI ile tek seferlik sistem geri yükleme noktası oluşturulur; 24 saat kısıtlaması dürüstçe raporlanır.

### 2. Temizleyici ve Güvenli Dosya Hedefleri
- **Kapsam Tabanlı Temizlik:** Temizleyici yalnızca her kategorinin kendi klasörlerinde silme yapar (`CleanupScope`). Yolunda rastgele "temp" ya da "cache" geçen dosyalar artık hedef alınmaz.
- **24 Saat Koruması:** Temp dizinlerinde son 24 saate ait dosyalar korunur.
- **Spotify & Çevrimdışı İndirmeler:** Spotify çevrimdışı indirilen şarkılar varsayılan seçimden çıkarıldı.

### 3. Dürüstlük: Gerçek Metrikler ve Ölçümler
- **Sensör Doğruluğu:** CPU ve GPU sıcaklığı uydurulmaz; donanım sensörü okunamıyorsa "—" gösterilir.
- **Dürüst RAM & Performans:** RAM boşaltma fonksiyonları sabit değerler yerine gerçekte boşaltılan bellek farkını söyler.
- **Oyun Modu Güç Planı:** Oyun Modu kapatıldığında sistemin önceki güç planı eksiksiz geri yüklenir.

### 4. Güvenlik İnce Ayarları ve Doğrulama
- **Yaz-Oku-Karşılaştır Doğrulaması:** İnce ayarlar yazıldıktan sonra geri okunarak doğrulanır; yazılamayan ayarlar asla "uygulandı" görünmez, gerçek hata nedeni bildirilir.
- **Güvenlik Etki Rozetleri:** Güvenliği azaltabilecek ayarlar (SmartScreen, Windows Update vb.) kırmızı "Güvenliği Azaltır" rozeti ve ayrı onay diyaloğu alır.

### 5. Güvenli Mağaza ve Yönetici Kısayolları
- **Resmî Paketler & İmza Denetimi:** Üçüncü taraf VC++ paketleri yerine resmî winget paketleri kullanılır. DirectX kurulumunun Microsoft dijital imzası doğrulanır.
- **UAC Görev Kısayolları Güvenliği:** Yönetici kısayolu oluşturma özelliği yalnızca standart kullanıcıların değiştiremeyeceği güvenli konumlardaki programlar için `.lnk` olarak oluşturulur.

### 6. Analizör Geçmişi ve Değişiklik Panelleri
- **Tam Analiz Geçmişi:** Yapılan tüm tehdit analizleri, hash, imza durumu ve risk puanları kalıcı olarak indekslenir ve listelenir.
- **Dosya Değişiklikleri Paneli:** Taranan dosyaların önceki analizlerle karşılaştırmalı değişimleri incelenebilir.

### 7. Tek Sürüm Kaynağı (Directory.Build.props) ve CI Kalitesi
- **H-15 Çözümü:** Sürüm numarası tek bir merkezden (`Directory.Build.props`) yönetilir.
- **456 Test & Sıfır Hata:** 249 Core testi ve 207 sistem testi %100 başarılı; Fluent 2 token ve sembol doğrulamaları tam onaylı.
