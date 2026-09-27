# Bakım v4.19.0 - Sürüm Notları

## Oyun Modu v2: Ölçülebilir Etki

Önceki Oyun Modu çoğunlukla göstermelikti. Güç planı değişimi masaüstü işlemcilerde çok küçük fark yaratıyordu. "Arka plan belleğini boşalt" ise oyunun kendisi dahil bütün süreçlerin belleğini kırpıyordu; bellek aslında boşalmıyor, uygulamalar onu geri çekerken takılma oluyordu. Yeni sürüm yalnızca etkisi olan şeyleri yapıyor ve etkisini ölçüyor.

### Yeni
- **Windows Oyun Modu:** Kapalıysa oturum boyunca açılıyor. Windows oyuna öncelik veriyor ve oyun sırasında güncelleme kurulumunu erteliyor. Oturum bitince önceki ayar geri geliyor.
- **Oyunu öne al:** Oyun "Normalin üstü", Discord ve tarayıcı gibi arka plan uygulamaları "Normalin altı" önceliğe alınıyor.
  - Ön plandaki pencereye, Windows'un kritik süreçlerine ve sizin elle ayarladığınız önceliklere dokunulmuyor.
  - Oturum bitince hepsi eski hâline dönüyor.
- **Oyun sonradan açılsa da çalışır:** Oyun Modu'nu oyundan önce açtıysanız, oyun başlayınca bulunup öne alınıyor.
- **Ölçüm:** Oturum boyunca oyun dışındaki süreçlerin işlemci kullanımı ölçülüyor. Durum kartında ve Etkinlik Merkezi'nde "Arka plan işlemci: %12 → %4" gibi gösteriliyor. Eski yanıltıcı "GB boşaldı" rakamı kaldırıldı.
- **Riot Games desteği:** VALORANT, League of Legends ve Teamfight Tactics kütüphanede bulunuyor. Otomatik başlatma artık kütüphanedeki (Steam, Epic, Riot) oyunları da izleyebiliyor; exe adı yazmak gerekmiyor.

### Değişen
- **Bellek kırpma:** Yalnızca bellek %85'in üstündeyken ve yalnızca arka plan uygulamalarında yapılıyor. Oyuna ve ön plandaki pencereye dokunulmuyor.

### Düzeltmeler
- **Güç planı takılı kalıyordu:** Bakım, Oyun Modu açıkken kapanınca (güncelleme dahil) oturum açık kalıyor ve güç planı Yüksek Performans'ta takılıyordu. Artık kapanışta oturum düzgün kapatılıyor; güç planı, öncelikler ve Windows Oyun Modu geri yükleniyor.
- **Testler bilgisayarı değiştiriyordu:** Oyun Modu testleri gerçek güç planını değiştirip Bakım'ın durum dosyasını siliyordu. Artık sahte sistemle çalışıyorlar.
- **Günlük dosyası:** Aynı anda çalışan iki Bakım süreci günlük satırlarını birbirinin üstüne yazıyordu; düzeltildi.

**Testler:** Tüm birim testleri yeşil; Oyun Modu kuralları sahte sistemle, Windows işlemleri gerçek sistemde güvenli şekilde doğrulanıyor. 4 denetim betiği tam geçti; XAML duman testi tamamen yeşil.
