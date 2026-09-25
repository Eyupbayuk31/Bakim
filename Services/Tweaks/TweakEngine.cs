using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Bakım.Core.Tweaks;
using Bakım.Helpers;
using Bakım.Models;

namespace Bakım.Services.Tweaks
{
    /// <summary>
    /// Veri tabanlı ince ayar motoru (MASTER_PLAN §5.16). Tanımlar Assets/tweaks/*.json'dan gelir;
    /// motor uygular, geri okuyarak doğrular ve özgün değerleri RegistryCapture'a bırakır (Etkinlik
    /// Merkezi'nden birebir geri alma). Karmaşık ayarlar (komut, hizmet, dosya) hâlâ kendi servislerinde.
    /// </summary>
    public static class TweakEngine
    {
        private static readonly Lazy<List<TweakDefinition>> Loaded = new(Load);

        public static IReadOnlyList<TweakDefinition> Definitions => Loaded.Value;

        public static bool Handles(string id) => Find(id) != null;

        public static TweakDefinition? Find(string id) =>
            Loaded.Value.FirstOrDefault(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        private static List<TweakDefinition> Load()
        {
            var all = new List<TweakDefinition>();
            var assembly = typeof(TweakEngine).Assembly;
            foreach (string name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("tweaks/", StringComparison.Ordinal)).OrderBy(n => n))
            {
                try
                {
                    using var stream = assembly.GetManifestResourceStream(name);
                    if (stream == null) continue;
                    using var reader = new StreamReader(stream);
                    all.AddRange(TweakCatalog.Parse(reader.ReadToEnd()));
                }
                catch (System.Text.Json.JsonException ex)
                {
                    AppLog.Error($"İnce ayar kataloğu okunamadı: {name}", ex, nameof(TweakEngine));
                }
            }

            var errors = TweakCatalog.Validate(all);
            foreach (string error in errors) AppLog.Warning($"İnce ayar kataloğu: {error}", null, nameof(TweakEngine));

            int build = Environment.OSVersion.Version.Build;
            var valid = all.Where(d => d.On.Count > 0 && d.Off.Count > 0 && d.AppliesTo(build))
                .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            AppLog.Info($"İnce ayar kataloğu: {valid.Count} ayar yüklendi.", nameof(TweakEngine));
            return valid;
        }

        /// <summary>Kategorideki veri tabanlı ayarlar, şu anki durumlarıyla (katalog sırasıyla).</summary>
        public static List<SystemTweakItem> ItemsFor(string category)
        {
            var reader = new WindowsTweakRegistryReader();
            return Loaded.Value
                .Where(d => d.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .Select(d => ToItem(d, reader))
                .ToList();
        }

        public static SystemTweakItem ToItem(TweakDefinition d, ITweakRegistryReader reader) => new()
        {
            Id = d.Id,
            Category = d.Category,
            Title = d.Title,
            Description = d.Description,
            Type = TweakType.Toggle,
            RequiresAdmin = d.RequiresAdmin,
            RequiresRestart = d.RequiresRestart,
            IsRecommended = d.Recommended,
            IconSymbol = d.Icon,
            IsEnabled = TweakCatalog.IsOn(d, reader),
            ChangesWhenOn = d.On.Select(o => o.Describe()).ToList(),
            ChangesWhenOff = d.Off.Select(o => o.Describe()).ToList(),
        };

        /// <summary>Ayarı uygular ve sonucu doğrular. Legacy servislerle aynı sözleşme: LastError + IsEnabled.</summary>
        public static Task<bool> ApplyAsync(SystemTweakItem tweak, bool enable) => Task.Run(() =>
        {
            var definition = Find(tweak.Id);
            if (definition == null)
            {
                tweak.LastError = "Ayar katalogda yok.";
                return false;
            }

            var reader = new WindowsTweakRegistryReader();
            using var writes = WriteScope.Begin();
            foreach (var op in TweakCatalog.Plan(definition, enable, reader))
                Execute(op);

            if (!writes.Succeeded)
            {
                tweak.LastError = writes.Describe();
                return false;
            }

            // Doğrulama: açma değişikliklerinin hepsi geçerli mi (ya da kapatınca değil mi)?
            bool nowOn = TweakCatalog.IsOn(definition, reader);
            if (enable && !nowOn)
            {
                tweak.LastError = "Yazılan değerler doğrulanamadı (bir politika ya da başka bir program geri almış olabilir).";
                return false;
            }

            tweak.LastError = null;
            tweak.IsEnabled = enable;
            return true;
        });

        private static void Execute(TweakOp op)
        {
            RegistryKey root = op.IsMachine ? Registry.LocalMachine : Registry.CurrentUser;
            switch (op.Op)
            {
                case TweakOpKind.SetDword:
                    VerifiedRegistry.SetDword(root, op.Key, op.Name!, op.Dword!.Value);
                    break;
                case TweakOpKind.SetString:
                    VerifiedRegistry.SetString(root, op.Key, op.Name!, op.Text!);
                    break;
                case TweakOpKind.DeleteValue:
                    VerifiedRegistry.DeleteValue(root, op.Key, op.Name!);
                    break;
                case TweakOpKind.DeleteKey:
                    VerifiedRegistry.DeleteKeyTree(root, op.Key);
                    break;
                case TweakOpKind.CreateKey:
                    try
                    {
                        RegistryCapture.TrackKey(root, op.Key);
                        using var key = root.CreateSubKey(op.Key, true);
                        if (key == null) WriteScope.Report($"{op.Describe()}: anahtar oluşturulamadı");
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                    {
                        WriteScope.Report($"{op.Describe()}: {ex.Message}");
                    }
                    break;
            }
        }
    }

    /// <summary>Gerçek kayıt defteri okuyucu (varsayılan görünüm; eski servislerle aynı).</summary>
    public sealed class WindowsTweakRegistryReader : ITweakRegistryReader
    {
        public object? GetValue(string root, string key, string? name)
        {
            try
            {
                using var k = Root(root).OpenSubKey(key);
                return k?.GetValue(name);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return null;
            }
        }

        public bool KeyExists(string root, string key)
        {
            try
            {
                using var k = Root(root).OpenSubKey(key);
                return k != null;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return false;
            }
        }

        private static RegistryKey Root(string root) =>
            root.Equals("HKLM", StringComparison.OrdinalIgnoreCase) ? Registry.LocalMachine : Registry.CurrentUser;
    }
}
