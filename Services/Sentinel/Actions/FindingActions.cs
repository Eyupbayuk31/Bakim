using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Bakım.Core.Activity;
using Bakım.Core.Safety;
using Bakım.Core.Sentinel;
using Bakım.Helpers;
using Bakım.Models;
using Bakım.Services.Activity;
using Bakım.Services.Safety;
using Bakım.Services.Uninstall;

namespace Bakım.Services.Sentinel.Actions
{
    public sealed record FindingActionResult(bool Success, string Message);

    /// <summary>
    /// Nöbetçi bulgularındaki tek tık müdahaleler (NÖB 5.2). Her müdahale Etkinlik Merkezi'ne yazılır;
    /// kayıt defteri, hizmet, güvenlik duvarı ve sertifika müdahaleleri oradan geri alınabilir.
    /// </summary>
    public static class FindingActions
    {
        public static string? Label(FindingAction action) => action switch
        {
            FindingAction.RemoveStartupValue => "Açılıştan kaldır",
            FindingAction.RemoveRegistryValue => "Değeri kaldır",
            FindingAction.DisableService => "Devre dışı bırak",
            FindingAction.DisableTask => "Görevi kapat",
            FindingAction.RemoveFirewallRule => "Kuralı kaldır",
            FindingAction.DisableProxy => "Proxy'yi kapat",
            FindingAction.RemoveRootCertificate => "Sertifikayı kaldır",
            FindingAction.RemoveDefenderExclusion => "İstisnayı kaldır",
            _ => null
        };

        public static string ConfirmText(RiskFinding finding) => finding.Action switch
        {
            FindingAction.DisableTask => "Görev silinmez, devre dışı bırakılır; Görev Zamanlayıcı'dan yeniden açabilirsiniz.",
            FindingAction.RemoveDefenderExclusion => "Defender bu konumu yeniden tarar. Geri eklemek isterseniz Windows Güvenliği'nden yapabilirsiniz.",
            FindingAction.RemoveRootCertificate => "Sertifika yedeklenir ve kaldırılır; Etkinlik Merkezi'nden geri yüklenebilir. Kullanıcı deposunda Windows ayrıca onay isteyebilir.",
            _ => "Özgün değer yedeklenir; Etkinlik Merkezi'nden geri alabilirsiniz."
        };

        public static async Task<FindingActionResult> ExecuteAsync(RiskFinding finding, string appName, CancellationToken ct = default)
        {
            try
            {
                return finding.Action switch
                {
                    FindingAction.RemoveStartupValue or FindingAction.RemoveRegistryValue => await RemoveValueAsync(finding, appName),
                    FindingAction.DisableService => await DisableServiceAsync(finding, appName, ct),
                    FindingAction.DisableTask => await DisableTaskAsync(finding, appName, ct),
                    FindingAction.RemoveFirewallRule => await RemoveFirewallRuleAsync(finding, appName, ct),
                    FindingAction.DisableProxy => DisableProxy(finding, appName),
                    FindingAction.RemoveRootCertificate => await RemoveRootCertificateAsync(finding, appName, ct),
                    FindingAction.RemoveDefenderExclusion => await RemoveDefenderExclusionAsync(finding, appName, ct),
                    _ => new FindingActionResult(false, "Bu bulgu için bir müdahale tanımlı değil.")
                };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or CryptographicException)
            {
                AppLog.Warning($"Nöbetçi müdahalesi başarısız: {finding.Action} {finding.Target}", ex, nameof(FindingActions));
                return new FindingActionResult(false, ex.Message);
            }
        }

        private static IActivityService? Activity => App.TryGetService<IActivityService>();

        private static async Task<FindingActionResult> RemoveValueAsync(RiskFinding finding, string appName)
        {
            if (!FindingTarget.TryParseValue(finding.Target, out var path)) return new(false, "Kayıt yolu çözümlenemedi.");
            var safeRegistry = App.TryGetService<ISafeRegistryService>();
            if (safeRegistry == null) return new(false, "Kayıt defteri servisi kullanılamıyor.");

            // Nöbetçi 32 bit görünümdeki Run değerlerini de aynı metinle yazar: bulunamazsa diğer görünüm denenir.
            if (!ValueExists(path) && ValueExists(path with { View = RegistryView.Registry32 })) path = path with { View = RegistryView.Registry32 };
            if (!RegistrySafetyGuard.CheckValueDeletion(path).IsAllowed) return new(false, "Bu değer korumalı bir anahtarda.");

            string journal = UndoJournal.Create($"Nöbetçi: {finding.Title}");
            var result = await safeRegistry.DeleteValueAsync(path, journal);
            string title = finding.Action == FindingAction.RemoveStartupValue
                ? $"\"{path.ValueName}\" açılıştan kaldırıldı"
                : $"\"{path.ValueName}\" değeri kaldırıldı";
            if (Activity is { } activity)
            {
                activity.RecordWithRegBackup(ActivityKind.StartupChange, "Kurulum Nöbetçisi", title,
                    $"{appName} kurulumunun eklediği girdi · {result.Message}",
                    result.Succeeded ? ActivityOutcome.Succeeded : ActivityOutcome.Failed,
                    UndoJournal.GetDirectory(journal),
                    new[] { new ActivityItem(path.ToString(), "Kayıt değeri", result.Outcome.ToString(), result.Message) },
                    deepLink: "Sentinel");
            }
            return new(result.Succeeded, result.Succeeded ? title + "." : $"Kaldırılamadı: {result.Message}");
        }

        private static bool ValueExists(RegistryPath path)
        {
            using var root = RegistryKey.OpenBaseKey(path.Hive, path.View);
            using var key = root.OpenSubKey(path.SubKey);
            return key != null && key.GetValueNames().Contains(path.ValueName, StringComparer.OrdinalIgnoreCase);
        }

        private static async Task<FindingActionResult> DisableServiceAsync(RiskFinding finding, string appName, CancellationToken ct)
        {
            string name = finding.Target ?? string.Empty;
            if (!FootprintCollector.IsPlainName(name)) return new(false, "Geçersiz hizmet adı.");

            int? start = null;
            bool delayed = false;
            string display = name;
            using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = root.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}"))
            {
                if (key == null) return new(false, "Hizmet artık yok.");
                start = key.GetValue("Start") as int?;
                delayed = key.GetValue("DelayedAutostart") is int d && d == 1;
                if (key.GetValue("DisplayName") is string dn && !dn.StartsWith('@')) display = dn;
            }
            bool wasRunning = IsServiceRunning(name);

            string q = ElevatedPowerShell.Quote(name);
            var run = await ElevatedPowerShell.RunAsync(
                $"Stop-Service -Name {q} -Force -ErrorAction SilentlyContinue; & sc.exe config {q} start= disabled | Out-Null; exit $LASTEXITCODE",
                TimeSpan.FromSeconds(60), ct);

            string title = $"\"{display}\" hizmeti devre dışı bırakıldı";
            Activity?.RecordServiceChange(run.Succeeded ? title : $"\"{display}\" devre dışı bırakılamadı",
                $"{appName} kurulumunun eklediği hizmet · Kurulum Nöbetçisi",
                run.Succeeded ? ActivityOutcome.Succeeded : run.Cancelled ? ActivityOutcome.Cancelled : ActivityOutcome.Failed,
                run.Succeeded ? new ServiceUndoPayload(name, display, start, delayed, wasRunning ? true : null) : null);
            return run.Succeeded
                ? new(true, title + ".")
                : new(false, run.Cancelled ? "Yönetici izni verilmedi." : $"Başarısız: {run.Message}");
        }

        private static bool IsServiceRunning(string name)
        {
            // "sc query" çıktısında durum kodu dile bağlı değildir: "STATE : 4 RUNNING".
            var run = ProcessRunner.Run("sc.exe", new[] { "query", name }, TimeSpan.FromSeconds(10));
            return run.Succeeded && System.Text.RegularExpressions.Regex.IsMatch(run.StdOut, @"STATE\s*:\s*(4|2)\s");
        }

        private static async Task<FindingActionResult> DisableTaskAsync(RiskFinding finding, string appName, CancellationToken ct)
        {
            string path = finding.Target ?? string.Empty;
            if (path.Length == 0) return new(false, "Görev yolu yok.");
            var run = await ElevatedPowerShell.RunAsync(
                $"& schtasks.exe /Change /TN {ElevatedPowerShell.Quote(path)} /Disable | Out-Null; exit $LASTEXITCODE",
                TimeSpan.FromSeconds(60), ct);
            string title = $"\"{path}\" görevi devre dışı bırakıldı";
            Activity?.RecordSimple(ActivityKind.StartupChange, "Kurulum Nöbetçisi", run.Succeeded ? title : $"\"{path}\" görevi kapatılamadı",
                $"{appName} kurulumunun eklediği görev · Görev Zamanlayıcı'dan yeniden etkinleştirilebilir",
                run.Succeeded ? ActivityOutcome.Succeeded : run.Cancelled ? ActivityOutcome.Cancelled : ActivityOutcome.Failed,
                deepLink: "Sentinel");
            return run.Succeeded ? new(true, title + ".") : new(false, run.Cancelled ? "Yönetici izni verilmedi." : $"Başarısız: {run.Message}");
        }

        private static async Task<FindingActionResult> RemoveFirewallRuleAsync(RiskFinding finding, string appName, CancellationToken ct)
        {
            string id = finding.Target ?? string.Empty;
            if (FootprintCollector.ReadFirewallRule(id) == null) return new(false, "Kural artık yok.");

            string journal = UndoJournal.Create($"Nöbetçi: güvenlik duvarı kuralı");
            var item = new LeftoverItem { Path = id, ItemType = LeftoverType.FirewallRule, Description = finding.Detail };
            var results = await FootprintRemoval.RemoveAsync(new[] { item }, journal, null, ct);
            var result = results.FirstOrDefault();
            bool ok = result?.Succeeded == true;
            Activity?.RecordWithRegBackup(ActivityKind.FirewallRule, "Kurulum Nöbetçisi",
                ok ? "Güvenlik duvarı kuralı kaldırıldı" : "Güvenlik duvarı kuralı kaldırılamadı",
                $"{appName} · {finding.Detail}", ok ? ActivityOutcome.Succeeded : ActivityOutcome.Failed,
                UndoJournal.GetDirectory(journal),
                new[] { new ActivityItem(id, "Güvenlik duvarı kuralı", result?.Outcome.ToString() ?? "-", result?.Message) },
                deepLink: "Sentinel");
            return new(ok, ok ? "Kural kaldırıldı." : result?.Message ?? "Kaldırılamadı.");
        }

        private static FindingActionResult DisableProxy(RiskFinding finding, string appName)
        {
            const string path = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
            using var capture = RegistryCapture.Begin();
            bool ok = VerifiedRegistry.SetDword(Registry.CurrentUser, path, "ProxyEnable", 0);
            Activity?.RecordRegistryChange(ActivityKind.Tweak, "Kurulum Nöbetçisi",
                ok ? "Proxy kapatıldı" : "Proxy kapatılamadı", $"{appName} kurulumunun ayarladığı proxy · {finding.Detail}",
                ok ? ActivityOutcome.Succeeded : ActivityOutcome.Failed, capture.Items, deepLink: "Sentinel");
            return new(ok, ok ? "Proxy kapatıldı (ProxyEnable = 0). Proxy sunucusu değeri silinmedi." : "Proxy ayarı yazılamadı.");
        }

        private static async Task<FindingActionResult> RemoveRootCertificateAsync(RiskFinding finding, string appName, CancellationToken ct)
        {
            if (!FindingTarget.TryParseCertificate(finding.Target, out string thumbprint, out bool machine))
                return new(false, "Sertifika kimliği çözümlenemedi.");
            var location = machine ? StoreLocation.LocalMachine : StoreLocation.CurrentUser;

            X509Certificate2? cert;
            using (var store = new X509Store(StoreName.Root, location))
            {
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                cert = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false).OfType<X509Certificate2>().FirstOrDefault();
            }
            if (cert == null) return new(false, "Sertifika artık depoda değil.");

            using (cert)
            {
                // Yedek, kaldırmadan ÖNCE: geri alma handler'ı bu dosyayı yeniden içe aktarır.
                var entry = new ActivityEntry
                {
                    Kind = ActivityKind.Other,
                    Module = "Kurulum Nöbetçisi",
                    Title = $"Kök sertifika kaldırıldı: {cert.GetNameInfo(X509NameType.SimpleName, false)}",
                    Summary = $"{appName} kurulumunun eklediği sertifika · {cert.Subject}",
                    DeepLink = "Sentinel"
                };
                var activity = Activity;
                string? dir = activity?.CreateJournal(entry.Id);
                string? backupFile = null;
                if (dir != null)
                {
                    backupFile = Path.Combine(dir, $"{thumbprint}.cer");
                    await File.WriteAllBytesAsync(backupFile, cert.Export(X509ContentType.Cert), ct);
                    ActivityPayload.Write(dir, RootCertificateUndoHandler.PayloadFile, new RootCertificatePayload(thumbprint, machine, Path.GetFileName(backupFile)));
                }

                bool ok;
                string message;
                if (machine)
                {
                    var run = await ElevatedPowerShell.RunAsync(
                        $"Remove-Item -LiteralPath {ElevatedPowerShell.Quote($@"Cert:\LocalMachine\Root\{thumbprint}")} -Force; exit 0",
                        TimeSpan.FromSeconds(60), ct);
                    ok = run.Succeeded && !CertificateExists(thumbprint, location);
                    message = ok ? "Sertifika kaldırıldı." : run.Cancelled ? "Yönetici izni verilmedi." : $"Kaldırılamadı: {run.Message}";
                }
                else
                {
                    using var store = new X509Store(StoreName.Root, location);
                    store.Open(OpenFlags.ReadWrite);
                    store.Remove(cert); // Windows kullanıcıdan onay isteyebilir.
                    ok = !CertificateExists(thumbprint, location);
                    message = ok ? "Sertifika kaldırıldı." : "Sertifika kaldırılmadı (onay verilmemiş olabilir).";
                }

                if (activity != null)
                {
                    activity.Record(entry with
                    {
                        Outcome = ok ? ActivityOutcome.Succeeded : ActivityOutcome.Failed,
                        Undo = ok && backupFile != null ? UndoState.Undoable : UndoState.NotUndoable,
                        UndoHandler = ok && backupFile != null ? UndoHandlers.RootCertificate : null,
                        PayloadPath = ok ? dir : null,
                        Items = new System.Collections.Generic.List<ActivityItem> { new(thumbprint, "Kök sertifika", ok ? "Kaldırıldı" : "Başarısız", cert.Subject) }
                    });
                }
                return new(ok, message);
            }
        }

        internal static bool CertificateExists(string thumbprint, StoreLocation location)
        {
            using var store = new X509Store(StoreName.Root, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            return store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false).Count > 0;
        }

        private static async Task<FindingActionResult> RemoveDefenderExclusionAsync(RiskFinding finding, string appName, CancellationToken ct)
        {
            if (!FindingTarget.TryParseDefenderExclusion(finding.Target, out string parameter, out string value))
                return new(false, "İstisna çözümlenemedi.");
            var run = await ElevatedPowerShell.RunAsync(
                $"Remove-MpPreference -{parameter} {ElevatedPowerShell.Quote(value)}; exit 0", TimeSpan.FromSeconds(60), ct);
            string title = $"Defender istisnası kaldırıldı: {value}";
            Activity?.RecordSimple(ActivityKind.Other, "Kurulum Nöbetçisi", run.Succeeded ? title : "Defender istisnası kaldırılamadı",
                $"{appName} kurulumunun eklediği istisna · yeniden eklemek için Windows Güvenliği > Virüs ve tehdit koruması > Dışlamalar",
                run.Succeeded ? ActivityOutcome.Succeeded : run.Cancelled ? ActivityOutcome.Cancelled : ActivityOutcome.Failed,
                deepLink: "Sentinel");
            return run.Succeeded ? new(true, title + ".") : new(false, run.Cancelled ? "Yönetici izni verilmedi." : $"Başarısız: {run.Message}");
        }
    }

    public sealed record RootCertificatePayload(string Thumbprint, bool Machine, string File);

    /// <summary>"root-certificate": kaldırılan kök sertifikayı yedeğinden depoya geri ekler.</summary>
    public sealed class RootCertificateUndoHandler : IUndoHandler
    {
        public const string PayloadFile = "certificate.json";

        public string Key => UndoHandlers.RootCertificate;

        public bool HasPayload(ActivityEntry entry) => ActivityPayload.Exists(entry.PayloadPath, PayloadFile);

        public async Task<UndoResult> UndoAsync(ActivityEntry entry, CancellationToken ct)
        {
            var p = ActivityPayload.Read<RootCertificatePayload>(entry.PayloadPath, PayloadFile);
            if (p == null) return UndoResult.Fail("Sertifika yedeği bulunamadı.");
            string file = Path.Combine(entry.PayloadPath!, p.File);
            if (!File.Exists(file)) return UndoResult.Fail("Sertifika dosyası bulunamadı.");

            var location = p.Machine ? StoreLocation.LocalMachine : StoreLocation.CurrentUser;
            bool ok;
            if (p.Machine)
            {
                var run = await ElevatedPowerShell.RunAsync(
                    $"Import-Certificate -FilePath {ElevatedPowerShell.Quote(file)} -CertStoreLocation 'Cert:\\LocalMachine\\Root' | Out-Null",
                    TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
                if (run.Cancelled) return UndoResult.Fail("Yönetici izni verilmedi.");
            }
            else
            {
                using var cert = X509CertificateLoader.LoadCertificateFromFile(file);
                using var store = new X509Store(StoreName.Root, location);
                store.Open(OpenFlags.ReadWrite);
                store.Add(cert); // Windows kullanıcıdan onay isteyebilir.
            }
            ok = FindingActions.CertificateExists(p.Thumbprint, location);
            return ok
                ? new UndoResult(true, 1, 0, "Sertifika yeniden eklendi.", new[] { new ActivityItem(p.Thumbprint, "Kök sertifika", "Geri eklendi") })
                : UndoResult.Fail("Sertifika geri eklenemedi.");
        }
    }
}
