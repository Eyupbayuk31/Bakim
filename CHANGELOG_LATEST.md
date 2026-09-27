# Bakım v4.12.0 - Sürüm Notları

## Analizör v3 · Faz 4: Canlı Kalıcılık İzleme ve Kurulum Bağlantısı

### Canlı izleme
Bakım artık bir programın kendini başlangıca eklediği anı yakalıyor. Analizör sayfasında "Canlı izleme" anahtarı var; varsayılan olarak açık.

- **Nasıl izleniyor:**
  - Run/RunOnce (HKCU, HKLM, 32 bit) ve politika anahtarları ile hizmet eklemeleri: Windows'un kayıt defteri değişim bildirimiyle, anında.
  - Başlangıç klasörleri: dosya izleyicisiyle.
  - Zamanlanmış görevler ve WMI abonelikleri: 5 dakikada bir hafif yoklamayla.
- **Nasıl değerlendiriliyor:** Değişiklikler 3 saniye birleştiriliyor ve yalnızca değişen konum yeniden taranıyor. Yeni girdi (ya da hedefi değiştirilmiş mevcut bir Run değeri) diğer satırlarla aynı risk ölçüsüyle değerlendiriliyor.
- **Nereye yazılıyor:**
  - Doğrulanmış Microsoft imzalı olmayan her ekleme Etkinlik Merkezi'ne yazılıyor.
  - Dikkat ve üstündekiler tepsi bildirimiyle haber veriliyor (Oyun Modu'nda yalnızca Tehlikeli).
  - Analizör açıksa yeni girdi listeye anında ekleniyor.
- **Açılışı yavaşlatmıyor:** İzleme tabanı, uygulama açıldıktan 45 saniye sonra arka planda alınıyor.

### "Bu girdiyi hangi kurulum ekledi?"
- **Nöbetçi raporlarıyla eşleştirme:** Analizör satırları Kurulum Nöbetçisi raporlarıyla (başlangıç girdileri, hizmetler, görevler) eşleştiriliyor. Eşleşen satırda örneğin "Notepad++ kurulumu ekledi (26 Eylül 2026)" yazıyor.
- **Canlı izleme sırasında:** Bir kurulum oturumu sürerken eklenen girdi "… kurulumu sırasında eklendi" olarak işaretleniyor.

**Testler:** 939 birim test yeşil; canlı izleme gerçek bir HKCU Run değeriyle uçtan uca doğrulandı (değer test sonunda siliniyor). 4 denetim betiği tam geçti.
