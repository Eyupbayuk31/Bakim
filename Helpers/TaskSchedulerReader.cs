using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Bakım.Services;

namespace Bakım.Helpers
{
    /// <summary>Görev eylemi: çalıştırılan dosya ve argümanları ayrı (yol boşluk içerebilir).</summary>
    public sealed record TaskAction(string Path, string Arguments);

    /// <summary>Zamanlanmış görev ve çalıştırdığı komutlar.</summary>
    /// <param name="ExecActions">"yol argüman" biçiminde birleşik eylemler (Nöbetçi ve kaldırıcı izleri için).</param>
    /// <param name="Actions">Yol ve argümanı ayrı eylemler (Analizör).</param>
    /// <param name="ComHandlers">COM işleyici eylemlerinin CLSID'leri.</param>
    public sealed record ScheduledTaskInfo(
        string Path,
        string Name,
        IReadOnlyList<string> ExecActions,
        bool Enabled = true,
        bool Hidden = false,
        string? Author = null,
        IReadOnlyList<TaskAction>? Actions = null,
        IReadOnlyList<string>? ComHandlers = null);

    /// <summary>
    /// Görev Zamanlayıcı'yı COM (Schedule.Service) ile okur; yönetici gerektirmez (erişilemeyen
    /// korumalı görevler atlanır). Kaldırıcı iz toplama ve Kurulum Nöbetçisi aynı okuyucuyu kullanır.
    /// </summary>
    public static class TaskSchedulerReader
    {
        public static IReadOnlyList<ScheduledTaskInfo> ReadAll(int maxDepth = 8)
        {
            var list = new List<ScheduledTaskInfo>();
            object? service = Connect();
            if (service == null) return list;
            try
            {
                dynamic ts = service;
                Walk(ts.GetFolder("\\"), list, 0, maxDepth);
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                AppLog.Debug($"Görev kökü okunamadı: {ex.Message}", nameof(TaskSchedulerReader));
            }
            finally
            {
                Marshal.ReleaseComObject(service);
            }
            return list;
        }

        public static object? Connect()
        {
            try
            {
                Type? type = Type.GetTypeFromProgID("Schedule.Service");
                if (type == null) return null;
                object service = Activator.CreateInstance(type)!;
                ((dynamic)service).Connect();
                return service;
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                AppLog.Warning("Görev Zamanlayıcı'ya bağlanılamadı.", ex, nameof(TaskSchedulerReader));
                return null;
            }
        }

        /// <summary>İsteğe bağlı bir görev özelliğini okur; okunamazsa varsayılan döner.</summary>
        private static T TryRead<T>(Func<T> read, T fallback)
        {
            try
            {
                return read();
            }
            catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException)
            {
                AppLog.Debug($"Görev özelliği okunamadı: {ex.Message}", nameof(TaskSchedulerReader));
                return fallback;
            }
        }

        private static void Walk(dynamic folder, List<ScheduledTaskInfo> list, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;

            // 1 = TASK_ENUM_HIDDEN: gizli görevler de listelenir.
            foreach (dynamic task in folder.GetTasks(1))
            {
                try
                {
                    var actions = new List<string>();
                    var split = new List<TaskAction>();
                    var handlers = new List<string>();
                    dynamic definition = task.Definition;
                    foreach (dynamic action in definition.Actions)
                    {
                        int type = (int)action.Type;
                        if (type == 0) // TASK_ACTION_EXEC
                        {
                            string path = action.Path ?? string.Empty;
                            string args = action.Arguments ?? string.Empty;
                            actions.Add(args.Length > 0 ? $"{path} {args}" : path);
                            split.Add(new TaskAction(path, args));
                        }
                        else if (type == 5) // TASK_ACTION_COM_HANDLER
                        {
                            string clsid = action.ClassId ?? string.Empty;
                            if (clsid.Length > 0) handlers.Add(clsid);
                        }
                    }
                    bool enabled = TryRead(() => (bool)task.Enabled, true);
                    bool hidden = TryRead(() => (bool)definition.Settings.Hidden, false);
                    string? author = TryRead<string?>(() => definition.RegistrationInfo.Author as string, null);
                    list.Add(new ScheduledTaskInfo((string)task.Path, (string)task.Name, actions, enabled, hidden, author, split, handlers));
                }
                catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    // Erişilemeyen tek görev taramayı durdurmaz (korumalı sistem görevleri).
                    AppLog.Debug($"Görev okunamadı: {ex.Message}", nameof(TaskSchedulerReader));
                }
            }

            foreach (dynamic sub in folder.GetFolders(0))
            {
                try
                {
                    Walk(sub, list, depth + 1, maxDepth);
                }
                catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
                {
                    AppLog.Debug($"Görev klasörü okunamadı: {ex.Message}", nameof(TaskSchedulerReader));
                }
            }
        }
    }
}
