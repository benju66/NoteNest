using System;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Simple console logger for Clean Architecture testing
    /// </summary>
    public class ConsoleAppLogger : IAppLogger
    {
        public void Debug(string message) => Console.WriteLine($"[DEBUG] {message}");
        public void Info(string message) => Console.WriteLine($"[INFO] {message}");
        public void Warning(string message) => Console.WriteLine($"[WARN] {message}");
        public void Error(string message) => Console.WriteLine($"[ERROR] {message}");
        public void Error(Exception exception, string message) => Console.WriteLine($"[ERROR] {message}: {exception.Message}");
        public void Fatal(Exception exception, string message) => Console.WriteLine($"[FATAL] {message}: {exception.Message}");
    }
}
