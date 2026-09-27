using System;
using System.Windows;
using System.Windows.Threading;
using Bakım.Models;

namespace Bakım.Views.Dialogs
{
    /// <summary>
    /// Bakım dışından kaldırılan program için köşe bildirimi: "Kalıntıları tara" Kaldırıcı'nın kalıntı akışını açar.
    /// İki dakika içinde yanıt verilmezse kendiliğinden kapanır (kalıntılar Kaldırıcı'dan her zaman taranabilir).
    /// </summary>
    public partial class UninstallDetectedFlyoutWindow : Window
    {
        private readonly DispatcherTimer _autoClose = new() { Interval = TimeSpan.FromMinutes(2) };

        public InstalledAppItem App { get; }

        /// <summary>Kullanıcı "Kalıntıları tara"yı seçti.</summary>
        public event Action<InstalledAppItem>? ScanRequested;

        public UninstallDetectedFlyoutWindow(InstalledAppItem app)
        {
            InitializeComponent();
            App = app;
            AppNameText.Text = app.DisplayName;
            AppNameText.ToolTip = app.DisplayName;
            if (app.IconSource != null)
            {
                AppIcon.Source = app.IconSource;
                AppIcon.Visibility = Visibility.Visible;
                FallbackIcon.Visibility = Visibility.Collapsed;
            }
            _autoClose.Tick += (_, _) => Close();
            Closed += (_, _) => _autoClose.Stop();
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth - 12;
            Top = workArea.Bottom - ActualHeight - 12;
            _autoClose.Start();
        }

        private void OnDismissClicked(object sender, RoutedEventArgs e) => Close();

        private void OnScanClicked(object sender, RoutedEventArgs e)
        {
            Close();
            ScanRequested?.Invoke(App);
        }
    }
}
