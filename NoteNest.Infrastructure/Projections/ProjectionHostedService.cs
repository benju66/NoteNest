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
    /// - Ensures projections eventually catch up even if synchronous update fails
    /// - Handles events from external sources (if applicable)
    /// - Provides resilience against edge cases
    /// 
    /// Polling interval: 5 seconds (configurable)
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

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.Info("🚀 Starting projection background service...");
            
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            // Start continuous catch-up in background (don't await - let it run)
            _executingTask = Task.Run(async () =>
            {
                // Give app time to fully start up
                await Task.Delay(2000, _cancellationTokenSource.Token);
                
                _logger.Info("📊 Projection background polling started (5s interval)");
                
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        await _orchestrator.CatchUpAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Background projection catch-up error: {ex.Message}");
                        // Continue running despite errors
                    }
                    
                    // Poll every 5 seconds (less aggressive than continuous mode)
                    await Task.Delay(5000, _cancellationTokenSource.Token);
                }
            }, _cancellationTokenSource.Token);
            
            return Task.CompletedTask;
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

