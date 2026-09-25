using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Bakım.Core.Sentinel
{
    /// <summary>
    /// NTFS USN (Update Sequence Number) Değişiklik Günlüğü Kaydı (NÖB Faz 8 / Tam Koruma Modu).
    /// </summary>
    public sealed record UsnJournalRecord(
        long Usn,
        ulong FileReferenceNumber,
        ulong ParentFileReferenceNumber,
        string FileName,
        uint Reason,
        DateTime TimestampUtc)
    {
        public bool IsFileCreated => (Reason & UsnJournalConstants.USN_REASON_FILE_CREATE) != 0;
        public bool IsFileDeleted => (Reason & UsnJournalConstants.USN_REASON_FILE_DELETE) != 0;
        public bool IsFileModified => (Reason & (UsnJournalConstants.USN_REASON_DATA_OVERWRITE |
                                                UsnJournalConstants.USN_REASON_DATA_EXTEND |
                                                UsnJournalConstants.USN_REASON_DATA_TRUNCATION)) != 0;
        public bool IsRenamed => (Reason & (UsnJournalConstants.USN_REASON_RENAME_NEW_NAME |
                                            UsnJournalConstants.USN_REASON_RENAME_OLD_NAME)) != 0;
    }

    public static class UsnJournalConstants
    {
        public const uint FSCTL_QUERY_USN_JOURNAL = 0x000900f4;
        public const uint FSCTL_READ_USN_JOURNAL = 0x000900bb;

        public const uint USN_REASON_DATA_OVERWRITE = 0x00000001;
        public const uint USN_REASON_DATA_EXTEND = 0x00000002;
        public const uint USN_REASON_DATA_TRUNCATION = 0x00000004;
        public const uint USN_REASON_FILE_CREATE = 0x00000100;
        public const uint USN_REASON_FILE_DELETE = 0x00000200;
        public const uint USN_REASON_RENAME_OLD_NAME = 0x00001000;
        public const uint USN_REASON_RENAME_NEW_NAME = 0x00002000;
        public const uint USN_REASON_SECURITY_CHANGE = 0x00010000;
        public const uint USN_REASON_CLOSE = 0x80000000;

        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct USN_JOURNAL_DATA_V0
    {
        public ulong UsnJournalID;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct READ_USN_JOURNAL_DATA_V0
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalID;
    }

    /// <summary>
    /// USN kayıt tamponunu ayrıştıran saf ayrıştırıcı mantığı (Linux uyumlu, birim test edilebilir).
    /// </summary>
    public static class UsnRecordParser
    {
        public static List<UsnJournalRecord> ParseBuffer(byte[] buffer, int bytesRead)
        {
            var records = new List<UsnJournalRecord>();
            if (buffer == null || bytesRead < 8) return records;

            // İlk 8 bayt sonraki USN başlangıç değerini (long) içerir
            int offset = sizeof(long);

            while (offset + 60 <= bytesRead)
            {
                uint recordLength = BitConverter.ToUInt32(buffer, offset);
                if (recordLength < 60 || offset + recordLength > bytesRead)
                {
                    break;
                }

                ushort majorVersion = BitConverter.ToUInt16(buffer, offset + 4);
                if (majorVersion == 2 || majorVersion == 3)
                {
                    ulong fileRef = BitConverter.ToUInt64(buffer, offset + 8);
                    ulong parentRef = BitConverter.ToUInt64(buffer, offset + 16);
                    long usn = BitConverter.ToInt64(buffer, offset + 24);
                    long fileTime = BitConverter.ToInt64(buffer, offset + 32);
                    uint reason = BitConverter.ToUInt32(buffer, offset + 40);

                    ushort fileNameLength = BitConverter.ToUInt16(buffer, offset + 56);
                    ushort fileNameOffset = BitConverter.ToUInt16(buffer, offset + 58);

                    string fileName = string.Empty;
                    if (fileNameLength > 0 && offset + fileNameOffset + fileNameLength <= bytesRead)
                    {
                        fileName = Encoding.Unicode.GetString(buffer, offset + fileNameOffset, fileNameLength);
                    }

                    DateTime timeUtc = DateTime.FromFileTimeUtc(fileTime > 0 ? fileTime : DateTime.UtcNow.ToFileTimeUtc());
                    records.Add(new UsnJournalRecord(usn, fileRef, parentRef, fileName, reason, timeUtc));
                }

                offset += (int)recordLength;
            }

            return records;
        }
    }

    /// <summary>
    /// NTFS USN Change Journal sensörü. Yönetici yetkisi olduğunda doğrudan hacim günlüğünden
    /// derin dosya ve dizin hareketlerini okur (Tam Koruma Modu).
    /// </summary>
    public sealed class UsnJournalSensor : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            ref READ_USN_JOURNAL_DATA_V0 lpInBuffer,
            int nInBufferSize,
            byte[] lpOutBuffer,
            int nOutBufferSize,
            out int lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            int nInBufferSize,
            out USN_JOURNAL_DATA_V0 lpOutBuffer,
            int nOutBufferSize,
            out int lpBytesReturned,
            IntPtr lpOverlapped);

        private readonly Dictionary<char, SafeFileHandle> _volumeHandles = new();
        private readonly Dictionary<char, USN_JOURNAL_DATA_V0> _journalMetadata = new();
        private readonly Dictionary<char, long> _lastUsnPerDrive = new();

        public bool IsSupportedOnPlatform => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public bool InitializeDrive(char driveLetter)
        {
            if (!IsSupportedOnPlatform) return false;

            char drive = char.ToUpperInvariant(driveLetter);
            if (_volumeHandles.ContainsKey(drive)) return true;

            try
            {
                string volumePath = $@"\\.\{drive}:";
                var handle = CreateFile(
                    volumePath,
                    UsnJournalConstants.GENERIC_READ | UsnJournalConstants.GENERIC_WRITE,
                    UsnJournalConstants.FILE_SHARE_READ | UsnJournalConstants.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    UsnJournalConstants.OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (handle.IsInvalid)
                {
                    handle.Dispose();
                    return false;
                }

                if (DeviceIoControl(
                    handle,
                    UsnJournalConstants.FSCTL_QUERY_USN_JOURNAL,
                    IntPtr.Zero,
                    0,
                    out USN_JOURNAL_DATA_V0 journalData,
                    Marshal.SizeOf<USN_JOURNAL_DATA_V0>(),
                    out _,
                    IntPtr.Zero))
                {
                    _volumeHandles[drive] = handle;
                    _journalMetadata[drive] = journalData;
                    _lastUsnPerDrive[drive] = journalData.NextUsn;
                    return true;
                }

                handle.Dispose();
                return false;
            }
            catch
            {
                return false;
            }
        }

        public List<UsnJournalRecord> PollChanges(char driveLetter, int maxBytesToRead = 64 * 1024)
        {
            var results = new List<UsnJournalRecord>();
            if (!IsSupportedOnPlatform) return results;

            char drive = char.ToUpperInvariant(driveLetter);
            if (!_volumeHandles.TryGetValue(drive, out var handle) ||
                !_journalMetadata.TryGetValue(drive, out var journalData) ||
                !_lastUsnPerDrive.TryGetValue(drive, out var startUsn))
            {
                return results;
            }

            try
            {
                var readData = new READ_USN_JOURNAL_DATA_V0
                {
                    StartUsn = startUsn,
                    ReasonMask = 0xFFFFFFFF, // Tüm nedenler
                    ReturnOnlyOnClose = 0,
                    Timeout = 0,
                    BytesToWaitFor = 0,
                    UsnJournalID = journalData.UsnJournalID
                };

                byte[] outBuffer = new byte[maxBytesToRead];
                if (DeviceIoControl(
                    handle,
                    UsnJournalConstants.FSCTL_READ_USN_JOURNAL,
                    ref readData,
                    Marshal.SizeOf<READ_USN_JOURNAL_DATA_V0>(),
                    outBuffer,
                    outBuffer.Length,
                    out int bytesReturned,
                    IntPtr.Zero) && bytesReturned > sizeof(long))
                {
                    long nextUsn = BitConverter.ToInt64(outBuffer, 0);
                    _lastUsnPerDrive[drive] = nextUsn;

                    results = UsnRecordParser.ParseBuffer(outBuffer, bytesReturned);
                }
            }
            catch
            {
                // Sessiz koruma
            }

            return results;
        }

        public void Dispose()
        {
            foreach (var kvp in _volumeHandles)
            {
                try
                {
                    kvp.Value?.Dispose();
                }
                catch
                {
                    // Dispose sessiz koruma
                }
            }
            _volumeHandles.Clear();
            _journalMetadata.Clear();
            _lastUsnPerDrive.Clear();
        }
    }
}
