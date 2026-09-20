using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Bakım.Helpers
{
    /// <summary>
    /// Süreç dosyalarından yüksek çözünürlüklü ikon ve yayıncı bilgilerini çıkaran bellek sızıntısız yardımcı sınıf.
    /// </summary>
    public static class ProcessIconHelper
    {
        private static readonly ConcurrentDictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, (string Publisher, string Description)> MetadataCache = new(StringComparer.OrdinalIgnoreCase);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        public static ImageSource? GetProcessIcon(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            if (IconCache.TryGetValue(filePath, out var cached))
                return cached;

            try
            {
                using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
                if (sysIcon != null)
                {
                    using var bitmap = sysIcon.ToBitmap();
                    var hBitmap = bitmap.GetHbitmap();
                    try
                    {
                        var wpfBmp = Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        wpfBmp.Freeze(); // Cross-thread güvenliği
                        IconCache[filePath] = wpfBmp;
                        return wpfBmp;
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
            }
            catch
            {
                // Erişim engeli veya korumalı dosya
            }

            IconCache[filePath] = null;
            return null;
        }

        public static (string Publisher, string Description) GetProcessMetadata(string? filePath, string fallbackName)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return ("Bilinmeyen Yayıncı", fallbackName);

            if (MetadataCache.TryGetValue(filePath, out var cached))
                return cached;

            try
            {
                var vi = FileVersionInfo.GetVersionInfo(filePath);
                string publisher = !string.IsNullOrWhiteSpace(vi.CompanyName)
                    ? vi.CompanyName.Trim()
                    : "Bilinmeyen Yayıncı";

                string desc = !string.IsNullOrWhiteSpace(vi.FileDescription)
                    ? vi.FileDescription.Trim()
                    : (!string.IsNullOrWhiteSpace(vi.ProductName) ? vi.ProductName.Trim() : fallbackName);

                var result = (publisher, desc);
                MetadataCache[filePath] = result;
                return result;
            }
            catch
            {
                return ("Bilinmeyen Yayıncı", fallbackName);
            }
        }

        public static string? TryGetProcessMainModulePath(Process process)
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch
            {
                // 32/64 bit uyumsuzluğu veya erişim izni yok
                return null;
            }
        }
    }
}
