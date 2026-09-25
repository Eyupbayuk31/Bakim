using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Bakım.Models;

namespace Bakım.Services
{
    public class TweakSnapshotItem
    {
        public string TweakId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool OriginalState { get; set; }
        public int OriginalNumericValue { get; set; }
        public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Değer düzeyinde yedek (H-13): Bakım bu ayarın dokunduğu kayıt defteri değerlerini ilk kez
        /// değiştirmeden önceki halleri. Doluysa geri yükleme bunları birebir yazar.
        /// </summary>
        public List<Bakım.Helpers.RegistryValueSnapshot> RegistryOriginals { get; set; } = new();
    }

    public class TweaksSnapshotData
    {
        public DateTime SnapshotDate { get; set; } = DateTime.UtcNow;
        public string WindowsVersion { get; set; } = Environment.OSVersion.VersionString;
        public string MachineName { get; set; } = Environment.MachineName;
        public Dictionary<string, TweakSnapshotItem> Items { get; set; } = new();
    }

    public interface ITweaksSnapshotService
    {
        Task EnsureInitialSnapshotAsync(IEnumerable<SystemTweakItem> currentTweaks);
        Task<bool> HasSnapshotAsync();
        Task<TweaksSnapshotData?> GetSnapshotAsync();
        Task RecordTweakBeforeChangeAsync(SystemTweakItem tweak);

        /// <summary>Uygulama sırasında yakalanan özgün kayıt defteri değerlerini ekler (ilk görülen korunur).</summary>
        Task RecordRegistryOriginalsAsync(SystemTweakItem tweak, IReadOnlyList<Bakım.Helpers.RegistryValueSnapshot> captured);
        Task<bool> RestoreFromSnapshotAsync(
            IEnumerable<SystemTweakItem> currentTweaks,
            Func<SystemTweakItem, bool, Task<bool>> applyToggleAction,
            Func<SystemTweakItem, Task<bool>>? applyNumericAction = null);
        Task<bool> RestartExplorerAsync();
        void BroadcastSettingsChange();
    }

    public class TweaksSnapshotService : ITweaksSnapshotService
    {
        private static readonly string StorageDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bakim");

        private static readonly string BackupFilePath = Path.Combine(StorageDirectory, "tweaks_backup.json");

        private readonly SemaphoreSlim _fileLock = new(1, 1);

        #region Win32 P/Invoke for System Notification Broadcast

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            UIntPtr wParam,
            string lParam,
            uint fuFlags,
            uint uTimeout,
            out UIntPtr lpdwResult);

        private static readonly IntPtr HWND_BROADCAST = new(0xffff);
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint WM_THEMECHANGED = 0x031A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        public void BroadcastSettingsChange()
        {
            try
            {
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 500, out _);
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "Policy", SMTO_ABORTIFHUNG, 500, out _);
                SendMessageTimeout(HWND_BROADCAST, WM_THEMECHANGED, UIntPtr.Zero, string.Empty, SMTO_ABORTIFHUNG, 500, out _);
            }
            catch { }
        }

        #endregion

        public async Task EnsureInitialSnapshotAsync(IEnumerable<SystemTweakItem> currentTweaks)
        {
            await _fileLock.WaitAsync();
            try
            {
                if (File.Exists(BackupFilePath)) return;

                if (!Directory.Exists(StorageDirectory))
                {
                    Directory.CreateDirectory(StorageDirectory);
                }

                var snapshot = new TweaksSnapshotData();
                foreach (var tweak in currentTweaks)
                {
                    if (string.IsNullOrWhiteSpace(tweak.Id)) continue;

                    snapshot.Items[tweak.Id] = new TweakSnapshotItem
                    {
                        TweakId = tweak.Id,
                        Title = tweak.Title,
                        Category = tweak.Category,
                        OriginalState = tweak.IsEnabled,
                        OriginalNumericValue = tweak.NumericValue,
                        CapturedAt = DateTime.UtcNow
                    };
                }

                string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(BackupFilePath, json);
            }
            catch { }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task<bool> HasSnapshotAsync()
        {
            return await Task.Run(() => File.Exists(BackupFilePath));
        }

        public async Task<TweaksSnapshotData?> GetSnapshotAsync()
        {
            await _fileLock.WaitAsync();
            try
            {
                if (!File.Exists(BackupFilePath)) return null;
                string json = await File.ReadAllTextAsync(BackupFilePath);
                return JsonSerializer.Deserialize<TweaksSnapshotData>(json);
            }
            catch
            {
                return null;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task RecordTweakBeforeChangeAsync(SystemTweakItem tweak)
        {
            if (string.IsNullOrWhiteSpace(tweak.Id)) return;

            await _fileLock.WaitAsync();
            try
            {
                TweaksSnapshotData data;
                if (File.Exists(BackupFilePath))
                {
                    string json = await File.ReadAllTextAsync(BackupFilePath);
                    data = JsonSerializer.Deserialize<TweaksSnapshotData>(json) ?? new TweaksSnapshotData();
                }
                else
                {
                    data = new TweaksSnapshotData();
                    if (!Directory.Exists(StorageDirectory))
                    {
                        Directory.CreateDirectory(StorageDirectory);
                    }
                }

                // Sadece daha önce yedeklenmemişse ilk halini kalıcı kaydet
                if (!data.Items.ContainsKey(tweak.Id))
                {
                    data.Items[tweak.Id] = new TweakSnapshotItem
                    {
                        TweakId = tweak.Id,
                        Title = tweak.Title,
                        Category = tweak.Category,
                        OriginalState = tweak.IsEnabled,
                        OriginalNumericValue = tweak.NumericValue,
                        CapturedAt = DateTime.UtcNow
                    };

                    string outJson = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(BackupFilePath, outJson);
                }
            }
            catch { }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task RecordRegistryOriginalsAsync(SystemTweakItem tweak, IReadOnlyList<Bakım.Helpers.RegistryValueSnapshot> captured)
        {
            if (string.IsNullOrWhiteSpace(tweak.Id) || captured.Count == 0) return;

            await _fileLock.WaitAsync();
            try
            {
                var data = File.Exists(BackupFilePath)
                    ? JsonSerializer.Deserialize<TweaksSnapshotData>(await File.ReadAllTextAsync(BackupFilePath)) ?? new TweaksSnapshotData()
                    : new TweaksSnapshotData();
                Directory.CreateDirectory(StorageDirectory);

                if (!data.Items.TryGetValue(tweak.Id, out var item))
                {
                    item = new TweakSnapshotItem
                    {
                        TweakId = tweak.Id,
                        Title = tweak.Title,
                        Category = tweak.Category,
                        OriginalState = !tweak.IsEnabled,
                        OriginalNumericValue = tweak.NumericValue
                    };
                    data.Items[tweak.Id] = item;
                }

                var known = new HashSet<string>(item.RegistryOriginals.Select(r => r.Identity), StringComparer.Ordinal);
                bool changed = false;
                foreach (var snapshot in captured)
                {
                    if (known.Add(snapshot.Identity))
                    {
                        item.RegistryOriginals.Add(snapshot);
                        changed = true;
                    }
                }

                if (changed)
                {
                    string outJson = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(BackupFilePath, outJson);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning($"İnce ayar değer yedeği yazılamadı: {tweak.Id}", ex, nameof(TweaksSnapshotService));
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task<bool> RestoreFromSnapshotAsync(
            IEnumerable<SystemTweakItem> currentTweaks,
            Func<SystemTweakItem, bool, Task<bool>> applyToggleAction,
            Func<SystemTweakItem, Task<bool>>? applyNumericAction = null)
        {
            var snapshot = await GetSnapshotAsync();
            if (snapshot == null || snapshot.Items.Count == 0) return false;

            bool allSuccess = true;
            foreach (var tweak in currentTweaks)
            {
                if (string.IsNullOrWhiteSpace(tweak.Id)) continue;

                if (snapshot.Items.TryGetValue(tweak.Id, out var itemBackup))
                {
                    // Değer düzeyinde yedek varsa özgün değerler birebir yazılır (açık/kapalı tahmini yerine).
                    if (itemBackup.RegistryOriginals.Count > 0)
                    {
                        bool restored = await Task.Run(() =>
                            itemBackup.RegistryOriginals.Aggregate(true, (ok, r) => Bakım.Helpers.RegistryCapture.Restore(r) && ok));
                        if (!restored) allSuccess = false;
                        continue;
                    }

                    if (tweak.Type == TweakType.Toggle && tweak.IsEnabled != itemBackup.OriginalState)
                    {
                        try
                        {
                            bool ok = await applyToggleAction(tweak, itemBackup.OriginalState);
                            if (!ok) allSuccess = false;
                        }
                        catch
                        {
                            allSuccess = false;
                        }
                    }
                    else if (tweak.Type == TweakType.Numeric && applyNumericAction != null && tweak.NumericValue != itemBackup.OriginalNumericValue)
                    {
                        try
                        {
                            tweak.NumericValue = itemBackup.OriginalNumericValue;
                            bool ok = await applyNumericAction(tweak);
                            if (!ok) allSuccess = false;
                        }
                        catch
                        {
                            allSuccess = false;
                        }
                    }
                }
            }

            BroadcastSettingsChange();
            return allSuccess;
        }

        public async Task<bool> RestartExplorerAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    foreach (var proc in Process.GetProcessesByName("explorer"))
                    {
                        try { proc.Kill(); proc.WaitForExit(2000); } catch { }
                    }

                    Thread.Sleep(500);

                    string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = explorerPath,
                        UseShellExecute = true
                    });

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }
    }
}
