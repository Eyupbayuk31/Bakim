# Bakım v3.18.5 - Sürüm Notları

## Boş Klasör Seçim Komut Türü Uyumluluğu & Savunmacı Düzeltme

### 1. Komut Türü Dönüşüm Hatasının Giderilmesi
- **Kök Neden:** "Donanım & Depolama" modülü altındaki "Boş Klasörler" sekmesinde yer alan "Tümünü Seç" ve "Seçimi Kaldır" butonları, XAML üzerinden komuta parametre olarak dize (`string "True"`, `"False"`) gönderirken, ViewModel tarafındaki `ToggleSelectAllEmptyFolders` komutu `bool` beklediği için `System.ArgumentException` (`Parameter cannot be of type System.String, as the command type requires an argument of type System.Boolean`) istisnası tetikleniyordu.
- **Kalıcı Çözüm:** `ToggleSelectAllEmptyFolders` komut parametresi `object?` olarak esnetildi. Hem `bool` (`true`, `false`) hem de `string` (`"True"`, `"False"`) değerlerini hatasız karşılayıp güvenle çözümleyen savunmacı bir ayrıştırıcı uygulandı.

### 2. Kalite, Test ve Doğrulama
- **170/170 Birim Testi:** `Theory` veri matrisiyle `"True"`, `"False"`, `true` ve `false` parametrelerinin komut yürütme katmanında kusursuz çalıştığı birim testleriyle kanıtlandı.
- **Arayüz Duman Testleri:** Tüm modüller, temalar ve pencereler 100% başarıyla geçti.
- **9.235 Sembol & Tasarım Tokeni:** `verify-symbols.py` ve `verify-tokens.py` ile sıfır hata doğrulandı.
- **Sıfır Uyarı / Sıfır Hata:** .NET 10 Release derlemesi temiz şekilde tamamlandı.
