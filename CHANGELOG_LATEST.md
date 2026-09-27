# Bakım v4.11.0 - Sürüm Notları

## Analizör v3 · Faz 3: Antivirüs İkinci Görüşü, Betik Analizi ve Karantina

### Windows Defender ikinci görüşü
- **Otomatik tarama:** Analizör'de tek dosya analizi (satırdan "Analiz et", "Dosya İncele", Kurulum Nöbetçisi'nden gelen dosya) Windows Defender'ın kendi motoruyla da taranıyor. Diğer girişler için dosya analizi penceresine "Windows Defender ile tara" menü öğesi eklendi.
- **Tehdit bulunursa:** Defender'ın verdiği tehdit adıyla birlikte puan en az 90 oluyor ve karar yeniden yazılıyor.
- **Defender kapalıysa:** "İkinci görüş alınamadı" deniyor ve puan değişmiyor. Defender kapalıyken komut satırı aracı hiçbir şey taramadan "başarılı" döner; Bakım bunu "temiz" saymaz.

### Betik analizi
- **Desteklenen türler:** PowerShell, VBScript, JScript, toplu iş (.bat/.cmd) ve HTA dosyaları içerikleriyle inceleniyor.
- **Güçlü sinyaller (birleşimler):**
  - indir + çalıştır,
  - Defender istisnası ekleme veya korumayı kapatma,
  - gölge kopya/günlük silme,
  - kimlik bilgisi çalma araçları (mimikatz, LSASS dökümü).
- **Zayıf sinyaller:** kodlanmış (base64) yük, karartılmış kod ve kalıcılık oluşturma.
- **Karartma temizliği:** PowerShell ters tırnağı, cmd şapkası ve bölünmüş dizeler temizlenerek gizleme atlatılıyor.
- **Tek başına kanıt değil:** Tek bir anahtar kelime puan üretmiyor. Normal kurulum ve güncelleme betikleri temiz kalıyor.
- **AMSI:** Betik içeriği sistemdeki antivirüsün AMSI arayüzüyle de taranıyor; tespit edilirse puan en az 90 oluyor.
- **Gömülü komutlar:** Kalıcılık girdilerindeki gömülü komutlar (`cmd /c`, çözülmüş `powershell -enc`, WMI ActiveScript metni) ve hedefi betik olan girdiler de aynı kurallardan geçiyor.

### Karantina
- **Yeni düğme:** Dosya analizi penceresinde "Karantinaya al".
- **Nasıl çalışıyor:**
  - Dosya, çalıştırma izni kaldırılmış bir karantina klasörüne taşınıyor.
  - Bağlı başlangıç girdisi (izin veriliyorsa) kapatılıyor.
  - İşlem Etkinlik Merkezi'ne kaydediliyor; "Geri al" dosyayı özgün yerine döndürüyor.
- **Korumalar:**
  - Korumalı konumlardaki (Windows klasörü …) dosyalar reddediliyor.
  - Özgün konumda yeni bir dosya varsa geri yükleme üzerine yazmıyor.
- **Geçmişe yansıyor:** Karar Analizör Geçmişi'ne "Karantina" olarak yazılıyor.
- **Öneri metni:** Yüksek risk önerisi artık silme yerine geri alınabilir karantinayı öneriyor.

### Puanlama
- Doğrulanmış Microsoft imzalı Active Setup/Winlogon girdileri "nadir konum" riski almıyor (ör. Edge'in Active Setup girdisi artık "Dikkat" değil); başka doğrulanmış imzalarda bu risk yarıya iniyor.

**Testler:** 931 birim test yeşil; 4 denetim betiği tam geçti.
