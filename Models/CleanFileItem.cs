using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Models
{
    public partial class CleanFileItem : ObservableObject
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
        public long SizeBytes { get; set; }
        public string FormattedSize => CleanCategory.FormatBytes(SizeBytes);

        [ObservableProperty]
        private string _status = "Bulundu";

        [ObservableProperty]
        private bool _isDeleted;

        [ObservableProperty]
        private bool _isExcluded;
    }
}
