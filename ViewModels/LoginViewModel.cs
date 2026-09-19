using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Services;

namespace Bakım.ViewModels
{
    public partial class LoginViewModel : ObservableObject
    {
        private readonly IAuthService _authService;

        public LoginViewModel(IAuthService? authService = null)
        {
            _authService = authService ?? new AuthService();

            // DPAPI Beni Hatırla Kontrolü
            LoadRememberedCredentials();
        }

        [ObservableProperty]
        private bool _isRegisterMode = false;

        [ObservableProperty]
        private string _username = string.Empty;

        [ObservableProperty]
        private string _password = string.Empty;

        [ObservableProperty]
        private string _confirmPassword = string.Empty;

        [ObservableProperty]
        private bool _rememberMe = true;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private bool _hasError;

        [ObservableProperty]
        private bool _isSuccess;

        [ObservableProperty]
        private string _successMessage = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

        public event Action? LoginSucceeded;

        private void LoadRememberedCredentials()
        {
            try
            {
                var creds = CredentialStorageService.LoadCredentials();
                if (creds.HasValue)
                {
                    Username = creds.Value.Username;
                    Password = creds.Value.Password;
                    RememberMe = true;
                }
            }
            catch { }
        }

        [RelayCommand]
        public void SwitchToLogin()
        {
            IsRegisterMode = false;
            HasError = false;
            IsSuccess = false;
            ErrorMessage = string.Empty;
            ConfirmPassword = string.Empty;
        }

        [RelayCommand]
        public void SwitchToRegister()
        {
            IsRegisterMode = true;
            HasError = false;
            IsSuccess = false;
            ErrorMessage = string.Empty;
            ConfirmPassword = string.Empty;
        }

        [RelayCommand]
        public async Task SubmitAsync()
        {
            if (IsBusy) return;

            HasError = false;
            IsSuccess = false;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            if (IsRegisterMode)
            {
                // KAYIT OL
                if (Password != ConfirmPassword)
                {
                    HasError = true;
                    ErrorMessage = "Şifreler birbiriyle uyuşmuyor! Lütfen kontrol edin.";
                    return;
                }

                IsBusy = true;
                try
                {
                    var result = await _authService.RegisterAsync(Username, Password);
                    if (result.Success)
                    {
                        IsSuccess = true;
                        SuccessMessage = result.Message;
                        IsRegisterMode = false;
                        ConfirmPassword = string.Empty;
                    }
                    else
                    {
                        HasError = true;
                        ErrorMessage = result.Message;
                    }
                }
                finally
                {
                    IsBusy = false;
                }
            }
            else
            {
                // GİRİŞ YAP
                IsBusy = true;
                try
                {
                    var result = await _authService.AuthenticateAsync(Username, Password);
                    if (result.Success)
                    {
                        // Beni Hatırla: DPAPI ile sakla veya temizle
                        if (RememberMe)
                        {
                            CredentialStorageService.SaveCredentials(Username, Password);
                        }
                        else
                        {
                            CredentialStorageService.ClearCredentials();
                        }

                        LoginSucceeded?.Invoke();
                    }
                    else
                    {
                        HasError = true;
                        ErrorMessage = result.Message;
                    }
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        [RelayCommand]
        public void ToggleTheme()
        {
            ThemeManager.ToggleTheme();
        }
    }
}
