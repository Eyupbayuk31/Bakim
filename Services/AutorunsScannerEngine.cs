using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Bakım.Core.Analyzer;
using Bakım.Core.Safety;
using Bakım.Core.Startup;
using Bakım.Helpers;
using Bakım.Models;

namespace Bakım.Services
{
    /// <summary>
    /// Kalıcılık (autorun) tarayıcısı — Analizör v3 Faz 0.
    ///
    /// Akış: toplayıcılar ham komutu okur → <see cref="CommandLineResolver"/> Windows'un çözdüğü
    /// gibi çözer (tırnaksız boşluklu yol, \SystemRoot, göreli ad, sarmalayıcılar) → dosya bilgisi
    /// paralel ve önbellekli zenginleştirilir (katalog dahil imza) → eylemler
    /// <see cref="AutorunActionPolicy"/>'den geçer.
    ///
    /// Tüm tarama arka plan iş parçacığında yürür (eskiden Run/Başlangıç/Winlogon/Kabuk UI iş
    /// parçacığında imza + SHA-256 + simge hesaplıyordu).
    /// </summary>
    public partial class AutorunsScannerEngine : IAutorunsScannerEngine
    {
        private readonly IVirusTotalCheckService _virusTotalService;
        private readonly DisabledServiceStore _disabledServices = new();

        /// <summary>Tek tek hash'lenen en büyük dosya; daha büyükleri istenince (VT, kopyala) hesaplanır.</summary>
        private const long MaxEagerHashBytes = 100L * 1024 * 1024;

        public AutorunsScannerEngine(IVirusTotalCheckService virusTotalService)
        {
            _virusTotalService = virusTotalService;
        }

        public async IAsyncEnumerable<PersistenceItem> ScanAllAsync(IProgress<string>? progress = null)
        {
            var env = ResolverEnvironment.ForCurrentSystem();
            var collectors = new (string Status, Func<ResolverEnvironment, List<PersistenceItem>> Collect)[]
            {
                ("Kayıt defteri başlangıç anahtarları taranıyor…", CollectRunKeys),
                ("Başlangıç klasörleri taranıyor…", CollectStartupFolders),
                ("Winlogon ve IFEO taranıyor…", CollectWinlogonAndIfeo),
                ("Zamanlanmış görevler taranıyor…", CollectScheduledTasks),
                ("Hizmetler ve sürücüler taranıyor…", CollectServicesAndDrivers),
                ("WMI abonelikleri taranıyor…", CollectWmiConsumers),
                ("Gezgin eklentileri taranıyor…", CollectShellExtensions),
                ("Oturum açılışı kancaları taranıyor…", CollectLogonHooks),
                ("Sistem kancaları (LSA, Winsock, AppInit …) taranıyor…", CollectSystemHooks),
                ("Gezgin kancaları taranıyor…", CollectExplorerHooks),
                ("Tarayıcı eklentileri taranıyor…", CollectBrowserExtensions),
                ("Office eklentileri taranıyor…", CollectOfficeAddins),
            };

            foreach (var (status, collect) in collectors)
            {
                progress?.Report(status);
                var items = await Task.Run(() =>
                {
                    List<PersistenceItem> list;
                    try
                    {
                        list = collect(env);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                    {
                        // Tek bir konumun hatası taramanın geri kalanını düşürmez.
                        AppLog.Warning($"Kalıcılık toplayıcısı başarısız: {status}", ex, nameof(AutorunsScannerEngine));
                        return new List<PersistenceItem>();
                    }
                    Parallel.ForEach(list, new ParallelOptions { MaxDegreeOfParallelism = 4 }, Enrich);
                    return list;
                });

                foreach (var item in items) yield return item;
            }
        }

        #region Toplayıcılar

        private PersistenceItem CreateItem(string name, string rawCommand, PersistenceCategory category, string categoryDisplay, string source, ResolverEnvironment env)
        {
            var resolved = CommandLineResolver.Resolve(rawCommand, env);
            return new PersistenceItem
            {
                Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(resolved.EffectiveTarget) : name,
                RawCommand = rawCommand,
                FilePath = resolved.EffectiveTarget,
                Arguments = resolved.Arguments,
                HostName = resolved.HostName,
                InlineScript = resolved.InlineScript,
                Resolution = resolved.IsWrapped && !resolved.PayloadFound && resolved.Payload != null ? TargetResolution.NotFound : resolved.Resolution,
                Category = category,
                CategoryDisplayName = categoryDisplay,
                LocationSource = source
            };
        }

        private static readonly (RegistryHive Hive, string SubKey, string Display)[] RunKeys =
        {
            (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", @"HKCU\…\Run"),
            (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", @"HKCU\…\RunOnce"),
            (RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", @"HKLM\…\Run"),
            (RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", @"HKLM\…\RunOnce"),
            (RegistryHive.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", @"HKLM\WOW6432Node\…\Run"),
            (RegistryHive.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce", @"HKLM\WOW6432Node\…\RunOnce"),
            (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", @"HKCU\Policies\Explorer\Run"),
            (RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", @"HKLM\Policies\Explorer\Run"),
        };

        private List<PersistenceItem> CollectRunKeys(ResolverEnvironment env)
        {
            var list = new List<PersistenceItem>();
            foreach (var target in RunKeys)
            {
                using var baseKey = RegistryKey.OpenBaseKey(target.Hive, RegistryView.Registry64);
                using var subKey = baseKey.OpenSubKey(target.SubKey);
                if (subKey == null) continue;
                bool approvable = AutorunActionPolicy.IsApprovableRunKey(target.SubKey);
                using var approvedKey = approvable ? baseKey.OpenSubKey(StartupApprovedPaths.ForRunKey(target.SubKey)) : null;

                foreach (var valueName in subKey.GetValueNames())
                {
                    string rawCmd = subKey.GetValue(valueName)?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(rawCmd)) continue;

                    // Eski sürümler devre dışı bırakmak için değeri "Bakim_Disabled_X" diye yeniden
                    // adlandırıyordu; Windows Run altındaki HER değeri çalıştırdığı için bu etkisizdi.
                    // Gerçek durum StartupApproved'dan okunur; eski adlar olduğu gibi (etkin) gösterilir.
                    bool isEnabled = !approvable || StartupApprovedPaths.IsEnabled(approvedKey?.GetValue(valueName) as byte[]);
                    string cleanName = valueName.StartsWith(LegacyDisabledPrefix, StringComparison.Ordinal)
                        ? valueName[LegacyDisabledPrefix.Length..]
                        : valueName;

                    var item = CreateItem(cleanName, rawCmd, PersistenceCategory.RegistryRun, "Kayıt Defteri (Run)", $"{target.Display} → {valueName}", env);
                    item.IsEnabled = isEnabled;
                    item.RegistryKeyPath = (target.Hive == RegistryHive.LocalMachine ? "HKLM\\" : "HKCU\\") + target.SubKey;
                    item.RegistryValueName = valueName;
                    list.Add(item);
                }
            }
            return list;
        }

        private const string LegacyDisabledPrefix = "Bakim_Disabled_";
        private const string LegacyDisabledFileSuffix = ".bakim_disabled";

        private List<PersistenceItem> CollectStartupFolders(ResolverEnvironment env)
        {
            var list = new List<PersistenceItem>();
            string userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);

            foreach (var (folder, display) in new[] { (userStartup, "Kullanıcı başlangıç klasörü"), (commonStartup, "Ortak başlangıç klasörü") })
            {
                if (!Directory.Exists(folder)) continue;
                string[] files;
                try
                {
                    files = Directory.GetFiles(folder);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppLog.Debug($"Başlangıç klasörü okunamadı: {folder} — {ex.Message}", nameof(AutorunsScannerEngine));
                    continue;
                }

                foreach (var file in files)
                {
                    string fileName = Path.GetFileName(file);
                    if (string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                    bool legacyDisabled = fileName.EndsWith(LegacyDisabledFileSuffix, StringComparison.OrdinalIgnoreCase);
                    bool isEnabled = !legacyDisabled && StartupApprovedPaths.IsEnabled(ReadStartupFolderApproval(folder == commonStartup, fileName));
                    string target = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ResolveShortcutTarget(file) ?? file : file;

                    var item = CreateItem(
                        Path.GetFileNameWithoutExtension(fileName.Replace(LegacyDisabledFileSuffix, "", StringComparison.OrdinalIgnoreCase)),
                        target.Contains(' ') ? $"\"{target}\"" : target,
                        PersistenceCategory.StartupFolder, "Başlangıç Klasörü", display, env);
                    item.IsEnabled = isEnabled;
                    item.SourceFilePath = file;
                    list.Add(item);
                }
            }
            return list;
        }

        private static byte[]? ReadStartupFolderApproval(bool common, string fileName)
        {
            try
            {
                using var key = (common ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(StartupApprovedPaths.ForStartupFolder);
                return key?.GetValue(fileName) as byte[];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                AppLog.Debug($"StartupApproved okunamadı: {ex.Message}", nameof(AutorunsScannerEngine));
                return null;
            }
        }

        private List<PersistenceItem> CollectWinlogonAndIfeo(ResolverEnvironment env)
        {
            var list = new List<PersistenceItem>();
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

            // Winlogon: Userinit virgülle ayrılmış birden çok program içerebilir; zararlılar sona ekler.
            using (var winlogon = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"))
            {
                foreach (string val in new[] { "Shell", "Userinit", "Taskman" })
                {
                    string raw = winlogon?.GetValue(val)?.ToString() ?? string.Empty;
                    var parts = val == "Userinit" ? raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : new[] { raw.Trim() };
                    foreach (string part in parts.Where(p => p.Length > 0))
                        list.Add(CreateItem($"Winlogon: {val}", part, PersistenceCategory.WinlogonIfeo, "Winlogon", $@"HKLM\…\Winlogon\{val}", env));
                }
            }

            // IFEO: Debugger ele geçirmesi (64 ve 32 bit görünüm) + SilentProcessExit izleyicisi.
            foreach (var (path, suffix) in new[]
                     {
                         (@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", ""),
                         (@"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", " (32 bit)")
                     })
            {
                using var ifeo = hklm.OpenSubKey(path);
                if (ifeo == null) continue;
                foreach (var subName in ifeo.GetSubKeyNames())
                {
                    string debugger = ReadValueSafe(ifeo, subName, "Debugger");
                    if (!string.IsNullOrWhiteSpace(debugger))
                        list.Add(CreateItem($"IFEO: {subName}{suffix}", debugger, PersistenceCategory.WinlogonIfeo, "IFEO Debugger", $@"HKLM\…\IFEO\{subName}\Debugger", env));
                }
            }

            using (var spe = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SilentProcessExit"))
            {
                foreach (var subName in spe?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    string monitor = ReadValueSafe(spe!, subName, "MonitorProcess");
                    if (!string.IsNullOrWhiteSpace(monitor))
                        list.Add(CreateItem($"SilentProcessExit: {subName}", monitor, PersistenceCategory.WinlogonIfeo, "IFEO SilentProcessExit", $@"HKLM\…\SilentProcessExit\{subName}\MonitorProcess", env));
                }
            }
            return list;
        }

        /// <summary>Tek bir alt anahtarı okur; erişilemeyen anahtar (korumalı IFEO girdileri) tüm grubu düşürmez.</summary>
        private static string ReadValueSafe(RegistryKey parent, string subKey, string valueName)
        {
            try
            {
                using var key = parent.OpenSubKey(subKey);
                return key?.GetValue(valueName)?.ToString() ?? string.Empty;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                AppLog.Debug($"Anahtar okunamadı: {subKey} — {ex.Message}", nameof(AutorunsScannerEngine));
                return string.Empty;
            }
        }

        /// <summary>
        /// Görevler Görev Zamanlayıcı COM arayüzünden okunur (ANL Z7). Eskiden schtasks CSV'si yanlış
        /// kod sayfasıyla okunuyor, başlık satırları görev sanılıyor ve "Devre Dışı" tanınmadığı için
        /// bütün görevler etkin görünüyordu.
        /// </summary>
        private List<PersistenceItem> CollectScheduledTasks(ResolverEnvironment env)
        {
            var list = new List<PersistenceItem>();
            foreach (var task in TaskSchedulerReader.ReadAll())
            {
                var actions = task.Actions ?? Array.Empty<TaskAction>();
                int index = 0;
                foreach (var action in actions)
                {
                    index++;
                    string path = action.Path.Trim();
                    string raw = path.StartsWith('"') || !path.Contains(' ') ? path : $"\"{path}\"";
                    if (action.Arguments.Length > 0) raw += " " + action.Arguments;
                    list.Add(TaskItem(task, actions.Count > 1 ? $"{task.Name} ({index})" : task.Name, raw, env));
                }
                foreach (string clsid in task.ComHandlers ?? Array.Empty<string>())
                {
                    string? dll = ReadComServer(clsid);
                    var item = TaskItem(task, task.Name, dll ?? string.Empty, env);
                    item.HostName ??= "taskhostw";
                    if (dll == null)
                    {
                        // Paketlenmiş (Mağaza) COM sunucuları klasik CLSID kaydında yer almaz: yetim değil, çözümlenemedi.
                        item.RawCommand = $"COM işleyicisi {clsid}";
                        item.InlineScript = item.RawCommand;
                    }
                    list.Add(item);
                }
            }
            return list;
        }

        private PersistenceItem TaskItem(ScheduledTaskInfo task, string name, string raw, ResolverEnvironment env)
        {
            var item = CreateItem(name, raw, PersistenceCategory.ScheduledTask,
                task.Hidden ? "Zamanlanmış Görev (gizli)" : "Zamanlanmış Görev", $"Görev: {task.Path}", env);
            item.TaskPath = task.Path;
            item.IsEnabled = task.Enabled;
            return item;
        }

        private List<PersistenceItem> CollectServicesAndDrivers(ResolverEnvironment env)
        {
            var list = new List<PersistenceItem>();
            var disabledByBakim = _disabledServices.Load();
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var root = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (root == null) return list;

            foreach (var serviceName in root.GetSubKeyNames())
            {
                try
                {
                    using var key = root.OpenSubKey(serviceName);
                    if (key == null) continue;
                    int start = key.GetValue("Start") is int s ? s : 3;
                    int type = key.GetValue("Type") is int t ? t : 0;
                    bool bakimDisabled = start == 4 && disabledByBakim.ContainsKey(serviceName);
                    if (start > 2 && !bakimDisabled) continue;

                    string imagePath = key.GetValue("ImagePath", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(imagePath)) continue;

                    bool driver = AutorunActionPolicy.IsDriverType(type);
                    string displayName = LoadIndirectString(key.GetValue("DisplayName")?.ToString()) ?? serviceName;
                    string startText = start switch { 0 => "Açılış", 1 => "Sistem", 2 => "Otomatik", 4 => "Devre dışı", _ => "El ile" };

                    var item = CreateItem(displayName, imagePath,
                        driver ? PersistenceCategory.Driver : PersistenceCategory.WindowsService,
                        driver ? "Sürücü" : "Windows Hizmeti",
                        $"{(driver ? "Sürücü" : "Hizmet")}: {serviceName} ({startText})", env);

                    // svchost ile barınan hizmetin asıl yükü ServiceDll'dir (AN-07).
                    string? serviceDll = ReadServiceDll(key);
                    if (!string.IsNullOrWhiteSpace(serviceDll))
                    {
                        string raw = CommandLineResolver.NormalizeLeadingPath(Environment.ExpandEnvironmentVariables(serviceDll), env.WindowsDirectory);
                        string? located = CommandLineResolver.Locate(raw, env);
                        item.HostName = "svchost";
                        item.FilePath = located ?? raw;
                        item.Resolution = located != null ? TargetResolution.Resolved : TargetResolution.NotFound;
                    }

                    item.ServiceName = serviceName;
                    item.ServiceStart = start;
                    item.ServiceType = type;
                    item.DisabledByBakim = bakimDisabled;
                    item.IsEnabled = start <= 2;
                    list.Add(item);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    AppLog.Debug($"Hizmet okunamadı: {serviceName} — {ex.Message}", nameof(AutorunsScannerEngine));
                }
            }
            return list;
        }

        private static string? ReadServiceDll(RegistryKey serviceKey)
        {
            using var parameters = serviceKey.OpenSubKey("Parameters");
            return parameters?.GetValue("ServiceDll", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string
                   ?? serviceKey.GetValue("ServiceDll", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }

        /// <summary>WMI kalıcılığı: komut satırı ve betik tüketicileri (ActiveScript en yaygın zararlı biçimi).</summary>
        private List<PersistenceItem> CollectWmiConsumers(ResolverEnvironment env)
        {
            var list = new List<PersistenceItem>();
            var scope = new ManagementScope(@"\\.\root\subscription");
            scope.Connect();

            using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM CommandLineEventConsumer")))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    using (obj)
                    {
                        string name = obj["Name"]?.ToString() ?? "WMI tüketicisi";
                        string cmd = obj["CommandLineTemplate"]?.ToString() ?? obj["ExecutablePath"]?.ToString() ?? string.Empty;
                        list.Add(CreateItem($"WMI: {name}", cmd, PersistenceCategory.WmiEventConsumer, "WMI Kalıcılığı", @"root\subscription\CommandLineEventConsumer", env));
                    }
                }
            }

            using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM ActiveScriptEventConsumer")))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    using (obj)
                    {
                        string name = obj["Name"]?.ToString() ?? "WMI betiği";
                        string file = obj["ScriptFileName"]?.ToString() ?? string.Empty;
                        string text = obj["ScriptText"]?.ToString() ?? string.Empty;
                        string engine = obj["ScriptingEngine"]?.ToString() ?? "Betik";
                        var item = CreateItem($"WMI: {name}", file, PersistenceCategory.WmiEventConsumer, $"WMI Betiği ({engine})", @"root\subscription\ActiveScriptEventConsumer", env);
                        if (text.Length > 0)
                        {
                            item.InlineScript = text;
                            item.RawCommand = $"{engine}: {text[..Math.Min(200, text.Length)]}";
                            if (file.Length == 0) item.Resolution = TargetResolution.Resolved;
                        }
                        list.Add(item);
                    }
                }
            }
            return list;
        }

        private List<PersistenceItem> CollectShellExtensions(ResolverEnvironment env)
        {
            var list = new List<PersistenceItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in new[] { @"*\shellex\ContextMenuHandlers", @"Directory\shellex\ContextMenuHandlers", @"Folder\shellex\ContextMenuHandlers" })
            {
                using var key = Registry.ClassesRoot.OpenSubKey(p);
                if (key == null) continue;

                foreach (var handlerName in key.GetSubKeyNames())
                {
                    using var hKey = key.OpenSubKey(handlerName);
                    string clsid = hKey?.GetValue(null)?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(clsid)) clsid = handlerName;
                    if (!clsid.StartsWith('{')) continue;
                    // Aynı işleyici *, Directory ve Folder altında tekrar kayıtlıdır (AN-24).
                    if (!seen.Add(clsid)) continue;

                    string? dll = ReadComServer(clsid);
                    if (string.IsNullOrWhiteSpace(dll)) continue;
                    list.Add(CreateItem(handlerName, dll.Contains(' ') && !dll.StartsWith('"') ? $"\"{dll}\"" : dll,
                        PersistenceCategory.ShellExtension, "Gezgin Eklentisi", $@"HKCR\{p}\{handlerName}", env));
                }
            }
            return list;
        }

        /// <summary>CLSID → sunucu yolu: 64 bit, ardından 32 bit (WOW6432Node) kayıt; süreç içi ve süreç dışı.</summary>
        private static string? ReadComServer(string clsid)
        {
            foreach (string prefix in new[] { "CLSID", @"WOW6432Node\CLSID" })
            {
                foreach (string server in new[] { "InprocServer32", "LocalServer32" })
                {
                    using var key = Registry.ClassesRoot.OpenSubKey($@"{prefix}\{clsid}\{server}");
                    if (key?.GetValue(null)?.ToString() is { Length: > 0 } path) return path;
                }
            }
            return null;
        }

        #endregion

        #region Zenginleştirme (önbellekli)

        private sealed record FileFacts(SignatureStatus Status, string Signer, string Sha256, ImageSource? Icon, DateTime? CreatedUtc);

        /// <summary>(yol, boyut, değişim zamanı) → dosya bilgisi. Yeniden taramalarda imza ve hash tekrar hesaplanmaz.</summary>
        private static readonly ConcurrentDictionary<(string Path, long Size, long Ticks), FileFacts> FactsCache = new();

        private void Enrich(PersistenceItem item)
        {
            string path = item.FilePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                // İncelenecek dosya yok: imza hükmü verilemez ("imzasız" puanı uygulanmaz).
                item.SignatureApplicable = false;
                item.HasValidFile = false;
                item.Publisher = item.InlineScript != null ? "Satır içi betik" : "Komut boş";
                return;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or UnauthorizedAccessException)
            {
                info = null!;
                AppLog.Debug($"Geçersiz yol: {path} — {ex.Message}", nameof(AutorunsScannerEngine));
            }

            if (info == null || !info.Exists)
            {
                item.HasValidFile = false;
                item.Resolution = TargetResolution.NotFound;
                item.Signature = SignatureStatus.Unsigned;
                item.Publisher = "Dosya bulunamadı (yetim kayıt)";
                return;
            }

            item.HasValidFile = true;
            var key = (path.ToLowerInvariant(), info.Length, info.LastWriteTimeUtc.Ticks);
            var facts = FactsCache.GetOrAdd(key, _ => ReadFacts(path, info));
            if (FactsCache.Count > 4096) FactsCache.Clear();

            item.Signature = facts.Status;
            item.SignatureSignerName = facts.Signer;
            // İmzanın anlamsız olduğu girdilerde (eklenti, betik) toplayıcının yayıncı etiketi korunur.
            if (!item.SignatureApplicable && !string.IsNullOrEmpty(item.Publisher)) { item.Sha256Hash = facts.Sha256; item.FileCreatedUtc = facts.CreatedUtc; return; }
            item.Publisher = facts.Status switch
            {
                SignatureStatus.Verified => facts.Signer.Length > 0 ? facts.Signer : "İmzalı",
                SignatureStatus.InvalidOrTampered => facts.Signer.Length > 0 ? $"{facts.Signer} (geçersiz imza)" : "Geçersiz imza",
                _ => "İmzasız"
            };
            // Gizleme yalnızca DOĞRULANMIŞ Microsoft imzasına dayanır (ANL Z2): eskiden yol C:\Windows
            // altındaysa ya da (geçersiz) sertifika adında "Microsoft" geçiyorsa gizleniyordu.
            item.IsMicrosoft = facts.Status == SignatureStatus.Verified && IsMicrosoftSigner(facts.Signer);
            item.Sha256Hash = facts.Sha256;
            item.IconSource = facts.Icon;
            item.FileCreatedUtc = facts.CreatedUtc;
        }

        public static bool IsMicrosoftSigner(string? signer) =>
            !string.IsNullOrWhiteSpace(signer) && signer.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase);

        private FileFacts ReadFacts(string path, FileInfo info)
        {
            var signature = SignatureInspector.Inspect(path);
            string hash = info.Length <= MaxEagerHashBytes ? _virusTotalService.ComputeSha256(path) : string.Empty;
            DateTime? created = null;
            try
            {
                created = info.CreationTimeUtc;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Debug($"Oluşturma zamanı okunamadı: {path}", nameof(AutorunsScannerEngine));
            }
            return new FileFacts(signature.Status, signature.Signer ?? string.Empty, hash, ExtractFileIcon(path), created);
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

        /// <summary>"@%systemroot%\system32\spoolsv.exe,-1" gibi kaynak dizelerini okunur ada çevirir.</summary>
        private static string? LoadIndirectString(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || !value.StartsWith('@')) return value;
            var sb = new StringBuilder(512);
            return SHLoadIndirectString(value, sb, sb.Capacity, IntPtr.Zero) == 0 && sb.Length > 0 ? sb.ToString() : value;
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private static ImageSource? ExtractFileIcon(string filePath)
        {
            try
            {
                using var ico = Icon.ExtractAssociatedIcon(filePath);
                if (ico == null) return null;
                using var bmp = ico.ToBitmap();
                var hBmp = bmp.GetHbitmap();
                try
                {
                    var wpfBmp = Imaging.CreateBitmapSourceFromHBitmap(hBmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    wpfBmp.Freeze();
                    return wpfBmp;
                }
                finally
                {
                    DeleteObject(hBmp);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or ExternalException or UnauthorizedAccessException)
            {
                AppLog.Debug($"Simge okunamadı: {filePath}", nameof(AutorunsScannerEngine));
                return null;
            }
        }

        private static string? ResolveShortcutTarget(string shortcutPath) => ShellLink.ResolveTarget(shortcutPath);

        #endregion

        #region Eylemler

        /// <summary>
        /// Girdiyi Görev Yöneticisi ile aynı yöntemle etkinleştirir/devre dışı bırakır. Her eylem önce
        /// <see cref="AutorunActionPolicy"/>'den geçer: sürücüler, açılış/sistem hizmetleri, kritik
        /// hizmetler ve Windows görevleri hiçbir koşulda değiştirilmez (ANL Z1).
        /// </summary>
        public async Task<bool> ToggleItemAsync(PersistenceItem item, bool enable)
        {
            var decision = AutorunActionPolicy.CanToggle(item.Facts);
            if (!decision.Allowed)
            {
                AppLog.Warning($"Kalıcılık eylemi politika gereği reddedildi: {item.Name} — {decision.Reason}", null, nameof(AutorunsScannerEngine));
                return false;
            }

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
                        var result = await ElevatedPowerShell.RunAsync(
                            $"schtasks.exe /Change /TN {ElevatedPowerShell.Quote(item.TaskPath!)} {(enable ? "/Enable" : "/Disable")}; exit $LASTEXITCODE",
                            TimeSpan.FromSeconds(30));
                        return result.Succeeded;
                    }

                    case PersistenceCategory.WindowsService:
                        return await ToggleServiceAsync(item, enable);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or ArgumentException)
            {
                AppLog.Warning($"Kalıcılık girdisi değiştirilemedi: {item.Name}", ex, nameof(AutorunsScannerEngine));
            }
            return false;
        }

        /// <summary>
        /// Hizmeti kapatırken özgün başlangıç türü (Start + DelayedAutostart) saklanır; açarken geri
        /// yüklenir. Eskiden açma her zaman "Automatic" yazıyordu.
        /// </summary>
        private async Task<bool> ToggleServiceAsync(PersistenceItem item, bool enable)
        {
            string name = item.ServiceName!;
            string mode;
            if (enable)
            {
                var saved = _disabledServices.Load().GetValueOrDefault(name);
                var (start, delayed) = AutorunActionPolicy.RestoreStartType(saved?.Start, saved?.Delayed ?? false);
                mode = start == 3 ? "demand" : delayed ? "delayed-auto" : "auto";
            }
            else
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
                int start = key?.GetValue("Start") is int s ? s : 2;
                bool delayed = key?.GetValue("DelayedAutostart") is int d && d == 1;
                _disabledServices.Save(name, new DisabledServiceStore.Entry(start, delayed, DateTime.UtcNow));
                mode = "disabled";
            }

            var result = await ElevatedPowerShell.RunAsync(
                $"& sc.exe config {ElevatedPowerShell.Quote(name)} start= {mode} | Out-Null; exit $LASTEXITCODE",
                TimeSpan.FromSeconds(30));
            if (!result.Succeeded)
            {
                if (!enable) _disabledServices.Remove(name);
                return false;
            }

            if (enable)
            {
                _disabledServices.Remove(name);
                item.ServiceStart = mode == "demand" ? 3 : 2;
                item.DisabledByBakim = false;
            }
            else
            {
                item.ServiceStart = 4;
                item.DisabledByBakim = true;
            }
            return true;
        }

        private static bool ToggleRegistryItem(PersistenceItem item, bool enable)
        {
            if (string.IsNullOrEmpty(item.RegistryKeyPath) || string.IsNullOrEmpty(item.RegistryValueName)) return false;
            var root = item.RegistryKeyPath.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) ? Registry.LocalMachine : Registry.CurrentUser;
            string subKey = item.RegistryKeyPath[5..];

            // RunOnce ve politika anahtarları StartupApproved'u dikkate almaz: devre dışı bırakılamaz, silinebilir.
            if (!AutorunActionPolicy.IsApprovableRunKey(subKey)) return false;

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
        /// başlangıç klasörü kısayolu Geri Dönüşüm Kutusu'na gider. Hizmet, sürücü, WMI, Winlogon ve
        /// Windows görevleri politika gereği silinmez.
        /// </summary>
        public async Task<bool> DeleteItemAsync(PersistenceItem item)
        {
            var decision = AutorunActionPolicy.CanDelete(item.Facts);
            if (!decision.Allowed)
            {
                AppLog.Warning($"Kalıcılık silme politika gereği reddedildi: {item.Name} — {decision.Reason}", null, nameof(AutorunsScannerEngine));
                return false;
            }

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

                        if (AutorunActionPolicy.IsApprovableRunKey(subKey))
                            VerifiedRegistry.DeleteValue(hklm ? Registry.LocalMachine : Registry.CurrentUser, StartupApprovedPaths.ForRunKey(subKey), item.RegistryValueName);
                        return true;
                    }

                    case PersistenceCategory.ScheduledTask:
                    {
                        var result = await ElevatedPowerShell.RunAsync(
                            $"schtasks.exe /Delete /TN {ElevatedPowerShell.Quote(item.TaskPath!)} /F; exit $LASTEXITCODE",
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or ArgumentException)
            {
                AppLog.Warning($"Kalıcılık girdisi silinemedi: {item.Name}", ex, nameof(AutorunsScannerEngine));
            }
            return false;
        }

        #endregion
    }

    /// <summary>
    /// Bakım'ın kapattığı hizmetlerin özgün başlangıç türü. Hizmet yeniden açılırken buradan geri
    /// yüklenir; kayıt yoksa "Otomatik". %LocalAppData%\Bakim\History\Analyzer\disabled-services.json
    /// </summary>
    internal sealed class DisabledServiceStore
    {
        public sealed record Entry(int Start, bool Delayed, DateTime DisabledAtUtc);

        private static readonly object Gate = new();
        private readonly string _path = Path.Combine(Core.History.AnalysisHistoryStore.DefaultDirectory(), "disabled-services.json");

        public Dictionary<string, Entry> Load()
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(_path)) return new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                    var map = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_path));
                    return new Dictionary<string, Entry>(map ?? new(), StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    AppLog.Warning("Kapatılan hizmet listesi okunamadı.", ex, nameof(DisabledServiceStore));
                    return new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        public void Save(string serviceName, Entry entry) => Mutate(map => map[serviceName] = entry);

        public void Remove(string serviceName) => Mutate(map => map.Remove(serviceName));

        private void Mutate(Action<Dictionary<string, Entry>> change)
        {
            lock (Gate)
            {
                var map = Load();
                change(map);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    File.WriteAllText(_path, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppLog.Warning("Kapatılan hizmet listesi yazılamadı.", ex, nameof(DisabledServiceStore));
                }
            }
        }
    }
}
