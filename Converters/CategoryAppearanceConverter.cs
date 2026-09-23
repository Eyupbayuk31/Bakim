using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Bakım.Converters
{
    /// <summary>
    /// Returns a foreground SolidColorBrush based on the category.
    /// Used for: icon box border accent, pill badge text color in Command Palette and Store Hub.
    /// </summary>
    public class CategoryToForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string category = value as string ?? string.Empty;

            return category switch
            {
                // Command Palette Categories
                "Hızlı Eylem"        => new SolidColorBrush(Color.FromRgb(16,  185, 129)),  // Emerald green
                "Güvenlik"           => new SolidColorBrush(Color.FromRgb(245, 158,  11)),  // Amber/orange
                "Görünüm"            => new SolidColorBrush(Color.FromRgb(167,  139, 250)), // Violet
                "Sayfa Navigasyonu"  => new SolidColorBrush(Color.FromRgb( 56,  189, 248)), // Sky cyan
                "Windows Tweaker"    => new SolidColorBrush(Color.FromRgb(251,  113, 133)), // Rose pink

                // Mağaza (Store Hub) Kategorileri
                "Runtimes"           => new SolidColorBrush(Color.FromRgb( 56, 189, 248)),  // Sky cyan (#38BDF8)
                "Oyun"               => new SolidColorBrush(Color.FromRgb(167, 139, 250)),  // Neon Violet (#A78BFA)
                "Müzik & Medya"      => new SolidColorBrush(Color.FromRgb(251, 191,  36)),  // Amber Gold (#FBBF24)
                "Yazılım & Araçlar"  => new SolidColorBrush(Color.FromRgb( 52, 211, 153)),  // Emerald Teal (#34D399)
                "Tarayıcılar"        => new SolidColorBrush(Color.FromRgb( 96, 165, 250)),  // Vivid Blue (#60A5FA)
                "İletişim"           => new SolidColorBrush(Color.FromRgb(251, 113, 133)),  // Rose Coral (#FB7185)
                "Geliştirici"        => new SolidColorBrush(Color.FromRgb(192, 132, 252)),  // Electric Fuchsia (#C084FC)

                _                    => new SolidColorBrush(Color.FromRgb(148,  163, 184)), // Slate secondary
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// Returns a very low-opacity background SolidColorBrush (icon box tint) based on category.
    /// Alpha ~38 out of 255 — just enough to tint, not overpower.
    /// </summary>
    public class CategoryToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string category = value as string ?? string.Empty;

            return category switch
            {
                // Command Palette Categories
                "Hızlı Eylem"        => new SolidColorBrush(Color.FromArgb(38,  16,  185, 129)), // Emerald tint
                "Güvenlik"           => new SolidColorBrush(Color.FromArgb(38, 245,  158,  11)), // Amber tint
                "Görünüm"            => new SolidColorBrush(Color.FromArgb(38, 167,  139, 250)), // Violet tint
                "Sayfa Navigasyonu"  => new SolidColorBrush(Color.FromArgb(38,  56,  189, 248)), // Cyan tint
                "Windows Tweaker"    => new SolidColorBrush(Color.FromArgb(38, 251,  113, 133)), // Rose tint

                // Mağaza (Store Hub) Kategorileri
                "Runtimes"           => new SolidColorBrush(Color.FromArgb(38,  56, 189, 248)), // Sky cyan tint
                "Oyun"               => new SolidColorBrush(Color.FromArgb(38, 167, 139, 250)), // Neon Violet tint
                "Müzik & Medya"      => new SolidColorBrush(Color.FromArgb(38, 251, 191,  36)), // Amber tint
                "Yazılım & Araçlar"  => new SolidColorBrush(Color.FromArgb(38,  52, 211, 153)), // Emerald tint
                "Tarayıcılar"        => new SolidColorBrush(Color.FromArgb(38,  96, 165, 250)), // Vivid blue tint
                "İletişim"           => new SolidColorBrush(Color.FromArgb(38, 251, 113, 133)), // Rose tint
                "Geliştirici"        => new SolidColorBrush(Color.FromArgb(38, 192, 132, 252)), // Fuchsia tint

                _                    => new SolidColorBrush(Color.FromArgb(38, 148,  163, 184)), // Slate tint
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
