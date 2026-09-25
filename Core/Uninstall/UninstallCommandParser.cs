using System;
using System.Text.RegularExpressions;

namespace Bakım.Core.Uninstall
{
    public enum InstallerFamily
    {
        Unknown,
        Msi,
        InnoSetup,
        Nsis,
        InstallShield,
        WixBurn,
        Squirrel,
        Steam,
        GenericExe
    }

    public sealed record ParsedUninstallCommand(
        string FileName,
        string Arguments,
        string? MsiProductCode,
        bool IsShellCommand)
    {
        public bool IsMsi => MsiProductCode != null;
    }

    /// <summary>
    /// Uninstall kayıtlarındaki UninstallString / QuietUninstallString değerlerini
    /// çalıştırılabilir dosya + argüman ikilisine ayırır.
    ///
    /// Eski ayrıştırıcı tırnaksız ve boşluklu yolları ("C:\Program Files (x86)\Foo\uninst.exe /S")
    /// ilk boşluktan bölüp "C:\Program" diye çalıştırmaya çalışıyordu.
    /// </summary>
    public static class UninstallCommandParser
    {
        private static readonly Regex GuidPattern = new(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}", RegexOptions.Compiled);
        private static readonly Regex InnoUninstaller = new(@"(^|\\)unins\d{3}\.exe$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <param name="command">Kayıttaki ham komut.</param>
        /// <param name="fileExists">Dosya varlık denetimi (testte sahte verilir).</param>
        /// <param name="expandEnvironment">Ortam değişkeni genişletme (testte sahte verilir).</param>
        public static ParsedUninstallCommand? Parse(string? command, Func<string, bool> fileExists, Func<string, string>? expandEnvironment = null)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;

            string cmd = (expandEnvironment ?? Environment.ExpandEnvironmentVariables)(command.Trim());

            // 1. MSI: argümanı sıfırdan kur ("/I" → "/X" hilesi yerine ürün koduyla).
            string firstToken = FirstToken(cmd);
            string firstName = firstToken.Trim('"').Replace('/', '\\').Split('\\')[^1];
            if (firstName.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase))
            {
                var guid = GuidPattern.Match(cmd);
                return guid.Success
                    ? new ParsedUninstallCommand("msiexec.exe", "/X" + guid.Value.ToUpperInvariant(), guid.Value.ToUpperInvariant(), false)
                    : new ParsedUninstallCommand("msiexec.exe", cmd.Substring(firstToken.Length).Trim(), null, false);
            }

            // 2. Tırnaklı dosya yolu.
            if (cmd.StartsWith('"'))
            {
                int close = cmd.IndexOf('"', 1);
                if (close > 1)
                {
                    string file = cmd.Substring(1, close - 1);
                    string args = cmd.Substring(close + 1).Trim();
                    return new ParsedUninstallCommand(file, args, null, IsShell(file));
                }
                return new ParsedUninstallCommand(cmd.Trim('"'), string.Empty, null, IsShell(cmd));
            }

            // 3. Tırnaksız: boşluklara göre parçaları birleştirerek var olan ilk dosyayı bul.
            string[] parts = cmd.Split(' ');
            var candidate = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) candidate.Append(' ');
                candidate.Append(parts[i]);
                string c = candidate.ToString();

                if (fileExists(c) || (!c.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && fileExists(c + ".exe")))
                {
                    string file = fileExists(c) ? c : c + ".exe";
                    string args = string.Join(' ', parts, i + 1, parts.Length - i - 1).Trim();
                    return new ParsedUninstallCommand(file, args, null, IsShell(file));
                }
            }

            // 4. Dosya bulunamadı: ".exe" ile biten ilk konuma kadar olan kısım dosyadır.
            int exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe > 0)
            {
                int end = exe + 4;
                if (end == cmd.Length || cmd[end] == ' ')
                {
                    string file = cmd.Substring(0, end);
                    return new ParsedUninstallCommand(file, cmd.Substring(end).Trim(), null, IsShell(file));
                }
            }

            // 5. Son çare: ilk boşluktan böl.
            int space = cmd.IndexOf(' ');
            return space > 0
                ? new ParsedUninstallCommand(cmd[..space], cmd[(space + 1)..].Trim(), null, IsShell(cmd[..space]))
                : new ParsedUninstallCommand(cmd, string.Empty, null, IsShell(cmd));
        }

        /// <summary>Kaldırıcı ailesini komut ve dosya yolundan tahmin eder.</summary>
        public static InstallerFamily DetectFamily(ParsedUninstallCommand parsed, string? uninstallKeyName, Func<string, bool> fileExists)
        {
            if (parsed.IsMsi) return InstallerFamily.Msi;

            string file = parsed.FileName.Replace('/', '\\');
            string fileName = file.Split('\\')[^1];
            string args = parsed.Arguments;

            if (!string.IsNullOrEmpty(uninstallKeyName) && uninstallKeyName.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase))
                return InstallerFamily.Steam;
            if (args.Contains("steam://uninstall", StringComparison.OrdinalIgnoreCase))
                return InstallerFamily.Steam;

            if (InnoUninstaller.IsMatch(file))
                return InstallerFamily.InnoSetup;

            if (fileName.Equals("Update.exe", StringComparison.OrdinalIgnoreCase) &&
                args.Contains("--uninstall", StringComparison.OrdinalIgnoreCase))
                return InstallerFamily.Squirrel;

            if (file.Contains(@"\Package Cache\", StringComparison.OrdinalIgnoreCase) &&
                args.Contains("/uninstall", StringComparison.OrdinalIgnoreCase))
                return InstallerFamily.WixBurn;

            if (args.Contains("-uninst", StringComparison.OrdinalIgnoreCase) ||
                args.Contains("-removeonly", StringComparison.OrdinalIgnoreCase) ||
                file.Contains("InstallShield", StringComparison.OrdinalIgnoreCase))
                return InstallerFamily.InstallShield;

            if (fileName.Equals("uninstall.exe", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("uninst.exe", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("uninst-", StringComparison.OrdinalIgnoreCase))
                return InstallerFamily.Nsis;

            return InstallerFamily.GenericExe;
        }

        /// <summary>
        /// Sessiz kaldırma argümanları. Desteklenmiyorsa null döner:
        /// InstallShield yanıt dosyası ister, genel exe'lerin sessiz parametresi bilinmez.
        /// </summary>
        public static string? BuildSilentArguments(ParsedUninstallCommand parsed, InstallerFamily family)
        {
            string args = parsed.Arguments;
            string dir = parsed.FileName.Contains('\\') ? parsed.FileName[..parsed.FileName.LastIndexOf('\\')] : string.Empty;

            switch (family)
            {
                case InstallerFamily.Msi when parsed.MsiProductCode != null:
                    return $"/X{parsed.MsiProductCode} /qn /norestart";

                case InstallerFamily.InnoSetup:
                    return Append(args, "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART");

                case InstallerFamily.Nsis:
                    // _?= en sonda olmalı: kaldırıcının kendini temp'e kopyalayıp
                    // hemen çıkmasını engeller, bitene kadar bekler.
                    string nsis = Append(args, "/S");
                    return string.IsNullOrEmpty(dir) || nsis.Contains("_?=") ? nsis : $"{nsis} _?={dir}";

                case InstallerFamily.Squirrel:
                    return Append(args, "-s");

                case InstallerFamily.WixBurn:
                    return Append(args, "/quiet", "/norestart");

                default:
                    return null;
            }
        }

        /// <summary>MSI için başarılı sayılan çıkış kodları.</summary>
        public static bool IsMsiSuccess(int exitCode) => exitCode is 0 or 1605 or 3010 or 1641;

        /// <summary>1602: kullanıcı iptal etti (MSI).</summary>
        public static bool IsUserCancel(int exitCode) => exitCode == 1602;

        private static string Append(string args, params string[] switches)
        {
            string result = args;
            foreach (string s in switches)
            {
                if (!Regex.IsMatch(result, $@"(^|\s){Regex.Escape(s)}(\s|$)", RegexOptions.IgnoreCase))
                    result = string.IsNullOrEmpty(result) ? s : result + " " + s;
            }
            return result.Trim();
        }

        private static string FirstToken(string cmd)
        {
            if (cmd.StartsWith('"'))
            {
                int close = cmd.IndexOf('"', 1);
                return close > 0 ? cmd[..(close + 1)] : cmd;
            }
            int space = cmd.IndexOf(' ');
            return space > 0 ? cmd[..space] : cmd;
        }

        private static bool IsShell(string file)
        {
            string name = file.Replace('/', '\\').Split('\\')[^1];
            return name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("cmd", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("wscript.exe", StringComparison.OrdinalIgnoreCase);
        }
    }
}
