using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public class AutorunsScannerEngine : IAutorunsScannerEngine
    {
        private readonly IVirusTotalCheckService _virusTotalService;

        #region Win32 WinTrust P/Invoke

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class WINTRUST_FILE_INFO : IDisposable
        {
            public uint cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO));
            public IntPtr pcwszFilePath;
            public IntPtr hFile = IntPtr.Zero;
            public IntPtr pgKnownSubject = IntPtr.Zero;

            public WINTRUST_FILE_INFO(string filePath)
            {
                pcwszFilePath = Marshal.StringToCoTaskMemUni(filePath);
            }

            public void Dispose()
            {
                if (pcwszFilePath != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pcwszFilePath);
                    pcwszFilePath = IntPtr.Zero;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class WINTRUST_DATA : IDisposable
        {
            public uint cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
            public IntPtr pPolicyCallbackData = IntPtr.Zero;
            public IntPtr pSIPClientData = IntPtr.Zero;
            public uint dwUIChoice = 2; // WTD_UI_NONE
            public uint fdwRevocationChecks = 0;
            public uint dwUnionChoice = 1; // WTD_CHOICE_FILE
            public IntPtr pFile;
            public uint dwStateAction = 0;
            public IntPtr hWVTStateData = IntPtr.Zero;
            public IntPtr pwszURLReference = IntPtr.Zero;
            public uint dwProvFlags = 0x00000040 | 0x00000010;
            public uint dwUIContext = 0;
            public IntPtr pSignatureSettings = IntPtr.Zero;

            public WINTRUST_DATA(WINTRUST_FILE_INFO fileInfo)
            {
                pFile = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
                Marshal.StructureToPtr(fileInfo, pFile, false);
            }

            public void Dispose()
            {
                if (pFile != IntPtr.Zero)
                {
                    Marshal.DestroyStructure(pFile, typeof(WINTRUST_FILE_INFO));
                    Marshal.FreeCoTaskMem(pFile);
                    pFile = IntPtr.Zero;
                }
            }
        }

        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(
            IntPtr hwnd,
            [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
            WINTRUST_DATA pWVTData);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        #endregion

        public AutorunsScannerEngine(IVirusTotalCheckService virusTotalService)
        {
            _virusTotalService = virusTotalService;
        }

        public async IAsyncEnumerable<PersistenceItem> ScanAllAsync(IProgress<string>? progress = null)
        {
            // 1. Registry Run & RunOnce Keys
            progress?.Report("Kayıt Defteri Başlangıç Anahtarları taranıyor...");
            await foreach (var item in ScanRegistryRunKeysAsync())
            {
                yield return item;
            }

            // 2. Startup Folders
            progress?.Report("Başlangıç Klasörleri taranıyor...");
            await foreach (var item in ScanStartupFoldersAsync())
            {
                yield return item;
            }

            // 3. Winlogon & IFEO Hijacks
            progress?.Report("Winlogon ve IFEO Kalıcılık Noktaları taranıyor...");
            await foreach (var item in ScanWinlogonAndIfeoAsync())
            {
                yield return item;
            }

            // 4. Scheduled Tasks
            progress?.Report("Zamanlanmış Görevler taranıyor...");
            await foreach (var item in ScanScheduledTasksAsync())
            {
                yield return item;
            }

            // 5. Windows Services
            progress?.Report("Windows Servisleri taranıyor...");
            await foreach (var item in ScanServicesAsync())
            {
                yield return item;
            }

            // 6. WMI Event Consumers
            progress?.Report("WMI Olay Kalıcılıkları (WMI Event Consumers) taranıyor...");
            await foreach (var item in ScanWmiConsumersAsync())
            {
                yield return item;
            }

            // 7. Shell Context Menu Extensions
            progress?.Report("Explorer Shell Eklentileri taranıyor...");
            await foreach (var item in ScanShellExtensionsAsync())
            {
                yield return item;
            }
        }

        #region 1. Registry Run & RunOnce Scanning

        private async IAsyncEnumerable<PersistenceItem> ScanRegistryRunKeysAsync()
        {
            var targets = new (RegistryHive Hive, string SubKey, string Display)[]
            {
                (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", @"HKCU\...\Run"),
                (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", @"HKCU\...\RunOnce"),
                (RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", @"HKLM\...\Run"),
                (RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", @"HKLM\...\RunOnce"),
                (RegistryHive.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", @"HKLM\WOW6432Node\...\Run"),
                (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", @"HKCU\Policies\Explorer\Run")
            };

            foreach (var target in targets)
            {
                using var baseKey = RegistryKey.OpenBaseKey(target.Hive, RegistryView.Default);
                using var subKey = baseKey.OpenSubKey(target.SubKey);
                if (subKey != null)
                {
                    foreach (var valueName in subKey.GetValueNames())
                    {
                        string rawCmd = subKey.GetValue(valueName)?.ToString() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(rawCmd)) continue;

                        var (filePath, args) = ParseCommandAndArgs(rawCmd);
                        bool isEnabled = !valueName.StartsWith("Bakim_Disabled_");
                        string cleanName = isEnabled ? valueName : valueName.Replace("Bakim_Disabled_", "");

                        var item = CreateItem(
                            cleanName,
                            filePath,
                            args,
                            PersistenceCategory.RegistryRun,
                            "Kayıt Defteri (Run)",
                            $"{target.Display} -> {valueName}");

                        item.IsEnabled = isEnabled;
                        yield return item;
                    }
                }
            }

            await Task.CompletedTask;
        }

        #endregion

        #region 2. Startup Folders Scanning

        private async IAsyncEnumerable<PersistenceItem> ScanStartupFoldersAsync()
        {
            string userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);

            var folders = new (string Path, string Display)[]
            {
                (userStartup, "Kullanıcı Başlangıç Klasörü"),
                (commonStartup, "Ortak Başlangıç Klasörü")
            };

            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder.Path)) continue;

                string[] files;
                try
                {
                    files = Directory.GetFiles(folder.Path);
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    string fileName = Path.GetFileName(file);
                    if (string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                    string targetFile = file;
                    string args = string.Empty;
                    bool isEnabled = !fileName.EndsWith(".bakim_disabled", StringComparison.OrdinalIgnoreCase);

                    // Resolve .lnk target if shortcut
                    if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        targetFile = ResolveShortcutTarget(file) ?? file;
                    }

                    var item = CreateItem(
                        Path.GetFileNameWithoutExtension(fileName).Replace(".bakim_disabled", ""),
                        targetFile,
                        args,
                        PersistenceCategory.StartupFolder,
                        "Başlangıç Klasörü",
                        folder.Display);

                    item.IsEnabled = isEnabled;
                    yield return item;
                }
            }

            await Task.CompletedTask;
        }

        #endregion

        #region 3. Winlogon & IFEO Hijacks Scanning

        private async IAsyncEnumerable<PersistenceItem> ScanWinlogonAndIfeoAsync()
        {
            var results = new List<PersistenceItem>();

            // Winlogon (Userinit, Shell)
            try
            {
                using var winlogonKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon");
                if (winlogonKey != null)
                {
                    string[] checkValues = { "Shell", "Userinit", "Taskman" };
                    foreach (var val in checkValues)
                    {
                        string raw = winlogonKey.GetValue(val)?.ToString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            var (exePath, args) = ParseCommandAndArgs(raw);
                            results.Add(CreateItem(
                                $"Winlogon: {val}",
                                exePath,
                                args,
                                PersistenceCategory.WinlogonIfeo,
                                "Winlogon",
                                $@"HKLM\...\Winlogon\{val}"));
                        }
                    }
                }
            }
            catch { }

            // IFEO (Image File Execution Options) Debugger hijack
            try
            {
                using var ifeoKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options");
                if (ifeoKey != null)
                {
                    foreach (var subName in ifeoKey.GetSubKeyNames())
                    {
                        using var appKey = ifeoKey.OpenSubKey(subName);
                        string debugger = appKey?.GetValue("Debugger")?.ToString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(debugger))
                        {
                            var (exePath, args) = ParseCommandAndArgs(debugger);
                            results.Add(CreateItem(
                                $"IFEO Hijack: {subName}",
                                exePath,
                                args,
                                PersistenceCategory.WinlogonIfeo,
                                "IFEO Debugger",
                                $@"HKLM\...\IFEO\{subName}\Debugger"));
                        }
                    }
                }
            }
            catch { }

            foreach (var item in results)
            {
                yield return item;
            }

            await Task.CompletedTask;
        }

        #endregion

        #region 4. Scheduled Tasks Scanning

        private async IAsyncEnumerable<PersistenceItem> ScanScheduledTasksAsync()
        {
            var taskList = await Task.Run(() =>
            {
                var list = new List<PersistenceItem>();
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = "/Query /FO CSV /V",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };

                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        string output = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit(10000);

                        using var reader = new StringReader(output);
                        string? line;
                        bool headerParsed = false;
                        int colTaskName = 0;
                        int colTaskToRun = 8;
                        int colStatus = 2;

                        while ((line = reader.ReadLine()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            var parts = ParseCsvLine(line);
                            if (parts.Count < 9) continue;

                            if (!headerParsed)
                            {
                                headerParsed = true;
                                for (int i = 0; i < parts.Count; i++)
                                {
                                    string h = parts[i].ToLowerInvariant();
                                    if (h.Contains("taskname") || h.Contains("görev adı")) colTaskName = i;
                                    if (h.Contains("task to run") || h.Contains("çalıştırılacak görev")) colTaskToRun = i;
                                    if (h.Contains("status") || h.Contains("durum")) colStatus = i;
                                }
                                continue;
                            }

                            string taskName = parts.Count > colTaskName ? parts[colTaskName] : string.Empty;
                            string taskToRun = parts.Count > colTaskToRun ? parts[colTaskToRun] : string.Empty;
                            string status = parts.Count > colStatus ? parts[colStatus] : string.Empty;

                            if (string.IsNullOrWhiteSpace(taskToRun) || taskToRun.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                                continue;

                            var (exePath, args) = ParseCommandAndArgs(taskToRun);
                            if (string.IsNullOrWhiteSpace(exePath)) continue;

                            var item = CreateItem(
                                Path.GetFileName(taskName.Trim('\\')),
                                exePath,
                                args,
                                PersistenceCategory.ScheduledTask,
                                "Zamanlanmış Görev",
                                $"Görev: {taskName}");

                            item.IsEnabled = !status.Equals("Disabled", StringComparison.OrdinalIgnoreCase) &&
                                             !status.Equals("Devre Dışı", StringComparison.OrdinalIgnoreCase);

                            list.Add(item);
                        }
                    }
                }
                catch { }

                return list;
            });

            foreach (var item in taskList)
            {
                yield return item;
            }
        }

        #endregion

        #region 5. Windows Services Scanning

        private async IAsyncEnumerable<PersistenceItem> ScanServicesAsync()
        {
            var services = await Task.Run(() =>
            {
                var list = new List<PersistenceItem>();
                try
                {
                    using var rootKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
                    if (rootKey != null)
                    {
                        foreach (var serviceName in rootKey.GetSubKeyNames())
                        {
                            try
                            {
                                using var sKey = rootKey.OpenSubKey(serviceName);
                                if (sKey == null) continue;

                                int startType = (int)(sKey.GetValue("Start") ?? 3);
                                // Only Automatic (2) or Boot (0) or System (1)
                                if (startType > 2) continue;

                                string rawImagePath = sKey.GetValue("ImagePath")?.ToString() ?? string.Empty;
                                if (string.IsNullOrWhiteSpace(rawImagePath)) continue;

                                string displayName = sKey.GetValue("DisplayName")?.ToString() ?? serviceName;

                                rawImagePath = Environment.ExpandEnvironmentVariables(rawImagePath);
                                var (exePath, args) = ParseCommandAndArgs(rawImagePath);

                                var item = CreateItem(
                                    displayName,
                                    exePath,
                                    args,
                                    PersistenceCategory.WindowsService,
                                    "Windows Hizmeti",
                                    $"Hizmet: {serviceName} (Otomatik)");

                                item.IsEnabled = startType <= 2;
                                list.Add(item);
                            }
                            catch { }
                        }
                    }
                }
                catch { }

                return list;
            });

            foreach (var item in services)
            {
                yield return item;
            }
        }

        #endregion

        #region 6. WMI Event Consumers Scanning

        private async IAsyncEnumerable<PersistenceItem> ScanWmiConsumersAsync()
        {
            var wmiItems = await Task.Run(() =>
            {
                var list = new List<PersistenceItem>();
                try
                {
                    var scope = new ManagementScope(@"\\.\root\subscription");
                    scope.Connect();

                    // CommandLineEventConsumer
                    using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM CommandLineEventConsumer"));
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = obj["Name"]?.ToString() ?? "WMI Consumer";
                        string cmd = obj["CommandLineTemplate"]?.ToString() ?? string.Empty;
                        var (exePath, args) = ParseCommandAndArgs(cmd);

                        var item = CreateItem(
                            $"WMI: {name}",
                            exePath,
                            args,
                            PersistenceCategory.WmiEventConsumer,
                            "WMI Kalıcılığı",
                            "root\\subscription\\CommandLineEventConsumer");

                        list.Add(item);
                    }
                }
                catch { }

                return list;
            });

            foreach (var item in wmiItems)
            {
                yield return item;
            }
        }

        #endregion

        #region 7. Shell Context Menu Extensions Scanning

        private async IAsyncEnumerable<PersistenceItem> ScanShellExtensionsAsync()
        {
            var paths = new[]
            {
                @"*\shellex\ContextMenuHandlers",
                @"Directory\shellex\ContextMenuHandlers",
                @"Folder\shellex\ContextMenuHandlers"
            };

            foreach (var p in paths)
            {
                using var key = Registry.ClassesRoot.OpenSubKey(p);
                if (key == null) continue;

                foreach (var handlerName in key.GetSubKeyNames())
                {
                    using var hKey = key.OpenSubKey(handlerName);
                    string clsid = hKey?.GetValue(null)?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(clsid)) clsid = handlerName;

                    if (!clsid.StartsWith("{")) continue;

                    using var clsidKey = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}\InprocServer32");
                    string dllPath = clsidKey?.GetValue(null)?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(dllPath)) continue;

                    dllPath = Environment.ExpandEnvironmentVariables(dllPath);

                    yield return CreateItem(
                        handlerName,
                        dllPath,
                        string.Empty,
                        PersistenceCategory.ShellExtension,
                        "Explorer Eklentisi",
                        $@"HKCR\{p}\{handlerName}");
                }
            }

            await Task.CompletedTask;
        }

        #endregion

        #region Toggle & Delete Operations

        public async Task<bool> ToggleItemAsync(PersistenceItem item, bool enable)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (item.Category == PersistenceCategory.RegistryRun)
                    {
                        // Example: HKCU\...\Run -> valueName
                        return ToggleRegistryItem(item, enable);
                    }
                    else if (item.Category == PersistenceCategory.ScheduledTask)
                    {
                        string taskName = item.LocationSource.Replace("Görev: ", "").Trim();
                        string arg = enable ? "/Enable" : "/Disable";
                        var psi = new ProcessStartInfo
                        {
                            FileName = "schtasks.exe",
                            Arguments = $"/Change /TN \"{taskName}\" {arg}",
                            CreateNoWindow = true,
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        using var proc = Process.Start(psi);
                        proc?.WaitForExit(5000);
                        return proc?.ExitCode == 0;
                    }
                    else if (item.Category == PersistenceCategory.StartupFolder)
                    {
                        if (File.Exists(item.FilePath))
                        {
                            if (!enable && !item.FilePath.EndsWith(".bakim_disabled"))
                            {
                                string newPath = item.FilePath + ".bakim_disabled";
                                File.Move(item.FilePath, newPath);
                                item.FilePath = newPath;
                                return true;
                            }
                            else if (enable && item.FilePath.EndsWith(".bakim_disabled"))
                            {
                                string newPath = item.FilePath.Substring(0, item.FilePath.Length - ".bakim_disabled".Length);
                                File.Move(item.FilePath, newPath);
                                item.FilePath = newPath;
                                return true;
                            }
                        }
                    }
                    else if (item.Category == PersistenceCategory.WindowsService)
                    {
                        string serviceName = item.LocationSource.Replace("Hizmet: ", "").Split(' ')[0];
                        string startMode = enable ? "auto" : "disabled";
                        var psi = new ProcessStartInfo
                        {
                            FileName = "sc.exe",
                            Arguments = $"config \"{serviceName}\" start={startMode}",
                            CreateNoWindow = true,
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        using var proc = Process.Start(psi);
                        proc?.WaitForExit(5000);
                        return proc?.ExitCode == 0;
                    }
                }
                catch { }

                return false;
            });
        }

        private static bool ToggleRegistryItem(PersistenceItem item, bool enable)
        {
            try
            {
                // Parse Hive and SubKey from LocationSource
                string loc = item.LocationSource;
                int arrowIdx = loc.IndexOf("->");
                if (arrowIdx <= 0) return false;

                string pathPart = loc.Substring(0, arrowIdx).Trim();
                string valName = loc.Substring(arrowIdx + 2).Trim();

                RegistryKey? rootKey = loc.StartsWith("HKLM") ? Registry.LocalMachine : Registry.CurrentUser;
                string subPath = pathPart.Replace("HKLM\\", "").Replace("HKCU\\", "").Replace("...\\", "Software\\Microsoft\\Windows\\CurrentVersion\\");

                using var key = rootKey.OpenSubKey(subPath, writable: true);
                if (key == null) return false;

                if (!enable && !valName.StartsWith("Bakim_Disabled_"))
                {
                    object? val = key.GetValue(valName);
                    if (val != null)
                    {
                        key.SetValue($"Bakim_Disabled_{valName}", val);
                        key.DeleteValue(valName);
                        return true;
                    }
                }
                else if (enable && valName.StartsWith("Bakim_Disabled_"))
                {
                    string originalVal = valName.Replace("Bakim_Disabled_", "");
                    object? val = key.GetValue(valName);
                    if (val != null)
                    {
                        key.SetValue(originalVal, val);
                        key.DeleteValue(valName);
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        public async Task<bool> DeleteItemAsync(PersistenceItem item)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (item.Category == PersistenceCategory.RegistryRun)
                    {
                        string loc = item.LocationSource;
                        int arrowIdx = loc.IndexOf("->");
                        if (arrowIdx > 0)
                        {
                            string pathPart = loc.Substring(0, arrowIdx).Trim();
                            string valName = loc.Substring(arrowIdx + 2).Trim();
                            RegistryKey? rootKey = loc.StartsWith("HKLM") ? Registry.LocalMachine : Registry.CurrentUser;
                            string subPath = pathPart.Replace("HKLM\\", "").Replace("HKCU\\", "").Replace("...\\", "Software\\Microsoft\\Windows\\CurrentVersion\\");

                            using var key = rootKey.OpenSubKey(subPath, writable: true);
                            key?.DeleteValue(valName, false);
                            return true;
                        }
                    }
                    else if (item.Category == PersistenceCategory.ScheduledTask)
                    {
                        string taskName = item.LocationSource.Replace("Görev: ", "").Trim();
                        var psi = new ProcessStartInfo
                        {
                            FileName = "schtasks.exe",
                            Arguments = $"/Delete /TN \"{taskName}\" /F",
                            CreateNoWindow = true,
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        using var proc = Process.Start(psi);
                        proc?.WaitForExit(5000);
                        return proc?.ExitCode == 0;
                    }
                    else if (item.Category == PersistenceCategory.StartupFolder)
                    {
                        if (File.Exists(item.FilePath))
                        {
                            File.Delete(item.FilePath);
                            return true;
                        }
                    }
                }
                catch { }

                return false;
            });
        }

        #endregion

        #region Helper Methods & Item Factory

        private PersistenceItem CreateItem(
            string name,
            string filePath,
            string args,
            PersistenceCategory category,
            string categoryDisplay,
            string source)
        {
            var item = new PersistenceItem
            {
                Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(filePath) : name,
                FilePath = filePath,
                Arguments = args,
                Category = category,
                CategoryDisplayName = categoryDisplay,
                LocationSource = source
            };

            // 1. Digital Signature Analysis (WinVerifyTrust)
            if (File.Exists(filePath))
            {
                try
                {
                    var (sigStatus, signer) = VerifyFileSignature(filePath);
                    item.Signature = sigStatus;
                    item.SignatureSignerName = signer;
                    item.Publisher = !string.IsNullOrWhiteSpace(signer) ? signer : "Bilinmeyen / İmzasız";

                    bool isMs = item.Publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                                filePath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase);
                    item.IsMicrosoft = isMs;

                    // Compute SHA-256
                    item.Sha256Hash = _virusTotalService.ComputeSha256(filePath);

                    // Extract Icon
                    item.IconSource = ExtractFileIcon(filePath);
                }
                catch { }
            }
            else
            {
                item.Publisher = "Dosya Bulunamadı (Yetim Kayıt)";
                item.Signature = SignatureStatus.Unsigned;
            }

            return item;
        }

        private static (SignatureStatus Status, string Signer) VerifyFileSignature(string filePath)
        {
            try
            {
                using var fileInfo = new WINTRUST_FILE_INFO(filePath);
                using var trustData = new WINTRUST_DATA(fileInfo);

                uint result = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, trustData);

                string signer = string.Empty;
                try
                {
#pragma warning disable SYSLIB0057
                    var cert = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
                    if (cert != null)
                    {
                        string subject = cert.Subject;
                        int cnIdx = subject.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
                        if (cnIdx >= 0)
                        {
                            int commaIdx = subject.IndexOf(',', cnIdx);
                            signer = commaIdx > 0 ? subject.Substring(cnIdx + 3, commaIdx - (cnIdx + 3)) : subject.Substring(cnIdx + 3);
                        }
                        else
                        {
                            signer = subject;
                        }
                    }
                }
                catch { }

                if (result == 0) // ERROR_SUCCESS
                {
                    return (SignatureStatus.Verified, signer);
                }
                else if (result == 0x800B0100) // TRUST_E_NOSIGNATURE
                {
                    return (SignatureStatus.Unsigned, string.Empty);
                }
                else
                {
                    return (SignatureStatus.InvalidOrTampered, !string.IsNullOrWhiteSpace(signer) ? $"{signer} (Geçersiz)" : "Bozulmuş İmza");
                }
            }
            catch
            {
                return (SignatureStatus.Unsigned, string.Empty);
            }
        }

        private static ImageSource? ExtractFileIcon(string filePath)
        {
            try
            {
                using var ico = Icon.ExtractAssociatedIcon(filePath);
                if (ico != null)
                {
                    using var bmp = ico.ToBitmap();
                    var hBmp = bmp.GetHbitmap();
                    try
                    {
                        var wpfBmp = Imaging.CreateBitmapSourceFromHBitmap(
                            hBmp,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        wpfBmp.Freeze();
                        return wpfBmp;
                    }
                    finally
                    {
                        DeleteObject(hBmp);
                    }
                }
            }
            catch { }

            return null;
        }

        private static (string FilePath, string Arguments) ParseCommandAndArgs(string rawCmd)
        {
            if (string.IsNullOrWhiteSpace(rawCmd)) return (string.Empty, string.Empty);
            string trimmed = rawCmd.Trim();

            if (trimmed.StartsWith("\""))
            {
                int endQuote = trimmed.IndexOf('"', 1);
                if (endQuote > 0)
                {
                    string path = trimmed.Substring(1, endQuote - 1).Trim();
                    string args = trimmed.Substring(endQuote + 1).Trim();
                    return (path, args);
                }
            }

            int spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx > 0)
            {
                string path = trimmed.Substring(0, spaceIdx).Trim();
                string args = trimmed.Substring(spaceIdx + 1).Trim();
                return (path, args);
            }

            return (trimmed, string.Empty);
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var sb = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(sb.ToString().Trim());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            result.Add(sb.ToString().Trim());
            return result;
        }

        private static string? ResolveShortcutTarget(string shortcutPath)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic shell = Activator.CreateInstance(shellType)!;
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    return shortcut.TargetPath;
                }
            }
            catch { }
            return null;
        }

        #endregion
    }
}
