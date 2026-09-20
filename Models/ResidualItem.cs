using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Models
{
    public enum ResidualType
    {
        Folder,
        File,
        RegistryKey
    }

    public partial class ResidualItem : ObservableObject
    {
        [ObservableProperty]
        private bool _isSelected = true;

        [ObservableProperty]
        private bool _isDeleted;

        [ObservableProperty]
        private bool _isDeleting;

        public string Path { get; set; } = string.Empty;
        public ResidualType Type { get; set; } = ResidualType.Folder;
        public long SizeInBytes { get; set; }
        public string Description { get; set; } = string.Empty;
        public int ConfidenceScore { get; set; } = 100;
        public bool IsSafeToDelete { get; set; } = true;

        public string FormattedSize => SizeInBytes > 0 
            ? (SizeInBytes >= 1024 * 1024 
                ? $"{Math.Round((double)SizeInBytes / (1024 * 1024), 1)} MB" 
                : $"{Math.Round((double)SizeInBytes / 1024, 1)} KB")
            : (Type == ResidualType.RegistryKey ? "Kayıt Anahtarı" : "0 B");

        public string TypeName => Type switch
        {
            ResidualType.Folder => "Klasör",
            ResidualType.File => "Dosya",
            ResidualType.RegistryKey => "Kayıt Defteri",
            _ => "Kalıntı"
        };

        public string TypeIconSymbol => Type switch
        {
            ResidualType.Folder => "Folder20",
            ResidualType.File => "Document20",
            ResidualType.RegistryKey => "Tag20",
            _ => "Apps20"
        };

        public string RiskBadgeText => IsSafeToDelete ? "%100 Güvenli" : "İnceleyin";
        /// <summary>Kalıntının silinme güvenliğinin anlamsal tonu.</summary>
        public Intent RiskIntent => IsSafeToDelete ? Intent.Success : Intent.Caution;
        public string RiskBadgeBackground => IsSafeToDelete ? "#1510B981" : "#15F59E0B";
    }
}
