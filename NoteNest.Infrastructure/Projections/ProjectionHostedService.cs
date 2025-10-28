using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Infrastructure.Projections
{
    /// <summary>
    /// Background service that continuously polls for new events and updates projections.
    /// Runs as a safety net alongside synchronous projection updates in command pipeline.
    /// 
    /// Purpose:
    /// - Ensures projections are synchronized on startup (fixes session persistence issues)
    /// - Ensures projections eventually catch up even if synchronous update fails
    /// - Handles events from external sources (if applicable)
    /// - Provides resilience against edge cases
    /// 
    /// Startup: Synchronous catch-up before app proceeds (fast when current: ~30-50ms)
    /// Background polling: 5 seconds (configurable)
    /// </summary>
    public class ProjectionHostedService : IHostedService
    {
        private readonly ProjectionOrchestrator _orchestrator;
        private readonly IAppLogger _logger;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _executingTask;

        public ProjectionHostedService(
            ProjectionOrchestrator orchestrator,
            IAppLogger logger)
        {
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.Info("🚀 Starting projection background service...");
            
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            // ✅ CRITICAL FIX: Perform initial projection catch-up synchronously
            // This ensures projections are current BEFORE any UI loads or queries run
            // Fixes session persistence bug where TodoStore/CategoryStore loaded stale data
            // 
            // Performance: FAST when projections are current (typical case: ~30-50ms)
            // - Only slow when actually behind (rare: first run or after event replay)
            _logger.Info("📊 Performing initial projection catch-up...");
            var startTime = DateTime.UtcNow;
            
            try
            {
                await _orchestrator.CatchUpAsync();
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.Info($"✅ Initial projection catch-up complete in {elapsed:F0}ms");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ Initial projection catch-up failed - will retry in background");
                // Don't throw - allow app to start even if catch-up fails
                // Background polling will retry and eventually catch up
            }
            
            // Start continuous background polling (don't await - let it run)
            _executingTask = Task.Run(async () =>
            {
                _logger.Info("📊 Projection background polling started (5s interval)");
                
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        // Poll every 5 seconds for new events
                        await Task.Delay(5000, _cancellationTokenSource.Token);
                        await _orchestrator.CatchUpAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        // Normal shutdown - exit gracefully
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Background projection catch-up error: {ex.Message}");
                        // Continue running despite errors
                    }
                }
                
                _logger.Info("📊 Projection background polling stopped");
            }, _cancellationTokenSource.Token);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Stopping projection background service...");
            
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
            }
            
            if (_executingTask != null)
            {
                // Wait for background task to complete (with timeout)
                await Task.WhenAny(_executingTask, Task.Delay(3000, cancellationToken));
            }
            
            _logger.Info("✅ Projection background service stopped");
        }
    }
}

