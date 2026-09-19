using System.Diagnostics;
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IClassicAppsService
    {
        Task<bool> ActivateWindowsPhotoViewerAsync();
        Task<bool> IsWindowsPhotoViewerActivatedAsync();
        Task<List<ClassicAppItem>> GetClassicToolsAsync();
        Task<bool> LaunchClassicToolAsync(string toolId);
    }

    public class ClassicAppsService : IClassicAppsService
    {
        private static readonly string[] ImageExtensions = new[]
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".tif", ".gif", ".ico", ".jfif", ".dib"
        };

        public async Task<bool> IsWindowsPhotoViewerActivatedAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Photo Viewer\Capabilities\FileAssociations");
                    if (key == null) return false;
                    var val = key.GetValue(".jpg")?.ToString();
                    return val == "PhotoViewer.FileAssoc.Tiff";
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> ActivateWindowsPhotoViewerAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 1. PhotoViewer.FileAssoc.Tiff tanımını garantiye al
                    string progId = "PhotoViewer.FileAssoc.Tiff";
                    using (var cmdKey = Registry.ClassesRoot.CreateSubKey($@"{progId}\shell\open\command", true))
                    {
                        cmdKey?.SetValue("", @"%SystemRoot%\System32\rundll32.exe ""%ProgramFiles%\Windows Photo Viewer\PhotoViewer.dll"", ImageView_Fullscreen %1", RegistryValueKind.ExpandString);
                    }

                    using (var dropKey = Registry.ClassesRoot.CreateSubKey($@"{progId}\shell\open\DropTarget", true))
                    {
                        dropKey?.SetValue("Clsid", "{FFE2A43C-56B9-4bf5-9A79-CE6D4278460B}", RegistryValueKind.String);
                    }

                    // 2. Capabilities\FileAssociations altına tüm resim uzantılarını bağla
                    using (var assocKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows Photo Viewer\Capabilities\FileAssociations", true))
                    {
                        if (assocKey != null)
                        {
                            foreach (var ext in ImageExtensions)
                            {
                                assocKey.SetValue(ext, progId, RegistryValueKind.String);
                            }
                        }
                    }

                    // 3. RegisteredApplications altına ekle
                    using (var regAppKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\RegisteredApplications", true))
                    {
                        regAppKey?.SetValue("Windows Photo Viewer", @"SOFTWARE\Microsoft\Windows Photo Viewer\Capabilities", RegistryValueKind.String);
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<List<ClassicAppItem>> GetClassicToolsAsync()
        {
            return await Task.Run(async () =>
            {
                bool isPhotoActive = await IsWindowsPhotoViewerActivatedAsync();

                var list = new List<ClassicAppItem>
                {
                    new()
                    {
                        Id = "photo_viewer",
                        Title = "Klasik Windows Fotoğraf Görüntüleyicisi",
                        Description = "Windows 7'nin hızlı, akıcı ve kasmayan orijinal Windows Photo Viewer motorunu tüm resim formatları için etkinleştirir.",
                        IconSymbol = "Image24",
                        ExecutablePath = "PhotoViewer.dll",
                        IsActivated = isPhotoActive
                    },
                    new()
                    {
                        Id = "calc",
                        Title = "Klasik Hesap Makinesi (Calc)",
                        Description = "Windows 7/8'in UWP olmayan, anında açılan ve RAM tüketmeyen saf Win32 hesap makinesi.",
                        IconSymbol = "Calculator24",
                        ExecutablePath = "calc.exe",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "notepad",
                        Title = "Klasik Not Defteri (Notepad)",
                        Description = "Windows 11 Mağaza sürümü yerine anında sıfır gecikmeyle açılan klasik Not Defteri.",
                        IconSymbol = "Document24",
                        ExecutablePath = "notepad.exe",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "wordpad",
                        Title = "Klasik WordPad (Zengin Metin)",
                        Description = "Windows 11'in yeni sürümlerinde kaldırılan temel ve hızlı RTF/DOCX editörü WordPad.",
                        IconSymbol = "DocumentText24",
                        ExecutablePath = "write.exe",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "msconfig",
                        Title = "Sistem Yapılandırması (MSConfig)",
                        Description = "Önyükleme seçenekleri, güvenli mod anahtarları ve gelişmiş servis başlatma konsolu.",
                        IconSymbol = "Settings24",
                        ExecutablePath = "msconfig.exe",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "resmon",
                        Title = "Kaynak İzleyicisi (Resource Monitor)",
                        Description = "Ağ bağlantılarını, disk I/O işlemlerini ve işlemci çekirdek yüklerini derinlemesine inceleme aracı.",
                        IconSymbol = "HeartPulse24",
                        ExecutablePath = "resmon.exe",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "diskmgmt",
                        Title = "Disk Yönetimi Konsolu (Disk Management)",
                        Description = "Bölümleri yeniden boyutlandırma, VHD sanal disk bağlama ve sürücü harfi atama konsolu.",
                        IconSymbol = "HardDrive24",
                        ExecutablePath = "diskmgmt.msc",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "secpol",
                        Title = "Yerel Güvenlik İlkesi (SecPol)",
                        Description = "Kullanıcı hakları, parola karmaşıklığı ve sistem güvenlik denetimi kuralları yöneticisi.",
                        IconSymbol = "ShieldKeyhole24",
                        ExecutablePath = "secpol.msc",
                        IsActivated = true
                    }
                };

                return list;
            });
        }

        public async Task<bool> LaunchClassicToolAsync(string toolId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string target = toolId switch
                    {
                        "calc" => "calc.exe",
                        "notepad" => "notepad.exe",
                        "wordpad" => "write.exe",
                        "msconfig" => "msconfig.exe",
                        "resmon" => "resmon.exe",
                        "diskmgmt" => "diskmgmt.msc",
                        "secpol" => "secpol.msc",
                        _ => string.Empty
                    };

                    if (string.IsNullOrWhiteSpace(target)) return false;

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = target,
                        UseShellExecute = true
                    });

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }
    }
}
