using System;
using System.IO;
using Serilog;
using Serilog.Core;

namespace NoteNest.Core.Services.Logging
{
    public class AppLogger : IAppLogger, IDisposable
    {
        private readonly Logger _logger;
        private static readonly Lazy<AppLogger> _instance = 
            new Lazy<AppLogger>(() => new AppLogger(), isThreadSafe: true);
        private bool _disposed;


        // Thread-safe singleton instance
        public static AppLogger Instance => _instance.Value;

        private AppLogger()
        {
            try
            {
                // Try multiple locations for log files
                string logPath = GetLogPath();
                
                var logFile = Path.Combine(logPath, "notenest-.log");

                var logConfig = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.Debug(); // Always write to debug output

                // Try to add file logging
                try
                {
                    logConfig = logConfig.WriteTo.File(
                        logFile,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 7,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
                    
                    _logger = logConfig.CreateLogger();

                    
                    Info("NoteNest application started");
                    Info($"Log files location: {logPath}");
                }
                catch (Exception fileEx)
                {
                    // If file logging fails, just use debug output
                    _logger = logConfig.CreateLogger();

                    
                    Warning($"Could not initialize file logging: {fileEx.Message}");
                    Info("NoteNest application started (debug output only)");
                }
            }
            catch (Exception ex)
            {
                // If everything fails, create a minimal logger
                _logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.Debug()
                    .CreateLogger();
                

                Error($"Failed to properly initialize logger: {ex.Message}");
            }
        }

        private string GetLogPath()
        {
            // Try LocalApplicationData first
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NoteNest",
                    "Logs");
                
                Directory.CreateDirectory(logPath);
                
                // Test if we can write to this directory
                var testFile = Path.Combine(logPath, ".test");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                
                return logPath;
            }
            catch
            {
                // LocalApplicationData failed
            }

            // Try user profile
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".notenest",
                    "logs");
                
                Directory.CreateDirectory(logPath);
                
                // Test if we can write to this directory
                var testFile = Path.Combine(logPath, ".test");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                
                return logPath;
            }
            catch
            {
                // User profile failed
            }

            // Try desktop as last resort
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "NoteNest_Logs");
                
                Directory.CreateDirectory(logPath);
                return logPath;
            }
            catch
            {
                // Even desktop failed, use current directory
                return Directory.GetCurrentDirectory();
            }
        }

        public void Debug(string message, params object[] args)
        {
            if (_disposed || _logger == null) return;
            try
            {
                _logger.Debug(message, args);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public void Info(string message, params object[] args)
        {
            if (_disposed || _logger == null) return;
            try
            {
                _logger.Information(message, args);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public void Warning(string message, params object[] args)
        {
            if (_disposed || _logger == null) return;
            try
            {
                _logger.Warning(message, args);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public void Error(string message, params object[] args)
        {
            if (_disposed || _logger == null) return;
            try
            {
                _logger.Error(message, args);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public void Error(Exception ex, string message, params object[] args)
        {
            if (_disposed || _logger == null) return;
            try
            {
                _logger.Error(ex, message, args);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public void Fatal(string message, params object[] args)
        {
            if (_disposed || _logger == null) return;
            try
            {
                _logger.Fatal(message, args);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public void Fatal(Exception ex, string message, params object[] args)
        {
            if (_disposed || _logger == null) return;
            try
            {
                _logger.Fatal(ex, message, args);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Info("NoteNest application shutting down");
                    _logger?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}