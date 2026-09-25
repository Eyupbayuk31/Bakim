using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Bakım.Services.Sentinel.Detection
{
    /// <summary>
    /// UAC ile ykseltilmi srelerde dahi ynetici yetkisi gerektirmeden
    /// (PROCESS_QUERY_LIMITED_INFORMATION) grnt yolunu, ebeveyn PID'sini
    /// ve oluturulma zaman damgasn gvenle okuyan P/Invoke yardmcs.
    /// </summary>
    public static class ProcessInfoReader
    {
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint TH32CS_SNAPPROCESS = 0x00000002;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessTimes(
            IntPtr hProcess,
            out long lpCreationTime,
            out long lpExitTime,
            out long lpKernelTime,
            out long lpUserTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PROCESSENTRY32
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

        /// <summary>
        /// Srecin tam dosya yolunu dner. Standart Process.MainModule ulaamadnda
        /// QueryFullProcessImageName ile UAC engellerini aar.
        /// </summary>
        public static string? GetProcessExecutablePath(int processId)
        {
            if (processId <= 4) return null;

            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
                if (handle != IntPtr.Zero)
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (QueryFullProcessImageName(handle, 0, sb, ref size))
                    {
                        return sb.ToString();
                    }
                }
            }
            catch { }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    CloseHandle(handle);
                }
            }

            // Fallback: Standart Process API
            try
            {
                using var proc = Process.GetProcessById(processId);
                return proc.MainModule?.FileName;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Srecin oluturulma zamann (UTC) dner. PID yeniden kullanmn (PID reuse)
        /// engellemek iin kullanlr.
        /// </summary>
        public static DateTime? GetProcessCreationTimeUtc(int processId)
        {
            if (processId <= 4) return null;

            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
                if (handle != IntPtr.Zero)
                {
                    if (GetProcessTimes(handle, out long creationTime, out _, out _, out _))
                    {
                        return DateTime.FromFileTimeUtc(creationTime);
                    }
                }
            }
            catch { }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    CloseHandle(handle);
                }
            }

            try
            {
                using var proc = Process.GetProcessById(processId);
                return proc.StartTime.ToUniversalTime();
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Srecin ebeveyn sre kimliini (Parent PID) ToolHelp32 ile yetki engeli olmadan dner.
        /// </summary>
        public static int? GetParentProcessId(int processId)
        {
            if (processId <= 4) return null;

            IntPtr snapshot = IntPtr.Zero;
            try
            {
                snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
                if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return null;

                var entry = new PROCESSENTRY32();
                entry.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));

                if (Process32First(snapshot, ref entry))
                {
                    do
                    {
                        if (entry.th32ProcessID == processId)
                        {
                            return (int)entry.th32ParentProcessID;
                        }
                    }
                    while (Process32Next(snapshot, ref entry));
                }
            }
            catch { }
            finally
            {
                if (snapshot != IntPtr.Zero && snapshot != new IntPtr(-1))
                {
                    CloseHandle(snapshot);
                }
            }

            return null;
        }

        /// <summary>
        /// Sre hakkndaki tm nemli bilgileri tek seferde gvenle dner.
        /// </summary>
        public static bool TryGetProcessDetails(int processId, out string? path, out int? parentPid, out DateTime? creationTimeUtc)
        {
            path = GetProcessExecutablePath(processId);
            parentPid = GetParentProcessId(processId);
            creationTimeUtc = GetProcessCreationTimeUtc(processId);

            return !string.IsNullOrWhiteSpace(path) || parentPid.HasValue;
        }
    }
}
