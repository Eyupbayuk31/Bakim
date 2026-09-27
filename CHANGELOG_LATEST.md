# Bakım v4.16.0 - Sürüm Notları

## Kaldırıcı: Program İkonları ve Dışarıdan Kaldırma Algılama · Uygulama Genelinde Fluent Pencereler

### Dışarıdan kaldırılan programlar için kalıntı önerisi
- **Artık Bakım dışında kaldırılanları da yakalıyor:** Program Windows Ayarlar'dan, kendi kaldırıcısıyla ya da winget ile kaldırılsa bile Bakım fark ediyor. Köşede "*Program* kaldırıldı · **Kalıntıları tara**" bildirimi çıkıyor.
- **Kalıntıları tara:** Kaldırıcı sayfasının kalıntı akışı açılıyor. Klasör, yayıncı ve kayıt bilgileri kaldırmadan önceki kayıttan geliyor, bu yüzden tarama program gittikten sonra da doğru çalışıyor.
- **Yanlış alarm yok:**
  - MSI güncellemeleri (eski sürüm kaydı silinip yenisi yazılır) ve aynı klasöre yeniden kurulum kaldırma sayılmıyor.
  - Sistem bileşenleri bildirim üretmiyor.
  - Bakım'ın kendi yaptığı kaldırmalarda bildirim çıkmıyor; sihirbaz kalıntıları zaten gösteriyor.
- **Oyun Modu'nda:** Bildirim gösterilmiyor.
- **Açma/kapama:** Kaldırıcı sayfasında yeni "Dışarıdan kaldırmaları izle" anahtarı var; varsayılan olarak açık.
- **Pil ve işlemci dostu:** Kaldırma kayıtlarının yalnızca adları 15 saniyede bir karşılaştırılıyor. Tam okuma yalnızca bir değişiklik olunca yapılıyor.

### Kaldırıcıda program ikonları
- **Sorun:** Listede her programda aynı varsayılan simge görünüyordu. Programın ikon bilgisi (`DisplayIcon`) okunuyor ama hiç kullanılmıyordu.
- **İkonun kaynağı, sırasıyla:**
  1. `DisplayIcon`: dizinli ve `.ico` değerler dahil.
  2. MSI kurulumlarında Windows Installer'ın sakladığı ürün ikonu (Notepad++, Everything gibi).
  3. Kurulum klasöründeki ana exe.
  4. Kaldırıcı exe'si.
- **Sonuç (geliştirme makinesi):** Uygulamalar sekmesindeki 63 programın 55'inde ikon var. Kalanların çoğunun zaten ikonu yok (kütüphane, eklenti, font paketi).
- **Hız:** İkonlar liste göründükten sonra arka planda yükleniyor; liste açılışı yavaşlamadı.

### Fluent pencereler (Analizör v3 Faz 5)
- **Kapsam:** Uygulama genelindeki 148 sistem penceresi (`MessageBox`) Fluent 2 pencerelere taşındı. Kaldırıcı, Ağ İzleyicisi, Gizlilik, Ayarlar, Optimizasyon, Hizmetler, Başlangıç, Depolama, Mağaza, Etkinlik Merkezi, güncelleme ve diğer bütün pencereler koyu temayı ve Mica'yı alıyor.
- **Durum simgesi:** Hata, uyarı, soru ve bilgi yalnızca renkle değil simgeyle de ayırt ediliyor.
- **Kırmızı onay düğmesi:** Silme, kaldırma, sonlandırma, sıfırlama ve geri alınamaz işlemlerde onay düğmesi kırmızı.
- **Davranış aynı:** Pencereler eskisi gibi yanıt beklemeden işlemi sürdürmüyor. Evet/Hayır/Vazgeç seçimleri birebir aynı sonucu veriyor.
- **İstisna:** Beklenmeyen hata yakalayıcısı ve yönetici izni penceresi bilinçli olarak sistem penceresinde kaldı; arayüz bozukken de açılabilmeleri gerekiyor.

**Testler:** Tüm birim testleri yeşil. Dışarıdan kaldırma algılama gerçek bir HKCU kaldırma kaydıyla uçtan uca doğrulanıyor. Fluent pencerelerin Evet/Hayır/Vazgeç sonuçları duman testinde ekran dışında doğrulanıyor. 4 denetim betiği tam geçti; XAML duman testinde kaynak hatası yok.
