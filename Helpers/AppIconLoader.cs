using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bakım.Core.Uninstall;

namespace Bakım.Helpers
{
    /// <summary>
    /// Dosyadaki belirli bir ikonu (dizin ya da kaynak kimliğiyle) istenen boyutta okur. <c>Icon.ExtractAssociatedIcon</c>
    /// yalnızca ilk ikonu 32 px verir ve <c>DisplayIcon</c>'daki dizini yok sayar. Sonuçlar dondurulur (iş parçacığı
    /// güvenli) ve yol+dizin+boyutla önbelleğe alınır.
    /// </summary>
    public static class AppIconLoader
    {
        private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint PrivateExtractIcons(string lpszFile, int nIconIndex, int cxIcon, int cyIcon,
            IntPtr[] phicon, uint[] piconid, uint nIcons, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static ImageSource? Load(IconLocation location, int size = 32) =>
            Cache.GetOrAdd($"{location.Path}|{location.Index}|{size}", _ => LoadCore(location, size));

        /// <summary>Adaylardan ilk okunabilen ikon.</summary>
        public static ImageSource? LoadFirst(System.Collections.Generic.IEnumerable<IconLocation> candidates, int size = 32)
        {
            foreach (var candidate in candidates)
                if (Load(candidate, size) is { } image) return image;
            return null;
        }

        private static ImageSource? LoadCore(IconLocation location, int size)
        {
            try
            {
                var handles = new IntPtr[1];
                var ids = new uint[1];
                uint count = PrivateExtractIcons(location.Path, location.Index, size, size, handles, ids, 1, 0);
                if (count == 0 || count == uint.MaxValue || handles[0] == IntPtr.Zero)
                {
                    // Dizin geçersizse ilk ikon denenir (bazı DisplayIcon değerleri eski sürümlerden kalma).
                    if (location.Index != 0) return LoadCore(location with { Index = 0 }, size);
                    return null;
                }
                try
                {
                    var image = Imaging.CreateBitmapSourceFromHIcon(handles[0], Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(size, size));
                    image.Freeze();
                    return image;
                }
                finally
                {
                    DestroyIcon(handles[0]);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or ArgumentException or ExternalException)
            {
                Services.AppLog.Debug($"İkon okunamadı: {location.Path},{location.Index}: {ex.Message}", nameof(AppIconLoader));
                return null;
            }
        }
    }
}
