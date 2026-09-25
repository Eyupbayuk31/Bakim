using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Models
{
    public enum TweakType
    {
        Toggle,
        Numeric,
        Action
    }

    public partial class SystemTweakItem : ObservableObject
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = "Davranışlar (Behavior)";
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public TweakType Type { get; set; } = TweakType.Toggle;
        public bool RequiresAdmin { get; set; } = true;
        public bool RequiresRestart { get; set; } = false;
        public bool IsRecommended { get; set; } = false;
        public string IconSymbol { get; set; } = "Wrench24";

        /// <summary>Güvenlik etkisi (S-16); <see cref="ApplySecurityNote"/> ile katalogdan doldurulur.</summary>
        public Bakım.Core.Security.SecurityImpact SecurityImpact { get; private set; }
        public string SecurityWarning { get; private set; } = string.Empty;
        public bool ReducesSecurity => SecurityImpact == Bakım.Core.Security.SecurityImpact.High;
        public bool AffectsSecurity => SecurityImpact == Bakım.Core.Security.SecurityImpact.Low;

        /// <summary>
        /// Katalogdaki güvenlik notunu uygular. Güvenliği azaltan ayar hiçbir zaman
        /// "önerilen" sayılmaz; böylece "Önerilenleri uygula" onu asla açmaz.
        /// </summary>
        public void ApplySecurityNote()
        {
            var note = Bakım.Core.Security.TweakSecurityCatalog.For(Id);
            SecurityImpact = note.Impact;
            SecurityWarning = note.Warning;
            if (note.Impact == Bakım.Core.Security.SecurityImpact.High) IsRecommended = false;
            OnPropertyChanged(nameof(SecurityImpact));
            OnPropertyChanged(nameof(SecurityWarning));
            OnPropertyChanged(nameof(ReducesSecurity));
            OnPropertyChanged(nameof(AffectsSecurity));
        }

        public bool IsToggle => Type == TweakType.Toggle;
        public bool IsNumeric => Type == TweakType.Numeric;
        public bool IsAction => Type == TweakType.Action;

        [ObservableProperty]
        private bool _isEnabled;

        public bool IsActive
        {
            get => IsEnabled;
            set => IsEnabled = value;
        }

        partial void OnIsEnabledChanged(bool value)
        {
            OnPropertyChanged(nameof(IsActive));
        }

        public void NotifyStateChanged()
        {
            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(IsActive));
        }

        [ObservableProperty]
        private bool _isBusy;

        /// <summary>Son uygulama denemesinde başarısız olan yazımların özeti (yoksa null).</summary>
        public string? LastError { get; set; }

        [ObservableProperty]
        private int _numericValue;

        public int MinNumericValue { get; set; } = 0;
        public int MaxNumericValue { get; set; } = 3600;
        public string NumericUnit { get; set; } = "sn";

        public string CategoryBadgeBrush => Category switch
        {
            var c when c.Contains("Görünüm") => "AccentTextFillColorPrimaryBrush",
            var c when c.Contains("Gelişmiş") => "SystemFillColorSuccessBrush",
            var c when c.Contains("Windows 11") => "AccentTextFillColorPrimaryBrush",
            var c when c.Contains("Masaüstü") => "AccentTextFillColorPrimaryBrush",
            var c when c.Contains("Sağ Tık") => "SystemFillColorSuccessBrush",
            var c when c.Contains("Araçlar") => "SystemFillColorCautionBrush",
            var c when c.Contains("Klasik") => "AccentTextFillColorPrimaryBrush",
            var c when c.Contains("Açılış") => "AccentTextFillColorPrimaryBrush",
            var c when c.Contains("Edge") => "AccentTextFillColorPrimaryBrush",
            var c when c.Contains("Ayarlar") => "SystemFillColorSuccessBrush",
            var c when c.Contains("Gezgin") => "SystemFillColorCautionBrush",
            _ => "SystemFillColorCautionBrush"
        };

        public string RestartNotice => RequiresRestart ? "Yeniden başlatma gerektirir" : "Anında aktif";

        public string UserBenefitText => !string.IsNullOrWhiteSpace(Description)
            ? Description
            : "Bu ayar sistem kararlılığını ve kullanıcı deneyimini iyileştirir.";

        public string SafetyNotice => IsRecommended
            ? "Herkes için güvenli ve önerilen temel sistem ayarıdır."
            : "Kişisel tercihe bağlı gelişmiş sistem özelleştirmesidir.";

        public string ExecutionNotice => RequiresRestart
            ? "Etkili olması için bilgisayarın veya oturumun yeniden başlatılması gerekir."
            : "Anında geçerli olur (yeniden başlatma gerektirmez).";

        public string RevertNotice => "İstediğiniz zaman bu ayarı kapatıp Windows'un orijinal varsayılan haline dönebilirsiniz.";
    }

    public class TweakerCategoryModel : ObservableObject
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string IconSymbol { get; set; } = "Wrench24";
        public string ShortDescription { get; set; } = string.Empty;
        public string BenefitSummary { get; set; } = string.Empty;

        private int _totalCount;
        public int TotalCount
        {
            get => _totalCount;
            set
            {
                if (SetProperty(ref _totalCount, value))
                {
                    OnPropertyChanged(nameof(CountBadge));
                }
            }
        }

        private int _activeCount;
        public int ActiveCount
        {
            get => _activeCount;
            set
            {
                if (SetProperty(ref _activeCount, value))
                {
                    OnPropertyChanged(nameof(CountBadge));
                }
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string CountBadge => $"{ActiveCount}/{TotalCount}";
    }

    public partial class WindowMetricsData : ObservableObject
    {
        [ObservableProperty]
        private int _iconSpacing = 75; // 30 - 150 px

        [ObservableProperty]
        private int _iconVerticalSpacing = 75; // 30 - 150 px

        [ObservableProperty]
        private int _scrollWidth = 17; // 10 - 40 px

        [ObservableProperty]
        private int _borderWidth = 1; // 1 - 10 px

        [ObservableProperty]
        private int _paddedBorderWidth = 4; // 0 - 20 px

        [ObservableProperty]
        private int _captionHeight = 22; // 15 - 50 px

        [ObservableProperty]
        private int _menuHeight = 19; // 15 - 50 px

        [ObservableProperty]
        private string _inactiveTitleBarHex = "#2B2B2B";

        [ObservableProperty]
        private string _activeTitleBarHex = "#0078D7";

        [ObservableProperty]
        private bool _isBusy;
    }

    public partial class OemInfoData : ObservableObject
    {
        [ObservableProperty]
        private string _manufacturer = string.Empty;

        [ObservableProperty]
        private string _model = string.Empty;

        [ObservableProperty]
        private string _supportHours = string.Empty;

        [ObservableProperty]
        private string _supportPhone = string.Empty;

        [ObservableProperty]
        private string _supportURL = string.Empty;

        [ObservableProperty]
        private string _registeredOwner = string.Empty;

        [ObservableProperty]
        private string _registeredOrganization = string.Empty;

        [ObservableProperty]
        private bool _isBusy;
    }

    public partial class ClassicAppItem : ObservableObject
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = "Sistem";
        public string IconSymbol { get; set; } = "Apps24";
        public string ExecutablePath { get; set; } = string.Empty;

        [ObservableProperty]
        private bool _isActivated;

        [ObservableProperty]
        private bool _isBusy;
    }

    public class TweakerStats
    {
        public int TotalTweaksCount { get; set; }
        public int ActiveTweaksCount { get; set; }
        public int Windows11TweaksCount { get; set; }
        public int BehaviorTweaksCount { get; set; }
        public int BootLogonTweaksCount { get; set; }
        public int DesktopTaskbarTweaksCount { get; set; }
        public int ContextMenuTweaksCount { get; set; }
        public int AppearanceTweaksCount { get; set; }
        public int AdvancedAppearanceCount { get; set; }
        public int FileExplorerTweaksCount { get; set; }
        public int SettingsControlPanelTweaksCount { get; set; }
        public int EdgeTweaksCount { get; set; }
        public int ToolsCount { get; set; }
        public int ClassicAppsCount { get; set; }

        public int OptimizationPercentage => TotalTweaksCount > 0 
            ? (int)((ActiveTweaksCount / (double)TotalTweaksCount) * 100) 
            : 0;
    }
}
