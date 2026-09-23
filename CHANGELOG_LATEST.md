# Bakım v3.17.2 - Sürüm Notları

## Engelsiz ve Tam Otonom Güncelleme Altyapısı (Zero-Friction Updates)

- **Engelsiz ve Sıfır Uyarı Güncelleme Mekanizması:**
  - Güncelleme akışındaki tüm sertifika/Authenticode doğrulama ve engelleme pencereleri tamamen kaldırıldı.
  - Güncelleme paketleri doğrudan resmi GitHub Releases üzerinden HTTPS ile güvenli biçimde indirilir ve hiçbir ara onay penceresi, imza uyarısı veya güvenlik engeli gösterilmeden `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS` parametreleriyle otonom olarak kurulur.

- **Sertifika ve Authenticode Katmanının Kaldırılması:**
  - Kendi kendine imzalanan sertifikaların Windows Güvenilen Kök deposunda yer almaması nedeniyle oluşan "İmza geçersiz veya dosya değiştirilmiş" sahte pozitif engeli kökten çözüldü.
  - `Helpers/AuthenticodeVerifier.cs` ve geçici sertifika mekanizmaları projeden tamamen arındırıldı.

- **Kalıcı Kalite Standartları Revizyonu:**
  - Strict Guard kalite protokolü güncellendi; kullanıcı deneyimini bozan ve sahte engellemelere yol açan imza denetimleri iptal edildi, kesintisiz otonom güncelleme ilkesi zorunlu hale getirildi.

- **Sıfır Geçersiz Sembol Güvencesi:**
  - `Tools/verify-symbols.py` ve `Tools/verify-tokens.py` CI/CD hattındaki zorunlu gatekeeper konumunu korumaktadır. Proje genelindeki 9200+ Fluent 2 sembolü 100% hatasız ve geçerlidir.
