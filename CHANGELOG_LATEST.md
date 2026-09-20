# Bakım v3.14.0 - Sürüm Notları

## Windows Tweaker Master-Detail Mimarisi & Zengin Bilgilendirici Hover Kartları

- **Tekil & Temiz Kategori Yönetimi (Master-Detail Mimarisi):**
  - Sol ana gezinme menüsündeki 13 alt butonluk akordiyon karmaşası tek bir sade ve kurumsal "Windows Tweaker" butonuna dönüştürüldü.
  - Sayfa içindeki açılır pencere (popup flyout) ve 5 adet hardcoded hap buton kaldırıldı; yerine tüm 14 kategoriyi sol tarafta dikey olarak listeleyen, canlı sayaç rozetli (`Aktif/Toplam`) modern bir Kategori Rayı (Navigation Rail) entegre edildi.
  - Sağ taraftaki çalışma alanında kategoriye özgü filtreleme, genel sistem çapında canlı arama (Ctrl + F / Ctrl + K) ve işlem araç çubuğu kusursuz bir hiyerarşide toplandı.

- **Zengin Kategori Bilgilendirme Kartları (Category Hover Cards):**
  - Kategori butonlarının üzerine gelindiğinde kategorinin ne işe yaradığını, sisteme sağladığı katkıyı ve o kategoride kaç ayarın listelenip kaç tanesinin aktif olduğunu açıklayan zengin Fluent bilgilendirme kartları eklendi.

- **Anlaşılır İnce Ayar Hover Kartları (İnsanca Açıklamalar):**
  - Kullanıcı için anlamsız olan ham Kayıt Defteri (Registry) yolları (`HKCU\Software\...`) ve veri tipleri (`REG_DWORD`) arayüzden tamamen temizlendi.
  - Her ayar kartının üzerine gelindiğinde açılan yardım penceresinde:
    - **Ayar Ne İşe Yarar:** Ayarın sisteme ve kullanıcıya sunduğu somut fayda.
    - **Güvenlik & Uyumluluk:** Ayarın herkes için güvenli mi yoksa kişisel bir tercih mi olduğu.
    - **Uygulanma Şekli:** Değişikliğin anında mı geçerli olduğu yoksa oturum/bilgisayar yeniden başlatması mı gerektirdiği.
    - **Geri Alınabilirlik:** Ayarın istendiğinde kapatılıp Windows orijinal varsayılanına dönebileceği bilgisi sade ve anlaşılır Türkçe ile sunuldu.

- **Anti-AI & Kurumsal Tasarım Disiplini:**
  - Arayüzde yapay zeka yapımı hissi veren uyumsuz simgeler ve çocuksu emojiler kaldırıldı; Microsoft Windows 11 yerel tasarım dili (Wpf.Ui Fluent 2 sembolleri ve Slate Dark renk paleti) eksiksiz uygulandı.
