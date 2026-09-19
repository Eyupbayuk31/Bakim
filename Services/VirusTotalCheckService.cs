using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Bakım.Services
{
    public class VirusTotalCheckService : IVirusTotalCheckService
    {
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, (int malicious, int total, string message)> _cache = new();

        public string ApiKey { get; set; } = string.Empty;
        public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

        public VirusTotalCheckService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            LoadApiKey();
        }

        public string LoadApiKey()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string path = Path.Combine(appData, "Bakim", "appsettings.json");
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("VirusTotalApiKey", out var prop))
                    {
                        ApiKey = prop.GetString() ?? string.Empty;
                        return ApiKey;
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        public void SaveApiKey(string apiKey)
        {
            ApiKey = apiKey?.Trim() ?? string.Empty;
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appDir = Path.Combine(appData, "Bakim");
                if (!Directory.Exists(appDir)) Directory.CreateDirectory(appDir);
                string path = Path.Combine(appDir, "appsettings.json");

                var dict = new Dictionary<string, object>();
                if (File.Exists(path))
                {
                    try
                    {
                        string existingJson = File.ReadAllText(path);
                        dict = JsonSerializer.Deserialize<Dictionary<string, object>>(existingJson) ?? new Dictionary<string, object>();
                    }
                    catch { }
                }
                dict["VirusTotalApiKey"] = ApiKey;
                File.WriteAllText(path, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public async Task<bool> ValidateApiKeyAsync(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return false;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.virustotal.com/api/v3/files/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
                request.Headers.Add("x-apikey", apiKey.Trim());
                using var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public string ComputeSha256(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return string.Empty;

            try
            {
                using var sha256 = SHA256.Create();
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                byte[] hashBytes = sha256.ComputeHash(stream);
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
            catch
            {
                return string.Empty;
            }
        }

        public async Task<(int malicious, int total, string message)> CheckHashAsync(string sha256Hash, string? apiKey = null)
        {
            if (string.IsNullOrWhiteSpace(sha256Hash))
                return (-1, -1, "Geçersiz Hash");

            if (_cache.TryGetValue(sha256Hash, out var cachedResult))
                return cachedResult;

            string key = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : ApiKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                key = LoadApiKey();
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                return (-1, -1, "API Anahtarı Gerekli");
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.virustotal.com/api/v3/files/{sha256Hash}");
                request.Headers.Add("x-apikey", key.Trim());

                using var response = await _httpClient.SendAsync(request);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    var notFound = (0, 0, "VT Kaydı Yok");
                    _cache[sha256Hash] = notFound;
                    return notFound;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return (-1, -1, $"VT HTTP {(int)response.StatusCode}");
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("attributes", out var attrs) &&
                    attrs.TryGetProperty("last_analysis_stats", out var stats))
                {
                    int malicious = stats.GetProperty("malicious").GetInt32();
                    int suspicious = stats.GetProperty("suspicious").GetInt32();
                    int harmless = stats.GetProperty("harmless").GetInt32();
                    int undetected = stats.GetProperty("undetected").GetInt32();
                    int total = malicious + suspicious + harmless + undetected;

                    string msg = malicious > 0 ? $"{malicious}/{total} Zararlı!" : $"{harmless}/{total} Temiz";
                    var result = (malicious, total, msg);
                    _cache[sha256Hash] = result;
                    return result;
                }
            }
            catch (Exception ex)
            {
                return (-1, -1, $"Hata: {ex.Message}");
            }

            return (-1, -1, "Bilinmiyor");
        }

        public void OpenInBrowser(string sha256Hash)
        {
            if (string.IsNullOrWhiteSpace(sha256Hash)) return;

            try
            {
                string url = $"https://www.virustotal.com/gui/file/{sha256Hash}";
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
