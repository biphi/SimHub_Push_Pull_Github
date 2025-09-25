using System;
using System.IO;

namespace SimHub_Push_Pull_Github
{
    internal static class PluginLogger
    {
        private static readonly object _sync = new object();
        private static string _logFilePath;
        private static bool _initialized;

        public static void Initialize(string directory, string fileName = "plugin.log")
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            try
            {
                Directory.CreateDirectory(directory);
                _logFilePath = Path.Combine(directory, fileName);
                _initialized = true;
                Info("Logger initialized");
            }
            catch
            {
                // ignore logging init problems
            }
        }

        private static void Write(string level, string message, Exception ex = null)
        {
            if (!_initialized) return;
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                lock (_sync)
                {
                    File.AppendAllText(_logFilePath, line + Environment.NewLine);
                    if (ex != null)
                    {
                        File.AppendAllText(_logFilePath, ex.GetType().FullName + ": " + ex.Message + Environment.NewLine);
                        File.AppendAllText(_logFilePath, ex.StackTrace + Environment.NewLine);
                    }
                }
            }
            catch
            {
                // ignore logging write problems
            }
        }

        public static void Debug(string message) => Write("DBG", message);
        public static void Info(string message) => Write("INF", message);
        public static void Warn(string message) => Write("WRN", message);
        public static void Error(string message, Exception ex = null) => Write("ERR", message, ex);
    }
}
