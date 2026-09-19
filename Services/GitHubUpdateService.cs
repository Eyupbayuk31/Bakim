using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Bakım.Services
{
    public class GitHubReleaseInfo
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("published_at")]
        public DateTime? PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public GitHubReleaseAsset[]? Assets { get; set; }
    }

    public class GitHubReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    public class UpdateCheckResult
    {
        public bool Success { get; set; }
        public bool IsUpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = "2.5.0";
        public string LatestVersion { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Changelog { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public string? DownloadUrl { get; set; }
        public long FileSizeBytes { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public interface IGitHubUpdateService
    {
        Task<UpdateCheckResult> CheckForUpdatesAsync();
        string GetCurrentVersion();
    }

    public class GitHubUpdateService : IGitHubUpdateService
    {
        private const string RepositoryOwner = "Eyupbayuk31";
        private const string RepositoryName = "Bakim";
        private const string ReleasesApiUrl = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";
        public const string CurrentAppVersion = "2.5.0";

        private static readonly HttpClient HttpClient = new HttpClient();

        static GitHubUpdateService()
        {
            HttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Bakim-SystemOptimizer", "2.5.0"));
            HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            HttpClient.Timeout = TimeSpan.FromSeconds(15);
        }

        public string GetCurrentVersion() => CurrentAppVersion;

        public async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            try
            {
                using var response = await HttpClient.GetAsync(ReleasesApiUrl);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new UpdateCheckResult
                    {
                        Success = true,
                        IsUpdateAvailable = false,
                        CurrentVersion = CurrentAppVersion,
                        LatestVersion = CurrentAppVersion,
                        ErrorMessage = "GitHub üzerinde henüz yayınlanmış genel bir sürüm (Release) bulunmuyor."
                    };
                }

                if (!response.IsSuccessStatusCode)
                {
                    return new UpdateCheckResult
                    {
                        Success = false,
                        CurrentVersion = CurrentAppVersion,
                        ErrorMessage = $"GitHub API Hatası: {response.StatusCode} ({(int)response.StatusCode})"
                    };
                }

                var json = await response.Content.ReadAsStringAsync();
                var release = JsonSerializer.Deserialize<GitHubReleaseInfo>(json);

                if (release == null || string.IsNullOrWhiteSpace(release.TagName))
                {
                    return new UpdateCheckResult
                    {
                        Success = false,
                        CurrentVersion = CurrentAppVersion,
                        ErrorMessage = "Sürüm bilgileri ayrıştırılamadı."
                    };
                }

                string cleanLatestTag = release.TagName.TrimStart('v', 'V', ' ');
                bool isNewer = CompareVersions(cleanLatestTag, CurrentAppVersion) > 0;

                string? directDownloadUrl = null;
                long assetSize = 0;

                if (release.Assets != null && release.Assets.Length > 0)
                {
                    foreach (var asset in release.Assets)
                    {
                        if (asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                            asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            directDownloadUrl = asset.BrowserDownloadUrl;
                            assetSize = asset.Size;
                            break;
                        }
                    }
                }

                return new UpdateCheckResult
                {
                    Success = true,
                    IsUpdateAvailable = isNewer,
                    CurrentVersion = CurrentAppVersion,
                    LatestVersion = cleanLatestTag,
                    Title = string.IsNullOrWhiteSpace(release.Name) ? $"Bakım v{cleanLatestTag}" : release.Name,
                    Changelog = release.Body ?? "Bu sürüm için detaylı değişiklik notu girilmedi.",
                    ReleaseUrl = release.HtmlUrl ?? $"https://github.com/{RepositoryOwner}/{RepositoryName}/releases",
                    DownloadUrl = directDownloadUrl,
                    FileSizeBytes = assetSize
                };
            }
            catch (HttpRequestException ex)
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    CurrentVersion = CurrentAppVersion,
                    ErrorMessage = $"İnternet bağlantısı kurulamadı: {ex.Message}"
                };
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    CurrentVersion = CurrentAppVersion,
                    ErrorMessage = $"Güncelleme kontrolü sırasında beklenmeyen hata: {ex.Message}"
                };
            }
        }

        private static int CompareVersions(string versionA, string versionB)
        {
            if (Version.TryParse(versionA, out var vA) && Version.TryParse(versionB, out var vB))
            {
                return vA.CompareTo(vB);
            }

            // Fallback: segment comparison
            var partsA = versionA.Split('.');
            var partsB = versionB.Split('.');
            int maxLen = Math.Max(partsA.Length, partsB.Length);

            for (int i = 0; i < maxLen; i++)
            {
                int valA = i < partsA.Length && int.TryParse(partsA[i], out int pA) ? pA : 0;
                int valB = i < partsB.Length && int.TryParse(partsB[i], out int pB) ? pB : 0;

                if (valA != valB)
                    return valA.CompareTo(valB);
            }

            return 0;
        }
    }
}
