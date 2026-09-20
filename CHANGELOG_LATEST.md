• **Analizör Satır Eylemleri Sadeleştirildi**:
  - Her satırda yan yana duran 6 etiketsiz simge 3 kontrole indirildi: metinli **"Analiz Et"** düğmesi, diğer eylemler için taşma menüsü (⋯) ve kırmızı silme düğmesi.
  - Önceki düzende iki ayrı kalkan simgesi yan yanaydı (biri derin analiz, diğeri VirusTotal sorgusu); görsel olarak neredeyse aynı oldukları için hangisine basılacağı belirsizdi.
  - Taşma menüsündeki eylemler artık adlarıyla görünüyor: VirusTotal ile Sorgula, Raporu Tarayıcıda Aç, Dosya Konumunu Aç, SHA-256 Özetini Kopyala.

• **"Analiz Et" Artık Yanıltmıyor**:
  - VirusTotal durum rozeti tarama yapılmamışken **"Analiz Et"** yazıyordu. Emir kipindeki bu ifade tıklanabilir bir düğme izlenimi veriyordu; oysa rozet yalnızca durumu gösteriyor. Artık **"Taranmadı"** yazıyor.

• **Toplu VirusTotal Taraması Düzeltildi**:
  - "VirusTotal ile Toplu Tara" komutu, hiç taranmamış girdileri sessizce atlıyordu: filtre "Taranmadı" değerini arıyor, ancak varsayılan değer farklı olduğu için hiçbir kayıt eşleşmiyordu. Artık tüm taranmamış girdiler doğru şekilde kuyruğa alınıyor.

• **Arka Planda**:
  - XAML komut bağlamalarını denetleyen yeni test katmanı eklendi. WPF'te hatalı bir komut bağlaması sessizce düğmeyi işlevsiz bırakır; bu testler 13 modül genelinde 42 bağlamayı doğruluyor.
