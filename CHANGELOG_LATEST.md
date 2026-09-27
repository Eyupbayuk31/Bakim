# Bakım v4.13.0 - Sürüm Notları

## Analizör v3 · Faz 4 + Faz 5 (ilk bölüm): Canlı İzleme, Kurulum Bağlantısı, Fluent Pencereler, Sürükle-Bırak ve Sağ Tık Analizi

> v4.12.0 derlenmiş ama yayın testlerinde bir yarış durumu yakalandığı için yayınlanmamıştı. Bu sürüm onun içeriğini düzeltilmiş hâliyle içerir.

### Canlı kalıcılık izleme (Faz 4)
- **Nasıl izleniyor:**
  - Run/RunOnce (HKCU, HKLM, 32 bit) ve politika anahtarları ile hizmet eklemeleri: kayıt defteri değişim bildirimiyle, anında.
  - Başlangıç klasörleri: dosya izleyicisiyle.
  - Zamanlanmış görevler ve WMI abonelikleri: 5 dakikada bir yoklamayla. Yoklama diğer konumlar için de güvenlik ağıdır.
- **Nasıl değerlendiriliyor:** Yeni girdi (ya da hedefi değiştirilmiş mevcut bir Run değeri) diğer satırlarla aynı risk ölçüsüyle değerlendiriliyor.
- **Nereye yazılıyor:**
  - Doğrulanmış Microsoft imzalı olmayan her ekleme Etkinlik Merkezi'ne yazılıyor.
  - Dikkat ve üstündekiler tepsi bildirimiyle haber veriliyor (Oyun Modu'nda yalnızca Tehlikeli).
  - Analizör açıksa yeni girdi listeye anında ekleniyor.
- **Anahtar ve varsayılan:** Analizör sayfasında "Canlı izleme" anahtarı var; varsayılan olarak açık. Taban, açılıştan 45 saniye sonra arka planda alınıyor.
- **Dayanıklılık:**
  - İzlenen anahtar henüz yoksa (ör. temiz bir kullanıcı hesabında HKCU Run) var olan en yakın üst anahtar izleniyor; anahtar oluşunca izleme ona geçiyor.
  - İzleme, bildirimler kurulmadan "hazır" sayılmıyor. Eskiden açılıştaki kısa bir aralıkta yapılan değişiklik kaçabiliyordu.

### "Bu girdiyi hangi kurulum ekledi?" (Faz 4)
- **Nöbetçi raporlarıyla eşleştirme:** Analizör satırları Kurulum Nöbetçisi raporlarıyla eşleştiriliyor; satırda örneğin "Notepad++ kurulumu ekledi (26 Eylül 2026)" yazıyor.
- **Canlı izleme sırasında:** Bir kurulum oturumu sürerken eklenen girdi "… kurulumu sırasında eklendi" olarak işaretleniyor.

### Fluent pencereler (Faz 5)
- **44 sistem penceresi kaldırıldı:** Analizör, dosya analizi penceresi, Analizör Geçmişi ve Ağ İzleyicisi'nin eylem penceresindeki `MessageBox` pencereleri Fluent 2 onay/bilgi pencerelerine taşındı.
- **Daha az kesinti:** "Kopyalandı", "tamamlandı" gibi bilgiler artık pencere açmıyor; durum satırında gösteriliyor.
- **Yıkıcı eylemler kırmızı:** Kaldırma, süreç sonlandırma ve geçmişi silme onaylarında birincil düğme kırmızı.
- **Onay eklendi:** Ağ İzleyicisi eylem penceresindeki "Süreci sonlandır" eskiden onay sormadan sonlandırıyordu; artık soruyor.
- **VirusTotal yükleme onayı:** Yüklenen dosyanın VirusTotal ortaklarıyla paylaşıldığını açıkça söylüyor; "Vazgeç" artık tarayıcı açmıyor.

### Sürükle-bırak ve sağ tık (Faz 5)
- **Sürükle-bırak:** Dosyaları Analizör sayfasına sürükleyip bırakarak analiz edebilirsiniz. Tek dosya analiz penceresinde, birden çok dosya "İncelenen dosyalar" olarak listede açılır.
- **Sağ tık:** Gezgin'de yeni "Bakım ile analiz et" seçeneği var (tüm dosyalarda). Bakım açıksa istek açık örneğe iletiliyor ve Analizör'de analiz başlıyor. Menü, mevcut "Sağ tık menüsü" ayarıyla birlikte açılıp kapanıyor ve açılışta kendiliğinden ekleniyor.

**Testler:** 944 birim test yeşil (canlı izleme gerçek bir HKCU Run değeriyle uçtan uca doğrulanıyor); 4 denetim betiği tam geçti; XAML duman testinde kaynak hatası yok.
