# Bakım v3.23.0 - Sürüm Notları

## Ağ Teşhis Paketi, DNS Karşılaştırma, Güvenli Avcı Modu & Dürüst Sistem Araçları

### 1. Ağ Teşhisleri & Canlı Traceroute (İzleme)
- **TTL Tabanlı Paket Rotalama:** Ağ hedeflerine doğru giden her bir sekmedeki (hop) yönlendiriciyi ICMP TTL paketleriyle adım adım tespit eder.
- **Ters DNS Çözümleme:** Her sekmenin IP adresi için arka planda asenkron ters alan adı çözümlemesi gerçekleştirilir.
- **Canlı İlerleme & İptal Desteği:** Sekme sekme gerçek gecikme süreleri hesaplanır; istenildiğinde tanılama işlemi anında iptal edilebilir.

### 2. DNS Benchmark & Hız Kıyaslama Aracı
- **Global Güvenilir Sunucular:** Cloudflare (1.1.1.1, 1.0.0.1), Google (8.8.8.8, 8.8.4.4), Quad9 (9.9.9.9), OpenDNS (208.67.222.222), AdGuard (94.140.14.14) ve Comodo Secure (8.26.56.26) anycast sunucuları dahil edilmiştir.
- **Gerçek DNS Sorgu Gecikmesi:** Standart ICMP ping yerine gerçek 53. port UDP DNS sorguları üzerinden yanıt süreleri ölçülür (fallback olarak ping desteklenir).
- **En Hızlı Sunucu Etiketi:** En düşük ortalama gecikmeye sahip DNS sunucusu otomatik olarak tespit edilir ve "En Hızlı" rozetiyle vurgulanır.

### 3. Tarifeli Ağ (Metered Connection) Tespiti ve Koruması
- **Üç Katmanlı Denetim:** Windows COM `INetworkCostManager`, hücresel WWAN arayüz tespiti ve kayıt defteri `DefaultMediaCost` politikaları birlikte sorgulanır.
- **Kota Tüketim Uyarısı:** Kotalı/mobil ağlarda gigabit hız testi çalıştırılmadan önce kullanıcıya veri harcama uyarısı gösterilir ve gereksiz kota tüketimi engellenir.

### 4. Ağ Güvenlik Duvarı Yönetimi & Analizör Entegrasyonu
- **Bakım Güvenlik Duvarı Kuralları:** Bakım tarafından oluşturulan `Bakim_Block_*` güvenlik duvarı engelleme kuralları ayrı bir kartta listelenir; kullanıcı tek tıkla engelleri kaldırabilir.
- **Ağ Çekmecesinden Derin Analiz:** Aktif soket ve bağlantı listesindeki şüpheli süreçler için çekmeceden tek tıkla `Analizör ile Tara` eylemi tetiklenebilir.

### 5. Avcı Modu (Hunter) Sistem Koruması
- **Kritik Süreç Koruması:** `CriticalProcessPolicy` muhafızı Avcı Modu'na entegre edildi. Windows işletim sisteminin kritik süreçleri (csrss, lsass, smss, winlogon, services vb.) yanlışlıkla sonlandırılamaz; koruma uyarısı gösterilir.
- **Analizör Entegrasyonu:** Avcı Modu tehdit analiz diyalogu tam DI (`IAutorunsScannerEngine`, `IVirusTotalCheckService`) bileşenleriyle güçlendirildi.

### 6. TrustedInstaller ve Sistem Araçları Dürüstlüğü (S-17 / DEN G-6)
- **Şeffaf Geri Düşüş Raporlaması:** TrustedInstaller belirteci veya süreci temin edilemediğinde ve Standart Yönetici (Administrator) seviyesinde çalıştırma yapıldığında, kullanıcıya sahte TI başarısı yerine durum dürüstçe (`TrustedInstallerLaunchOutcome`) bildirilir.
- **Net Durum Bildirimi:** Arayüz banner'ında komutun gerçekten TrustedInstaller olarak mı yoksa Yönetici haklarıyla mı çalıştığı açıkça ayrıştırılır.
