using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Models
{
    public partial class CleanCategory : ObservableObject
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public bool RequiresAdmin { get; set; }

        [ObservableProperty]
        private bool _isSelected = true;

        [ObservableProperty]
        private long _totalBytes;

        [ObservableProperty]
        private int _fileCount;

        [ObservableProperty]
        private bool _isScanning;

        public string FormattedSize => FormatBytes(TotalBytes);

        partial void OnTotalBytesChanged(long value)
        {
            OnPropertyChanged(nameof(FormattedSize));
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }
    }
}
