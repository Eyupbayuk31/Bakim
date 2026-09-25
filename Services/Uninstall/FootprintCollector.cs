using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Bakım.Core.Safety;
using Bakım.Core.Text;
using Bakım.Core.Uninstall;
using Bakım.Helpers;
using Bakım.Models;
using Bakım.Services.Safety;

namespace Bakım.Services.Uninstall
{
    public interface IFootprintCollector
    {
        /// <summary>
        /// Kaldırmadan ÖNCE çağrılır (kurulum klasöründeki dosyalar hâlâ varken): kurulum klasörüne
        /// işaret eden hizmet, zamanlanmış görev, başlangıç girdisi, kısayol, güvenlik duvarı kuralı
        /// ve App Paths kayıtlarını toplar.
        /// </summary>
        Task<UninstallFootprint> CollectAsync(InstalledAppItem app, CancellationToken ct = default);

        /// <summary>Kaldırma doğrulandıktan sonra hâlâ duran izler: kesin kanıtlı kalıntılar.</summary>
        List<LeftoverItem> StillPresentLeftovers(UninstallFootprint footprint);
    }

    /// <summary>
    /// Kanıta dayalı iz toplayıcı (KAL B1). İsim benzerliği kullanılmaz: bir öğe ancak
    /// çalıştırdığı dosya bu programın kurulum klasörünün İÇİNDEYSE bu programa ait sayılır.
    /// Kurulum klasörü paylaşılıyorsa (başka bir programın klasörüyle iç içe) hiç iz toplanmaz.
    /// </summary>
    public sealed class FootprintCollector : IFootprintCollector
    {
        private static readonly TimeSpan Budget = TimeSpan.FromSeconds(25);
        private const int Certain = (int)MatchConfidence.Certain;

        internal const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";
        internal const string FirewallRulesKey = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";
        private const string AppPathsKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths";

        private readonly IUninstallerService _apps;
        private readonly PathSafetyGuard _guard;
        private readonly ILogService _log;

        public FootprintCollector(IUninstallerService apps, ILogService log)
        {
            _apps = apps;
            _log = log;
            _guard = PathSafetyGuard.Default;
        }

        public async Task<UninstallFootprint> CollectAsync(InstalledAppItem app, CancellationToken ct = default)
        {
            string? dir = FootprintMatch.InferInstallDir(app.InstallLocation, app.UninstallString, InstallRoots(), File.Exists);
            if (!FootprintMatch.IsUsableInstallDir(dir) || !Directory.Exists(dir))
            {
                _log.Info($"İz toplanmadı ({app.DisplayName}): kurulum klasörü bilinmiyor ya da paylaşılan bir kök.", nameof(FootprintCollector));
                return UninstallFootprint.Empty;
            }
            if (!_guard.CheckDeletion(dir!, isDirectory: true, allowOutsideKnownRoots: true).IsAllowed)
            {
                _log.Info($"İz toplanmadı ({app.DisplayName}): kurulum klasörü korumalı ({dir}).", nameof(FootprintCollector));
                return UninstallFootprint.Empty;
            }
            if (await SharesFolderWithOtherAppAsync(app, dir!))
            {
                _log.Info($"İz toplanmadı ({app.DisplayName}): kurulum klasörü başka bir programla iç içe ({dir}).", nameof(FootprintCollector));
                return UninstallFootprint.Empty;
            }

            return await Task.Run(() => Collect(dir!, ct), ct);
        }

        /// <summary>Kurulum klasörü kaldırıcının yerinden çıkarılırken kabul edilen kökler.</summary>
        private static IReadOnlyList<string> InstallRoots()
        {
            static string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
            return new[]
                {
                    F(Environment.SpecialFolder.ProgramFiles),
                    F(Environment.SpecialFolder.ProgramFilesX86),
                    Path.Combine(F(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                }
                .Select(WindowsPath.Normalize)
                .Where(p => p != null)
                .Select(p => p!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<bool> SharesFolderWithOtherAppAsync(InstalledAppItem target, string dir)
        {
            try
            {
                var apps = await _apps.GetInstalledAppsAsync();
                return apps
                    .Where(a => !string.Equals(a.RegistryKeyPath, target.RegistryKeyPath, StringComparison.OrdinalIgnoreCase))
                    .Where(a => !string.Equals(a.DisplayName, target.DisplayName, StringComparison.OrdinalIgnoreCase))
                    .Select(a => WindowsPath.Normalize(a.InstallLocation))
                    .Where(p => p != null && WindowsPath.Depth(p) >= 1)
                    .Any(p => WindowsPath.IsUnderOrEqual(dir, p!) || WindowsPath.IsStrictlyUnder(p!, dir));
            }
            catch (Exception ex)
            {
                // Liste alınamazsa paylaşım dışlanamaz: güvenli taraf iz toplamamaktır.
                _log.Warning("Kurulu program listesi alınamadı; iz toplanmayacak.", ex, nameof(FootprintCollector));
                return true;
            }
        }

        private UninstallFootprint Collect(string dir, CancellationToken ct)
        {
            var footprint = new UninstallFootprint(dir);
            var sw = Stopwatch.StartNew();

            void Run(string label, Action<string, List<FootprintItem>> source)
            {
                if (ct.IsCancellationRequested) return;
                if (sw.Elapsed > Budget)
                {
                    footprint.IsPartial = true;
                    _log.Info($"İz toplama zaman bütçesini aştı; atlandı: {label}", nameof(FootprintCollector));
                    return;
                }
                try
                {
                    source(dir, footprint.Items);
                }
                catch (Exception ex)
                {
                    footprint.IsPartial = true;
                    _log.Warning($"İz kaynağı okunamadı: {label}", ex, nameof(FootprintCollector));
                }
            }

            Run("hizmetler", CollectServices);
            Run("zamanlanmış görevler", CollectScheduledTasks);
            Run("başlangıç girdileri", CollectRunValues);
            Run("App Paths", CollectAppPaths);
            Run("güvenlik duvarı", CollectFirewallRules);
            Run("kısayollar", CollectShortcuts);

            var distinct = footprint.Items
                .GroupBy(i => (i.Kind, i.Target.ToUpperInvariant()))
                .Select(g => g.First())
                .ToList();
            footprint.Items.Clear();
            footprint.Items.AddRange(distinct);

            _log.Info($"İz toplandı: {dir} → {footprint.Items.Count} öğe ({sw.ElapsedMilliseconds} ms{(footprint.IsPartial ? ", eksik" : "")}).", nameof(FootprintCollector));
            return footprint;
        }

        private static bool Into(string? command, string dir) =>
            FootprintMatch.PointsInto(command, dir, File.Exists);

        #region Kaynaklar

        private static void CollectServices(string dir, List<FootprintItem> items)
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var services = baseKey.OpenSubKey(ServicesKey);
            if (services == null) return;

            foreach (string name in services.GetSubKeyNames())
            {
                using var key = TryOpen(services, name);
                if (key == null) continue;

                string image = FootprintText.NormalizeServiceImagePath(
                    key.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string);
                string? dll = null;
                using (var parameters = TryOpen(key, "Parameters"))
                    dll = parameters?.GetValue("ServiceDll", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;

                string? evidencePath = Into(image, dir) ? image : Into(dll, dir) ? dll : null;
                if (evidencePath == null) continue;

                string display = key.GetValue("DisplayName") as string ?? name;
                if (display.StartsWith('@')) display = name;
                bool driver = key.GetValue("Type") is int type && (type & 0x3) != 0;
                items.Add(new FootprintItem(FootprintKind.Service, name, display,
                    $"{(driver ? "Sürücünün" : "Hizmetin")} dosyası kurulum klasöründe: {evidencePath}"));
            }
        }

        private static void CollectScheduledTasks(string dir, List<FootprintItem> items)
        {
            object? service = CreateTaskService();
            if (service == null) return;
            try
            {
                dynamic ts = service;
                WalkTaskFolder(ts.GetFolder("\\"), dir, items, depth: 0);
            }
            finally
            {
                Marshal.ReleaseComObject(service);
            }
        }

        private static void WalkTaskFolder(dynamic folder, string dir, List<FootprintItem> items, int depth)
        {
            if (depth > 8) return;
            // 1 = TASK_ENUM_HIDDEN: gizli görevler de listelenir.
            foreach (dynamic task in folder.GetTasks(1))
            {
                try
                {
                    string path = task.Path;
                    foreach (dynamic action in task.Definition.Actions)
                    {
                        // 0 = TASK_ACTION_EXEC
                        if ((int)action.Type != 0) continue;
                        string exe = action.Path ?? string.Empty;
                        if (!Into(exe, dir)) continue;
                        items.Add(new FootprintItem(FootprintKind.ScheduledTask, path, (string)task.Name,
                            $"Görevin çalıştırdığı dosya kurulum klasöründe: {Environment.ExpandEnvironmentVariables(exe.Trim('"'))}"));
                        break;
                    }
                }
                catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    // Erişilemeyen tek görev taramayı durdurmaz (korumalı sistem görevleri).
                    AppLog.Debug($"Görev okunamadı: {ex.Message}", nameof(FootprintCollector));
                }
            }

            foreach (dynamic sub in folder.GetFolders(0))
            {
                try
                {
                    WalkTaskFolder(sub, dir, items, depth + 1);
                }
                catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
                {
                    AppLog.Debug($"Görev klasörü okunamadı: {ex.Message}", nameof(FootprintCollector));
                }
            }
        }

        private static readonly (RegistryHive Hive, RegistryView View)[] RunRoots =
        {
            (RegistryHive.CurrentUser, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
        };

        private static void CollectRunValues(string dir, List<FootprintItem> items)
        {
            foreach (var (hive, view) in RunRoots)
            {
                foreach (string sub in new[] { @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\RunOnce" })
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = TryOpen(baseKey, sub);
                    if (key == null) continue;
                    foreach (string valueName in key.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(valueName)) continue;
                        if (key.GetValue(valueName) is not string command || !Into(command, dir)) continue;
                        var path = new RegistryPath(hive, view, sub, valueName);
                        items.Add(new FootprintItem(FootprintKind.StartupEntry, path.ToString(), valueName,
                            $"Windows açılışında kurulum klasöründeki dosyayı çalıştırıyor: {command}"));
                    }
                }
            }
        }

        private static void CollectAppPaths(string dir, List<FootprintItem> items)
        {
            foreach (var (hive, view) in RunRoots)
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var appPaths = TryOpen(baseKey, AppPathsKey);
                if (appPaths == null) continue;
                foreach (string exe in appPaths.GetSubKeyNames())
                {
                    using var key = TryOpen(appPaths, exe);
                    if (key?.GetValue(null) is not string target || !Into(target, dir)) continue;
                    var path = new RegistryPath(hive, view, $@"{AppPathsKey}\{exe}");
                    items.Add(new FootprintItem(FootprintKind.AppPath, path.ToDisplay(), exe,
                        $"Çalıştır kutusundaki \"{exe}\" kaydı kurulum klasörünü gösteriyor: {target}"));
                }
            }
        }

        private static void CollectFirewallRules(string dir, List<FootprintItem> items)
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var rules = baseKey.OpenSubKey(FirewallRulesKey);
            if (rules == null) return;
            foreach (string id in rules.GetValueNames())
            {
                if (rules.GetValue(id) is not string data) continue;
                var fields = FootprintText.ParseFirewallRule(data);
                if (!fields.TryGetValue("App", out string? program) || !Into(program, dir)) continue;
                string name = fields.TryGetValue("Name", out string? n) && !string.IsNullOrWhiteSpace(n) && !n.StartsWith('@') ? n : id;
                string direction = fields.TryGetValue("Dir", out string? d) && d.Equals("Out", StringComparison.OrdinalIgnoreCase) ? "giden" : "gelen";
                items.Add(new FootprintItem(FootprintKind.FirewallRule, id, name,
                    $"{direction} bağlantı kuralı kurulum klasöründeki programa ait: {Environment.ExpandEnvironmentVariables(program)}"));
            }
        }

        private static IEnumerable<(string Root, string Label, bool Recurse)> ShortcutRoots()
        {
            static string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
            yield return (F(Environment.SpecialFolder.DesktopDirectory), "Masaüstü kısayolu", false);
            yield return (F(Environment.SpecialFolder.CommonDesktopDirectory), "Ortak masaüstü kısayolu", false);
            yield return (F(Environment.SpecialFolder.Startup), "Başlangıç klasörü kısayolu", false);
            yield return (F(Environment.SpecialFolder.CommonStartup), "Ortak başlangıç klasörü kısayolu", false);
            yield return (F(Environment.SpecialFolder.Programs), "Başlat menüsü kısayolu", true);
            yield return (F(Environment.SpecialFolder.CommonPrograms), "Başlat menüsü kısayolu", true);
            yield return (Path.Combine(F(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch"),
                "Görev çubuğu / Hızlı Başlat kısayolu", true);
        }

        private static void CollectShortcuts(string dir, List<FootprintItem> items)
        {
            foreach (var (root, label, recurse) in ShortcutRoots())
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                List<string> files;
                try
                {
                    var options = new EnumerationOptions
                    {
                        RecurseSubdirectories = recurse,
                        MaxRecursionDepth = 4,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    };
                    files = Directory.EnumerateFiles(root, "*.lnk", options).Take(2000).ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (string file in files)
                {
                    string? normalizedFile = WindowsPath.Normalize(file);
                    if (normalizedFile == null || WindowsPath.IsStrictlyUnder(normalizedFile, dir)) continue; // klasörle birlikte gider
                    string? target = ShellLink.ResolveTarget(file);
                    if (target == null || !Into(target, dir)) continue;
                    items.Add(new FootprintItem(FootprintKind.Shortcut, file, Path.GetFileNameWithoutExtension(file),
                        $"{label}; hedefi kurulum klasöründe: {target}"));
                }
            }
        }

        #endregion

        #region Kaldırma sonrası

        public List<LeftoverItem> StillPresentLeftovers(UninstallFootprint footprint)
        {
            var results = new List<LeftoverItem>();
            if (footprint.InstallDir == null || footprint.Items.Count == 0) return results;

            object? taskService = footprint.Items.Any(i => i.Kind == FootprintKind.ScheduledTask) ? CreateTaskService() : null;
            try
            {
                foreach (var item in footprint.Items)
                {
                    try
                    {
                        var leftover = ToLeftoverIfPresent(item, taskService);
                        if (leftover != null) results.Add(leftover);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or COMException)
                    {
                        _log.Debug($"İz denetlenemedi: {item.Target} — {ex.Message}", nameof(FootprintCollector));
                    }
                }
            }
            finally
            {
                if (taskService != null) Marshal.ReleaseComObject(taskService);
            }
            return results;
        }

        private const string StillThere = " Program kaldırıldıktan sonra hâlâ duruyor.";

        private LeftoverItem? ToLeftoverIfPresent(FootprintItem item, object? taskService)
        {
            switch (item.Kind)
            {
                case FootprintKind.Service:
                    if (!ServiceIsRegistered(item.Target)) return null;
                    return New(item, LeftoverType.Service, $"Hizmet: {item.Display}", "Hizmet");

                case FootprintKind.ScheduledTask:
                    if (taskService == null || !TaskExists(taskService, item.Target)) return null;
                    return New(item, LeftoverType.ScheduledTask, $"Zamanlanmış görev: {item.Display}", "Görev");

                case FootprintKind.FirewallRule:
                    if (ReadFirewallRule(item.Target) == null) return null;
                    return New(item, LeftoverType.FirewallRule, $"Güvenlik duvarı kuralı: {item.Display}", "Kural");

                case FootprintKind.StartupEntry:
                {
                    if (!RegistryPath.TryParseWithValue(item.Target, RegistryView.Registry64, out var value) || value.ValueName == null) return null;
                    if (!RegistrySafetyGuard.CheckValueDeletion(value).IsAllowed || !ValueExists(value)) return null;
                    return New(item, LeftoverType.RegistryValue, $"Başlangıç girdisi: {item.Display}", "Kayıt değeri");
                }

                case FootprintKind.AppPath:
                {
                    if (!RegistryPath.TryParse(item.Target, RegistryView.Registry64, out var key)) return null;
                    if (!RegistrySafetyGuard.CheckKeyDeletion(key).IsAllowed || !KeyExists(key)) return null;
                    return New(item, LeftoverType.RegistryKey, $"Uygulama yolu (App Paths): {item.Display}", "Kayıt anahtarı");
                }

                case FootprintKind.Shortcut:
                {
                    if (!File.Exists(item.Target)) return null;
                    if (!_guard.CheckDeletion(item.Target, isDirectory: false).IsAllowed) return null;
                    long size = new FileInfo(item.Target).Length;
                    var leftover = New(item, LeftoverType.File, $"Kısayol: {item.Display}", ByteFormatter.Format(size));
                    leftover.SizeBytes = size;
                    return leftover;
                }
            }
            return null;
        }

        private static LeftoverItem New(FootprintItem item, LeftoverType type, string description, string sizeText) => new()
        {
            Path = item.Target,
            ItemType = type,
            FormattedSize = sizeText,
            Description = description,
            EvidenceText = item.Evidence + "." + StillThere,
            ConfidenceScore = Certain,
            IsSelected = true
        };

        #endregion

        #region Ortak denetimler (FootprintRemoval da kullanır)

        /// <summary>Hizmet hâlâ kayıtlı mı? Silinmek üzere işaretlenmiş (DeleteFlag) hizmet kayıtlı sayılmaz.</summary>
        internal static bool ServiceIsRegistered(string name)
        {
            if (!IsPlainName(name)) return false;
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey($@"{ServicesKey}\{name}");
            if (key == null) return false;
            return !(key.GetValue("DeleteFlag") is int flag && flag != 0);
        }

        internal static bool ServicePendingDelete(string name)
        {
            if (!IsPlainName(name)) return false;
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey($@"{ServicesKey}\{name}");
            return key?.GetValue("DeleteFlag") is int flag && flag != 0;
        }

        /// <summary>Hizmet adları ters eğik çizgi içeremez; kayıt yolu kaçışını önler.</summary>
        internal static bool IsPlainName(string name) =>
            !string.IsNullOrWhiteSpace(name) && name.IndexOfAny(new[] { '\\', '/', '\0' }) < 0 && name.Length <= 256;

        internal static string? ReadFirewallRule(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var rules = baseKey.OpenSubKey(FirewallRulesKey);
            return rules?.GetValue(id) as string;
        }

        internal static object? CreateTaskService()
        {
            try
            {
                Type? type = Type.GetTypeFromProgID("Schedule.Service");
                if (type == null) return null;
                object service = Activator.CreateInstance(type)!;
                ((dynamic)service).Connect();
                return service;
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                AppLog.Warning("Görev Zamanlayıcı'ya bağlanılamadı.", ex, nameof(FootprintCollector));
                return null;
            }
        }

        internal static bool TaskExists(object taskService, string taskPath) => GetTaskXml(taskService, taskPath) != null;

        /// <summary>Görevin tam XML tanımı (schtasks /Create /XML ile yeniden oluşturulabilir).</summary>
        internal static string? GetTaskXml(object taskService, string taskPath)
        {
            try
            {
                dynamic ts = taskService;
                dynamic root = ts.GetFolder("\\");
                dynamic task = root.GetTask(taskPath);
                return (string)task.Xml;
            }
            catch (Exception ex) when (ex is COMException or FileNotFoundException or UnauthorizedAccessException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                return null;
            }
        }

        private static bool ValueExists(RegistryPath value)
        {
            using var baseKey = RegistryKey.OpenBaseKey(value.Hive, value.View);
            using var key = baseKey.OpenSubKey(value.SubKey);
            return key != null && key.GetValueNames().Contains(value.ValueName, StringComparer.OrdinalIgnoreCase);
        }

        private static bool KeyExists(RegistryPath path)
        {
            using var baseKey = RegistryKey.OpenBaseKey(path.Hive, path.View);
            using var key = baseKey.OpenSubKey(path.SubKey);
            return key != null;
        }

        private static RegistryKey? TryOpen(RegistryKey parent, string name)
        {
            try
            {
                return parent.OpenSubKey(name);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return null;
            }
        }

        #endregion
    }
}
