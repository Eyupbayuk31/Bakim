using System;
using Bakım.Core.Safety;
using Microsoft.Win32;

namespace Bakım.Core.Sentinel
{
    /// <summary>Nöbetçi anahtarlarını ("HKLM\Sub\Değer [32]") kayıt defteri yoluna çevirir.</summary>
    public static class FindingTarget
    {
        /// <summary>
        /// "HKLM\SOFTWARE\...\notepad.exe\Debugger [32]" → (HKLM, 32, "SOFTWARE\...\notepad.exe", "Debugger").
        /// Son bölüm değer adıdır.
        /// </summary>
        public static bool TryParseValue(string? key, out RegistryPath path)
        {
            path = default;
            if (string.IsNullOrWhiteSpace(key)) return false;
            string k = key.Trim();
            var view = RegistryView.Registry64;
            if (k.EndsWith(" [32]", StringComparison.Ordinal))
            {
                view = RegistryView.Registry32;
                k = k[..^5];
            }

            int first = k.IndexOf('\\');
            int last = k.LastIndexOf('\\');
            if (first <= 0 || last <= first) return false;

            RegistryHive hive;
            switch (k[..first].ToUpperInvariant())
            {
                case "HKLM":
                case "HKEY_LOCAL_MACHINE":
                    hive = RegistryHive.LocalMachine;
                    break;
                case "HKCU":
                case "HKEY_CURRENT_USER":
                    hive = RegistryHive.CurrentUser;
                    break;
                default:
                    return false;
            }

            string sub = k[(first + 1)..last];
            string value = k[(last + 1)..];
            if (sub.Length == 0 || value.Length == 0) return false;
            path = new RegistryPath(hive, view, sub, value);
            return true;
        }

        /// <summary>"...\Root\Certificates\{parmak izi}" → parmak izi ve deponun kullanıcı/makine olduğu.</summary>
        public static bool TryParseCertificate(string? key, out string thumbprint, out bool machine)
        {
            thumbprint = string.Empty;
            machine = false;
            if (string.IsNullOrWhiteSpace(key) || !key.Contains(@"\Root\Certificates\", StringComparison.OrdinalIgnoreCase)) return false;
            thumbprint = key[(key.LastIndexOf('\\') + 1)..].Trim();
            machine = key.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase);
            return thumbprint.Length == 40 && IsHex(thumbprint);
        }

        /// <summary>"Paths: C:\x" → ("Path", "C:\x"); Defender tercih adı ve değeri.</summary>
        public static bool TryParseDefenderExclusion(string? key, out string parameter, out string value)
        {
            parameter = value = string.Empty;
            if (string.IsNullOrWhiteSpace(key)) return false;
            int colon = key.IndexOf(": ", StringComparison.Ordinal);
            if (colon <= 0) return false;
            parameter = key[..colon] switch
            {
                "Paths" => "ExclusionPath",
                "Processes" => "ExclusionProcess",
                "Extensions" => "ExclusionExtension",
                "IpAddresses" => "ExclusionIpAddress",
                _ => string.Empty
            };
            value = key[(colon + 2)..];
            return parameter.Length > 0 && value.Length > 0;
        }

        private static bool IsHex(string s)
        {
            foreach (char c in s)
            {
                if (!Uri.IsHexDigit(c)) return false;
            }
            return true;
        }
    }
}
