using System.Windows;
using System.Windows.Controls;

namespace Bakım.Controls
{
    /// <summary>
    /// Boş durum yüzeyi — sonuç yok, henüz tarama yapılmadı, liste boş.
    ///
    /// Kullanım:
    ///   &lt;c:EmptyState Icon="Search24"
    ///                 Title="Henüz tarama yapılmadı"
    ///                 Description="Başlamak için Taramayı Başlat düğmesini kullanın."/&gt;
    /// </summary>
    public partial class EmptyState : UserControl
    {
        public EmptyState()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(nameof(Icon), typeof(string), typeof(EmptyState),
                new PropertyMetadata("Inbox24"));

        public string Icon
        {
            get => (string)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyState),
                new PropertyMetadata(string.Empty));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register(nameof(Description), typeof(string), typeof(EmptyState),
                new PropertyMetadata(string.Empty));

        public string Description
        {
            get => (string)GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        /// <summary>Kullanıcıya çıkış yolu sunan düğme(ler).</summary>
        public static readonly DependencyProperty ActionsProperty =
            DependencyProperty.Register(nameof(Actions), typeof(object), typeof(EmptyState),
                new PropertyMetadata(null));

        public object? Actions
        {
            get => GetValue(ActionsProperty);
            set => SetValue(ActionsProperty, value);
        }
    }
}
