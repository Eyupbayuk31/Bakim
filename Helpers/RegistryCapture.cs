using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;

namespace Bakım.Helpers
{
    /// <summary>
    /// Bir kayıt defteri değerinin (ya da tüm bir anahtarın) değiştirilmeden ÖNCEKİ hali.
    /// <see cref="ValueName"/> null ise anahtarın tamamıdır ve <see cref="Data"/> ağacın JSON'udur.
    /// </summary>
    public sealed record RegistryValueSnapshot(string Root, string SubKey, string? ValueName, bool Existed, string? Kind, string? Data)
    {
        public string Identity => $"{Root}\\{SubKey}|{ValueName ?? "*"}".ToLowerInvariant();
    }

    /// <summary>
    /// Değer düzeyinde geri alma (H-13). Etkin bir kapsam varken <see cref="VerifiedRegistry"/>
    /// her yazma/silmeden hemen önce hedefin mevcut halini kaydeder (yoksa "yoktu" diye).
    /// Böylece ince ayar geri alınırken açık/kapalı tahmini yerine TAM ÖZGÜN DEĞER yazılır:
    /// kullanıcının kendi özel değeri ya da hiç olmayan bir politika anahtarı doğru geri gelir.
    /// </summary>
    public sealed class RegistryCapture : IDisposable
    {
        private const int MaxTreeValues = 2000;
        private static readonly AsyncLocal<RegistryCapture?> CurrentCapture = new();

        private readonly RegistryCapture? _previous;
        private readonly List<RegistryValueSnapshot> _items = new();
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private bool _disposed;

        private RegistryCapture(RegistryCapture? previous) => _previous = previous;

        public static RegistryCapture Begin()
        {
            var capture = new RegistryCapture(CurrentCapture.Value);
            CurrentCapture.Value = capture;
            return capture;
        }

        public IReadOnlyList<RegistryValueSnapshot> Items
        {
            get { lock (_items) return _items.ToList(); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(CurrentCapture.Value, this)) CurrentCapture.Value = _previous;
        }

        private void Add(RegistryValueSnapshot snapshot)
        {
            lock (_items)
            {
                // Aynı hedefe ikinci yazımda ilk (özgün) hal korunur.
                if (_seen.Add(snapshot.Identity)) _items.Add(snapshot);
            }
        }

        internal static void BeforeValueChange(RegistryKey root, string subKey, string valueName)
        {
            var capture = CurrentCapture.Value;
            if (capture == null) return;
            try
            {
                using var key = root.OpenSubKey(subKey, false);
                object? value = key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (key == null || value == null)
                {
                    capture.Add(new RegistryValueSnapshot(root.Name, subKey, valueName, false, null, null));
                    return;
                }
                var kind = key.GetValueKind(valueName);
                capture.Add(new RegistryValueSnapshot(root.Name, subKey, valueName, true, kind.ToString(), Encode(kind, value)));
            }
            catch
            {
                // Okunamayan değer yedeklenemez; ince ayar yine de uygulanır (geri alma bool'a düşer).
            }
        }

        /// <summary>
        /// Doğrudan <c>key.SetValue/DeleteValue</c> kullanan eski servisler için: açık anahtardaki
        /// değerin özgün halini kaydeder. Etkin kapsam yoksa hiçbir şey yapmaz.
        /// </summary>
        public static void Track(RegistryKey? key, string valueName)
        {
            var capture = CurrentCapture.Value;
            if (capture == null || key == null) return;
            try
            {
                int slash = key.Name.IndexOf('\\');
                string root = slash < 0 ? key.Name : key.Name[..slash];
                string subKey = slash < 0 ? string.Empty : key.Name[(slash + 1)..];
                object? value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value == null)
                {
                    capture.Add(new RegistryValueSnapshot(root, subKey, valueName, false, null, null));
                    return;
                }
                var kind = key.GetValueKind(valueName);
                capture.Add(new RegistryValueSnapshot(root, subKey, valueName, true, kind.ToString(), Encode(kind, value)));
            }
            catch
            {
            }
        }

        /// <summary>Tüm bir anahtarın özgün halini kaydeder (yoksa "yoktu": geri almada silinir).</summary>
        public static void TrackKey(RegistryKey root, string subKey) => BeforeKeyDelete(root, subKey);

        internal static void BeforeKeyDelete(RegistryKey root, string subKey)
        {
            var capture = CurrentCapture.Value;
            if (capture == null) return;
            try
            {
                using var key = root.OpenSubKey(subKey, false);
                if (key == null)
                {
                    capture.Add(new RegistryValueSnapshot(root.Name, subKey, null, false, null, null));
                    return;
                }
                int budget = MaxTreeValues;
                var tree = ReadTree(key, ref budget);
                if (budget < 0) return; // çok büyük anahtar: yedeklenmez
                capture.Add(new RegistryValueSnapshot(root.Name, subKey, null, true, "Tree", JsonSerializer.Serialize(tree)));
            }
            catch { }
        }

        #region Geri yükleme

        /// <summary>Kaydı özgün haline döndürür; başarılıysa true.</summary>
        public static bool Restore(RegistryValueSnapshot s)
        {
            var root = RootFromName(s.Root);
            if (root == null) return false;

            try
            {
                if (s.ValueName == null)
                {
                    if (!s.Existed) return VerifiedRegistry.DeleteKeyTree(root, s.SubKey);
                    var tree = JsonSerializer.Deserialize<KeyTree>(s.Data ?? "{}");
                    if (tree == null) return false;
                    root.DeleteSubKeyTree(s.SubKey, false);
                    WriteTree(root, s.SubKey, tree);
                    return true;
                }

                if (!s.Existed) return VerifiedRegistry.DeleteValue(root, s.SubKey, s.ValueName);

                var kind = Enum.Parse<RegistryValueKind>(s.Kind ?? nameof(RegistryValueKind.String));
                using var key = root.CreateSubKey(s.SubKey, true);
                if (key == null) return false;
                key.SetValue(s.ValueName, Decode(kind, s.Data), kind);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Kayıt zaten özgün halinde mi? (Yazma hiç gerçekleşmemiş ya da kullanıcı elle geri almış
        /// olabilir.) Böyle bir değer için geri alma yazma gerektirmez ve yönetici izni istemez.
        /// </summary>
        public static bool IsCurrent(RegistryValueSnapshot s)
        {
            var root = RootFromName(s.Root);
            if (root == null) return false;
            try
            {
                using var key = root.OpenSubKey(s.SubKey, false);
                if (s.ValueName == null) return !s.Existed && key == null;

                object? value = key?.GetValue(s.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (!s.Existed) return value == null;
                if (key == null || value == null) return false;
                var kind = key.GetValueKind(s.ValueName);
                return string.Equals(kind.ToString(), s.Kind, StringComparison.Ordinal) &&
                       string.Equals(Encode(kind, value), s.Data, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Değer düzeyindeki bir anlık görüntüyü geri yazan PowerShell satırı (yönetici olmadan
        /// HKLM'ye yazılamadığında tek UAC onayıyla toplu geri yükleme için). Var olan bir anahtar
        /// ağacının yeniden yazımı desteklenmez: null döner.
        /// </summary>
        public static string? ToPowerShell(RegistryValueSnapshot s)
        {
            if (RootFromName(s.Root) == null) return null;
            string path = Q($"Registry::{s.Root}\\{s.SubKey}");

            if (s.ValueName == null)
            {
                // Bakım'ın oluşturduğu anahtar: silinir. Var olan bir ağacın geri yazımı betiklenmez.
                return s.Existed ? null : $"if (Test-Path -LiteralPath {path}) {{ Remove-Item -LiteralPath {path} -Recurse -Force }}";
            }
            string name = Q(s.ValueName);

            if (!s.Existed)
                return $"if (Test-Path -LiteralPath {path}) {{ Remove-ItemProperty -LiteralPath {path} -Name {name} -ErrorAction SilentlyContinue }}";

            var kind = Enum.Parse<RegistryValueKind>(s.Kind ?? nameof(RegistryValueKind.String));
            string? literal = kind switch
            {
                RegistryValueKind.String or RegistryValueKind.ExpandString => Q(s.Data ?? string.Empty),
                RegistryValueKind.DWord => "([int]" + (s.Data ?? "0") + ")",
                RegistryValueKind.QWord => "([long]" + (s.Data ?? "0") + ")",
                RegistryValueKind.MultiString => "@(" + string.Join(",", (JsonSerializer.Deserialize<string[]>(s.Data ?? "[]") ?? Array.Empty<string>()).Select(Q)) + ")",
                RegistryValueKind.Binary => "([byte[]]@(" + string.Join(",", Convert.FromBase64String(s.Data ?? string.Empty)) + "))",
                _ => null
            };
            if (literal == null) return null;

            // New-Item -Force var olan anahtarı SİLİP yeniden oluşturur; yalnızca yoksa oluşturulur.
            return $"if (-not (Test-Path -LiteralPath {path})) {{ New-Item -Path {path} -Force | Out-Null }}; " +
                   $"New-ItemProperty -LiteralPath {path} -Name {name} -PropertyType {kind} -Value {literal} -Force | Out-Null";

            static string Q(string v) => "'" + v.Replace("'", "''") + "'";
        }

        private static RegistryKey? RootFromName(string name) => name.ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            "HKEY_USERS" => Registry.Users,
            _ => null
        };

        #endregion

        #region Kodlama

        private sealed class KeyTree
        {
            public List<TreeValue> Values { get; set; } = new();
            public Dictionary<string, KeyTree> SubKeys { get; set; } = new();
        }

        private sealed class TreeValue
        {
            public string Name { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
            public string? Data { get; set; }
        }

        private static KeyTree ReadTree(RegistryKey key, ref int budget)
        {
            var tree = new KeyTree();
            foreach (var name in key.GetValueNames())
            {
                if (--budget < 0) return tree;
                var kind = key.GetValueKind(name);
                var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value != null) tree.Values.Add(new TreeValue { Name = name, Kind = kind.ToString(), Data = Encode(kind, value) });
            }
            foreach (var sub in key.GetSubKeyNames())
            {
                using var child = key.OpenSubKey(sub, false);
                if (child != null) tree.SubKeys[sub] = ReadTree(child, ref budget);
                if (budget < 0) return tree;
            }
            return tree;
        }

        private static void WriteTree(RegistryKey root, string subKey, KeyTree tree)
        {
            using var key = root.CreateSubKey(subKey, true);
            if (key == null) return;
            foreach (var v in tree.Values)
            {
                var kind = Enum.Parse<RegistryValueKind>(v.Kind);
                key.SetValue(v.Name, Decode(kind, v.Data), kind);
            }
            foreach (var (name, child) in tree.SubKeys)
                WriteTree(root, subKey + "\\" + name, child);
        }

        private static string? Encode(RegistryValueKind kind, object value) => kind switch
        {
            RegistryValueKind.Binary or RegistryValueKind.None or RegistryValueKind.Unknown =>
                value is byte[] b ? Convert.ToBase64String(b) : null,
            RegistryValueKind.MultiString => JsonSerializer.Serialize(value as string[] ?? Array.Empty<string>()),
            RegistryValueKind.DWord => Convert.ToInt32(value).ToString(System.Globalization.CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => Convert.ToInt64(value).ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

        private static object Decode(RegistryValueKind kind, string? data) => kind switch
        {
            RegistryValueKind.Binary or RegistryValueKind.None or RegistryValueKind.Unknown =>
                data == null ? Array.Empty<byte>() : Convert.FromBase64String(data),
            RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(data ?? "[]") ?? Array.Empty<string>(),
            RegistryValueKind.DWord => int.Parse(data ?? "0", System.Globalization.CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => long.Parse(data ?? "0", System.Globalization.CultureInfo.InvariantCulture),
            _ => data ?? string.Empty
        };

        #endregion
    }
}
