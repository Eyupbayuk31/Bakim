using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Bakım.Models;
using Bakım.Services.Sentinel.Actions;
using Bakım.Services.Sentinel.Detection;
using Bakım.Services.Sentinel.Sensors;
using Bakım.Services.Sentinel.Storage;
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
                SchemaVersion = 2,
                Kind = SessionKind.Install,
                AppName = "TestApp",
                InstallerPath = @"C:\Downloads\TestApp_Setup.exe",
                InstallTime = DateTime.UtcNow,
                Duration = TimeSpan.FromSeconds(45),
                CreatedFiles = new List<string> { @"C:\Program Files\TestApp\app.exe", @"C:\Program Files\TestApp\core.dll" },
                ModifiedFiles = new List<string> { @"C:\Program Files\Common\shared.dll" },
                DeletedFiles = new List<string> { @"C:\Temp\installer_temp.tmp" },
                AddedFiles = new List<string> { @"C:\Program Files\TestApp\app.exe", @"C:\Program Files\TestApp\core.dll" },
                AddedExecutables = new List<string> { @"C:\Program Files\TestApp\app.exe", @"C:\Program Files\TestApp\core.dll" },
                AddedFolders = new List<string> { @"C:\Program Files\TestApp" },
                AddedServices = new List<string> { @"HKLM\SYSTEM\CurrentControlSet\Services\TestAppService" },
                AddedStartupEntries = new List<string> { @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run\TestApp" },
                TotalSizeBytes = 1024 * 1024 * 25,
                FormattedSize = "25.00 MB",
                QuickRiskSummary = "2 yürütülebilir • 1 başlangıç kaydı • 1 yeni servis",
                IsProfileSaved = true,
                ThreatScanRequested = true
            };

            report.AddedRegistryRecords.Add(new SetupRegistryRecord
            {
                Hive = "HKLM",
                KeyPath = @"HKLM\Software\TestApp",
                ValueName = "InstallDir",
                ValueData = @"C:\Program Files\TestApp",
                ChangeKind = "ValueAdded",
                IsAutorunOrService = false
            });

            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            Assert.False(string.IsNullOrWhiteSpace(json));

            var deserialized = JsonSerializer.Deserialize<SetupDeltaReport>(json);
            Assert.NotNull(deserialized);
            Assert.Equal(2, deserialized.SchemaVersion);
            Assert.Equal(SessionKind.Install, deserialized.Kind);
            Assert.Equal(report.AppName, deserialized.AppName);
            Assert.Equal(2, deserialized.CreatedFiles.Count);
            Assert.Single(deserialized.ModifiedFiles);
            Assert.Single(deserialized.DeletedFiles);
            Assert.Equal(2, deserialized.AddedExecutables.Count);
            Assert.Single(deserialized.AddedFolders);
            Assert.Single(deserialized.AddedServices);
            Assert.Single(deserialized.AddedStartupEntries);
            Assert.Single(deserialized.AddedRegistryRecords);
            Assert.Equal("25.00 MB", deserialized.FormattedSize);
            Assert.Equal(report.QuickRiskSummary, deserialized.QuickRiskSummary);
        }

        [Theory]
        [InlineData("setup.exe", @"C:\Downloads\setup.exe", true)]
        [InlineData("installer.exe", @"C:\Users\User\Downloads\installer.exe", true)]
        [InlineData("vcredist_x64.exe", @"C:\Downloads\vcredist_x64.exe", true)]
        [InlineData("dxsetup.exe", @"C:\Downloads\dxsetup.exe", true)]
        [InlineData("program_kurulum.exe", @"C:\Users\User\Desktop\program_kurulum.exe", true)]
        [InlineData("msiexec.exe", @"C:\Windows\System32\msiexec.exe", true)]
        [InlineData("notepad.exe", @"C:\Windows\notepad.exe", false)]
        [InlineData("explorer.exe", @"C:\Windows\explorer.exe", false)]
        [InlineData("taskmgr.exe", @"C:\Windows\System32\taskmgr.exe", false)]
        public void InstallerClassifier_RealClass_MatchesInstallersCorrectly(string processName, string path, bool expectedInstaller)
        {
            bool isMatch = InstallerClassifier.ClassifyProcess(
                processName,
                path,
                null,
                null,
                null,
                null,
                out SessionKind kind,
                out _,
                out int score);

            if (expectedInstaller)
            {
                Assert.True(isMatch, $"Beklenen kurulum yakalanamadı: {processName}");
                Assert.Equal(SessionKind.Install, kind);
                Assert.True(score >= 50);
            }
            else
            {
                Assert.False(isMatch, $"Beklenmeyen süreç kurulum olarak işaretlendi: {processName}");
            }
        }

        [Theory]
        [InlineData("unins000.exe", @"C:\Program Files\TestApp\unins000.exe")]
        [InlineData("uninstall.exe", @"C:\Program Files\TestApp\uninstall.exe")]
        [InlineData("uninstaller.exe", @"C:\Program Files\TestApp\uninstaller.exe")]
        public void InstallerClassifier_IdentifiesUninstallersCorrectly(string processName, string path)
        {
            bool isMatch = InstallerClassifier.ClassifyProcess(
                processName,
                path,
                null,
                null,
                null,
                null,
                out SessionKind kind,
                out string appName,
                out _);

            Assert.True(isMatch);
            Assert.Equal(SessionKind.Uninstall, kind);
        }

        [Theory]
        [InlineData("jusched.exe", @"C:\Program Files (x86)\Common Files\Java\Java Update\jusched.exe")]
        [InlineData("googleupdate.exe", @"C:\Program Files (x86)\Google\Update\googleupdate.exe")]
        [InlineData("onedrive.exe", @"C:\Users\User\AppData\Local\Microsoft\OneDrive\onedrive.exe")]
        public void InstallerClassifier_RejectsExcludedBackgroundUpdaters(string processName, string path)
        {
            bool isMatch = InstallerClassifier.ClassifyProcess(
                processName,
                path,
                "Auto Updater",
                null,
                "Auto Updater",
                null,
                out SessionKind kind,
                out _,
                out _);

            Assert.False(isMatch);
            Assert.Equal(SessionKind.Update, kind);
        }

        [Fact]
        public void InstallerClassifier_RejectsAlreadyInstalledProgramFiles()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string exePath = Path.Combine(programFiles, "SomeApp", "helper_setup.exe");

            bool isMatch = InstallerClassifier.ClassifyProcess(
                "helper_setup",
                exePath,
                null,
                null,
                null,
                null,
                out _,
                out _,
                out _);

            Assert.False(isMatch);
        }

        [Fact]
        public void RollbackPlanner_NeverDeletesPreExistingOrModifiedFiles()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "Bakim_Rollback_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string modifiedFile = Path.Combine(tempDir, "existing_config.ini");
                string createdFile = Path.Combine(tempDir, "newly_installed.exe");

                File.WriteAllText(modifiedFile, "[Config]\nVersion=1.0");
                File.WriteAllText(createdFile, "MZ_DUMMY_BINARY");

                var report = new SetupDeltaReport
                {
                    AppName = "TestRollbackApp",
                    CreatedFiles = new List<string> { createdFile },
                    ModifiedFiles = new List<string> { modifiedFile },
                    AddedFiles = new List<string> { createdFile, modifiedFile } // Geriye döküm AddedFiles listesi ikisini de içerse bile!
                };

                var result = RollbackPlanner.ExecuteSafeRollback(report);

                // DEĞİŞMEZ KURAL (P0-1): ModifiedFiles ASLA silinemez!
                Assert.True(File.Exists(modifiedFile), "HATA: Önceden var olan veya değiştirilen dosya geri almada silindi!");
                Assert.False(File.Exists(createdFile), "HATA: Yeni eklenen dosya geri almada silinmedi!");
                Assert.Equal(1, result.DeletedFilesCount);
                Assert.Equal(1, result.ProtectedModifiedFilesCount);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public void RegistryHotspotSensor_ComputesDeltaCorrectly()
        {
            var pre = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [@"HKLM\Software\Microsoft\Windows\CurrentVersion\Run\ExistingApp"] = @"C:\Existing\app.exe",
                [@"HKLM\SYSTEM\CurrentControlSet\Services\ExistingSvc"] = @"C:\Existing\svc.exe"
            };

            var post = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [@"HKLM\Software\Microsoft\Windows\CurrentVersion\Run\ExistingApp"] = @"C:\Existing\app.exe",
                [@"HKLM\Software\Microsoft\Windows\CurrentVersion\Run\NewApp"] = @"C:\New\app.exe", // Yeni Run
                [@"HKLM\SYSTEM\CurrentControlSet\Services\ExistingSvc"] = @"C:\Existing\svc.exe",
                [@"HKLM\SYSTEM\CurrentControlSet\Services\NewSvc"] = @"C:\New\svc.exe", // Yeni Service
                [@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run\UserApp"] = @"C:\User\app.exe" // Yeni HKCU Run
            };

            var (records, services, startups) = RegistryHotspotSensor.ComputeDelta(pre, post);

            Assert.Equal(3, records.Count);
            Assert.Single(services);
            Assert.Equal(2, startups.Count);
            Assert.Contains(@"HKLM\SYSTEM\CurrentControlSet\Services\NewSvc", services);
            Assert.Contains(@"HKLM\Software\Microsoft\Windows\CurrentVersion\Run\NewApp", startups);
            Assert.Contains(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run\UserApp", startups);
        }

        [Fact]
        public async Task SessionStore_SavesAndLoadsReportsCleanly()
        {
            var store = new SessionStore();
            var report = new SetupDeltaReport
            {
                SessionId = Guid.NewGuid().ToString("N"),
                AppName = "Unit_Test_Sample_App",
                InstallTime = DateTime.UtcNow,
                CreatedFiles = new List<string> { @"C:\Test\app.exe" }
            };

            bool saved = await store.SaveReportAsync(report);
            Assert.True(saved);

            var all = await store.LoadAllReportsAsync();
            Assert.NotNull(all);
            Assert.Contains(all, r => r.SessionId == report.SessionId);

            // Cleanup
            store.DeleteReport(report.SessionId);
        }
    }
}
