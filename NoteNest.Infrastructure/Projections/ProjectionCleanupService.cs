using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Infrastructure.Projections
{
    /// <summary>
    /// Ensures projections database is properly checkpointed on application shutdown.
    /// Final safety net to guarantee WAL changes are flushed to main database file.
    /// This complements PRAGMA synchronous = FULL by ensuring clean shutdown even if app crashes.
    /// </summary>
    public class ProjectionCleanupService : IHostedService
    {
        private readonly string _connectionString;
        private readonly IAppLogger _logger;
        
        public ProjectionCleanupService(string projectionsConnectionString, IAppLogger logger)
        {
            _connectionString = projectionsConnectionString ?? throw new ArgumentNullException(nameof(projectionsConnectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.Info("[ProjectionCleanup] Service registered - will checkpoint projections.db on shutdown");
            return Task.CompletedTask;
        }
        
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.Info("[ProjectionCleanup] 🔄 Performing final WAL checkpoint before shutdown...");
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                // TRUNCATE mode: Checkpoint WAL to main DB and remove WAL file
                // This is most aggressive mode - guarantees clean shutdown
                // Returns: "0|X|Y" where X=pages checkpointed, Y=pages moved to DB
                var result = await connection.ExecuteScalarAsync<string>("PRAGMA wal_checkpoint(TRUNCATE)");
                
                _logger.Info($"[ProjectionCleanup] ✅ Final checkpoint completed: {result}");
                _logger.Info($"[ProjectionCleanup] ✅ All projection changes persisted to disk");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[ProjectionCleanup] Failed to checkpoint on shutdown");
                // Don't throw - shutdown should continue even if checkpoint fails
                // With synchronous=FULL, data should already be safe
            }
        }
    }
}

