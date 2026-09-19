using System.Windows;
using Wpf.Ui.Controls;
using Bakım.ViewModels;

namespace Bakım
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// Wpf.Ui FluentWindow miras alınır; sıfır code-behind ile saf navigasyon yürütülür.
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            if (DataContext is MainViewModel vm)
            {
                vm.LogoutRequested += OnLogoutRequested;
            }
        }

        private void OnLogoutRequested()
        {
            var loginWindow = new LoginWindow();
            Application.Current.MainWindow = loginWindow;
            loginWindow.Show();
            Close();
        }
    }
}