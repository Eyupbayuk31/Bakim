using System.Windows;
using Wpf.Ui.Controls;
using Bakım.ViewModels;

namespace Bakım
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// Wpf.Ui FluentWindow ile modern oturum açma arayüzü.
    /// PasswordBox güvenliği ve DPAPI kimlik yönetimi ile entegre çalışır.
    /// </summary>
    public partial class LoginWindow : FluentWindow
    {
        public LoginWindow(LoginViewModel viewModel)
        {
            InitializeComponent();

            DataContext = viewModel;
            viewModel.LoginSucceeded += OnLoginSucceeded;

            // PasswordBox içeriği bağlanamaz (güvenlik gereği bir DependencyProperty
            // değildir), bu yüzden senkronizasyon kod arkasında elle yapılır.
            if (!string.IsNullOrEmpty(viewModel.Password))
            {
                TxtPassword.Password = viewModel.Password;
            }

            TxtPassword.PasswordChanged += (_, _) => viewModel.Password = TxtPassword.Password;
            TxtConfirmPassword.PasswordChanged += (_, _) => viewModel.ConfirmPassword = TxtConfirmPassword.Password;

            Loaded += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(TxtUser.Text)) TxtUser.Focus();
                else TxtPassword.Focus();
            };
        }

        private void OnLoginSucceeded()
        {
            var mainWindow = App.GetService<MainWindow>();
            Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
            Close();
        }
    }
}
