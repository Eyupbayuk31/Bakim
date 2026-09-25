using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Bakım.Helpers;

namespace Bakım.Controls
{
    [ContentProperty(nameof(GatedContent))]
    public partial class AdminGate : UserControl
    {
        private static readonly bool IsAdmin = UacHelper.IsAdministrator();

        public AdminGate()
        {
            InitializeComponent();
            Apply();
        }

        public static readonly DependencyProperty GatedContentProperty =
            DependencyProperty.Register(nameof(GatedContent), typeof(object), typeof(AdminGate));

        public object? GatedContent
        {
            get => GetValue(GatedContentProperty);
            set => SetValue(GatedContentProperty, value);
        }

        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register(nameof(Message), typeof(string), typeof(AdminGate),
                new PropertyMetadata("Bu bölümdeki işlemler yönetici izni gerektirir.", (d, _) => ((AdminGate)d).Apply()));

        public string Message
        {
            get => (string)GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        private void Apply()
        {
            if (Banner == null) return;
            MessageText.Text = Message;
            Banner.Visibility = IsAdmin ? Visibility.Collapsed : Visibility.Visible;
            Body.Opacity = IsAdmin ? 1.0 : 0.55;
            Body.IsHitTestVisible = IsAdmin;
            Body.IsEnabled = IsAdmin;
        }

        private void OnRestartClicked(object sender, RoutedEventArgs e) => UacHelper.RestartAsAdministrator();
    }
}
