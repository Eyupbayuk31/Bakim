using System.Windows;
using Wpf.Ui.Controls;
using Bakım.ViewModels;

namespace Bakım
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// Wpf.Ui FluentWindow miras alınır; sıfır code-behind ile saf navigasyon yürütülür.
    /// </summary>
    public partial class LoginWindow : FluentWindow
    {
        public LoginWindow()
        {
            InitializeComponent();

            if (DataContext is LoginViewModel vm)
            {
                vm.LoginSucceeded += OnLoginSucceeded;
            }

            Loaded += (s, e) => TxtUser.Focus();
        }

        private void OnLoginSucceeded()
        {
            var mainWindow = new MainWindow();
            Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
            Close();
        }
    }
}
