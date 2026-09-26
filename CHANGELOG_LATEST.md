# Bakım v4.9.0 - Sürüm Notları

## Analizör v3 · Faz 1: Autoruns Düzeyinde Kapsam

Analizör artık Windows'ta programların kendini kalıcı kılabildiği konumların büyük bölümünü tarıyor. Yeni konumlar salt okunurdur: incelenir ve puanlanır, ama Analizör'den değiştirilemez (yanlış bir LSA ya da Winsock değişikliği oturum açmayı veya ağı bozabilir).

### Yeni konumlar
- **Oturum açılışı:** Active Setup StubPath, komut istemi AutoRun (HKLM/HKCU), kullanıcıya özel Winlogon Shell, Winlogon AppSetup.
- **Sistem kancaları (yeni kategori):**
  - AppInit_DLLs (64/32 bit) ve AppCertDlls
  - LSA kimlik doğrulama, bildirim ve güvenlik paketleri
  - Yazdırma izleyicileri
  - Winsock katalog ve ad alanı sağlayıcıları
  - Netsh yardımcıları
  - BootExecute
  - PowerShell profil betikleri
- **Gezgin kancaları:** Simge kaplamaları, ShellServiceObjectDelayLoad, ShellExecuteHooks, tarayıcı yardımcı nesneleri (BHO), özellik sayfası, arka plan, sürücü ve sürükle-bırak işleyicileri.
- **Tarayıcı eklentileri (yeni kategori):**
  - Chrome, Edge, Brave ve Vivaldi (tüm profiller), Opera ve Firefox
  - Politika ile zorla kurulan eklentiler (`ExtensionInstallForcelist`), henüz indirilmemiş olsalar bile
  - Eklenti adları tarayıcının dil dosyalarından okunuyor
- **Office eklentileri (yeni kategori):** Word, Excel, Outlook, PowerPoint ve diğerleri için COM ve VSTO eklentileri; 32 bit COM kayıtları da çözülüyor.

### Puanlama
- **Tarayıcı eklentileri:** "İmzasız" sayılmıyor. Asıl sinyal, mağaza dışı bir adresten politika ile zorla kurulmak (yüksek risk). Tüm sitelerde ağ trafiğine erişen reklam engelleyici ve parola yöneticileri düşük riskte kalıyor; bulgular satırda açıklanıyor.
- **AppInit/AppCert DLL'leri ve varsayılan dışı BootExecute:** Konum riski taşıyor.
- **Dosyası olmayan girdiler:** Yetim kayıt artık "tehdit" değil temizlik konusu (en fazla 20 puan). İncelenecek dosyası olmayan girdiye imza puanı uygulanmıyor.

### Arayüz
- Yeni süzgeçler: **Sistem kancaları**, **Tarayıcı eklentileri**, **Office eklentileri**.
- Toplayıcı bulguları (ör. "Tüm sitelerde ağ trafiğini görebilir") satırın altında gösteriliyor.

### Dayanıklılık
- Tek bir konumun okunamaması taramanın geri kalanını düşürmüyor.
- Korumalı IFEO anahtarları tek tek atlanıyor.
- Windows'un argüman içeren Active Setup girdileri ("/UserInstall", "U") komut sanılmıyor.

**Testler:** 899 birim test yeşil; 4 denetim betiği tam geçti.
