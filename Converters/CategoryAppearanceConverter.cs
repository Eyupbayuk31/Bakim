using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Bakım.Converters
{
    /// <summary>
    /// Returns a foreground SolidColorBrush based on the CommandPaletteItem category.
    /// Used for: icon box border accent, pill badge text color.
    /// </summary>
    public class CategoryToForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string category = value as string ?? string.Empty;

            return category switch
            {
                "Hızlı Eylem"        => new SolidColorBrush(Color.FromRgb(16,  185, 129)),  // Emerald green
                "Güvenlik"           => new SolidColorBrush(Color.FromRgb(245, 158,  11)),  // Amber/orange
                "Görünüm"            => new SolidColorBrush(Color.FromRgb(167,  139, 250)), // Violet
                "Sayfa Navigasyonu"  => new SolidColorBrush(Color.FromRgb( 56,  189, 248)), // Sky cyan
                "Windows Tweaker"    => new SolidColorBrush(Color.FromRgb(251,  113, 133)), // Rose pink
                _                    => new SolidColorBrush(Color.FromRgb(148,  163, 184)), // Slate secondary
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// Returns a very low-opacity background SolidColorBrush (icon box tint) based on category.
    /// Alpha ~30 out of 255 — just enough to tint, not overpower.
    /// </summary>
    public class CategoryToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string category = value as string ?? string.Empty;

            return category switch
            {
                "Hızlı Eylem"        => new SolidColorBrush(Color.FromArgb(38,  16,  185, 129)), // Emerald tint
                "Güvenlik"           => new SolidColorBrush(Color.FromArgb(38, 245,  158,  11)), // Amber tint
                "Görünüm"            => new SolidColorBrush(Color.FromArgb(38, 167,  139, 250)), // Violet tint
                "Sayfa Navigasyonu"  => new SolidColorBrush(Color.FromArgb(38,  56,  189, 248)), // Cyan tint
                "Windows Tweaker"    => new SolidColorBrush(Color.FromArgb(38, 251,  113, 133)), // Rose tint
                _                    => new SolidColorBrush(Color.FromArgb(38, 148,  163, 184)), // Slate tint
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
