using System.Threading.Tasks;

namespace Bakım.Services
{
    public interface IVirusTotalCheckService
    {
        string ApiKey { get; set; }
        bool HasApiKey { get; }
        string ComputeSha256(string filePath);
        Task<(int malicious, int total, string message)> CheckHashAsync(string sha256Hash, string? apiKey = null);
        Task<bool> ValidateApiKeyAsync(string apiKey);
        void OpenInBrowser(string sha256Hash);
        void SaveApiKey(string apiKey);
        string LoadApiKey();
    }
}
