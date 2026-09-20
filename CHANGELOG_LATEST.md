# Bakım v3.15.1 - Sürüm Notları

## Sistem Bilgisi Simgesi Düzeltmesi & Otomatik XAML Doğrulama Koruması

- **Sistem Bilgisi XAML İkon Düzeltmesi (Hotfix):**
  - Donanım ve depolama modülündeki HTML raporu oluşturma butonunda tanımsız olan `OpenInNewWindow20` simgesi yerine Lepo Wpf.Ui kütüphanesinin geçerli `ArrowExport20` Fluent simgesi entegre edildi.
  - Uygulama başlatılırken veya Sistem Bilgisi sekmesine geçildiğinde meydana gelen `XamlParseException: 'OpenInNewWindow20' metninden 'SymbolRegular' oluşturulamadı` hatası tamamen giderildi.

- **Otomatik XAML Sembol Doğrulayıcı (SymbolValidator Testi):**
  - Gelecekte hatalı, eksik veya uydurma `SymbolRegular` simgelerinin projeye eklenmesini derleme ve birim test seviyesinde önleyen otomatik xUnit test mekanizması (`SymbolValidator.cs`) sisteme kazandırıldı.
  - Projedeki tüm XAML dosyaları taranarak kullanılan tüm sembollerin resmi Fluent 2 kütüphanesindeki varlığı doğrulandı.

- **Sürüm Bütünlüğü:**
  - Tüm manifesto, güncelleme servisleri ve kurulum betikleri v3.15.1 sürümüne güncellendi.
