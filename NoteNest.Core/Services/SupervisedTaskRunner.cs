using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Replaces all fire-and-forget patterns with supervised execution
    /// that guarantees user notification on failures
    /// </summary>
    public interface ISupervisedTaskRunner
    {
        Task RunAsync(Func<Task> operation, string operationName, 
                     OperationType type = OperationType.Background);
        
        SaveHealthStatus GetSaveHealth();
        event EventHandler<SaveFailureEventArgs> SaveFailed;
    }

    public enum OperationType
    {
        WALWrite,        // Critical - crash protection
        AutoSave,        // Critical - data persistence  
        TabSwitchSave,   // Critical - user expectation
        SearchIndex,     // Non-critical - can retry later
        Background       // General background operation
    }

    public class SaveFailureEventArgs : EventArgs
    {
        public string OperationName { get; set; } = string.Empty;
        public OperationType Type { get; set; }
        public Exception Exception { get; set; } = new Exception();
        public DateTime Timestamp { get; set; }
        public bool WasRetried { get; set; }
        public int RetryCount { get; set; }
    }

    public class SaveHealthStatus
    {
        private readonly object _lockObject = new();
        private volatile bool _walHealthy = true;
        private volatile bool _autoSaveHealthy = true;
        private int _successfulSaves;
        private int _failedSaves;
        private DateTime? _lastFailure;
        private string _lastFailureReason = string.Empty;

        public int SuccessfulSaves 
        { 
            get => _successfulSaves;
            set => Interlocked.Exchange(ref _successfulSaves, value);
        }
        
        public int FailedSaves 
        { 
            get => _failedSaves;
            set => Interlocked.Exchange(ref _failedSaves, value);
        }
        
        public DateTime? LastFailure 
        { 
            get { lock (_lockObject) { return _lastFailure; } }
            set { lock (_lockObject) { _lastFailure = value; } }
        }
        
        public string LastFailureReason 
        { 
            get { lock (_lockObject) { return _lastFailureReason; } }
            set { lock (_lockObject) { _lastFailureReason = value; } }
        }
        
        public bool WALHealthy
        {
            get => _walHealthy;
            set => _walHealthy = value;
        }
        
        public bool AutoSaveHealthy
        {
            get => _autoSaveHealthy;
            set => _autoSaveHealthy = value;
        }

        public void IncrementSuccess()
        {
            Interlocked.Increment(ref _successfulSaves);
        }

        public void IncrementFailure()
        {
            Interlocked.Increment(ref _failedSaves);
        }
    }

    public class SupervisedTaskRunner : ISupervisedTaskRunner
    {
        private readonly IAppLogger _logger;
        private readonly IUserNotificationService _notifications;
        private readonly ConcurrentQueue<SaveFailureEventArgs> _recentFailures = new();
        private readonly SaveHealthStatus _health = new();
        private DateTime _lastUserNotification = DateTime.MinValue;
        private const int MAX_RETRIES = 3;
        
        public event EventHandler<SaveFailureEventArgs>? SaveFailed;

        public SupervisedTaskRunner(IAppLogger logger, IUserNotificationService notifications)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        }

        public async Task RunAsync(Func<Task> operation, string operationName, 
                                  OperationType type = OperationType.Background)
        {
            int retryCount = 0;
            Exception? lastException = null;

            while (retryCount <= MAX_RETRIES)
            {
                try
                {
                    await operation();
                    
                    // Success - update health
                    _health.IncrementSuccess();
                    if (type == OperationType.WALWrite)
                        _health.WALHealthy = true;
                    if (type == OperationType.AutoSave)
                        _health.AutoSaveHealthy = true;
                    
                    return; // Success, exit
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.Error(ex, $"Operation failed: {operationName} (Attempt {retryCount + 1})");
                    
                    // Update health status
                    _health.IncrementFailure();
                    _health.LastFailure = DateTime.UtcNow;
                    _health.LastFailureReason = ex.Message;
                    
                    if (type == OperationType.WALWrite)
                        _health.WALHealthy = false;
                    if (type == OperationType.AutoSave)
                        _health.AutoSaveHealthy = false;
                    
                    // Determine if we should retry
                    if (ShouldRetry(ex, type) && retryCount < MAX_RETRIES)
                    {
                        retryCount++;
                        await Task.Delay(GetRetryDelay(retryCount));
                        continue;
                    }
                    
                    break; // No more retries
                }
            }

            // All retries failed - notify user
            if (lastException != null)
            {
                await NotifyUserOfFailure(operationName, type, lastException, retryCount);
            }
        }

        private bool ShouldRetry(Exception ex, OperationType type)
        {
            // Retry transient failures
            if (ex is IOException || ex is UnauthorizedAccessException)
                return true;
            
            // Critical operations always retry
            if (type == OperationType.WALWrite || type == OperationType.AutoSave)
                return true;
            
            return false;
        }

        private int GetRetryDelay(int retryCount)
        {
            // Exponential backoff: 100ms, 200ms, 400ms
            return 100 * (int)Math.Pow(2, retryCount - 1);
        }

        private async Task NotifyUserOfFailure(string operationName, OperationType type, 
                                              Exception exception, int retryCount)
        {
            var failure = new SaveFailureEventArgs
            {
                OperationName = operationName,
                Type = type,
                Exception = exception,
                Timestamp = DateTime.UtcNow,
                WasRetried = retryCount > 0,
                RetryCount = retryCount
            };

            _recentFailures.Enqueue(failure);
            SaveFailed?.Invoke(this, failure);

            // Determine notification strategy based on operation type
            switch (type)
            {
                case OperationType.WALWrite:
                    // Critical - always notify immediately
                    await _notifications.ShowErrorAsync(
                        "⚠️ Crash protection disabled - Your work is being saved but won't survive a crash. " +
                        $"Reason: {exception.Message}",
                        exception);
                    break;

                case OperationType.AutoSave:
                    // Critical - notify immediately
                    await _notifications.ShowErrorAsync(
                        "❌ Auto-save failed - Your changes are NOT saved. Please save manually (Ctrl+S). " +
                        $"Error: {exception.Message}",
                        exception);
                    break;

                case OperationType.TabSwitchSave:
                    // Critical - notify with suggestion
                    await _notifications.ShowErrorAsync(
                        "⚠️ Save failed when switching tabs - Your changes may not be saved. " +
                        "Please save manually (Ctrl+S) to ensure your work is safe. " +
                        $"Error: {exception.Message}",
                        exception);
                    break;

                case OperationType.SearchIndex:
                    // Non-critical - batch notification
                    ScheduleBatchNotification();
                    break;

                default:
                    // General background operation
                    ScheduleBatchNotification();
                    break;
            }
        }

        private void ScheduleBatchNotification()
        {
            // Batch non-critical failures to avoid notification spam
            if ((DateTime.UtcNow - _lastUserNotification).TotalMinutes > 5)
            {
                _lastUserNotification = DateTime.UtcNow;
                var failureCount = _recentFailures.Count;
                if (failureCount > 0)
                {
                    _ = _notifications.ShowWarningAsync(
                        $"Background operations - {failureCount} background operations failed. " +
                        "Check logs for details. Your notes are safe but some features may not work properly.");
                }
            }
        }

        public SaveHealthStatus GetSaveHealth() => _health;
    }
}
