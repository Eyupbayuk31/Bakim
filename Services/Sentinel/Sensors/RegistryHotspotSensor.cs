using System;
using System.Collections.Generic;
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services.Sentinel.Sensors
{
    /// <summary>
    /// Run/RunOnce deerlerini, Windows servislerini ve kritik sistem
    /// anahtarlarn hem 64-bit hem de 32-bit kayt defteri grnmlerinde
    /// deer dzeyinde yakalayan yksek duyarlkl sensr.
    /// </summary>
    public static class RegistryHotspotSensor
    {
        public static readonly string[] RunSubKeyPaths = new[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
            @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
            @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce",
            @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run"
        };

        public static readonly string[] UninstallSubKeyPaths = new[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
            @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        public const string ServicesSubKeyPath = @"SYSTEM\CurrentControlSet\Services";

        /// <summary>
        /// Kritik kayt defteri noktalarndan ncesi/sonras karlatrmas iin
        /// tam anlk durum (snapshot) alr. Anahtar adlarn ve Run/Services deerlerini ierir.
        /// </summary>
        public static Dictionary<string, string> CaptureHotspotSnapshot()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 1. HKLM Run/RunOnce Deerleri (64 & 32 bit)
            CaptureRunValues(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM", map);
            CaptureRunValues(RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM", map);

            // 2. HKCU Run/RunOnce Deerleri
            CaptureRunValues(RegistryHive.CurrentUser, RegistryView.Default, "HKCU", map);

            // 3. HKLM Services Anahtarlar ve Servis Bilgileri
            CaptureServices(map);

            // 4. Uninstall Anahtarlar (HKLM 64, HKLM 32, HKCU)
            CaptureUninstallKeys(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM", map);
            CaptureUninstallKeys(RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM", map);
            CaptureUninstallKeys(RegistryHive.CurrentUser, RegistryView.Default, "HKCU", map);

            return map;
        }

        /// <summary>
        /// ki snapshot arasndaki fark karp yeni eklenen/deien kaytlar,
        /// servisleri ve balang girdilerini ayrtrr.
        /// </summary>
        public static (List<SetupRegistryRecord> records, List<string> addedServices, List<string> addedStartupEntries) ComputeDelta(
            Dictionary<string, string> preSnapshot,
            Dictionary<string, string> postSnapshot)
        {
            var records = new List<SetupRegistryRecord>();
            var addedServices = new List<string>();
            var addedStartupEntries = new List<string>();

            foreach (var kvp in postSnapshot)
            {
                string key = kvp.Key;
                string valueData = kvp.Value;

                bool existsInPre = preSnapshot.TryGetValue(key, out string? preValueData);

                if (!existsInPre)
                {
                    // Yeni eklenen anahtar veya deer
                    string hive = key.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) ? "HKLM" : "HKCU";
                    bool isService = key.Contains(@"\Services\", StringComparison.OrdinalIgnoreCase);
                    bool isRun = key.Contains(@"\Run", StringComparison.OrdinalIgnoreCase);

                    var rec = new SetupRegistryRecord
                    {
                        Hive = hive,
                        KeyPath = key,
                        ValueData = valueData,
                        ChangeKind = string.IsNullOrEmpty(valueData) ? "KeyAdded" : "ValueAdded",
                        IsAutorunOrService = isService || isRun
                    };

                    records.Add(rec);

                    if (isService)
                    {
                        addedServices.Add(key);
                    }
                    else if (isRun)
                    {
                        addedStartupEntries.Add(key);
                    }
                }
                else if (preValueData != valueData)
                {
                    // Deitirilen deer
                    string hive = key.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) ? "HKLM" : "HKCU";
                    bool isService = key.Contains(@"\Services\", StringComparison.OrdinalIgnoreCase);
                    bool isRun = key.Contains(@"\Run", StringComparison.OrdinalIgnoreCase);

                    records.Add(new SetupRegistryRecord
                    {
                        Hive = hive,
                        KeyPath = key,
                        ValueData = valueData,
                        ChangeKind = "ValueModified",
                        IsAutorunOrService = isService || isRun
                    });
                }
            }

            return (records, addedServices, addedStartupEntries);
        }

        private static void CaptureRunValues(RegistryHive hive, RegistryView view, string hivePrefix, Dictionary<string, string> map)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                foreach (var runPath in RunSubKeyPaths)
                {
                    try
                    {
                        using var subKey = baseKey.OpenSubKey(runPath);
                        if (subKey == null) continue;

                        string normalizedKeyPath = $@"{hivePrefix}\{runPath}";
                        foreach (var valName in subKey.GetValueNames())
                        {
                            string fullValPath = $@"{normalizedKeyPath}\{valName}";
                            string data = subKey.GetValue(valName)?.ToString() ?? string.Empty;
                            map[fullValPath] = data;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void CaptureServices(Dictionary<string, string> map)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var servicesKey = baseKey.OpenSubKey(ServicesSubKeyPath);
                if (servicesKey == null) return;

                foreach (var serviceName in servicesKey.GetSubKeyNames())
                {
                    try
                    {
                        string normalizedPath = $@"HKLM\{ServicesSubKeyPath}\{serviceName}";
                        using var svcKey = servicesKey.OpenSubKey(serviceName);
                        if (svcKey != null)
                        {
                            string imagePath = svcKey.GetValue("ImagePath")?.ToString() ?? string.Empty;
                            map[normalizedPath] = imagePath;
                        }
                        else
                        {
                            map[normalizedPath] = string.Empty;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void CaptureUninstallKeys(RegistryHive hive, RegistryView view, string hivePrefix, Dictionary<string, string> map)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                foreach (var uninstallPath in UninstallSubKeyPaths)
                {
                    try
                    {
                        using var subKey = baseKey.OpenSubKey(uninstallPath);
                        if (subKey == null) continue;

                        string normalizedKeyPath = $@"{hivePrefix}\{uninstallPath}";
                        foreach (var appKeyName in subKey.GetSubKeyNames())
                        {
                            string fullPath = $@"{normalizedKeyPath}\{appKeyName}";
                            using var appKey = subKey.OpenSubKey(appKeyName);
                            string displayName = appKey?.GetValue("DisplayName")?.ToString() ?? string.Empty;
                            map[fullPath] = displayName;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
