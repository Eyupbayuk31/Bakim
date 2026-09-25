using System;
using System.Reflection;

namespace Bakım
{
    /// <summary>
    /// Uygulama sürümü: derleme sürümünden okunur, o da Directory.Build.props'taki
    /// BakimVersion'dan gelir. Kodda ve XAML'de sürüm numarası yazılmaz (H-15, H-20).
    /// </summary>
    public static class AppInfo
    {
        public static Version VersionNumber { get; } = ReadVersion();

        /// <summary>"3.21.0"</summary>
        public static string Version => VersionNumber.ToString(3);

        /// <summary>"BAKIM v3.21.0" (kenar çubuğu başlığı)</summary>
        public static string BrandTitle => $"BAKIM v{Version}";

        private static Version ReadVersion()
        {
            var v = typeof(AppInfo).Assembly.GetName().Version;
            return v != null && v.Major > 0 ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(0, 0, 0);
        }
    }
}
