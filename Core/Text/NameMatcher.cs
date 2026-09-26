using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Bakım.Core.Text
{
    public enum MatchConfidence
    {
        None = 0,
        Low = 30,
        Medium = 60,
        High = 90,
        Certain = 100
    }

    public enum MatchKind
    {
        None,
        /// <summary>Klasör/anahtar adı uygulama adının anlamlı kısmına birebir eşit.</summary>
        NameExact,
        /// <summary>"Yayıncı\Uygulama" yapısı: üst klasör yayıncı, alt klasör uygulama.</summary>
        NameVendorApp,
        /// <summary>Ad, uygulama adının tüm anlamlı kelimelerini sırayla içeriyor.</summary>
        NameContains,
        /// <summary>Tek bir uzun, ayırt edici kelime eşleşiyor.</summary>
        NameToken,
        /// <summary>Ad yayıncının kendisi: silinmez, yalnızca içine bakılır.</summary>
        PublisherRoot
    }

    public readonly record struct NameMatch(MatchConfidence Confidence, MatchKind Kind, string Reason)
    {
        public static readonly NameMatch NoMatch = new(MatchConfidence.None, MatchKind.None, string.Empty);
        public bool IsCandidate => Confidence > MatchConfidence.None;
    }

    /// <summary>
    /// Kalıntı taramasında klasör ve kayıt anahtarı adlarını uygulama adıyla
    /// karşılaştırır.
    ///
    /// Eski algoritma tek bir kelimenin ALT DİZE olarak geçmesine %85 güven
    /// veriyordu ("git" → "Digital…", "media" → "Windows Media Player") ve
    /// yayıncı + herhangi bir kelime eşleşmesine %100 veriyordu ("Google Drive"
    /// kaldırılırken %LocalAppData%\Google — Chrome profili dahil). Bu sınıf:
    ///   • kelime bazlı karşılaştırır (alt dize değil),
    ///   • sürüm/mimari ifadelerini ve genel kelimeleri atar,
    ///   • yayıncı adını TEK BAŞINA asla aday saymaz,
    ///   • isimden gelen hiçbir kanıta Certain (kesin) vermez.
    /// </summary>
    public sealed class NameMatcher
    {
        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            // Genel yazılım kelimeleri
            "microsoft", "windows", "corporation", "corp", "inc", "ltd", "llc", "gmbh", "co", "company", "limited",
            "the", "app", "apps", "application", "software", "installer", "setup", "update", "updater",
            "service", "services", "system", "tool", "tools", "common", "shared", "temp", "cache", "data",
            "bin", "lib", "help", "doc", "docs", "support", "net", "framework", "runtime", "client", "desktop",
            "media", "player", "studio", "pro", "free", "suite", "manager", "launcher", "helper", "driver",
            "drivers", "redistributable", "edition", "community", "professional", "ultimate", "home", "plus",
            "online", "cloud", "sync", "drive", "reader", "viewer", "editor", "converter", "and", "for", "of",
            "version", "sürüm", "beta", "preview", "insider", "lite", "portable", "x64", "x86", "amd64",
            "arm64", "win64", "win32", "bit", "user", "machine", "wide", "package", "packages", "team",
        };

        private static readonly Regex VersionPattern = new(@"\bv?\d+(?:[._]\d+)+[a-z]?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ArchParen = new(@"\([^)]*(?:bit|x64|x86|amd64|arm64|win64|win32)[^)]*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly List<string> _allTokens;
        private readonly List<string> _meaningful;
        private readonly List<string> _publisherTokens;
        private readonly bool _appNamedAfterPublisher;

        public NameMatcher(string appDisplayName, string? publisher)
        {
            _allTokens = Tokenize(StripVersions(appDisplayName));
            _publisherTokens = string.IsNullOrWhiteSpace(publisher) || publisher.Contains("Bilinmeyen", StringComparison.OrdinalIgnoreCase)
                ? new List<string>()
                : Tokenize(publisher).Where(t => !StopWords.Contains(t) || t.Equals("microsoft", StringComparison.OrdinalIgnoreCase)).ToList();

            var withoutStop = _allTokens.Where(t => !StopWords.Contains(t)).ToList();
            var withoutPublisher = withoutStop.Where(t => !IsPublisherToken(t)).ToList();
            var allMinusPublisher = _allTokens.Where(t => !IsPublisherToken(t)).ToList();

            if (withoutPublisher.Count > 0)
            {
                // Normal durum: "VLC media player" → [vlc], "Adobe Acrobat Reader" → [acrobat]
                _meaningful = withoutPublisher;
            }
            else if (allMinusPublisher.Count > 0)
            {
                // Ayırt edici kelime genel bir kelime: "Google Drive" → [drive]. Yayıncı
                // adı ("google") kimlik olarak KULLANILMAZ; aksi halde tüm Google
                // klasörleri (Chrome profili dahil) aday olurdu.
                _meaningful = allMinusPublisher;
            }
            else
            {
                // Uygulama yayıncısıyla aynı adı taşıyor: "Discord" / "Discord Inc.",
                // "Notepad++" / "Notepad++ Team". O addaki klasör uygulamanın kendisidir.
                _meaningful = withoutStop.Count > 0 ? withoutStop : _allTokens;
                _appNamedAfterPublisher = _meaningful.Count > 0;
            }
        }

        private bool IsPublisherToken(string token) => _publisherTokens.Contains(token, StringComparer.OrdinalIgnoreCase);

        /// <summary>Uygulama adının ayırt edici kelimeleri (küçük harf).</summary>
        public IReadOnlyList<string> MeaningfulTokens => _meaningful;

        public IReadOnlyList<string> PublisherTokens => _publisherTokens;

        /// <summary>Eşleştirme yapılabilecek bir ad var mı?</summary>
        public bool HasIdentity => _meaningful.Count > 0;

        /// <summary>Ad, yayıncının kendisi mi? (ör. "Google", "Adobe")</summary>
        public bool IsPublisherName(string name)
        {
            if (_publisherTokens.Count == 0) return false;
            var tokens = Tokenize(name).Where(t => !StopWords.Contains(t) || t.Equals("microsoft", StringComparison.OrdinalIgnoreCase)).ToList();
            return tokens.Count > 0 && tokens.SequenceEqual(_publisherTokens, StringComparer.OrdinalIgnoreCase);
        }

        /// <param name="name">Klasör ya da kayıt anahtarı adı.</param>
        /// <param name="parentName">Üst klasör/anahtar adı (Yayıncı\Uygulama tespiti için).</param>
        public NameMatch Match(string name, string? parentName = null)
        {
            if (!HasIdentity || string.IsNullOrWhiteSpace(name)) return NameMatch.NoMatch;

            if (!_appNamedAfterPublisher && IsPublisherName(name))
                return new NameMatch(MatchConfidence.None, MatchKind.PublisherRoot, "Yayıncı klasörü: yalnızca içine bakılır.");

            var tokens = Tokenize(StripVersions(name));
            if (tokens.Count == 0) return NameMatch.NoMatch;

            var meaningfulName = tokens.Where(t => !StopWords.Contains(t)).ToList();
            bool parentIsPublisher = parentName != null && !_appNamedAfterPublisher && IsPublisherName(parentName);

            // Kimlik yalnızca genel kelimelerden oluşuyorsa ("Google Drive" → [drive])
            // ad tek başına yeterli kanıt değildir; yayıncı klasörü altında olmalıdır.
            bool genericIdentity = _meaningful.All(t => StopWords.Contains(t));

            // 1. Birebir: ad, uygulamanın anlamlı adına ya da tam adına eşit.
            bool fullNameMatch = Same(tokens, _allTokens);
            if (fullNameMatch || Same(meaningfulName, _meaningful) || Same(tokens, _meaningful))
            {
                if (parentIsPublisher)
                    return new NameMatch(MatchConfidence.High, MatchKind.NameVendorApp, $"'{parentName}\\{name}' yayıncı ve uygulama adıyla eşleşiyor.");

                // Tam ad ("OBS Studio" ↔ "obs-studio", "Google Drive") her zaman
                // ayırt edicidir; yalnızca genel kelimeye indirgenmiş eşleşme zayıftır.
                return genericIdentity && !fullNameMatch
                    ? new NameMatch(MatchConfidence.Low, MatchKind.NameToken, $"'{name}' genel bir kelime; yayıncı klasörü dışında zayıf kanıt.")
                    : new NameMatch(MatchConfidence.High, MatchKind.NameExact, $"'{name}' uygulama adıyla birebir eşleşiyor.");
            }

            if (genericIdentity) return NameMatch.NoMatch;

            // 2. İçerme: tüm anlamlı kelimeler sırayla geçiyor. Tek kısa kelimeli
            //    adlar (Git, VLC, Edge) bu kurala giremez; çok fazla yanlış pozitif üretir.
            bool distinctive = _meaningful.Count >= 2 || _meaningful[0].Length >= 6;
            if (distinctive && ContainsSequence(tokens, _meaningful))
            {
                return new NameMatch(MatchConfidence.Medium, MatchKind.NameContains,
                    $"'{name}' uygulama adının tüm kelimelerini içeriyor ({string.Join(' ', _meaningful)}).");
            }

            // 3. Tek ayırt edici kelime (≥ 6 harf).
            string? shared = _meaningful.FirstOrDefault(m => m.Length >= 6 && tokens.Contains(m, StringComparer.OrdinalIgnoreCase));
            if (shared != null)
            {
                return new NameMatch(MatchConfidence.Low, MatchKind.NameToken, $"'{name}' içinde '{shared}' kelimesi geçiyor.");
            }

            return NameMatch.NoMatch;
        }

        /// <summary>Ayırt edici olmayan genel kelime mi ("setup", "x64", "software" …)?</summary>
        public static bool IsStopWord(string token) => StopWords.Contains(token);

        /// <summary>
        /// "VideoLAN VLC_Player-3" → ["video", "lan", "vlc", "player", "3"].
        /// Harf/rakam dışı her şey ayırıcıdır; küçük→büyük harf geçişleri de bölünür.
        /// </summary>
        public static List<string> Tokenize(string? text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            var sb = new StringBuilder();
            char prev = '\0';
            foreach (char c in text)
            {
                if (!char.IsLetterOrDigit(c))
                {
                    Flush();
                }
                else
                {
                    // camelCase sınırı: "videoLan" / "EdgeWebView" / "DriveFS"
                    bool boundary = sb.Length > 0 && char.IsLower(prev) && char.IsUpper(c);
                    if (boundary) Flush();
                    sb.Append(c);
                }
                prev = c;
            }
            Flush();
            return result;

            void Flush()
            {
                if (sb.Length > 0)
                {
                    result.Add(sb.ToString().ToLowerInvariant());
                    sb.Clear();
                }
            }
        }

        private static string StripVersions(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string s = ArchParen.Replace(text, " ");
            s = VersionPattern.Replace(s, " ");
            return s;
        }

        private static bool Same(List<string> a, List<string> b) =>
            a.Count > 0 && a.SequenceEqual(b, StringComparer.OrdinalIgnoreCase);

        private static bool ContainsSequence(List<string> haystack, List<string> needle)
        {
            if (needle.Count == 0 || needle.Count > haystack.Count) return false;
            for (int i = 0; i <= haystack.Count - needle.Count; i++)
            {
                bool ok = true;
                for (int j = 0; j < needle.Count; j++)
                {
                    if (!haystack[i + j].Equals(needle[j], StringComparison.OrdinalIgnoreCase)) { ok = false; break; }
                }
                if (ok) return true;
            }
            return false;
        }
    }
}
