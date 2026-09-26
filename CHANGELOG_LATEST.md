# Bakım v4.7.0 - Sürüm Notları

## Kurulum Nöbetçisi v3 · Faz A: Doğru Raporlar, Dürüst Koruma Rozeti & Arka Plan Gürültüsünün Susturulması

Bakım v4.7.0 sürümü, Kurulum Nöbetçisi'nin ürettiği raporları gerçek kurulum davranışına uygun hale getiriyor. Riskli ama dosya bırakmayan kurulumlar artık bildiriliyor. Oyun başlatıcılarının ve güncelleyicilerin arka plan kurulumları kullanıcı kurulumu sanılmıyor. Kurulumun geçici dosyaları "silinen dosya" olarak raporlanmıyor. Ayrıntılı yol haritası: `docs/SENTINEL_V3_MEGA_PLAN.md`.

---

### 1. Bildirim ve Karar
- **Dosyasız ama riskli kurulumlar bildiriliyor:** Yalnızca başlangıç girdisi, hizmet, kök sertifika ya da Defender istisnası ekleyen ya da "Dikkat" ve üstü karar alan kurulumlar artık bildirim gösteriyor. Eskiden yeni dosya yoksa bildirim tamamen susuyordu.
- **Dürüst koruma rozeti:** Hiçbir zaman doğru çalışmayan NTFS USN bağlantısı kaldırıldı. Yönetici modunda rozet artık "Tam Koruma" yerine **"Gelişmiş Mod"** gösteriyor ve rapora aynı anda çalışan programların yazdıklarının da girebileceğini açıkça söylüyor. "Tam Koruma" adı süreç atıflı yakalamaya (Faz C) ayrıldı.

---

### 2. Doğru Dosya Farkı
- **Geçici dosyalar ayrıldı:** Kurulumun oluşturup yine sildiği dosyalar "silinen" listesinde değil, ayrı bir sayı olarak tutuluyor.
- **Yeniden adlandırma destekleniyor:** Geçici adla yazılıp yeniden adlandırılan dosyalar ve klasörler (içerikleriyle birlikte) son adlarıyla rapora giriyor.
- **Geri alma güvenliği:** Silinip yeniden yazılan ya da var olan bir dosyanın üstüne taşınan dosyalar "değişen" sayılıyor ve geri almada asla silinmiyor.
- **Son yazmalar kaybolmuyor:** Kurulum bitince beklenen 1,5 saniyede gelen dosya olayları artık kaydediliyor.
- **Doğru boyut:** Toplam boyut, dosyalar diske yazıldıktan sonra okunuyor (eskiden çoğu dosya 0 bayt sayılıyordu).
- **Daha hafif izleme:** Dosya olayı iş parçacığında disk erişimi kaldırıldı; büyük kurulumlarda arabellek taşması riski azaldı.

---

### 3. Kayıt Defteri
- **32 bit girdiler:** 32 bit başlangıç ve Uninstall kayıtları artık yalnızca doğru `WOW6432Node` yoluyla ve bir kez raporlanıyor; 64 bit değerleri ezmiyor.
- **Kesin eşleşme:** "RuneLite", "RunAsDate" gibi Uninstall kayıtları artık başlangıç girdisi sayılmıyor.

---

### 4. Oturum Yönetimi
- **Arka plan kurulumları:** Riot, Steam, Epic, Battle.net gibi başlatıcıların, güncelleyicilerin ve Windows hizmetlerinin başlattığı kurulumlar için varsayılan olarak oturum açılmıyor. Nöbetçi sayfasındaki **"Arka plan kurulumlarını da izle"** seçeneğiyle açılabilir. Terminalden elle başlatılan kurulumlar etkilenmiyor.
- **Uygulamayı başlatan kurulumlar:** Kurulum bitince açılan uygulama izlemeden ayrılıyor; oturum uygulama kapanana kadar açık kalmıyor.
- **Doğru uygulama adı:** "7-Zip SFX", "Setup/Uninstall", "Windows Installer" gibi kurulum aracı adları atlanıyor. Kurulumun oluşturduğu Uninstall kaydı varsa ad oradan alınıyor.
- **Kararlılık:** İzlenen süreç kümesi eşzamanlı yazımlara karşı güvenli hale getirildi; kapanışta çalışan döngü beklenmeden kilit atılmıyor.

---

### 5. Temizlik & Testler
- Kullanılmayan kurulum öncesi ön görüntüsü (oturum başını geciktiriyordu) ve arayüzden çağrılmayan geri alma yolu kaldırıldı. `KernelTraceSensor` gerçekte yaptığı işe uygun olarak `WmiProcessSensor` adını aldı.
- Saf mantık Core katmanına taşındı: `SetupDeltaBuilder`, `SetupAppName`, `SetupSessionPolicy`.
- **822 birim test yeşil** (39 yeni Nöbetçi testi); 4 denetim betiği tam geçti.
