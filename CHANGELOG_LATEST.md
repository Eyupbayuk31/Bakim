# Bakım v4.10.0 - Sürüm Notları

## Analizör v3 · Faz 2: Tek Risk Dili ve Açıklamalı Kararlar

### Tek ölçek
Analizör listesi, dosya analizi, Analizör Geçmişi ve Kurulum Nöbetçisi artık aynı eşikleri ve aynı etiketleri kullanıyor:

**Temiz 0–9 · Bilgi 10–29 · Dikkat 30–49 · Şüpheli 50–74 · Tehlikeli 75+**

Eskiden dört ayrı ölçek vardı (60/30, 61/26, 15/25/45/60, 16/40/70); aynı puan bir ekranda "Orta", diğerinde "Şüpheli" görünebiliyordu. Nöbetçi'nin bulgu ağırlıkları yeni ölçeğe göre ayarlandı:
- tek yüksek bulgu → Dikkat,
- iki yüksek ya da tek kritik bulgu → Şüpheli,
- iki kritik bulgu → Tehlikeli.

### "Neden bu puan?"
- **Risk rozeti:** Her kalıcılık satırında tek ölçekli bir rozet var; üzerine gelince kararın gerekçeleri listeleniyor (imza, konum, VirusTotal, yaş, devre dışı, yetim kayıt, toplayıcı bulguları).
- **Test edilebilir çekirdek:** Satır puanı artık saf ve test edilebilir bir çekirdekte hesaplanıyor (`Core/Analyzer/PersistenceRisk`).

### Güvenilir işaretleme gerçekten çalışıyor
- **Kalıcı karar:** Dosya analizi penceresindeki "Güvenli listeye ekle" eskiden yalnızca o pencerede bir bayrak kuruyor ama "güvenli listeye eklendi" diyordu. Artık Analizör Geçmişi'ne kalıcı "Güvenilir" kararı yazıyor.
- **Listeye yansıyor:** Analizör listesi güvenilen hash'leri tabana indiriyor; karar Geçmiş'ten geri alınabilir.
- **VirusTotal önceliği:** Çok sayıda antivirüsün zararlı dediği dosyada kullanıcı güveni puanı düşürmüyor; gerekçede bu açıkça yazıyor.

### Geliştirici araçları
- **Tek sayfa çekimi:** Ekran görüntüsü aracına `--only <Sayfa>` seçeneği eklendi.
- **Düzeltme:** Araçta `Application.Current` boş olduğunda oluşan çökme giderildi.

**Testler:** 914 birim test yeşil; 4 denetim betiği tam geçti.
