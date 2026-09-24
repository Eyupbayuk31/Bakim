using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Bakım.Models;

namespace Bakım.Views.Dialogs
{
    public partial class HunterHighlightOverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public HunterHighlightOverlayWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Make the overlay completely click-through, toolwindow, and non-activating
            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        public void UpdateTarget(HunterTargetInfo info)
        {
            if (info.WindowWidth <= 0 || info.WindowHeight <= 0)
            {
                HideTarget();
                return;
            }

            // Calculate DPI scaling
            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            Left = info.WindowLeft / dpiX;
            Top = info.WindowTop / dpiY;
            Width = info.WindowWidth / dpiX;
            Height = info.WindowHeight / dpiY;

            if (info.IsSelfProcess)
            {
                TargetFrame.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                TargetFrame.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#15EF4444"));
                HudTitle.Text = "Bakım (Korumalı Uygulama)";
                HudSubtitle.Text = "Bakım kendi kendini kaldıramaz";
            }
            else if (info.IsSystemShell)
            {
                TargetFrame.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                TargetFrame.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#15F59E0B"));
                HudTitle.Text = "Windows Kabuğu / Gezgini";
                HudSubtitle.Text = "Sistem kararlılığı için korumalıdır";
            }
            else
            {
                TargetFrame.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"));
                TargetFrame.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1538BDF8"));
                string appName = info.MatchedApp?.DisplayName ?? (!string.IsNullOrWhiteSpace(info.WindowTitle) ? info.WindowTitle : info.ProcessName);
                HudTitle.Text = appName;
                HudSubtitle.Text = $"Süreç: {info.ProcessName}.exe (PID: {info.ProcessId})";
            }

            if (Visibility != Visibility.Visible)
            {
                Visibility = Visibility.Visible;
            }
        }

        public void HideTarget()
        {
            if (Visibility == Visibility.Visible)
            {
                Visibility = Visibility.Collapsed;
            }
        }
    }
}
