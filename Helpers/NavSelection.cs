using System.Windows;

namespace Bakım.Helpers
{
    /// <summary>
    /// Gezinme öğesinin seçili olduğunu stile bildirir (Nav.ItemButton). Kenar çubuğu,
    /// Ayarlar kategori menüsü gibi farklı veri modelleri aynı Fluent stili paylaşır.
    ///
    /// Kullanım: &lt;Button Style="{StaticResource Nav.ItemButton}" helpers:NavSelection.IsSelected="{Binding IsSelected}"/&gt;
    /// </summary>
    public static class NavSelection
    {
        public static readonly DependencyProperty IsSelectedProperty =
            DependencyProperty.RegisterAttached("IsSelected", typeof(bool), typeof(NavSelection),
                new FrameworkPropertyMetadata(false));

        public static bool GetIsSelected(DependencyObject obj) => (bool)obj.GetValue(IsSelectedProperty);
        public static void SetIsSelected(DependencyObject obj, bool value) => obj.SetValue(IsSelectedProperty, value);
    }
}
