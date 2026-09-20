# Bakım v3.12.0 - Sürüm Notları

## ✨ Yeni Özellikler ve İyileştirmeler

- **WPF Fluent Tray Flyout**: Eski ve hantal sağ tık (WinForms) menüsü kaldırılarak, Windows 11 Action Center hissiyatında animasyonlu ve gölgeli özel WPF menüsü tasarlandı.
- **Ghost Mode (Görünmez Başlangıç)**: Windows ile başla denildiğinde uygulama ekranda belirmek yerine `--autostart` parametresiyle tamamen arka planda çalışmaya başlıyor.
- **Deep Freeze Game Mode İyileştirmeleri**: Oyun modu açıldığında, artık sadece güç planı değişmekle kalmıyor, arka plandaki tüm RAM denetim döngüleri de duraklatılarak %0 CPU tüketimi sağlanıyor.
- **Memory Leak & Dispose Koruması**: Eski sistem tepsisi menüsünden kaynaklı `NullReferenceException` ve bellek sızıntıları giderildi.
