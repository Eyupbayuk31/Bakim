using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Models;
using Bakım.Services;
using Bakım.ViewModels;
using Xunit;

namespace Bakim.Tests
{
    public class DuplicateFinderTests : IDisposable
    {
        private readonly string _testRoot;
        private readonly DuplicateFinderService _service;

        public DuplicateFinderTests()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "Bakim_DuplicateTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRoot);
            _service = new DuplicateFinderService();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testRoot))
                {
                    Directory.Delete(_testRoot, true);
                }
            }
            catch
            {
                // Test temizleme
            }
        }

        [Fact]
        public async Task ScanDuplicatesAsync_FindsMatchingDuplicatesAcrossFolders()
        {
            // Arrange
            byte[] identicalContent = new byte[8192];
            new Random(42).NextBytes(identicalContent);

            string file1 = Path.Combine(_testRoot, "doc_original.txt");
            string file2 = Path.Combine(_testRoot, "doc_copy1.txt");
            string subDir = Path.Combine(_testRoot, "SubFolder");
            Directory.CreateDirectory(subDir);
            string file3 = Path.Combine(subDir, "doc_nested_copy.txt");

            await File.WriteAllBytesAsync(file1, identicalContent);
            await File.WriteAllBytesAsync(file2, identicalContent);
            await File.WriteAllBytesAsync(file3, identicalContent);

            // Farklı içerikli tekil dosya
            byte[] uniqueContent = new byte[8192];
            new Random(99).NextBytes(uniqueContent);
            string uniqueFile = Path.Combine(_testRoot, "unique.txt");
            await File.WriteAllBytesAsync(uniqueFile, uniqueContent);

            var options = new DuplicateScanOptions
            {
                TargetPath = _testRoot,
                FileTypeFilter = "All",
                MinSizeBytes = 10,
                ExcludeSystemDirs = true
            };

            // Act
            var groups = await _service.ScanDuplicatesAsync(options, null, CancellationToken.None);

            // Assert
            Assert.Single(groups);
            var group = groups[0];
            Assert.Equal(3, group.Files.Count);
            Assert.Equal(identicalContent.Length, group.SingleFileSizeBytes);
            Assert.Equal(2 * identicalContent.Length, group.TotalWastedBytes);
            Assert.False(string.IsNullOrWhiteSpace(group.Hash));
        }

        [Fact]
        public async Task ScanDuplicatesAsync_RespectsFileTypeFilter()
        {
            // Arrange
            byte[] imageBytes = new byte[2048];
            new Random(10).NextBytes(imageBytes);

            string img1 = Path.Combine(_testRoot, "photo1.jpg");
            string img2 = Path.Combine(_testRoot, "photo1_copy.jpg");
            await File.WriteAllBytesAsync(img1, imageBytes);
            await File.WriteAllBytesAsync(img2, imageBytes);

            byte[] docBytes = new byte[2048];
            new Random(20).NextBytes(docBytes);
            string doc1 = Path.Combine(_testRoot, "notes1.txt");
            string doc2 = Path.Combine(_testRoot, "notes1_copy.txt");
            await File.WriteAllBytesAsync(doc1, docBytes);
            await File.WriteAllBytesAsync(doc2, docBytes);

            // Sadece resimler için tara
            var imageOptions = new DuplicateScanOptions
            {
                TargetPath = _testRoot,
                FileTypeFilter = "Images",
                MinSizeBytes = 10,
                ExcludeSystemDirs = true
            };

            // Act
            var imageGroups = await _service.ScanDuplicatesAsync(imageOptions, null, CancellationToken.None);

            // Assert
            Assert.Single(imageGroups);
            Assert.All(imageGroups[0].Files, f => Assert.EndsWith(".jpg", f.FilePath, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task ScanEmptyFoldersAsync_DetectsAndDeletesEmptyDirectories()
        {
            // Arrange
            string emptyDir1 = Path.Combine(_testRoot, "EmptyFolder1");
            string emptyDir2 = Path.Combine(_testRoot, "EmptyFolder2");
            string nonEmptyDir = Path.Combine(_testRoot, "NonEmptyFolder");

            Directory.CreateDirectory(emptyDir1);
            Directory.CreateDirectory(emptyDir2);
            Directory.CreateDirectory(nonEmptyDir);
            await File.WriteAllTextAsync(Path.Combine(nonEmptyDir, "data.txt"), "some content");

            // Act: Tara
            var emptyFolders = await _service.ScanEmptyFoldersAsync(_testRoot, null, CancellationToken.None);

            // Assert: Tarama
            Assert.Equal(2, emptyFolders.Count);
            Assert.Contains(emptyFolders, f => f.FolderPath == emptyDir1);
            Assert.Contains(emptyFolders, f => f.FolderPath == emptyDir2);

            // Act: Sil
            int deletedCount = await _service.DeleteEmptyFoldersAsync(emptyFolders);

            // Assert: Silme
            Assert.Equal(2, deletedCount);
            Assert.False(Directory.Exists(emptyDir1));
            Assert.False(Directory.Exists(emptyDir2));
            Assert.True(Directory.Exists(nonEmptyDir));
        }

        [Fact]
        public void ViewModel_SmartSelection_PreservesOldestOriginalFile()
        {
            // Arrange
            var vm = new SystemInfoViewModel(
                new SystemInfoRevampTests.MockSystemInfoService(),
                new SystemInfoRevampTests.MockSettingsService(),
                _service);

            var group = new DuplicateFileGroup
            {
                GroupId = 1,
                Hash = "TESTHASH123",
                SingleFileSizeBytes = 1024,
                FileSizeFormatted = "1 KB"
            };

            var fileOldest = new DuplicateFileItem
            {
                FileName = "original.png",
                FilePath = "C:\\test\\original.png",
                SizeBytes = 1024,
                CreationTime = new DateTime(2025, 1, 1)
            };

            var fileCopy1 = new DuplicateFileItem
            {
                FileName = "copy1.png",
                FilePath = "C:\\test\\copy1.png",
                SizeBytes = 1024,
                CreationTime = new DateTime(2025, 3, 1)
            };

            var fileCopy2 = new DuplicateFileItem
            {
                FileName = "copy2.png",
                FilePath = "C:\\test\\copy2.png",
                SizeBytes = 1024,
                CreationTime = new DateTime(2025, 5, 1)
            };

            group.Files.Add(fileCopy2); // Karışık sırada ekle
            group.Files.Add(fileOldest);
            group.Files.Add(fileCopy1);

            vm.DuplicateGroups.Add(group);

            // Act
            vm.SelectAllDuplicatesExceptOne();

            // Assert
            Assert.True(fileOldest.IsOriginal);
            Assert.False(fileOldest.IsSelected); // Orijinal silinmek üzere seçilmemeli!

            Assert.False(fileCopy1.IsOriginal);
            Assert.True(fileCopy1.IsSelected);   // Kopyalar seçilmeli

            Assert.False(fileCopy2.IsOriginal);
            Assert.True(fileCopy2.IsSelected);   // Kopyalar seçilmeli
        }

        [Theory]
        [InlineData("True", true)]
        [InlineData("False", false)]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void ViewModel_ToggleSelectAllEmptyFolders_HandlesBothStringAndBooleanParameters(object parameter, bool expected)
        {
            // Arrange
            var vm = new SystemInfoViewModel(
                new SystemInfoRevampTests.MockSystemInfoService(),
                new SystemInfoRevampTests.MockSettingsService(),
                _service);

            var folder1 = new EmptyFolderItem { FolderName = "Dir1", FolderPath = "C:\\test\\Dir1", IsSelected = !expected };
            var folder2 = new EmptyFolderItem { FolderName = "Dir2", FolderPath = "C:\\test\\Dir2", IsSelected = !expected };

            vm.FilteredEmptyFolders.Add(folder1);
            vm.FilteredEmptyFolders.Add(folder2);

            // Act - Execute as IRelayCommand / Command to verify binding layer behavior
            vm.ToggleSelectAllEmptyFoldersCommand.Execute(parameter);

            // Assert
            Assert.Equal(expected, folder1.IsSelected);
            Assert.Equal(expected, folder2.IsSelected);
        }
    }
}
