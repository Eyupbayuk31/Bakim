using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Bakım.Core.Analyzer;
using Bakım.Models;

namespace Bakım.Services
{
    /// <summary>
    /// Analizör v3 Faz 1: Sysinternals Autoruns düzeyinde ek kalıcılık konumları. Bu toplayıcılar
    /// salt okunurdur (<see cref="AutorunActionPolicy"/> değiştirmeye izin vermez).
    /// </summary>
    public partial class AutorunsScannerEngine
    {
        private static RegistryKey Hklm64() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        private static RegistryKey Hkcu() => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);

        private static string System32 => Environment.SystemDirectory;

        /// <summary>DLL adı ("msv1_0", "mswsock.dll") → System32 altında tam yol.</summary>
        private static string System32Dll(string name)
        {
            string n = name.Trim().Trim('"');
            if (n.Length == 0) return n;
            if (CommandLineResolver.IsRooted(n) || n.Contains('%')) return n;
            return Path.Combine(System32, n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n : n + ".dll");
        }

        private static IEnumerable<string> MultiString(RegistryKey? key, string valueName) => key?.GetValue(valueName) switch
        {
            string[] multi => multi,
            string single => single.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries),
            _ => Array.Empty<string>()
        };

        #region Oturum açılışı kancaları

        private List<PersistenceItem> CollectLogonHooks(ResolverEnvironment env)
        {
            var list = new List<PersistenceItem>();

            // Active Setup: her kullanıcının ilk oturumunda bir kez çalışan StubPath.
            foreach (string path in new[] { @"SOFTWARE\Microsoft\Active Setup\Installed Components", @"SOFTWARE\WOW6432Node\Microsoft\Active Setup\Installed Components" })
            {
                using var root = Hklm64().OpenSubKey(path);
                foreach (string guid in root?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    string stub = ReadValueSafe(root!, guid, "StubPath");
                    // Bazı Windows bileşenlerinde StubPath yalnızca argümandır ("/UserInstall", "U"): komut değildir.
                    if (string.IsNullOrWhiteSpace(stub) || stub.Trim().Length <= 2 || stub.TrimStart().StartsWith('/')) continue;
                    using var comp = root!.OpenSubKey(guid);
                    string name = comp?.GetValue(null) as string ?? guid;
                    list.Add(CreateItem($"Active Setup: {name}", stub, PersistenceCategory.WinlogonIfeo, "Active Setup", $@"HKLM\…\Active Setup\{guid}", env));
                }
            }

            // Komut istemi AutoRun: her cmd.exe açılışında çalışır.
            foreach (var (root, hive) in new[] { (Hklm64(), "HKLM"), (Hkcu(), "HKCU") })
            {
                using (root)
                using (var key = root.OpenSubKey(@"Software\Microsoft\Command Processor"))
                {
                    string auto = key?.GetValue("AutoRun")?.ToString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(auto))
                        list.Add(CreateItem("Komut istemi AutoRun", auto, PersistenceCategory.WinlogonIfeo, "Komut istemi", $@"{hive}\…\Command Processor\AutoRun", env));
                }
            }

            // Kullanıcıya özel Winlogon Shell (explorer yerine başka bir kabuk) ve AppSetup.
            using (var userWinlogon = Hkcu().OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Winlogon"))
            {
                string shell = userWinlogon?.GetValue("Shell")?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(shell))
                    list.Add(CreateItem("Winlogon: Shell (kullanıcı)", shell, PersistenceCategory.WinlogonIfeo, "Winlogon", @"HKCU\…\Winlogon\Shell", env));
            }
            using (var winlogon = Hklm64().OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"))
            {
                string appSetup = winlogon?.GetValue("AppSetup")?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(appSetup))
                    list.Add(CreateItem("Winlogon: AppSetup", appSetup, PersistenceCategory.WinlogonIfeo, "Winlogon", @"HKLM\…\Winlogon\AppSetup", env));
            }
            return list;
        }

        #endregion

        #region Sistem kancaları

        private List<PersistenceItem> CollectSystemHooks(ResolverEnvironment env)
        {
            var list = new List<PersistenceItem>();
            PersistenceItem Hook(string name, string raw, string display, string source)
            {
                var item = CreateItem(name, raw, PersistenceCategory.SystemHook, display, source, env);
                list.Add(item);
                return item;
            }

            using var hklm = Hklm64();

            // AppInit_DLLs: user32 yükleyen HER sürece enjekte edilir.
            foreach (var (path, suffix) in new[] { (@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows", ""), (@"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Windows", " (32 bit)") })
            {
                using var key = hklm.OpenSubKey(path);
                bool load = key?.GetValue("LoadAppInit_DLLs") is int l && l != 0;
                foreach (string dll in MultiString(key, "AppInit_DLLs"))
                {
                    var item = Hook($"AppInit_DLLs{suffix}: {Path.GetFileName(dll)}", dll.Contains(' ') ? $"\"{dll}\"" : dll, "AppInit DLL", $@"HKLM\…\Windows\AppInit_DLLs{suffix}");
                    item.IsEnabled = load;
                    item.CollectorRisk = 25;
                    item.CollectorNotes = "AppInit_DLLs, pencere kullanan her sürece yüklenir; modern yazılımlar nadiren kullanır.";
                }
            }

            using (var key = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\AppCertDlls"))
            {
                foreach (string name in key?.GetValueNames() ?? Array.Empty<string>())
                {
                    string dll = key!.GetValue(name)?.ToString() ?? string.Empty;
                    if (dll.Length == 0) continue;
                    var item = Hook($"AppCertDlls: {name}", dll, "AppCert DLL", @"HKLM\…\Session Manager\AppCertDlls");
                    item.CollectorRisk = 25;
                    item.CollectorNotes = "AppCertDlls, süreç oluşturan her çağrıya yüklenir; meşru kullanımı çok nadirdir.";
                }
            }

            // LSA paketleri: parola ve oturum açma sürecine (lsass) yüklenir.
            using (var lsa = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa"))
            {
                foreach (string value in new[] { "Authentication Packages", "Notification Packages", "Security Packages" })
                {
                    foreach (string pkg in MultiString(lsa, value).Where(p => p.Trim('"').Length > 0))
                        Hook($"LSA: {pkg}", System32Dll(pkg), "LSA paketi", $@"HKLM\…\Lsa\{value}");
                }
            }

            // Yazdırma izleyicileri: spoolsv (SYSTEM) içine yüklenir.
            using (var monitors = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Monitors"))
            {
                foreach (string name in monitors?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    string driver = ReadValueSafe(monitors!, name, "Driver");
                    if (driver.Length > 0) Hook($"Yazdırma izleyicisi: {name}", System32Dll(driver), "Yazdırma izleyicisi", $@"HKLM\…\Print\Monitors\{name}");
                }
            }

            // Winsock katalogları: ağ kullanan her sürece yüklenen sağlayıcılar.
            foreach (string catalog in new[] { "Catalog_Entries", "Catalog_Entries64" })
            {
                using var entries = hklm.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\WinSock2\Parameters\Protocol_Catalog9\{catalog}");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string entry in entries?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using var key = entries!.OpenSubKey(entry);
                    if (key?.GetValue("PackedCatalogItem") is not byte[] packed) continue;
                    int end = Array.IndexOf(packed, (byte)0);
                    string dll = Encoding.Default.GetString(packed, 0, end > 0 ? end : Math.Min(packed.Length, 260)).Trim();
                    if (dll.Length > 0 && seen.Add(dll)) Hook($"Winsock sağlayıcısı: {Path.GetFileName(dll)}", dll, "Winsock LSP", $@"HKLM\…\WinSock2\Protocol_Catalog9\{catalog}");
                }
            }
            using (var ns = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WinSock2\Parameters\NameSpace_Catalog5\Catalog_Entries64"))
            {
                foreach (string entry in ns?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    string lib = ReadValueSafe(ns!, entry, "LibraryPath");
                    if (lib.Length > 0) Hook($"Ad alanı sağlayıcısı: {Path.GetFileName(lib)}", lib, "Winsock NSP", @"HKLM\…\WinSock2\NameSpace_Catalog5");
                }
            }

            // Netsh yardımcıları: netsh.exe her çalıştığında yüklenir.
            using (var netsh = hklm.OpenSubKey(@"SOFTWARE\Microsoft\NetSh"))
            {
                foreach (string name in netsh?.GetValueNames() ?? Array.Empty<string>())
                {
                    string dll = netsh!.GetValue(name)?.ToString() ?? string.Empty;
                    if (dll.Length > 0) Hook($"Netsh yardımcısı: {name}", System32Dll(dll), "Netsh yardımcısı", @"HKLM\…\NetSh");
                }
            }

            // BootExecute: Windows açılmadan önce yerel (native) uygulama olarak çalışır.
            using (var sm = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager"))
            {
                if (sm?.GetValue("BootExecute") is string[] boot)
                {
                    foreach (string line in boot.Where(b => !string.IsNullOrWhiteSpace(b)))
                    {
                        // "autocheck autochk *": "autocheck" anahtar kelimedir, program ikinci belirteçtir.
                        string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        string exe = parts[0].Equals("autocheck", StringComparison.OrdinalIgnoreCase) && parts.Length > 1 ? parts[1] : parts[0];
                        var item = Hook($"BootExecute: {line.Trim()}", System32Dll(exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe : exe + ".exe"), "BootExecute", @"HKLM\…\Session Manager\BootExecute");
                        item.RawCommand = line.Trim();
                        if (!line.Trim().Equals("autocheck autochk *", StringComparison.OrdinalIgnoreCase))
                        {
                            item.CollectorRisk = 20;
                            item.CollectorNotes = "Varsayılan dışı BootExecute girdisi: Windows açılmadan önce çalışır.";
                        }
                    }
                }
            }

            // PowerShell profilleri: her PowerShell açılışında çalışan betikler.
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var profiles = new[]
            {
                Path.Combine(System32, @"WindowsPowerShell\v1.0\profile.ps1"),
                Path.Combine(System32, @"WindowsPowerShell\v1.0\Microsoft.PowerShell_profile.ps1"),
                Path.Combine(docs, @"WindowsPowerShell\profile.ps1"),
                Path.Combine(docs, @"WindowsPowerShell\Microsoft.PowerShell_profile.ps1"),
                Path.Combine(docs, @"PowerShell\profile.ps1"),
                Path.Combine(docs, @"PowerShell\Microsoft.PowerShell_profile.ps1"),
            };
            foreach (string profile in profiles.Where(File.Exists))
            {
                var item = Hook($"PowerShell profili: {Path.GetFileName(profile)}", $"\"{profile}\"", "PowerShell profili", profile);
                item.SignatureApplicable = false;
            }
            return list;
        }

        #endregion

        #region Gezgin kancaları

        private List<PersistenceItem> CollectExplorerHooks(ResolverEnvironment env)
        {
            var list = new List<PersistenceItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string name, string clsid, string display, string source)
            {
                if (!clsid.StartsWith('{') || !seen.Add(clsid + "|" + display)) return;
                string? dll = ReadComServer(clsid);
                if (string.IsNullOrWhiteSpace(dll)) return;
                list.Add(CreateItem(name, dll.Contains(' ') && !dll.StartsWith('"') ? $"\"{dll}\"" : dll, PersistenceCategory.ShellExtension, display, source, env));
            }

            const string explorer = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";
            using var hklm = Hklm64();

            using (var overlays = hklm.OpenSubKey($@"{explorer}\ShellIconOverlayIdentifiers"))
            {
                foreach (string name in overlays?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using var key = overlays!.OpenSubKey(name);
                    Add(name.Trim(), key?.GetValue(null)?.ToString() ?? string.Empty, "Simge kaplaması", $@"HKLM\…\ShellIconOverlayIdentifiers\{name.Trim()}");
                }
            }

            using (var ssodl = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ShellServiceObjectDelayLoad"))
            {
                foreach (string name in ssodl?.GetValueNames() ?? Array.Empty<string>())
                    Add(name, ssodl!.GetValue(name)?.ToString() ?? string.Empty, "Kabuk hizmet nesnesi", @"HKLM\…\ShellServiceObjectDelayLoad");
            }

            using (var hooks = hklm.OpenSubKey($@"{explorer}\ShellExecuteHooks"))
            {
                foreach (string clsid in hooks?.GetValueNames() ?? Array.Empty<string>())
                    Add(hooks!.GetValue(clsid)?.ToString() is { Length: > 0 } n ? n : clsid, clsid, "ShellExecute kancası", @"HKLM\…\Explorer\ShellExecuteHooks");
            }

            foreach (string bhoPath in new[] { $@"{explorer}\Browser Helper Objects", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects" })
            {
                using var bho = hklm.OpenSubKey(bhoPath);
                foreach (string clsid in bho?.GetSubKeyNames() ?? Array.Empty<string>())
                    Add(clsid, clsid, "Tarayıcı yardımcı nesnesi (BHO)", @"HKLM\…\Browser Helper Objects");
            }

            foreach (string p in new[] { @"*\shellex\PropertySheetHandlers", @"Directory\Background\shellex\ContextMenuHandlers", @"Drive\shellex\ContextMenuHandlers", @"Directory\shellex\DragDropHandlers" })
            {
                using var key = Registry.ClassesRoot.OpenSubKey(p);
                foreach (string handler in key?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using var h = key!.OpenSubKey(handler);
                    string clsid = h?.GetValue(null)?.ToString() is { Length: > 0 } v ? v : handler;
                    Add(handler, clsid, "Gezgin Eklentisi", $@"HKCR\{p}\{handler}");
                }
            }
            return list;
        }

        #endregion

        #region Tarayıcı eklentileri

        private static readonly (string Browser, string UserData)[] ChromiumBrowsers =
        {
            ("Chrome", @"Google\Chrome\User Data"),
            ("Edge", @"Microsoft\Edge\User Data"),
            ("Brave", @"BraveSoftware\Brave-Browser\User Data"),
            ("Vivaldi", @"Vivaldi\User Data"),
        };

        private static readonly (string Browser, string PolicyKey)[] ChromiumPolicies =
        {
            ("Chrome", @"SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist"),
            ("Edge", @"SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist"),
            ("Brave", @"SOFTWARE\Policies\BraveSoftware\Brave\ExtensionInstallForcelist"),
        };


        private List<PersistenceItem> CollectBrowserExtensions(ResolverEnvironment env)
        {
            var list = new List<PersistenceItem>();
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // Politika ile zorla kurulanlar: kullanıcı kaldıramaz; reklam/izleme yazılımlarının sık kullandığı yol.
            var forced = new Dictionary<string, (string Browser, string UpdateUrl)>(StringComparer.OrdinalIgnoreCase);
            foreach (var (browser, policy) in ChromiumPolicies)
            {
                foreach (var root in new[] { Hklm64(), Hkcu() })
                {
                    using (root)
                    using (var key = root.OpenSubKey(policy))
                    {
                        foreach (string name in key?.GetValueNames() ?? Array.Empty<string>())
                        {
                            string value = key!.GetValue(name)?.ToString() ?? string.Empty;
                            string id = value.Split(';')[0].Trim();
                            if (id.Length > 0) forced[$"{browser}|{id}"] = (browser, value.Contains(';') ? value[(value.IndexOf(';') + 1)..] : string.Empty);
                        }
                    }
                }
            }

            foreach (var (browser, userData) in ChromiumBrowsers)
            {
                string root = Path.Combine(local, userData);
                if (!Directory.Exists(root)) continue;
                foreach (string profile in SafeDirectories(root).Where(d => File.Exists(Path.Combine(d, "Preferences")) || Directory.Exists(Path.Combine(d, "Extensions"))))
                    list.AddRange(ReadChromiumProfile(browser, Path.GetFileName(profile), Path.Combine(profile, "Extensions"), forced));
            }
            string opera = Path.Combine(roaming, @"Opera Software\Opera Stable\Extensions");
            if (Directory.Exists(opera)) list.AddRange(ReadChromiumProfile("Opera", "Varsayılan", opera, forced));

            // Politikada olup diskte bulunmayan (henüz indirilmemiş) zorunlu eklentiler de listelenir.
            foreach (var (key, (browser, url)) in forced)
            {
                string id = key[(key.IndexOf('|') + 1)..];
                if (list.Any(i => i.RawCommand.EndsWith(id, StringComparison.OrdinalIgnoreCase) && i.Name.Contains(browser, StringComparison.Ordinal))) continue;
                list.Add(ExtensionItem(browser, $"Zorunlu eklenti {id}", string.Empty, $"{browser} politika: {id}", id, true, url, Array.Empty<string>()));
            }

            // Firefox
            string profilesRoot = Path.Combine(roaming, @"Mozilla\Firefox\Profiles");
            foreach (string profile in SafeDirectories(profilesRoot))
            {
                string json = Path.Combine(profile, "extensions.json");
                if (!File.Exists(json)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(json));
                    if (!doc.RootElement.TryGetProperty("addons", out var addons)) continue;
                    foreach (var addon in addons.EnumerateArray())
                    {
                        if (Str(addon, "type") != "extension") continue;
                        string location = Str(addon, "location");
                        if (location is "app-builtin" or "app-system-defaults" or "app-system-addons") continue;
                        string id = Str(addon, "id");
                        string name = addon.TryGetProperty("defaultLocale", out var loc) ? Str(loc, "name") : id;
                        var perms = new List<string>();
                        if (addon.TryGetProperty("userPermissions", out var up))
                        {
                            foreach (string arr in new[] { "permissions", "origins" })
                                if (up.TryGetProperty(arr, out var a) && a.ValueKind == JsonValueKind.Array)
                                    perms.AddRange(a.EnumerateArray().Select(e => e.GetString() ?? string.Empty));
                        }
                        var item = ExtensionItem("Firefox", name.Length > 0 ? name : id, Str(addon, "path"), $"Firefox profili: {Path.GetFileName(profile)}", id, location.StartsWith("app-system", StringComparison.Ordinal), string.Empty, perms);
                        item.IsEnabled = addon.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True;
                        list.Add(item);
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
                {
                    AppLog.Debug($"Firefox eklentileri okunamadı: {profile} — {ex.Message}", nameof(AutorunsScannerEngine));
                }
            }
            return list;
        }

        private IEnumerable<PersistenceItem> ReadChromiumProfile(string browser, string profile, string extensionsDir, Dictionary<string, (string Browser, string UpdateUrl)> forced)
        {
            foreach (string idDir in SafeDirectories(extensionsDir))
            {
                string id = Path.GetFileName(idDir);
                if (id.Equals("Temp", StringComparison.OrdinalIgnoreCase)) continue;
                string? versionDir = SafeDirectories(idDir).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                string manifestPath = versionDir != null ? Path.Combine(versionDir, "manifest.json") : string.Empty;
                if (!File.Exists(manifestPath)) continue;

                PersistenceItem? item = null;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                    var m = doc.RootElement;
                    string name = LocalizeChromiumName(Str(m, "name"), versionDir!, Str(m, "default_locale"));
                    var perms = new List<string>();
                    foreach (string arr in new[] { "permissions", "host_permissions", "optional_permissions" })
                        if (m.TryGetProperty(arr, out var a) && a.ValueKind == JsonValueKind.Array)
                            perms.AddRange(a.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!));
                    bool isForced = forced.TryGetValue($"{browser}|{id}", out var policy);
                    item = ExtensionItem(browser, name.Length > 0 ? name : id, manifestPath, $"{browser} · {profile}", id, isForced, policy.UpdateUrl ?? string.Empty, perms);
                    item.Arguments = Str(m, "version");
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
                {
                    AppLog.Debug($"Eklenti bildirimi okunamadı: {manifestPath} — {ex.Message}", nameof(AutorunsScannerEngine));
                }
                if (item != null) yield return item;
            }
        }

        private static PersistenceItem ExtensionItem(string browser, string name, string path, string source, string id, bool forcedByPolicy, string updateUrl, IReadOnlyCollection<string> permissions)
        {
            var assessment = ExtensionRules.Evaluate(forcedByPolicy, updateUrl, permissions);
            return new PersistenceItem
            {
                Name = $"{name} ({browser})",
                FilePath = path,
                RawCommand = id,
                Category = PersistenceCategory.BrowserExtension,
                CategoryDisplayName = forcedByPolicy ? "Tarayıcı eklentisi (zorunlu)" : "Tarayıcı eklentisi",
                LocationSource = source,
                SignatureApplicable = false,
                CollectorRisk = assessment.Risk,
                CollectorNotes = assessment.Notes,
                Publisher = forcedByPolicy ? "Kurumsal/sistem politikası" : "Tarayıcı mağazası"
            };
        }

        private static string LocalizeChromiumName(string name, string versionDir, string defaultLocale)
        {
            if (ExtensionRules.MessageKey(name) is not { } key) return name;
            foreach (string locale in new[] { "tr", defaultLocale, "en", "en_US" }.Where(l => l.Length > 0).Distinct())
            {
                string messages = Path.Combine(versionDir, "_locales", locale, "messages.json");
                if (!File.Exists(messages)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(messages));
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && prop.Value.TryGetProperty("message", out var msg))
                            return msg.GetString() ?? name;
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    AppLog.Debug($"Eklenti dil dosyası okunamadı: {messages}", nameof(AutorunsScannerEngine));
                }
            }
            return name;
        }

        private static string Str(JsonElement e, string property) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

        private static IEnumerable<string> SafeDirectories(string path)
        {
            try
            {
                return Directory.Exists(path) ? Directory.GetDirectories(path) : Array.Empty<string>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Debug($"Klasör okunamadı: {path} — {ex.Message}", nameof(AutorunsScannerEngine));
                return Array.Empty<string>();
            }
        }

        #endregion

        #region Office eklentileri

        private static readonly string[] OfficeApps = { "Word", "Excel", "Outlook", "PowerPoint", "Access", "OneNote", "Visio", "MS Project", "Publisher" };

        private List<PersistenceItem> CollectOfficeAddins(ResolverEnvironment env)
        {
            var list = new List<PersistenceItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (hive, hiveName) in new[] { (RegistryHive.CurrentUser, "HKCU"), (RegistryHive.LocalMachine, "HKLM") })
            {
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    if (hive == RegistryHive.CurrentUser && view == RegistryView.Registry32) continue;
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    foreach (string app in OfficeApps)
                    {
                        using var addins = root.OpenSubKey($@"Software\Microsoft\Office\{app}\Addins");
                        foreach (string progId in addins?.GetSubKeyNames() ?? Array.Empty<string>())
                        {
                            // Office anahtarı 64 ve 32 bit görünümde paylaşılabilir: aynı eklenti bir kez.
                            if (!seen.Add($"{hiveName}|{app}|{progId}")) continue;
                            using var key = addins!.OpenSubKey(progId);
                            if (key == null) continue;
                            int behavior = key.GetValue("LoadBehavior") is int b ? b : 0;
                            string friendly = key.GetValue("FriendlyName") as string ?? progId;
                            string manifest = key.GetValue("Manifest") as string ?? string.Empty;
                            string target;
                            if (manifest.Length > 0)
                            {
                                // VSTO: "file:///C:/…/Eklenti.vsto|vstolocal"
                                target = manifest.Split('|')[0];
                                if (target.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) target = Uri.UnescapeDataString(target[8..]).Replace('/', '\\');
                            }
                            else
                            {
                                using var clsidKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\CLSID");
                                target = clsidKey?.GetValue(null) is string clsid ? ReadComServer(clsid) ?? string.Empty : string.Empty;
                            }

                            var item = CreateItem($"{friendly} ({app})", target.Contains(' ') && !target.StartsWith('"') ? $"\"{target}\"" : target,
                                PersistenceCategory.OfficeAddin, manifest.Length > 0 ? "Office eklentisi (VSTO)" : "Office eklentisi (COM)",
                                $@"{hiveName}\…\Office\{app}\Addins\{progId}", env);
                            item.IsEnabled = (behavior & 1) != 0;
                            if (target.Length == 0) item.RawCommand = $"{progId} (COM kaydı bulunamadı; paketlenmiş eklenti olabilir)";
                            if (manifest.Length > 0) item.SignatureApplicable = false;
                            list.Add(item);
                        }
                    }
                }
            }
            return list;
        }

        #endregion
    }
}
