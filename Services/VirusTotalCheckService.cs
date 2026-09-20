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

        public VirusTotalCheckService() : this(null) { }

        public VirusTotalCheckService(IAppSettingsService? settingsService)
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            _settings = settingsService
                        ?? App.TryGetService<IAppSettingsService>()
                        ?? new AppSettingsService(AppLog.Current);

            LoadApiKey();
        }

        private readonly IAppSettingsService _settings;

        /// <summary>
        /// Ayar nesnesinden kullanılabilir API anahtarını çözer.
        /// Önce DPAPI ile korunan alan, yoksa eski düz metin alan okunur.
        /// </summary>
        public static string ResolveApiKey(Models.AppSettingsData data)
        {
            if (data == null) return string.Empty;

            if (!string.IsNullOrWhiteSpace(data.VirusTotalApiKeyProtected))
            {
                string unprotected = Helpers.DataProtection.Unprotect(data.VirusTotalApiKeyProtected);
                if (!string.IsNullOrWhiteSpace(unprotected)) return unprotected;
            }

            return data.VirusTotalApiKey ?? string.Empty;
        }

        public string LoadApiKey()
        {
            try
            {
                var data = _settings.Current;
                ApiKey = ResolveApiKey(data);

                // Tek seferlik göç: düz metin anahtar bulunduysa şifreleyip düz metni sil.
                if (!string.IsNullOrWhiteSpace(data.VirusTotalApiKey))
                {
                    AppLog.Info("VirusTotal API anahtarı DPAPI ile şifrelenmiş depolamaya taşınıyor.", nameof(VirusTotalCheckService));
                    SaveApiKey(ApiKey);
                }

                return ApiKey;
            }
            catch (Exception ex)
            {
                AppLog.Error("VirusTotal API anahtarı okunamadı.", ex, nameof(VirusTotalCheckService));
                return string.Empty;
            }
        }

        public void SaveApiKey(string apiKey)
        {
            ApiKey = apiKey?.Trim() ?? string.Empty;

            try
            {
                string protectedKey = Helpers.DataProtection.Protect(ApiKey);

                _settings.Update(s =>
                {
                    s.VirusTotalApiKeyProtected = protectedKey;
                    // Eski düz metin alanı her koşulda temizlenir.
                    s.VirusTotalApiKey = string.Empty;
                });

                AppLog.Info("VirusTotal API anahtarı güvenli şekilde kaydedildi.", nameof(VirusTotalCheckService));
            }
            catch (Exception ex)
            {
                AppLog.Error("VirusTotal API anahtarı kaydedilemedi.", ex, nameof(VirusTotalCheckService));
            }
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

        public void OpenInBrowser(string sha256OrUrl)
        {
            if (string.IsNullOrWhiteSpace(sha256OrUrl)) return;

            try
            {
                string url = sha256OrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? sha256OrUrl
                    : $"https://www.virustotal.com/gui/file/{sha256OrUrl}";

                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        public void OpenUploadPage(string filePath)
        {
            try
            {
                // 1. Copy path to clipboard
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    try { System.Windows.Clipboard.SetText(filePath); } catch { }

                    // 2. Highlight in Explorer for easy drag & drop
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = $"/select,\"{filePath}\"",
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    }
                }

                // 3. Open VT Upload Page
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.virustotal.com/gui/home/upload",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        public void SmartOpenInBrowser(string filePath, string sha256Hash, int totalDetections)
        {
            // If totalDetections == 0 specifically, we verified that there is no record on VirusTotal (404)
            if (totalDetections == 0)
            {
                OpenUploadPage(filePath);
            }
            else
            {
                OpenInBrowser(sha256Hash);
            }
        }

        public async Task<VirusTotalUploadResult> UploadFileAsync(string filePath, IProgress<string>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return new VirusTotalUploadResult { Success = false, ErrorMessage = "Dosya bulunamadı." };
            }

            var fi = new FileInfo(filePath);
            if (fi.Length > 32 * 1024 * 1024)
            {
                return new VirusTotalUploadResult
                {
                    Success = false,
                    ErrorMessage = "Dosya boyutu 32 MB'tan büyük. Lütfen web arayüzünden yükleyin."
                };
            }

            string key = !string.IsNullOrWhiteSpace(ApiKey) ? ApiKey : LoadApiKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                return new VirusTotalUploadResult
                {
                    Success = false,
                    ErrorMessage = "VirusTotal API anahtarı tanımlanmamış."
                };
            }

            try
            {
                progress?.Report("Dosya VirusTotal sunucularına yükleniyor...");

                using var content = new MultipartFormDataContent();
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var streamContent = new StreamContent(fileStream);
                streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                content.Add(streamContent, "file", Path.GetFileName(filePath));

                using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.virustotal.com/api/v3/files");
                request.Headers.Add("x-apikey", key.Trim());
                request.Content = content;

                using var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    return new VirusTotalUploadResult
                    {
                        Success = false,
                        ErrorMessage = $"Yükleme başarısız oldu (HTTP {(int)response.StatusCode})"
                    };
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("id", out var idProp))
                {
                    string analysisId = idProp.GetString() ?? string.Empty;
                    string statusUrl = $"https://www.virustotal.com/gui/file-analysis/{analysisId}";

                    progress?.Report("Dosya başarıyla yüklendi! Canlı analiz başlatılıyor...");

                    return new VirusTotalUploadResult
                    {
                        Success = true,
                        AnalysisId = analysisId,
                        StatusUrl = statusUrl
                    };
                }

                return new VirusTotalUploadResult
                {
                    Success = false,
                    ErrorMessage = "Analiz yanıtı çözümlenemedi."
                };
            }
            catch (Exception ex)
            {
                return new VirusTotalUploadResult
                {
                    Success = false,
                    ErrorMessage = $"Hata: {ex.Message}"
                };
            }
        }
    }
}
