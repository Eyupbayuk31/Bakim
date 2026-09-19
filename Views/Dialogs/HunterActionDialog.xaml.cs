using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Bakım.Models;

namespace Bakım.Views.Dialogs
{
    public enum HunterAction
    {
        Cancel,
        Uninstall,
        ForceUninstall,
        KillProcess,
        OpenFileLocation,
        ThreatAnalysis
    }

    public partial class HunterActionDialog : Window
    {
        public HunterTargetInfo TargetInfo { get; }
        public HunterAction SelectedAction { get; private set; } = HunterAction.Cancel;

        public HunterActionDialog(HunterTargetInfo targetInfo)
        {
            InitializeComponent();
            TargetInfo = targetInfo;
            Loaded += HunterActionDialog_Loaded;

            MouseDown += (s, e) =>
            {
                if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                {
                    try { DragMove(); } catch { }
                }
            };
        }

        private void HunterActionDialog_Loaded(object sender, RoutedEventArgs e)
        {
            string appName = TargetInfo.MatchedApp?.DisplayName ??
                (!string.IsNullOrWhiteSpace(TargetInfo.WindowTitle) ? TargetInfo.WindowTitle : TargetInfo.ProcessName);

            TxtDisplayName.Text = appName;
            TxtProcessInfo.Text = $"{TargetInfo.ProcessName}.exe (PID: {TargetInfo.ProcessId})";
            TxtExePath.Text = !string.IsNullOrWhiteSpace(TargetInfo.ExecutablePath) ? TargetInfo.ExecutablePath : "Konum belirlenemedi";

            if (TargetInfo.MatchedApp != null)
            {
                TxtMatchStatus.Text = " • [Yüklü Programlar Listesinde Kayıtlı]";
                TxtMatchStatus.Foreground = (System.Windows.Media.Brush)FindResource("SystemFillColorSuccessBrush");
            }
            else
            {
                TxtMatchStatus.Text = " • [Bağımsız Süreç / Özel Hedef]";
                TxtMatchStatus.Foreground = (System.Windows.Media.Brush)FindResource("SystemFillColorCautionBrush");
            }

            // Extract Icon
            TryLoadAppIcon();
        }

        private void TryLoadAppIcon()
        {
            try
            {
                if (TargetInfo.MatchedApp?.IconSource != null)
                {
                    TargetAppIcon.Source = TargetInfo.MatchedApp.IconSource;
                    FallbackIcon.Visibility = Visibility.Collapsed;
                    return;
                }

                if (!string.IsNullOrWhiteSpace(TargetInfo.ExecutablePath) && File.Exists(TargetInfo.ExecutablePath))
                {
                    using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(TargetInfo.ExecutablePath);
                    if (sysIcon != null)
                    {
                        var bitmap = sysIcon.ToBitmap();
                        var hBitmap = bitmap.GetHbitmap();
                        try
                        {
                            var wpfBmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                hBitmap,
                                IntPtr.Zero,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            wpfBmp.Freeze();
                            TargetAppIcon.Source = wpfBmp;
                            FallbackIcon.Visibility = Visibility.Collapsed;
                            return;
                        }
                        finally
                        {
                            DeleteObject(hBitmap);
                        }
                    }
                }
            }
            catch { }

            FallbackIcon.Visibility = Visibility.Visible;
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            SelectedAction = HunterAction.Uninstall;
            DialogResult = true;
            Close();
        }

        private void ForceUninstall_Click(object sender, RoutedEventArgs e)
        {
            SelectedAction = HunterAction.ForceUninstall;
            DialogResult = true;
            Close();
        }

        private void KillProcess_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var proc = Process.GetProcessById(TargetInfo.ProcessId);
                proc.Kill();
                MessageBox.Show($"{TargetInfo.ProcessName} (PID: {TargetInfo.ProcessId}) süreci başarıyla sonlandırıldı.", "Süreç Sonlandırıldı", MessageBoxButton.OK, MessageBoxImage.Information);
                SelectedAction = HunterAction.KillProcess;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Süreç sonlandırılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(TargetInfo.ExecutablePath) && File.Exists(TargetInfo.ExecutablePath))
                {
                    Process.Start("explorer.exe", $"/select,\"{TargetInfo.ExecutablePath}\"");
                }
                else if (!string.IsNullOrWhiteSpace(TargetInfo.ExecutablePath))
                {
                    string dir = Path.GetDirectoryName(TargetInfo.ExecutablePath) ?? string.Empty;
                    if (Directory.Exists(dir))
                    {
                        Process.Start("explorer.exe", dir);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dosya konumu açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ThreatAnalysis_Click(object sender, RoutedEventArgs e)
        {
            string exe = TargetInfo.ExecutablePath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                MessageBox.Show("Hedef çalıştırılabilir dosyası bulunamadı.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var analyzer = new Services.FileThreatAnalyzerService();
                var result = await analyzer.AnalyzeFileAsync(exe);
                var dialog = new ThreatAnalysisDialog(result, analyzer);
                dialog.Owner = this;
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Analiz başlatılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedAction = HunterAction.Cancel;
            DialogResult = false;
            Close();
        }
    }
}
