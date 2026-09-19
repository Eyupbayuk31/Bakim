using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Bakım.Models;
using Bakım.Services;
using Bakım.ViewModels;

namespace Bakım.Views.Dialogs
{
    public partial class ResidualCleanupDialog : Window
    {
        public ResidualCleanupViewModel ViewModel { get; }

        public ResidualCleanupDialog(InstalledAppItem targetApp, System.Collections.Generic.IEnumerable<ResidualItem> items, IResidualScannerEngine? scanner = null)
        {
            InitializeComponent();
            ViewModel = new ResidualCleanupViewModel(targetApp, items, scanner);
            DataContext = ViewModel;

            ViewModel.RequestClose += OnRequestClose;

            Loaded += ResidualCleanupDialog_Loaded;

            MouseDown += (s, e) =>
            {
                if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                {
                    try { DragMove(); } catch { }
                }
            };
        }

        private void OnRequestClose(bool wasCleaned)
        {
            try
            {
                DialogResult = wasCleaned;
            }
            catch
            {
                // In case window wasn't shown modally
            }
            Close();
        }

        private void ResidualCleanupDialog_Loaded(object sender, RoutedEventArgs e)
        {
            TryLoadAppIcon();
        }

        private void TryLoadAppIcon()
        {
            try
            {
                string? iconPath = ViewModel.DisplayIconPath;
                if (!string.IsNullOrWhiteSpace(iconPath))
                {
                    // If icon path contains comma (index), strip it
                    if (iconPath.Contains(','))
                    {
                        iconPath = iconPath.Split(',')[0].Trim('\"', ' ');
                    }

                    if (File.Exists(iconPath))
                    {
                        var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(iconPath);
                        if (sysIcon != null)
                        {
                            using var bmp = sysIcon.ToBitmap();
                            using var ms = new MemoryStream();
                            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            ms.Position = 0;

                            var bi = new BitmapImage();
                            bi.BeginInit();
                            bi.StreamSource = ms;
                            bi.CacheOption = BitmapCacheOption.OnLoad;
                            bi.EndInit();
                            bi.Freeze();

                            ImgAppIcon.Source = bi;
                            ImgAppIcon.Visibility = Visibility.Visible;
                            FallbackIcon.Visibility = Visibility.Collapsed;
                            return;
                        }
                    }
                }
            }
            catch { }

            ImgAppIcon.Visibility = Visibility.Collapsed;
            FallbackIcon.Visibility = Visibility.Visible;
        }
    }
}
