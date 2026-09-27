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

    public partial class HunterActionDialog : Wpf.Ui.Controls.FluentWindow
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

        private async void KillProcess_Click(object sender, RoutedEventArgs e)
        {
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (Bakım.Core.Safety.CriticalProcessPolicy.IsProtected(
                TargetInfo.ProcessId,
                TargetInfo.ProcessName,
                TargetInfo.ExecutablePath,
                winDir,
                Environment.ProcessId))
            {
                await Bakım.Helpers.FluentDialog.InfoAsync("Kritik sistem koruması", $"'{TargetInfo.ProcessName}' kritik bir Windows sistem bileşenidir; sistem kararlılığı için sonlandırılamaz.");
                return;
            }

            // Eskiden onaysız sonlandırıyordu.
            if (!await Bakım.Helpers.FluentDialog.ConfirmAsync("Süreci sonlandır",
                    $"{TargetInfo.ProcessName} (PID: {TargetInfo.ProcessId}) zorla sonlandırılacak. Kaydedilmemiş veriler kaybolabilir.",
                    primaryText: "Sonlandır", destructive: true)) return;

            try
            {
                var proc = Process.GetProcessById(TargetInfo.ProcessId);
                proc.Kill();
                SelectedAction = HunterAction.KillProcess;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                await Bakım.Helpers.FluentDialog.InfoAsync("Süreç sonlandırılamadı", ex.Message);
            }
        }

        private async void OpenFileLocation_Click(object sender, RoutedEventArgs e)
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
                await Bakım.Helpers.FluentDialog.InfoAsync("Dosya konumu açılamadı", ex.Message);
            }
        }

        private async void ThreatAnalysis_Click(object sender, RoutedEventArgs e)
        {
            string exe = TargetInfo.ExecutablePath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                await Bakım.Helpers.FluentDialog.InfoAsync("Dosya bulunamadı", "Hedef çalıştırılabilir dosya bulunamadı.");
                return;
            }

            try
            {
                var analyzer = App.GetService<Services.IFileThreatAnalyzerService>();
                var autoruns = App.GetService<Services.IAutorunsScannerEngine>();
                var vtService = App.GetService<Services.IVirusTotalCheckService>();
                ThreatAnalysisResult result;
                using (Bakım.Core.History.AnalysisContext.Begin(Bakım.Core.History.AnalysisSource.Processes, $"{TargetInfo.ProcessName}.exe (PID {TargetInfo.ProcessId})"))
                {
                    result = await analyzer.AnalyzeFileAsync(exe);
                }
                var dialog = new ThreatAnalysisDialog(result, analyzer, autoruns, vtService);
                dialog.Owner = this;
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                await Bakım.Helpers.FluentDialog.InfoAsync("Analiz başlatılamadı", ex.Message);
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
