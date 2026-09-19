using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakım.ViewModels
{
    public partial class LoginViewModel : ObservableObject
    {
        private const string ValidUsername = "Eyüp";
        private const string ValidPassword = "1061";

        [ObservableProperty]
        private string _username = "Eyüp";

        [ObservableProperty]
        private string _password = string.Empty;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private bool _hasError;

        [ObservableProperty]
        private bool _isBusy;

        public event Action? LoginSucceeded;

        [RelayCommand]
        private void Login()
        {
            HasError = false;
            ErrorMessage = string.Empty;

            string enteredUser = (Username ?? string.Empty).Trim();
            string enteredPass = Password ?? string.Empty;

            if (string.Equals(enteredUser, ValidUsername, StringComparison.CurrentCultureIgnoreCase) &&
                enteredPass == ValidPassword)
            {
                LoginSucceeded?.Invoke();
            }
            else
            {
                HasError = true;
                ErrorMessage = "Hatalı kullanıcı adı veya şifre! Lütfen kontrol edin.";
                Password = string.Empty;
            }
        }

        [RelayCommand]
        private void ToggleTheme()
        {
            ThemeManager.ToggleTheme();
        }
    }
}
