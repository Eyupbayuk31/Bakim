using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IHunterService
    {
        HunterTargetInfo IdentifyTargetAtPoint(int screenX, int screenY, IEnumerable<InstalledAppItem>? installedApps = null);
        HunterTargetInfo IdentifyTargetAtCurrentCursor(IEnumerable<InstalledAppItem>? installedApps = null);
    }

    public class HunterService : IHunterService
    {
        #region Win32 P/Invoke & Structures

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        public const uint GA_ROOT = 2;
        public const uint GA_ROOTOWNER = 3;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        #endregion

        public HunterTargetInfo IdentifyTargetAtCurrentCursor(IEnumerable<InstalledAppItem>? installedApps = null)
        {
            if (GetCursorPos(out POINT curPt))
            {
                return IdentifyTargetAtPoint(curPt.x, curPt.y, installedApps);
            }
            return new HunterTargetInfo();
        }

        public HunterTargetInfo IdentifyTargetAtPoint(int screenX, int screenY, IEnumerable<InstalledAppItem>? installedApps = null)
        {
            var info = new HunterTargetInfo();

            try
            {
                var pt = new POINT { x = screenX, y = screenY };
                IntPtr hWnd = WindowFromPoint(pt);

                if (hWnd == IntPtr.Zero) return info;

                // 1. Resolve Root Window (Avoid stopping at child controls or canvas elements)
                IntPtr rootWnd = GetAncestor(hWnd, GA_ROOT);
                if (rootWnd != IntPtr.Zero && IsWindowVisible(rootWnd))
                {
                    hWnd = rootWnd;
                }

                info.WindowHandle = hWnd;

                // 2. Window Rect (Coordinates for real-time highlight frame)
                if (GetWindowRect(hWnd, out RECT rect))
                {
                    info.WindowLeft = rect.Left;
                    info.WindowTop = rect.Top;
                    info.WindowWidth = Math.Max(0, rect.Right - rect.Left);
                    info.WindowHeight = Math.Max(0, rect.Bottom - rect.Top);
                }

                // 3. Window Class
                var sbClass = new StringBuilder(256);
                GetClassName(hWnd, sbClass, 256);
                info.WindowClass = sbClass.ToString();

                // 4. Process ID & Self-Protection Check
                GetWindowThreadProcessId(hWnd, out uint processId);
                if (processId == 0) return info;

                info.ProcessId = (int)processId;

                // CRITICAL FIX: Ignore Bakım itself so it never targets itself!
                uint currentProcessId = (uint)Environment.ProcessId;
                if (processId == currentProcessId)
                {
                    info.IsSelfProcess = true;
                    info.ProcessName = "Bakım";
                    info.WindowTitle = "Bakım (Korumalı Sistem Aracı)";
                    try
                    {
                        info.ExecutablePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                    }
                    catch { }
                    return info;
                }

                // 5. Windows Shell / Desktop / Taskbar Protection
                string classLower = info.WindowClass.ToLowerInvariant();
                if (classLower == "progman" || classLower == "workerw" ||
                    classLower == "shell_traywnd" || classLower == "shell_secondarytraywnd")
                {
                    info.IsSystemShell = true;
                    info.ProcessName = "explorer";
                    info.WindowTitle = "Windows Masaüstü / Görev Çubuğu (Korumalı)";
                    return info;
                }

                // 6. Window Title
                var sbTitle = new StringBuilder(512);
                GetWindowText(hWnd, sbTitle, 512);
                info.WindowTitle = sbTitle.ToString();

                // 7. Executable Path
                string exePath = GetProcessExecutablePath(processId);
                info.ExecutablePath = exePath;

                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    info.ProcessName = Path.GetFileNameWithoutExtension(exePath);
                }
                else
                {
                    try
                    {
                        var proc = Process.GetProcessById((int)processId);
                        info.ProcessName = proc.ProcessName;
                    }
                    catch { }
                }

                if (string.Equals(info.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(info.WindowTitle))
                {
                    info.IsSystemShell = true;
                    info.WindowTitle = "Windows Gezgini (Sistem Çekirdeği)";
                }

                // 8. Match with Installed Applications
                if (installedApps != null && !string.IsNullOrWhiteSpace(info.ExecutablePath))
                {
                    info.MatchedApp = FindMatchingInstalledApp(info.ExecutablePath, info.ProcessName, installedApps);
                }
            }
            catch { }

            return info;
        }

        private static string GetProcessExecutablePath(uint processId)
        {
            // Primary method: QueryFullProcessImageName
            IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                    {
                        return sb.ToString();
                    }
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }

            // Secondary fallback: Process.MainModule
            try
            {
                using var proc = Process.GetProcessById((int)processId);
                return proc.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static InstalledAppItem? FindMatchingInstalledApp(string exePath, string procName, IEnumerable<InstalledAppItem> installedApps)
        {
            string exeDir = Path.GetDirectoryName(exePath) ?? string.Empty;
            string exeName = Path.GetFileName(exePath);

            // 1. Direct match on InstallLocation directory
            var dirMatch = installedApps.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(a.InstallLocation) &&
                exePath.StartsWith(a.InstallLocation.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            if (dirMatch != null) return dirMatch;

            // 2. Match on DisplayIcon containing the exe
            var iconMatch = installedApps.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(a.DisplayIconPath) &&
                a.DisplayIconPath.Contains(exeName, StringComparison.OrdinalIgnoreCase));
            if (iconMatch != null) return iconMatch;

            // 3. Match on DisplayName or Publisher
            var nameMatch = installedApps.FirstOrDefault(a =>
                a.DisplayName.Contains(procName, StringComparison.OrdinalIgnoreCase) ||
                procName.Contains(CleanToken(a.DisplayName), StringComparison.OrdinalIgnoreCase));
            if (nameMatch != null) return nameMatch;

            return null;
        }

        private static string CleanToken(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var parts = input.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : input;
        }
    }
}
