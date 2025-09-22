using System;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Utils
{
    /// <summary>
    /// Provides safe execution patterns for background tasks with proper error handling and cancellation support
    /// </summary>
    public static class SafeBackgroundTask
    {
        /// <summary>
        /// Runs a background task with supervised execution, proper error handling, and cancellation support
        /// </summary>
        /// <param name="taskFactory">Factory function that creates the task to execute</param>
        /// <param name="cancellationToken">Token for cancellation support</param>
        /// <param name="logger">Optional logger for error reporting</param>
        /// <param name="taskName">Optional name for logging purposes</param>
        /// <returns>Task that represents the supervised background operation</returns>
        public static Task RunSafelyAsync(
            Func<CancellationToken, Task> taskFactory,
            CancellationToken cancellationToken,
            IAppLogger? logger = null,
            string? taskName = null)
        {
            if (taskFactory == null)
                throw new ArgumentNullException(nameof(taskFactory));

            var safeTaskName = taskName ?? "BackgroundTask";

            return Task.Run(async () =>
            {
                try
                {
                    logger?.Debug($"Starting background task: {safeTaskName}");
                    await taskFactory(cancellationToken);
                    logger?.Debug($"Background task completed successfully: {safeTaskName}");
                }
                catch (OperationCanceledException)
                {
                    logger?.Debug($"Background task was cancelled: {safeTaskName}");
                    // Don't treat cancellation as an error - this is expected behavior
                }
                catch (Exception ex)
                {
                    logger?.Error(ex, $"Background task failed: {safeTaskName}");
                    // Log but don't rethrow - background tasks should not crash the app
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Runs a background task with supervised execution and automatic retry on failure
        /// </summary>
        /// <param name="taskFactory">Factory function that creates the task to execute</param>
        /// <param name="cancellationToken">Token for cancellation support</param>
        /// <param name="maxRetries">Maximum number of retry attempts (default: 3)</param>
        /// <param name="retryDelayMs">Delay between retries in milliseconds (default: 1000)</param>
        /// <param name="logger">Optional logger for error reporting</param>
        /// <param name="taskName">Optional name for logging purposes</param>
        /// <returns>Task that represents the supervised background operation with retry logic</returns>
        public static Task RunSafelyWithRetryAsync(
            Func<CancellationToken, Task> taskFactory,
            CancellationToken cancellationToken,
            int maxRetries = 3,
            int retryDelayMs = 1000,
            IAppLogger? logger = null,
            string? taskName = null)
        {
            if (taskFactory == null)
                throw new ArgumentNullException(nameof(taskFactory));

            var safeTaskName = taskName ?? "BackgroundTaskWithRetry";

            return Task.Run(async () =>
            {
                var attempt = 0;
                while (attempt <= maxRetries)
                {
                    try
                    {
                        if (attempt > 0)
                        {
                            logger?.Debug($"Retrying background task (attempt {attempt + 1}/{maxRetries + 1}): {safeTaskName}");
                        }
                        else
                        {
                            logger?.Debug($"Starting background task: {safeTaskName}");
                        }

                        await taskFactory(cancellationToken);
                        logger?.Debug($"Background task completed successfully: {safeTaskName}");
                        return; // Success - exit retry loop
                    }
                    catch (OperationCanceledException)
                    {
                        logger?.Debug($"Background task was cancelled: {safeTaskName}");
                        return; // Don't retry on cancellation
                    }
                    catch (Exception ex)
                    {
                        attempt++;
                        if (attempt > maxRetries)
                        {
                            logger?.Error(ex, $"Background task failed after {maxRetries + 1} attempts: {safeTaskName}");
                            return; // Max retries reached - give up
                        }
                        else
                        {
                            logger?.Warning($"Background task failed, will retry (attempt {attempt}/{maxRetries + 1}): {safeTaskName}. Error: {ex.Message}");
                            await Task.Delay(retryDelayMs, cancellationToken);
                        }
                    }
                }
            }, cancellationToken);
        }
    }
}
