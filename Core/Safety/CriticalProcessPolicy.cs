using System;
using System.Collections.Generic;

namespace Bakım.Core.Safety
{
    /// <summary>
    /// Sonlandırılması ya da askıya alınması sistemi kilitleyebilecek veya
    /// oturumu sonlandırabilecek süreçlerin TEK listesi. Önceden Optimizer,
    /// temizleyici ve kaldırıcıda dört farklı (ve eksik) liste vardı; ör.
    /// winlogon hiçbirinde yoktu ve askıya alınabiliyordu.
    /// </summary>
    public static class CriticalProcessPolicy
    {
        private static readonly HashSet<string> CriticalNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "system", "idle", "registry", "memory compression", "secure system",
            "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "lsm",
            "svchost", "fontdrvhost", "dwm", "sihost", "ctfmon", "spoolsv",
            "msmpeng", "nissrv", "mpdefendercoreservice", "securityhealthservice", "securityhealthsystray",
            "searchhost", "searchindexer", "startmenuexperiencehost", "shellexperiencehost",
            "textinputhost", "runtimebroker", "audiodg", "conhost", "wudfhost", "taskhostw",
            "lockapp", "logonui", "userinit", "dllhost", "wmiprvse", "trustedinstaller", "tiworker",
            "explorer",
        };

        /// <summary>Ad listede mi? (".exe" uzantısı yok sayılır)</summary>
        public static bool IsCriticalName(string? processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return false;
            string name = processName.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            return CriticalNames.Contains(name);
        }

        /// <summary>
        /// Süreç korumalı mı? Ada ek olarak Windows klasöründen çalışan her süreç
        /// ve Bakım'ın kendisi korunur.
        /// </summary>
        /// <param name="imagePath">Sürecin tam yolu (bilinmiyorsa null).</param>
        /// <param name="windowsDirectory">Windows klasörü (ör. C:\Windows).</param>
        public static bool IsProtected(int processId, string? processName, string? imagePath, string? windowsDirectory, int currentProcessId)
        {
            if (processId <= 4 || processId == currentProcessId) return true;
            if (IsCriticalName(processName)) return true;

            if (!string.IsNullOrWhiteSpace(imagePath) && !string.IsNullOrWhiteSpace(windowsDirectory))
            {
                string? img = WindowsPath.Normalize(imagePath);
                string? win = WindowsPath.Normalize(windowsDirectory);
                if (img != null && win != null && WindowsPath.IsUnderOrEqual(img, win)) return true;
            }

            return false;
        }

        public static IReadOnlyCollection<string> Names => CriticalNames;
    }
}
