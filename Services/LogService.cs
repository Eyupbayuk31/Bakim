using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Bakım.Services
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    public interface ILogService
    {
        string LogDirectory { get; }
        string CurrentLogFile { get; }
        LogLevel MinimumLevel { get; set; }

        void Debug(string message, string? category = null);
        void Info(string message, string? category = null);
        void Warning(string message, Exception? exception = null, string? category = null);
        void Error(string message, Exception? exception = null, string? category = null);
        void Write(LogLevel level, string message, Exception? exception = null, string? category = null);

        IReadOnlyList<string> ReadRecent(int maxLines = 300);
    }

    /// <summary>
    /// Bağımlılıksız, thread-safe, günlük döngülü (rolling) dosya günlükleyici.
    /// %LocalAppData%\Bakim\Logs\bakim-yyyy-MM-dd.log konumuna yazar ve
    /// RetentionDays gününden eski dosyaları açılışta temizler.
    /// Hiçbir koşulda istisna fırlatmaz: günlükleme, uygulamayı asla çökertmemelidir.
    /// </summary>
    public sealed class FileLogService : ILogService, IDisposable
    {
        private const int RetentionDays = 7;
        private const long MaxFileBytes = 8 * 1024 * 1024; // 8 MB üstünde parçala

        private readonly object _gate = new();
        private readonly string _logDirectory;

        private StreamWriter? _writer;
        private DateTime _currentFileDate = DateTime.MinValue;
        private string _currentLogFile = string.Empty;
        private int _rollIndex;
        private bool _disposed;

        public FileLogService()
        {
            _logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Bakim",
                "Logs");

            try
            {
                Directory.CreateDirectory(_logDirectory);
                PurgeOldLogs();
            }
            catch
            {
                // Günlük klasörü oluşturulamadıysa günlükleme sessizce devre dışı kalır.
            }
        }

        public string LogDirectory => _logDirectory;

        public string CurrentLogFile
        {
            get { lock (_gate) { return _currentLogFile; } }
        }

        public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

        public void Debug(string message, string? category = null)
            => Write(LogLevel.Debug, message, null, category);

        public void Info(string message, string? category = null)
            => Write(LogLevel.Info, message, null, category);

        public void Warning(string message, Exception? exception = null, string? category = null)
            => Write(LogLevel.Warning, message, exception, category);

        public void Error(string message, Exception? exception = null, string? category = null)
            => Write(LogLevel.Error, message, exception, category);

        public void Write(LogLevel level, string message, Exception? exception = null, string? category = null)
        {
            if (level < MinimumLevel) return;

            try
            {
                string line = Format(level, message, exception, category);

                lock (_gate)
                {
                    if (_disposed) return;

                    EnsureWriter();
                    if (_writer == null) return;

                    _writer.Write(line);
                    // Her satırda flush: bakım aracı süreci beklenmedik şekilde sonlanırsa
                    // son satırlar kaybolmamalı.
                    _writer.Flush();
                }
            }
            catch
            {
                // Günlükleme hatası yutulur.
            }
        }

        public IReadOnlyList<string> ReadRecent(int maxLines = 300)
        {
            try
            {
                string path;
                lock (_gate)
                {
                    EnsureWriter();
                    _writer?.Flush();
                    path = _currentLogFile;
                }

                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return Array.Empty<string>();

                // Yazarken bile okunabilsin diye paylaşımlı erişim
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                var buffer = new Queue<string>(maxLines);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (buffer.Count == maxLines) buffer.Dequeue();
                    buffer.Enqueue(line);
                }

                return new List<string>(buffer);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string Format(LogLevel level, string message, Exception? exception, string? category)
        {
            string tag = level switch
            {
                LogLevel.Debug => "DBG",
                LogLevel.Info => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                _ => "INF"
            };

            var sb = new StringBuilder(256);
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
              .Append(" [").Append(tag).Append(']')
              .Append(" [T").Append(Environment.CurrentManagedThreadId).Append(']');

            if (!string.IsNullOrWhiteSpace(category))
                sb.Append(" [").Append(category).Append(']');

            sb.Append(' ').AppendLine(message);

            if (exception != null)
            {
                sb.Append("    -> ").Append(exception.GetType().Name).Append(": ").AppendLine(exception.Message);

                if (exception.InnerException != null)
                {
                    sb.Append("    -> iç istisna: ")
                      .Append(exception.InnerException.GetType().Name).Append(": ")
                      .AppendLine(exception.InnerException.Message);
                }

                if (!string.IsNullOrWhiteSpace(exception.StackTrace))
                {
                    foreach (var frame in exception.StackTrace.Split('\n'))
                    {
                        string trimmed = frame.TrimEnd('\r');
                        if (trimmed.Length > 0) sb.Append("       ").AppendLine(trimmed.Trim());
                    }
                }
            }

            return sb.ToString();
        }

        /// <summary>Gün değiştiyse veya dosya çok büyüdüyse yeni dosyaya geçer. _gate altında çağrılmalıdır.</summary>
        private void EnsureWriter()
        {
            var today = DateTime.Now.Date;

            bool needsRoll = _writer == null || _currentFileDate != today;

            if (!needsRoll && _writer != null)
            {
                try
                {
                    if (_writer.BaseStream.Length > MaxFileBytes)
                    {
                        _rollIndex++;
                        needsRoll = true;
                    }
                }
                catch
                {
                    needsRoll = true;
                }
            }

            if (!needsRoll) return;

            try
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch { /* kapatma hatası önemsiz */ }
            _writer = null;

            if (_currentFileDate != today)
            {
                _rollIndex = 0;
                _currentFileDate = today;
            }

            try
            {
                Directory.CreateDirectory(_logDirectory);

                string suffix = _rollIndex == 0 ? string.Empty : $"_{_rollIndex}";
                _currentLogFile = Path.Combine(_logDirectory, $"bakim-{today:yyyy-MM-dd}{suffix}.log");

                var stream = new FileStream(_currentLogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                {
                    AutoFlush = false
                };
            }
            catch
            {
                _writer = null;
            }
        }

        private void PurgeOldLogs()
        {
            try
            {
                var cutoff = DateTime.Now.Date.AddDays(-RetentionDays);
                foreach (string file in Directory.GetFiles(_logDirectory, "bakim-*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file).Date < cutoff)
                            File.Delete(file);
                    }
                    catch { /* kilitli dosya atlanır */ }
                }
            }
            catch { /* klasör yoksa yapacak bir şey yok */ }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;

                try
                {
                    _writer?.Flush();
                    _writer?.Dispose();
                }
                catch { }
                _writer = null;
            }
        }
    }

    /// <summary>Hiçbir şey yazmayan günlükleyici: tasarım zamanı ve yedek senaryolar için.</summary>
    public sealed class NullLogService : ILogService
    {
        public static readonly NullLogService Instance = new();

        public string LogDirectory => string.Empty;
        public string CurrentLogFile => string.Empty;
        public LogLevel MinimumLevel { get; set; } = LogLevel.Error;

        public void Debug(string message, string? category = null) { }
        public void Info(string message, string? category = null) { }
        public void Warning(string message, Exception? exception = null, string? category = null) { }
        public void Error(string message, Exception? exception = null, string? category = null) { }
        public void Write(LogLevel level, string message, Exception? exception = null, string? category = null) { }
        public IReadOnlyList<string> ReadRecent(int maxLines = 300) => Array.Empty<string>();
    }

    /// <summary>
    /// Statik servislerin (DI ile örneklenemeyen) günlüğe erişebilmesi için ince cephe.
    /// App.OnStartup içinde gerçek günlükleyiciye bağlanır.
    /// </summary>
    public static class AppLog
    {
        private static ILogService _current = NullLogService.Instance;

        public static ILogService Current => _current;

        public static void Initialize(ILogService logService)
        {
            _current = logService ?? NullLogService.Instance;
        }

        public static void Debug(string message, string? category = null) => _current.Debug(message, category);
        public static void Info(string message, string? category = null) => _current.Info(message, category);
        public static void Warning(string message, Exception? ex = null, string? category = null) => _current.Warning(message, ex, category);
        public static void Error(string message, Exception? ex = null, string? category = null) => _current.Error(message, ex, category);
    }
}
