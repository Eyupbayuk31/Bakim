using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Bakım.Services;

namespace Bakım.Views.Dialogs
{
    public partial class UpdateDialogView : Wpf.Ui.Controls.FluentWindow
    {
        private readonly UpdateInfo _updateInfo;
        private bool _isDownloading = false;

        public UpdateDialogView(UpdateInfo updateInfo)
        {
            InitializeComponent();
            _updateInfo = updateInfo;

            CurrentVersionText.Text = $"v{_updateInfo.CurrentVersion}";
            LatestVersionText.Text = $"v{_updateInfo.LatestVersion}";
            ReleaseNotesText.Text = string.IsNullOrWhiteSpace(_updateInfo.ReleaseNotes) 
                ? AutoUpdateService.GetDefaultChangelog() 
                : _updateInfo.ReleaseNotes;

            if (_updateInfo.FileSizeBytes > 0)
            {
                SizeText.Text = $"{(_updateInfo.FileSizeBytes / (1024.0 * 1024.0)):F1} MB";
                SizeBadge.Visibility = Visibility.Visible;
            }
            else
            {
                SizeBadge.Visibility = Visibility.Collapsed;
            }

            MouseDown += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch { }
                }
            };
        }

        private void OnCloseClicked(object sender, RoutedEventArgs e)
        {
            if (_isDownloading) return;
            Close();
        }

        private void OnLaterClicked(object sender, RoutedEventArgs e)
        {
            if (_isDownloading) return;
            Close();
        }

        private async void OnUpdateNowClicked(object sender, RoutedEventArgs e)
        {
            if (_isDownloading) return;

            if (string.IsNullOrWhiteSpace(_updateInfo.DownloadUrl))
            {
                MessageBox.Show(
                    "Güncelleme paketi indirme bağlantısı alınamadı. Lütfen daha sonra tekrar deneyiniz.",
                    "Güncelleme Bildirimi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _isDownloading = true;
            UpdateButton.IsEnabled = false;
            LaterButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;
            StatusLabel.Text = "Sessiz güncelleme paketi indiriliyor...";

            try
            {
                await AutoUpdateService.DownloadAndExecuteInstallerAsync(_updateInfo.DownloadUrl, progress =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        DownloadProgressBar.Value = progress;
                        PercentageLabel.Text = $"%{progress}";
                    });
                }, _updateInfo.FileSizeBytes);
            }
            catch (Exception ex)
            {
                _isDownloading = false;
                UpdateButton.IsEnabled = true;
                LaterButton.IsEnabled = true;
                ProgressPanel.Visibility = Visibility.Collapsed;

                MessageBox.Show(
                    $"Güncelleme indirilemedi: {ex.Message}\nLütfen internet bağlantınızı kontrol edip tekrar deneyiniz.",
                    "Güncelleme Hatası",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
