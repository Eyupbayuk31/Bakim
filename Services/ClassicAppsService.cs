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
                    // 1. SISTEM & ÇEKİRDEK YÖNETİMİ
                    new()
                    {
                        Id = "devmgmt",
                        Title = "Aygıt Yöneticisi (Device Manager)",
                        Description = "Donanım bileşenleri, sürücü güncellemeleri ve aygıt durumlarını teftiş etme konsolu.",
                        Category = "Sistem",
                        IconSymbol = "DeveloperBoard24",
                        ExecutablePath = "devmgmt.msc",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "compmgmt",
                        Title = "Bilgisayar Yönetimi (Computer Management)",
                        Description = "Olay görüntüleyici, paylaşılan klasörler, servisler ve depolama birimlerini tek konsolda toplar.",
                        Category = "Sistem",
                        IconSymbol = "Desktop24",
                        ExecutablePath = "compmgmt.msc",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "services",
                        Title = "Windows Hizmetleri (Services)",
                        Description = "Arka plan sistem servislerini başlatma, durdurma ve başlangıç türlerini (Otomatik/Devre Dışı) yapılandırma.",
                        Category = "Sistem",
                        IconSymbol = "Settings24",
                        ExecutablePath = "services.msc",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "regedit",
                        Title = "Kayıt Defteri Düzenleyicisi (Regedit)",
                        Description = "Windows kayıt defteri kovanlarını, sistem anahtarlarını ve gelişmiş yapılandırmaları doğrudan düzenleme.",
                        Category = "Sistem",
                        IconSymbol = "Wrench24",
                        ExecutablePath = "regedit.exe",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "msconfig",
                        Title = "Sistem Yapılandırması (MSConfig)",
                        Description = "Önyükleme seçenekleri, güvenli mod anahtarları ve gelişmiş servis başlatma konsolu.",
                        Category = "Sistem",
                        IconSymbol = "Settings24",
                        ExecutablePath = "msconfig.exe",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "eventvwr",
                        Title = "Olay Görüntüleyicisi (Event Viewer)",
                        Description = "Sistem çökmeleri, mavi ekran (BSOD) analizleri ve uygulama hata günlüklerini detaylı inceleme.",
                        Category = "Sistem",
                        IconSymbol = "DocumentText24",
                        ExecutablePath = "eventvwr.msc",
                        IsActivated = true
                    },

                    // 2. DONANIM, DİSK & PERFORMANS
                    new()
                    {
                        Id = "diskmgmt",
                        Title = "Disk Yönetimi Konsolu (Disk Management)",
                        Description = "Bölümleri yeniden boyutlandırma, VHD sanal disk bağlama ve sürücü harfi atama konsolu.",
                        Category = "Donanım & Disk",
                        IconSymbol = "HardDrive24",
                        ExecutablePath = "diskmgmt.msc",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "cleanmgr",
                        Title = "Gelişmiş Disk Temizleme (CleanMgr)",
                        Description = "Windows Update temizliği, geçici dosyalar ve sistem önbelleği temizleme konsolu.",
                        Category = "Donanım & Disk",
                        IconSymbol = "Storage24",
                        ExecutablePath = "cleanmgr.exe",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "resmon",
                        Title = "Kaynak İzleyicisi (Resource Monitor)",
                        Description = "Ağ bağlantılarını, disk I/O işlemlerini ve işlemci çekirdek yüklerini derinlemesine inceleme aracı.",
                        Category = "Donanım & Disk",
                        IconSymbol = "HeartPulse24",
                        ExecutablePath = "resmon.exe",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "dxdiag",
                        Title = "DirectX Teşhis Aracı (DxDiag)",
                        Description = "DirectX sürümleri, ekran kartı VRAM, ses donanımı ve grafik sürücü teşhis raporu.",
                        Category = "Donanım & Disk",
                        IconSymbol = "Games24",
                        ExecutablePath = "dxdiag.exe",
                        IsActivated = true
                    },

                    // 3. AĞ, KULLANICI & GÜVENLİK
                    new()
                    {
                        Id = "gpedit",
                        Title = "Yerel Grup İlkesi Düzenleyicisi (GPEdit)",
                        Description = "İşletim sistemi ilkeleri, Windows Update ve kullanıcı kısıtlamalarını yöneten kurumsal ilke konsolu.",
                        Category = "Ağ & Güvenlik",
                        IconSymbol = "ShieldKeyhole24",
                        ExecutablePath = "gpedit.msc",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "secpol",
                        Title = "Yerel Güvenlik İlkesi (SecPol)",
                        Description = "Kullanıcı hakları atamaları, parola karmaşıklığı ve sistem güvenlik denetimi kuralları yöneticisi.",
                        Category = "Ağ & Güvenlik",
                        IconSymbol = "ShieldKeyhole24",
                        ExecutablePath = "secpol.msc",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "ncpa",
                        Title = "Ağ Bağlantıları Paneli (NCPA)",
                        Description = "Ethernet ve Wi-Fi adaptörleri, statik IP atama, DNS sunucu ayarları ve bağdaştırıcı özellikleri.",
                        Category = "Ağ & Güvenlik",
                        IconSymbol = "Globe24",
                        ExecutablePath = "ncpa.cpl",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "certmgr",
                        Title = "Sistem Sertifika Yöneticisi (CertMgr)",
                        Description = "Kök sertifika yetkilileri, SSL güvenlik sertifikaları ve güvenilen sertifika depolarını yönetme.",
                        Category = "Ağ & Güvenlik",
                        IconSymbol = "ShieldCheckmark24",
                        ExecutablePath = "certmgr.msc",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "lusrmgr",
                        Title = "Yerel Kullanıcılar ve Gruplar (LusrMgr)",
                        Description = "Windows yerel kullanıcı hesapları, parolaları ve yönetici (Administrators) grup üyelikleri.",
                        Category = "Ağ & Güvenlik",
                        IconSymbol = "Person24",
                        ExecutablePath = "lusrmgr.msc",
                        IsActivated = true
                    },

                    // 4. HIZLI ERİŞİM & GİZLİ ARAÇLAR
                    new()
                    {
                        Id = "photo_viewer",
                        Title = "Klasik Windows Fotoğraf Görüntüleyicisi",
                        Description = "Windows 7'nin hızlı, akıcı ve kasmayan orijinal Windows Photo Viewer motorunu tüm resim formatları için etkinleştirir.",
                        Category = "Hızlı Erişim",
                        IconSymbol = "Image24",
                        ExecutablePath = "PhotoViewer.dll",
                        IsActivated = isPhotoActive
                    },
                    new()
                    {
                        Id = "godmode",
                        Title = "Windows God Mode (Tüm Sistem Ayarları)",
                        Description = "Windows'un 200'den fazla gizli kontrol ve denetim masası ayarını tek bir klasörde listeleyen özel panel.",
                        Category = "Hızlı Erişim",
                        IconSymbol = "Flash24",
                        ExecutablePath = "shell:::{ED7BA470-8E54-465E-825C-99712043E01C}",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "control",
                        Title = "Klasik Denetim Masası (Control Panel)",
                        Description = "Windows 11'in yeni ayarlar sayfası yerine tüm klasik applet'leri içeren orijinal Denetim Masası.",
                        Category = "Hızlı Erişim",
                        IconSymbol = "Grid24",
                        ExecutablePath = "control.exe",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "sysdm",
                        Title = "Gelişmiş Sistem Özellikleri (SysDM)",
                        Description = "Ortam değişkenleri (Environment Variables), sanal bellek (Pagefile) ve donanım profilleri penceresi.",
                        Category = "Hızlı Erişim",
                        IconSymbol = "Apps24",
                        ExecutablePath = "sysdm.cpl",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "wt",
                        Title = "Windows Terminal / Konsol (WT)",
                        Description = "PowerShell, CMD ve WSL sekmelerini bir arada sunan modern Windows Terminal konsolu.",
                        Category = "Hızlı Erişim",
                        IconSymbol = "Code24",
                        ExecutablePath = "wt.exe",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "calc",
                        Title = "Klasik Hesap Makinesi (Calc)",
                        Description = "Windows 7/8'in UWP olmayan, anında açılan ve RAM tüketmeyen saf Win32 hesap makinesi.",
                        Category = "Hızlı Erişim",
                        IconSymbol = "Calculator24",
                        ExecutablePath = "calc.exe",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "notepad",
                        Title = "Klasik Not Defteri (Notepad)",
                        Description = "Windows 11 Mağaza sürümü yerine anında sıfır gecikmeyle açılan klasik Not Defteri.",
                        Category = "Hızlı Erişim",
                        IconSymbol = "Document24",
                        ExecutablePath = "notepad.exe",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "wordpad",
                        Title = "Klasik WordPad (Zengin Metin)",
                        Description = "Windows 11'in yeni sürümlerinde kaldırılan temel ve hızlı RTF/DOCX editörü WordPad.",
                        Category = "Hızlı Erişim",
                        IconSymbol = "DocumentText24",
                        ExecutablePath = "write.exe",
                        IsActivated = true
                    },
                    new()
                    {
                        Id = "charmap",
                        Title = "Karakter Eşlem (Charmap)",
                        Description = "Unicode sembolleri, özel karakterler ve yazı tipi gliflerini panoya kopyalama aracı.",
                        Category = "Hızlı Erişim",
                        IconSymbol = "FontIncrease24",
                        ExecutablePath = "charmap.exe",
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
                    if (toolId == "godmode")
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = "shell:::{ED7BA470-8E54-465E-825C-99712043E01C}",
                            UseShellExecute = true
                        });
                        return true;
                    }

                    string target = toolId switch
                    {
                        "calc" => "calc.exe",
                        "notepad" => "notepad.exe",
                        "wordpad" => "write.exe",
                        "msconfig" => "msconfig.exe",
                        "resmon" => "resmon.exe",
                        "diskmgmt" => "diskmgmt.msc",
                        "secpol" => "secpol.msc",
                        "devmgmt" => "devmgmt.msc",
                        "compmgmt" => "compmgmt.msc",
                        "services" => "services.msc",
                        "regedit" => "regedit.exe",
                        "gpedit" => "gpedit.msc",
                        "eventvwr" => "eventvwr.msc",
                        "cleanmgr" => "cleanmgr.exe",
                        "dxdiag" => "dxdiag.exe",
                        "ncpa" => "ncpa.cpl",
                        "certmgr" => "certmgr.msc",
                        "lusrmgr" => "lusrmgr.msc",
                        "control" => "control.exe",
                        "sysdm" => "sysdm.cpl",
                        "wt" => "wt.exe",
                        "charmap" => "charmap.exe",
                        _ => string.Empty
                    };

                    if (string.IsNullOrWhiteSpace(target)) return false;

                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = target,
                            UseShellExecute = true
                        });
                        return true;
                    }
                    catch
                    {
                        if (toolId == "wt")
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "powershell.exe",
                                UseShellExecute = true
                            });
                            return true;
                        }
                        throw;
                    }
                }
                catch
                {
                    return false;
                }
            });
        }
    }
}
