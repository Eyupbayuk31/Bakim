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
