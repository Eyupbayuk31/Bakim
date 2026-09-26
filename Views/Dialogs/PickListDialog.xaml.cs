using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Views.Dialogs
{
    /// <summary>Seçim diyaloğundaki bir satır.</summary>
    public sealed partial class PickItem : ObservableObject
    {
        public PickItem(string value, string title, string subtitle = "", string meta = "")
        {
            Value = value;
            Title = title;
            Subtitle = subtitle;
            Meta = meta;
        }

        /// <summary>Seçilince döndürülen değer (ör. süreç adı).</summary>
        public string Value { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public string Meta { get; }

        [ObservableProperty] private bool _isSelected;
    }

    /// <summary>
    /// Çoklu seçim diyaloğu. Kullanım:
    ///   var picked = PickListDialog.Show(owner, "Çalışan uygulamalar", "Başlık", "Açıklama", items, "Liste boş.");
    /// </summary>
    public partial class PickListDialog : Wpf.Ui.Controls.FluentWindow
    {
        private readonly IReadOnlyList<PickItem> _items;

        private PickListDialog(IReadOnlyList<PickItem> items, string heading, string description, string emptyMessage)
        {
            _items = items;
            Heading = heading;
            Description = description;
            EmptyMessage = emptyMessage;
            View = CollectionViewSource.GetDefaultView(items);
            InitializeComponent();
            foreach (var item in items) item.PropertyChanged += (_, _) => UpdateState();
            UpdateState();
            Loaded += (_, _) => SearchBox.Focus();
        }

        public ICollectionView View { get; }
        public string Heading { get; }
        public string Description { get; }
        public string EmptyMessage { get; }

        /// <summary>Diyaloğu gösterir; seçilen değerleri döndürür (iptalde boş liste).</summary>
        public static IReadOnlyList<string> Show(Window? owner, string windowTitle, string heading, string description,
            IReadOnlyList<PickItem> items, string emptyMessage)
        {
            var dialog = new PickListDialog(items, heading, description, emptyMessage)
            {
                Title = windowTitle,
                Owner = owner
            };
            if (owner == null) dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return dialog.ShowDialog() == true
                ? items.Where(i => i.IsSelected).Select(i => i.Value).ToList()
                : Array.Empty<string>();
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchBox.Text?.Trim() ?? string.Empty;
            View.Filter = query.Length == 0
                ? null
                : o => o is PickItem item &&
                       (item.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                        item.Subtitle.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                        item.Value.Contains(query, StringComparison.OrdinalIgnoreCase));
            UpdateState();
        }

        private void UpdateState()
        {
            int selected = _items.Count(i => i.IsSelected);
            CountText.Text = selected == 0 ? $"{_items.Count} öğe" : $"{selected} seçili";
            AcceptButton.IsEnabled = selected > 0;
            EmptyText.Visibility = View.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnAccept(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
