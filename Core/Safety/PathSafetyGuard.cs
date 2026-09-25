using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Bakım.Core.Safety
{
    public enum PathVerdict
    {
        Allowed,
        /// <summary>Yolun kendisi silinemez (altındakiler silinebilir). Ör. Program Files, İndirilenler.</summary>
        ProtectedExact,
        /// <summary>Yol ve altındaki her şey korunur. Ör. Windows, WindowsApps.</summary>
        ProtectedTree,
        /// <summary>Sürücü köküne çok yakın (ör. "D:\Oyunlar").</summary>
        TooShallow,
        /// <summary>Bilinen güvenli köklerin dışında ve açık izin verilmedi.</summary>
        OutsideKnownRoots,
        Invalid
    }

    public readonly record struct PathCheck(PathVerdict Verdict, string NormalizedPath, string Reason)
    {
        public bool IsAllowed => Verdict == PathVerdict.Allowed;
    }

    /// <summary>
    /// Güvenlik kararlarında kullanılan klasör kümesi. Üretimde
    /// <see cref="FromEnvironment"/> ile doldurulur; testlerde elle kurulur.
    /// </summary>
    public sealed class KnownFolderSet
    {
        /// <summary>Kendisi silinemeyen klasörler (altı silinebilir).</summary>
        public IReadOnlyList<string> ExactProtected { get; init; } = Array.Empty<string>();

        /// <summary>Kendisi ve altı silinemeyen klasörler.</summary>
        public IReadOnlyList<string> TreeProtected { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Ağaç koruması içinde kalan ama içindeki öğelerin silinebildiği istisnalar
        /// (ör. Başlat Menüsü\Programlar kısayolları, ProgramData\Microsoft altında).
        /// </summary>
        public IReadOnlyList<string> TreeExceptions { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Altındaki klasörlerin (en az 1 seviye) silinebildiği bilinen kökler.
        /// </summary>
        public IReadOnlyList<string> DeletableRoots { get; init; } = Array.Empty<string>();

        /// <summary>Her sürücünün kökünde korunan klasör adları.</summary>
        public static readonly IReadOnlyList<string> ProtectedDriveRootNames = new[]
        {
            "Windows", "System Volume Information", "$Recycle.Bin", "Recovery", "Config.Msi",
            "$WinREAgent", "$Windows.~BT", "$Windows.~WS", "Boot", "EFI", "PerfLogs"
        };

        public static KnownFolderSet FromEnvironment(string? appBaseDirectory = null)
        {
            static string F(Environment.SpecialFolder f)
            {
                try { return Environment.GetFolderPath(f); } catch { return string.Empty; }
            }

            string userProfile = F(Environment.SpecialFolder.UserProfile);
            string local = F(Environment.SpecialFolder.LocalApplicationData);
            string roaming = F(Environment.SpecialFolder.ApplicationData);
            string programData = F(Environment.SpecialFolder.CommonApplicationData);
            string programFiles = F(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = F(Environment.SpecialFolder.ProgramFilesX86);
            string windows = F(Environment.SpecialFolder.Windows);
            string temp = string.Empty;
            try { temp = Path.GetTempPath(); } catch { }

            string usersRoot = string.IsNullOrEmpty(userProfile) ? string.Empty : (Path.GetDirectoryName(userProfile) ?? string.Empty);

            var exact = new List<string>
            {
                programFiles, programFilesX86,
                F(Environment.SpecialFolder.CommonProgramFiles), F(Environment.SpecialFolder.CommonProgramFilesX86),
                programData, usersRoot, userProfile,
                F(Environment.SpecialFolder.Desktop), F(Environment.SpecialFolder.DesktopDirectory),
                F(Environment.SpecialFolder.CommonDesktopDirectory),
                F(Environment.SpecialFolder.MyDocuments), F(Environment.SpecialFolder.CommonDocuments),
                F(Environment.SpecialFolder.MyPictures), F(Environment.SpecialFolder.MyMusic), F(Environment.SpecialFolder.MyVideos),
                F(Environment.SpecialFolder.CommonPictures), F(Environment.SpecialFolder.CommonMusic), F(Environment.SpecialFolder.CommonVideos),
                F(Environment.SpecialFolder.Favorites), F(Environment.SpecialFolder.Templates),
                roaming, local, temp,
                F(Environment.SpecialFolder.Programs), F(Environment.SpecialFolder.CommonPrograms),
                F(Environment.SpecialFolder.StartMenu), F(Environment.SpecialFolder.CommonStartMenu),
                F(Environment.SpecialFolder.Startup), F(Environment.SpecialFolder.CommonStartup),
            };

            if (!string.IsNullOrEmpty(userProfile))
            {
                foreach (string sub in new[] { "Downloads", "AppData", @"AppData\LocalLow", "Saved Games", "Contacts", "Links", "Searches", "OneDrive" })
                    exact.Add(Path.Combine(userProfile, sub));
            }
            if (!string.IsNullOrEmpty(local)) exact.Add(Path.Combine(local, "Programs"));
            if (!string.IsNullOrEmpty(usersRoot)) exact.Add(Path.Combine(usersRoot, "Public"));

            foreach (string env in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            {
                string? v = Environment.GetEnvironmentVariable(env);
                if (!string.IsNullOrWhiteSpace(v)) exact.Add(v);
            }

            // Diğer kullanıcı profillerinin kökleri
            try
            {
                if (!string.IsNullOrEmpty(usersRoot) && Directory.Exists(usersRoot))
                    exact.AddRange(Directory.GetDirectories(usersRoot));
            }
            catch { /* erişilemeyen profiller atlanır; kök zaten korumalı */ }

            var tree = new List<string> { windows };
            if (!string.IsNullOrEmpty(programFiles)) tree.Add(Path.Combine(programFiles, "WindowsApps"));
            if (!string.IsNullOrEmpty(programData))
            {
                tree.Add(Path.Combine(programData, "Microsoft"));
                tree.Add(Path.Combine(programData, "Package Cache"));
            }
            if (!string.IsNullOrEmpty(local)) tree.Add(Path.Combine(local, "Microsoft", "Windows"));
            if (!string.IsNullOrEmpty(roaming)) tree.Add(Path.Combine(roaming, "Microsoft", "Windows"));

            // Bakım'ın kendi kurulum ve veri klasörleri
            if (!string.IsNullOrEmpty(appBaseDirectory)) tree.Add(appBaseDirectory);
            if (!string.IsNullOrEmpty(roaming)) tree.Add(Path.Combine(roaming, "Bakım"));
            if (!string.IsNullOrEmpty(local)) tree.Add(Path.Combine(local, "Bakim"));

            var exceptions = new List<string>
            {
                F(Environment.SpecialFolder.Programs), F(Environment.SpecialFolder.CommonPrograms),
                F(Environment.SpecialFolder.Startup), F(Environment.SpecialFolder.CommonStartup),
            };
            if (!string.IsNullOrEmpty(roaming))
                exceptions.Add(Path.Combine(roaming, "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar"));

            var deletable = new List<string>
            {
                programFiles, programFilesX86, programData, local, roaming, temp,
                F(Environment.SpecialFolder.MyDocuments),
                F(Environment.SpecialFolder.Desktop), F(Environment.SpecialFolder.DesktopDirectory),
                F(Environment.SpecialFolder.CommonDesktopDirectory),
                F(Environment.SpecialFolder.Programs), F(Environment.SpecialFolder.CommonPrograms),
                userProfile,
            };
            if (!string.IsNullOrEmpty(userProfile))
            {
                deletable.Add(Path.Combine(userProfile, "Downloads"));
                deletable.Add(Path.Combine(userProfile, @"AppData\LocalLow"));
                deletable.Add(Path.Combine(userProfile, "Saved Games"));
            }
            if (!string.IsNullOrEmpty(local)) deletable.Add(Path.Combine(local, "Programs"));

            return new KnownFolderSet
            {
                ExactProtected = Clean(exact),
                TreeProtected = Clean(tree),
                TreeExceptions = Clean(exceptions),
                DeletableRoots = Clean(deletable),
            };
        }

        private static IReadOnlyList<string> Clean(IEnumerable<string> paths) =>
            paths.Select(WindowsPath.Normalize)
                 .Where(p => p != null)
                 .Select(p => p!)
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .ToList();
    }

    /// <summary>
    /// Bir dosya ya da klasörün silinmesine, bir klasör altındaki süreçlerin
    /// sonlandırılmasına izin verilip verilmediğine TEK noktadan karar verir.
    /// Kaldırıcı, temizleyici, kurulum nöbetçisi ve depolama araçlarının hepsi
    /// yıkıcı işlemden önce bu sınıfa sorar.
    /// </summary>
    public sealed class PathSafetyGuard
    {
        private static readonly Lazy<PathSafetyGuard> _default =
            new(() => new PathSafetyGuard(KnownFolderSet.FromEnvironment(AppContext.BaseDirectory)));

        /// <summary>Çalışan sistemin klasörleriyle kurulmuş örnek.</summary>
        public static PathSafetyGuard Default => _default.Value;

        private readonly KnownFolderSet _folders;

        public PathSafetyGuard(KnownFolderSet folders)
        {
            _folders = folders;
        }

        /// <param name="path">Silinmek istenen yol.</param>
        /// <param name="isDirectory">Klasör mü?</param>
        /// <param name="allowOutsideKnownRoots">
        /// Bilinen köklerin dışındaki klasörlere (ör. "D:\Oyunlar\X") izin verir.
        /// Yalnızca kesin kanıtla (Uninstall kaydının InstallLocation değeri gibi)
        /// belirlenmiş hedefler için true verilmelidir.
        /// </param>
        public PathCheck CheckDeletion(string? path, bool isDirectory, bool allowOutsideKnownRoots = false)
        {
            string? n = WindowsPath.Normalize(path);
            if (n == null)
                return new PathCheck(PathVerdict.Invalid, path ?? string.Empty, "Geçersiz, göreli ya da ağ yolu.");

            int depth = WindowsPath.Depth(n);
            if (depth == 0)
                return new PathCheck(PathVerdict.ProtectedExact, n, "Sürücü kökü silinemez.");

            string? first = WindowsPath.FirstSegment(n);
            if (first != null && KnownFolderSet.ProtectedDriveRootNames.Any(r => r.Equals(first, StringComparison.OrdinalIgnoreCase)))
                return new PathCheck(PathVerdict.ProtectedTree, n, $"'{first}' sistem klasörü korunur.");

            bool inException = _folders.TreeExceptions.Any(e => WindowsPath.IsStrictlyUnder(n, e));
            if (!inException)
            {
                foreach (string tree in _folders.TreeProtected)
                {
                    if (WindowsPath.IsUnderOrEqual(n, tree))
                        return new PathCheck(PathVerdict.ProtectedTree, n, $"Korunan sistem alanı: {tree}");
                }
            }

            foreach (string exact in _folders.ExactProtected)
            {
                if (n.Equals(exact, StringComparison.OrdinalIgnoreCase))
                    return new PathCheck(PathVerdict.ProtectedExact, n, $"Bu klasörün kendisi silinemez: {exact}");
            }

            if (!isDirectory)
            {
                return depth >= 2
                    ? new PathCheck(PathVerdict.Allowed, n, string.Empty)
                    : new PathCheck(PathVerdict.TooShallow, n, "Sürücü kökündeki dosyalar silinmez.");
            }

            if (inException || _folders.DeletableRoots.Any(r => WindowsPath.IsStrictlyUnder(n, r)))
                return new PathCheck(PathVerdict.Allowed, n, string.Empty);

            if (depth < 2)
                return new PathCheck(PathVerdict.TooShallow, n, "Sürücü köküne çok yakın bir klasör silinmez.");

            return allowOutsideKnownRoots
                ? new PathCheck(PathVerdict.Allowed, n, string.Empty)
                : new PathCheck(PathVerdict.OutsideKnownRoots, n, "Klasör bilinen uygulama konumlarının dışında.");
        }

        /// <summary>
        /// Bu klasörün altında çalışan süreçler toplu sonlandırılabilir mi?
        /// Windows, Program Files kökü, İndirilenler gibi alanlar için hayır.
        /// </summary>
        public PathCheck CheckKillScope(string? directory) =>
            CheckDeletion(directory, isDirectory: true, allowOutsideKnownRoots: true);
    }
}
