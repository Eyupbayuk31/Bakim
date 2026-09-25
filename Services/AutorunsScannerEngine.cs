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
using Bakım.Core.Safety;
using Bakım.Core.Startup;
using Bakım.Helpers;
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
                bool approvable = IsApprovableRunKey(target.SubKey);
                using var approvedKey = approvable ? baseKey.OpenSubKey(StartupApprovedPaths.ForRunKey(target.SubKey)) : null;
                if (subKey != null)
                {
                    foreach (var valueName in subKey.GetValueNames())
                    {
                        string rawCmd = subKey.GetValue(valueName)?.ToString() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(rawCmd)) continue;

                        var (filePath, args) = ParseCommandAndArgs(rawCmd);

                        // Eski sürümler devre dışı bırakmak için değeri "Bakim_Disabled_X" diye yeniden
                        // adlandırıyordu; Windows Run altındaki HER değeri çalıştırdığı için bu etkisizdi.
                        // Gerçek durum StartupApproved'dan okunur; eski adlar olduğu gibi (etkin) gösterilir.
                        bool isEnabled = !approvable || StartupApprovedPaths.IsEnabled(approvedKey?.GetValue(valueName) as byte[]);
                        string cleanName = valueName.StartsWith(LegacyDisabledPrefix, StringComparison.Ordinal)
                            ? valueName[LegacyDisabledPrefix.Length..]
                            : valueName;

                        var item = CreateItem(
                            cleanName,
                            filePath,
                            args,
                            PersistenceCategory.RegistryRun,
                            "Kayıt Defteri (Run)",
                            $"{target.Display} -> {valueName}");

                        item.IsEnabled = isEnabled;
                        item.RegistryKeyPath = (target.Hive == RegistryHive.LocalMachine ? "HKLM\\" : "HKCU\\") + target.SubKey;
                        item.RegistryValueName = valueName;
                        yield return item;
                    }
                }
            }

            await Task.CompletedTask;
        }

        private const string LegacyDisabledPrefix = "Bakim_Disabled_";
        private const string LegacyDisabledFileSuffix = ".bakim_disabled";

        /// <summary>StartupApproved yalnızca Run ve WOW6432Node Run için geçerlidir (RunOnce ve politika anahtarları için değil).</summary>
        private static bool IsApprovableRunKey(string subKey) =>
            subKey.EndsWith(@"\CurrentVersion\Run", StringComparison.OrdinalIgnoreCase);

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
                    bool legacyDisabled = fileName.EndsWith(LegacyDisabledFileSuffix, StringComparison.OrdinalIgnoreCase);
                    bool isEnabled = !legacyDisabled && StartupApprovedPaths.IsEnabled(ReadStartupFolderApproval(folder.Path == commonStartup, fileName));

                    // Resolve .lnk target if shortcut
                    if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        targetFile = ResolveShortcutTarget(file) ?? file;
                    }

                    var item = CreateItem(
                        Path.GetFileNameWithoutExtension(fileName.Replace(LegacyDisabledFileSuffix, "", StringComparison.OrdinalIgnoreCase)),
                        targetFile,
                        args,
                        PersistenceCategory.StartupFolder,
                        "Başlangıç Klasörü",
                        folder.Display);

                    item.IsEnabled = isEnabled;
                    item.SourceFilePath = file;
                    yield return item;
                }
            }

            await Task.CompletedTask;
        }

        private static byte[]? ReadStartupFolderApproval(bool common, string fileName)
        {
            try
            {
                using var key = (common ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(StartupApprovedPaths.ForStartupFolder);
                return key?.GetValue(fileName) as byte[];
            }
            catch
            {
                return null;
            }
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

        /// <summary>
        /// Girdiyi Görev Yöneticisi ile aynı yöntemle etkinleştirir/devre dışı bırakır.
        ///
        /// Eski hatalar: Run değerini "Bakim_Disabled_X" diye yeniden adlandırmak etkisizdi (Windows her
        /// değeri çalıştırır); başlangıç klasörü girdisinde kısayol yerine kısayolun HEDEFİ olan program
        /// exe'si yeniden adlandırılıyordu (program bozuluyordu); WOW6432Node ve politika anahtarlarının
        /// yolu yanlış çözülüyordu.
        /// </summary>
        public async Task<bool> ToggleItemAsync(PersistenceItem item, bool enable)
        {
            try
            {
                switch (item.Category)
                {
                    case PersistenceCategory.RegistryRun:
                        return await Task.Run(() => ToggleRegistryItem(item, enable));

                    case PersistenceCategory.StartupFolder:
                        return await Task.Run(() => ToggleStartupFolderItem(item, enable));

                    case PersistenceCategory.ScheduledTask:
                    {
                        string taskName = item.LocationSource.Replace("Görev: ", "").Trim();
                        var result = await ElevatedPowerShell.RunAsync(
                            $"schtasks.exe /Change /TN {ElevatedPowerShell.Quote(taskName)} {(enable ? "/Enable" : "/Disable")}; exit $LASTEXITCODE",
                            TimeSpan.FromSeconds(30));
                        return result.Succeeded;
                    }

                    case PersistenceCategory.WindowsService:
                    {
                        string serviceName = item.LocationSource.Replace("Hizmet: ", "").Split(' ')[0];
                        var result = await ElevatedPowerShell.RunAsync(
                            $"Set-Service -Name {ElevatedPowerShell.Quote(serviceName)} -StartupType {(enable ? "Automatic" : "Disabled")}",
                            TimeSpan.FromSeconds(30));
                        return result.Succeeded;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning($"Kalıcılık girdisi değiştirilemedi: {item.Name}", ex, nameof(AutorunsScannerEngine));
            }
            return false;
        }

        private static bool ToggleRegistryItem(PersistenceItem item, bool enable)
        {
            if (string.IsNullOrEmpty(item.RegistryKeyPath) || string.IsNullOrEmpty(item.RegistryValueName)) return false;
            var root = item.RegistryKeyPath.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) ? Registry.LocalMachine : Registry.CurrentUser;
            string subKey = item.RegistryKeyPath[5..];

            // RunOnce ve politika anahtarları StartupApproved'u dikkate almaz: devre dışı bırakılamaz, silinebilir.
            if (!IsApprovableRunKey(subKey)) return false;

            string valueName = item.RegistryValueName;

            // Eski "Bakim_Disabled_" adını özgün adına döndür (yoksa Windows onu çalıştırmaya devam eder).
            if (valueName.StartsWith(LegacyDisabledPrefix, StringComparison.Ordinal))
            {
                string original = valueName[LegacyDisabledPrefix.Length..];
                using var key = root.OpenSubKey(subKey, writable: true);
                object? value = key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (key == null || value == null) return false;
                key.SetValue(original, value, key.GetValueKind(valueName));
                key.DeleteValue(valueName, false);
                valueName = original;
                item.RegistryValueName = original;
            }

            string approvedPath = StartupApprovedPaths.ForRunKey(subKey);
            byte[]? existing;
            using (var approved = root.OpenSubKey(approvedPath))
                existing = approved?.GetValue(valueName) as byte[];

            if (!VerifiedRegistry.SetBinary(root, approvedPath, valueName, StartupApprovedPaths.BuildValue(enable, DateTime.UtcNow, existing)))
                return false;
            item.IsEnabled = enable;
            return true;
        }

        private static bool ToggleStartupFolderItem(PersistenceItem item, bool enable)
        {
            // Yalnızca taramadan gelen gerçek kısayol; hedef programa ASLA dokunulmaz.
            string? shortcut = item.SourceFilePath;
            if (string.IsNullOrEmpty(shortcut) || !File.Exists(shortcut)) return false;

            bool common = shortcut.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StringComparison.OrdinalIgnoreCase);
            var root = common ? Registry.LocalMachine : Registry.CurrentUser;

            if (shortcut.EndsWith(LegacyDisabledFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                if (!enable) return true;
                string restored = shortcut[..^LegacyDisabledFileSuffix.Length];
                File.Move(shortcut, restored);
                item.SourceFilePath = shortcut = restored;
            }

            string name = Path.GetFileName(shortcut);
            byte[]? existing;
            using (var approved = root.OpenSubKey(StartupApprovedPaths.ForStartupFolder))
                existing = approved?.GetValue(name) as byte[];

            if (!VerifiedRegistry.SetBinary(root, StartupApprovedPaths.ForStartupFolder, name, StartupApprovedPaths.BuildValue(enable, DateTime.UtcNow, existing)))
                return false;
            item.IsEnabled = enable;
            return true;
        }

        /// <summary>
        /// Girdiyi kaldırır. Kayıt defteri değeri silinmeden önce yedeklenir (geri yüklenebilir);
        /// başlangıç klasörü kısayolu Geri Dönüşüm Kutusu'na gider. Eskiden klasör girdisinde kısayolun
        /// hedefi olan PROGRAM exe'si kalıcı olarak siliniyordu.
        /// </summary>
        public async Task<bool> DeleteItemAsync(PersistenceItem item)
        {
            try
            {
                switch (item.Category)
                {
                    case PersistenceCategory.RegistryRun:
                    {
                        if (string.IsNullOrEmpty(item.RegistryKeyPath) || string.IsNullOrEmpty(item.RegistryValueName)) return false;
                        bool hklm = item.RegistryKeyPath.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase);
                        string subKey = item.RegistryKeyPath[5..];
                        var safeRegistry = App.TryGetService<Safety.ISafeRegistryService>() ?? new Safety.SafeRegistryService(AppLog.Current);
                        string journal = Safety.UndoJournal.Create($"Kalıcılık girdisi silindi: {item.Name}");
                        var result = await safeRegistry.DeleteValueAsync(
                            new RegistryPath(hklm ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64, subKey, item.RegistryValueName),
                            journal);
                        if (!result.Succeeded && result.Outcome != Safety.DeleteOutcome.NotFound) return false;

                        if (IsApprovableRunKey(subKey))
                            VerifiedRegistry.DeleteValue(hklm ? Registry.LocalMachine : Registry.CurrentUser, StartupApprovedPaths.ForRunKey(subKey), item.RegistryValueName);
                        return true;
                    }

                    case PersistenceCategory.ScheduledTask:
                    {
                        string taskName = item.LocationSource.Replace("Görev: ", "").Trim();
                        var result = await ElevatedPowerShell.RunAsync(
                            $"schtasks.exe /Delete /TN {ElevatedPowerShell.Quote(taskName)} /F; exit $LASTEXITCODE",
                            TimeSpan.FromSeconds(30));
                        return result.Succeeded;
                    }

                    case PersistenceCategory.StartupFolder:
                    {
                        string? shortcut = item.SourceFilePath;
                        if (string.IsNullOrEmpty(shortcut) || !File.Exists(shortcut)) return false;
                        if (!RecycleBin.TrySend(shortcut)) return false;

                        bool common = shortcut.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StringComparison.OrdinalIgnoreCase);
                        VerifiedRegistry.DeleteValue(common ? Registry.LocalMachine : Registry.CurrentUser,
                            StartupApprovedPaths.ForStartupFolder, Path.GetFileName(shortcut));
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning($"Kalıcılık girdisi silinemedi: {item.Name}", ex, nameof(AutorunsScannerEngine));
            }
            return false;
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

        private static string? ResolveShortcutTarget(string shortcutPath) => Bakım.Helpers.ShellLink.ResolveTarget(shortcutPath);

        #endregion
    }
}
