using System.Windows;
using System.Windows.Controls;
using Bakım.Models;

namespace Bakım.Controls
{
    /// <summary>
    /// Telemetri / KPI kartı.
    ///
    /// Kullanım:
    ///   &lt;c:StatCard Icon="TopSpeed24" Label="Bellek Kullanımı"
    ///               Value="%58" Caption="9.3 GB / 16 GB" Intent="Accent"/&gt;
    /// </summary>
    public partial class StatCard : UserControl
    {
        public StatCard()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(nameof(Icon), typeof(string), typeof(StatCard),
                new PropertyMetadata(string.Empty));

        public string Icon
        {
            get => (string)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatCard),
                new PropertyMetadata(string.Empty));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatCard),
                new PropertyMetadata(string.Empty));

        public string Value
        {
            get => (string)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        /// <summary>Değerin altındaki ikincil açıklama (ör. "9.3 GB / 16 GB").</summary>
        public static readonly DependencyProperty CaptionProperty =
            DependencyProperty.Register(nameof(Caption), typeof(string), typeof(StatCard),
                new PropertyMetadata(string.Empty));

        public string Caption
        {
            get => (string)GetValue(CaptionProperty);
            set => SetValue(CaptionProperty, value);
        }

        public static readonly DependencyProperty IntentProperty =
            DependencyProperty.Register(nameof(Intent), typeof(Intent), typeof(StatCard),
                new PropertyMetadata(Models.Intent.Neutral));

        public Intent Intent
        {
            get => (Intent)GetValue(IntentProperty);
            set => SetValue(IntentProperty, value);
        }
    }
}
