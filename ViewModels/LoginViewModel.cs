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

        public LoginViewModel(IAuthService authService)
        {
            _authService = authService;

            // DPAPI Beni Hatırla Kontrolü
            LoadRememberedCredentials();

            // İlk kurulum tespiti: cihazda hiç hesap yoksa doğrudan kayıt moduna geç.
            _ = DetectFirstRunAsync();
        }

        [ObservableProperty]
        private bool _isRegisterMode = false;

        /// <summary>Cihazda kayıtlı hesap yok: giriş ekranı ilk kurulum sihirbazına dönüşür.</summary>
        [ObservableProperty]
        private bool _isFirstRun;

        partial void OnIsRegisterModeChanged(bool value) => RaiseFormTexts();
        partial void OnIsFirstRunChanged(bool value) => RaiseFormTexts();

        private void RaiseFormTexts()
        {
            OnPropertyChanged(nameof(FormTitle));
            OnPropertyChanged(nameof(FormSubtitle));
            OnPropertyChanged(nameof(SubmitButtonText));
            OnPropertyChanged(nameof(CanSwitchMode));
        }

        public string FormTitle => IsFirstRun
            ? "Kuruluma Hoş Geldiniz"
            : (IsRegisterMode ? "Hesap Oluştur" : "Hoş Geldiniz");

        public string FormSubtitle => IsFirstRun
            ? "Bu cihaz için bir yönetici hesabı oluşturun"
            : (IsRegisterMode ? "Kullanıcı profilinizi oluşturun" : "Devam etmek için giriş yapın");

        public string SubmitButtonText => IsFirstRun
            ? "Hesabı Oluştur"
            : (IsRegisterMode ? "Kayıt Ol" : "Giriş Yap");

        /// <summary>İlk kurulumda giriş sekmesine geçmek anlamsızdır; anahtar gizlenir.</summary>
        public bool CanSwitchMode => !IsFirstRun;

        private async Task DetectFirstRunAsync()
        {
            try
            {
                bool hasAccount = await _authService.HasAnyAccountAsync();
                if (hasAccount) return;

                IsFirstRun = true;
                IsRegisterMode = true;
            }
            catch (Exception ex)
            {
                AppLog.Warning("İlk kurulum denetimi başarısız.", ex, nameof(LoginViewModel));
            }
        }

        public string OsVersionInfo => $"Windows {(Environment.OSVersion.Version.Major >= 10 ? "11" : "10")} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})";
        public bool IsAdminUser => Bakım.Helpers.UacHelper.IsAdministrator();
        public string AdminStatusText => IsAdminUser ? "Yönetici İzni: Aktif" : "Standart Kullanıcı";

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
                        IsFirstRun = false;
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

        /// <summary>
        /// Yerel kilidi unutan kullanıcı için kurtarma yolu.
        /// Bu bir güvenlik açığı değildir: hesap veritabanı zaten kullanıcının kendi
        /// Windows profilindedir ve giriş ekranı yalnızca yerel bir kilittir.
        /// Kurtarma yolu olmadan kullanıcı kendi aracından tamamen dışlanabilirdi.
        /// </summary>
        [RelayCommand]
        public async Task ResetAccountsAsync()
        {
            var confirm = System.Windows.MessageBox.Show(
                "Bu cihazdaki tüm yerel Bakım hesapları silinecek ve yeni bir yönetici hesabı " +
                "oluşturmanız istenecek.\n\nWindows hesabınız veya sistem ayarlarınız etkilenmez.\n\nDevam edilsin mi?",
                "Hesapları Sıfırla",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (confirm != System.Windows.MessageBoxResult.Yes) return;

            IsBusy = true;
            try
            {
                var result = await _authService.ResetAccountsAsync();

                HasError = !result.Success;
                IsSuccess = result.Success;
                ErrorMessage = result.Success ? string.Empty : result.Message;
                SuccessMessage = result.Success ? result.Message : string.Empty;

                if (result.Success)
                {
                    Username = string.Empty;
                    Password = string.Empty;
                    ConfirmPassword = string.Empty;
                    RememberMe = false;
                    IsFirstRun = true;
                    IsRegisterMode = true;
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public void ToggleTheme()
        {
            // Giriş ekranı da uygulamanın tek tema motorunu kullanır; seçim kalıcıdır.
            ThemeService.Shared.ToggleNextTheme();
        }
    }
}
