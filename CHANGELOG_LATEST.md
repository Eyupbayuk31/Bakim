# Bakım v4.8.0 - Sürüm Notları

## Analizör v3 · Faz 0: Doğru Yollar, Güvenli Eylemler, Dürüst Puanlama

Analizör'ün tarama ve puanlama katmanı baştan ele alındı. Her değişiklik bir ölçümle başladı: önceki sürümde bu geliştirme makinesinde 607 kalıcılık girdisinin 505'i yanlışlıkla "dosya bulunamadı" görünüyordu, Windows'un kendi `explorer.exe`'si "KRİTİK, kalıcı olarak silin" önerisi alıyordu ve açılış sürücüleri bile tek tıkla kapatılabiliyordu. Ayrıntılı plan: `docs/ANALIZOR_V3_ULTRA_MEGA_PLAN.md`.

| Ölçüm (aynı makine) | v4.7.1 | v4.8.0 |
|---|---|---|
| Sahte "dosya bulunamadı" | 505 | 2 gerçek yetim kayıt (+6 çözülemeyen Mağaza COM görevi) |
| Görev durumu | Hepsi "etkin" | Görev Zamanlayıcı ile birebir |
| explorer.exe / Notepad++ / OBS puanı | 80 / 40 / 20 | 0 / 0 / 0 |
| İmzalı + VirusTotal 60/72 | 25 · "GÜVENLİ" | ≥ 75 · "YÜKSEK RİSK" |
| Anahtarı olan sürücü | 136 | 0 |
| Yeniden tarama | 5,0 sn | 0,8 sn |

---

### 1. Güvenlik
- **Eylem politikası:** Açma/kapama anahtarı ve sil düğmesi yalnızca güvenli satırlarda görünür. Sürücüler, açılış/sistem hizmetleri, kritik Windows hizmetleri ve Windows'un kendi görevleri değiştirilemez; kilit simgesi nedenini söyler.
- **Özgün başlangıç türü korunur:** Kapatılan hizmetin özgün türü (Otomatik / Gecikmeli / El ile) saklanır ve açılırken geri yüklenir. Eskiden hep "Otomatik" yazılıyordu. Bakım'ın kapattığı hizmetler listede kalır ve yeniden açılabilir.
- **Güvenilir gizleme:** "Doğrulanmış Microsoft girdilerini gizle" yalnızca Windows'un doğruladığı Microsoft imzasına bakar (katalog imzası dahil). Eskiden `C:\Windows` altındaki her dosya ve sertifika adında "Microsoft" geçen geçersiz imzalar da gizleniyordu.

### 2. Doğru tarama
- **Komut satırı çözümleyici:** Tırnaksız boşluklu yollar, sondaki virgül (`userinit.exe,`), göreli adlar (`explorer.exe`), `\SystemRoot\`, `\??\`, `System32\drivers` artık doğru çözülüyor.
- **Asıl hedef gösteriliyor:** `rundll32`, `regsvr32`, `cmd /c`, `powershell -File/-enc` (çözülmüş), `wscript/cscript`, `mshta` ve `conhost` sarmalayıcılarında incelenen dosya sarmalayıcının kendisi değil, onun çalıştırdığı asıl hedef.
- **Görevler Görev Zamanlayıcı'dan:** Türkçe karakterler bozulmuyor, etkin/devre dışı durumu doğru, gizli görevler işaretli, çoklu eylem ve COM işleyicili görevler destekleniyor.
- **Hizmet ve sürücü ayrımı:** svchost ile çalışan 56 hizmetin asıl `ServiceDll`'i inceleniyor. Sürücüler ayrı kategoride; `@%systemroot%\system32\spoolsv.exe,-1` gibi kaynak dizeleri yerine okunur hizmet adları gösteriliyor.
- **Kapsam genişletildi:** HKLM Politika Run, WOW6432Node RunOnce, IFEO 32 bit görünümü, SilentProcessExit izleyicisi, Userinit'e eklenen ikinci programlar, WMI ActiveScript tüketicileri. Gezgin eklentilerindeki tekrarlar kaldırıldı.
- **Hız:** Tarama tamamen arka planda; imza ve hash önbelleği sayesinde yeniden tarama 5 sn yerine 0,8 sn sürüyor. Sayfa açılışında donma yok.

### 3. Dürüst puanlama
- **Sistem dosyası taklidi:** Kural, her kritik Windows dosyasının yasal konumunu biliyor (`explorer.exe` → `%windir%`).
- **VirusTotal uzlaşısı baskın:** Çok sayıda motor zararlı dediğinde dosya imzalı olsa bile yüksek risk alıyor; "şüpheli" işaretleri de sayılıyor.
- **API birleşimleri:** `GetKeyState` ve `GetClipboardData` artık "keylogger" sayılmıyor. Enjeksiyon yalnızca tam zincir (ayır + yaz + çalıştır) varsa ve dosya güvenilir imzalı değilse kritik.
- **Tek puan:** Derin analizin puanı listedeki satıra yazılıyor. Eksik ASLR/DEP imzalı dosyada yalnızca bilgi.
- **Öneri dili:** "Derhal kalıcı olarak silin" gibi geri alınamaz öneriler kaldırıldı. Hiçbir yerde yapay zekâ iddiası yok.

### 4. VirusTotal
- **Hız sınırı:** Ücretsiz anahtarın dakikada 4 istek sınırına uyuluyor. Kota aşımında `Retry-After` kadar beklenip yeniden deneniyor, toplu sorguda kalan süre gösteriliyor. Eskiden 5. dosyadan sonra hepsi "VT HTTP 429" oluyordu.
- **Doğru bağlantı:** "VirusTotal'da aç" artık kayıt yoksa yükleme sayfasını açıyor.

### 5. Diğer
- **Güvenli CSV:** Dışa aktarmada formül enjeksiyonu önleniyor; ham komut, sarmalayıcı, risk ve VirusTotal sütunları eklendi.
- **İncelenen dosyalar ayrıldı:** Kurulum Nöbetçisi'nden gelen dosyalar "Başlangıç" süzgecini kirletmiyor, ayrı bir kategoride.
- **Değişiklikler sekmesi:** Tarama motoru güncellendiği için eski ve yeni tarama karşılaştırılmıyor; sahte "değişen" girdi yerine açıklama gösteriliyor.
- **Testler:** 891 birim test yeşil (50 yeni Analizör testi); 4 denetim betiği tam geçti.
