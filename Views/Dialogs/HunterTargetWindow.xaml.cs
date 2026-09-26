using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.Views.Dialogs
{
    public partial class HunterTargetWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private readonly IHunterService _hunterService;
        private readonly IEnumerable<InstalledAppItem>? _installedApps;
        private readonly Action<HunterTargetInfo, HunterAction>? _onActionRequested;
        private HunterHighlightOverlayWindow? _overlayWindow;
        private bool _isDraggingTarget;

        public HunterTargetWindow(
            IHunterService hunterService, 
            IEnumerable<InstalledAppItem>? installedApps, 
            Action<HunterTargetInfo, HunterAction> onActionRequested)
        {
            InitializeComponent();
            _hunterService = hunterService;
            _installedApps = installedApps;
            _onActionRequested = onActionRequested;

            MouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left && !_isDraggingTarget)
                {
                    try { DragMove(); } catch { }
                }
            };

            Loaded += (s, e) =>
            {
                _overlayWindow = new HunterHighlightOverlayWindow();
                _overlayWindow.Hide();
            };

            Closed += (s, e) =>
            {
                try
                {
                    _overlayWindow?.Close();
                }
                catch { }
            };
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TargetCrosshair_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingTarget = true;
            TargetCrosshair.CaptureMouse();
            Cursor = Cursors.Cross;

            // Make this window transparent to hit-testing so WindowFromPoint sees right through it!
            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);

            e.Handled = true;
        }

        private void TargetCrosshair_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingTarget)
            {
                var info = _hunterService.IdentifyTargetAtCurrentCursor(_installedApps);

                if (_overlayWindow != null)
                {
                    if (info.WindowWidth > 0 && info.WindowHeight > 0)
                    {
                        _overlayWindow.UpdateTarget(info);
                    }
                    else
                    {
                        _overlayWindow.HideTarget();
                    }
                }
            }
        }

        private void TargetCrosshair_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingTarget)
            {
                _isDraggingTarget = false;
                TargetCrosshair.ReleaseMouseCapture();
                Cursor = Cursors.Arrow;

                // Restore normal hit-testing
                var helper = new WindowInteropHelper(this);
                int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
                SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);

                // Hide overlay highlight
                _overlayWindow?.HideTarget();

                // Final target capture using Win32 cursor position
                var info = _hunterService.IdentifyTargetAtCurrentCursor(_installedApps);

                e.Handled = true;

                // Handle Self-Protection (Bakım)
                if (info.IsSelfProcess)
                {
                    MessageBox.Show(
                        "Bakım kendi kendini hedef alamaz!\n\nLütfen sisteminizde kaldırmak veya yönetmek istediğiniz harici bir program penceresini hedefleyin.",
                        "Pencereden seç - Korumalı uygulama",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                // Handle Windows Shell Protection
                if (info.IsSystemShell)
                {
                    MessageBox.Show(
                        "Windows Masaüstü ve Görev Çubuğu çekirdek işletim sistemi bileşenidir ve kaldırılamaz.",
                        "Pencereden seç - Sistem koruması",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Check if target was found
                if (!info.IsFound)
                {
                    MessageBox.Show(
                        "İmleç altında çalışan aktif bir uygulama penceresi tespit edilemedi.\nLütfen hedef simgesini kaldırmak istediğiniz programın penceresine bırakın.",
                        "Pencereden seç",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                // Valid external application captured!
                Hide(); // Temporarily hide hunter target
                var actionDialog = new HunterActionDialog(info);
                bool? result = actionDialog.ShowDialog();

                if (result == true && actionDialog.SelectedAction != HunterAction.Cancel)
                {
                    _onActionRequested?.Invoke(info, actionDialog.SelectedAction);
                    Close();
                }
                else
                {
                    Show(); // Re-show if cancelled
                }
            }
        }
    }
}
