# Bakım v4.15.0 - Sürüm Notları

## Analizör v3 · Faz 7: Girdi Başına Zaman Çizelgesi ve Kararlı Kimlik

### Zaman çizelgesi
- **Nereden açılır:** Analizör'de bir satırın "Diğer eylemler" menüsünde yeni **Zaman çizelgesi** seçeneği var.
- **Neler gösteriliyor:** Girdinin kayıtlı taramalardaki geçmişi:
  - İlk görüldüğü ya da eklendiği tarama.
  - Değişen alanlar. Yol değiştiyse eski → yeni yol da yazıyor.
  - Devre dışı bırakılma ve yeniden etkinleştirilme.
  - Kaldırılma ve geri dönüş.
- **Ek bilgiler:** Girdiyi hangi kurulumun eklediği (Kurulum Nöbetçisi raporlarından) ve bu dosya için verdiğiniz kararlar (Güvenilir, Karantina…) da listeleniyor.

### Değişiklikler sekmesi düzeltmesi
- **Sorun:** Bir hizmeti devre dışı bırakınca Değişiklikler sekmesi bunu "kaldırıldı" ve "yeni eklendi" diye iki ayrı girdi olarak gösteriyordu. Sebebi, hizmetin kimliğinde başlangıç türünün ("Otomatik") de yer almasıydı.
- **Çözüm:** Girdiler artık değişmeyen bir kimlikle eşleşiyor:
  - Hizmetlerde hizmet adı.
  - Görevlerde görev yolu.
  - Run girdilerinde kayıt anahtarı + değer adı.
- **Sonuç:** Kapatılan hizmet tek bir "Değişen" girdi olarak görünüyor. Görünen adı değişen hizmet ya da görev de artık yeni girdi sayılmıyor.
- **Geçiş:** Kimlik biçimi değiştiği için güncellemeden sonraki ilk tarama eskilerle karşılaştırılmıyor; karşılaştırma ikinci taramadan itibaren başlıyor.

### Diğer
- **Uzun mesajlar:** Fluent bilgi pencereleri artık uzun metinde kaydırılabiliyor, pencere ekrandan taşmıyor.

**Testler:** Tüm birim testleri yeşil (zaman çizelgesi hem saf mantıkta hem gerçek geçmiş deposuyla doğrulanıyor); 4 denetim betiği tam geçti; XAML duman testinde kaynak hatası yok.
