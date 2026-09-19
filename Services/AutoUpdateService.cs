using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace Bakım.Services
{
    public class UpdateInfo
    {
        public bool IsUpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = "2.5.0";
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
        public const string DefaultCurrentVersion = "2.5.1";

        public static Version GetCurrentVersion()
        {
            var asmVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (asmVersion != null && asmVersion > new Version(0, 0, 0))
            {
                return asmVersion;
            }
            return new Version(2, 5, 1);
        }

        public static async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.Add("User-Agent", "Bakim-App-AutoUpdater (Eyupbayuk31/Bakim)");
                client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

                var response = await client.GetAsync(GitHubApiUrl);

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
                string body = root.TryGetProperty("body", out var b) ? (b.GetString() ?? "Değişiklik notu belirtilmedi.") : "Değişiklik notu belirtilmedi.";
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
                return new UpdateInfo
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = GetCurrentVersion().ToString(3),
                    ErrorMessage = $"Güncelleme sorgusu başarısız: {ex.Message}"
                };
            }
        }

        public static async Task DownloadAndExecuteInstallerAsync(string downloadUrl, Action<int>? onProgressChanged = null)
        {
            string tempInstallerPath = Path.Combine(Path.GetTempPath(), "Bakim_Setup_Update.exe");

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

            // Inno Setup Sessiz Kurulum Parametreleri ile Başlat ve Uygulamayı Kapat
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

            Process.Start(startInfo);

            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
            }
            else
            {
                Environment.Exit(0);
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
    }
}
