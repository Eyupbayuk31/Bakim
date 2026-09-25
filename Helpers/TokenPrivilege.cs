using System;
using System.Runtime.InteropServices;

namespace Bakım.Helpers
{
    /// <summary>
    /// Süreç belirtecinde (token) bir ayrıcalığı etkinleştirir.
    ///
    /// Bekleme belleğini boşaltmak (NtSetSystemInformation / MemoryPurgeStandbyList)
    /// SeProfileSingleProcessPrivilege gerektirir. Yöneticilerde bu ayrıcalık VARDIR
    /// ama varsayılan olarak KAPALIDIR; etkinleştirilmeden yapılan çağrı
    /// STATUS_PRIVILEGE_NOT_HELD ile başarısız olur. Eski kod bunu hiç
    /// etkinleştirmiyor ve dönüş değerine bakmadan "100 MB boşaltıldı" diyordu.
    /// </summary>
    public static class TokenPrivilege
    {
        public const string ProfileSingleProcess = "SeProfileSingleProcessPrivilege";
        public const string IncreaseQuota = "SeIncreaseQuotaPrivilege";

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
        private const int ERROR_NOT_ALL_ASSIGNED = 1300;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID Luid;
            public uint Attributes;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValueW(string? systemName, string name, out LUID luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>Ayrıcalık etkinleştirildiyse true (yönetici değilse genellikle false).</summary>
        public static bool TryEnable(string privilege)
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr token))
                return false;

            try
            {
                if (!LookupPrivilegeValueW(null, privilege, out LUID luid)) return false;

                var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
                if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) return false;

                // AdjustTokenPrivileges ayrıcalık yoksa da true döner; asıl sonuç son hatadadır.
                return Marshal.GetLastWin32Error() != ERROR_NOT_ALL_ASSIGNED;
            }
            finally
            {
                CloseHandle(token);
            }
        }
    }
}
