using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Bakım.Models;
using Bakım.Services;
using Xunit;

namespace Bakim.Tests
{
    public class SetupSentinelTests
    {
        [Fact]
        public void SetupDeltaReport_Serialization_PreservesAllFields()
        {
            var report = new SetupDeltaReport
            {
                AppName = "TestApp",
                InstallerPath = @"C:\Downloads\TestApp_Setup.exe",
                InstallTime = DateTime.UtcNow,
                Duration = TimeSpan.FromSeconds(45),
                AddedFiles = new List<string> { @"C:\Program Files\TestApp\app.exe", @"C:\Program Files\TestApp\core.dll" },
                AddedExecutables = new List<string> { @"C:\Program Files\TestApp\app.exe", @"C:\Program Files\TestApp\core.dll" },
                AddedFolders = new List<string> { @"C:\Program Files\TestApp" },
                AddedServices = new List<string> { "TestAppService" },
                AddedStartupEntries = new List<string> { @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run\TestApp" },
                TotalSizeBytes = 1024 * 1024 * 25, // 25 MB
                FormattedSize = "25.00 MB",
                IsProfileSaved = true,
                ThreatScanRequested = true
            };

            report.AddedRegistryRecords.Add(new SetupRegistryRecord
            {
                Hive = "HKLM",
                KeyPath = @"Software\TestApp",
                ValueName = "InstallDir",
                ValueData = @"C:\Program Files\TestApp",
                ChangeKind = "ValueAdded",
                IsAutorunOrService = false
            });

            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            Assert.False(string.IsNullOrWhiteSpace(json));

            var deserialized = JsonSerializer.Deserialize<SetupDeltaReport>(json);
            Assert.NotNull(deserialized);
            Assert.Equal(report.AppName, deserialized.AppName);
            Assert.Equal(report.InstallerPath, deserialized.InstallerPath);
            Assert.Equal(2, deserialized.AddedFiles.Count);
            Assert.Equal(2, deserialized.AddedExecutables.Count);
            Assert.Single(deserialized.AddedFolders);
            Assert.Single(deserialized.AddedServices);
            Assert.Single(deserialized.AddedStartupEntries);
            Assert.Single(deserialized.AddedRegistryRecords);
            Assert.Equal("25.00 MB", deserialized.FormattedSize);
        }

        [Theory]
        [InlineData("setup.exe", true)]
        [InlineData("installer.exe", true)]
        [InlineData("vcredist_x64.exe", true)]
        [InlineData("vscode_setup.exe", true)]
        [InlineData("program_kurulum.exe", true)]
        [InlineData("msiexec.exe", true)]
        [InlineData("notepad.exe", false)]
        [InlineData("explorer.exe", false)]
        [InlineData("taskmgr.exe", false)]
        public void InstallerKeywords_MatchExpectedPatterns(string processOrFileName, bool expectedMatch)
        {
            string[] keywords = new[] { "setup", "install", "installer", "kurulum", "kurucu", "msiexec", "unins", "update", "vcredist" };
            string lower = processOrFileName.ToLowerInvariant();
            bool isMatch = Array.Exists(keywords, k => lower.Contains(k));

            Assert.Equal(expectedMatch, isMatch);
        }

        [Theory]
        [InlineData("test.exe", true)]
        [InlineData("core.dll", true)]
        [InlineData("driver.sys", true)]
        [InlineData("script.bat", true)]
        [InlineData("command.cmd", true)]
        [InlineData("payload.ps1", true)]
        [InlineData("helper.vbs", true)]
        [InlineData("package.msi", true)]
        [InlineData("readme.txt", false)]
        [InlineData("logo.png", false)]
        [InlineData("settings.json", false)]
        [InlineData("styles.css", false)]
        public void ExecutableExtensions_FilterCorrectly(string fileName, bool expectedExecutable)
        {
            var exeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".exe", ".dll", ".sys", ".bat", ".cmd", ".ps1", ".vbs", ".msi"
            };

            string ext = Path.GetExtension(fileName);
            bool isExecutable = exeExtensions.Contains(ext);

            Assert.Equal(expectedExecutable, isExecutable);
        }

        [Fact]
        public void WatchedSetupSession_TracksProcessTreeCorrectly()
        {
            var session = new WatchedSetupSession
            {
                RootProcessId = 1234,
                ProcessName = "setup",
                AppName = "Sample Software"
            };

            session.TrackedProcessIds.Add(1234);
            session.TrackedProcessIds.Add(5678); // Spawned child process (e.g. msiexec)

            Assert.Contains(1234, session.TrackedProcessIds);
            Assert.Contains(5678, session.TrackedProcessIds);
            Assert.DoesNotContain(9999, session.TrackedProcessIds);
            Assert.True(session.IsActive);
        }
    }
}
