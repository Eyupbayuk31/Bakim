using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bakım.Core.Analyzer
{
    public enum TargetResolution
    {
        /// <summary>Çalıştırılan dosya diskte bulundu.</summary>
        Resolved,
        /// <summary>Komut çözümlendi ama dosya yok (gerçek yetim kayıt).</summary>
        NotFound,
        /// <summary>Komut boş.</summary>
        Empty
    }

    /// <summary>Bir kalıcılık komutunun çözümlenmiş hâli.</summary>
    /// <param name="FilePath">Doğrudan çalıştırılan dosya (sarmalayıcı dahil, ör. rundll32.exe).</param>
    /// <param name="HostName">Sarmalayıcı süreç (rundll32, regsvr32, cmd, powershell, wscript, mshta …).</param>
    /// <param name="Payload">Sarmalayıcının çalıştırdığı asıl dosya ya da adres.</param>
    /// <param name="InlineScript">Komut satırına gömülü betik (cmd /c, powershell -Command, çözülmüş -EncodedCommand).</param>
    public sealed record ResolvedCommand(
        string RawCommand,
        string FilePath,
        string Arguments,
        TargetResolution Resolution,
        string? HostName = null,
        string? Payload = null,
        bool PayloadFound = false,
        string? InlineScript = null)
    {
        /// <summary>İmza, hash ve risk için incelenecek dosya: sarmalayıcı varsa asıl yük.</summary>
        public string EffectiveTarget => Payload != null && PayloadFound ? Payload : FilePath;

        public bool IsWrapped => HostName != null;
    }

    /// <summary>Çözümleyicinin dış dünyası (testte sahte verilir).</summary>
    public sealed class ResolverEnvironment
    {
        public required Func<string, bool> FileExists { get; init; }
        public Func<string, string> Expand { get; init; } = Environment.ExpandEnvironmentVariables;
        public required string WindowsDirectory { get; init; }
        /// <summary>Göreli adlar için arama sırası (%windir%, System32, PATH …).</summary>
        public IReadOnlyList<string> SearchDirectories { get; init; } = Array.Empty<string>();

        public static ResolverEnvironment ForCurrentSystem()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var dirs = new List<string> { Environment.SystemDirectory, windows, System.IO.Path.Combine(windows, "System32", "wbem") };
            foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';'))
            {
                string dir = Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));
                if (dir.Length > 2 && !dirs.Contains(dir, StringComparer.OrdinalIgnoreCase)) dirs.Add(dir);
            }
            return new ResolverEnvironment
            {
                FileExists = System.IO.File.Exists,
                WindowsDirectory = windows,
                SearchDirectories = dirs
            };
        }
    }

    /// <summary>
    /// Kalıcılık girdilerindeki ham komutu Windows'un çözdüğü gibi çözer (ANL Z6).
    ///
    /// Eski ayrıştırıcı ilk boşlukta kesiyordu: "C:\Program Files\X\a.exe" → "C:\Program";
    /// "userinit.exe," ve "explorer.exe" yetim sayılıyordu; "\SystemRoot\System32\drivers\x.sys"
    /// hiç çözülmüyordu. Bu makinede 365 görevin 363'ü, 213 hizmetin 135'i yanlışlıkla "dosya yok"
    /// görünüyordu.
    /// </summary>
    public static class CommandLineResolver
    {
        private static readonly string[] ExecutableExtensions =
            { ".exe", ".dll", ".sys", ".com", ".bat", ".cmd", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".ps1", ".scr", ".cpl", ".msc", ".hta", ".lnk" };

        public static ResolvedCommand Resolve(string? rawCommand, ResolverEnvironment env) => Resolve(rawCommand, env, depth: 0);

        private static ResolvedCommand Resolve(string? rawCommand, ResolverEnvironment env, int depth)
        {
            string raw = rawCommand ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return new ResolvedCommand(raw, string.Empty, string.Empty, TargetResolution.Empty);

            string cmd = NormalizeLeadingPath(env.Expand(raw.Trim()).Trim(), env.WindowsDirectory);
            var (file, args) = SplitFileAndArgs(cmd, env);
            file = file.Trim().Trim('"').TrimEnd(',', ';').Trim();
            string? located = Locate(file, env);
            var result = new ResolvedCommand(raw, located ?? file, args, located != null ? TargetResolution.Resolved : TargetResolution.NotFound);
            return depth < 2 ? Unwrap(result, env, depth) : result;
        }

        /// <summary>
        /// NT yol önekleri: "\??\C:\x", "\SystemRoot\x", "SystemRoot\x" ve hizmetlerdeki göreli
        /// "System32\drivers\x.sys" (%windir% altına göredir).
        /// </summary>
        public static string NormalizeLeadingPath(string command, string windowsDirectory)
        {
            if (string.IsNullOrEmpty(command)) return command;
            bool quoted = command.StartsWith('"');
            string body = quoted ? command[1..] : command;
            string windows = windowsDirectory.TrimEnd('\\');

            if (body.StartsWith(@"\??\", StringComparison.Ordinal)) body = body[4..];
            if (body.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) body = windows + body[11..];
            else if (body.StartsWith(@"SystemRoot\", StringComparison.OrdinalIgnoreCase)) body = windows + "\\" + body[11..];
            else if (body.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase)) body = windows + "\\" + body;

            return quoted ? "\"" + body : body;
        }

        private static (string File, string Args) SplitFileAndArgs(string cmd, ResolverEnvironment env)
        {
            if (cmd.StartsWith('"'))
            {
                int close = cmd.IndexOf('"', 1);
                return close > 1 ? (cmd[1..close], cmd[(close + 1)..].Trim()) : (cmd.Trim('"'), string.Empty);
            }

            // Tırnaksız: boşluklu parçaları birleştirerek var olan ilk dosyayı bul (CreateProcess davranışı).
            string[] parts = cmd.Split(' ');
            var candidate = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) candidate.Append(' ');
                candidate.Append(parts[i]);
                string c = candidate.ToString().TrimEnd(',', ';');
                if (c.Length == 0) continue;
                // Göreli çok kelimeli adları PATH'te aramak anlamsız ve yanıltıcı.
                if (i > 0 && !IsRooted(c)) break;
                if (Locate(c, env) != null)
                    return (c, string.Join(' ', parts, i + 1, parts.Length - i - 1).Trim());
            }

            // Dosya yok: bilinen bir uzantının bittiği ilk kelime sınırı dosyadır.
            string lower = cmd.ToLowerInvariant();
            int best = -1;
            foreach (string ext in ExecutableExtensions)
            {
                int at = 0;
                while ((at = lower.IndexOf(ext, at, StringComparison.Ordinal)) >= 0)
                {
                    int end = at + ext.Length;
                    if (end == lower.Length || lower[end] is ' ' or ',' or ';')
                    {
                        if (best < 0 || end < best) best = end;
                        break;
                    }
                    at = end;
                }
            }
            if (best > 0) return (cmd[..best], cmd[best..].TrimStart(',', ';').Trim());

            int space = cmd.IndexOf(' ');
            return space > 0 ? (cmd[..space], cmd[(space + 1)..].Trim()) : (cmd, string.Empty);
        }

        /// <summary>Dosyayı bulur: kök yol olduğu gibi; göreli ad %windir%, System32 ve PATH'te; uzantısızsa ".exe" de denenir.</summary>
        public static string? Locate(string? path, ResolverEnvironment env)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string p = path.Trim().Trim('"').TrimEnd(',', ';').Trim().Replace('/', '\\');
            if (p.Length == 0 || p.IndexOfAny(new[] { '<', '>', '|', '*', '?' }) >= 0) return null;

            string[] candidates = HasExtension(p) ? new[] { p } : new[] { p, p + ".exe" };
            if (IsRooted(p))
                return candidates.FirstOrDefault(env.FileExists);

            if (p.Contains('\\'))
            {
                string underWindows = env.WindowsDirectory.TrimEnd('\\') + "\\" + p.TrimStart('\\');
                return (HasExtension(p) ? new[] { underWindows } : new[] { underWindows, underWindows + ".exe" }).FirstOrDefault(env.FileExists);
            }

            var dirs = new List<string> { env.WindowsDirectory, env.WindowsDirectory.TrimEnd('\\') + @"\System32" };
            dirs.AddRange(env.SearchDirectories);
            foreach (string dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (string name in candidates)
                {
                    string full = dir.TrimEnd('\\') + "\\" + name;
                    if (env.FileExists(full)) return full;
                }
            }
            return null;
        }

        public static bool IsRooted(string path) =>
            (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/') || path.StartsWith(@"\\", StringComparison.Ordinal);

        private static bool HasExtension(string path)
        {
            string name = FileName(path);
            int dot = name.LastIndexOf('.');
            return dot > 0 && dot < name.Length - 1;
        }

        public static string FileName(string path)
        {
            string p = path.Trim().Trim('"');
            return p[(p.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];
        }

        private static string Stem(string path)
        {
            string name = FileName(path);
            int dot = name.LastIndexOf('.');
            return (dot > 0 ? name[..dot] : name).ToLowerInvariant();
        }

        /// <summary>Sarmalayıcıların (rundll32, regsvr32, cmd, powershell, wscript, mshta, conhost) asıl yükünü çıkarır.</summary>
        private static ResolvedCommand Unwrap(ResolvedCommand command, ResolverEnvironment env, int depth)
        {
            string host = Stem(command.FilePath);
            string args = command.Arguments;
            var tokens = Tokenize(args);

            switch (host)
            {
                case "rundll32":
                {
                    // rundll32 "C:\x.dll",Giriş  |  rundll32 shell32.dll,Control_RunDLL
                    string first = args.StartsWith('"')
                        ? args[1..Math.Max(1, args.IndexOf('"', 1))]
                        : args.Split(',', 2)[0].Split(' ', 2)[0];
                    return WithPayload(command, "rundll32", first, env);
                }
                case "regsvr32":
                {
                    string? dll = tokens.LastOrDefault(t => !t.StartsWith('/') && !t.StartsWith('-'));
                    string? remote = tokens.FirstOrDefault(t => t.StartsWith("/i:", StringComparison.OrdinalIgnoreCase));
                    var result = WithPayload(command, "regsvr32", dll, env);
                    return remote != null ? result with { InlineScript = remote[3..].Trim('"') } : result;
                }
                case "cmd":
                {
                    int at = IndexOfSwitch(args, "/c", "/k");
                    if (at < 0) return command with { HostName = "cmd" };
                    string inner = args[(at + 2)..].Trim();
                    if (inner.StartsWith('"') && inner.EndsWith('"') && inner.Count(c => c == '"') == 2) inner = inner[1..^1];
                    var nested = Resolve(inner, env, depth + 1);
                    return command with
                    {
                        HostName = "cmd",
                        InlineScript = inner,
                        Payload = nested.Resolution == TargetResolution.Resolved ? nested.EffectiveTarget : null,
                        PayloadFound = nested.Resolution == TargetResolution.Resolved
                    };
                }
                case "powershell":
                case "pwsh":
                {
                    for (int i = 0; i < tokens.Count; i++)
                    {
                        string t = tokens[i].ToLowerInvariant();
                        if ((t is "-file" or "-f") && i + 1 < tokens.Count)
                            return WithPayload(command, host, tokens[i + 1], env);
                        if ((t is "-encodedcommand" or "-enc" or "-e" or "-ec") && i + 1 < tokens.Count)
                            return command with { HostName = host, InlineScript = DecodePowerShell(tokens[i + 1]) ?? tokens[i + 1] };
                        if ((t is "-command" or "-c") && i + 1 < tokens.Count)
                            return command with { HostName = host, InlineScript = string.Join(' ', tokens.Skip(i + 1)) };
                    }
                    // Anahtarsız ilk serbest belirteç .ps1 ise dosyadır.
                    string? script = tokens.FirstOrDefault(t => t.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase));
                    return script != null ? WithPayload(command, host, script, env) : command with { HostName = host };
                }
                case "wscript":
                case "cscript":
                {
                    string? script = tokens.FirstOrDefault(t => !t.StartsWith("//", StringComparison.Ordinal) && !t.StartsWith('/'));
                    return WithPayload(command, host, script, env);
                }
                case "mshta":
                {
                    string? target = tokens.FirstOrDefault();
                    if (target != null && (target.StartsWith("http", StringComparison.OrdinalIgnoreCase) || target.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || target.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase)))
                        return command with { HostName = "mshta", InlineScript = target };
                    return WithPayload(command, "mshta", target, env);
                }
                case "conhost":
                {
                    string inner = string.Join(' ', tokens.SkipWhile(t => t.StartsWith("--", StringComparison.Ordinal)).Select(Quote));
                    if (inner.Length == 0) return command with { HostName = "conhost" };
                    var nested = Resolve(inner, env, depth + 1);
                    return command with
                    {
                        HostName = "conhost",
                        Payload = nested.EffectiveTarget,
                        PayloadFound = nested.Resolution == TargetResolution.Resolved,
                        InlineScript = nested.InlineScript
                    };
                }
                default:
                    return command;
            }
        }

        private static ResolvedCommand WithPayload(ResolvedCommand command, string host, string? payload, ResolverEnvironment env)
        {
            if (string.IsNullOrWhiteSpace(payload)) return command with { HostName = host };
            string raw = NormalizeLeadingPath(env.Expand(payload.Trim().Trim('"')), env.WindowsDirectory);
            string? located = Locate(raw, env);
            return command with { HostName = host, Payload = located ?? raw, PayloadFound = located != null };
        }

        private static int IndexOfSwitch(string args, params string[] switches)
        {
            foreach (string sw in switches)
            {
                int at = 0;
                while ((at = args.IndexOf(sw, at, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    bool start = at == 0 || args[at - 1] == ' ';
                    bool end = at + sw.Length == args.Length || args[at + sw.Length] == ' ';
                    if (start && end) return at;
                    at += sw.Length;
                }
            }
            return -1;
        }

        private static string Quote(string token) => token.Contains(' ') ? $"\"{token}\"" : token;

        /// <summary>Tırnak farkında boşluk bölme ("a b" tek belirteç).</summary>
        public static List<string> Tokenize(string? args)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(args)) return result;
            var sb = new StringBuilder();
            bool inQuotes = false;
            foreach (char c in args)
            {
                if (c == '"') { inQuotes = !inQuotes; continue; }
                if (c == ' ' && !inQuotes)
                {
                    if (sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); }
                    continue;
                }
                sb.Append(c);
            }
            if (sb.Length > 0) result.Add(sb.ToString());
            return result;
        }

        /// <summary>PowerShell -EncodedCommand: UTF-16LE Base64.</summary>
        public static string? DecodePowerShell(string encoded)
        {
            try
            {
                string text = Encoding.Unicode.GetString(Convert.FromBase64String(encoded.Trim()));
                return text.Length > 0 && text.All(ch => !char.IsControl(ch) || ch is '\r' or '\n' or '\t') ? text : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
