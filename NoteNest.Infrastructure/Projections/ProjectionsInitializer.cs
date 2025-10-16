using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Infrastructure.Projections
{
    /// <summary>
    /// Initializes the projections database.
    /// Creates schema for all read models (tree_view, tag_vocabulary, todo_view, etc.).
    /// </summary>
    public class ProjectionsInitializer
    {
        private readonly string _connectionString;
        private readonly IAppLogger _logger;
        
        public ProjectionsInitializer(string connectionString, IAppLogger logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task<bool> InitializeAsync()
        {
            try
            {
                _logger.Info("Initializing projections database...");
                
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
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='projection_metadata'") > 0;
                
                if (tableExists)
                {
                    _logger.Info("Projections database already initialized");
                    return true;
                }
                
                // Create schema from embedded resource
                var schema = LoadSchemaFromResource();
                
                // Split PRAGMA statements from CREATE statements (WAL mode can't be changed in transaction)
                var lines = schema.Split('\n');
                var pragmas = lines.Where(l => l.Trim().StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase)).ToList();
                var createStatements = string.Join("\n", lines.Where(l => !l.Trim().StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase)));
                
                // Execute PRAGMA statements FIRST (outside transaction)
                foreach (var pragma in pragmas)
                {
                    if (!string.IsNullOrWhiteSpace(pragma) && !pragma.Trim().StartsWith("--"))
                    {
                        await connection.ExecuteAsync(pragma);
                    }
                }
                
                // Execute CREATE statements in transaction
                using var transaction = connection.BeginTransaction();
                try
                {
                    await connection.ExecuteAsync(createStatements, transaction: transaction);
                    transaction.Commit();
                    _logger.Info("Projections database schema created successfully");
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
                _logger.Error("Failed to initialize projections database", ex);
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
                var requiredTables = new[] { "tree_view", "entity_tags", "tag_vocabulary", "todo_view", "projection_metadata" };
                
                foreach (var table in requiredTables)
                {
                    var exists = await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@Table",
                        new { Table = table }) > 0;
                    
                    if (!exists)
                    {
                        _logger.Warning($"Required table missing: {table}");
                        return false;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Projections health check failed", ex);
                return false;
            }
        }
        
        public async Task<ProjectionStats> GetStatsAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            return new ProjectionStats
            {
                TreeViewCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM tree_view"),
                EntityTagsCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM entity_tags"),
                TagVocabularyCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM tag_vocabulary"),
                TodoViewCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM todo_view")
            };
        }
        
        private string LoadSchemaFromResource()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "NoteNest.Database.Schemas.Projections_Schema.sql";
            
            // Try to find the resource
            var availableResources = assembly.GetManifestResourceNames();
            var actualResourceName = availableResources.FirstOrDefault(r => r.Contains("Projections_Schema"));
            
            if (actualResourceName == null)
            {
                throw new InvalidOperationException(
                    $"Could not find Projections schema resource. Available resources: {string.Join(", ", availableResources)}");
            }
            
            using var stream = assembly.GetManifestResourceStream(actualResourceName);
            if (stream == null)
            {
                throw new InvalidOperationException($"Could not load Projections schema from {actualResourceName}");
            }
            
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
    
    public class ProjectionStats
    {
        public int TreeViewCount { get; set; }
        public int EntityTagsCount { get; set; }
        public int TagVocabularyCount { get; set; }
        public int TodoViewCount { get; set; }
    }
}

