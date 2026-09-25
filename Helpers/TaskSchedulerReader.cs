using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Bakım.Services;

namespace Bakım.Helpers
{
    /// <summary>Zamanlanmış görev ve çalıştırdığı komutlar.</summary>
    public sealed record ScheduledTaskInfo(string Path, string Name, IReadOnlyList<string> ExecActions);

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

        private static void Walk(dynamic folder, List<ScheduledTaskInfo> list, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;

            // 1 = TASK_ENUM_HIDDEN: gizli görevler de listelenir.
            foreach (dynamic task in folder.GetTasks(1))
            {
                try
                {
                    var actions = new List<string>();
                    foreach (dynamic action in task.Definition.Actions)
                    {
                        // 0 = TASK_ACTION_EXEC
                        if ((int)action.Type != 0) continue;
                        string path = action.Path ?? string.Empty;
                        string args = action.Arguments ?? string.Empty;
                        actions.Add(args.Length > 0 ? $"{path} {args}" : path);
                    }
                    list.Add(new ScheduledTaskInfo((string)task.Path, (string)task.Name, actions));
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
