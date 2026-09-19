using System.Threading.Tasks;

namespace Bakım.Services
{
    public interface IVirusTotalCheckService
    {
        string ComputeSha256(string filePath);
        Task<(int malicious, int total, string message)> CheckHashAsync(string sha256Hash, string? apiKey = null);
        void OpenInBrowser(string sha256Hash);
    }
}
