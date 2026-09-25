using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Bakım.Core.Cleaning
{
    /// <summary>
    /// Chromium tabanlı tarayıcıların (Chrome, Edge, Brave, Vivaldi) profil klasörleri (§5.2).
    /// Eskiden yalnızca "Default" profil temizleniyordu; "Profile 1", "Profile 2" … atlanıyordu.
    /// </summary>
    public static class BrowserProfiles
    {
        private static readonly Regex NumberedProfile = new(@"^Profile \d+$", RegexOptions.CultureInvariant);

        /// <summary>Profil başına temizlenen önbellek alt klasörleri. Çerez, geçmiş ve oturumlar ASLA dahil değil.</summary>
        public static readonly IReadOnlyList<string> CacheSubdirectories = new[] { "Cache", "Code Cache", "GPUCache" };

        public static bool IsChromiumProfileDirectory(string name) =>
            string.Equals(name, "Default", StringComparison.Ordinal) ||
            string.Equals(name, "Guest Profile", StringComparison.Ordinal) ||
            NumberedProfile.IsMatch(name);

        /// <summary>User Data altındaki profil adlarından temizlenecek önbellek klasörlerini üretir.</summary>
        public static IReadOnlyList<string> CacheDirectories(string userDataRoot, IEnumerable<string> childDirectoryNames) =>
            childDirectoryNames
                .Where(IsChromiumProfileDirectory)
                .OrderBy(n => n, StringComparer.Ordinal)
                .SelectMany(profile => CacheSubdirectories.Select(sub => Path.Combine(userDataRoot, profile, sub)))
                .ToList();
    }
}
