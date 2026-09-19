using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IInstallerMonitorService
    {
        Task<InstallationSnapshot> TakePreInstallSnapshotAsync(string appName);
        Task<SnapshotDelta> TakePostInstallSnapshotAndSaveDeltaAsync(string appName, InstallationSnapshot preSnapshot);
        List<SnapshotDelta> GetRecordedInstallations();
        Task<int> RevertInstallationDeltaAsync(string appName);
    }

    public class InstallerMonitorService : IInstallerMonitorService
    {
        private static readonly string StorageDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Bakım", "InstallationLogs");

        public InstallerMonitorService()
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

        #region Snapshot Engine

        public async Task<InstallationSnapshot> TakePreInstallSnapshotAsync(string appName)
        {
            return await Task.Run(() =>
            {
                var snapshot = new InstallationSnapshot
                {
                    AppName = appName,
                    SnapshotTime = DateTime.UtcNow
                };

                // Registry Snapshot
                CaptureRegistryKeys(RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", snapshot.RegistryKeys);
                CaptureRegistryKeys(RegistryHive.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", snapshot.RegistryKeys);
                CaptureRegistryKeys(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", snapshot.RegistryKeys);
                CaptureRegistryKeys(RegistryHive.LocalMachine, @"Software", snapshot.RegistryKeys, maxDepth: 2);
                CaptureRegistryKeys(RegistryHive.CurrentUser, @"Software", snapshot.RegistryKeys, maxDepth: 2);

                // File System Top-level Snapshot
                CaptureDirectoryTopLevel(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), snapshot.FilePaths);
                CaptureDirectoryTopLevel(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), snapshot.FilePaths);
                CaptureDirectoryTopLevel(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), snapshot.FilePaths);
                CaptureDirectoryTopLevel(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), snapshot.FilePaths);
                CaptureDirectoryTopLevel(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), snapshot.FilePaths);

                return snapshot;
            });
        }

        public async Task<SnapshotDelta> TakePostInstallSnapshotAndSaveDeltaAsync(string appName, InstallationSnapshot preSnapshot)
        {
            return await Task.Run(() =>
            {
                var currentSnapshot = TakePreInstallSnapshotAsync(appName).GetAwaiter().GetResult();

                var delta = new SnapshotDelta
                {
                    AppName = appName,
                    CreatedAt = DateTime.UtcNow
                };

                // Added Registry Keys
                foreach (var reg in currentSnapshot.RegistryKeys)
                {
                    if (!preSnapshot.RegistryKeys.Contains(reg))
                    {
                        delta.AddedRegistryKeys.Add(reg);
                    }
                }

                // Added Files & Folders
                long totalBytes = 0;
                foreach (var path in currentSnapshot.FilePaths)
                {
                    if (!preSnapshot.FilePaths.Contains(path))
                    {
                        if (Directory.Exists(path))
                        {
                            delta.AddedFolders.Add(path);
                            totalBytes += CalculateFolderSizeSafe(path);
                        }
                        else if (File.Exists(path))
                        {
                            delta.AddedFiles.Add(path);
                            try { totalBytes += new FileInfo(path).Length; } catch { }
                        }
                    }
                }

                delta.TotalSizeBytes = totalBytes;
                delta.FormattedSize = FormatBytes(totalBytes);

                // Save to JSON
                SaveDeltaToDisk(delta);

                return delta;
            });
        }

        #endregion

        #region Persistence & Revert

        public List<SnapshotDelta> GetRecordedInstallations()
        {
            var list = new List<SnapshotDelta>();
            try
            {
                if (!Directory.Exists(StorageDirectory)) return list;

                foreach (var file in Directory.GetFiles(StorageDirectory, "*.json"))
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var delta = JsonSerializer.Deserialize<SnapshotDelta>(json);
                        if (delta != null) list.Add(delta);
                    }
                    catch { }
                }
            }
            catch { }

            return list;
        }

        public async Task<int> RevertInstallationDeltaAsync(string appName)
        {
            return await Task.Run(() =>
            {
                string filePath = Path.Combine(StorageDirectory, $"{SanitizeFileName(appName)}.json");
                if (!File.Exists(filePath)) return 0;

                int cleanedCount = 0;
                try
                {
                    string json = File.ReadAllText(filePath);
                    var delta = JsonSerializer.Deserialize<SnapshotDelta>(json);
                    if (delta == null) return 0;

                    // Delete added files
                    foreach (var f in delta.AddedFiles)
                    {
                        try
                        {
                            if (File.Exists(f))
                            {
                                File.SetAttributes(f, FileAttributes.Normal);
                                File.Delete(f);
                                cleanedCount++;
                            }
                        }
                        catch { }
                    }

                    // Delete added folders
                    foreach (var d in delta.AddedFolders)
                    {
                        try
                        {
                            if (Directory.Exists(d))
                            {
                                Directory.Delete(d, true);
                                cleanedCount++;
                            }
                        }
                        catch { }
                    }

                    // Delete added registry keys
                    foreach (var r in delta.AddedRegistryKeys)
                    {
                        try
                        {
                            if (DeleteRegistryKey(r))
                            {
                                cleanedCount++;
                            }
                        }
                        catch { }
                    }

                    // Remove delta file
                    File.Delete(filePath);
                }
                catch { }

                return cleanedCount;
            });
        }

        private void SaveDeltaToDisk(SnapshotDelta delta)
        {
            try
            {
                if (!Directory.Exists(StorageDirectory)) Directory.CreateDirectory(StorageDirectory);
                string path = Path.Combine(StorageDirectory, $"{SanitizeFileName(delta.AppName)}.json");
                string json = JsonSerializer.Serialize(delta, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
        }

        #endregion

        #region Helper Capture Methods

        private void CaptureRegistryKeys(RegistryHive hive, string subPath, HashSet<string> keys, int maxDepth = 4, int currentDepth = 1)
        {
            if (currentDepth > maxDepth) return;

            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(subPath);
                if (key == null) return;

                foreach (string subName in key.GetSubKeyNames())
                {
                    string fullKey = $@"{hive}\{subPath}\{subName}";
                    keys.Add(fullKey);

                    if (currentDepth < maxDepth)
                    {
                        CaptureRegistryKeys(hive, $@"{subPath}\{subName}", keys, maxDepth, currentDepth + 1);
                    }
                }
            }
            catch { }
        }

        private void CaptureDirectoryTopLevel(string dirPath, HashSet<string> paths)
        {
            try
            {
                if (!Directory.Exists(dirPath)) return;

                var dir = new DirectoryInfo(dirPath);
                foreach (var sub in dir.GetDirectories())
                {
                    paths.Add(sub.FullName);
                }
                foreach (var file in dir.GetFiles())
                {
                    paths.Add(file.FullName);
                }
            }
            catch { }
        }

        private bool DeleteRegistryKey(string fullPath)
        {
            try
            {
                int slashIndex = fullPath.IndexOf('\\');
                if (slashIndex <= 0) return false;

                string hiveStr = fullPath.Substring(0, slashIndex);
                string subPath = fullPath.Substring(slashIndex + 1);

                RegistryHive hive = hiveStr.Contains("Current", StringComparison.OrdinalIgnoreCase)
                    ? RegistryHive.CurrentUser
                    : RegistryHive.LocalMachine;

                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                baseKey.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static long CalculateFolderSizeSafe(string folderPath)
        {
            try
            {
                var di = new DirectoryInfo(folderPath);
                return di.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
            }
            catch
            {
                return 0;
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }

        #endregion
    }
}
