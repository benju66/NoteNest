using System;
using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using ModernWpf;
using NoteNest.UI.Services;

namespace NoteNest.UI
{
    public partial class App : Application
    {
        private IAppLogger _logger;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // Set shutdown mode to ensure clean exit when main window closes
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                
                // Initialize logging first
                _logger = AppLogger.Instance;
                _logger.Info("Application starting up");

                // Initialize ModernWPF theme from settings
                try
                {
                    ThemeService.Initialize();
                }
                catch (Exception themeEx)
                {
                    _logger?.Warning($"Theme initialization failed: {themeEx.Message}");
                    // Continue with default theme
                }

                // Ensure a MainWindow is created and shown in case StartupUri didn't materialize due to an error
                if (Current.MainWindow == null)
                {
                    try
                    {
                        var mainWindow = new MainWindow();
                        Current.MainWindow = mainWindow;
                        mainWindow.Show();
                    }
                    catch (Exception ex)
                    {
                        _logger?.Fatal(ex, "Failed to create MainWindow");
                        ShowDetailedError("Failed to create MainWindow", ex);
                        Shutdown(1);
                    }
                }

                // Set up global exception handlers
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                DispatcherUnhandledException += OnDispatcherUnhandledException;

                // Ensure all required directories exist
                try
                {
                    PathService.EnsureDirectoriesExist();
                    _logger.Info($"Data directory: {PathService.RootPath}");
                    _logger.Info($"Settings directory: {PathService.AppDataPath}");
                }
                catch (Exception ex)
                {
                    _logger?.Fatal(ex, "Failed to create required directories");
                    ShowDetailedError("Failed to create required directories", ex);
                    Shutdown(1);
                }
            }
            catch (Exception ex)
            {
                // If logger fails, show detailed error
                ShowDetailedError("Failed to initialize logging", ex);
                Shutdown(1);
            }
        }

        private void ShowDetailedError(string context, Exception ex)
        {
            var details = $"{context}\n\n" +
                         $"Error Type: {ex.GetType().FullName}\n" +
                         $"Message: {ex.Message}\n\n" +
                         $"Stack Trace:\n{ex.StackTrace}\n\n";

            if (ex.InnerException != null)
            {
                details += $"Inner Exception:\n" +
                          $"Type: {ex.InnerException.GetType().FullName}\n" +
                          $"Message: {ex.InnerException.Message}\n" +
                          $"Stack: {ex.InnerException.StackTrace}\n\n";
            }

            // Try to write to a fallback log file on desktop
            try
            {
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var errorFile = Path.Combine(desktopPath, $"NoteNest_Error_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(errorFile, details);
                details += $"Error details saved to:\n{errorFile}\n\n";
            }
            catch
            {
                // If we can't write to desktop, just show the error
            }

            // Also try to show log location
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NoteNest",
                    "Logs");
                details += $"Check logs at:\n{logPath}";
            }
            catch
            {
                // Ignore if we can't get log path
            }

            MessageBox.Show(
                details,
                "NoteNest Startup Error - Detailed Information",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _logger?.Info("Application shutting down");

            try
            {
                // Quick cleanup - don't wait for complex operations
                if (MainWindow != null)
                {
                    try
                    {
                        var mainPanel = MainWindow.FindName("MainPanel") as Controls.NoteNestPanel;
                        var viewModel = mainPanel?.DataContext as ViewModels.MainViewModel;
                        if (viewModel != null)
                        {
                            _logger?.Info("Disposing MainViewModel from App.OnExit");
                            // Dispose synchronously but don't wait for long operations
                            viewModel.Dispose();
                        }
                        else
                        {
                            _logger?.Warning("Could not find MainViewModel to dispose");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"Error disposing MainViewModel: {ex.Message}");
                        // Continue shutdown anyway
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error during shutdown cleanup");
                // Continue shutdown anyway
            }
            finally
            {
                // Dispose logger last
                try
                {
                    (_logger as IDisposable)?.Dispose();
                }
                catch
                {
                    // Ignore logger disposal errors during shutdown
                }
            }

            base.OnExit(e);
            
            // If normal shutdown doesn't work within reasonable time, force exit
            _ = System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ => 
            {
                try
                {
                    System.Environment.Exit(e.ApplicationExitCode);
                }
                catch
                {
                    // Final safety net
                }
            });
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            
            if (_logger != null)
            {
                _logger.Fatal(exception, "Unhandled exception occurred");
            }
            else
            {
                ShowDetailedError("Unhandled exception (no logger available)", exception);
            }

            MessageBox.Show(
                $"An unexpected error occurred. The application will now close.\n\n" +
                $"Error: {exception?.Message}\n\n" +
                $"Please check the log file for details.",
                "Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            if (_logger != null)
            {
                _logger.Error(e.Exception, "Unhandled dispatcher exception");
            }
            else
            {
                ShowDetailedError("Dispatcher exception (no logger available)", e.Exception);
            }

            // Attempt to recover from UI exceptions
            MessageBox.Show(
                $"An error occurred: {e.Exception.Message}\n\n" +
                "The application will attempt to continue.",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            // Prevent application crash
            e.Handled = true;
        }
    }
}