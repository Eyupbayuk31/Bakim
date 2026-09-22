# Bakım v3.15.2 - Sürüm Notları

## Windows ile Otomatik Başlama (Autostart) Kökten Onarımı & Güncelleme İyileştirmeleri

- **Kusursuz Windows Başlangıç Motoru (Task Scheduler XML):**
  - Uygulama `RUNASADMIN` ile işaretlendiğinde Windows Logon mimarisinin standart Kayıt Defteri (`Run` anahtarı) başlangıcını sessizce engellemesi sorunu kökten çözüldü.
  - Windows Görev Zamanlayıcısı (`Task Scheduler`) XML tabanlı garantili kayıt mimarisine geçirildi. Tırnak, boşluk ve Türkçe karakter sorunları bertaraf edildi.
  - Standart kullanıcı modunda çalışırken tek seferlik UAC işçisi (`--register-autostart`) ile UAC uyarısız en yüksek yetkili başlangıç kaydı sorunsuz oluşturulmaktadır.

- **Canlı Başlangıç Durum Teşhisi (Autostart Health Diagnostics):**
  - Ayarlar -> Sistem Başlangıcı sekmesine Windows başlangıcının gerçek durumunu anlık denetleyen akıllı durum rozeti eklendi:
    - 🟢 *Aktif (Görev Zamanlayıcı - UAC Uyarısız Yönetici)*
    - 🟡 *Aktif (Kayıt Defteri - Standart Kullanıcı)*
    - 🔴 *Engellendi (Windows UAC Kısıtlaması)*
    - ⚪ *Devre Dışı*

- **Tek Tıkla "Başlangıcı Onar & Kur" Aracı:**
  - Çakışan AppCompat ve yetkisiz Run kayıtlarını temizleyen, doğru Görev Zamanlayıcı kaydını kuran ve Windows açılışını garanti altına alan onarım mekanizması entegre edildi.

- **ControlAppearance & XAML Doğrulama Güvencesi:**
  - Sürücü açma butonundaki tanımsız `Appearance="Subtle"` değeri `Appearance="Secondary"` olarak düzeltildi (`Subtle is not a valid value for ControlAppearance` hatası giderildi).
  - Projedeki tüm XAML dosyalarındaki `ControlAppearance` ve `SymbolRegular` değerlerini derleme/test seviyesinde denetleyen xUnit birim testleri sisteme dahil edildi.

- **Güncelleme Kurulumu & Uygulama Denetimi İyileştirmeleri:**
  - Güncelleme paketi artık `%TEMP%` yerine `%LOCALAPPDATA%\Bakim\Updates` dizinine indirilerek Smart App Control / WDAC engeli olasılığı düşürüldü.
  - Bir günden eski artık kurulum paketleri otomatik temizleniyor ve indirilen paketten Mark of the Web (`Zone.Identifier`) etiketi kaldırılıyor.
  - Kurulum bir ilke tarafından engellendiğinde yönlendirmeli bilgilendirme ve klasörü açma seçeneği sunuluyor.
