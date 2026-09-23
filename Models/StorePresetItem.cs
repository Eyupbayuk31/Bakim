using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;

namespace Bakım.Models
{
    /// <summary>
    /// Mağazadaki hazır paketleri (format kurtarıcı, oyuncu, ofis, yazılımcı vb.) temsil eden model.
    /// </summary>
    public partial class StorePresetItem : ObservableObject
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Badge { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public SymbolRegular IconSymbol { get; set; } = SymbolRegular.Apps24;
        public List<string> IncludedApps { get; set; } = new();
        public HashSet<string> TargetIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [ObservableProperty]
        private int _totalCount;

        [ObservableProperty]
        private int _installedCount;

        [ObservableProperty]
        private bool _isSelected;

        public string InstalledStatusText => $"{InstalledCount} / {TotalCount} Kurulu";
        public bool IsFullyInstalled => TotalCount > 0 && InstalledCount >= TotalCount;

        partial void OnInstalledCountChanged(int value)
        {
            OnPropertyChanged(nameof(InstalledStatusText));
            OnPropertyChanged(nameof(IsFullyInstalled));
        }

        partial void OnTotalCountChanged(int value)
        {
            OnPropertyChanged(nameof(InstalledStatusText));
            OnPropertyChanged(nameof(IsFullyInstalled));
        }
    }
}
