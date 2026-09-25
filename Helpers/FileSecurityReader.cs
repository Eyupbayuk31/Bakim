using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Bakım.Core.Security;

namespace Bakım.Helpers
{
    /// <summary>Dosya/klasör ACL'sini <see cref="SecuritySnapshot"/>'a çevirir (Windows).</summary>
    public static class FileSecurityReader
    {
        /// <summary>Okunamazsa null (erişim yoksa güvenli sayılmamalı).</summary>
        public static SecuritySnapshot? TryRead(string path)
        {
            try
            {
                FileSystemSecurity security = File.Exists(path)
                    ? new FileInfo(path).GetAccessControl()
                    : new DirectoryInfo(path).GetAccessControl();

                string? owner = (security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier)?.Value;
                var entries = new List<AccessEntry>();
                foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                {
                    entries.Add(new AccessEntry(
                        ((SecurityIdentifier)rule.IdentityReference).Value,
                        (int)rule.FileSystemRights,
                        rule.AccessControlType == AccessControlType.Allow,
                        rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)));
                }
                return new SecuritySnapshot(path, owner, entries);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException or SystemException)
            {
                return null;
            }
        }

        /// <summary>SID'i "BUILTIN\Users" gibi okunur ada çevirir; çevrilemezse SID'i döndürür.</summary>
        public static string DescribeSid(string sid)
        {
            try
            {
                return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
            }
            catch
            {
                return sid;
            }
        }
    }
}
