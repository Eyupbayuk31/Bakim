using System.Security.Principal;
using System.Diagnostics;
using System.Windows;

namespace Bakım.Helpers
{
    public static class UacHelper
    {
        public static bool IsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static void RestartAsAdministrator()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = Environment.ProcessPath,
                    Verb = "runas"
                };
                Process.Start(processInfo);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Yönetici yetkisi başlatılamadı: {ex.Message}", "Yetki Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
