using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Models
{
    public partial class CleanCategory : ObservableObject
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public List<string> AdditionalPaths { get; set; } = new();
        public string FilePattern { get; set; } = "*";
        public string GroupName { get; set; } = "Windows & Sistem";
        public string IconSymbol { get; set; } = "Folder24";
        public bool RequiresAdmin { get; set; }
        public bool IsDeepClean { get; set; }

        /// <summary>
        /// Bu süreden yeni dosyalar taranmaz ve silinmez (ör. temp: açık bir kurulumun
        /// dosyalarını korumak için 24 saat).
        /// </summary>
        public TimeSpan MinFileAge { get; set; } = TimeSpan.Zero;

        /// <summary>Bu kategorinin dosyalarını kullanan süreçler (ör. "chrome"). Açıksa kullanıcı uyarılır (§5.2).</summary>
        public string[] ProcessNames { get; set; } = System.Array.Empty<string>();

        /// <summary>İlgili uygulama şu an açık: bazı dosyalar kilitli olacağı için atlanacak.</summary>
        [ObservableProperty]
        private bool _isAppRunning;

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
