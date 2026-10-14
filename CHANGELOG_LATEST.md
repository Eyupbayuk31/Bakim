# Bakım v4.21.0 - Sürüm Notları

## Gelişmiş PE Adli Bilişim ve Tehdit Analiz Motoru

Tehdit Analizörü (File Threat Analyzer) ikili dosya inceleme altyapısı ileri düzey adli bilişim (binary forensics) yetenekleriyle güçlendirildi. Derin PE başlık incelemesi, şifreli payload tespiti, TLS callback analizi ve kötü niyetli API zincirlerinin yakalanması sağlandı.

### Yenilikler ve İyileştirmeler
- **PE Overlay & Shannon Entropi Analizi:** PE bölümlerinin bittiği ofsetten sonra dosyaya eklenen veriler tespit edilir ve Shannon entropisi (0.0 - 8.0) hesaplanır. Şifreli veya paketlenmiş dropper payload'ları anında ortaya çıkarılır.
- **Authenticode İstisna Kalkanı:** İmzalı Windows ikililerinde dijital sertifika tablosunun overlay alanında yer alması nedeniyle oluşan yanlış alarmlar önlendi. Meşru sistem dosyalarında sıfır sahte pozitif sağlandı.
- **TLS (Thread Local Storage) Callback Denetimi:** Giriş noktası (EntryPoint) çalışmadan önce bellekte yürütülen gizli TLS fonksiyonları taranır; anti-debug ve analiz savuşturma rutinleri yakalanır.
- **Gömülü İkili & BYOVD Tespiti:** PE overlay ve kaynak (.rsrc) bölümlerine gizlenen ikincil çalıştırılabilir dosyalar (.exe) ve savunmasız sürücüler (.sys) otomatik tespit edilir.
- **Derleyici Parmak İzi (Rich Header):** DOS stub üzerindeki DanS/Rich XOR yapısı doğrulanarak dosyanın Microsoft Visual C++ (MSVC), MinGW/GCC veya .NET derleyicileriyle üretildiği belirlenir.
- **Genişletilmiş Win32 API Kural Zincirleri:** Anti-Debug / Evasion, LSASS bellek dökümü ve doğrudan kernel sürücü yükleme (BYOVD) API zincirleri tehdit kataloğuna eklendi.
