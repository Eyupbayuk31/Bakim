using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Bakım.Controls
{
    /// <summary>
    /// Klavye kısayolu tuş kapakları.
    ///
    /// Kullanım: &lt;c:KeyCap Shortcut="Ctrl+Shift+G"/&gt;
    /// </summary>
    public partial class KeyCap : UserControl
    {
        public KeyCap()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty ShortcutProperty =
            DependencyProperty.Register(nameof(Shortcut), typeof(string), typeof(KeyCap),
                new PropertyMetadata(string.Empty, (d, _) => ((KeyCap)d).UpdateKeys()));

        public string Shortcut
        {
            get => (string)GetValue(ShortcutProperty);
            set => SetValue(ShortcutProperty, value);
        }

        private static readonly DependencyPropertyKey KeysPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(Keys), typeof(IReadOnlyList<string>), typeof(KeyCap),
                new PropertyMetadata(Array.Empty<string>()));

        public static readonly DependencyProperty KeysProperty = KeysPropertyKey.DependencyProperty;

        public IReadOnlyList<string> Keys => (IReadOnlyList<string>)GetValue(KeysProperty);

        private void UpdateKeys() => SetValue(KeysPropertyKey, Split(Shortcut));

        /// <summary>"Ctrl+Shift+G" → ["Ctrl", "Shift", "G"]; "Ctrl++" → ["Ctrl", "+"].</summary>
        public static IReadOnlyList<string> Split(string? shortcut)
        {
            var keys = new List<string>();
            if (string.IsNullOrWhiteSpace(shortcut)) return keys;
            foreach (var part in shortcut.Split('+'))
            {
                var key = part.Trim();
                if (key.Length > 0) keys.Add(key);
            }
            if (shortcut.TrimEnd().EndsWith("++", StringComparison.Ordinal)) keys.Add("+");
            return keys;
        }
    }
}
