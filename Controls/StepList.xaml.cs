using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Bakım.Core.GameMode;

namespace Bakım.Controls
{
    /// <summary>
    /// Durumlu adım listesi. Kullanım: &lt;c:StepList Steps="{Binding Steps}"/&gt;
    /// </summary>
    public partial class StepList : UserControl
    {
        public StepList()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty StepsProperty =
            DependencyProperty.Register(nameof(Steps), typeof(IEnumerable<GameModeStep>), typeof(StepList),
                new PropertyMetadata(null));

        public IEnumerable<GameModeStep>? Steps
        {
            get => (IEnumerable<GameModeStep>?)GetValue(StepsProperty);
            set => SetValue(StepsProperty, value);
        }
    }
}
