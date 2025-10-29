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
    /// Ensures projections database is properly closed on application shutdown.
    /// In DELETE journal mode, this ensures clean database shutdown.
    /// Note: In DELETE mode, wal_checkpoint is a no-op (database uses rollback journal instead).
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
            _logger.Info("[ProjectionCleanup] Service registered - will ensure clean shutdown of projections.db");
            return Task.CompletedTask;
        }
        
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.Info("[ProjectionCleanup] 🔄 Ensuring clean database shutdown...");
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                // Note: In DELETE journal mode, this is a no-op (harmless)
                // In WAL mode, this would checkpoint and truncate the WAL file
                // We keep it for compatibility if journal mode changes in future
                var result = await connection.ExecuteScalarAsync<string>("PRAGMA wal_checkpoint(TRUNCATE)");
                
                _logger.Info($"[ProjectionCleanup] ✅ Database shutdown complete");
                _logger.Debug($"[ProjectionCleanup] Checkpoint result: {result}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[ProjectionCleanup] Failed during shutdown cleanup");
                // Don't throw - shutdown should continue even if cleanup fails
                // With synchronous=FULL in DELETE mode, data is already persisted
            }
        }
    }
}

