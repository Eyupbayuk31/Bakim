using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using Bakım.Core.Sentinel;
using Microsoft.Win32;

namespace Bakım.Services.Sentinel.Detection
{
    /// <summary>Bir MSI ürününün Windows Installer'a kayıtlı dosya, klasör ve kayıt defteri izleri.</summary>
    public sealed class MsiProductFootprint
    {
        public List<string> Files { get; } = new();
        public List<string> Folders { get; } = new();
        public List<string> RegistryKeys { get; } = new();
        public int ComponentCount => Files.Count + Folders.Count + RegistryKeys.Count;
    }

    /// <summary>
    /// MSI ürününün kesin ayak izini Windows Installer'ın bileşen kayıtlarından okur (NÖB v3 D3).
    /// Kurulum anında hiçbir sensör çalışmamış olsa da hangi dosyaların bu ürüne ait olduğunu
    /// Windows'un kendisi söyler.
    /// </summary>
    public static class MsiProductInventory
    {
        private const string UserDataPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData";

        /// <summary>
        /// Ürün adından (ve varsa sürümden) MSI ürün kodunu bulur: MSI ürünlerinin Uninstall anahtarı
        /// ürün kodudur. Kurulum olay kaydı (1033) ürün kodunu taşımadığı için gerekir.
        /// </summary>
        public static string? FindProductCode(string productName, string? version)
        {
            if (string.IsNullOrWhiteSpace(productName)) return null;
            string? fallback = null;
            var roots = new (RegistryHive Hive, string Path)[]
            {
                (RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                (RegistryHive.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
            };
            foreach (var (hive, path) in roots)
            {
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                    using var uninstall = root.OpenSubKey(path);
                    if (uninstall == null) continue;
                    foreach (string name in uninstall.GetSubKeyNames())
                    {
                        if (!Guid.TryParse(name, out _)) continue;
                        using var app = uninstall.OpenSubKey(name);
                        if (!string.Equals(app?.GetValue("DisplayName") as string, productName, StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.IsNullOrWhiteSpace(version) || string.Equals(app?.GetValue("DisplayVersion") as string, version, StringComparison.OrdinalIgnoreCase))
                            return name;
                        fallback ??= name;
                    }
                }
                catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
                {
                    AppLog.Debug($"Uninstall anahtarları okunamadı ({hive}\\{path}): {ex.Message}", nameof(MsiProductInventory));
                }
            }
            return fallback;
        }

        public static MsiProductFootprint Read(string productCode)
        {
            var result = new MsiProductFootprint();
            string? packed = MsiComponents.PackGuid(productCode);
            if (packed == null) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var userData = root.OpenSubKey(UserDataPath);
                if (userData == null) return result;

                foreach (string sid in userData.GetSubKeyNames())
                {
                    using var components = userData.OpenSubKey($@"{sid}\Components");
                    if (components == null) continue;
                    foreach (string component in components.GetSubKeyNames())
                    {
                        using var key = components.OpenSubKey(component);
                        if (key?.GetValue(packed) is not string raw || !MsiComponents.TryParseKeyPath(raw, out var keyPath)) continue;
                        if (!seen.Add(keyPath.Path)) continue;
                        switch (keyPath.Kind)
                        {
                            case MsiKeyPathKind.File: result.Files.Add(keyPath.Path); break;
                            case MsiKeyPathKind.Folder: result.Folders.Add(keyPath.Path); break;
                            default: result.RegistryKeys.Add(keyPath.Path); break;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                AppLog.Debug($"MSI bileşen kayıtları okunamadı ({productCode}): {ex.Message}", nameof(MsiProductInventory));
            }

            result.Files.Sort(StringComparer.OrdinalIgnoreCase);
            result.Folders.Sort(StringComparer.OrdinalIgnoreCase);
            result.RegistryKeys.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }
    }
}
