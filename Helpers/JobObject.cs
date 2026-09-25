using System;
using System.Runtime.InteropServices;

namespace Bakım.Helpers
{
    /// <summary>
    /// Bir süreci ve başlattığı tüm çocuk süreçleri tek grupta izlemek için
    /// Windows Job Object sarmalayıcısı.
    ///
    /// Inno Setup ve NSIS kaldırıcıları kendilerini %TEMP%'e kopyalayıp asıl
    /// süreç hemen çıkar. Yalnızca ilk sürecin bitişini beklemek, kaldırma
    /// sürerken "bitti" sanılmasına yol açıyordu. Job içindeki aktif süreç
    /// sayısı 0'a indiğinde kaldırma gerçekten bitmiştir.
    ///
    /// KILL_ON_JOB_CLOSE bilinçli olarak AYARLANMAZ: Bakım kapanırsa kaldırıcı ölmemelidir.
    /// </summary>
    public sealed class JobObject : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, out JOBOBJECT_BASIC_ACCOUNTING_INFORMATION info, int length, IntPtr returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private const int JobObjectBasicAccountingInformation = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
        {
            public long TotalUserTime;
            public long TotalKernelTime;
            public long ThisPeriodTotalUserTime;
            public long ThisPeriodTotalKernelTime;
            public uint TotalPageFaultCount;
            public uint TotalProcesses;
            public uint ActiveProcesses;
            public uint TotalTerminatedProcesses;
        }

        private IntPtr _handle;

        public JobObject()
        {
            _handle = CreateJobObjectW(IntPtr.Zero, null);
            if (_handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        /// <summary>Süreci job'a ekler. Farklı bütünlük seviyesindeki süreçte başarısız olabilir.</summary>
        public bool TryAssign(IntPtr processHandle) => _handle != IntPtr.Zero && AssignProcessToJobObject(_handle, processHandle);

        /// <summary>Job içinde hâlâ çalışan süreç sayısı; okunamazsa null.</summary>
        public int? ActiveProcessCount
        {
            get
            {
                if (_handle == IntPtr.Zero) return null;
                return QueryInformationJobObject(_handle, JobObjectBasicAccountingInformation, out var info,
                        Marshal.SizeOf<JOBOBJECT_BASIC_ACCOUNTING_INFORMATION>(), IntPtr.Zero)
                    ? (int)info.ActiveProcesses
                    : null;
            }
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
