• **Ağ Röntgeni Kesintisiz İnceleme (Sticky Inspection Lock)**:
  - Canlı izleme sırasında arka planda liste otomatik yenilendiğinde seçili bağlantının ve sağdaki Ayrıntılı Röntgen panelinin anında sıfırlanıp kapanma hatası kökten çözüldü.
  - Seçilen soket kapansa dahi inceleme paneli kullanıcının yüzüne kapanmaz; son durum bilgileriyle korunur.

• **Akıllı Donanım Filtresi (Sanal & Filtre Sürücü Temizliği)**:
  - NDIS paket filtreleri (QoS Packet Scheduler, WFP 802.3 MAC Filter vb.) ve IP adresi atanmamış sanal alt arayüzler listeden gizlendi.
  - Ekranda yalnızca gerçekten aktif, internete ve yerel ağa bağlı fiziksel Ethernet ve Wi-Fi kartları gösterilir.
  - Adaptörler sekmesine doğrudan tek tıkla **DNS Sıfırla (Flush DNS)** ve **IP Yenile (ipconfig /renew)** onarım butonları eklendi.

• **Windows Tweaker Kategori Çökme Düzeltmesi & DI Sertleştirmesi**:
  - `TweakerCategoriesViewModel` eksik bağımlılık enjeksiyonu kaydı tamamlandı; alt kategorilere tıklanıldığında veya komut paletinden geçildiğinde oluşan arayüz istisnası giderildi.
  - Reflection tabanlı dinamik DI denetimi sisteme kazandırıldı; artık hiçbir ViewModel testlerden geçmeden üretim sürümüne giremez.
