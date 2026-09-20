using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Models
{
    public partial class ReleaseChangelogItem : ObservableObject
    {
        public string Version { get; set; } = string.Empty;
        public string ReleaseDate { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public bool IsLatest { get; set; }
        public string BadgeBrush => IsLatest ? "SystemFillColorSuccessBrush" : "AccentTextFillColorPrimaryBrush";
        public List<string> Highlights { get; set; } = new();

        [ObservableProperty]
        private bool _isExpanded;
    }
}
