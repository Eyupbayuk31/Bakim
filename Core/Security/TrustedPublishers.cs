using System;
using System.Collections.Generic;

namespace Bakım.Core.Security
{
    /// <summary>
    /// Tehdit analizinde risk puanını düşüren tanınmış yayıncılar.
    ///
    /// Eski kontrol alt dize eşleşmesiyle yapılıyordu: "AMD" "Hamdi Yazılım" ile,
    /// "Intel" "Intellisoft" ile, "Apple" "Applet Games" ile eşleşiyordu. Artık
    /// imzalayan sertifikanın CN değeri bu listedeki adlardan biriyle BİREBİR
    /// (büyük/küçük harf duyarsız) aynı olmalıdır.
    /// </summary>
    public static class TrustedPublishers
    {
        private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft Corporation",
            "Microsoft Windows",
            "Microsoft Windows Publisher",
            "Microsoft Windows Hardware Compatibility Publisher",
            "Microsoft 3rd Party Application Component",
            "Google LLC",
            "Google Inc",
            "NVIDIA Corporation",
            "Intel Corporation",
            "Advanced Micro Devices, Inc.",
            "Advanced Micro Devices Inc.",
            "Valve",
            "Valve Corp.",
            "Valve Corporation",
            "Apple Inc.",
            "Adobe Inc.",
            "Adobe Systems, Incorporated",
            "Adobe Systems Incorporated",
            "Mozilla Corporation",
            "Discord Inc.",
            "Spotify AB",
            "Oracle America, Inc.",
            "GitHub, Inc.",
            "Epic Games Inc.",
            "Epic Games, Inc.",
        };

        public static bool IsTrusted(string? signerCommonName)
        {
            if (string.IsNullOrWhiteSpace(signerCommonName)) return false;
            return Names.Contains(signerCommonName.Trim().Trim('"'));
        }

        public static IReadOnlyCollection<string> All => Names;
    }
}
