using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bakım.Models;
using Bakım.Services.Sentinel.Actions;

namespace Bakım.Services.Sentinel.Storage
{
    public interface ISessionStore
    {
        string StorageDirectory { get; }
        Task<bool> SaveReportAsync(SetupDeltaReport report);
        Task<List<SetupDeltaReport>> LoadAllReportsAsync();
        Task<RollbackResult> RollbackReportAsync(SetupDeltaReport report);
        bool DeleteReport(string sessionId);
    }

    /// <summary>
    /// Kurulum raporlarının ve delta kayıtlarının disk üzerindeki saklama,
    /// yükleme ve güvenli geri alma işlemlerini yöneten tekil merkezi depo.
    /// </summary>
    public class SessionStore : ISessionStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public string StorageDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Bakım", "InstallationLogs");

        public SessionStore()
        {
            try
            {
                if (!Directory.Exists(StorageDirectory))
                {
                    Directory.CreateDirectory(StorageDirectory);
                }
            }
            catch { }
        }

        public async Task<bool> SaveReportAsync(SetupDeltaReport report)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(StorageDirectory))
                    {
                        Directory.CreateDirectory(StorageDirectory);
                    }

                    string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(report.AppName) ? "Kurulum" : report.AppName);
                    string fileName = $"{safeName}_{report.InstallTime:yyyyMMdd_HHmmss}_{report.SessionId}.json";
                    string filePath = Path.Combine(StorageDirectory, fileName);

                    string json = JsonSerializer.Serialize(report, JsonOptions);
                    File.WriteAllText(filePath, json);
                    report.IsProfileSaved = true;
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<List<SetupDeltaReport>> LoadAllReportsAsync()
        {
            return await Task.Run(() =>
            {
                var reports = new List<SetupDeltaReport>();
                if (!Directory.Exists(StorageDirectory)) return reports;

                var files = Directory.GetFiles(StorageDirectory, "*.json")
                    .OrderByDescending(File.GetCreationTimeUtc);

                foreach (var file in files)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var report = JsonSerializer.Deserialize<SetupDeltaReport>(json, JsonOptions);
                        if (report != null && !string.IsNullOrWhiteSpace(report.SessionId))
                        {
                            reports.Add(report);
                        }
                    }
                    catch
                    {
                        // Bozuk veya uyumsuz eski şema dosyalarını sessizce atla
                    }
                }

                return reports;
            });
        }

        public async Task<RollbackResult> RollbackReportAsync(SetupDeltaReport report)
        {
            return await RollbackPlanner.ExecuteSafeRollbackAsync(report);
        }

        public bool DeleteReport(string sessionId)
        {
            try
            {
                if (!Directory.Exists(StorageDirectory)) return false;

                var file = Directory.GetFiles(StorageDirectory, $"*{sessionId}*.json").FirstOrDefault();
                if (file != null && File.Exists(file))
                {
                    File.Delete(file);
                    return true;
                }
            }
            catch { }

            return false;
        }

        public static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "Unknown" : cleaned;
        }
    }
}
