using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Core.Activity;
using Bakım.Helpers;
using Bakım.Services.Safety;

namespace Bakım.Services.Activity
{
    /// <summary>Geri alma verisi dosya adları ve yükleri.</summary>
    public static class ActivityPayload
    {
        public const string RegistryValuesFile = "registry.json";
        public const string ServiceFile = "service.json";
        public const string FirewallFile = "firewall.json";
        /// <summary>Kaldırıcının sildiği hizmet/görev/güvenlik duvarı kuralı yedekleri.</summary>
        public const string FootprintFile = "footprint.json";

        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        public static void Write<T>(string directory, string fileName, T payload)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(payload, Json), new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
        }

        public static T? Read<T>(string? directory, string fileName) where T : class
        {
            if (string.IsNullOrEmpty(directory)) return null;
            string path = Path.Combine(directory, fileName);
            if (!File.Exists(path)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                AppLog.Warning($"Geri alma verisi okunamadı: {path}", ex, nameof(ActivityPayload));
                return null;
            }
        }

        public static bool Exists(string? directory, string fileName) =>
            !string.IsNullOrEmpty(directory) && File.Exists(Path.Combine(directory, fileName));
    }

    /// <param name="Start">Services\{ad}\Start: 2 otomatik, 3 elle, 4 devre dışı; null → değiştirilmedi.</param>
    /// <param name="DelayedAutoStart">Otomatik (Gecikmeli).</param>
    /// <param name="RestoreRunning">true → başlat, false → durdur, null → çalışma durumuna dokunma.</param>
    public sealed record ServiceUndoPayload(string ServiceName, string DisplayName, int? Start, bool DelayedAutoStart, bool? RestoreRunning);

    /// <param name="File">Günlükteki "tasks" klasörüne göre XML dosya adı.</param>
    public sealed record TaskBackup(string Path, string File);

    /// <param name="Data">FirewallRules altındaki ham kural verisi ("v2.30|Action=…|").</param>
    public sealed record FirewallRuleBackup(string Id, string Data);

    /// <summary>Kaldırıcının yedekleyerek sildiği sistem öğeleri (hizmet .reg yedekleri ayrıca *.reg olarak durur).</summary>
    public sealed class FootprintBackup
    {
        public List<string> Services { get; set; } = new();
        public List<TaskBackup> Tasks { get; set; } = new();
        public List<FirewallRuleBackup> FirewallRules { get; set; } = new();

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsEmpty => Services.Count == 0 && Tasks.Count == 0 && FirewallRules.Count == 0;
    }

    /// <param name="Added">true → Bakım kuralı ekledi (geri alma: sil); false → kuralı sildi (geri alma: yeniden ekle).</param>
    public sealed record FirewallUndoPayload(string RuleName, string ProgramPath, bool Added);

    /// <summary>
    /// "registry-values" ve "startup-approved": değer düzeyinde eski halleri yazar (ince ayarlar,
    /// başlangıç girdileri). Zaten özgün halinde olan değerlere dokunulmaz; yönetici olmadan
    /// yazılamayan HKLM değerleri tek UAC onayıyla PowerShell üzerinden geri yazılır.
    /// </summary>
    public sealed class RegistryValuesUndoHandler : IUndoHandler
    {
        public RegistryValuesUndoHandler(string key = UndoHandlers.RegistryValues) => Key = key;

        public string Key { get; }

        public bool HasPayload(ActivityEntry entry) => ActivityPayload.Exists(entry.PayloadPath, ActivityPayload.RegistryValuesFile);

        public async Task<UndoResult> UndoAsync(ActivityEntry entry, CancellationToken ct)
        {
            var snapshots = ActivityPayload.Read<List<RegistryValueSnapshot>>(entry.PayloadPath, ActivityPayload.RegistryValuesFile);
            if (snapshots == null || snapshots.Count == 0) return UndoResult.Fail("Kayıt defteri yedeği bulunamadı.");

            var items = new List<ActivityItem>();
            var pending = new List<RegistryValueSnapshot>();
            int restored = 0;

            // Ters sırada: aynı hedefe birden fazla yazım varsa en eski hal en son yazılır.
            foreach (var s in Enumerable.Reverse(snapshots))
            {
                ct.ThrowIfCancellationRequested();
                string target = $"{s.Root}\\{s.SubKey}" + (s.ValueName == null ? "" : $" → {DisplayName(s.ValueName)}");
                if (RegistryCapture.IsCurrent(s) || RegistryCapture.Restore(s))
                {
                    restored++;
                    items.Add(new ActivityItem(target, "Geri yükle", "Tamam"));
                }
                else
                {
                    pending.Add(s);
                }
            }

            int failed = 0;
            if (pending.Count > 0)
            {
                var lines = pending.Select(RegistryCapture.ToPowerShell).ToList();
                var scriptable = pending.Zip(lines).Where(p => p.Second != null).ToList();
                var unscriptable = pending.Zip(lines).Where(p => p.Second == null).Select(p => p.First).ToList();

                if (scriptable.Count > 0)
                {
                    var run = await ElevatedPowerShell.RunAsync(string.Join("; ", scriptable.Select(p => p.Second)), TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
                    foreach (var (s, _) in scriptable)
                    {
                        bool ok = run.Succeeded && RegistryCapture.IsCurrent(s);
                        if (ok) restored++; else failed++;
                        items.Add(new ActivityItem($"{s.Root}\\{s.SubKey} → {DisplayName(s.ValueName)}", "Geri yükle",
                            ok ? "Tamam (yönetici)" : "Başarısız", ok ? null : (run.Cancelled ? "Yönetici izni verilmedi" : run.Message)));
                    }
                }
                foreach (var s in unscriptable)
                {
                    failed++;
                    items.Add(new ActivityItem($"{s.Root}\\{s.SubKey}", "Geri yükle", "Başarısız", "Anahtar ağacı yönetici izni olmadan geri yüklenemedi"));
                }
            }

            string message = failed == 0
                ? $"{restored} kayıt defteri değeri özgün haline döndürüldü."
                : $"{restored} değer geri yüklendi, {failed} değer geri yüklenemedi.";
            return new UndoResult(failed == 0, restored, failed, message, items);
        }

        private static string DisplayName(string? valueName) => string.IsNullOrEmpty(valueName) ? "(Varsayılan)" : valueName;
    }

    /// <summary>
    /// "registry-reg-import": silinmeden önce alınan .reg yedeklerini ters sırayla içe aktarır;
    /// kaldırıcının sildiği zamanlanmış görevleri XML'den, güvenlik duvarı kurallarını ham veriden
    /// yeniden oluşturur. Yönetici gereken her şey tek UAC onayıyla yapılır.
    /// </summary>
    public sealed class RegImportUndoHandler : IUndoHandler
    {
        public string Key => UndoHandlers.RegistryRegImport;

        internal const string FirewallRulesPath = @"Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";

        public bool HasPayload(ActivityEntry entry) => HasRestorableBackup(entry.PayloadPath);

        /// <summary>Klasörde geri yüklenebilir bir yedek (.reg ya da iz yedeği) var mı?</summary>
        public static bool HasRestorableBackup(string? directory) =>
            !string.IsNullOrEmpty(directory) && Directory.Exists(directory) &&
            (Directory.EnumerateFiles(directory, "*.reg").Any() || ActivityPayload.Exists(directory, ActivityPayload.FootprintFile));

        public async Task<UndoResult> UndoAsync(ActivityEntry entry, CancellationToken ct)
        {
            if (!HasPayload(entry)) return UndoResult.Fail("Kayıt defteri yedeği (.reg) bulunamadı.");

            var files = Directory.GetFiles(entry.PayloadPath!, "*.reg").OrderByDescending(f => f, StringComparer.Ordinal).ToList();
            var footprint = ActivityPayload.Read<FootprintBackup>(entry.PayloadPath, ActivityPayload.FootprintFile);
            bool admin = UacHelper.IsAdministrator();
            var items = new List<ActivityItem>();
            var elevate = new List<string>();
            int restored = 0, failed = 0;

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (!admin && NeedsAdmin(file)) { elevate.Add(file); continue; }

                bool ok = await UndoJournal.RunRegAsync("import", file).ConfigureAwait(false);
                if (ok) restored++; else failed++;
                items.Add(new ActivityItem(Path.GetFileName(file), "reg import", ok ? "Tamam" : "Başarısız"));
            }

            // Görevler ve kurallar: yöneticiyken de ElevatedPowerShell doğrudan çalışır.
            var tasks = footprint?.Tasks.Where(t => File.Exists(Path.Combine(entry.PayloadPath!, "tasks", t.File))).ToList() ?? new List<TaskBackup>();
            var rules = footprint?.FirewallRules ?? new List<FirewallRuleBackup>();
            int elevatedCount = elevate.Count + tasks.Count + rules.Count;

            if (elevatedCount > 0)
            {
                // Tek UAC onayı; çıkış kodu başarısız öğe sayısıdır.
                string reg = Path.Combine(Environment.SystemDirectory, "reg.exe");
                // reg.exe başarı iletisini bile stderr'e yazar: 'Stop' ile PowerShell bunu hata sanar.
                var script = new StringBuilder("$ErrorActionPreference = 'Continue'; $failed = 0; ");
                foreach (var f in elevate)
                    script.Append($"& {ElevatedPowerShell.Quote(reg)} import {ElevatedPowerShell.Quote(f)} 2>$null; if ($LASTEXITCODE -ne 0) {{ $failed++ }}; ");
                foreach (var t in tasks)
                {
                    string xml = Path.Combine(entry.PayloadPath!, "tasks", t.File);
                    script.Append($"& schtasks.exe /Create /TN {ElevatedPowerShell.Quote(t.Path)} /XML {ElevatedPowerShell.Quote(xml)} /F 2>$null | Out-Null; if ($LASTEXITCODE -ne 0) {{ $failed++ }}; ");
                }
                foreach (var r in rules)
                {
                    script.Append($"try {{ New-ItemProperty -LiteralPath {ElevatedPowerShell.Quote(FirewallRulesPath)} -Name {ElevatedPowerShell.Quote(r.Id)} " +
                                  $"-Value {ElevatedPowerShell.Quote(r.Data)} -PropertyType String -Force -ErrorAction Stop | Out-Null }} catch {{ $failed++ }}; ");
                }
                script.Append("exit $failed");

                var run = await ElevatedPowerShell.RunAsync(script.ToString(), TimeSpan.FromMinutes(2), ct).ConfigureAwait(false);
                int elevatedFailed = run.Succeeded ? 0 : (run.ExitCode > 0 && run.ExitCode <= elevatedCount ? run.ExitCode : elevatedCount);
                restored += elevatedCount - elevatedFailed;
                failed += elevatedFailed;
                string what = string.Join(", ", new[]
                {
                    elevate.Count > 0 ? $"{elevate.Count} HKLM yedeği" : null,
                    tasks.Count > 0 ? $"{tasks.Count} görev" : null,
                    rules.Count > 0 ? $"{rules.Count} güvenlik duvarı kuralı" : null
                }.Where(x => x != null));
                items.Add(new ActivityItem(what, "Geri yükleme (yönetici)",
                    elevatedFailed == 0 ? "Tamam" : $"{elevatedFailed} başarısız",
                    run.Cancelled ? "Yönetici izni verilmedi" : null));
            }

            string message = failed == 0
                ? $"{restored} yedek geri yüklendi."
                : $"{restored} yedek geri yüklendi, {failed} yedek geri yüklenemedi.";
            if (restored > 0 && footprint != null && (footprint.Services.Count > 0 || rules.Count > 0))
                message += " Geri yüklenen hizmet ve güvenlik duvarı kayıtları Windows yeniden başlatıldığında etkin olur.";
            return new UndoResult(failed == 0, restored, failed, message, items);
        }

        private static bool NeedsAdmin(string regFile)
        {
            try
            {
                // reg.exe export UTF-16 yazar; File.ReadLines BOM'dan kodlamayı tanır.
                return File.ReadLines(regFile).Any(l => l.StartsWith("[HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) ||
                                                        l.StartsWith("[HKEY_CLASSES_ROOT", StringComparison.OrdinalIgnoreCase));
            }
            catch (IOException)
            {
                return true;
            }
        }
    }

    /// <summary>"service-config": hizmetin başlangıç türünü ve çalışma durumunu eski haline döndürür.</summary>
    public sealed class ServiceConfigUndoHandler : IUndoHandler
    {
        public string Key => UndoHandlers.ServiceConfig;

        public bool HasPayload(ActivityEntry entry) => ActivityPayload.Exists(entry.PayloadPath, ActivityPayload.ServiceFile);

        public async Task<UndoResult> UndoAsync(ActivityEntry entry, CancellationToken ct)
        {
            var p = ActivityPayload.Read<ServiceUndoPayload>(entry.PayloadPath, ActivityPayload.ServiceFile);
            if (p == null) return UndoResult.Fail("Hizmet yedeği bulunamadı.");

            string name = ElevatedPowerShell.Quote(p.ServiceName);
            var script = new StringBuilder();
            string? mode = p.Start switch
            {
                2 => p.DelayedAutoStart ? "delayed-auto" : "auto",
                3 => "demand",
                4 => "disabled",
                _ => null
            };
            if (mode != null)
                script.Append($"& sc.exe config {name} start= {mode} | Out-Null; if ($LASTEXITCODE -ne 0) {{ exit 2 }}; ");
            if (p.RestoreRunning == true) script.Append($"Start-Service -Name {name}; ");
            if (p.RestoreRunning == false) script.Append($"Stop-Service -Name {name} -Force; ");
            if (script.Length == 0) return UndoResult.Fail("Geri alınacak hizmet değişikliği yok.");

            var run = await ElevatedPowerShell.RunAsync(script.ToString(), TimeSpan.FromSeconds(90), ct).ConfigureAwait(false);
            string label = mode switch
            {
                "delayed-auto" => "Otomatik (Gecikmeli)",
                "auto" => "Otomatik",
                "demand" => "El ile",
                "disabled" => "Devre dışı",
                _ => null
            } ?? "";
            string what = string.Join(", ", new[]
            {
                mode != null ? $"başlangıç türü {label}" : null,
                p.RestoreRunning == true ? "yeniden başlatıldı" : p.RestoreRunning == false ? "durduruldu" : null
            }.Where(s => s != null));

            return run.Succeeded
                ? new UndoResult(true, 1, 0, $"{p.DisplayName}: {what}.", new[] { new ActivityItem(p.ServiceName, "Hizmet", "Tamam", what) })
                : new UndoResult(false, 0, 1, $"{p.DisplayName} geri alınamadı: {(run.Cancelled ? "yönetici izni verilmedi" : run.Message)}",
                    new[] { new ActivityItem(p.ServiceName, "Hizmet", "Başarısız", run.Message) });
        }
    }

    /// <summary>"firewall-rule": Bakım'ın eklediği engelleme kuralını kaldırır (ya da sildiğini geri ekler).</summary>
    public sealed class FirewallRuleUndoHandler : IUndoHandler
    {
        public string Key => UndoHandlers.FirewallRule;

        public bool HasPayload(ActivityEntry entry) => ActivityPayload.Exists(entry.PayloadPath, ActivityPayload.FirewallFile);

        public async Task<UndoResult> UndoAsync(ActivityEntry entry, CancellationToken ct)
        {
            var p = ActivityPayload.Read<FirewallUndoPayload>(entry.PayloadPath, ActivityPayload.FirewallFile);
            if (p == null) return UndoResult.Fail("Güvenlik duvarı kuralı bilgisi bulunamadı.");

            var run = p.Added
                ? await FirewallRules.RemoveAsync(p.RuleName, ct).ConfigureAwait(false)
                : await FirewallRules.AddBlockAsync(p.RuleName, p.ProgramPath, ct).ConfigureAwait(false);
            string action = p.Added ? "kaldırıldı" : "yeniden eklendi";
            return run.Succeeded
                ? new UndoResult(true, 1, 0, $"\"{p.RuleName}\" kuralı {action}.", new[] { new ActivityItem(p.RuleName, "Güvenlik duvarı", "Tamam", action) })
                : new UndoResult(false, 0, 1, $"Kural geri alınamadı: {(run.Cancelled ? "yönetici izni verilmedi" : run.Message)}");
        }
    }

    /// <summary>
    /// "recycle-bin": Geri Dönüşüm Kutusu'ndan otomatik geri yükleme güvenilir değildir
    /// (aynı adlı dosyalar, kullanıcı boşaltmış olabilir). Kutu açılır ve dosyalar listelenir.
    /// </summary>
    public sealed class RecycleBinUndoHandler : IUndoHandler
    {
        public string Key => UndoHandlers.RecycleBin;

        public bool HasPayload(ActivityEntry entry) => true;

        public Task<UndoResult> UndoAsync(ActivityEntry entry, CancellationToken ct)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return Task.FromResult(UndoResult.Fail($"Geri Dönüşüm Kutusu açılamadı: {ex.Message}"));
            }
            int count = entry.Items.Count;
            return Task.FromResult(new UndoResult(true, 0, 0,
                $"Geri Dönüşüm Kutusu açıldı. {count} öğeyi oradan \"Geri yükle\" ile geri alabilirsiniz.",
                entry.Items, Manual: true));
        }
    }

    /// <summary>Güvenlik duvarı engelleme kuralları (NetSecurity cmdlet'leri; çıkış kodu doğrulanır).</summary>
    public static class FirewallRules
    {
        public static Task<ElevatedPowerShell.Result> AddBlockAsync(string ruleName, string programPath, CancellationToken ct = default) =>
            ElevatedPowerShell.RunAsync(
                $"if (-not (Get-NetFirewallRule -DisplayName {ElevatedPowerShell.Quote(ruleName)} -ErrorAction SilentlyContinue)) {{ " +
                $"New-NetFirewallRule -DisplayName {ElevatedPowerShell.Quote(ruleName)} -Direction Outbound -Action Block " +
                $"-Program {ElevatedPowerShell.Quote(programPath)} -Enabled True | Out-Null }}",
                TimeSpan.FromSeconds(60), ct);

        public static Task<ElevatedPowerShell.Result> RemoveAsync(string ruleName, CancellationToken ct = default) =>
            ElevatedPowerShell.RunAsync(
                $"Get-NetFirewallRule -DisplayName {ElevatedPowerShell.Quote(ruleName)} -ErrorAction SilentlyContinue | Remove-NetFirewallRule",
                TimeSpan.FromSeconds(60), ct);
    }
}
