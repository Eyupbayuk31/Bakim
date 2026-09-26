using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakım.Core.Sentinel
{
    /// <summary>
    /// Kurulum oturumunun ne zaman açılıp kapanacağına dair saf kurallar (NÖB v3 A8, A10).
    /// </summary>
    public static class SetupSessionPolicy
    {
        /// <summary>
        /// Arka planda kendi kendine kurulum/güncelleme başlatan süreçler (oyun başlatıcıları,
        /// güncelleyiciler, Windows hizmet ve görev ana bilgisayarları). Bunların çocuğu olan bir
        /// kurulumu kullanıcı başlatmamıştır: varsayılan olarak oturum açılmaz.
        /// </summary>
        private static readonly HashSet<string> BackgroundLaunchers = new(StringComparer.OrdinalIgnoreCase)
        {
            // Oyun başlatıcıları
            "riotclientservices", "riotclientux", "riotclientuxrender", "riot client", "leagueclient",
            "steam", "steamservice", "epicgameslauncher", "epiconlineservices", "battle.net", "blizzard battle.net",
            "eadesktop", "eabackgroundservice", "origin", "originwebhelperservice", "upc", "ubisoftconnect",
            "uplaywebcore", "galaxyclient", "galaxyclientservice", "rockstarservice", "launcher-rockstar",
            // Güncelleyiciler
            "googleupdate", "googleupdater", "updater", "microsoftedgeupdate", "msedgeupdate", "onedrive",
            "onedrivestandaloneupdater", "jusched", "jucheck", "adobearm", "armsvc", "adobe desktop service",
            "adobeupdateservice", "nvcontainer", "nvidia app", "nvidia share", "amdrsserv", "radeonsoftware",
            "dropboxupdate", "zoomupdater", "brave_updater", "braveupdate", "operaupdater",
            "wuauclt", "trustedinstaller", "tiworker", "usoclient", "musnotification", "mousocoreworker",
        };

        /// <summary>
        /// Windows hizmet ve görev ana bilgisayarları: yalnızca DOĞRUDAN ebeveynse sayılır. Mağaza
        /// uygulamaları (Windows Terminal vb.) da svchost altından başlar; üst kuşaklarda aranırsa
        /// terminalden elle başlatılan kurulumlar da susturulurdu.
        /// </summary>
        private static readonly HashSet<string> DirectParentHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "svchost", "services", "taskhostw", "taskeng", "wmiprvse",
        };

        public static bool IsBackgroundLauncher(string? exeName) => BackgroundLaunchers.Contains(NameOf(exeName));

        private static string NameOf(string? exeName) =>
            SetupAppName.FileStem(exeName);

        /// <summary>
        /// Adayın ata zincirinde (en fazla <paramref name="maxDepth"/> kuşak) arka plan başlatıcısı ya da
        /// doğrudan ebeveyn olarak bir Windows hizmet ana bilgisayarı var mı?
        /// <paramref name="parentOf"/> bir sürecin hâlâ yaşayan ebeveynini (PID, exe adı) verir; ebeveyn
        /// çıkmışsa ya da PID başka bir sürece geçmişse null dönmelidir.
        /// </summary>
        public static string? FindBackgroundAncestor(int pid, Func<int, (int Pid, string ExeName)?> parentOf, int maxDepth = 4)
        {
            var seen = new HashSet<int> { pid };
            int current = pid;
            for (int depth = 0; depth < maxDepth; depth++)
            {
                var parent = parentOf(current);
                if (parent is not { } p || p.Pid <= 4 || !seen.Add(p.Pid)) return null;
                if (IsBackgroundLauncher(p.ExeName) || (depth == 0 && DirectParentHosts.Contains(NameOf(p.ExeName)))) return p.ExeName;
                current = p.Pid;
            }
            return null;
        }

        /// <summary>
        /// Kurulumun sonunda başlattığı uygulama mı? Kök kurulum süreci çıktıktan sonra ağaçta kalan
        /// bir sürecin görüntüsü bu oturumda yazılmış bir dosyaysa, geçici klasörde değilse ve adı
        /// kurulum aracına benzemiyorsa kurulan uygulamadır: oturumu açık tutmamalıdır (A10).
        /// </summary>
        public static bool IsLaunchedInstalledApp(string? imagePath, ISet<string> sessionWrittenFiles, IEnumerable<string> tempRoots)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !sessionWrittenFiles.Contains(imagePath)) return false;

            string lower = imagePath.ToLowerInvariant();
            if (lower.Contains(@"\package cache\") || lower.Contains(@"\installer\")) return false;
            foreach (string root in tempRoots.Where(r => !string.IsNullOrWhiteSpace(r)))
            {
                if (imagePath.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return false;
            }

            string name = SetupAppName.FileStem(imagePath).ToLowerInvariant();
            string[] installerWords = { "setup", "install", "unins", "uninst", "kurulum", "msiexec", "bootstrap" };
            return !installerWords.Any(name.Contains);
        }
    }
}
