# Bakım v4.18.2 - Sürüm Notları

## Düzeltme: "Bakım ile analiz et" Analizör sayfasını açmıyordu

- **Sorun:** Gezgin'de sağ tık → "Bakım ile analiz et" deyince Bakım öne geliyor ama Kontrol Paneli'nde kalıyordu. Dosya arka planda analiz ediliyor, sonuç görünmüyordu.
- **Sebep:** Sayfa geçişi, ekrandaki pencereye değil, her istekte yeniden oluşturulan görünmez bir kopyaya gidiyordu. Bu kopya ayrıca her sağ tıkta gereksiz bir güncelleme denetimi başlatıyordu.
- **Çözüm:** Sayfa geçişi artık uygulamanın gezinme servisi üzerinden, ekrandaki pencereye yapılıyor.
- **Sonuç:** Sağ tık analizinde Analizör açılıyor ve dosyanın analizi sayfanın içinde gösteriliyor. Bakım kapalıyken de, açıkken de geçerli.
- **Aynı hata başka yerde:** "Program kaldırıldı · Kalıntıları tara" bildirimi de Kaldırıcı'ya geçmiyordu; o da düzeldi.

**Testler:** Tüm birim testleri yeşil; 4 denetim betiği tam geçti; XAML duman testi tamamen yeşil.
