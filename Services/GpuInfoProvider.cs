using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Bakım.Services
{
    /// <summary>
    /// Ekran kartı (GPU) model bilgileri, VRAM kapasitesi ve sürücü detaylarını temsil eden model.
    /// </summary>
    public class GpuDeviceInfo
    {
        public string Name { get; set; } = string.Empty;
        public ulong VramBytes { get; set; }
        public double VramGb { get; set; }
        public string FormattedVram { get; set; } = string.Empty;
        public string DriverVersion { get; set; } = string.Empty;
        public bool IsDiscrete { get; set; }
        public string Vendor { get; set; } = "Bilinmeyen";
        public uint VendorId { get; set; }
    }

    /// <summary>
    /// Sistem genelindeki birincil ve ikincil (dahili/harici) tüm GPU yapılandırması.
    /// </summary>
    public class GpuSystemConfiguration
    {
        public GpuDeviceInfo PrimaryGpu { get; set; } = new();
        public GpuDeviceInfo? SecondaryGpu { get; set; }
        public bool HasDualGpu => SecondaryGpu != null;
        public string GpuBadgeText => HasDualGpu ? "Harici GPU" : (PrimaryGpu.IsDiscrete ? "Harici GPU" : "Dahili GPU");
        public string DetailedTooltip { get; set; } = string.Empty;
        public List<GpuDeviceInfo> AllGpus { get; set; } = new();
    }

    /// <summary>
    /// Ekran kartı (GPU) model adı, 64-bit VRAM bellek miktarı ve çift GPU (Optimus / Enduro)
    /// konfigürasyonunu WMI 32-bit uint32 taşması (4 GB limiti) ve laptop adaptör öncelik
    /// hataları olmaksızın doğrudan DirectX DXGI API, Registry 64-bit QWORD ve WMI üzerinden
    /// çözümleyen birleşik donanım sağlayıcısı.
    /// </summary>
    public static class GpuInfoProvider
    {
        #region DXGI API & COM Tanımlamaları

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DXGI_ADAPTER_DESC1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public UIntPtr DedicatedVideoMemory; // 64-bit SIZE_T (True VRAM)
            public UIntPtr DedicatedSystemMemory;
            public UIntPtr SharedSystemMemory;
            public LUID AdapterLuid;
            public uint Flags; // DXGI_ADAPTER_FLAG (0 = None, 1 = Remote, 2 = Software)
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIFactory1
        {
            [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
            [PreserveSig] int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
            [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);

            [PreserveSig] int EnumAdapters(uint Adapter, out IntPtr ppAdapter);
            [PreserveSig] int MakeWindowAssociation(IntPtr WindowHandle, uint Flags);
            [PreserveSig] int GetWindowAssociation(out IntPtr pWindowHandle);
            [PreserveSig] int CreateSwapChain(IntPtr pDevice, IntPtr pDesc, out IntPtr ppSwapChain);
            [PreserveSig] int CreateSoftwareAdapter(IntPtr Module, out IntPtr ppAdapter);

            [PreserveSig] int EnumAdapters1(uint Adapter, out IntPtr ppAdapter);
            [PreserveSig] int IsCurrent();
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDesc1Delegate(IntPtr thisPtr, out DXGI_ADAPTER_DESC1 pDesc);

        [DllImport("dxgi.dll", EntryPoint = "CreateDXGIFactory1")]
        private static extern int CreateDXGIFactory1([MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IDXGIFactory1 ppFactory);

        #endregion

        private static readonly object _syncLock = new();
        private static GpuSystemConfiguration? _cachedConfig;
        private static DateTime _lastEvaluated = DateTime.MinValue;

        /// <summary>
        /// Sistemdeki tüm GPU adaptörlerini tarar, puanlar ve birincil/ikincil konfigürasyonu döner.
        /// </summary>
        public static GpuSystemConfiguration GetGpuConfiguration(bool forceRefresh = false)
        {
            lock (_syncLock)
            {
                if (_cachedConfig != null && !forceRefresh && (DateTime.Now - _lastEvaluated).TotalSeconds < 300)
                {
                    return _cachedConfig;
                }

                var discovered = new List<GpuDeviceInfo>();

                // 1. DXGI 1.1 ile Tüm Adaptörleri Numaralandır
                EnumerateDxgiAdapters(discovered);

                // 2. Registry Display Class ile Zenginleştir ({4d36e968-e325-11ce-bfc1-08002be10318})
                EnumerateRegistryAdapters(discovered);

                // 3. WMI Win32_VideoController ile Eksikleri Tamamla
                EnumerateWmiAdapters(discovered);

                // 4. Temizle & Filtrele (Yazılımsal ve Temel Görüntü Adaptörlerini Ayıkla)
                var validAdapters = discovered
                    .Where(a => !string.IsNullOrWhiteSpace(a.Name))
                    .Where(a => !a.Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase))
                    .Where(a => !a.Name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase))
                    .Where(a => !a.Name.Contains("Microsoft Remote", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(a => a.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => MergeGpuGroup(g))
                    .ToList();

                if (validAdapters.Count == 0)
                {
                    var fallback = new GpuDeviceInfo
                    {
                        Name = "Standart Grafik Bağdaştırıcısı",
                        VramBytes = 0,
                        VramGb = 0,
                        FormattedVram = "Paylaşımlı VRAM",
                        DriverVersion = "Güncel",
                        IsDiscrete = false
                    };
                    _cachedConfig = new GpuSystemConfiguration
                    {
                        PrimaryGpu = fallback,
                        DetailedTooltip = fallback.Name,
                        AllGpus = new List<GpuDeviceInfo> { fallback }
                    };
                    _lastEvaluated = DateTime.Now;
                    return _cachedConfig;
                }

                // 5. Her Kartı Sınıflandır (Discrete vs Integrated) ve Puanla
                foreach (var gpu in validAdapters)
                {
                    gpu.IsDiscrete = DetermineIfDiscrete(gpu);
                    gpu.VramGb = Math.Round((double)gpu.VramBytes / (1024.0 * 1024.0 * 1024.0), 1);
                    gpu.FormattedVram = FormatVramGB(gpu.VramGb, gpu.IsDiscrete);
                }

                // Öncelik Sıralaması:
                // 1. Harici GPU (VRAM >= 1 GB) -> En Yüksek Skor
                // 2. Harici GPU (VRAM > 0)
                // 3. Harici GPU (Genel)
                // 4. Dahili GPU (Yüksek VRAM)
                // 5. Dahili GPU (Paylaşımlı)
                var sortedGpus = validAdapters
                    .OrderByDescending(gpu => CalculateGpuScore(gpu))
                    .ToList();

                var primary = sortedGpus.First();
                GpuDeviceInfo? secondary = null;

                if (sortedGpus.Count > 1)
                {
                    // Eğer birincil harici ise, en iyi dahili kartı ikincil yap
                    if (primary.IsDiscrete)
                    {
                        secondary = sortedGpus.FirstOrDefault(g => !g.IsDiscrete && g != primary)
                                    ?? sortedGpus.FirstOrDefault(g => g != primary);
                    }
                    else
                    {
                        // Birincil dahili ise başka kart varsa ikincil ata
                        secondary = sortedGpus.FirstOrDefault(g => g != primary);
                    }
                }

                // Zengin Tooltip Oluştur
                var sbTooltip = new StringBuilder();
                if (secondary != null)
                {
                    sbTooltip.AppendLine("Çift GPU Donanım Konfigürasyonu:");
                    sbTooltip.AppendLine($"• Harici GPU: {primary.Name} ({primary.FormattedVram}, Sürücü: {primary.DriverVersion})");
                    sbTooltip.AppendLine($"• Dahili GPU: {secondary.Name} ({secondary.FormattedVram}, Sürücü: {secondary.DriverVersion})");
                    sbTooltip.Append("Sistem yüksek grafik yükünde otomatik harici karta geçiş yapar.");
                }
                else
                {
                    sbTooltip.AppendLine($"Grafik Kartı: {primary.Name}");
                    sbTooltip.AppendLine($"Bellek: {primary.FormattedVram}");
                    if (!string.IsNullOrWhiteSpace(primary.DriverVersion))
                    {
                        sbTooltip.AppendLine($"Sürücü Sürümü: {primary.DriverVersion}");
                    }
                    sbTooltip.Append(primary.IsDiscrete ? "Tür: Harici (Dedicated) Grafik İşlemcisi" : "Tür: Dahili (Entegre) Grafik İşlemcisi");
                }

                _cachedConfig = new GpuSystemConfiguration
                {
                    PrimaryGpu = primary,
                    SecondaryGpu = secondary,
                    DetailedTooltip = sbTooltip.ToString(),
                    AllGpus = sortedGpus
                };

                _lastEvaluated = DateTime.Now;
                return _cachedConfig;
            }
        }

        private static int CalculateGpuScore(GpuDeviceInfo gpu)
        {
            int score = 0;
            if (gpu.IsDiscrete)
            {
                score += 1000;
                if (gpu.VramBytes >= 1024UL * 1024UL * 1024UL)
                {
                    score += 500;
                }
            }
            else
            {
                score += 100;
            }

            // VRAM büyüklüğüne göre kademeli puan
            score += (int)Math.Min(gpu.VramBytes / (100UL * 1024UL * 1024UL), 400);

            return score;
        }

        private static bool DetermineIfDiscrete(GpuDeviceInfo gpu)
        {
            string name = gpu.Name.ToLowerInvariant();

            // Kesin Harici (Discrete) İmzaları
            string[] discreteKeywords = new[]
            {
                "radeon (tm) r", "radeon r", "radeon rx", "geforce", "rtx", "gtx", "quadro",
                "titan", "tesla", "arc a", "arc pro", "firepro", "radeon pro"
            };

            foreach (var kw in discreteKeywords)
            {
                if (name.Contains(kw)) return true;
            }

            // Kesin Dahili (Integrated) İmzaları
            string[] integratedKeywords = new[]
            {
                "hd graphics", "uhd graphics", "iris", "vega", "radeon(tm) graphics",
                "intel graphics", "integrated"
            };

            foreach (var kw in integratedKeywords)
            {
                if (name.Contains(kw)) return false;
            }

            // Satıcı Kimliğine Göre (NVIDIA veya AMD harici çipleri)
            if (gpu.VendorId == 0x10DE) return true; // NVIDIA
            if (gpu.VendorId == 0x1002 && gpu.VramBytes >= 512UL * 1024UL * 1024UL) return true; // AMD Discrete

            // Bellek Kapasitesi Eşiği (1 GB üzeri ayrılmış bellek genelde harici karttır)
            if (gpu.VramBytes >= 1024UL * 1024UL * 1024UL) return true;

            return false;
        }

        private static GpuDeviceInfo MergeGpuGroup(IEnumerable<GpuDeviceInfo> group)
        {
            var best = new GpuDeviceInfo();
            foreach (var item in group)
            {
                if (string.IsNullOrEmpty(best.Name)) best.Name = item.Name;
                if (item.VramBytes > best.VramBytes) best.VramBytes = item.VramBytes;
                if (!string.IsNullOrEmpty(item.DriverVersion) && (string.IsNullOrEmpty(best.DriverVersion) || best.DriverVersion == "Güncel"))
                {
                    best.DriverVersion = item.DriverVersion;
                }
                if (item.VendorId != 0 && best.VendorId == 0) best.VendorId = item.VendorId;
                if (item.Vendor != "Bilinmeyen" && best.Vendor == "Bilinmeyen") best.Vendor = item.Vendor;
            }
            return best;
        }

        private static void EnumerateDxgiAdapters(List<GpuDeviceInfo> list)
        {
            try
            {
                Guid factoryGuid = typeof(IDXGIFactory1).GUID;
                if (CreateDXGIFactory1(factoryGuid, out IDXGIFactory1 factory) == 0 && factory != null)
                {
                    try
                    {
                        uint index = 0;
                        while (true)
                        {
                            int enumHr = factory.EnumAdapters1(index, out IntPtr pAdapter);
                            if (enumHr != 0 || pAdapter == IntPtr.Zero)
                                break;

                            try
                            {
                                IntPtr vtable = Marshal.ReadIntPtr(pAdapter);
                                IntPtr getDesc1Ptr = Marshal.ReadIntPtr(vtable, 10 * IntPtr.Size);
                                var getDesc1 = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(getDesc1Ptr);

                                if (getDesc1(pAdapter, out DXGI_ADAPTER_DESC1 desc) == 0)
                                {
                                    // Software adapter flag (2) atla
                                    if ((desc.Flags & 2) == 0 && !string.IsNullOrWhiteSpace(desc.Description))
                                    {
                                        ulong bytes = desc.DedicatedVideoMemory.ToUInt64();
                                        list.Add(new GpuDeviceInfo
                                        {
                                            Name = desc.Description.Trim(),
                                            VramBytes = bytes,
                                            VendorId = desc.VendorId,
                                            Vendor = ResolveVendorName(desc.VendorId, desc.Description)
                                        });
                                    }
                                }
                            }
                            finally
                            {
                                Marshal.Release(pAdapter);
                            }

                            index++;
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(factory);
                    }
                }
            }
            catch { }
        }

        private static void EnumerateRegistryAdapters(List<GpuDeviceInfo> list)
        {
            try
            {
                const string regPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
                using var key = Registry.LocalMachine.OpenSubKey(regPath);
                if (key != null)
                {
                    foreach (var subkeyName in key.GetSubKeyNames())
                    {
                        using var subkey = key.OpenSubKey(subkeyName);
                        if (subkey == null) continue;

                        string? desc = subkey.GetValue("DriverDesc") as string 
                                       ?? subkey.GetValue("Device Description") as string;
                        if (string.IsNullOrWhiteSpace(desc)) continue;

                        string driverVer = subkey.GetValue("DriverVersion") as string ?? string.Empty;
                        ulong bytes = ExtractVramFromRegistryKey(subkey);

                        // Var olan DXGI girdisini güncelle veya yeni ekle
                        var existing = list.FirstOrDefault(a => a.Name.Equals(desc.Trim(), StringComparison.OrdinalIgnoreCase)
                                                                || desc.Contains(a.Name, StringComparison.OrdinalIgnoreCase)
                                                                || a.Name.Contains(desc, StringComparison.OrdinalIgnoreCase));
                        if (existing != null)
                        {
                            if (bytes > existing.VramBytes) existing.VramBytes = bytes;
                            if (!string.IsNullOrEmpty(driverVer) && string.IsNullOrEmpty(existing.DriverVersion))
                            {
                                existing.DriverVersion = driverVer;
                            }
                        }
                        else
                        {
                            list.Add(new GpuDeviceInfo
                            {
                                Name = desc.Trim(),
                                VramBytes = bytes,
                                DriverVersion = driverVer,
                                Vendor = ResolveVendorName(0, desc)
                            });
                        }
                    }
                }
            }
            catch { }
        }

        private static void EnumerateWmiAdapters(List<GpuDeviceInfo> list)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM, DriverVersion, PNPDeviceID FROM Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                    string? name = obj["Name"]?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    string driver = obj["DriverVersion"]?.ToString()?.Trim() ?? string.Empty;
                    ulong vramBytes = 0;
                    var ramObj = obj["AdapterRAM"];
                    if (ramObj != null)
                    {
                        try { vramBytes = Convert.ToUInt64(ramObj); } catch { }
                    }

                    var existing = list.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                                                            || name.Contains(a.Name, StringComparison.OrdinalIgnoreCase)
                                                            || a.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        if (vramBytes > existing.VramBytes) existing.VramBytes = vramBytes;
                        if (!string.IsNullOrEmpty(driver) && string.IsNullOrEmpty(existing.DriverVersion))
                        {
                            existing.DriverVersion = driver;
                        }
                    }
                    else
                    {
                        list.Add(new GpuDeviceInfo
                        {
                            Name = name,
                            VramBytes = vramBytes,
                            DriverVersion = driver,
                            Vendor = ResolveVendorName(0, name)
                        });
                    }
                }
            }
            catch { }
        }

        private static ulong ExtractVramFromRegistryKey(RegistryKey key)
        {
            try
            {
                // 1. HardwareInformation.qwMemorySize (64-bit QWORD - true capacity for >4GB VRAM)
                object? qwObj = key.GetValue("HardwareInformation.qwMemorySize");
                if (qwObj != null)
                {
                    ulong parsed = ParseVramObject(qwObj);
                    if (parsed > 0) return parsed;
                }

                // 2. HardwareInformation.MemorySize (Binary or DWORD fallback)
                object? memObj = key.GetValue("HardwareInformation.MemorySize");
                if (memObj != null)
                {
                    ulong parsed = ParseVramObject(memObj);
                    if (parsed > 0) return parsed;
                }
            }
            catch { }
            return 0;
        }

        private static ulong ParseVramObject(object val)
        {
            try
            {
                if (val is byte[] bytes)
                {
                    if (bytes.Length >= 8) return BitConverter.ToUInt64(bytes, 0);
                    if (bytes.Length >= 4) return BitConverter.ToUInt32(bytes, 0);
                }
                else if (val is long l)
                {
                    if (l > 0) return (ulong)l;
                }
                else if (val is int i)
                {
                    return unchecked((uint)i);
                }
                else if (val is ulong ul)
                {
                    return ul;
                }
                else if (val is uint ui)
                {
                    return ui;
                }
                else
                {
                    return Convert.ToUInt64(val);
                }
            }
            catch { }
            return 0;
        }

        private static string ResolveVendorName(uint vendorId, string name)
        {
            if (vendorId == 0x10DE) return "NVIDIA";
            if (vendorId == 0x1002) return "AMD";
            if (vendorId == 0x8086) return "Intel";

            string lower = name.ToLowerInvariant();
            if (lower.Contains("nvidia") || lower.Contains("geforce") || lower.Contains("rtx") || lower.Contains("gtx")) return "NVIDIA";
            if (lower.Contains("amd") || lower.Contains("radeon")) return "AMD";
            if (lower.Contains("intel") || lower.Contains("arc") || lower.Contains("iris")) return "Intel";

            return "Bilinmeyen";
        }

        #region Geriye Uyumluluk API'leri (Backwards Compatibility)

        /// <summary>
        /// Birincil GPU bilgilerini (Model Adı ve GB cinsinden VRAM) döndürür.
        /// </summary>
        public static (string Name, double VramGB) GetPrimaryGpuDetails()
        {
            var config = GetGpuConfiguration();
            return (config.PrimaryGpu.Name, config.PrimaryGpu.VramGb);
        }

        /// <summary>
        /// GPU Model Adı, Biçimlendirilmiş VRAM Metni, VRAM GB Değeri ve Sürücü Sürümünü döner.
        /// </summary>
        public static (string Name, string FormattedVram, double VramGB, string DriverVersion) GetCompleteGpuDetails()
        {
            var config = GetGpuConfiguration();
            return (config.PrimaryGpu.Name, config.PrimaryGpu.FormattedVram, config.PrimaryGpu.VramGb, config.PrimaryGpu.DriverVersion);
        }

        /// <summary>
        /// VRAM değerini kullanıcı dostu formatta döndürür. ASLA "0 GB VRAM" üretmez.
        /// </summary>
        public static string FormatVramGB(double vramGb, bool isDiscrete = true)
        {
            if (vramGb <= 0.05)
            {
                return isDiscrete ? "Harici Bellek" : "Paylaşımlı VRAM";
            }

            // 1 GB altı küçük bellekler (örn: 128 MB DVMT / 512 MB)
            if (vramGb < 0.8)
            {
                if (!isDiscrete)
                {
                    return "Paylaşımlı VRAM";
                }
                ulong mb = (ulong)Math.Round(vramGb * 1024.0);
                return $"{mb} MB VRAM";
            }

            int rounded = (int)Math.Round(vramGb);
            if (Math.Abs(vramGb - rounded) < 0.25)
            {
                return $"{rounded} GB VRAM";
            }

            return $"{vramGb.ToString("F1", CultureInfo.InvariantCulture)} GB VRAM";
        }

        #endregion
    }
}
