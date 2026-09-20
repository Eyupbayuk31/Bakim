using System.Windows;
using System.Windows.Controls;

namespace Bakım.Controls
{
    /// <summary>
    /// Anlamsal durum rozeti. Simge verilmezse renkli nokta gösterir, böylece
    /// anlam hiçbir zaman yalnızca renge bağlı kalmaz.
    ///
    /// Kullanım:
    ///   &lt;c:StatusBadge Text="Canlı Telemetri Aktif" Intent="Success"/&gt;
    /// </summary>
    public partial class StatusBadge : UserControl
    {
        public StatusBadge()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusBadge),
                new PropertyMetadata(string.Empty));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        /// <summary>Boş bırakılırsa nokta göstergesi kullanılır.</summary>
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(nameof(Icon), typeof(string), typeof(StatusBadge),
                new PropertyMetadata(string.Empty));

        public string Icon
        {
            get => (string)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public static readonly DependencyProperty IntentProperty =
            DependencyProperty.Register(nameof(Intent), typeof(Intent), typeof(StatusBadge),
                new PropertyMetadata(Controls.Intent.Neutral));

        public Intent Intent
        {
            get => (Intent)GetValue(IntentProperty);
            set => SetValue(IntentProperty, value);
        }
    }
}
