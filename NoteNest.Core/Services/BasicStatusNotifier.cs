using System;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Basic status notifier implementation that logs to the application logger
    /// Used as a fallback when WPFStatusNotifier is not available
    /// </summary>
    public class BasicStatusNotifier : IStatusNotifier
    {
        private readonly IAppLogger _logger;

        public BasicStatusNotifier(IAppLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Show a status message by logging it
        /// </summary>
        public void ShowStatus(string message, StatusType type, int duration = 3000)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var prefix = GetPrefixForType(type);
            var formattedMessage = $"{prefix} {message}";

            switch (type)
            {
                case StatusType.Error:
                    _logger.Error(formattedMessage);
                    break;
                case StatusType.Warning:
                    _logger.Warning(formattedMessage);
                    break;
                case StatusType.Success:
                case StatusType.Info:
                    _logger.Info(formattedMessage);
                    break;
                case StatusType.InProgress:
                    _logger.Debug(formattedMessage);
                    break;
                default:
                    _logger.Info(formattedMessage);
                    break;
            }
        }

        /// <summary>
        /// Get appropriate prefix for status type
        /// </summary>
        private string GetPrefixForType(StatusType type)
        {
            return type switch
            {
                StatusType.Success => "[SUCCESS]",
                StatusType.Error => "[ERROR]",
                StatusType.Warning => "[WARNING]",
                StatusType.InProgress => "[PROGRESS]",
                StatusType.Info => "[INFO]",
                _ => "[STATUS]"
            };
        }

        /// <summary>
        /// Dispose pattern implementation - no resources to dispose
        /// </summary>
        public void Dispose()
        {
            // No resources to dispose
        }
    }
}
