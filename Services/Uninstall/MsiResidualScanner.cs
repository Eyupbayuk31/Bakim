using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using Bakım.Core.Safety;
using Bakım.Core.Text;
using Bakım.Core.Uninstall;
using Bakım.Models;

namespace Bakım.Services.Uninstall
{
    public interface IMsiResidualScanner
    {
        void Scan(string productCode, InstalledAppItem app, List<LeftoverItem> results);
    }

    /// <summary>
    /// Windows Installer (MSI) paketlerinin arkasında bıraktığı derin kayıt defteri ve disk kalıntılarını tarar.
    ///
    /// Revo Uninstaller gibi derin tarama araçlarının tespit ettiği:
    ///   • S-1-5-18\Components altındaki yüzlerce bileşen anahtarı ve referansı
    ///   • UserData\Products ve Classes\Products altındaki ürün meta verileri
    ///   • UpgradeCodes altındaki yükseltme kodu bağlantıları
    ///   • Installer\Folders altındaki dizin kayıtları
    ///   • C:\Windows\Installer altındaki SourceHash ve önbellek .msi dosyaları
    /// </summary>
    public sealed class MsiResidualScanner : IMsiResidualScanner
    {
        /// <summary>Tek bir anahtar/klasör okunamazsa tarama sürer; neden günlüğe yazılır.</summary>
        private static void Note(Exception ex) => AppLog.Debug($"MSI kalıntı taraması: {ex.GetType().Name}: {ex.Message}", nameof(MsiResidualScanner));

        private const int Certain = (int)MatchConfidence.Certain;

        public void Scan(string productCode, InstalledAppItem app, List<LeftoverItem> results)
        {
            if (string.IsNullOrWhiteSpace(productCode)) return;

            string? packed = MsiGuidHelper.ToPackedGuid(productCode);
            if (string.IsNullOrEmpty(packed)) return;

            ScanUserDataProducts(packed, results);
            ScanClassesInstallerProducts(packed, results);
            ScanUpgradeCodes(packed, results);
            ScanComponents(packed, results);
            ScanInstallerFolders(app.InstallLocation, productCode, results);
            ScanInstallerCacheFiles(productCode, results);
        }

        #region UserData Products & LocalPackage

        private static void ScanUserDataProducts(string packedGuid, List<LeftoverItem> results)
        {
            string subKey = $@"Software\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products\{packedGuid}";
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var productKey = baseKey.OpenSubKey(subKey);
                if (productKey == null) return;

                // 1. Önbellek .msi dosyasını oku (LocalPackage değeri)
                using (var installProps = productKey.OpenSubKey("InstallProperties"))
                {
                    if (installProps != null)
                    {
                        string? localPackage = installProps.GetValue("LocalPackage") as string;
                        if (!string.IsNullOrWhiteSpace(localPackage) && File.Exists(localPackage))
                        {
                            try
                            {
                                var fi = new FileInfo(localPackage);
                                results.Add(new LeftoverItem
                                {
                                    Path = fi.FullName,
                                    ItemType = LeftoverType.File,
                                    SizeBytes = fi.Length,
                                    FormattedSize = ByteFormatter.Format(fi.Length),
                                    Description = "Önbelleğe alınmış MSI kurulum paketi",
                                    EvidenceText = "Windows Installer önbelleğindeki yetim .msi kurulum paketi.",
                                    ConfidenceScore = Certain,
                                    AllowOutsideKnownRoots = true,
                                    IsSelected = true
                                });
                            }
                            catch (Exception ex) { Note(ex); }
                        }
                    }
                }

                // 2. Products anahtarını kalıntı olarak ekle
                var regPath = new RegistryPath(RegistryHive.LocalMachine, RegistryView.Registry64, subKey);
                results.Add(new LeftoverItem
                {
                    Path = regPath.ToDisplay(),
                    ItemType = LeftoverType.RegistryKey,
                    FormattedSize = "Kayıt anahtarı",
                    Description = "MSI ürün kayıt defteri anahtarı",
                    EvidenceText = $"Windows Installer ürün meta verisi (Packed GUID: {packedGuid}).",
                    ConfidenceScore = Certain,
                    IsSelected = true
                });
            }
            catch (Exception ex) { Note(ex); }
        }

        #endregion

        #region Classes Installer Products & Features

        private static void ScanClassesInstallerProducts(string packedGuid, List<LeftoverItem> results)
        {
            void CheckClassKey(string subKey, string desc)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                    using var key = baseKey.OpenSubKey(subKey);
                    if (key != null)
                    {
                        var regPath = new RegistryPath(RegistryHive.LocalMachine, RegistryView.Registry64, subKey);
                        results.Add(new LeftoverItem
                        {
                            Path = regPath.ToDisplay(),
                            ItemType = LeftoverType.RegistryKey,
                            FormattedSize = "Kayıt anahtarı",
                            Description = desc,
                            EvidenceText = $"Windows Installer sınıf kaydı (Packed GUID: {packedGuid}).",
                            ConfidenceScore = Certain,
                            IsSelected = true
                        });
                    }
                }
                catch (Exception ex) { Note(ex); }
            }

            CheckClassKey($@"Software\Classes\Installer\Products\{packedGuid}", "MSI ürün sınıf kaydı");
            CheckClassKey($@"Software\Classes\Installer\Features\{packedGuid}", "MSI özellik sınıf kaydı");
        }

        #endregion

        #region UpgradeCodes

        private static void ScanUpgradeCodes(string packedGuid, List<LeftoverItem> results)
        {
            void ScanUpgradeRoot(string rootSubKey, string desc)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                    using var upgradeKey = baseKey.OpenSubKey(rootSubKey);
                    if (upgradeKey == null) return;

                    foreach (string codeKeyName in upgradeKey.GetSubKeyNames())
                    {
                        using var codeKey = upgradeKey.OpenSubKey(codeKeyName);
                        if (codeKey == null) continue;

                        string[] valueNames = codeKey.GetValueNames();
                        bool matches = false;
                        foreach (string vn in valueNames)
                        {
                            if (string.Equals(vn, packedGuid, StringComparison.OrdinalIgnoreCase))
                            {
                                matches = true;
                                break;
                            }
                        }

                        if (!matches) continue;

                        string path = $@"{rootSubKey}\{codeKeyName}";
                        if (valueNames.Length <= 1)
                        {
                            var regPath = new RegistryPath(RegistryHive.LocalMachine, RegistryView.Registry64, path);
                            results.Add(new LeftoverItem
                            {
                                Path = regPath.ToDisplay(),
                                ItemType = LeftoverType.RegistryKey,
                                FormattedSize = "Kayıt anahtarı",
                                Description = $"{desc} anahtarı",
                                EvidenceText = $"Bu ürün koduna ait yükseltme bağlantısı (Packed GUID: {packedGuid}).",
                                ConfidenceScore = Certain,
                                IsSelected = true
                            });
                        }
                        else
                        {
                            var regPath = new RegistryPath(RegistryHive.LocalMachine, RegistryView.Registry64, path, packedGuid);
                            results.Add(new LeftoverItem
                            {
                                Path = regPath.ToString(),
                                ItemType = LeftoverType.RegistryValue,
                                FormattedSize = "Kayıt değeri",
                                Description = $"{desc} değeri",
                                EvidenceText = $"Yükseltme kodundaki ürün referansı (Packed GUID: {packedGuid}).",
                                ConfidenceScore = Certain,
                                IsSelected = true
                            });
                        }
                    }
                }
                catch (Exception ex) { Note(ex); }
            }

            ScanUpgradeRoot(@"Software\Microsoft\Windows\CurrentVersion\Installer\UpgradeCodes", "MSI UpgradeCode");
            ScanUpgradeRoot(@"Software\Classes\Installer\UpgradeCodes", "MSI UpgradeCode (Classes)");
        }

        #endregion

        #region MSI Components

        private static void ScanComponents(string packedGuid, List<LeftoverItem> results)
        {
            const string componentsSubKey = @"Software\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Components";
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var componentsKey = baseKey.OpenSubKey(componentsSubKey);
                if (componentsKey == null) return;

                foreach (string compName in componentsKey.GetSubKeyNames())
                {
                    using var compKey = componentsKey.OpenSubKey(compName);
                    if (compKey == null) continue;

                    string[] valNames = compKey.GetValueNames();
                    bool hasProduct = false;
                    foreach (string vn in valNames)
                    {
                        if (string.Equals(vn, packedGuid, StringComparison.OrdinalIgnoreCase))
                        {
                            hasProduct = true;
                            break;
                        }
                    }

                    if (!hasProduct) continue;

                    string compPath = $@"{componentsSubKey}\{compName}";
                    if (valNames.Length <= 1)
                    {
                        // Yalnızca bu ürüne ait bileşen: tüm anahtar silinebilir
                        var regPath = new RegistryPath(RegistryHive.LocalMachine, RegistryView.Registry64, compPath);
                        results.Add(new LeftoverItem
                        {
                            Path = regPath.ToDisplay(),
                            ItemType = LeftoverType.RegistryKey,
                            FormattedSize = "Kayıt anahtarı",
                            Description = "MSI bileşen kaydı",
                            EvidenceText = $"Bileşen bu ürüne aittir (Packed GUID: {packedGuid}).",
                            ConfidenceScore = Certain,
                            IsSelected = true
                        });
                    }
                    else
                    {
                        // Paylaşılan bileşen: yalnızca bu ürünün referans değeri silinir
                        var regPath = new RegistryPath(RegistryHive.LocalMachine, RegistryView.Registry64, compPath, packedGuid);
                        results.Add(new LeftoverItem
                        {
                            Path = regPath.ToString(),
                            ItemType = LeftoverType.RegistryValue,
                            FormattedSize = "Kayıt değeri",
                            Description = "MSI paylaşılan bileşen referansı",
                            EvidenceText = $"Bileşenin bu ürüne referansı (Packed GUID: {packedGuid}).",
                            ConfidenceScore = Certain,
                            IsSelected = true
                        });
                    }
                }
            }
            catch (Exception ex) { Note(ex); }
        }

        #endregion

        #region Installer Folders

        private static void ScanInstallerFolders(string? installLocation, string productCode, List<LeftoverItem> results)
        {
            const string foldersSubKey = @"Software\Microsoft\Windows\CurrentVersion\Installer\Folders";
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var foldersKey = baseKey.OpenSubKey(foldersSubKey);
                if (foldersKey == null) return;

                string? normInstall = !string.IsNullOrWhiteSpace(installLocation)
                    ? WindowsPath.Normalize(installLocation)
                    : null;

                foreach (string folderVal in foldersKey.GetValueNames())
                {
                    if (string.IsNullOrWhiteSpace(folderVal)) continue;

                    bool match = false;
                    if (normInstall != null && (WindowsPath.IsUnderOrEqual(folderVal, normInstall) ||
                                                WindowsPath.Normalize(folderVal)?.StartsWith(normInstall, StringComparison.OrdinalIgnoreCase) == true))
                    {
                        match = true;
                    }
                    else if (folderVal.Contains(productCode, StringComparison.OrdinalIgnoreCase))
                    {
                        match = true;
                    }

                    if (match)
                    {
                        var regPath = new RegistryPath(RegistryHive.LocalMachine, RegistryView.Registry64, foldersSubKey, folderVal);
                        results.Add(new LeftoverItem
                        {
                            Path = regPath.ToString(),
                            ItemType = LeftoverType.RegistryValue,
                            FormattedSize = "Kayıt değeri",
                            Description = "MSI kayıtlı klasör girdisi",
                            EvidenceText = $"Windows Installer dizin indeksi: {folderVal}",
                            ConfidenceScore = Certain,
                            IsSelected = true
                        });
                    }
                }
            }
            catch (Exception ex) { Note(ex); }
        }

        #endregion

        #region Windows Installer Cache Files

        private static void ScanInstallerCacheFiles(string productCode, List<LeftoverItem> results)
        {
            try
            {
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string installerDir = Path.Combine(winDir, "Installer");
                if (!Directory.Exists(installerDir)) return;

                // 1. SourceHash{GUID}
                string sourceHashFile = Path.Combine(installerDir, "SourceHash" + productCode);
                if (File.Exists(sourceHashFile))
                {
                    try
                    {
                        var fi = new FileInfo(sourceHashFile);
                        results.Add(new LeftoverItem
                        {
                            Path = fi.FullName,
                            ItemType = LeftoverType.File,
                            SizeBytes = fi.Length,
                            FormattedSize = ByteFormatter.Format(fi.Length),
                            Description = "MSI kaynak özet dosyası (SourceHash)",
                            EvidenceText = "Windows Installer kaynak hash dosyası.",
                            ConfidenceScore = Certain,
                            AllowOutsideKnownRoots = true,
                            IsSelected = true
                        });
                    }
                    catch (Exception ex) { Note(ex); }
                }

                // 2. Installer\{GUID} klasörü
                string guidFolder = Path.Combine(installerDir, productCode);
                if (Directory.Exists(guidFolder))
                {
                    try
                    {
                        long size = 0;
                        try
                        {
                            foreach (var f in Directory.EnumerateFiles(guidFolder, "*", SearchOption.AllDirectories))
                                size += new FileInfo(f).Length;
                        }
                        catch (Exception ex) { Note(ex); }

                        results.Add(new LeftoverItem
                        {
                            Path = guidFolder,
                            ItemType = LeftoverType.Folder,
                            SizeBytes = size,
                            FormattedSize = ByteFormatter.Format(size),
                            Description = "MSI kurulum kaynak klasörü",
                            EvidenceText = "Windows Installer ürün kaynak klasörü.",
                            ConfidenceScore = Certain,
                            AllowOutsideKnownRoots = true,
                            IsSelected = true
                        });
                    }
                    catch (Exception ex) { Note(ex); }
                }
            }
            catch (Exception ex) { Note(ex); }
        }

        #endregion
    }
}
