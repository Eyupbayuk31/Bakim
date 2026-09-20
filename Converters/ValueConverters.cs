using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace Bakım.Converters
{
    public class SafeSymbolConverter : IValueConverter
    {
        public static SymbolRegular[] Test20 = new[]
        {
            SymbolRegular.Shield20,
            SymbolRegular.ShieldKeyhole20,
            SymbolRegular.DarkTheme20,
            SymbolRegular.Person20,
            SymbolRegular.SignOut20,
            SymbolRegular.Checkmark20,
            SymbolRegular.ChevronUp20,
            SymbolRegular.ChevronDown20,
            SymbolRegular.Dismiss20,
            SymbolRegular.Search20,
            SymbolRegular.Grid20,
            SymbolRegular.AppGeneric20,
            SymbolRegular.Color20,
            SymbolRegular.FontIncrease20,
            SymbolRegular.Wrench20,
            SymbolRegular.Power20,
            SymbolRegular.Desktop20,
            SymbolRegular.Cursor20,
            SymbolRegular.Folder20,
            SymbolRegular.Settings20,
            SymbolRegular.Globe20,
            SymbolRegular.DeveloperBoard20,
            SymbolRegular.Apps20
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string iconString && !string.IsNullOrWhiteSpace(iconString))
            {
                if (Enum.TryParse<SymbolRegular>(iconString, true, out var result))
                {
                    return result;
                }

                string clean = iconString.Trim();
                return clean.ToLowerInvariant() switch
                {
                    "numberlist20" or "numberlist" or "numberedlist" or "numberedlist24" => SymbolRegular.List24,
                    "wrench20" or "wrench" => SymbolRegular.Wrench24,
                    "power20" or "power" or "boot" or "logon" => SymbolRegular.Power24,
                    "desktop20" or "desktop" or "taskbar" => SymbolRegular.Desktop24,
                    "cursor20" or "cursor" or "contextmenu" or "contextmenu24" => SymbolRegular.CursorClick24,
                    "folder20" or "folder" or "explorer" => SymbolRegular.Folder24,
                    "settings20" or "settings" or "config" => SymbolRegular.Settings24,
                    "globe20" or "globe" or "edge" or "web" => SymbolRegular.Globe24,
                    "developerboard20" or "developerboard" or "cpu" => SymbolRegular.DeveloperBoard24,
                    "apps20" or "apps" or "programs" => SymbolRegular.Apps24,
                    "appgeneric20" or "appgeneric" or "win11" or "windows11" => SymbolRegular.AppGeneric24,
                    "color20" or "color" or "appearance" or "theme" => SymbolRegular.Color24,
                    "fontincrease20" or "fontincrease" => SymbolRegular.FontIncrease24,
                    _ => SymbolRegular.QuestionCircle24
                };
            }
            else if (value is SymbolRegular symbol)
            {
                return symbol;
            }

            return SymbolRegular.QuestionCircle24;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class BoolToVisConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                if (Invert) b = !b;
                return b ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
            {
                bool b = v == Visibility.Visible;
                return Invert ? !b : b;
            }
            return false;
        }
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return true;
        }
    }

    public class IntToVisConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intVal && parameter != null && int.TryParse(parameter.ToString(), out int targetVal))
            {
                return intVal == targetVal ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class PercentageToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double pct = 0;
            if (value is int i) pct = i;
            else if (value is double d) pct = d;
            else if (value is float f) pct = f;

            if (pct >= 85)
            {
                return Application.Current?.TryFindResource("SystemFillColorCriticalBrush") 
                    ?? Application.Current?.TryFindResource("Brush.Danger") 
                    ?? System.Windows.Media.Brushes.Crimson;
            }
            if (pct >= 70)
            {
                return Application.Current?.TryFindResource("SystemFillColorCautionBrush") 
                    ?? Application.Current?.TryFindResource("Brush.Warning") 
                    ?? System.Windows.Media.Brushes.Goldenrod;
            }
            return Application.Current?.TryFindResource("AccentTextFillColorPrimaryBrush") 
                ?? Application.Current?.TryFindResource("Brush.Accent") 
                ?? System.Windows.Media.Brushes.DodgerBlue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ImpactLevelToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int level)
            {
                if (level >= 3)
                {
                    return Application.Current?.TryFindResource("SystemFillColorCriticalBrush") 
                        ?? Application.Current?.TryFindResource("Brush.Danger") 
                        ?? System.Windows.Media.Brushes.Crimson;
                }
                if (level == 2)
                {
                    return Application.Current?.TryFindResource("SystemFillColorCautionBrush") 
                        ?? Application.Current?.TryFindResource("Brush.Warning") 
                        ?? System.Windows.Media.Brushes.Goldenrod;
                }
                return Application.Current?.TryFindResource("SystemFillColorSuccessBrush") 
                    ?? Application.Current?.TryFindResource("Brush.Success") 
                    ?? System.Windows.Media.Brushes.MediumSeaGreen;
            }
            return Application.Current?.TryFindResource("TextFillColorSecondaryBrush") 
                ?? System.Windows.Media.Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StringToVisConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && parameter is string target)
            {
                bool matches = string.Equals(s, target, StringComparison.OrdinalIgnoreCase);
                if (Invert) matches = !matches;
                return matches ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StringToNavAppearanceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null && parameter != null)
            {
                string valStr = value.ToString()!;
                string paramStr = parameter.ToString()!;
                bool matches = string.Equals(valStr, paramStr, StringComparison.OrdinalIgnoreCase)
                    || (string.Equals(paramStr, "Tweaker", StringComparison.OrdinalIgnoreCase) && string.Equals(valStr, "PrivacyDebloat", StringComparison.OrdinalIgnoreCase));
                return matches ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
            }
            return Wpf.Ui.Controls.ControlAppearance.Secondary;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToNavAppearanceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
            {
                return Wpf.Ui.Controls.ControlAppearance.Primary;
            }
            return Wpf.Ui.Controls.ControlAppearance.Secondary;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EventLevelToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string level = value?.ToString()?.Trim() ?? string.Empty;
            if (string.Equals(level, "Kritik", StringComparison.OrdinalIgnoreCase))
            {
                return Application.Current?.TryFindResource("SystemFillColorCriticalBrush") 
                       ?? System.Windows.Media.Brushes.Crimson;
            }
            if (string.Equals(level, "Hata", StringComparison.OrdinalIgnoreCase))
            {
                return Application.Current?.TryFindResource("SystemFillColorCautionBrush") 
                       ?? System.Windows.Media.Brushes.DarkOrange;
            }
            return Application.Current?.TryFindResource("AccentTextFillColorPrimaryBrush") 
                   ?? System.Windows.Media.Brushes.DodgerBlue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool hasValue;
            if (value == null)
            {
                hasValue = false;
            }
            else if (value is string s)
            {
                hasValue = !string.IsNullOrWhiteSpace(s);
            }
            else
            {
                hasValue = true;
            }

            if (Invert) hasValue = !hasValue;
            return hasValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Sekme seçimi: bağlanan int, ConverterParameter ile eşleşirse görünür.
    /// TabControl yerine segmented düğme + ContentControl deseninde kullanılır.
    /// </summary>
    public class IntEqualsToVisConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool match = value is int actual
                         && parameter != null
                         && int.TryParse(parameter.ToString(), out int expected)
                         && actual == expected;

            if (Invert) match = !match;
            return match ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Seçili sekme düğmesini vurgular: eşleşen indeks Primary, diğerleri Transparent.
    /// </summary>
    public class IntEqualsToAppearanceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool match = value is int actual
                         && parameter != null
                         && int.TryParse(parameter.ToString(), out int expected)
                         && actual == expected;

            return match
                ? Wpf.Ui.Controls.ControlAppearance.Primary
                : Wpf.Ui.Controls.ControlAppearance.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Entropi değerini (0.0 - 8.0) yüzdeye çevirir; ilerleme çubuğu doldurmak için.
    /// </summary>
    public class EntropyToPercentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double entropy = value is double d ? d : 0;
            return Math.Clamp(entropy / 8.0 * 100.0, 0, 100);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

}
