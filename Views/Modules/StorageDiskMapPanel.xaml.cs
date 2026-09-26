using System.Windows.Controls;

namespace Bakım.Views.Modules
{
    /// <summary>Depolama › Disk haritası. DataContext: <see cref="ViewModels.StorageViewModel"/>.</summary>
    public partial class StorageDiskMapPanel : UserControl
    {
        public StorageDiskMapPanel()
        {
            InitializeComponent();
        }

        /// <summary>Treemap yerleşimi alan boyutuna bağlıdır; boyut değişince yeniden hesaplanır.</summary>
        private void OnDiskMapSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            if (DataContext is ViewModels.StorageViewModel vm)
                vm.SetMapViewport(e.NewSize.Width - 2, e.NewSize.Height - 2);
        }
    }
}
