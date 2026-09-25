using System;
using System.IO;
using System.Text;
using Bakım.Core.Sentinel;
using Xunit;

namespace Bakim.Core.Tests
{
    public class UsnJournalSensorTests
    {
        [Fact]
        public void ParseBuffer_WithNullOrSmallBuffer_ReturnsEmpty()
        {
            Assert.Empty(UsnRecordParser.ParseBuffer(null!, 0));
            Assert.Empty(UsnRecordParser.ParseBuffer(new byte[4], 4));
        }

        [Fact]
        public void ParseBuffer_WithSyntheticRecord_ParsesFileCreationCorrectly()
        {
            // İlk 8 bayt nextUsn
            long nextUsn = 1000L;
            string fileName = "malicious_payload.exe";
            byte[] fileNameBytes = Encoding.Unicode.GetBytes(fileName);

            ushort fileNameLength = (ushort)fileNameBytes.Length;
            ushort fileNameOffset = 60; // Başlık sonrası
            uint recordLength = (uint)(fileNameOffset + fileNameLength);

            byte[] buffer = new byte[8 + recordLength];
            Array.Copy(BitConverter.GetBytes(nextUsn), 0, buffer, 0, 8);

            int recStart = 8;
            Array.Copy(BitConverter.GetBytes(recordLength), 0, buffer, recStart, 4);
            Array.Copy(BitConverter.GetBytes((ushort)2), 0, buffer, recStart + 4, 2); // Major 2
            Array.Copy(BitConverter.GetBytes((ushort)0), 0, buffer, recStart + 6, 2); // Minor 0
            Array.Copy(BitConverter.GetBytes((ulong)123456), 0, buffer, recStart + 8, 8); // FileRef
            Array.Copy(BitConverter.GetBytes((ulong)999999), 0, buffer, recStart + 16, 8); // ParentRef
            Array.Copy(BitConverter.GetBytes(1001L), 0, buffer, recStart + 24, 8); // Usn
            Array.Copy(BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()), 0, buffer, recStart + 32, 8);
            Array.Copy(BitConverter.GetBytes(UsnJournalConstants.USN_REASON_FILE_CREATE), 0, buffer, recStart + 40, 4);
            Array.Copy(BitConverter.GetBytes(fileNameLength), 0, buffer, recStart + 56, 2);
            Array.Copy(BitConverter.GetBytes(fileNameOffset), 0, buffer, recStart + 58, 2);
            Array.Copy(fileNameBytes, 0, buffer, recStart + fileNameOffset, fileNameLength);

            var records = UsnRecordParser.ParseBuffer(buffer, buffer.Length);
            Assert.Single(records);

            var rec = records[0];
            Assert.Equal("malicious_payload.exe", rec.FileName);
            Assert.Equal(1001L, rec.Usn);
            Assert.Equal((ulong)123456, rec.FileReferenceNumber);
            Assert.Equal((ulong)999999, rec.ParentFileReferenceNumber);
            Assert.True(rec.IsFileCreated);
            Assert.False(rec.IsFileDeleted);
            Assert.False(rec.IsFileModified);
        }

        [Fact]
        public void ParseBuffer_WithFileModificationAndDeletion_FlagsCorrectly()
        {
            string fileName = "config.ini";
            byte[] nameBytes = Encoding.Unicode.GetBytes(fileName);
            ushort nameLen = (ushort)nameBytes.Length;
            ushort nameOff = 60;
            uint len = (uint)(nameOff + nameLen);

            byte[] buffer = new byte[8 + len];
            Array.Copy(BitConverter.GetBytes(500L), 0, buffer, 0, 8);

            int recStart = 8;
            Array.Copy(BitConverter.GetBytes(len), 0, buffer, recStart, 4);
            Array.Copy(BitConverter.GetBytes((ushort)2), 0, buffer, recStart + 4, 2);
            Array.Copy(BitConverter.GetBytes(UsnJournalConstants.USN_REASON_DATA_OVERWRITE | UsnJournalConstants.USN_REASON_FILE_DELETE), 0, buffer, recStart + 40, 4);
            Array.Copy(BitConverter.GetBytes(nameLen), 0, buffer, recStart + 56, 2);
            Array.Copy(BitConverter.GetBytes(nameOff), 0, buffer, recStart + 58, 2);
            Array.Copy(nameBytes, 0, buffer, recStart + nameOff, nameLen);

            var records = UsnRecordParser.ParseBuffer(buffer, buffer.Length);
            Assert.Single(records);
            Assert.True(records[0].IsFileModified);
            Assert.True(records[0].IsFileDeleted);
            Assert.False(records[0].IsFileCreated);
        }
    }
}
