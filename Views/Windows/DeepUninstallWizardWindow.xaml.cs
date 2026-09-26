using System.Windows;
using System.Windows.Input;
using Bakım.ViewModels;

namespace Bakım.Views.Windows
{
    public partial class DeepUninstallWizardWindow : Wpf.Ui.Controls.FluentWindow
    {
        public DeepUninstallWizardViewModel ViewModel { get; }

        public DeepUninstallWizardWindow(DeepUninstallWizardViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = viewModel;

            ViewModel.RequestClose += OnRequestClose;
        }

        private void OnRequestClose(bool wasCleaned)
        {
            try
            {
                DialogResult = wasCleaned;
            }
            catch
            {
                // In non-modal mode DialogResult might throw; safe fallback
            }

            Close();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    DragMove();
                }
                catch { }
            }
        }
    }
}
