# Bakım v4.18.1 - Sürüm Notları

## Sağ Tık Menüsü İkonları ve Belge Analizinde Yanlış Alarm

### Sağ tık menüsü ikonları
- **Sorun:** "Bakım ile analiz et" ve "Bakım ile Kaldır" menü öğeleri Gezgin'de ikonsuz görünüyordu. Menü ikonu `BakimShell.exe`'den okunuyordu, ama bu dosyada ikon kaynağı yoktu.
- **Çözüm:** Menü ikonu artık `Bakim.exe`'den okunuyor; `BakimShell.exe`'ye de uygulama ikonu eklendi.
- **Mevcut kurulumlar:** Açılıştaki menü denetimi ikonu da doğruluyor; eski ikonsuz kayıtlar Bakım açılınca kendiliğinden onarılıyor.
- **Çalışma kontrolü:** İki menü öğesi de uçtan uca denendi. "Bakım ile analiz et" dosyayı Analizör'de açıyor, "Bakım ile Kaldır" kaldırma sihirbazını açıyor. Bakım yönetici olarak çalışırken de istekler iletiliyor.

### Belge ve metin dosyası analizi
- **Sorun:** Sağ tıkla analiz edilen düz bir metin dosyası 60 (Şüpheli) alıyordu, çünkü "imzasız", "Temp klasöründe" ve "VirusTotal'de kaydı yok" kuralları program olmayan dosyalara da uygulanıyordu.
- **Çözüm:** Bu kurallar artık yalnızca kod benzeri dosyalara uygulanıyor: programlar, kütüphaneler, betikler, kurulum paketleri ve kısayollar. Belge, görsel ve metin dosyaları "Çalıştırılabilir dosya değil" olarak açıklanıyor.
- **Güvenlik:** İçerik uzantıdan önce gelir. Uzantısı `.txt`'ye çevrilmiş bir program (MZ başlığı) yine program sayılıyor ve bütün kuralları alıyor.

**Testler:** Tüm birim testleri yeşil; 4 denetim betiği tam geçti.
