# Bakım v4.2.4 - Sürüm Notları

## CI/CD Doğrulama Kararlılığı & Birim Test İzolasyonu

Bakım v4.2.4 sürümü; GitHub Actions CI/CD derleme hattındaki Windows konsol kodlama hatalarını gidermekte, Kurulum Nöbetçisi birim testlerini izole geçici çalışma alanına taşımakta ve tüm doğrulama cırcırlarını kusursuzlaştırmaktadır.

---

### 1. CI/CD Doğrulama Kararlılığı (Unicode / Charmap Düzeltmesi)
- **UTF-8 Çıkış Yapılandırması:** Windows tabanlı GitHub Actions çalıştırıcılarında Türkçe özel karakterlerin konsol kod sayfasında (`cp1252` / `charmap`) `UnicodeEncodeError` oluşturması engellendi; Python araçlarına UTF-8 akış yapılandırması ve CI iş akışlarına `PYTHONIOENCODING: utf-8` ortam değişkeni eklendi.
- **Güvenli Durum Mesajları:** Bağlama doğrulama betiğindeki çıktı metinleri platformlar arası güvenli ASCII standartlarına uyarlandı.

---

### 2. Kurulum Nöbetçisi Test İzolasyonu
- **Parametrik SessionStore:** `SessionStore` sınıfına isteğe bağlı depolama dizini (`storageDirectory`) parametresi kazandırıldı.
- **Kullanıcı Verisinden Bağımsız Birim Testler:** Birim testlerin yerel kullanıcı profili dizinindeki (`%APPDATA%\Bakım\InstallationLogs`) geçmiş kurulum günlüklerinden etkilenmesi engellendi; her test izole geçici dizinlerde çalışacak şekilde güçlendirildi.

---

### 3. Cümle Düzeni ve Komut Bağlama Uyumu
- **WPF Eylem Butonları:** Windows 11 cümle düzeni (sentence-case) standartlarına geçişle güncellenen buton metinleri birim test kontrol kıstaslarıyla tam senkronize edildi.
- **Eksiksiz Test Başarısı:** Çekirdek mantık (462 test) ve UI/WPF katmanı (307 test) olmak üzere toplam 769 testin tamamı %100 başarıyla tamamlandı.
