using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Bakım.Models;

namespace Bakım.Services
{
    public class UpdateInfo
    {
        public bool IsUpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = "3.5.0";

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
        public const string DefaultCurrentVersion = "3.6.0";

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

            // Eşzamanlı/eski indirmelerin üzerine yazmaması için benzersiz ad
            string tempInstallerPath = Path.Combine(
                Path.GetTempPath(),
                $"Bakim_Setup_Update_{Guid.NewGuid():N}.exe");

            AppLog.Info($"Güncelleme indiriliyor: {downloadUrl}", nameof(AutoUpdateService));

            using (var client = new HttpClient())
            using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long totalBytes = response.Content.Headers.ContentLength ?? -1;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(tempInstallerPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
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

            // 2) BÜTÜNLÜK DENETİMİ — paket çalıştırılmadan önce imzası ve özeti incelenir.
            var verdict = Helpers.AuthenticodeVerifier.Verify(tempInstallerPath);

            AppLog.Info(
                $"Güncelleme paketi doğrulandı — {verdict.Describe()}, SHA-256: {verdict.Sha256}",
                nameof(AutoUpdateService));

            if (verdict.Status == SignatureStatus.InvalidOrTampered)
            {
                // Bozulmuş imza kurtarılabilir bir durum değildir: kesin reddedilir.
                AppLog.Error("Güncelleme paketinin imzası geçersiz; kurulum iptal edildi.", null, nameof(AutoUpdateService));
                TryDelete(tempInstallerPath);

                ShowOnUiThread(() => MessageBox.Show(
                    "İndirilen güncelleme paketinin dijital imzası geçersiz veya dosya değiştirilmiş.\n\n" +
                    "Kurulum güvenlik gereği iptal edildi ve dosya silindi.",
                    "Güncelleme Engellendi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error));

                return;
            }

            if (!verdict.IsTrusted)
            {
                // Paket henüz kod imzalama sertifikasıyla imzalanmıyor. Sessizce çalıştırmak
                // yerine kullanıcıya özeti gösterip açık onay isteniyor.
                bool proceed = false;

                ShowOnUiThread(() =>
                {
                    var answer = MessageBox.Show(
                        "İndirilen güncelleme paketi dijital olarak imzalanmamış.\n\n" +
                        $"Kaynak: {downloadUrl}\n" +
                        $"SHA-256: {verdict.Sha256}\n\n" +
                        "Paketi yalnızca bu özet, resmî sürüm sayfasındaki değerle aynıysa çalıştırın.\n\n" +
                        "Kuruluma devam edilsin mi?",
                        "İmzasız Güncelleme Paketi",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    proceed = answer == MessageBoxResult.Yes;
                });

                if (!proceed)
                {
                    AppLog.Info("Kullanıcı imzasız güncellemeyi reddetti.", nameof(AutoUpdateService));
                    TryDelete(tempInstallerPath);
                    return;
                }

                AppLog.Warning("Kullanıcı imzasız güncelleme paketini onayladı.", null, nameof(AutoUpdateService));
            }

            // 3) Inno Setup Sessiz Kurulum Parametreleri ile Başlat ve Uygulamayı Kapat
            // /VERYSILENT: Hiçbir pencere göstermez
            // /SUPPRESSMSGBOXES: Mesaj kutusu sormaz
            // /NORESTART: Bilgisayarı yeniden başlatmaz
            // /CLOSEAPPLICATIONS: Eski açık Bakım uygulamasını arka planda kapatıp dosyaları günceller
            var startInfo = new ProcessStartInfo
            {
                FileName = tempInstallerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
                UseShellExecute = true,
                Verb = "runas"
            };

            try
            {
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                // Kullanıcı UAC istemini reddettiğinde buraya düşülür.
                AppLog.Warning("Güncelleme kurulumu başlatılamadı.", ex, nameof(AutoUpdateService));
                TryDelete(tempInstallerPath);

                ShowOnUiThread(() => MessageBox.Show(
                    $"Güncelleme kurulumu başlatılamadı: {ex.Message}",
                    "Güncelleme",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning));

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
