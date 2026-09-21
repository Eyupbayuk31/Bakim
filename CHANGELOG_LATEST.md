# Bakım v3.15.2 - Sürüm Notları

## Güncelleme Kurulumu Engellenme Sorunu & Uygulama Denetimi Yönlendirmesi

- **Kurulum Paketi Konumu Değişti (Ana Düzeltme):**
  - Güncelleme paketi artık `%TEMP%` yerine `%LOCALAPPDATA%\Bakim\Updates` dizinine indiriliyor.
  - Akıllı Uygulama Denetimi (Smart App Control), ASR kuralları ve birçok güvenlik yazılımı geçici dizinden çalıştırılan kurulum dosyalarını düşük itibarlı kabul edip engellediği için engellenme olasılığı belirgin biçimde azaltıldı.
  - Bir günden eski artık kurulum paketleri otomatik olarak temizleniyor.

- **Çalışma Dizini Düzeltmesi:**
  - Kurulum süreci artık çalışan uygulamadan `C:\Program Files\Bakım` dizinini miras almıyor; çalışma dizini paketin kendi klasörüne sabitlendi.
  - Engelleme mesajlarında yanıltıcı dizin adı raporlanması giderildi.

- **Uygulama Denetimi Yönlendirmesi:**
  - Kurulum bir ilke tarafından engellendiğinde ham .NET hata metni yerine yönlendirmeli bir bilgilendirme gösteriliyor.
  - Akıllı Uygulama Denetimi etkinse, kullanıcı doğrudan ilgili Windows Güvenliği ayar ekranına yönlendirilebiliyor veya paketin klasörü açılarak elle kurulum yapılabiliyor.
  - Akıllı Uygulama Denetimi kapalıysa engelin kurumsal WDAC ilkesinden ya da güvenlik yazılımından kaynaklandığı açıkça belirtiliyor.
  - Ayarın bir kez kapatıldığında Windows yeniden kurulmadan geri açılamayacağı uyarısı kullanıcıya açıkça sunuluyor.

- **Ayrıntılı Hata Sınıflandırması:**
  - Yönetici onayının reddi, Uygulama Denetimi ilke engeli ve yetki reddi durumları Win32 hata koduna göre ayrıştırılarak her biri için ayrı ve anlaşılır mesaj gösteriliyor.
  - İlke engeli durumunda indirilen paket silinmiyor; kullanıcının elle kurulum yapabilmesi için korunuyor.

- **Güvenlik:**
  - İndirilen paketten Mark of the Web (`Zone.Identifier`) etiketi temizleniyor.
  - Mevcut imza ve SHA-256 bütünlük denetimleri aynen korunuyor.

- **Sürüm Bütünlüğü:**
  - Tüm manifesto, güncelleme servisleri ve kurulum betikleri v3.15.2 sürümüne güncellendi.
