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
        public LoginWindow()
        {
            InitializeComponent();

            if (DataContext is LoginViewModel vm)
            {
                vm.LoginSucceeded += OnLoginSucceeded;

                // DPAPI'den otomatik yüklenen şifreyi PasswordBox'a aktar
                if (!string.IsNullOrEmpty(vm.Password))
                {
                    TxtPassword.Password = vm.Password;
                }

                // PasswordBox iki yönlü senkronizasyonu
                TxtPassword.PasswordChanged += (s, e) =>
                {
                    vm.Password = TxtPassword.Password;
                };

                TxtConfirmPassword.PasswordChanged += (s, e) =>
                {
                    vm.ConfirmPassword = TxtConfirmPassword.Password;
                };
            }

            Loaded += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(TxtUser.Text))
                {
                    TxtUser.Focus();
                }
                else
                {
                    TxtPassword.Focus();
                }
            };
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
