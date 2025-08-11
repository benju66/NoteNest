using System;
using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Threading;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI
{
    public partial class App : Application
    {
        private IAppLogger _logger;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Initialize logging first
            _logger = AppLogger.Instance;
            _logger.Info("Application starting up");

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
                _logger.Fatal(ex, "Failed to create required directories");
                MessageBox.Show(
                    "Failed to initialize application directories. Please check permissions.",
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _logger.Info("Application shutting down");

            try
            {
                // Dispose of main window resources
                if (MainWindow != null)
                {
                    var viewModel = MainWindow.DataContext as ViewModels.MainViewModel;
                    viewModel?.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during shutdown");
            }
            finally
            {
                // Dispose logger last
                (_logger as IDisposable)?.Dispose();
            }

            base.OnExit(e);
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            _logger.Fatal(exception, "Unhandled exception occurred");

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
            _logger.Error(e.Exception, "Unhandled dispatcher exception");

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