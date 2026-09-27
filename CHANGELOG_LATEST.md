# Bakım v4.14.0 - Sürüm Notları

## Analizör v3 · Faz 6 + Faz 8: Kalıcı Tarama Önbelleği ve Komut Satırı Derlemi

### Daha hızlı ilk tarama (Faz 6)
- **Kalıcı önbellek:** İmza doğrulaması ve SHA-256 sonuçları artık diske yazılıyor (`%LocalAppData%\Bakim\History\Analyzer\enrich-cache.json`). Bakım yeniden açıldığında Analizör'ün ilk taraması bu sonuçları yeniden hesaplamıyor.
- **Ölçüm (geliştirme makinesi):** Uygulama açılışından sonraki ilk tarama 4,5 sn'den 1,5 sn'ye indi. Aynı oturumdaki tekrar taramalar 0,8 sn.
- **Güvenli geçersizleşme:** Kayıt yol + boyut + değişim zamanıyla eşleşiyor; dosya güncellenirse ya da değiştirilirse imza yeniden doğrulanıyor.
- **Sınırlı boyut:** En çok 6000 dosya tutuluyor, en uzun süre kullanılmayanlar atılıyor. Dosya bozulursa sessizce yeniden oluşturuluyor.

### Komut satırı çözümleyici derlemi (Faz 8)
- **29 yeni durum:** Run girdileri, zamanlanmış görevler, hizmet yolları (`svchost -k`, tırnaklı argümanlar), sürücüler (`System32\drivers`, `\SystemRoot`, `\??\`, DriverStore), Winlogon değerleri, 8.3 kısa adlar, sarmalayıcılar (rundll32, cmd, powershell, wscript) ve yetim girdiler için.
- **Kişisel veri yok:** Tüm yollar sentetik; Linux CI'da da çalışıyor.

**Testler:** Tüm birim testleri yeşil; 4 denetim betiği tam geçti.
