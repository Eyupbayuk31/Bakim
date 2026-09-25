using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Bakım.Helpers
{
    /// <summary>
    /// Süreç bilgisini <see cref="System.Diagnostics.Process.MainModule"/> olmadan okur.
    ///
    /// MainModule, yönetici olarak çalışan ya da farklı mimarideki süreçlerde
    /// "Erişim reddedildi" atar. PROCESS_QUERY_LIMITED_INFORMATION +
    /// QueryFullProcessImageName, korumalı süreçler dışında her durumda çalışır
    /// ve çok daha ucuzdur.
    /// </summary>
    public static class NativeProcess
    {
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private static readonly IntPtr InvalidHandle = new(-1);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder exeName, ref uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32W
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        /// <summary>Sürecin tam yolu; okunamazsa null.</summary>
        public static string? TryGetImagePath(int processId)
        {
            if (processId <= 4) return null;
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString(0, (int)size) : null;
            }
            finally
            {
                CloseHandle(h);
            }
        }

        /// <summary>Sürecin oluşturulma zamanı (UTC); PID yeniden kullanımını ayırt etmek için.</summary>
        public static DateTime? TryGetStartTimeUtc(int processId)
        {
            if (processId <= 4) return null;
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (h == IntPtr.Zero) return null;
            try
            {
                return GetProcessTimes(h, out long creation, out _, out _, out _)
                    ? DateTime.FromFileTimeUtc(creation)
                    : null;
            }
            finally
            {
                CloseHandle(h);
            }
        }

        public readonly record struct ProcessEntry(int ProcessId, int ParentProcessId, string ExeName);

        /// <summary>Tüm süreçlerin (PID, ebeveyn PID, exe adı) listesi. Yetki gerektirmez.</summary>
        public static IReadOnlyList<ProcessEntry> Snapshot()
        {
            var list = new List<ProcessEntry>();
            IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snap == InvalidHandle || snap == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
                if (!Process32FirstW(snap, ref entry)) return list;
                do
                {
                    list.Add(new ProcessEntry((int)entry.th32ProcessID, (int)entry.th32ParentProcessID, entry.szExeFile ?? string.Empty));
                }
                while (Process32NextW(snap, ref entry));
            }
            finally
            {
                CloseHandle(snap);
            }

            return list;
        }
    }
}
