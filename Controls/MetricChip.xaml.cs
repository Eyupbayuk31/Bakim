using System.Windows;
using System.Windows.Controls;
using Bakım.Models;

namespace Bakım.Controls
{
    /// <summary>
    /// Simge + etiket + değer taşıyan kompakt bilgi kapsülü.
    ///
    /// Kullanım:
    ///   &lt;c:MetricChip Icon="TopSpeed24" Label="RAM:" Value="%58" Intent="Accent"/&gt;
    /// </summary>
    public partial class MetricChip : UserControl
    {
        public MetricChip()
        {
            InitializeComponent();
        }

        /// <summary>WPF-UI SymbolRegular adı (ör. "TopSpeed24"). Boşsa simge gizlenir.</summary>
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(nameof(Icon), typeof(string), typeof(MetricChip),
                new PropertyMetadata(string.Empty));

        public string Icon
        {
            get => (string)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(MetricChip),
                new PropertyMetadata(string.Empty));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(string), typeof(MetricChip),
                new PropertyMetadata(string.Empty));

        public string Value
        {
            get => (string)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public static readonly DependencyProperty IntentProperty =
            DependencyProperty.Register(nameof(Intent), typeof(Intent), typeof(MetricChip),
                new PropertyMetadata(Models.Intent.Neutral));

        public Intent Intent
        {
            get => (Intent)GetValue(IntentProperty);
            set => SetValue(IntentProperty, value);
        }
    }
}
