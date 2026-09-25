using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Bakım.Core.Sentinel;
using Bakım.Helpers;

namespace Bakım.Services.Sentinel.Sensors
{
    /// <summary>Sistem durumu görüntüsü ve okunabilen alanlar.</summary>
    public sealed record SystemStateCapture(IReadOnlyDictionary<string, string> Values, IReadOnlySet<SystemArea> ReadableAreas, DateTime TakenUtc)
    {
        public static SystemStateCapture Empty { get; } =
            new(new Dictionary<string, string>(), new HashSet<SystemArea>(), DateTime.MinValue);
    }

    /// <summary>
    /// Kurulumların sistem genelinde dokunduğu alanları okur (NÖB 2.3, 2.5): IFEO, Winlogon,
    /// AppInit_DLLs, PATH, proxy, tarayıcı politikaları, Defender istisnaları, kök sertifikalar,
    /// hosts, güvenlik duvarı kuralları, zamanlanmış görevler ve sağ tık uzantıları.
    /// Okunamayan alan (ör. yönetici gerektiren Defender) görüntüye "okunamadı" olarak girer ve
    /// karşılaştırmada yok sayılır: yetki farkı sahte "eklendi" üretmez.
    /// </summary>
    public static class SystemStateSensor
    {
        private const string Hklm = "HKLM";
        private const string Hkcu = "HKCU";

        public static SystemStateCapture Capture()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var readable = new HashSet<SystemArea>();

            void Area(SystemArea area, Action<Dictionary<string, string>> read)
            {
                try
                {
                    read(map);
                    readable.Add(area);
                }
                catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException or COMException or
                                               CryptographicException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    AppLog.Debug($"Sistem alanı okunamadı ({area}): {ex.Message}", nameof(SystemStateSensor));
                }
            }

            Area(SystemArea.Ifeo, ReadIfeo);
            Area(SystemArea.Winlogon, ReadWinlogon);
            Area(SystemArea.AppInit, ReadAppInit);
            Area(SystemArea.EnvironmentPath, ReadPath);
            Area(SystemArea.Proxy, ReadProxy);
            Area(SystemArea.BrowserPolicy, ReadBrowserPolicies);
            Area(SystemArea.DefenderExclusion, ReadDefenderExclusions);
            Area(SystemArea.RootCertificate, ReadRootCertificates);
            Area(SystemArea.Hosts, ReadHosts);
            Area(SystemArea.FirewallRule, ReadFirewallRules);
            Area(SystemArea.ScheduledTask, ReadScheduledTasks);
            Area(SystemArea.ShellExtension, ReadShellExtensions);

            return new SystemStateCapture(map, readable, DateTime.UtcNow);
        }

        private static void Put(Dictionary<string, string> map, SystemArea area, string key, string? value) =>
            map[SystemStateSnapshot.MakeKey(area, key)] = value ?? string.Empty;

        private static string ValueText(RegistryKey key, string? name) =>
            key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) switch
            {
                null => string.Empty,
                string[] multi => string.Join(";", multi),
                byte[] bytes => Convert.ToHexString(bytes),
                object o => o.ToString() ?? string.Empty
            };

        private static readonly RegistryView[] BothViews = { RegistryView.Registry64, RegistryView.Registry32 };

        private static string Label(RegistryView view) => view == RegistryView.Registry32 ? " [32]" : string.Empty;

        private static void ReadIfeo(Dictionary<string, string> map)
        {
            const string ifeo = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
            const string spe = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SilentProcessExit";
            foreach (var view in BothViews)
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using (var key = root.OpenSubKey(ifeo))
                {
                    foreach (string exe in key?.GetSubKeyNames() ?? Array.Empty<string>())
                    {
                        using var sub = key!.OpenSubKey(exe);
                        if (sub?.GetValue("Debugger") != null)
                            Put(map, SystemArea.Ifeo, $@"{Hklm}\{ifeo}\{exe}\Debugger{Label(view)}", ValueText(sub, "Debugger"));
                    }
                }
                using (var key = root.OpenSubKey(spe))
                {
                    foreach (string exe in key?.GetSubKeyNames() ?? Array.Empty<string>())
                    {
                        using var sub = key!.OpenSubKey(exe);
                        if (sub?.GetValue("MonitorProcess") != null)
                            Put(map, SystemArea.Ifeo, $@"{Hklm}\{spe}\{exe}\MonitorProcess{Label(view)}", ValueText(sub, "MonitorProcess"));
                    }
                }
            }
        }

        private static void ReadWinlogon(Dictionary<string, string> map)
        {
            const string path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
            using (var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(path))
            {
                foreach (string name in new[] { "Shell", "Userinit", "Taskman" })
                {
                    if (lm?.GetValue(name) != null) Put(map, SystemArea.Winlogon, $@"{Hklm}\{path}\{name}", ValueText(lm, name));
                }
            }
            using var cu = Registry.CurrentUser.OpenSubKey(path);
            if (cu?.GetValue("Shell") != null) Put(map, SystemArea.Winlogon, $@"{Hkcu}\{path}\Shell", ValueText(cu, "Shell"));
        }

        private static void ReadAppInit(Dictionary<string, string> map)
        {
            const string path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows";
            foreach (var view in BothViews)
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(path);
                if (key == null) continue;
                foreach (string name in new[] { "AppInit_DLLs", "LoadAppInit_DLLs" })
                {
                    if (key.GetValue(name) != null) Put(map, SystemArea.AppInit, $@"{Hklm}\{path}\{name}{Label(view)}", ValueText(key, name));
                }
            }
        }

        private static void ReadPath(Dictionary<string, string> map)
        {
            const string machine = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
            using (var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(machine))
            {
                if (lm?.GetValue("Path") != null) Put(map, SystemArea.EnvironmentPath, $@"{Hklm}\{machine}\Path", ValueText(lm, "Path"));
            }
            using var cu = Registry.CurrentUser.OpenSubKey("Environment");
            if (cu?.GetValue("Path") != null) Put(map, SystemArea.EnvironmentPath, $@"{Hkcu}\Environment\Path", ValueText(cu, "Path"));
        }

        private static void ReadProxy(Dictionary<string, string> map)
        {
            const string path = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
            using var key = Registry.CurrentUser.OpenSubKey(path);
            if (key == null) return;
            foreach (string name in new[] { "ProxyServer", "ProxyEnable", "AutoConfigURL" })
            {
                if (key.GetValue(name) != null) Put(map, SystemArea.Proxy, $@"{Hkcu}\{path}\{name}", ValueText(key, name));
            }
        }

        private static readonly string[] PolicyRoots =
        {
            @"SOFTWARE\Policies\Google\Chrome",
            @"SOFTWARE\Policies\Microsoft\Edge",
            @"SOFTWARE\Policies\Mozilla\Firefox",
            @"SOFTWARE\Policies\BraveSoftware\Brave",
        };

        private static void ReadBrowserPolicies(Dictionary<string, string> map)
        {
            foreach (var (hive, prefix) in new[] { (RegistryHive.LocalMachine, Hklm), (RegistryHive.CurrentUser, Hkcu) })
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                foreach (string policy in PolicyRoots)
                {
                    using var key = root.OpenSubKey(policy);
                    if (key != null) ReadValuesRecursive(map, SystemArea.BrowserPolicy, $@"{prefix}\{policy}", key, depth: 0);
                }
            }
        }

        private static void ReadValuesRecursive(Dictionary<string, string> map, SystemArea area, string display, RegistryKey key, int depth)
        {
            foreach (string name in key.GetValueNames())
                Put(map, area, $@"{display}\{(name.Length == 0 ? "(Varsayılan)" : name)}", ValueText(key, name));
            if (depth >= 2) return;
            foreach (string sub in key.GetSubKeyNames())
            {
                using var child = key.OpenSubKey(sub);
                if (child != null) ReadValuesRecursive(map, area, $@"{display}\{sub}", child, depth + 1);
            }
        }

        private static void ReadDefenderExclusions(Dictionary<string, string> map)
        {
            const string path = @"SOFTWARE\Microsoft\Windows Defender\Exclusions";
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            // Yönetici olmadan erişim reddedilir; alan "okunamadı" sayılır.
            using var key = root.OpenSubKey(path) ?? throw new UnauthorizedAccessException("Defender istisnaları okunamadı.");
            foreach (string kind in new[] { "Paths", "Processes", "Extensions", "IpAddresses" })
            {
                using var sub = key.OpenSubKey(kind);
                foreach (string name in sub?.GetValueNames() ?? Array.Empty<string>())
                    Put(map, SystemArea.DefenderExclusion, $"{kind}: {name}", ValueText(sub!, name));
            }
        }

        /// <summary>
        /// Kök sertifikalar FİZİKSEL depolardan (kayıt defteri) okunur. Mantıksal "Root" deposu Windows'un
        /// otomatik güncellediği AuthRoot sertifikalarını da gösterebilir; kurulum sırasında Windows'un
        /// indirdiği bir kök "kurulum sertifika ekledi" sanılmasın.
        /// </summary>
        private static readonly (RegistryHive Hive, string Prefix, string Path)[] RootStores =
        {
            (RegistryHive.CurrentUser, Hkcu, @"Software\Microsoft\SystemCertificates\Root\Certificates"),
            (RegistryHive.LocalMachine, Hklm, @"SOFTWARE\Microsoft\SystemCertificates\Root\Certificates"),
            (RegistryHive.LocalMachine, Hklm, @"SOFTWARE\Policies\Microsoft\SystemCertificates\Root\Certificates"),
            (RegistryHive.CurrentUser, Hkcu, @"Software\Policies\Microsoft\SystemCertificates\Root\Certificates"),
        };

        private static void ReadRootCertificates(Dictionary<string, string> map)
        {
            Dictionary<string, string>? subjects = null;
            foreach (var (hive, prefix, path) in RootStores)
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = root.OpenSubKey(path);
                foreach (string thumbprint in key?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    subjects ??= LoadSubjects();
                    Put(map, SystemArea.RootCertificate, $@"{prefix}\{path}\{thumbprint}",
                        subjects.TryGetValue(thumbprint, out var subject) ? subject : "(konu okunamadı)");
                }
            }
        }

        /// <summary>Parmak izi → konu (yalnızca görüntü için).</summary>
        private static Dictionary<string, string> LoadSubjects()
        {
            var subjects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
            {
                try
                {
                    using var store = new X509Store(StoreName.Root, location);
                    store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                    foreach (var cert in store.Certificates)
                    {
                        using (cert) subjects.TryAdd(cert.Thumbprint, cert.Subject);
                    }
                }
                catch (CryptographicException ex)
                {
                    AppLog.Debug($"Sertifika deposu açılamadı ({location}): {ex.Message}", nameof(SystemStateSensor));
                }
            }
            return subjects;
        }

        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

        private static void ReadHosts(Dictionary<string, string> map)
        {
            string hosts = Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");
            if (!File.Exists(hosts)) return;
            foreach (string raw in File.ReadLines(hosts).Take(20000))
            {
                string line = raw.Trim();
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line[..hash].Trim();
                if (line.Length == 0) continue;
                Put(map, SystemArea.Hosts, Whitespace.Replace(line, " "), string.Empty);
            }
        }

        private static void ReadFirewallRules(Dictionary<string, string> map)
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var rules = root.OpenSubKey(Uninstall.FootprintCollector.FirewallRulesKey);
            foreach (string id in rules?.GetValueNames() ?? Array.Empty<string>())
                Put(map, SystemArea.FirewallRule, id, rules!.GetValue(id) as string);
        }

        private static void ReadScheduledTasks(Dictionary<string, string> map)
        {
            if (TaskSchedulerReader.Connect() is not { } probe)
                throw new COMException("Görev Zamanlayıcı kullanılamıyor.");
            Marshal.ReleaseComObject(probe);
            foreach (var task in TaskSchedulerReader.ReadAll())
                Put(map, SystemArea.ScheduledTask, task.Path, string.Join(" | ", task.ExecActions));
        }

        private static readonly string[] ShellRoots = { "*", "Directory", @"Directory\Background", "Folder", "Drive", "AllFilesystemObjects" };

        private static void ReadShellExtensions(Dictionary<string, string> map)
        {
            foreach (var (hive, prefix) in new[] { (RegistryHive.LocalMachine, Hklm), (RegistryHive.CurrentUser, Hkcu) })
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                foreach (string type in ShellRoots)
                {
                    string handlers = $@"SOFTWARE\Classes\{type}\shellex\ContextMenuHandlers";
                    using (var key = root.OpenSubKey(handlers))
                    {
                        foreach (string name in key?.GetSubKeyNames() ?? Array.Empty<string>())
                        {
                            using var sub = key!.OpenSubKey(name);
                            Put(map, SystemArea.ShellExtension, $@"{prefix}\{handlers}\{name}", sub == null ? null : ValueText(sub, null));
                        }
                    }
                    string verbs = $@"SOFTWARE\Classes\{type}\shell";
                    using (var key = root.OpenSubKey(verbs))
                    {
                        foreach (string verb in key?.GetSubKeyNames() ?? Array.Empty<string>())
                        {
                            using var command = key!.OpenSubKey($@"{verb}\command");
                            Put(map, SystemArea.ShellExtension, $@"{prefix}\{verbs}\{verb}", command == null ? null : ValueText(command, null));
                        }
                    }
                }
            }
        }
    }
}
