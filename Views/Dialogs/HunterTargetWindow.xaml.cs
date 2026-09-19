using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.Views.Dialogs
{
    public partial class HunterTargetWindow : Window
    {
        private readonly IHunterService _hunterService;
        private readonly IEnumerable<InstalledAppItem>? _installedApps;
        private readonly Action<HunterTargetInfo>? _onTargetCaptured;
        private bool _isDraggingTarget;

        public HunterTargetWindow(IHunterService hunterService, IEnumerable<InstalledAppItem>? installedApps, Action<HunterTargetInfo> onTargetCaptured)
        {
            InitializeComponent();
            _hunterService = hunterService;
            _installedApps = installedApps;
            _onTargetCaptured = onTargetCaptured;

            MouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left && !_isDraggingTarget)
                {
                    try { DragMove(); } catch { }
                }
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
            e.Handled = true;
        }

        private void TargetCrosshair_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingTarget)
            {
                // Active dragging
            }
        }

        private void TargetCrosshair_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingTarget)
            {
                _isDraggingTarget = false;
                TargetCrosshair.ReleaseMouseCapture();
                Cursor = Cursors.Arrow;

                // Capture screen coordinates
                Point dropPoint = PointToScreen(e.GetPosition(this));
                int screenX = (int)dropPoint.X;
                int screenY = (int)dropPoint.Y;

                // Identify target under cursor
                var info = _hunterService.IdentifyTargetAtPoint(screenX, screenY, _installedApps);

                _onTargetCaptured?.Invoke(info);
                Close();
                e.Handled = true;
            }
        }
    }
}
