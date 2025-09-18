using System;
using System.Threading;
using System.Windows.Threading;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Interfaces.Services;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// WPF implementation of IStatusNotifier that integrates with the existing status system
    /// Routes status messages through IStateManager to the UI TextBlock
    /// </summary>
    public class WPFStatusNotifier : IStatusNotifier
    {
        private readonly IStateManager _stateManager;
        private readonly Dispatcher _dispatcher;
        private Timer? _clearTimer;
        private readonly object _timerLock = new object();

        public WPFStatusNotifier(IStateManager stateManager)
        {
            _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
            _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        /// <summary>
        /// Show status message with automatic clearing
        /// </summary>
        public void ShowStatus(string message, StatusType type, int duration = 3000)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            // Get appropriate icon for message type
            var icon = GetIconForType(type);
            var formattedMessage = $"{icon} {message}";

            // Update status on UI thread
            _dispatcher.BeginInvoke(() =>
            {
                _stateManager.StatusMessage = formattedMessage;
            });

            // Set up auto-clear timer if duration is specified
            if (duration > 0)
            {
                SetupClearTimer(duration);
            }
        }

        /// <summary>
        /// Get appropriate icon emoji for status type
        /// </summary>
        private string GetIconForType(StatusType type)
        {
            return type switch
            {
                StatusType.Success => "✅",
                StatusType.Error => "❌",
                StatusType.Warning => "⚠️",
                StatusType.InProgress => "💾",
                StatusType.Info => "ℹ️",
                _ => ""
            };
        }

        /// <summary>
        /// Setup timer to clear status message after specified duration
        /// </summary>
        private void SetupClearTimer(int duration)
        {
            lock (_timerLock)
            {
                // Cancel existing timer
                _clearTimer?.Dispose();

                // Create new timer to clear status
                _clearTimer = new Timer(state =>
                {
                    _dispatcher.BeginInvoke(() =>
                    {
                        _stateManager.StatusMessage = "Ready";
                    });

                    // Clean up timer
                    lock (_timerLock)
                    {
                        _clearTimer?.Dispose();
                        _clearTimer = null;
                    }
                }, null, duration, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Dispose pattern implementation
        /// </summary>
        public void Dispose()
        {
            lock (_timerLock)
            {
                _clearTimer?.Dispose();
                _clearTimer = null;
            }
        }
    }
}
