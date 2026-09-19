using System.Threading.Tasks;

namespace Bakım.Services
{
    public class VirusTotalUploadResult
    {
        public bool Success { get; set; }
        public string? AnalysisId { get; set; }
        public string? ErrorMessage { get; set; }
        public string? StatusUrl { get; set; }
    }

    public interface IVirusTotalCheckService
    {
        string ApiKey { get; set; }
        bool HasApiKey { get; }
        string ComputeSha256(string filePath);
        Task<(int malicious, int total, string message)> CheckHashAsync(string sha256Hash, string? apiKey = null);
        Task<bool> ValidateApiKeyAsync(string apiKey);
        Task<VirusTotalUploadResult> UploadFileAsync(string filePath, IProgress<string>? progress = null);
        void OpenInBrowser(string sha256OrUrl);
        void OpenUploadPage(string filePath);
        void SmartOpenInBrowser(string filePath, string sha256Hash, int totalDetections);
        void SaveApiKey(string apiKey);
        string LoadApiKey();
    }
}
