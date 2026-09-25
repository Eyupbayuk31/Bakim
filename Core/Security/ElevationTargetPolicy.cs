using System;
using System.Collections.Generic;
using System.Linq;
using Bakım.Core.Safety;

namespace Bakım.Core.Security
{
    /// <summary>Bir erişim denetim girdisinin (ACE) platformdan bağımsız özeti.</summary>
    public readonly record struct AccessEntry(string Sid, int Rights, bool IsAllow, bool IsInheritOnly);

    /// <summary>Bir dosya ya da klasörün sahibi ve ACE listesi.</summary>
    public sealed record SecuritySnapshot(string Path, string? OwnerSid, IReadOnlyList<AccessEntry> Entries);

    /// <summary>
    /// "UAC'siz yönetici kısayolu" için hedefin güvenli olup olmadığına karar verir (S-11).
    ///
    /// Kısayol, en yüksek yetkiyle çalışan bir zamanlanmış görev oluşturur. Hedef exe'yi
    /// (ya da yanındaki DLL'leri) standart bir kullanıcı değiştirebiliyorsa, kullanıcı
    /// bağlamındaki herhangi bir zararlı exe'yi değiştirip UAC'ye hiç sormadan yönetici
    /// olur. Bu yüzden:
    ///   • Hedef Program Files ya da Windows klasörü altında olmalı.
    ///   • Dosyanın ve köke kadar her üst klasörün sahibi ve yazma izni olan herkes
    ///     SYSTEM / Administrators / TrustedInstaller olmalı.
    /// </summary>
    public static class ElevationTargetPolicy
    {
        // FileSystemRights: WriteData/CreateFiles, AppendData/CreateDirectories,
        // DeleteSubdirectoriesAndFiles, Delete, ChangePermissions, TakeOwnership,
        // GENERIC_ALL ve GENERIC_WRITE. Öznitelik yazma kod çalıştırmaya yol açmadığı için dahil değil.
        public const int WriteMask =
            0x0002 | 0x0004 | 0x0040 | 0x0001_0000 | 0x0004_0000 | 0x0008_0000 | 0x1000_0000 | 0x4000_0000;

        public const string SystemSid = "S-1-5-18";
        public const string AdministratorsSid = "S-1-5-32-544";
        public const string TrustedInstallerSid = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
        public const string CreatorOwnerSid = "S-1-3-0";

        private static readonly HashSet<string> TrustedSids = new(StringComparer.OrdinalIgnoreCase)
        {
            SystemSid, AdministratorsSid, TrustedInstallerSid,
            // CREATOR OWNER yalnızca alt nesnelere miras verilirken anlam taşır; nesnenin kendisinde etkisizdir.
            CreatorOwnerSid
        };

        public static bool IsTrustedSid(string? sid) => sid != null && TrustedSids.Contains(sid);

        /// <summary>Yazabilen güvenilmeyen ilk SID'i (sahip dahil) döndürür; yoksa null.</summary>
        public static string? FindUntrustedWriter(SecuritySnapshot snapshot)
        {
            // Sahip, DACL'yi her zaman değiştirebilir (örtük WRITE_DAC).
            if (snapshot.OwnerSid != null && !IsTrustedSid(snapshot.OwnerSid))
                return snapshot.OwnerSid;

            foreach (var entry in snapshot.Entries)
            {
                if (!entry.IsAllow || entry.IsInheritOnly) continue;
                if ((entry.Rights & WriteMask) == 0) continue;
                if (!IsTrustedSid(entry.Sid)) return entry.Sid;
            }
            return null;
        }

        /// <summary>Hedef, izin verilen köklerden birinin ALTINDA mı (kökün kendisi değil)?</summary>
        public static string? FindAllowedRoot(string targetPath, IEnumerable<string?> roots)
        {
            string? target = WindowsPath.Normalize(targetPath);
            if (target == null) return null;

            foreach (var root in roots)
            {
                string? normalizedRoot = WindowsPath.Normalize(root);
                if (normalizedRoot != null && WindowsPath.IsStrictlyUnder(target, normalizedRoot))
                    return normalizedRoot;
            }
            return null;
        }

        /// <summary>Hedef dosya, üst klasörleri ve kök (dahil) — denetlenmesi gereken zincir.</summary>
        public static IReadOnlyList<string> ChainToRoot(string targetPath, string root)
        {
            var chain = new List<string>();
            string? current = WindowsPath.Normalize(targetPath);
            string? normalizedRoot = WindowsPath.Normalize(root);
            if (current == null || normalizedRoot == null || !WindowsPath.IsUnderOrEqual(current, normalizedRoot))
                return chain;

            while (current != null)
            {
                chain.Add(current);
                if (string.Equals(current, normalizedRoot, StringComparison.OrdinalIgnoreCase)) break;
                current = WindowsPath.Parent(current);
            }
            return chain;
        }

        /// <summary>Zincirdeki ilk güvensiz nesne: (yol, SID); hepsi güvenliyse null.</summary>
        public static (string Path, string Sid)? FindFirstUnsafe(IEnumerable<SecuritySnapshot> chain)
        {
            foreach (var snapshot in chain)
            {
                string? sid = FindUntrustedWriter(snapshot);
                if (sid != null) return (snapshot.Path, sid);
            }
            return null;
        }
    }
}
