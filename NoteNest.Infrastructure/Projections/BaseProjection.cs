using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Projections;
using NoteNest.Domain.Common;

namespace NoteNest.Infrastructure.Projections
{
    /// <summary>
    /// Base class for projections with common functionality.
    /// Handles checkpoint tracking and database operations.
    /// </summary>
    public abstract class BaseProjection : IProjection
    {
        protected readonly string _connectionString;
        protected readonly IAppLogger _logger;
        
        public abstract string Name { get; }
        
        protected BaseProjection(string connectionString, IAppLogger logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public abstract Task HandleAsync(IDomainEvent @event);
        
        public virtual async Task RebuildAsync()
        {
            _logger.Info($"[{Name}] Starting rebuild...");
            
            // Clear projection data
            await ClearProjectionDataAsync();
            
            // Reset checkpoint
            await SetLastProcessedPositionAsync(0);
            
            _logger.Info($"[{Name}] Rebuild complete - ready to process events");
        }
        
        protected abstract Task ClearProjectionDataAsync();
        
        public async Task<long> GetLastProcessedPositionAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            // Check if checkpoint exists
            var checkpoint = await connection.QueryFirstOrDefaultAsync<ProjectionCheckpoint>(
                "SELECT last_processed_position AS LastProcessedPosition FROM projection_metadata WHERE projection_name = @Name",
                new { Name = this.Name });
            
            return checkpoint?.LastProcessedPosition ?? 0;
        }
        
        public async Task SetLastProcessedPositionAsync(long position)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            await connection.ExecuteAsync(
                @"INSERT INTO projection_metadata (projection_name, last_processed_position, last_updated_at, status)
                  VALUES (@Name, @Position, @UpdatedAt, 'ready')
                  ON CONFLICT(projection_name) DO UPDATE SET
                    last_processed_position = @Position,
                    last_updated_at = @UpdatedAt",
                new
                {
                    Name = this.Name,
                    Position = position,
                    UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
        }
        
        protected async Task<SqliteConnection> OpenConnectionAsync()
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            return connection;
        }
        
        private class ProjectionCheckpoint
        {
            public long LastProcessedPosition { get; set; }
        }
    }
}

