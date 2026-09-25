using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Models
{
    /// <summary>
    /// Mağazadaki uygulama kategorileri.
    /// </summary>
    public enum StoreCategory
    {
        All,
        Runtimes,
        Gaming,
        Music,
        Software,
        Browsers,
        Social,
        Developer
    }

    /// <summary>
    /// Kurulum motoru ve yükleyici tipi.
    /// </summary>
    public enum StoreInstallerType
    {
        Winget,
        DirectXWeb,
        DirectHttpSilent
    }

    /// <summary>
    /// Bir uygulamanın canlı indirme ve kurulum durumu.
    /// </summary>
    public enum StoreInstallStatus
    {
        Idle,
        Queued,
        Downloading,
        Installing,
        Installed,
        Failed
    }

    /// <summary>
    /// Mağaza vitrinindeki her bir uygulamayı temsil eden gelişmiş MVVM model nesnesi.
    /// </summary>
    public partial class StoreAppItem : ObservableObject
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public StoreCategory Category { get; set; }
        public string CategoryDisplayName { get; set; } = string.Empty;
        public string IconSymbol { get; set; } = "Apps24";
        public string Publisher { get; set; } = string.Empty;
        public string SizeText { get; set; } = "Bilinmiyor";
        public string WingetId { get; set; } = string.Empty;

        /// <summary>
        /// Tek kartta birden çok winget paketi (ör. tüm Visual C++ sürümleri). Doluysa
        /// <see cref="WingetId"/> yerine bunlar sırayla kurulur.
        /// </summary>
        public string[] BundleWingetIds { get; set; } = Array.Empty<string>();
        public string DirectDownloadUrl { get; set; } = string.Empty;
        public string SilentInstallArgs { get; set; } = string.Empty;
        public StoreInstallerType InstallerType { get; set; } = StoreInstallerType.Winget;
        /// <summary>Kurulu tespiti için DisplayName parçası; alternatifler "|" ile ayrılır.</summary>
        public string RegistryDetectKeyword { get; set; } = string.Empty;

        [ObservableProperty]
        private bool _isInstalled;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private StoreInstallStatus _status = StoreInstallStatus.Idle;

        [ObservableProperty]
        private int _progressPercentage;

        [ObservableProperty]
        private string _statusMessage = "Hazır";

        /// <summary>
        /// Durumuna göre kullanıcı arayüzünde görüntülenecek metin.
        /// </summary>
        public string ActionButtonText => Status switch
        {
            StoreInstallStatus.Downloading => $"İndiriliyor %{ProgressPercentage}",
            StoreInstallStatus.Installing => "Kuruluyor...",
            StoreInstallStatus.Installed => "Kurulu",
            StoreInstallStatus.Queued => "Kuyrukta",
            StoreInstallStatus.Failed => "Tekrar Dene",
            _ => IsInstalled ? "Yeniden Kur" : "Yükle"
        };

        /// <summary>
        /// Kurulum devam ediyor mu (ProgressBar gösterimi için).
        /// </summary>
        public bool IsBusy => Status == StoreInstallStatus.Downloading || Status == StoreInstallStatus.Installing;

        partial void OnStatusChanged(StoreInstallStatus value)
        {
            OnPropertyChanged(nameof(ActionButtonText));
            OnPropertyChanged(nameof(IsBusy));
        }

        partial void OnProgressPercentageChanged(int value)
        {
            OnPropertyChanged(nameof(ActionButtonText));
        }

        partial void OnIsInstalledChanged(bool value)
        {
            OnPropertyChanged(nameof(ActionButtonText));
        }
    }
}
