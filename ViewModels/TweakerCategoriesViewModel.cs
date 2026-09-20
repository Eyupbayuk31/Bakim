using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Services;

namespace Bakım.ViewModels
{
    /// <summary>
    /// Windows Tweaker alt kategori menü ögesi modeli.
    /// </summary>
    public partial class TweakerCategoryItem : ObservableObject
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string IconName { get; set; } = "Wrench24";
        public string ToolTip { get; set; } = string.Empty;
        public bool IsExternalPage { get; set; }

        [ObservableProperty]
        private bool _isSelected;
    }

    /// <summary>
    /// Windows Tweaker hiyerarşik akordiyon alt menüsünü ve 
    /// "Gizlilik & Debloat" gibi entegre kategorileri yöneten ViewModel.
    /// </summary>
    public partial class TweakerCategoriesViewModel : ObservableObject
    {
        private readonly INavigationService _navigationService;

        [ObservableProperty]
        private string _activeCategoryKey = "All";

        public ObservableCollection<TweakerCategoryItem> Categories { get; }

        public TweakerCategoriesViewModel(INavigationService navigationService)
        {
            _navigationService = navigationService ?? NavigationService.Instance;

            Categories = new ObservableCollection<TweakerCategoryItem>
            {
                new() { Key = "All", Title = "Tüm Ayarlar", IconName = "List24", ToolTip = "Tüm Ayarlar (150+)" },
                new() { Key = "PrivacyDebloat", Title = "Gizlilik & Debloat", IconName = "ShieldCheckmark24", ToolTip = "Telemetri, Cortana ve Bloatware Temizliği", IsExternalPage = true },
                new() { Key = "Windows11", Title = "Windows 11", IconName = "AppGeneric24", ToolTip = "Windows 11 Özelleştirmeleri" },
                new() { Key = "Appearance", Title = "Görünüm & Tema", IconName = "Color24", ToolTip = "Görünüm ve Tema Ayarları" },
                new() { Key = "AdvancedAppearance", Title = "Gelişmiş Görünüm", IconName = "FontIncrease24", ToolTip = "Yazı Tipleri ve Pencere Boyutları" },
                new() { Key = "Behavior", Title = "Davranışlar", IconName = "Wrench24", ToolTip = "Sistem Davranışları ve Bildirimler" },
                new() { Key = "BootLogon", Title = "Açılış & Oturum", IconName = "Power24", ToolTip = "Açılış, Kilit Ekranı ve Oturum" },
                new() { Key = "DesktopTaskbar", Title = "Masaüstü & Görev Çubuğu", IconName = "Desktop24", ToolTip = "Masaüstü Simgeleri ve Görev Çubuğu" },
                new() { Key = "ContextMenu", Title = "Sağ Tık & Kısayollar", IconName = "CursorClick24", ToolTip = "Sağ Tık Menüleri ve Kısayol Okları" },
                new() { Key = "FileExplorer", Title = "Dosya Gezgini", IconName = "Folder24", ToolTip = "Dosya Gezgini ve Şerit Menü" },
                new() { Key = "SettingsCpl", Title = "Ayarlar & Denetim", IconName = "Settings24", ToolTip = "Ayarlar ve Denetim Masası İnce Ayarları" },
                new() { Key = "Edge", Title = "Microsoft Edge", IconName = "Globe24", ToolTip = "Edge Kenar Çubuğu ve Bloatware Koruması" },
                new() { Key = "Tools", Title = "Sistem Araçları", IconName = "DeveloperBoard24", ToolTip = "Gelişmiş Windows Araçları" }
            };

            SelectCategory("All");
        }

        [RelayCommand]
        public void SelectCategory(string key)
        {
            SetSelectedCategorySilent(key);
            _navigationService.NavigateToTweakerCategory(key);
        }

        public void SetSelectedCategorySilent(string key)
        {
            ActiveCategoryKey = key;
            foreach (var item in Categories)
            {
                item.IsSelected = string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
