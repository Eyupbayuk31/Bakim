using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IFileExplorerTweaksService
    {
        Task<List<SystemTweakItem>> GetFileExplorerTweaksAsync();
        Task<bool> ApplyTweakAsync(SystemTweakItem tweak, bool enable);
        Task<bool> SetJumpListItemsAsync(int count);
        Task<bool> ApplyAllRecommendedAsync(List<SystemTweakItem> tweaks);
        Task<bool> RestoreDefaultsAsync(List<SystemTweakItem> tweaks);
    }

    public class FileExplorerTweaksService : IFileExplorerTweaksService
    {
        public async Task<List<SystemTweakItem>> GetFileExplorerTweaksAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<SystemTweakItem>
                {
                    // 1. "Do this for all current items" Checkbox
                    new()
                    {
                        Id = "explorer_do_for_all_checkbox",
                        Category = "Dosya Gezgini",
                        Title = "Dosya Çakışmalarında 'Tüm Geçerli Ögeler İçin Bunu Yap'ı Otomatik Seç",
                        Description = "Dosya kopyalama veya silme çakışması pencerelerinde 'tüm ögeler için geçerli kıl' kutucuğunun varsayılan olarak seçili gelmesini sağlar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "CheckmarkCircle24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\OperationStatusManager", "ConfirmationCheckBoxDoForAll", 1)
                    },

                    // 2. Automatic Folder Type Discovery
                    new()
                    {
                        Id = "explorer_disable_folder_type_discovery",
                        Category = "Dosya Gezgini",
                        Title = "Otomatik Klasör Şablonu Değiştirmeyi Kapat (Tüm Klasörleri Genel Yap)",
                        Description = "Windows'un klasör içindeki resim/müzik dosyalarına bakarak klasör görünüm şablonunu otomatik değiştirmesini engelleyip sabit genel liste yapar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "Folder24",
                        IsEnabled = CheckRegistryString(Registry.CurrentUser, @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell", "FolderType", "NotSpecified")
                    },

                    // 3. Change Jump List Item Number
                    new()
                    {
                        Id = "explorer_jumplist_item_count",
                        Category = "Dosya Gezgini",
                        Title = "Görev Çubuğu Jump List (Son Kullanılanlar) Öge Sayısı",
                        Description = "Görev çubuğu ve Başlat menüsündeki uygulama simgelerine sağ tıklandığında listelenecek son kullanılan dosya sayısını ayarlar.",
                        Type = TweakType.Numeric,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "DocumentBulletList24",
                        NumericValue = GetJumpListItems(),
                        MinNumericValue = 0,
                        MaxNumericValue = 60,
                        NumericUnit = "öğe",
                        IsEnabled = true
                    },

                    // 4. Compressed Overlay Icon
                    new()
                    {
                        Id = "explorer_remove_compressed_overlay",
                        Category = "Dosya Gezgini",
                        Title = "Sıkıştırılmış Dosyalardaki Mavi Çift Ok Simgesini Kaldır",
                        Description = "NTFS ile sıkıştırılmış dosya ve klasör simgelerinin sağ üst köşesinde beliren rahatsız edici çift mavi ok simgesini kaldırır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "ArrowSync24",
                        IsEnabled = CheckRegistryValueExists(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons", "179")
                    },

                    // 5. Customize Drive Icons
                    new()
                    {
                        Id = "explorer_customize_drive_icons",
                        Category = "Dosya Gezgini",
                        Title = "Sürücülere Özel Simge ve Etiket Atama Desteğini Aç",
                        Description = "Kayıt defterindeki DriveIcons mekanizmasını etkinleştirerek C:, D: vb. sürücülere özel ikon ve açıklamalar atanabilmesini sağlar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IconSymbol = "HardDrive24",
                        IsEnabled = CheckKeyExists(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons")
                    },

                    // 6. Customize Libraries Item
                    new()
                    {
                        Id = "explorer_hide_libraries_nav",
                        Category = "Dosya Gezgini",
                        Title = "Sol Gezinti Bölmesinden 'Kitaplıklar' (Libraries) Simgesini Gizle",
                        Description = "Dosya Gezgini sol kenar çubuğundaki Kitaplıklar bölümünü kaldırarak daha temiz bir gezinti ağacı sağlar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "Library24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Classes\CLSID\{031E4825-7B94-4dc3-B131-E946B7D29595}", "System.IsPinnedToNameSpaceTree", 0)
                    },

                    // 7. Customize This PC Folders (3D Objects)
                    new()
                    {
                        Id = "explorer_hide_this_pc_3d_objects",
                        Category = "Dosya Gezgini",
                        Title = "Bu Bilgisayar Altındaki '3D Nesneler' Klasörünü Gizle",
                        Description = "'Bu Bilgisayar' ana görünümünde neredeyse hiç kullanılmayan gereksiz 3D Nesneler klasörünü gizler.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "Box24",
                        IsEnabled = !CheckKeyExists(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace\{0DB7E03F-FC29-4DC6-9020-FF41B59E513A}")
                    },

                    // 8. Default Drag-n-Drop Action
                    new()
                    {
                        Id = "explorer_default_drag_drop_copy",
                        Category = "Dosya Gezgini",
                        Title = "Sürükle-Bırak Varsayılan Eylemini 'Kopyala' Olarak Zorla",
                        Description = "Fareyle dosya sürükleyip bırakırken yanlışlıkla dosyaları taşımak yerine her zaman kopyalama yapılmasını sağlar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IconSymbol = "Copy24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "DefaultDropEffect", 1)
                    },

                    // 9. Disable Jump Lists
                    new()
                    {
                        Id = "explorer_disable_jump_lists",
                        Category = "Dosya Gezgini",
                        Title = "Görev Çubuğu ve Başlat Menüsü Jump List Geçmişini Kapat",
                        Description = "Görev çubuğundaki uygulamalara sağ tıklandığında son açılan belge ve sayfaların listelenmesini gizlilik için engeller.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IconSymbol = "DismissCircle24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackDocs", 0)
                    },

                    // 10. Disable Search History
                    new()
                    {
                        Id = "explorer_disable_search_history",
                        Category = "Dosya Gezgini",
                        Title = "Dosya Gezgini Arama Kutusu Geçmişini Kapat",
                        Description = "Dosya Gezgini arama kutusuna yazılan önceki arama terimlerinin açılır listede görünmesini engeller.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "History24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1)
                    },

                    // 11. Disable Thumbnail Previews for Folders
                    new()
                    {
                        Id = "explorer_disable_thumbnail_previews",
                        Category = "Dosya Gezgini",
                        Title = "Klasör Küçük Resim Önizlemelerini Kapat (Sadece Simge Göster)",
                        Description = "Klasörlerin üzerinde içindeki resim ve belgelerin küçük minyatür önizlemelerini kapatıp sadece temiz sarı klasör ikonu gösterir.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IconSymbol = "Image24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly", 1)
                    },

                    // 12. Drag-n-Drop Sensitivity
                    new()
                    {
                        Id = "explorer_drag_drop_sensitivity",
                        Category = "Dosya Gezgini",
                        Title = "Yanlışlıkla Sürüklemeyi Önlemek İçin Sürükle-Bırak Hassasiyetini Artır",
                        Description = "Tıklarken yanlışlıkla dosyaların komşu klasöre taşınmasını önlemek için sürükleme tetikleme mesafesini 4 pikselden 15 piksele çıkarır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "Cursor24",
                        IsEnabled = CheckRegistryString(Registry.CurrentUser, @"Control Panel\Desktop", "DragHeight", "15")
                    },

                    // 13. Drive Letters
                    new()
                    {
                        Id = "explorer_show_drive_letters_first",
                        Category = "Dosya Gezgini",
                        Title = "Sürücü Harflerini Etiketten Önce Göster (Örn: (C:) Windows)",
                        Description = "'Windows (C:)' yerine '(C:) Windows' biçiminde sürücü harfini en başa alarak dosya sıralamasını kolaylaştırır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "HardDrive24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowDriveLettersFirst", 4)
                    },

                    // 14. Enable Auto Completion
                    new()
                    {
                        Id = "explorer_enable_auto_completion",
                        Category = "Dosya Gezgini",
                        Title = "Dosya Gezgini Adres Çubuğunda Otomatik Tamamlamayı Etkinleştir",
                        Description = "Dosya Gezgini adres çubuğuna ve 'Çalıştır' penceresine yazılan yolları yazarken otomatik tamamlar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "TextFieldEdit24",
                        IsEnabled = CheckRegistryString(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoComplete", "AutoSuggest", "yes")
                    },

                    // 15. Enable Classic Search
                    new()
                    {
                        Id = "explorer_enable_classic_search",
                        Category = "Dosya Gezgini",
                        Title = "Dosya Gezgini Klasik Arama Davranışını ve Şeridini Geri Getir",
                        Description = "Windows 10/11'de yavaşlayan web tabanlı arama kutusu yerine anlık tepki veren klasik Win32 yerel arama çubuğunu açar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IconSymbol = "Search24",
                        IsEnabled = CheckKeyExists(Registry.CurrentUser, @"Software\Classes\CLSID\{1d64637d-31e9-4706-970d-0399b943d0b6}")
                    },

                    // 16. Enable Recycle Bin for removable drives
                    new()
                    {
                        Id = "explorer_recycle_bin_removable",
                        Category = "Dosya Gezgini",
                        Title = "Taşınabilir USB Bellekler İçin Geri Dönüşüm Kutusu Desteğini Aç",
                        Description = "USB flash belleklerden silinen dosyaların doğrudan kalıcı silinmesi yerine USB içindeki Geri Dönüşüm Kutusu'na taşınmasını sağlar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "Delete24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "RecycleBinDrives", 1)
                    },

                    // 17. File Explorer Starting Folder
                    new()
                    {
                        Id = "explorer_launch_to_this_pc",
                        Category = "Dosya Gezgini",
                        Title = "Dosya Gezgini Açılışında 'Hızlı Erişim' Yerine 'Bu Bilgisayar'ı Aç",
                        Description = "Win + E kısayoluna basıldığında veya Gezgin açıldığında Hızlı Erişim yerine doğrudan sürücülerin olduğu 'Bu Bilgisayar' penceresini açar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "Desktop24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo", 1)
                    },

                    // 18. Folder View Number to Remember
                    new()
                    {
                        Id = "explorer_folder_view_number",
                        Category = "Dosya Gezgini",
                        Title = "Windows Klasör Görünümü Hafıza Sınırını 5000'e Yükselt (BagMRU)",
                        Description = "Windows'un klasörler için hatırladığı görünüm (büyük simge, ayrıntılar vb.) sınırını 400'den 5000'e çıkararak görünümün bozulmasını önler.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "Save24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell", "BagMRU Size", 5000)
                    },

                    // 19. Hide Network from Navigation Pane
                    new()
                    {
                        Id = "explorer_hide_network_nav",
                        Category = "Dosya Gezgini",
                        Title = "Sol Gezinti Bölmesinden 'Ağ' (Network) Simgesini Gizle",
                        Description = "Gezgin sol panelinde yerel ağdaki cihazları tarayan ve yavaşlamaya sebep olan 'Ağ' kısayolunu kaldırır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IconSymbol = "WifiOff24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Classes\CLSID\{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", "System.IsPinnedToNameSpaceTree", 0)
                    },

                    // 20. Icon Cache Size
                    new()
                    {
                        Id = "explorer_icon_cache_size",
                        Category = "Dosya Gezgini",
                        Title = "Simge Önbellek Boyutunu 4096 KB'a Büyüt (Bozuk Simgeleri Önle)",
                        Description = "Windows simge önbelleğini (Icon Cache) 500 KB'dan 4 MB'a genişleterek beyaz veya bozuk çıkan simge sorunlarını çözer.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "Apps24",
                        IsEnabled = CheckRegistryString(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "Max Cached Icons", "4096")
                    },

                    // 21. Navigation Pane - Show All Folders
                    new()
                    {
                        Id = "explorer_nav_pane_show_all_folders",
                        Category = "Dosya Gezgini",
                        Title = "Sol Gezinti Bölmesinde 'Tüm Klasörleri Göster' Modunu Aç",
                        Description = "Sol gezinti ağacında Denetim Masası, Geri Dönüşüm Kutusu ve kullanıcı profil klasörleri dahil tüm hiyerarşiyi görünür kılar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IconSymbol = "FolderSearch24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "NavPaneShowAllFolders", 1)
                    },

                    // 22. Navigation Pane - Expand to Current Folder
                    new()
                    {
                        Id = "explorer_nav_pane_expand_to_folder",
                        Category = "Dosya Gezgini",
                        Title = "Gezinti Bölmesinde 'Açık Klasöre Otomatik Genişlet'i Etkinleştir",
                        Description = "Sağdaki klasörlerde gezinirken sol gezinti bölmesindeki ağacın otomatik olarak mevcut klasöre kadar açılmasını sağlar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "FolderOpen24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "NavPaneExpandToCurrentFolder", 1)
                    },

                    // 23. Thumbnail Preview Border Shadow
                    new()
                    {
                        Id = "explorer_thumbnail_border_shadow",
                        Category = "Dosya Gezgini",
                        Title = "Resim Önizleme Kenarlıklarındaki Gölge Efektini Kapat",
                        Description = "Fotoğraf ve belge küçük resimlerinin etrafındaki 3D gölge katmanını kapatarak küçük resimlerin daha hızlı çizilmesini sağlar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IconSymbol = "Shadow24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ThumbnailShadow", 0)
                    }
                };

                return list;
            });
        }

        public async Task<bool> ApplyTweakAsync(SystemTweakItem tweak, bool enable)
        {
            return await Task.Run(() =>
            {
                try
                {
                    switch (tweak.Id)
                    {
                        // 1. "Do this for all current items" Checkbox
                        case "explorer_do_for_all_checkbox":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\OperationStatusManager", "ConfirmationCheckBoxDoForAll", enable ? 1 : 0);
                            break;

                        // 2. Automatic Folder Type Discovery
                        case "explorer_disable_folder_type_discovery":
                            if (enable)
                            {
                                SetRegistryString(Registry.CurrentUser, @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell", "FolderType", "NotSpecified");
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.CurrentUser, @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell", "FolderType");
                            }
                            break;

                        // 4. Compressed Overlay Icon
                        case "explorer_remove_compressed_overlay":
                            if (enable)
                            {
                                SetRegistryString(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons", "179", @"C:\Windows\System32\shell32.dll,50");
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons", "179");
                            }
                            break;

                        // 5. Customize Drive Icons
                        case "explorer_customize_drive_icons":
                            if (enable)
                            {
                                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons", true);
                            }
                            else
                            {
                                DeleteRegistryKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons");
                            }
                            break;

                        // 6. Customize Libraries Item
                        case "explorer_hide_libraries_nav":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Classes\CLSID\{031E4825-7B94-4dc3-B131-E946B7D29595}", "System.IsPinnedToNameSpaceTree", enable ? 0 : 1);
                            break;

                        // 7. Customize This PC Folders (3D Objects)
                        case "explorer_hide_this_pc_3d_objects":
                            if (enable)
                            {
                                DeleteRegistryKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace\{0DB7E03F-FC29-4DC6-9020-FF41B59E513A}");
                            }
                            else
                            {
                                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace\{0DB7E03F-FC29-4DC6-9020-FF41B59E513A}", true);
                            }
                            break;

                        // 8. Default Drag-n-Drop Action
                        case "explorer_default_drag_drop_copy":
                            if (enable)
                            {
                                SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "DefaultDropEffect", 1);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "DefaultDropEffect");
                            }
                            break;

                        // 9. Disable Jump Lists
                        case "explorer_disable_jump_lists":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackDocs", enable ? 0 : 1);
                            break;

                        // 10. Disable Search History
                        case "explorer_disable_search_history":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", enable ? 1 : 0);
                            break;

                        // 11. Disable Thumbnail Previews for Folders
                        case "explorer_disable_thumbnail_previews":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly", enable ? 1 : 0);
                            break;

                        // 12. Drag-n-Drop Sensitivity
                        case "explorer_drag_drop_sensitivity":
                            string sensVal = enable ? "15" : "4";
                            SetRegistryString(Registry.CurrentUser, @"Control Panel\Desktop", "DragHeight", sensVal);
                            SetRegistryString(Registry.CurrentUser, @"Control Panel\Desktop", "DragWidth", sensVal);
                            break;

                        // 13. Drive Letters
                        case "explorer_show_drive_letters_first":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowDriveLettersFirst", enable ? 4 : 0);
                            break;

                        // 14. Enable Auto Completion
                        case "explorer_enable_auto_completion":
                            string autoVal = enable ? "yes" : "no";
                            SetRegistryString(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoComplete", "AutoSuggest", autoVal);
                            break;

                        // 15. Enable Classic Search
                        case "explorer_enable_classic_search":
                            if (enable)
                            {
                                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\CLSID\{1d64637d-31e9-4706-970d-0399b943d0b6}\TreatAs", true);
                                key?.SetValue("", "{00000000-0000-0000-0000-000000000000}");
                            }
                            else
                            {
                                DeleteRegistryKey(Registry.CurrentUser, @"Software\Classes\CLSID\{1d64637d-31e9-4706-970d-0399b943d0b6}");
                            }
                            break;

                        // 16. Enable Recycle Bin for removable drives
                        case "explorer_recycle_bin_removable":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "RecycleBinDrives", 1);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "RecycleBinDrives");
                            }
                            break;

                        // 17. File Explorer Starting Folder
                        case "explorer_launch_to_this_pc":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo", enable ? 1 : 2);
                            break;

                        // 18. Folder View Number to Remember
                        case "explorer_folder_view_number":
                            if (enable)
                            {
                                SetRegistryDword(Registry.CurrentUser, @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell", "BagMRU Size", 5000);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.CurrentUser, @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell", "BagMRU Size");
                            }
                            break;

                        // 19. Hide Network from Navigation Pane
                        case "explorer_hide_network_nav":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Classes\CLSID\{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", "System.IsPinnedToNameSpaceTree", enable ? 0 : 1);
                            break;

                        // 20. Icon Cache Size
                        case "explorer_icon_cache_size":
                            if (enable)
                            {
                                SetRegistryString(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "Max Cached Icons", "4096");
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "Max Cached Icons");
                            }
                            break;

                        // 21. Navigation Pane - Show All Folders
                        case "explorer_nav_pane_show_all_folders":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "NavPaneShowAllFolders", enable ? 1 : 0);
                            break;

                        // 22. Navigation Pane - Expand to Current Folder
                        case "explorer_nav_pane_expand_to_folder":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "NavPaneExpandToCurrentFolder", enable ? 1 : 0);
                            break;

                        // 23. Thumbnail Preview Border Shadow
                        case "explorer_thumbnail_border_shadow":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ThumbnailShadow", enable ? 0 : 1);
                            break;

                        default:
                            return false;
                    }

                    tweak.IsEnabled = enable;
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> SetJumpListItemsAsync(int count)
        {
            return await Task.Run(() =>
            {
                try
                {
                    int clamped = Math.Clamp(count, 0, 60);
                    SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_JumpListItems", clamped);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> ApplyAllRecommendedAsync(List<SystemTweakItem> tweaks)
        {
            bool allOk = true;
            foreach (var tweak in tweaks)
            {
                if (tweak.IsRecommended && tweak.Type == TweakType.Toggle && !tweak.IsEnabled)
                {
                    bool ok = await ApplyTweakAsync(tweak, true);
                    if (!ok) allOk = false;
                }
            }
            return allOk;
        }

        public async Task<bool> RestoreDefaultsAsync(List<SystemTweakItem> tweaks)
        {
            bool allOk = true;
            foreach (var tweak in tweaks)
            {
                if (tweak.Type == TweakType.Toggle && tweak.IsEnabled)
                {
                    bool ok = await ApplyTweakAsync(tweak, false);
                    if (!ok) allOk = false;
                }
                else if (tweak.Type == TweakType.Numeric && tweak.Id == "explorer_jumplist_item_count")
                {
                    await SetJumpListItemsAsync(10);
                    tweak.NumericValue = 10;
                }
            }
            return allOk;
        }

        #region Helpers

        private static int GetJumpListItems()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                if (key == null) return 10;
                var val = key.GetValue("Start_JumpListItems");
                if (val == null) return 10;
                return Convert.ToInt32(val);
            }
            catch
            {
                return 10;
            }
        }

        private static bool CheckKeyExists(RegistryKey root, string subKey)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckRegistryValueExists(RegistryKey root, string subKey, string valueName)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key == null) return false;
                return key.GetValue(valueName) != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckRegistryDword(RegistryKey root, string subKey, string valueName, int expectedValue)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key == null) return false;
                var val = key.GetValue(valueName);
                if (val == null) return false;
                return Convert.ToInt32(val) == expectedValue;
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckRegistryString(RegistryKey root, string subKey, string valueName, string expectedValue)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key == null) return false;
                var val = key.GetValue(valueName)?.ToString();
                return string.Equals(val, expectedValue, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void SetRegistryDword(RegistryKey root, string subKey, string valueName, int value)
        {
            try
            {
                using var key = root.CreateSubKey(subKey, true);
                key?.SetValue(valueName, value, RegistryValueKind.DWord);
            }
            catch { }
        }

        private static void SetRegistryString(RegistryKey root, string subKey, string valueName, string value)
        {
            try
            {
                using var key = root.CreateSubKey(subKey, true);
                key?.SetValue(valueName, value, RegistryValueKind.String);
            }
            catch { }
        }

        private static void DeleteRegistryValue(RegistryKey root, string subKey, string valueName)
        {
            try
            {
                using var key = root.OpenSubKey(subKey, true);
                key?.DeleteValue(valueName, false);
            }
            catch { }
        }

        private static void DeleteRegistryKey(RegistryKey root, string subKey)
        {
            try
            {
                root.DeleteSubKeyTree(subKey, false);
            }
            catch { }
        }

        #endregion
    }
}
