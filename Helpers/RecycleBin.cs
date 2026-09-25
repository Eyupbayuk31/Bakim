using System;
using System.Runtime.InteropServices;

namespace Bakım.Helpers
{
    /// <summary>
    /// Dosya ve klasörleri Windows Geri Dönüşüm Kutusu'na taşır.
    /// (Önceden DuplicateFinderService ve SystemInfoService içinde iki ayrı kopyası vardı.)
    /// </summary>
    public static class RecycleBin
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCTW
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string? pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string? lpszProgressTitle;
        }

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOERRORUI = 0x0400;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
        private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW op);

        /// <summary>
        /// Yolu Geri Dönüşüm Kutusu'na gönderir. Başarılıysa 0, değilse Windows hata kodu döner.
        /// Not: Geri Dönüşüm Kutusu'nun kotası aşılırsa Windows öğeyi kalıcı silebilir;
        /// çağıran taraf büyük işlemlerde kullanıcıyı uyarmalıdır.
        /// </summary>
        public static int Send(string path)
        {
            var op = new SHFILEOPSTRUCTW
            {
                wFunc = FO_DELETE,
                pFrom = path + "\0\0",
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
            };

            int result = SHFileOperationW(ref op);
            if (result == 0 && op.fAnyOperationsAborted) return 1223; // ERROR_CANCELLED
            return result;
        }

        /// <summary>Kolaylık: başarılıysa true.</summary>
        public static bool TrySend(string path) => Send(path) == 0;
    }
}
