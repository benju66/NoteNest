using System;
using System.IO;
using Serilog;
using Serilog.Core;

namespace NoteNest.Core.Services.Logging
{
    public class AppLogger : IAppLogger, IDisposable
    {
        private readonly Logger _logger;
        private static AppLogger _instance;
        private static readonly object _lock = new object();

        // Singleton instance for global access
        public static AppLogger Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new AppLogger();
                        }
                    }
                }
                return _instance;
            }
        }

        private AppLogger()
        {
            // Ensure log directory exists
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NoteNest",
                "Logs");
            
            Directory.CreateDirectory(logPath);
            
            var logFile = Path.Combine(logPath, "notenest-.log");

            _logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    logFile,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,  // Keep 7 days of logs
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Debug()  // Also output to debug console in development
                .CreateLogger();

            Info("NoteNest application started");
            Info($"Log files location: {logPath}");
        }

        public void Debug(string message, params object[] args)
        {
            _logger.Debug(message, args);
        }

        public void Info(string message, params object[] args)
        {
            _logger.Information(message, args);
        }

        public void Warning(string message, params object[] args)
        {
            _logger.Warning(message, args);
        }

        public void Error(string message, params object[] args)
        {
            _logger.Error(message, args);
        }

        public void Error(Exception ex, string message, params object[] args)
        {
            _logger.Error(ex, message, args);
        }

        public void Fatal(string message, params object[] args)
        {
            _logger.Fatal(message, args);
        }

        public void Fatal(Exception ex, string message, params object[] args)
        {
            _logger.Fatal(ex, message, args);
        }

        public void Dispose()
        {
            Info("NoteNest application shutting down");
            _logger?.Dispose();
        }
    }
}