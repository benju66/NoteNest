using System;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Simple console logger implementation for development and testing
    /// </summary>
    public class ConsoleAppLogger : IAppLogger
    {
        private readonly string _name;
        
        public ConsoleAppLogger(string name = "ConsoleLogger")
        {
            _name = name;
        }

        public void Debug(string message, params object[] args)
        {
            WriteLog("DEBUG", message, args);
        }

        public void Info(string message, params object[] args)
        {
            WriteLog("INFO", message, args);
        }

        public void Warning(string message, params object[] args)
        {
            WriteLog("WARN", message, args);
        }

        public void Error(string message, params object[] args)
        {
            WriteLog("ERROR", message, args);
        }

        public void Error(Exception exception, string message, params object[] args)
        {
            WriteLog("ERROR", message, args);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{_name}] [ERROR] Exception: {exception}");
        }

        public void Fatal(string message, params object[] args)
        {
            WriteLog("FATAL", message, args);
        }

        public void Fatal(Exception exception, string message, params object[] args)
        {
            WriteLog("FATAL", message, args);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{_name}] [FATAL] Exception: {exception}");
        }

        private void WriteLog(string level, string message, params object[] args)
        {
            try
            {
                var formattedMessage = args?.Length > 0 
                    ? string.Format(message, args) 
                    : message;
                    
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{_name}] [{level}] {formattedMessage}");
            }
            catch (FormatException)
            {
                // Fallback if string.Format fails
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{_name}] [{level}] {message}");
            }
        }
    }
}
