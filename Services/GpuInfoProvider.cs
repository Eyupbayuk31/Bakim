using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Bakım.Services
{
    /// <summary>
    /// Ekran kartı (GPU) model adı ve gerçek 64-bit VRAM bellek miktarını 
    /// WMI 32-bit uint32 taşması (4 GB limiti) olmadan doğrudan DirectX DXGI API
    /// ve Registry 64-bit QWORD üzerinden okuyan donanım sağlayıcısı.
    /// </summary>
    public static class GpuInfoProvider
    {
        // 1. DİREKT DXGI API İLE 64-BIT VRAM OKUMA (TASK MANAGER / DIRECTX STANDARDI)
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
            // IDXGIObject (4)
            [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
            [PreserveSig] int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
            [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);

            // IDXGIFactory (5)
            [PreserveSig] int EnumAdapters(uint Adapter, out IntPtr ppAdapter);
            [PreserveSig] int MakeWindowAssociation(IntPtr WindowHandle, uint Flags);
            [PreserveSig] int GetWindowAssociation(out IntPtr pWindowHandle);
            [PreserveSig] int CreateSwapChain(IntPtr pDevice, IntPtr pDesc, out IntPtr ppSwapChain);
            [PreserveSig] int CreateSoftwareAdapter(IntPtr Module, out IntPtr ppAdapter);

            // IDXGIFactory1 (2)
            [PreserveSig] int EnumAdapters1(uint Adapter, out IntPtr ppAdapter);
            [PreserveSig] int IsCurrent();
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDesc1Delegate(IntPtr thisPtr, out DXGI_ADAPTER_DESC1 pDesc);

        [DllImport("dxgi.dll", EntryPoint = "CreateDXGIFactory1")]
        private static extern int CreateDXGIFactory1([MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IDXGIFactory1 ppFactory);

        /// <summary>
        /// Birincil GPU bilgilerini (Model Adı ve GB cinsinden VRAM) döndürür.
        /// </summary>
        public static (string Name, double VramGB) GetPrimaryGpuDetails()
        {
            try
            {
                // ÖNCELİK 1: DXGI API (En Doğru 64-Bit Yöntem)
                Guid factoryGuid = typeof(IDXGIFactory1).GUID;
                if (CreateDXGIFactory1(factoryGuid, out IDXGIFactory1 factory) == 0 && factory != null)
                {
                    try
                    {
                        uint index = 0;
                        (string Name, double VramGB) candidate = (string.Empty, 0);

                        while (true)
                        {
                            int enumHr = factory.EnumAdapters1(index, out IntPtr pAdapter);
                            if (enumHr != 0 || pAdapter == IntPtr.Zero)
                                break;

                            try
                            {
                                // IDXGIAdapter1 vtable: IUnknown (3) + IDXGIObject (4) + IDXGIAdapter (3) = Slot 10 (GetDesc1)
                                IntPtr vtable = Marshal.ReadIntPtr(pAdapter);
                                IntPtr getDesc1Ptr = Marshal.ReadIntPtr(vtable, 10 * IntPtr.Size);
                                var getDesc1 = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(getDesc1Ptr);

                                if (getDesc1(pAdapter, out DXGI_ADAPTER_DESC1 desc) == 0)
                                {
                                    // Software/Microsoft Basic Render Driver'ları atla (DXGI_ADAPTER_FLAG_SOFTWARE = 2)
                                    if ((desc.Flags & 2) == 0)
                                    {
                                        ulong bytes = desc.DedicatedVideoMemory.ToUInt64();
                                        if (bytes > 0)
                                        {
                                            double vramGb = Math.Round((double)bytes / (1024.0 * 1024.0 * 1024.0), 1);
                                            return (desc.Description.Trim(), vramGb);
                                        }
                                        else if (string.IsNullOrEmpty(candidate.Name) && !string.IsNullOrWhiteSpace(desc.Description))
                                        {
                                            candidate = (desc.Description.Trim(), 0);
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                Marshal.Release(pAdapter);
                            }

                            index++;
                        }

                        if (!string.IsNullOrEmpty(candidate.Name) && candidate.VramGB > 0)
                        {
                            return candidate;
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(factory);
                    }
                }
            }
            catch
            {
                // DXGI P/Invoke hatası durumunda Registry'e düş
            }

            // ÖNCELİK 2: REGISTRY QWORD FALLBACK ({4d36e968-e325-11ce-bfc1-08002be10318})
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

                        var driverDesc = subkey.GetValue("DriverDesc") as string;
                        if (string.IsNullOrWhiteSpace(driverDesc) || driverDesc.Contains("Basic Display", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // 64-bit QWORD HardwareInformation.qwMemorySize tespiti (Öncelikli)
                        var qwSize = subkey.GetValue("HardwareInformation.qwMemorySize");
                        var memSize = subkey.GetValue("HardwareInformation.MemorySize");

                        ulong bytes = 0;
                        if (qwSize != null)
                        {
                            bytes = ParseVramBytes(qwSize);
                        }

                        if (bytes == 0 && memSize != null)
                        {
                            bytes = ParseVramBytes(memSize);
                        }

                        if (bytes > 0)
                        {
                            double vramGb = Math.Round((double)bytes / (1024.0 * 1024.0 * 1024.0), 1);
                            return (driverDesc.Trim(), vramGb);
                        }
                    }
                }
            }
            catch
            {
                // Registry Fallback
            }

            return ("Harici Ekran Kartı", 0);
        }

        /// <summary>
        /// GPU Model Adı, Biçimlendirilmiş VRAM Metni, VRAM GB Değeri ve Sürücü Sürümünü döner.
        /// </summary>
        public static (string Name, string FormattedVram, double VramGB, string DriverVersion) GetCompleteGpuDetails()
        {
            var primary = GetPrimaryGpuDetails();
            string driverVersion = string.Empty;

            // Registry'den Sürücü Sürümünü tamamla
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

                        string? desc = subkey.GetValue("DriverDesc") as string;
                        if (!string.IsNullOrWhiteSpace(desc) && (primary.Name.Contains(desc, StringComparison.OrdinalIgnoreCase) || desc.Contains(primary.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            driverVersion = subkey.GetValue("DriverVersion") as string ?? string.Empty;
                            break;
                        }
                    }
                }
            }
            catch { }

            string formatted = FormatVramGB(primary.VramGB);
            return (primary.Name, formatted, primary.VramGB, driverVersion);
        }

        /// <summary>
        /// VRAM değerini kullanıcı dostu "16 GB VRAM" veya "Paylaşımlı VRAM" formatında döndürür.
        /// </summary>
        public static string FormatVramGB(double vramGb)
        {
            if (vramGb <= 0) return "Paylaşımlı / Standart VRAM";
            int rounded = (int)Math.Round(vramGb);
            if (Math.Abs(vramGb - rounded) < 0.25)
            {
                return $"{rounded} GB VRAM";
            }
            return $"{vramGb:F1} GB VRAM";
        }

        private static ulong ParseVramBytes(object rawVal)
        {
            if (rawVal == null) return 0;
            if (rawVal is byte[] byteArray)
            {
                if (byteArray.Length >= 8) return BitConverter.ToUInt64(byteArray, 0);
                if (byteArray.Length >= 4) return BitConverter.ToUInt32(byteArray, 0);
            }
            if (rawVal is long l) return (ulong)l;
            if (rawVal is int i) return (ulong)(uint)i;
            if (rawVal is ulong ul) return ul;
            if (rawVal is uint ui) return ui;
            return 0;
        }
    }
}
