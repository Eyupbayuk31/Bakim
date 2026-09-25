# Bakım v3.22.0 - Sürüm Notları

## Etkinlik Merkezi, Yeni Depolama & Disk Haritası, Kurulum Nöbetçisi v2 & Gelişmiş Sistem Denetimi

### 1. Etkinlik Merkezi (Activity Center)
- **Tüm Değişikliklerin Tek Kaydı:** Sistemde gerçekleştirilen tüm bakım, temizlik, ince ayar, süreç askıya alma ve kaldırma eylemleri tek bir zaman çizelgesinde kayıt altına alınır.
- **Tek Tıkla Geri Alma (Undo Support):** Yapılan değişiklikler ve uygulanan ayarlar doğrudan Etkinlik Merkezi üzerinden güvenle geri alınabilir.
- **Arama ve Filtreleme:** Modül, önem düzeyi ve tarih aralığına göre anlık filtreleme ve JSON/metin dışa aktarma yeteneği.

### 2. Yeni Depolama Modülü & Disk Haritası (Treemap)
- **Etkileşimli Disk Haritası (Treemap):** Sürücülerdeki alan kullanımını görselleştiren, klasör ve dosya blokları arasında derinlemesine gezinme imkanı sunan yüksek performanslı görsel harita.
- **Büyük Dosya ve Alan Avcısı:** Disk alanını tüketen devasa dosyaların hızlı tespiti ve güvenli temizliği.
- **Akıllı Yinelenen Dosyalar (Hardlink Farkındalığı):** Sabit bağlantı (NTFS hard link) içeren dosyaları tekilleştirerek diski yıpratmadan ve çift sayım yapmadan gerçek yinelenenleri tespit eder.
- **Boş Klasör Temizleyici:** Güvenli kök kontrolleriyle sistem bütünlüğünü bozmadan gereksiz boş dizinleri ayıklar.

### 3. Kurulum Nöbetçisi v2 (Sensörler & Risk Motoru)
- **Derin Sensörler ve Sistem Durumu:** Dosya sistemi (64 KB FSW tamponu), kayıt defteri hotspot'ları, Windows servisleri, zamanlanmış görevler ve güvenlik duvarı kuralları gerçek zamanlı izlenir.
- **Risk Motoru ve Triage:** Kurulum paketinin oluşturduğu değişiklikler MITRE ATT&CK teknikleriyle puanlanır; şüpheli kalıcılık yöntemleri anında işaretlenir.
- **Tek Tıkla Zararlı Müdahalesi:** Şüpheli kurulumların eklediği Run girdileri, servisler veya görevler doğrudan bulgu kartından tek tıkla devre dışı bırakılabilir.
- **Kurulum Kaynağı Doğrulaması:** İndirilen sitenin MOTW kaynağı, dijital imza durumu ve PE başlık kontrolleri rapora işlenir.

### 4. Kaldırıcı v2 & Kanıt Tabanlı Kalıntı Analizi
- **Ön Ayak İzi (Footprint) Toplama:** Kaldırma işlemi başlamadan önce programa ait kayıt defteri, servis, görev, kısayol ve dosya izleri eksiksiz haritalanır.
- **Kanıt Tabanlı Temizlik:** Yalnızca kesin (`Certain`) ve yüksek güvenli kalıntılar önerilir; başka uygulamaların veya ortak yayıncıların dosyaları asla silinmez.
- **Tek Örnek & IPC (Single Instance):** Sağ tıkla kaldırma çağrıları çalışan Bakım örneğine adlandırılmış kanal (Named Pipe) üzerinden hafif ve UAC istemi olmaksızın iletilir.
- **Kaldırma Geçmişi:** Yapılan tüm kaldırma işlemleri, temizlenen alanlar ve kayıt defteri geri alma noktaları saklanır.

### 5. TweakEngine & Veri Tabanlı İnce Ayar Kataloğu
- **JSON Tabanlı Ayar Motoru:** İnce ayarlar hardcoded mantıktan çıkarılarak şema versiyonlu JSON kataloglarına (`Assets/tweaks/*.json`) taşındı.
- **Yaz-Oku Doğrulaması:** Her ayar uygulandıktan sonra sistemden doğrulanır; uygulanamayan ayarlar dürüstçe raporlanır.
- **Güvenlik Rozetleri:** Güvenlik etkisi olan ayarlar belirgin rozetlerle işaretlenir.

### 6. Performans, Başlangıç ve Hizmet Yönetimi
- **Windows Açılış Ölçümleri:** Tahmini süreler yerine Windows Olay Günlüğü'nden (Event 100) okunan gerçek önyükleme ve masaüstü hazır olma süreleri sunulur.
- **Güvenli Hizmet Profilleri:** Windows servisleri için güvenli, hafif ve oyuncu profilleri; kritik sistem servislerini kapatmaya karşı tekil koruma politikası.
- **Askıya Alınan Süreç Defteri & Oyun Modu:** Oyun modu profiliyle arka plan kaynakları dondurulur, çıkışta önceki güç planı ve süreç durumları eksiksiz geri yüklenir.
- **Olaylar & Güvenilirlik Zaman Çizelgesi:** Windows çökme ve güvenilirlik olayları (Reliability Index) entegre zaman çizelgesinde incelenebilir.

### 7. Mağaza & Winget Güncellemeleri
- **Yazılım Güncellemeleri Sekmesi:** Sistemde kurulu tüm yazılımların winget üzerinden güncel sürümleri listelenir ve tek tıkla toplu güncellenebilir.
- **Resmî Paket Sağlayıcıları:** Üçüncü taraf riskli betikler yerine resmî winget ve doğrulanmış Microsoft kaynakları kullanılır.

### 8. Bilgi Mimarisi v2 & Fluent 2 Tasarım Sistemi
- **Gruplandırılmış Kenar Çubuğu:** Genel Bakış, Temizlik & Depolama, Performans, Güvenlik, Yazılım ve Sistem kategorileriyle sade ve modern navigasyon.
- **Tasarım Token'ları & Tipografi Ölçeği:** Semantik renk fırçaları, standart yazı boyutları ve yumuşak kavislerle kusursuz Windows 11 Fluent 2 deneyimi.
