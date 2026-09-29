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

    public sealed class ChangelogPageButton
    {
        public ChangelogPageButton(int number, bool isCurrent)
        {
            Number = number;
            IsCurrent = isCurrent;
        }

        public int Number { get; }
        public bool IsCurrent { get; }
    }
}
