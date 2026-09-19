using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Bakım.Services;

namespace Bakım.Views.Dialogs
{
    public partial class UpdateDialogView : Window
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
                ? "Bu sürüm için detaylı değişiklik açıklaması girilmedi." 
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

        private void OnViewOnGitHubClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = string.IsNullOrWhiteSpace(_updateInfo.ReleasePageUrl)
                    ? "https://github.com/Eyupbayuk31/Bakim/releases"
                    : _updateInfo.ReleasePageUrl;

                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        private async void OnUpdateNowClicked(object sender, RoutedEventArgs e)
        {
            if (_isDownloading) return;

            if (string.IsNullOrWhiteSpace(_updateInfo.DownloadUrl))
            {
                // Fallback to release page
                OnViewOnGitHubClicked(sender, e);
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
                });
            }
            catch (Exception ex)
            {
                _isDownloading = false;
                UpdateButton.IsEnabled = true;
                LaterButton.IsEnabled = true;
                ProgressPanel.Visibility = Visibility.Collapsed;

                MessageBox.Show(
                    $"Güncelleme indirilemedi: {ex.Message}\nTarayıcı üzerinden manuel indirme sayfası açılıyor.",
                    "Güncelleme Hatası",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                OnViewOnGitHubClicked(sender, e);
            }
        }
    }
}
