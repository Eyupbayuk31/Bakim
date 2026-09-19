using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Models
{
    public partial class PrivacyTweakItem : ObservableObject
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = "Telemetri & Tanılama";
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string RiskLevel { get; set; } = "Önerilen"; // Önerilen, İsteğe Bağlı, Gelişmiş
        public bool IsRecommended { get; set; } = true;

        [ObservableProperty]
        private bool _isEnabled;

        [ObservableProperty]
        private bool _isBusy;

        public string BadgeBrush => RiskLevel switch
        {
            "Önerilen" => "SystemFillColorSuccessBrush",
            "İsteğe Bağlı" => "AccentTextFillColorPrimaryBrush",
            _ => "SystemFillColorCautionBrush"
        };
    }

    public partial class BloatwareAppItem : ObservableObject
    {
        public string PackageName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Publisher { get; set; } = "Microsoft Corporation";
        public string Description { get; set; } = string.Empty;
        public bool IsEssential { get; set; } // Calculator, Store, Photos etc.
        public string Category { get; set; } = "Gereksiz Uygulama";

        [ObservableProperty]
        private bool _isInstalled = true;

        [ObservableProperty]
        private bool _isBusy;
    }

    public class PrivacyStats
    {
        public int TotalTweaksCount { get; set; }
        public int ActiveTweaksCount { get; set; }
        public int RecommendedActiveCount { get; set; }
        public int TotalBloatwareCount { get; set; }
        public int InstalledBloatwareCount { get; set; }

        public int ProtectionPercentage => TotalTweaksCount > 0 ? (int)((ActiveTweaksCount / (double)TotalTweaksCount) * 100) : 0;
    }
}
