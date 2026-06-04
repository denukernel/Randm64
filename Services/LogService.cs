using System;
using System.IO;
using System.Collections.Generic;

namespace Sm64DecompLevelViewer.Services
{
    public static class LogService
    {
        private static readonly string _logFilePath;
        private static readonly List<string> _history = new();
        public static IReadOnlyList<string> History => _history;

        static LogService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appDir = Path.Combine(appData, "Sm64DecompLevelViewer");
            if (!Directory.Exists(appDir)) Directory.CreateDirectory(appDir);
            
            _logFilePath = Path.Combine(appDir, "editor_logs.txt");
            
            // Clear old logs on startup
            try { File.WriteAllText(_logFilePath, $"--- Session Started {DateTime.Now} ---\n"); } catch { }
        }

        public static void Log(string message)
        {
            string logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _history.Add(logEntry);
            
            try
            {
                File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
            }
            catch { }
            
            Console.WriteLine(logEntry);
        }

        public static string GetLogPath() => _logFilePath;
    }
}
