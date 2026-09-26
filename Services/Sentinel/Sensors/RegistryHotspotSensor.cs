using System;
using System.Collections.Generic;
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services.Sentinel.Sensors
{
    /// <summary>
    /// Run/RunOnce değerlerini, Windows servislerini ve kritik sistem
    /// anahtarlarını hem 64-bit hem de 32-bit kayıt defteri görünümlerinde
    /// değer düzeyinde yakalayan yüksek duyarlıklı sensör.
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
        /// Kritik kayıt defteri noktalarından öncesi/sonrası karşılaştırması için
        /// tam anlık durum (snapshot) alır. Anahtar adlarını ve Run/Services değerlerini içerir.
        /// </summary>
        public static Dictionary<string, string> CaptureHotspotSnapshot()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 1. HKLM Run/RunOnce değerleri. 32 bit girdiler WOW6432Node yollarıyla 64 bit görünümden
            //    okunur. Registry32 görünümü ayrıca okunmaz: o görünümde "Software\Microsoft\…\Run"
            //    aslında WOW6432Node'dur ve 64 bit anahtar adıyla yazılınca 64 bit değerleri eziyordu (A3).
            CaptureRunValues(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM", map);

            // 2. HKCU Run/RunOnce değerleri
            CaptureRunValues(RegistryHive.CurrentUser, RegistryView.Default, "HKCU", map);

            // 3. HKLM Services Anahtarları ve Servis Bilgileri
            CaptureServices(map);

            // 4. Uninstall anahtarları (HKLM 64 + WOW6432Node yolu, HKCU)
            CaptureUninstallKeys(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM", map);
            CaptureUninstallKeys(RegistryHive.CurrentUser, RegistryView.Default, "HKCU", map);

            return map;
        }

        /// <summary>
        /// İki snapshot arasındaki farkı çıkarıp yeni eklenen/değişen kayıtları,
        /// servisleri ve başlangıç girdilerini ayrıştırır.
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
                    // Yeni eklenen anahtar veya değer
                    string hive = key.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) ? "HKLM" : "HKCU";
                    bool isService = IsServiceKey(key);
                    bool isRun = IsRunValueKey(key);

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
                    // Değiştirilen değer
                    string hive = key.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) ? "HKLM" : "HKCU";
                    bool isService = IsServiceKey(key);
                    bool isRun = IsRunValueKey(key);

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

        /// <summary>
        /// Anahtar bir Run/RunOnce değeri mi? Tam yol öneki aranır: eskiden "\Run" alt dizesi
        /// "…\Uninstall\RuneLite" gibi kayıtları da başlangıç girdisi sayıyordu (A4).
        /// </summary>
        public static bool IsRunValueKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            foreach (string hive in new[] { "HKLM", "HKCU" })
            {
                foreach (string runPath in RunSubKeyPaths)
                {
                    if (key.StartsWith($@"{hive}\{runPath}\", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        /// <summary>Anahtar bir hizmet kaydı mı (HKLM\SYSTEM\CurrentControlSet\Services\ad)?</summary>
        public static bool IsServiceKey(string? key) =>
            !string.IsNullOrWhiteSpace(key) && key.StartsWith($@"HKLM\{ServicesSubKeyPath}\", StringComparison.OrdinalIgnoreCase);

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
