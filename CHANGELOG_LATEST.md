# Bakım v3.18.2 - Sürüm Notları

## Windows Sistem Konsolları & Klasik Araçlar Mağaza Entegrasyonu (Tab 3) & Tweaker Sadeleştirmesi 🛠️⚡

### 1. 24 Windows Sistem Konsolu & Klasik Araçlar Taşıması
- **Tweaker Karmaşasına Son:** Kayıt defteri ve sistem ince ayarlarına odaklanan Windows Tweaker'dan sistem yönetim konsolları ayrılarak, doğrudan ait olduğu **Yazılım & Runtimes Mağazası** içerisine **3. Bağımsız Çalışma Alanı (Tab 3: Sistem Konsolları & Klasik Araçlar)** olarak entegre edildi.
- **24 Güçlü Windows Yönetim Aracı:** Aygıt Yöneticisi, Bilgisayar Yönetimi, Windows Hizmetleri (Services), Kayıt Defteri Düzenleyicisi (Regedit), Yerel Grup İlkesi (GPEdit), Olay Görüntüleyici, Görev Zamanlayıcı, DirectX Teşhis (DxDiag), Disk Yönetimi, Ağ Bağlantıları (NCPA), God Mode, Windows Terminal, Kaynak İzleyicisi ve daha fazlası tek merkezde toplandı.

### 2. Windows Fotoğraf Görüntüleyicisi 1-Tıkla Aktifleştirme Hero Banner'ı
- **Akıllı Canlı Durum Algılama:** Kayıt Defteri üzerinden Windows Fotoğraf Görüntüleyicisi'nin sistemde aktif olup olmadığını arka planda otomatik denetleyen canlı durum rozeti ("Etkin / Hazır" veya "Devre Dışı").
- **1-Tıkla Aktifleştirme & İlişkilendirme:** Windows 10/11'de gizlenen klasik hızlı Fotoğraf Görüntüleyicisi'ni tek tıkla aktifleştiren, sistem dosya uzantılarını (.jpg, .png vb.) otomatik kaydeden hero kartı ve anında durum güncellemesi.

### 3. Zengin Filtreleme, Kategori Çipleri & Anlık Arama
- **5 Kategori Filtre Çipi:** "Tümü", "Sistem", "Donanım & Disk", "Ağ & Güvenlik" ve "Hızlı Erişim" çipleri ile 24 konsol arasında amaca yönelik anında süzme.
- **Anlık Arama Kutusu:** Konsol adı, dosya uzantısı (.msc, .exe) veya açıklamasına göre milisaniyeler içinde canlı arama desteği.

### 4. Kusursuz Kart Mimarisi & Çalıştırılabilir Dosya Etiketleri
- **Solid Border & Fluent 2 Tasarım:** `#1E293B` Slate Dark kart tabanı, `#334155` solid sınır çizgileri, hover parlama efekti ve 2 sütunlu düzenli ızgara.
- **Teknik Dosya Etiketi:** Her konsol için çalıştırılan gerçek Win32 komut etiketi (örn: `devmgmt.msc`, `regedit.exe`, `ncpa.cpl`) ve resmi Fluent `WindowWrench24` ile `Play24` "Başlat" butonu.

### 5. Windows Tweaker Sol Menü Optimizasyonu
- **Odaklanmış Temiz Navigasyon:** Tweaker sol navigasyon rayı yalnızca Windows ince ayarlarına (Windows 11, Görev Çubuğu, Dosya Gezgini, Performans vb.) odaklanacak şekilde sadeleştirildi.
- **Dinamik Kategori Rozeti:** Tweaker başlığındaki kategori adedi statik değerden çıkarılarak `{Binding CategoryList.Count}` ile dinamik ve senkronize hale getirildi.

### 6. Pre-flight, Test ve Kalite Doğrulaması
- **147/147 Birim Testi:** Tüm testler %100 başarıyla geçti.
- **UI Smoke Testleri:** Tüm modül görünümleri, 4 tema, 3 mağaza sekmesi ve diyaloglar hatasız doğrulandı.
- **9.235 Sembol & Token %100 Geçerli:** `verify-symbols.py` ve `verify-tokens.py` ile sıfır geçersiz sembol kanıtlandı.
- **Sıfır Uyarı / Sıfır Hata:** .NET 10 Release derlemesi temiz tamamlandı.
