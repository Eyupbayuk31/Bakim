# Bakım v4.7.1 - Sürüm Notları

## Kurulum Nöbetçisi: winget ve Sessiz MSI Kurulumları Artık Yakalanıyor

v4.7.0'da winget ile kurulan Notepad++ Nöbetçi'ye hiç görünmedi. İnceleme iki neden ortaya çıkardı: winget MSI paketlerini `msiexec` başlatmadan, Windows Installer API'siyle **kendi süreci içinde** kuruyor ve kurulum yaklaşık **1,4 saniye** sürüyor. Süreç taraması böyle bir kurulumu hiçbir zaman göremezdi. Bu sürüm iki katmanlı çözüm getiriyor.

---

### 1. MSI Güvenlik Ağı (Windows Installer günlüğü)
- **Her MSI kurulumu yakalanır:** Nöbetçi, Windows Installer'ın "ürün yüklendi" kaydını (olay 1033) üç saniyede bir okur. Canlı izlenemeyen her kurulum için sonradan rapor üretilir; rapor listede "MSI günlüğünden sonradan yakalandı" etiketiyle görünür.
- **Kesin dosya listesi:** Dosyalar tahminle değil, Windows Installer'ın bu ürün için kaydettiği bileşenlerden okunur. Notepad++ örneğinde 215 dosya ve sağ tık menüsü dahil 3 kayıt anahtarı birebir bulundu.
- **Kayıt defteri ve sistem farkı:** Kurulumdan önce alınmış taban varsa başlangıç girdileri, hizmetler, sistem ayarları ve risk kararı canlı oturumdaki gibi hesaplanır. Taban yoksa rapor "eksik olabilir" olarak işaretlenir.
- **Güncelleme ayrımı:** Ürün kurulumdan önce zaten kayıtlıysa rapor "güncellendi" olarak yazılır ve dosyalar "değişen" sayılır.
- **Arka plan filtresi:** SYSTEM hesabıyla yapılan MSI kurulumları (hizmetler ve güncelleyiciler), "Arka plan kurulumlarını da izle" açık değilse atlanır.
- **Çift rapor yok:** Canlı izlenen bir oturumun içinde kalan MSI kayıtları ikinci kez raporlanmaz.

---

### 2. Anında Algılama
- Yönetici modunda her yeni süreç olayı (WMI) Nöbetçi turunu 1,5 saniyeyi beklemeden başlatır. `msiexec` ile başlayan kısa kurulumlar da artık kaçmıyor.

---

### 3. Kalite
- Core yol ayrıştırması Linux'taki test koşusunda da Windows yollarını doğru okuyor.
- **841 birim test yeşil** (508 Core + 333 Windows); 4 denetim betiği tam geçti.
