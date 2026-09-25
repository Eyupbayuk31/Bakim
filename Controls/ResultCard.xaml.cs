using System.Collections;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Bakım.Controls
{
    public partial class ResultCard : UserControl
    {
        public ResultCard()
        {
            InitializeComponent();
            Loaded += (_, _) => Refresh();
        }

        public static readonly DependencyProperty TitleProperty = Register<string>(nameof(Title), string.Empty);
        public static readonly DependencyProperty SucceededCountProperty = Register<int>(nameof(SucceededCount), 0);
        public static readonly DependencyProperty SkippedCountProperty = Register<int>(nameof(SkippedCount), 0);
        public static readonly DependencyProperty FailedCountProperty = Register<int>(nameof(FailedCount), 0);
        public static readonly DependencyProperty DetailsProperty = Register<IEnumerable?>(nameof(Details), null);
        public static readonly DependencyProperty UndoCommandProperty = Register<ICommand?>(nameof(UndoCommand), null);
        public static readonly DependencyProperty UndoCommandParameterProperty = Register<object?>(nameof(UndoCommandParameter), null);

        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public int SucceededCount { get => (int)GetValue(SucceededCountProperty); set => SetValue(SucceededCountProperty, value); }
        public int SkippedCount { get => (int)GetValue(SkippedCountProperty); set => SetValue(SkippedCountProperty, value); }
        public int FailedCount { get => (int)GetValue(FailedCountProperty); set => SetValue(FailedCountProperty, value); }
        public IEnumerable? Details { get => (IEnumerable?)GetValue(DetailsProperty); set => SetValue(DetailsProperty, value); }
        public ICommand? UndoCommand { get => (ICommand?)GetValue(UndoCommandProperty); set => SetValue(UndoCommandProperty, value); }
        public object? UndoCommandParameter { get => GetValue(UndoCommandParameterProperty); set => SetValue(UndoCommandParameterProperty, value); }

        private static DependencyProperty Register<T>(string name, T defaultValue) =>
            DependencyProperty.Register(name, typeof(T), typeof(ResultCard),
                new PropertyMetadata(defaultValue, (d, _) => ((ResultCard)d).Refresh()));

        private void Refresh()
        {
            if (SkippedPanel == null) return;
            SkippedPanel.Visibility = SkippedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            FailedPanel.Visibility = FailedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            DetailsExpander.Visibility = Details?.Cast<object>().Any() == true ? Visibility.Visible : Visibility.Collapsed;
            UndoButton.Visibility = UndoCommand != null ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnOpenActivityClicked(object sender, RoutedEventArgs e) =>
            App.TryGetService<Services.INavigationService>()?.Navigate("Activity");
    }
}
