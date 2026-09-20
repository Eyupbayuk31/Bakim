• **Windows 11 Fluent 2 Başlangıç Röntgeni & 4 KPI Kartı**:
  - Başlangıç Uygulamaları modülü baştan aşağı yeniden tasarlanarak sade ve cansız görünümden kurtarıldı.
  - Tepeye 4 adet etkileşimli KPI istatistik kartı eklendi: Toplam Başlatıcı, Etkin Uygulamalar, Devre Dışı Bırakılanlar ve Tahmini Açılış Gecikme Yükü (saniye bazlı dinamik hesaplama).
  - Modern pill filtreleme çubuğu ile Tümü, Etkin, Devre Dışı, Yüksek Etki, Kayıt Defteri ve Klasör filtreleri anında uygulanabilir hale getirildi.

• **Yüksek Çözünürlüklü Yerel Simgeler & Yayıncı Bilgisi**:
  - Program simgeleri doğrudan `.exe` ve `.lnk` dosyalarından Win32 GDI API'si (`ExtractAssociatedIcon` + `CreateBitmapSourceFromHBitmap`) ile bellek sızıntısız şekilde çekilerek arayüze aktarıldı (Discord, Steam, Spotify, Chrome, Riot vb.).
  - Windows `FileVersionInfo` entegrasyonu ile resmi yayıncı ve geliştirici şirket bilgileri listelendi.

• **Genişletilmiş 5 Noktalı Başlangıç Taraması**:
  - Yalnızca standart Run kayıtları değil; HKCU Run, HKLM 64-bit Run, HKLM WOW6432Node (32-bit ve eski oyun istemcileri), Kullanıcı Başlangıç Klasörü (`%APPDATA%`) ve Ortak Sistem Başlangıç Klasörü (`%PROGRAMDATA%`) eksiksiz denetim altına alındı.

• **Tek Tıkla Açılışı Hızlandır (Boot Optimizer)**:
  - Windows'un açılışını geciktiren yüksek etkili arka plan başlatıcıları (Discord, Steam, Chrome, Riot, Spotify vb.) tek tıkla tespit edilerek kullanıcı onayıyla optimize edilir.
  - "+ Başlangıç Uygulaması Ekle" özelliğiyle dosya seçiciden yeni program veya kısayol ekleme imkanı sağlandı.
  - Kayıt defterinden ve diskten kalıcı kaldırma desteği ve sağ tarafa kayan detaylı teftiş çekmecesi eklendi.
