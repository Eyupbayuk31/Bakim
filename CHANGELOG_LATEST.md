• **Sistem Tepsisinde Kesintisiz Nöbet (Close-to-Tray)**:
  - Çarpı (X) butonuna basıldığında uygulama tamamen kapanmak yerine sistem tepsisine (`NotifyIcon`) küçülür ve arka planda bilgisayarı korumaya devam eder.
  - Uygulamayı tamamen kapatmak için sistem tepsisi menüsünden **"Çıkış (Uygulamayı Kapat)"** seçeneği kullanılır.

• **Sessiz Windows Başlangıcı (`--autostart` / `--tray`)**:
  - Bilgisayar açılırken ekrana pencere fırlatmadan doğrudan sistem tepsisinde arka planda sessiz nöbete başlar.
  - Görev Zamanlayıcı ve Registry başlangıç kayıtları sessiz başlatma parametresiyle güncellendi.

• **Ultra Oyun Modu (Game Turbo Engine)**:
  - Oyun esnasında sistem kaynaklarını meşgul edebilecek arka plan RAM denetimleri, soket taramaları, zamanlanmış temizlikler ve masaüstü bildirimleri tamamen dondurulur; CPU ve bellek %100 oyuna odaklanır.
  - Oyun Modu açıldığı an otomatik derin bellek boşaltması (WorkingSet trim) yapılır ve Windows Güç Planı Yüksek Performans şemasına kilitlenir; mod kapatıldığında normal güç şemasına dönülür.

• **Canlı Başlık Çubuğu Mini Telemetri & Oyun Modu Anahtarı**:
  - TitleBar üzerinde anlık CPU ve RAM yükünü gösteren minimal telemetri çipi (`CPU: %12 · RAM: %44`) eklendi.
  - Başlık çubuğuna ve sistem tepsisi menüsüne tek tıkla açılıp kapanabilen Oyun Modu hızlı geçiş anahtarı entegre edildi.

• **Dashboard Turbo Oyun Modu Kartı**:
  - Genel Bakış paneline Oyun Modu'nun anlık durumunu gösteren ve tek tıkla devreye alan özel kontrol kartı eklendi.
