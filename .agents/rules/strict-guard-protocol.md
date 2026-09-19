---
trigger: always_on
description: "Strict Guard & Zero-Hallucination Protocol (V2.0) for Bakım Project"
---

# Bakım Projesi - Strict Guard & Zero-Hallucination Protokolü (V2.0)

Bu protokol, 'Bakım' çözümü altındaki tüm kod geliştirme, refactoring ve hata ayıklama süreçlerinde en yüksek kalite ve doğruluk standardını garanti altına alır:

---

### 1. SIFIR HALÜSİNASYON & NAMESPACE DOĞRULAMA (Zero Hallucination)
- **Var Olmayan Etiket Yasağı:** Kullanılan kütüphanelerin (`Wpf.Ui`, `CommunityToolkit.Mvvm`, `System.IO` vb.) resmi API ve dokümantasyonunda yer almayan uydurma etiketler (`ui:ProgressBar`, `ui:CustomCard` vb.) veya hayali özellikler (property) türetmek KESİNLİKLE YASAKTIR.
- **Namespace Kontrolü:** Bir XAML veya C# bileşeni önerilmeden önce ilgili kütüphanenin namespace (`xmlns:ui="..."`) ve assembly bağımlılıkları doğrulanır. Emin olunmayan bileşenlerde standart .NET / WPF karşılıkları güvenli fallback (yedek) olarak kullanılır.

---

### 2. KODU KISALTMA VE LAZY-CODE YASAĞI (Full Code Delivery)
- **Tembel Kodlama Yasağı:** `" // ... eski kodlar buraya gelecek ... "`, `" // ... kalan kısımlar aynı ... "` gibi ifadelerle kodu yarım bırakmak, fonksiyon gövdelerini boşaltmak veya çalışan blokları silmek KESİNLİKLE YASAKTIR.
- **Eksiksiz Çıktı:** Sorun düzeltilirken veya geliştirme yapılırken, ilgili dosyanın veya bloğun kopyalanıp doğrudan projeye yapıştırılabilir, eksiksiz, üretim seviyesinde (Production-Ready) tam hali sunulur.

---

### 3. ZİNCİRLEME DÜŞÜNCE İLE BUG ÇÖZÜMÜ (Chain-of-Thought Debugging)
Derleme hatası (Build Error), Exception veya Runtime crash durumunda çözüme atlamadan önce şu 3 adım sırasıyla uygulanır:
1. **Kök Neden Analizi (Root Cause):** Hatanın tam sebebi ve hangi kütüphane/namespace çakışmasından kaynaklandığı 1-2 cümle ile açıklanır.
2. **Mimari Risk Değerlendirmesi:** Yapılacak düzeltmenin projenin diğer bileşenlerine (MVVM yapısı, UI Thread, Async/Await dengesi) zarar vermeyeceği doğrulanır.
3. **Eksiksiz Düzeltme:** Hatalı bloğu çevreleyen tüm kod eksiksiz olarak yeniden yazılır.

---

### 4. MEMORY & PERFORMANCE SAFETY (Non-Blocking UI)
- Sistem I/O işlemleri (Temp temizleme, Registry okuma, Disk tarama) düzeltilirken `async/await` mimarisi bozulamaz.
- UI Thread'ini kilitleyecek senkron I/O işlemlerine asla dönülemez.
- Bellek sızıntılarını önlemek için Event Unsubscription, `IDisposable` ve Null-Check (`?.`) disiplininden taviz verilemez.
