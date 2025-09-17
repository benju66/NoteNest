using System;
using System.Threading;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.SaveCoordination
{
    /// <summary>
    /// Manages status bar updates for save operations
    /// Core logic only - UI threading handled by IStatusBarService implementation
    /// </summary>
    public class SaveStatusManager : IDisposable
    {
        private readonly NoteNest.Core.Services.IStatusBarService _statusBar;
        private readonly IAppLogger _logger;
        private readonly Timer _statusClearTimer;
        private bool _disposed = false;

        public SaveStatusManager(NoteNest.Core.Services.IStatusBarService statusBar, IAppLogger logger)
        {
            _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Timer for auto-clearing status messages
            _statusClearTimer = new Timer(ClearOldStatus, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Show save operation starting
        /// </summary>
        public void ShowSaveInProgress(string noteTitle)
        {
            if (_disposed) return;

            var truncatedTitle = TruncateTitle(noteTitle);
            var message = $"💾 Saving '{truncatedTitle}'...";
            
            UpdateStatusSafely(message, NoteNest.Core.Services.StatusType.Info);
            _logger.Debug($"Status: {message}");
        }

        /// <summary>
        /// Show successful save completion
        /// </summary>
        public void ShowSaveSuccess(string noteTitle)
        {
            if (_disposed) return;

            var truncatedTitle = TruncateTitle(noteTitle);
            var message = $"✅ Saved '{truncatedTitle}'";
            
            UpdateStatusSafely(message, NoteNest.Core.Services.StatusType.Info);
            _logger.Debug($"Status: {message}");
            
            // Auto-clear success message after 2 seconds
            _statusClearTimer.Change(2000, Timeout.Infinite);
        }

        /// <summary>
        /// Show save failure with retry indication
        /// </summary>
        public void ShowSaveFailure(string noteTitle, string error, bool isRetrying = false)
        {
            if (_disposed) return;

            var truncatedTitle = TruncateTitle(noteTitle);
            var truncatedError = TruncateError(error);
            
            string message;
            StatusType statusType;
            int clearDelayMs;

            if (isRetrying)
            {
                message = $"🔄 Retrying save for '{truncatedTitle}'...";
                statusType = NoteNest.Core.Services.StatusType.Warning;
                clearDelayMs = 1000; // Clear retry messages quickly
            }
            else
            {
                message = $"❌ Save failed: '{truncatedTitle}' - {truncatedError}";
                statusType = NoteNest.Core.Services.StatusType.Error;
                clearDelayMs = 10000; // Keep error messages visible longer
            }

            UpdateStatusSafely(message, statusType);
            _logger.Warning($"Status: {message}");
            
            // Auto-clear after appropriate delay
            _statusClearTimer.Change(clearDelayMs, Timeout.Infinite);
        }

        /// <summary>
        /// Show batch save progress
        /// </summary>
        public void ShowBatchSaveProgress(int completed, int total)
        {
            if (_disposed) return;

            string message;
            if (completed == total)
            {
                message = $"✅ Auto-saved {total} files";
                UpdateStatusSafely(message, NoteNest.Core.Services.StatusType.Info);
                // Clear after 3 seconds
                _statusClearTimer.Change(3000, Timeout.Infinite);
            }
            else
            {
                message = $"💾 Saving... {completed}/{total} files";
                UpdateStatusSafely(message, NoteNest.Core.Services.StatusType.Info);
                // Don't auto-clear progress messages
            }
            
            _logger.Debug($"Status: {message}");
        }

        /// <summary>
        /// Show general status message
        /// </summary>
        public void ShowStatus(string message, NoteNest.Core.Services.StatusType type = NoteNest.Core.Services.StatusType.Info, int clearAfterMs = 5000)
        {
            if (_disposed) return;

            UpdateStatusSafely(message, type);
            _logger.Debug($"Status: {message}");
            
            if (clearAfterMs > 0)
            {
                _statusClearTimer.Change(clearAfterMs, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Clear status message immediately
        /// </summary>
        public void ClearStatus()
        {
            if (_disposed) return;

            UpdateStatusSafely("Ready", NoteNest.Core.Services.StatusType.Info);
        }

        /// <summary>
        /// Update status bar - threading handled by IStatusBarService implementation
        /// </summary>
        private void UpdateStatusSafely(string message, NoteNest.Core.Services.StatusType type)
        {
            try
            {
                _statusBar.SetMessage(message, type);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to update status bar: {ex.Message}");
            }
        }

        /// <summary>
        /// Timer callback to clear old status messages
        /// </summary>
        private void ClearOldStatus(object state)
        {
            if (!_disposed)
            {
                ClearStatus();
            }
        }

        /// <summary>
        /// Truncate long titles for status bar display
        /// </summary>
        private string TruncateTitle(string title, int maxLength = 30)
        {
            if (string.IsNullOrEmpty(title))
                return "Unknown";
                
            return title.Length <= maxLength ? title : title.Substring(0, maxLength - 3) + "...";
        }

        /// <summary>
        /// Truncate long error messages for status bar display
        /// </summary>
        private string TruncateError(string error, int maxLength = 50)
        {
            if (string.IsNullOrEmpty(error))
                return "Unknown error";
                
            return error.Length <= maxLength ? error : error.Substring(0, maxLength - 3) + "...";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _statusClearTimer?.Dispose();
                ClearStatus();
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Error during SaveStatusManager disposal: {ex.Message}");
            }
        }
    }
}
