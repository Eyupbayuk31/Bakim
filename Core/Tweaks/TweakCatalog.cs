using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bakım.Core.Tweaks
{
    public enum TweakOpKind { SetDword, SetString, DeleteValue, CreateKey, DeleteKey }

    /// <summary>Tek bir kayıt defteri değişikliği.</summary>
    public sealed record TweakOp
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TweakOpKind Op { get; init; }
        /// <summary>"HKCU" ya da "HKLM".</summary>
        public string Root { get; init; } = "HKCU";
        public string Key { get; init; } = string.Empty;
        public string? Name { get; init; }
        public int? Dword { get; init; }
        public string? Text { get; init; }

        public bool IsMachine => Root.Equals("HKLM", StringComparison.OrdinalIgnoreCase);

        /// <summary>HKLM ve HKCU\Software\Policies (kullanıcıya salt okunur ACL) yönetici ister.</summary>
        public bool NeedsAdmin => IsMachine || Key.StartsWith(@"Software\Policies\", StringComparison.OrdinalIgnoreCase);

        /// <summary>"Neyi değiştirir?" satırı: gerçek kayıt yolu ve değeri.</summary>
        public string Describe() => Op switch
        {
            TweakOpKind.SetDword => $@"{Root}\{Key} → {Name} = {Dword} (DWORD)",
            TweakOpKind.SetString => $@"{Root}\{Key} → {Name} = ""{Text}""",
            TweakOpKind.DeleteValue => $@"{Root}\{Key} → {Name} silinir",
            TweakOpKind.CreateKey => $@"{Root}\{Key} oluşturulur",
            TweakOpKind.DeleteKey => $@"{Root}\{Key} silinir",
            _ => Key
        };
    }

    /// <summary>Veri tabanlı ince ayar (MASTER_PLAN §5.16).</summary>
    public sealed record TweakDefinition
    {
        public string Id { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Icon { get; init; } = "Wrench24";
        public bool Recommended { get; init; }
        public bool RequiresRestart { get; init; }
        /// <summary>Bu Windows derlemesinden itibaren geçerli (ör. 22000 = Windows 11).</summary>
        public int? MinBuild { get; init; }
        public int? MaxBuild { get; init; }
        public IReadOnlyList<TweakOp> On { get; init; } = Array.Empty<TweakOp>();
        public IReadOnlyList<TweakOp> Off { get; init; } = Array.Empty<TweakOp>();

        /// <summary>Yönetici gereksinimi tanımdan türetilir (elle yazılmaz): HKLM ya da HKCU politikaları.</summary>
        public bool RequiresAdmin => On.Concat(Off).Any(o => o.NeedsAdmin);

        public bool AppliesTo(int build) => (MinBuild is not int min || build >= min) && (MaxBuild is not int max || build <= max);
    }

    /// <summary>Kayıt defterini okuyan soyutlama (Windows'ta gerçek, testte sahte).</summary>
    public interface ITweakRegistryReader
    {
        /// <summary>Değer yoksa null; DWORD int, dize string döner.</summary>
        object? GetValue(string root, string key, string? name);
        bool KeyExists(string root, string key);
    }

    public static class TweakCatalog
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static IReadOnlyList<TweakDefinition> Parse(string json) =>
            JsonSerializer.Deserialize<List<TweakDefinition>>(json, Json) ?? new List<TweakDefinition>();

        /// <summary>Katalog hataları (boşsa geçerli): yinelenen kimlik, boş değişiklik, eksik değer, geçersiz kök.</summary>
        public static IReadOnlyList<string> Validate(IEnumerable<TweakDefinition> definitions)
        {
            var errors = new List<string>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in definitions)
            {
                if (string.IsNullOrWhiteSpace(d.Id)) { errors.Add("Kimliği olmayan ayar."); continue; }
                if (!ids.Add(d.Id)) errors.Add($"{d.Id}: yinelenen kimlik.");
                if (string.IsNullOrWhiteSpace(d.Title) || string.IsNullOrWhiteSpace(d.Category)) errors.Add($"{d.Id}: başlık/kategori eksik.");
                if (d.On.Count == 0 || d.Off.Count == 0) errors.Add($"{d.Id}: açma ve kapama değişiklikleri tanımlı olmalı.");
                foreach (var op in d.On.Concat(d.Off))
                {
                    if (!op.Root.Equals("HKCU", StringComparison.OrdinalIgnoreCase) && !op.Root.Equals("HKLM", StringComparison.OrdinalIgnoreCase))
                        errors.Add($"{d.Id}: geçersiz kök '{op.Root}'.");
                    if (string.IsNullOrWhiteSpace(op.Key)) errors.Add($"{d.Id}: anahtar yolu boş.");
                    bool needsName = op.Op is TweakOpKind.SetDword or TweakOpKind.SetString or TweakOpKind.DeleteValue;
                    if (needsName && op.Name == null) errors.Add($"{d.Id}: {op.Op} için değer adı gerekli.");
                    if (op.Op == TweakOpKind.SetDword && op.Dword == null) errors.Add($"{d.Id}: SetDword değeri yok.");
                    if (op.Op == TweakOpKind.SetString && op.Text == null) errors.Add($"{d.Id}: SetString değeri yok.");
                }
            }
            return errors;
        }

        /// <summary>
        /// Ayar açık mı? "Açma" değişikliklerinin HEPSİ şu an geçerliyse açıktır.
        /// </summary>
        public static bool IsOn(TweakDefinition definition, ITweakRegistryReader registry) =>
            definition.On.Count > 0 && definition.On.All(op => IsInEffect(op, registry));

        public static bool IsInEffect(TweakOp op, ITweakRegistryReader registry)
        {
            switch (op.Op)
            {
                case TweakOpKind.SetDword:
                {
                    object? v = registry.GetValue(op.Root, op.Key, op.Name);
                    return v is int i && i == op.Dword;
                }
                case TweakOpKind.SetString:
                {
                    object? v = registry.GetValue(op.Root, op.Key, op.Name);
                    return v is string s && string.Equals(s, op.Text, StringComparison.OrdinalIgnoreCase);
                }
                case TweakOpKind.DeleteValue:
                    return registry.GetValue(op.Root, op.Key, op.Name) == null;
                case TweakOpKind.CreateKey:
                    return registry.KeyExists(op.Root, op.Key);
                case TweakOpKind.DeleteKey:
                    return !registry.KeyExists(op.Root, op.Key);
                default:
                    return false;
            }
        }

        /// <summary>Uygulanacak değişiklikler (şu an zaten geçerli olanlar atlanır).</summary>
        public static IReadOnlyList<TweakOp> Plan(TweakDefinition definition, bool enable, ITweakRegistryReader registry) =>
            (enable ? definition.On : definition.Off).Where(op => !IsInEffect(op, registry)).ToList();
    }
}
