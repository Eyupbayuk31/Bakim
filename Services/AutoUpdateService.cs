using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public class UpdateInfo
    {
        public bool IsUpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = "3.17.3";

        /// <summary>
        /// Sürüm denetimi için varsayılan zaman aşımı (saniye).
        /// </summary>
        public int TimeoutSeconds { get; set; } = 8;

        public string LatestVersion { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string ReleasePageUrl { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }

    public static class AutoUpdateService
    {
        // TARGET REPO: Eyupbayuk31/Bakim
        private const string GitHubApiUrl = "https://api.github.com/repos/Eyupbayuk31/Bakim/releases/latest";
        public const string DefaultCurrentVersion = "3.17.3";

        public static Version GetCurrentVersion()
        {
            var asmVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (asmVersion != null && asmVersion > new Version(1, 0, 0))
            {
                return asmVersion;
            }
            return new Version(2, 8, 0);
        }

        public static string GetDefaultChangelog()
        {
            return "• Hata düzeltmeleri ve kararlılık iyileştirmeleri uygulandı.";
        }

        public static string FormatChangelog(string? rawBody, string tagName)
        {
            if (string.IsNullOrWhiteSpace(rawBody) || rawBody.Trim().Length < 5)
            {
                return GetDefaultChangelog();
            }

            // GitHub linklerini, başlıkları ve otomatik compare satırlarını temizle
            var lines = rawBody.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var cleanLines = new System.Collections.Generic.List<string>();

            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                // Başlık veya link içeren gereksiz satırları atla
                if (trimmed.StartsWith("#") ||
                    trimmed.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("Full Changelog", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("/compare/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Madde işareti düzeltmesi
                if (!trimmed.StartsWith("•") && !trimmed.StartsWith("-") && !trimmed.StartsWith("*"))
                {
                    trimmed = "• " + trimmed;
                }

                cleanLines.Add(trimmed);
            }

            if (cleanLines.Count == 0)
            {
                return GetDefaultChangelog();
            }

            return string.Join("\n", cleanLines);
        }

        public static async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.Add("User-Agent", "Bakim-App-AutoUpdater (Eyupbayuk31/Bakim)");
                client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

                // Anlık ve taze kontrol: Önbelleği tamamen devre dışı bırak
                client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
                {
                    NoCache = true,
                    NoStore = true,
                    MustRevalidate = true
                };
                client.DefaultRequestHeaders.Pragma.ParseAdd("no-cache");

                // Cache-buster parametresi ile GitHub CDN ve yerel Windows proxy önbelleğini atla
                string noCacheUrl = $"{GitHubApiUrl}?_nocache={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                var response = await client.GetAsync(noCacheUrl);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new UpdateInfo
                    {
                        IsUpdateAvailable = false,
                        CurrentVersion = GetCurrentVersion().ToString(3),
                        LatestVersion = GetCurrentVersion().ToString(3),
                        ErrorMessage = "GitHub üzerinde henüz yayınlanmış genel bir sürüm (Release) bulunamadı."
                    };
                }

                if (!response.IsSuccessStatusCode)
                {
                    // GitHub API rate limit (403 Forbidden) veya sunucu hatası durumunda web redirect fallback'e geç
                    var fallbackResult = await CheckForUpdatesViaWebRedirectAsync();
                    if (fallbackResult != null)
                    {
                        return fallbackResult;
                    }

                    return new UpdateInfo
                    {
                        IsUpdateAvailable = false,
                        CurrentVersion = GetCurrentVersion().ToString(3),
                        ErrorMessage = $"GitHub API Hatası: {response.StatusCode} ({(int)response.StatusCode})"
                    };
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string rawTag = root.GetProperty("tag_name").GetString() ?? "0.0.0";
                string cleanTagName = rawTag.TrimStart('v', 'V', ' ');
                string rawBody = root.TryGetProperty("body", out var b) ? (b.GetString() ?? "") : "";
                string body = FormatChangelog(rawBody, cleanTagName);
                string htmlUrl = root.TryGetProperty("html_url", out var h) ? (h.GetString() ?? "https://github.com/Eyupbayuk31/Bakim/releases") : "https://github.com/Eyupbayuk31/Bakim/releases";

                string downloadUrl = string.Empty;
                long assetSize = 0;

                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    // 1. Tercih: -Setup.exe kurulum paketi (Sessiz tam kurulum güncellemesi için)
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            if (asset.TryGetProperty("size", out var s)) assetSize = s.GetInt64();
                            break;
                        }
                    }

                    // 2. Tercih: Herhangi bir .exe dosyası
                    if (string.IsNullOrEmpty(downloadUrl))
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            string name = asset.GetProperty("name").GetString() ?? "";
                            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                                if (asset.TryGetProperty("size", out var s)) assetSize = s.GetInt64();
                                break;
                            }
                        }
                    }
                }

                Version currentVersion = GetCurrentVersion();
                Version latestVersion = ParseSemVer(cleanTagName);

                return new UpdateInfo
                {
                    IsUpdateAvailable = latestVersion > currentVersion,
                    CurrentVersion = currentVersion.ToString(3),
                    LatestVersion = cleanTagName,
                    ReleaseNotes = body,
                    DownloadUrl = downloadUrl,
                    FileSizeBytes = assetSize,
                    ReleasePageUrl = htmlUrl
                };
            }
            catch (Exception ex)
            {
                try
                {
                    var fallbackResult = await CheckForUpdatesViaWebRedirectAsync();
                    if (fallbackResult != null)
                    {
                        return fallbackResult;
                    }
                }
                catch { }

                return new UpdateInfo
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = GetCurrentVersion().ToString(3),
                    ErrorMessage = $"Güncelleme sorgusu başarısız: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Güncelleme paketinin indirilmesine izin verilen kaynaklar.
        /// Bu liste dışındaki bir adres (ör. ele geçirilmiş sürüm notundan gelen bağlantı)
        /// asla indirilip çalıştırılmaz.
        /// </summary>
        private static readonly string[] AllowedDownloadHosts =
        {
            "github.com",
            "objects.githubusercontent.com",
            "release-assets.githubusercontent.com",
            "api.github.com"
        };

        /// <summary>İndirme adresinin HTTPS ve beklenen GitHub kaynağından olduğunu doğrular.</summary>
        public static bool IsTrustedDownloadUrl(string downloadUrl)
        {
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)) return false;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;

            foreach (string host in AllowedDownloadHosts)
            {
                if (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        public static async Task DownloadAndExecuteInstallerAsync(string downloadUrl, Action<int>? onProgressChanged = null)
        {
            // 1) KAYNAK DENETİMİ — yabancı bir adresten indirilen paket çalıştırılmaz.
            if (!IsTrustedDownloadUrl(downloadUrl))
            {
                AppLog.Error($"Güvenilmeyen güncelleme adresi reddedildi: {downloadUrl}", null, nameof(AutoUpdateService));

                ShowOnUiThread(() => MessageBox.Show(
                    "Güncelleme paketi beklenmeyen bir adresten sunuluyor ve güvenlik gereği indirilmedi.\n\n" +
                    $"Adres: {downloadUrl}\n\n" +
                    "Lütfen güncellemeyi projenin resmî GitHub sürümler sayfasından elle indirin.",
                    "Güncelleme Engellendi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error));

                return;
            }

            // Paket bilinçli olarak %TEMP% altına indirilmez: Akıllı Uygulama Denetimi,
            // ASR kuralları ve birçok güvenlik yazılımı geçici dizinden çalıştırılan kurulum
            // dosyalarını düşük itibarlı kabul edip engeller.
            string stagingDirectory = GetUpdateStagingDirectory();
            PurgeStaleInstallers(stagingDirectory);

            // Eşzamanlı/eski indirmelerin üzerine yazmaması için benzersiz ad
            string installerPath = Path.Combine(
                stagingDirectory,
                $"Bakim_Setup_Update_{Guid.NewGuid():N}.exe");

            AppLog.Info($"Güncelleme indiriliyor: {downloadUrl}", nameof(AutoUpdateService));

            using (var client = new HttpClient())
            using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long totalBytes = response.Content.Headers.ContentLength ?? -1;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[16384];
                    long totalReadBytes = 0;
                    int readBytes;

                    while ((readBytes = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, readBytes);
                        totalReadBytes += readBytes;

                        if (totalBytes > 0)
                        {
                            int progress = (int)((double)totalReadBytes / totalBytes * 100);
                            onProgressChanged?.Invoke(progress);
                        }
                    }
                }
            }

            // Mark of the Web temizliği — indirme yolu ileride değişse bile paket
            // "internetten indirildi" damgasıyla SmartScreen'e takılmasın.
            RemoveMarkOfTheWeb(installerPath);

            // 2) BÜTÜNLÜK DENETİMİ — paket boyutu doğrulanır ve doğrudan sessiz kuruluma geçilir.
            var fileInfo = new FileInfo(installerPath);
            if (!fileInfo.Exists || fileInfo.Length == 0)
            {
                AppLog.Error("Güncelleme paketi indirilemedi veya dosya boş.", null, nameof(AutoUpdateService));
                TryDelete(installerPath);
                return;
            }

            AppLog.Info(
                $"Güncelleme paketi hazır ({fileInfo.Length:N0} bayt), sessiz kuruluma geçiliyor.",
                nameof(AutoUpdateService));

            // 3) Inno Setup Sessiz Kurulum Parametreleri ile Başlat ve Uygulamayı Kapat
            // /VERYSILENT: Hiçbir pencere göstermez
            // /SUPPRESSMSGBOXES: Mesaj kutusu sormaz
            // /NORESTART: Bilgisayarı yeniden başlatmaz
            // /CLOSEAPPLICATIONS: Eski açık Bakım uygulamasını arka planda kapatıp dosyaları günceller
            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
                // Çalışma dizini açıkça paketin klasörüne sabitlenir. Aksi halde süreç, çalışan
                // uygulamadan "C:\Program Files\Bakım" dizinini miras alır; bu hem gereksiz bir
                // yazma izni beklentisi doğurur hem de engelleme mesajlarında yanıltıcı bir
                // dizin adının raporlanmasına yol açar.
                WorkingDirectory = Path.GetDirectoryName(installerPath) ?? stagingDirectory,
                UseShellExecute = true,
                Verb = "runas"
            };

            try
            {
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                // UAC reddi, Uygulama Denetimi ilkesi ve yetki hataları burada ayrışır.
                HandleInstallerLaunchFailure(ex, installerPath);
                return;
            }

            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
            }
            else
            {
                Environment.Exit(0);
            }
        }

        /// <summary>Arka plan indirme görevinden arayüz penceresi açmak için güvenli geçiş.</summary>
        private static void ShowOnUiThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;

            if (dispatcher == null) action();
            else if (dispatcher.CheckAccess()) action();
            else dispatcher.Invoke(action);
        }

        // Win32 hata kodları (winerror.h)
        private const int ErrorFileNotFound = 2;
        private const int ErrorAccessDenied = 5;
        private const int ErrorCancelled = 1223;
        private const int ErrorAccessDisabledByPolicy = 1260;

        /// <summary>Akıllı Uygulama Denetimi (Smart App Control) ilke durumları.</summary>
        private enum SmartAppControlState
        {
            Unknown = -1,
            Off = 0,
            Enforced = 1,
            Evaluation = 2
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteFile(string lpFileName);

        /// <summary>
        /// Güncelleme paketinin indirileceği kalıcı hazırlık dizinini döndürür ve oluşturur.
        /// Dizin oluşturulamazsa güncelleme akışı durdurulmaz; geçici dizine geri dönülür.
        /// </summary>
        private static string GetUpdateStagingDirectory()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                if (!string.IsNullOrWhiteSpace(localAppData))
                {
                    string stagingDirectory = Path.Combine(localAppData, "Bakim", "Updates");
                    Directory.CreateDirectory(stagingDirectory);
                    return stagingDirectory;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning(
                    "Güncelleme hazırlık dizini oluşturulamadı; geçici dizine dönülüyor.",
                    ex,
                    nameof(AutoUpdateService));
            }

            return Path.GetTempPath();
        }

        /// <summary>
        /// Yarım kalan veya kurulumu tamamlanmış önceki paketleri temizler.
        /// Tek bir dosyanın kilitli olması tüm güncelleme akışını durdurmaz.
        /// </summary>
        private static void PurgeStaleInstallers(string stagingDirectory)
        {
            try
            {
                DateTime threshold = DateTime.UtcNow.AddDays(-1);

                foreach (string file in Directory.EnumerateFiles(stagingDirectory, "Bakim_Setup_Update_*.exe"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < threshold)
                        {
                            File.Delete(file);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warning($"Eski güncelleme paketi silinemedi: {file}", ex, nameof(AutoUpdateService));
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning("Eski güncelleme paketleri taranamadı.", ex, nameof(AutoUpdateService));
            }
        }

        /// <summary>
        /// Dosyadan "Mark of the Web" etiketini (Zone.Identifier alternatif veri akışı) kaldırır.
        /// <see cref="File.Delete(string)"/> alternatif veri akışı yollarını kabul etmediği için
        /// doğrudan Win32 çağrılır.
        /// </summary>
        private static void RemoveMarkOfTheWeb(string filePath)
        {
            try
            {
                if (DeleteFile(filePath + ":Zone.Identifier")) return;

                int error = Marshal.GetLastWin32Error();

                // Akışın hiç bulunmaması beklenen ve istenen durumdur.
                if (error != ErrorFileNotFound)
                {
                    AppLog.Warning(
                        $"Zone.Identifier akışı kaldırılamadı (Win32 hata {error}).",
                        null,
                        nameof(AutoUpdateService));
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning("Mark of the Web temizliği başarısız oldu.", ex, nameof(AutoUpdateService));
            }
        }

        /// <summary>
        /// Akıllı Uygulama Denetimi'nin ilke durumunu okur. Değer yalnızca okunur;
        /// yönetici yetkisi gerektirmez ve sistemde hiçbir değişiklik yapılmaz.
        /// </summary>
        private static SmartAppControlState GetSmartAppControlState()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CI\Policy");

                if (key?.GetValue("VerifiedAndReputablePolicyState") is int state)
                {
                    return state switch
                    {
                        0 => SmartAppControlState.Off,
                        1 => SmartAppControlState.Enforced,
                        2 => SmartAppControlState.Evaluation,
                        _ => SmartAppControlState.Unknown
                    };
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning("Akıllı Uygulama Denetimi durumu okunamadı.", ex, nameof(AutoUpdateService));
            }

            return SmartAppControlState.Unknown;
        }

        /// <summary>
        /// İstisna zincirini tarayarak ilk <see cref="Win32Exception"/> örneğinin yerel hata
        /// kodunu döndürür. Zincirde Win32 hatası yoksa <c>null</c> döner.
        /// </summary>
        private static int? ExtractWin32ErrorCode(Exception? exception)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is Win32Exception win32Exception)
                {
                    return win32Exception.NativeErrorCode;
                }
            }

            return null;
        }

        /// <summary>
        /// Kurulum paketi başlatılamadığında hatayı sınıflandırır ve kullanıcıya ham .NET
        /// mesajı yerine uygulanabilir bir yönlendirme sunar.
        /// </summary>
        private static void HandleInstallerLaunchFailure(Exception ex, string installerPath)
        {
            // Process.Start hatayı "An error occurred trying to start process..." metniyle
            // sarmalayabildiği için kod, istisna zinciri taranarak çıkarılır.
            int? nativeErrorCode = ExtractWin32ErrorCode(ex);

            switch (nativeErrorCode)
            {
                case ErrorCancelled:
                    AppLog.Info("Kullanıcı güncelleme için yönetici onayı vermedi.", nameof(AutoUpdateService));
                    TryDelete(installerPath);

                    ShowOnUiThread(() => MessageBox.Show(
                        "Güncelleme kurulumu için yönetici izni verilmedi.\n\n" +
                        "Güncellemeyi daha sonra yeniden başlatabilirsiniz.",
                        "Güncelleme",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information));
                    break;

                case ErrorAccessDisabledByPolicy:
                    // Paket bilinçli olarak silinmez: kullanıcı kurulumu elle denemek isteyebilir.
                    ShowPolicyBlockGuidance(installerPath);
                    break;

                case ErrorAccessDenied:
                    AppLog.Warning("Güncelleme paketi erişim reddi nedeniyle başlatılamadı.", ex, nameof(AutoUpdateService));

                    ShowOnUiThread(() => MessageBox.Show(
                        "Güncelleme kurulumu yetki reddi nedeniyle başlatılamadı.\n\n" +
                        "Bakım'ı yönetici olarak çalıştırıp güncellemeyi yeniden deneyin.",
                        "Güncelleme",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning));
                    break;

                default:
                    AppLog.Warning(
                        $"Güncelleme kurulumu başlatılamadı (Win32 hata {nativeErrorCode?.ToString() ?? "yok"}).",
                        ex,
                        nameof(AutoUpdateService));
                    TryDelete(installerPath);

                    ShowOnUiThread(() => MessageBox.Show(
                        $"Güncelleme kurulumu başlatılamadı: {ex.Message}",
                        "Güncelleme",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning));
                    break;
            }
        }

        /// <summary>
        /// Uygulama Denetimi ilkesi paketi engellediğinde kullanıcıyı bilgilendirir ve
        /// engelin kaynağına göre uygun eylemi sunar.
        /// </summary>
        private static void ShowPolicyBlockGuidance(string installerPath)
        {
            SmartAppControlState state = GetSmartAppControlState();

            AppLog.Warning(
                $"Güncelleme paketi Uygulama Denetimi ilkesi tarafından engellendi " +
                $"(Akıllı Uygulama Denetimi: {state}). Paket korundu: {installerPath}",
                null,
                nameof(AutoUpdateService));

            bool isSmartAppControlActive =
                state is SmartAppControlState.Enforced or SmartAppControlState.Evaluation;

            ShowOnUiThread(() =>
            {
                if (isSmartAppControlActive)
                {
                    var answer = MessageBox.Show(
                        "Güncelleme paketi Windows'un Akıllı Uygulama Denetimi (Smart App Control) " +
                        "ilkesi tarafından engellendi.\n\n" +
                        "Nedeni, kurulum paketinin henüz kod imzalama sertifikasıyla imzalanmamış " +
                        "olmasıdır. Bu engel uygulama içinden aşılamaz.\n\n" +
                        "Evet — Akıllı Uygulama Denetimi ayarlarını açar.\n" +
                        "Hayır — İndirilen paketin klasörünü açar, kurulumu elle deneyebilirsiniz.\n" +
                        "İptal — Güncellemeyi erteler.\n\n" +
                        "UYARI: Akıllı Uygulama Denetimi bir kez kapatıldığında, Windows yeniden " +
                        "kurulmadan tekrar açılamaz.",
                        "Güncelleme Engellendi",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);

                    if (answer == MessageBoxResult.Yes)
                    {
                        OpenSmartAppControlSettings();
                    }
                    else if (answer == MessageBoxResult.No)
                    {
                        RevealInExplorer(installerPath);
                    }

                    return;
                }

                var fallbackAnswer = MessageBox.Show(
                    "Güncelleme paketi bir Uygulama Denetimi ilkesi tarafından engellendi.\n\n" +
                    "Akıllı Uygulama Denetimi bu bilgisayarda kapalı görünüyor; engel büyük " +
                    "olasılıkla kurumsal bir WDAC ilkesinden veya güvenlik yazılımınızdan " +
                    "kaynaklanıyor. Kısıtlamayı yalnızca sistem yöneticiniz kaldırabilir.\n\n" +
                    "İndirilen paketin klasörü açılsın mı?",
                    "Güncelleme Engellendi",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (fallbackAnswer == MessageBoxResult.Yes)
                {
                    RevealInExplorer(installerPath);
                }
            });
        }

        /// <summary>
        /// Windows Güvenliği'ndeki Akıllı Uygulama Denetimi sayfasını açar. Derin bağlantı
        /// segmenti Windows sürümüne göre değişebildiğinden en özelden en genele doğru denenir.
        /// </summary>
        private static void OpenSmartAppControlSettings()
        {
            string[] candidateUris =
            {
                "windowsdefender://smartappcontrol",
                "windowsdefender://appbrowser",
                "windowsdefender://"
            };

            foreach (string uri in candidateUris)
            {
                try
                {
                    using var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = uri,
                        UseShellExecute = true
                    });

                    AppLog.Info($"Windows Güvenliği açıldı: {uri}", nameof(AutoUpdateService));
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.Warning($"Ayar sayfası açılamadı: {uri}", ex, nameof(AutoUpdateService));
                }
            }

            ShowOnUiThread(() => MessageBox.Show(
                "Windows Güvenliği açılamadı.\n\n" +
                "Ayara elle ulaşmak için: Windows Güvenliği > Uygulama ve tarayıcı denetimi > " +
                "Akıllı Uygulama Denetimi ayarları.",
                "Güncelleme",
                MessageBoxButton.OK,
                MessageBoxImage.Information));
        }

        /// <summary>İndirilen paketi Dosya Gezgini'nde seçili olarak gösterir.</summary>
        private static void RevealInExplorer(string installerPath)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{installerPath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppLog.Warning("Güncelleme paketinin klasörü açılamadı.", ex, nameof(AutoUpdateService));

                ShowOnUiThread(() => MessageBox.Show(
                    $"Klasör açılamadı.\n\nPaketin konumu:\n{installerPath}",
                    "Güncelleme",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information));
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                AppLog.Warning($"Geçici güncelleme dosyası silinemedi: {path}", ex, nameof(AutoUpdateService));
            }
        }

        private static Version ParseSemVer(string tag)
        {
            if (Version.TryParse(tag, out var v))
            {
                return v;
            }

            // Fallback: X.Y.Z
            var parts = tag.Split('.');
            int major = parts.Length > 0 && int.TryParse(parts[0], out int mj) ? mj : 0;
            int minor = parts.Length > 1 && int.TryParse(parts[1], out int mn) ? mn : 0;
            int build = parts.Length > 2 && int.TryParse(parts[2], out int bd) ? bd : 0;

            return new Version(major, minor, build);
        }

        public static async Task<UpdateInfo?> CheckForUpdatesViaWebRedirectAsync()
        {
            try
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                string webUrl = "https://github.com/Eyupbayuk31/Bakim/releases/latest";
                var response = await client.GetAsync(webUrl);

                string? location = response.Headers.Location?.ToString();
                if (string.IsNullOrEmpty(location) && response.Headers.TryGetValues("Location", out var values))
                {
                    location = System.Linq.Enumerable.FirstOrDefault(values);
                }

                if (!string.IsNullOrEmpty(location))
                {
                    // Örnek: "https://github.com/Eyupbayuk31/Bakim/releases/tag/v2.7.0"
                    string rawTag = location.Substring(location.LastIndexOf('/') + 1);
                    string cleanTagName = rawTag.TrimStart('v', 'V', ' ');

                    Version currentVersion = GetCurrentVersion();
                    Version latestVersion = ParseSemVer(cleanTagName);

                    string downloadUrl = $"https://github.com/Eyupbayuk31/Bakim/releases/download/{rawTag}/Bakim-{rawTag}-Setup.exe";

                    return new UpdateInfo
                    {
                        IsUpdateAvailable = latestVersion > currentVersion,
                        CurrentVersion = currentVersion.ToString(3),
                        LatestVersion = cleanTagName,
                        ReleaseNotes = "• En son sürüm performans ve güvenlik güncellemeleri içerir.",
                        DownloadUrl = downloadUrl,
                        FileSizeBytes = 0,
                        ReleasePageUrl = location
                    };
                }
            }
            catch
            {
                // Fallback hatası yutulur
            }

            return null;
        }
    }
}
