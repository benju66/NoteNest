using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Infrastructure.EventStore
{
    /// <summary>
    /// Initializes the event store database.
    /// Creates schema, handles migrations, ensures integrity.
    /// </summary>
    public class EventStoreInitializer
    {
        private readonly string _connectionString;
        private readonly IAppLogger _logger;
        
        public EventStoreInitializer(string connectionString, IAppLogger logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task<bool> InitializeAsync()
        {
            try
            {
                _logger.Info("Initializing event store database...");
                
                // Ensure database directory exists
                var dbPath = new SqliteConnectionStringBuilder(_connectionString).DataSource;
                var directory = Path.GetDirectoryName(dbPath);
                
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.Info($"Created database directory: {directory}");
                }
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                // Check if already initialized
                var tableExists = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='events'") > 0;
                
                if (tableExists)
                {
                    _logger.Info("Event store database already initialized");
                    return true;
                }
                
                // Create schema from embedded resource
                var schema = LoadSchemaFromResource();
                
                // Execute schema creation
                using var transaction = connection.BeginTransaction();
                try
                {
                    await connection.ExecuteAsync(schema, transaction: transaction);
                    transaction.Commit();
                    _logger.Info("Event store database schema created successfully");
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to initialize event store database", ex);
                return false;
            }
        }
        
        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                // Verify essential tables exist
                var eventsExists = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='events'") > 0;
                
                var snapshotsExists = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='snapshots'") > 0;
                
                return eventsExists && snapshotsExists;
            }
            catch (Exception ex)
            {
                _logger.Error("Event store health check failed", ex);
                return false;
            }
        }
        
        public async Task<int> GetEventCountAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM events");
        }
        
        public async Task<long> GetCurrentStreamPositionAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            return await connection.ExecuteScalarAsync<long>(
                "SELECT current_position FROM stream_position WHERE id = 1");
        }
        
        private string LoadSchemaFromResource()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "NoteNest.Database.Schemas.EventStore_Schema.sql";
            
            // Try to find the resource
            var availableResources = assembly.GetManifestResourceNames();
            var actualResourceName = availableResources.FirstOrDefault(r => r.Contains("EventStore_Schema"));
            
            if (actualResourceName == null)
            {
                throw new InvalidOperationException(
                    $"Could not find EventStore schema resource. Available resources: {string.Join(", ", availableResources)}");
            }
            
            using var stream = assembly.GetManifestResourceStream(actualResourceName);
            if (stream == null)
            {
                throw new InvalidOperationException($"Could not load EventStore schema from {actualResourceName}");
            }
            
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}

