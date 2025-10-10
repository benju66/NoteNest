using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Search
{
    /// <summary>
    /// Configuration and initialization for SQLite FTS5 search database
    /// Single Responsibility: Database setup and connection management
    /// </summary>
    public static class DatabaseConfig
    {
        public const string DatabaseFileName = "search.db";
        
        /// <summary>
        /// SQLite connection string template
        /// </summary>
        public static string GetConnectionString(string databasePath) => 
            $"Data Source={databasePath};Cache=Shared;";

        /// <summary>
        /// Database pragma statements optimized for search performance
        /// No WAL mode - we use rebuild-on-failure strategy
        /// </summary>
        public static readonly string[] PragmaStatements = 
        {
            "PRAGMA journal_mode = DELETE",      // No SQLite WAL conflicts with save system
            "PRAGMA synchronous = NORMAL",       // Balance performance vs safety  
            "PRAGMA foreign_keys = ON",          // Enable referential integrity
            "PRAGMA temp_store = MEMORY",        // Store temp data in memory for speed
            "PRAGMA cache_size = -4000",         // 4MB page cache for better performance
            "PRAGMA mmap_size = 268435456",      // 256MB memory mapping
            "PRAGMA optimize"                    // Optimize query planner statistics
        };

        /// <summary>
        /// FTS5 virtual table creation SQL
        /// Uses porter stemming and ASCII folding for better search
        /// Enhanced with smart preview caching for optimal performance
        /// </summary>
        public const string CreateFtsTableSql = @"
            CREATE VIRTUAL TABLE IF NOT EXISTS notes_fts USING fts5(
                title,                      -- Note title (searchable)
                content,                    -- Extracted plain text (searchable)  
                content_preview UNINDEXED,  -- Pre-generated smart preview (not searchable)
                category_id UNINDEXED,      -- Category UUID (filterable, not searchable)
                file_path UNINDEXED,        -- Full file path (for result mapping)
                note_id UNINDEXED,          -- Note UUID (for result mapping)
                last_modified UNINDEXED,    -- ISO timestamp (for sorting)
                tokenize='porter ascii'     -- Stemming + ASCII character folding
            );";

        /// <summary>
        /// Metadata table for extended search attributes
        /// </summary>
        public const string CreateMetadataTableSql = @"
            CREATE TABLE IF NOT EXISTS note_metadata (
                note_id TEXT PRIMARY KEY,
                file_size INTEGER DEFAULT 0,
                created_date TEXT,
                usage_count INTEGER DEFAULT 0,
                last_accessed TEXT,
                tags TEXT DEFAULT '[]'      -- JSON array of tags
            );";

        /// <summary>
        /// Performance indexes for metadata queries
        /// </summary>
        public static readonly string[] CreateIndexesSql = 
        {
            "CREATE INDEX IF NOT EXISTS idx_metadata_usage ON note_metadata(usage_count DESC);",
            "CREATE INDEX IF NOT EXISTS idx_metadata_accessed ON note_metadata(last_accessed DESC);",
            "CREATE INDEX IF NOT EXISTS idx_metadata_created ON note_metadata(created_date DESC);"
        };

        /// <summary>
        /// Initialize database with schema and optimizations
        /// </summary>
        public static async Task InitializeDatabaseAsync(string databasePath, IAppLogger? logger = null)
        {
            try
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(databasePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var connection = new SqliteConnection(GetConnectionString(databasePath));
                await connection.OpenAsync();

                // Apply performance pragmas
                foreach (var pragma in PragmaStatements)
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = pragma;
                    await command.ExecuteNonQueryAsync();
                }

                // Create FTS5 virtual table
                using var createFtsCommand = connection.CreateCommand();
                createFtsCommand.CommandText = CreateFtsTableSql;
                await createFtsCommand.ExecuteNonQueryAsync();

                // Create metadata table
                using var createMetadataCommand = connection.CreateCommand();
                createMetadataCommand.CommandText = CreateMetadataTableSql;
                await createMetadataCommand.ExecuteNonQueryAsync();

                // Create performance indexes
                foreach (var indexSql in CreateIndexesSql)
                {
                    using var indexCommand = connection.CreateCommand();
                    indexCommand.CommandText = indexSql;
                    await indexCommand.ExecuteNonQueryAsync();
                }

                logger?.Info($"FTS5 search database initialized: {databasePath}");
            }
            catch (Exception ex)
            {
                logger?.Error(ex, $"Failed to initialize FTS5 database: {databasePath}");
                throw;
            }
        }

        /// <summary>
        /// Create configured SQLite connection
        /// </summary>
        public static async Task<SqliteConnection> CreateConnectionAsync(string databasePath)
        {
            var connection = new SqliteConnection(GetConnectionString(databasePath));
            await connection.OpenAsync();
            
            // Apply runtime pragmas for performance
            var runtimePragmas = new[] 
            {
                "PRAGMA temp_store = MEMORY",
                "PRAGMA cache_size = -4000"
            };

            foreach (var pragma in runtimePragmas)
            {
                using var command = connection.CreateCommand();
                command.CommandText = pragma;
                await command.ExecuteNonQueryAsync();
            }

            return connection;
        }

        /// <summary>
        /// Check if database exists and has correct schema
        /// </summary>
        public static async Task<bool> ValidateDatabaseAsync(string databasePath, IAppLogger? logger = null)
        {
            try
            {
                if (!File.Exists(databasePath))
                    return false;

                using var connection = new SqliteConnection(GetConnectionString(databasePath));
                await connection.OpenAsync();

                // Check if FTS5 table exists with expected columns
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT name FROM pragma_table_info('notes_fts') 
                    WHERE name IN ('title', 'content', 'category_id', 'file_path', 'note_id', 'last_modified');";

                var columns = 0;
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    columns++;
                }

                var isValid = columns == 6; // Should have all 6 expected columns
                logger?.Debug($"Database validation: {databasePath} - Valid: {isValid} (Found {columns}/6 columns)");
                
                return isValid;
            }
            catch (Exception ex)
            {
                logger?.Warning($"Database validation failed: {databasePath} - {ex.Message}");
                return false;
            }
        }
    }
}
