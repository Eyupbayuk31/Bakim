using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Bakım.Core.GameMode;

namespace Bakım.Controls
{
    /// <summary>
    /// Çipli ad listesi düzenleyici.
    ///
    /// Kullanım:
    ///   &lt;c:ChipListEditor Items="{Binding SuspendAppItems}"
    ///                     Suggestions="{Binding SuspendSuggestions}"
    ///                     PlaceholderText="Uygulama adı yazıp Enter'a basın"/&gt;
    ///
    /// Items bir <see cref="ObservableCollection{T}"/> olmalıdır; düzenleyici doğrudan onu değiştirir,
    /// görünüm modeli CollectionChanged ile kaydeder.
    /// </summary>
    public partial class ChipListEditor : UserControl
    {
        public ChipListEditor()
        {
            InitializeComponent();
            UpdateSuggestions();
        }

        public static readonly DependencyProperty ItemsProperty =
            DependencyProperty.Register(nameof(Items), typeof(ObservableCollection<string>), typeof(ChipListEditor),
                new PropertyMetadata(null, OnItemsChanged));

        public ObservableCollection<string>? Items
        {
            get => (ObservableCollection<string>?)GetValue(ItemsProperty);
            set => SetValue(ItemsProperty, value);
        }

        public static readonly DependencyProperty SuggestionsProperty =
            DependencyProperty.Register(nameof(Suggestions), typeof(IEnumerable<string>), typeof(ChipListEditor),
                new PropertyMetadata(null, (d, _) => ((ChipListEditor)d).UpdateSuggestions()));

        public IEnumerable<string>? Suggestions
        {
            get => (IEnumerable<string>?)GetValue(SuggestionsProperty);
            set => SetValue(SuggestionsProperty, value);
        }

        public static readonly DependencyProperty PlaceholderTextProperty =
            DependencyProperty.Register(nameof(PlaceholderText), typeof(string), typeof(ChipListEditor),
                new PropertyMetadata("Ad yazıp Enter'a basın"));

        public string PlaceholderText
        {
            get => (string)GetValue(PlaceholderTextProperty);
            set => SetValue(PlaceholderTextProperty, value);
        }

        public static readonly DependencyProperty InputAutomationNameProperty =
            DependencyProperty.Register(nameof(InputAutomationName), typeof(string), typeof(ChipListEditor),
                new PropertyMetadata("Yeni ad ekle"));

        public string InputAutomationName
        {
            get => (string)GetValue(InputAutomationNameProperty);
            set => SetValue(InputAutomationNameProperty, value);
        }

        private static readonly DependencyPropertyKey VisibleSuggestionsKey =
            DependencyProperty.RegisterReadOnly(nameof(VisibleSuggestions), typeof(IReadOnlyList<string>), typeof(ChipListEditor),
                new PropertyMetadata(Array.Empty<string>()));

        /// <summary>Henüz listede olmayan öneriler.</summary>
        public IReadOnlyList<string> VisibleSuggestions => (IReadOnlyList<string>)GetValue(VisibleSuggestionsKey.DependencyProperty);

        private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = (ChipListEditor)d;
            if (e.OldValue is INotifyCollectionChanged oldList) oldList.CollectionChanged -= editor.OnCollectionChanged;
            if (e.NewValue is INotifyCollectionChanged newList) newList.CollectionChanged += editor.OnCollectionChanged;
            editor.UpdateSuggestions();
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateSuggestions();

        private void UpdateSuggestions()
        {
            var current = Items ?? new ObservableCollection<string>();
            var visible = (Suggestions ?? Enumerable.Empty<string>())
                .Where(s => !current.Any(c => string.Equals(c, s, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();
            SetValue(VisibleSuggestionsKey, visible);
            if (SuggestionPanel != null)
                SuggestionPanel.Visibility = visible.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Commit()
        {
            if (Items == null || string.IsNullOrWhiteSpace(Input.Text)) return;
            ProcessNameList.AddTo(Items, Input.Text);
            Input.Text = string.Empty;
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.OemComma)
            {
                Commit();
                e.Handled = true;
            }
            else if (e.Key == Key.Back && string.IsNullOrEmpty(Input.Text) && Items is { Count: > 0 } items)
            {
                items.RemoveAt(items.Count - 1);
                e.Handled = true;
            }
        }

        private void OnInputLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit();

        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string name } && Items != null)
            {
                Items.Remove(name);
                Input.Focus();
            }
        }

        private void OnSuggestionClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string name } && Items != null)
                ProcessNameList.AddTo(Items, name);
        }
    }
}
