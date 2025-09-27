using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Infrastructure.Database
{
    /// <summary>
    /// Initializes the tree database with schema creation and migrations.
    /// Handles database setup, schema versioning, and safe upgrades.
    /// </summary>
    public interface ITreeDatabaseInitializer
    {
        Task<bool> InitializeAsync();
        Task<bool> UpgradeSchemaAsync();
        Task<int> GetCurrentSchemaVersionAsync();
        Task<bool> IsHealthyAsync();
    }
    
    public class TreeDatabaseInitializer : ITreeDatabaseInitializer
    {
        private readonly string _connectionString;
        private readonly IAppLogger _logger;
        
        public TreeDatabaseInitializer(string connectionString, IAppLogger logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> InitializeAsync()
        {
            try
            {
                _logger.Info("Initializing TreeDatabase...");
                
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
                
                // Check if database already initialized
                var tableExists = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='tree_nodes'") > 0;
                
                if (tableExists)
                {
                    _logger.Info("Database already initialized, checking for upgrades...");
                    return await UpgradeSchemaAsync();
                }
                
                _logger.Info("Creating fresh database schema...");
                
                // Execute the complete schema
                var schemaScript = GetFullSchemaScript();
                await connection.ExecuteAsync(schemaScript);
                
                // Verify the schema was created correctly
                if (await VerifySchemaAsync())
                {
                    _logger.Info("Database schema created successfully");
                    return true;
                }
                else
                {
                    _logger.Error("Database schema creation failed verification");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize database");
                return false;
            }
        }
        
        public async Task<bool> UpgradeSchemaAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var currentVersion = await GetCurrentSchemaVersionAsync(connection);
                var targetVersion = GetLatestSchemaVersion();
                
                if (currentVersion >= targetVersion)
                {
                    _logger.Info($"Database schema is up-to-date (version {currentVersion})");
                    return true;
                }
                
                _logger.Info($"Upgrading database schema from version {currentVersion} to {targetVersion}");
                
                // Apply migrations
                var migrations = GetMigrations();
                foreach (var migration in migrations)
                {
                    if (migration.Version <= currentVersion)
                        continue;
                        
                    await ApplyMigrationAsync(connection, migration);
                }
                
                _logger.Info("Database schema upgrade completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to upgrade database schema");
                return false;
            }
        }
        
        public async Task<int> GetCurrentSchemaVersionAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                return await GetCurrentSchemaVersionAsync(connection);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to get schema version: {ex.Message}");
                return 0;
            }
        }
        
        private async Task<int> GetCurrentSchemaVersionAsync(SqliteConnection connection)
        {
            try
            {
                // Check if schema_version table exists
                var tableExists = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_version'") > 0;
                
                if (!tableExists)
                    return 0;
                
                return await connection.ExecuteScalarAsync<int>(
                    "SELECT COALESCE(MAX(version), 0) FROM schema_version");
            }
            catch
            {
                return 0;
            }
        }
        
        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                // Basic connectivity test
                await connection.ExecuteScalarAsync<int>("SELECT 1");
                
                // Integrity check
                var integrityResult = await connection.ExecuteScalarAsync<string>("PRAGMA integrity_check");
                if (integrityResult != "ok")
                {
                    _logger.Warning($"Database integrity check failed: {integrityResult}");
                    return false;
                }
                
                // Schema version check
                var schemaVersion = await GetCurrentSchemaVersionAsync(connection);
                if (schemaVersion < GetLatestSchemaVersion())
                {
                    _logger.Warning($"Database schema is outdated: {schemaVersion} < {GetLatestSchemaVersion()}");
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Database health check failed");
                return false;
            }
        }
        
        private async Task<bool> VerifySchemaAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                // Check that all required tables exist
                var requiredTables = new[]
                {
                    "tree_nodes", "tree_state", "file_operations", "metadata_cache", 
                    "audit_log", "schema_version"
                };
                
                foreach (var table in requiredTables)
                {
                    var exists = await connection.ExecuteScalarAsync<int>(
                        $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'") > 0;
                    
                    if (!exists)
                    {
                        _logger.Error($"Required table '{table}' was not created");
                        return false;
                    }
                }
                
                // Check that views exist
                var requiredViews = new[]
                {
                    "tree_hierarchy", "category_stats", "recent_items", 
                    "pinned_items", "health_metrics"
                };
                
                foreach (var view in requiredViews)
                {
                    var exists = await connection.ExecuteScalarAsync<int>(
                        $"SELECT COUNT(*) FROM sqlite_master WHERE type='view' AND name='{view}'") > 0;
                    
                    if (!exists)
                    {
                        _logger.Error($"Required view '{view}' was not created");
                        return false;
                    }
                }
                
                // Verify schema_version has initial record
                var versionCount = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM schema_version");
                
                if (versionCount == 0)
                {
                    _logger.Error("schema_version table is empty");
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Schema verification failed");
                return false;
            }
        }
        
        private async Task ApplyMigrationAsync(SqliteConnection connection, Migration migration)
        {
            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                _logger.Info($"Applying migration {migration.Version}: {migration.Description}");
                
                // Execute migration SQL
                await connection.ExecuteAsync(migration.UpgradeSql, transaction: transaction);
                
                // Record migration in schema_version table
                await connection.ExecuteAsync(
                    @"INSERT INTO schema_version (version, applied_at, description, upgrade_sql, rollback_sql)
                      VALUES (@Version, @AppliedAt, @Description, @UpgradeSql, @RollbackSql)",
                    new
                    {
                        migration.Version,
                        AppliedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        migration.Description,
                        migration.UpgradeSql,
                        migration.RollbackSql
                    },
                    transaction: transaction);
                
                await transaction.CommitAsync();
                _logger.Info($"Successfully applied migration {migration.Version}");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.Error(ex, $"Failed to apply migration {migration.Version}");
                throw;
            }
        }
        
        private int GetLatestSchemaVersion()
        {
            return 1; // Current schema version
        }
        
        private Migration[] GetMigrations()
        {
            // Future migrations would be added here
            return new Migration[0];
        }
        
        private string GetFullSchemaScript()
        {
            // Read the complete schema from embedded resource or file
            // For now, return the complete schema as a string literal
            return @"
-- Enable foreign keys and configure for performance
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA cache_size = -64000;
PRAGMA temp_store = MEMORY;
PRAGMA mmap_size = 268435456;

-- Core tree structure with GUID identity
CREATE TABLE tree_nodes (
    -- Identity
    id TEXT PRIMARY KEY,
    parent_id TEXT,
    
    -- Path information
    canonical_path TEXT NOT NULL,
    display_path TEXT NOT NULL,
    absolute_path TEXT NOT NULL,
    
    -- Node information
    node_type TEXT NOT NULL CHECK(node_type IN ('category', 'note')),
    name TEXT NOT NULL,
    file_extension TEXT,
    
    -- File metadata
    file_size INTEGER,
    created_at INTEGER NOT NULL,
    modified_at INTEGER NOT NULL,
    accessed_at INTEGER,
    
    -- Hash information
    quick_hash TEXT,
    full_hash TEXT,
    hash_algorithm TEXT DEFAULT 'xxHash64',
    hash_calculated_at INTEGER,
    
    -- UI state
    is_expanded INTEGER DEFAULT 0,
    is_pinned INTEGER DEFAULT 0,
    is_selected INTEGER DEFAULT 0,
    
    -- Organization
    sort_order INTEGER DEFAULT 0,
    color_tag TEXT,
    icon_override TEXT,
    
    -- Soft delete
    is_deleted INTEGER DEFAULT 0,
    deleted_at INTEGER,
    
    -- Metadata
    metadata_version INTEGER DEFAULT 1,
    custom_properties TEXT,
    
    -- Constraints
    FOREIGN KEY (parent_id) REFERENCES tree_nodes(id) ON DELETE CASCADE,
    CHECK (node_type = 'category' OR file_extension IS NOT NULL),
    CHECK (is_deleted IN (0, 1)),
    CHECK (is_expanded IN (0, 1)),
    CHECK (is_pinned IN (0, 1)),
    CHECK (is_selected IN (0, 1))
);

-- Indexes
CREATE INDEX idx_tree_parent ON tree_nodes(parent_id, node_type, sort_order) WHERE is_deleted = 0;
CREATE INDEX idx_tree_path ON tree_nodes(canonical_path) WHERE is_deleted = 0;
CREATE INDEX idx_tree_type ON tree_nodes(node_type) WHERE is_deleted = 0;
CREATE INDEX idx_tree_pinned ON tree_nodes(is_pinned) WHERE is_pinned = 1 AND is_deleted = 0;
CREATE INDEX idx_tree_deleted ON tree_nodes(is_deleted, deleted_at) WHERE is_deleted = 1;
CREATE INDEX idx_tree_modified ON tree_nodes(modified_at DESC) WHERE is_deleted = 0;
CREATE INDEX idx_tree_hash ON tree_nodes(quick_hash);

-- UI state persistence
CREATE TABLE tree_state (
    node_id TEXT PRIMARY KEY,
    is_expanded INTEGER DEFAULT 0,
    scroll_position REAL DEFAULT 0,
    last_selected_at INTEGER,
    view_mode TEXT,
    custom_state TEXT,
    FOREIGN KEY (node_id) REFERENCES tree_nodes(id) ON DELETE CASCADE
);

CREATE INDEX idx_state_selected ON tree_state(last_selected_at DESC);

-- File operation tracking
CREATE TABLE file_operations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    operation_type TEXT NOT NULL,
    node_id TEXT,
    old_path TEXT,
    new_path TEXT,
    performed_at INTEGER NOT NULL,
    performed_by TEXT,
    status TEXT DEFAULT 'pending',
    error_message TEXT,
    retry_count INTEGER DEFAULT 0,
    FOREIGN KEY (node_id) REFERENCES tree_nodes(id) ON DELETE SET NULL
);

CREATE INDEX idx_operations_status ON file_operations(status, performed_at);
CREATE INDEX idx_operations_node ON file_operations(node_id);
CREATE INDEX idx_operations_time ON file_operations(performed_at DESC);

-- Metadata cache
CREATE TABLE metadata_cache (
    key TEXT PRIMARY KEY,
    value TEXT,
    cached_at INTEGER NOT NULL,
    expires_at INTEGER,
    category TEXT,
    size_bytes INTEGER
);

CREATE INDEX idx_cache_expiry ON metadata_cache(expires_at);
CREATE INDEX idx_cache_category ON metadata_cache(category);

-- Audit trail
CREATE TABLE audit_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    table_name TEXT NOT NULL,
    operation TEXT NOT NULL,
    node_id TEXT,
    old_values TEXT,
    new_values TEXT,
    changed_at INTEGER NOT NULL,
    changed_by TEXT,
    change_source TEXT,
    session_id TEXT
);

CREATE INDEX idx_audit_node ON audit_log(node_id);
CREATE INDEX idx_audit_time ON audit_log(changed_at DESC);
CREATE INDEX idx_audit_session ON audit_log(session_id);

-- Schema versioning
CREATE TABLE schema_version (
    version INTEGER PRIMARY KEY,
    applied_at INTEGER NOT NULL,
    description TEXT,
    upgrade_sql TEXT,
    rollback_sql TEXT
);

INSERT INTO schema_version (version, applied_at, description) 
VALUES (1, strftime('%s', 'now'), 'Initial database schema with TreeNode architecture');

-- Triggers
CREATE TRIGGER update_tree_nodes_timestamp 
AFTER UPDATE ON tree_nodes
FOR EACH ROW
WHEN NEW.is_deleted = 0 AND OLD.modified_at = NEW.modified_at
BEGIN
    UPDATE tree_nodes SET modified_at = strftime('%s', 'now') WHERE id = NEW.id;
END;

CREATE TRIGGER audit_tree_nodes_insert
AFTER INSERT ON tree_nodes
BEGIN
    INSERT INTO audit_log (table_name, operation, node_id, new_values, changed_at, change_source)
    VALUES ('tree_nodes', 'INSERT', NEW.id, json_object(
        'id', NEW.id,
        'parent_id', NEW.parent_id,
        'name', NEW.name,
        'canonical_path', NEW.canonical_path,
        'node_type', NEW.node_type,
        'file_extension', NEW.file_extension
    ), strftime('%s', 'now'), 'app');
END;

CREATE TRIGGER audit_tree_nodes_update
AFTER UPDATE ON tree_nodes
FOR EACH ROW
WHEN OLD.name != NEW.name OR OLD.canonical_path != NEW.canonical_path OR OLD.parent_id != NEW.parent_id
BEGIN
    INSERT INTO audit_log (table_name, operation, node_id, old_values, new_values, changed_at, change_source)
    VALUES ('tree_nodes', 'UPDATE', NEW.id, 
        json_object('name', OLD.name, 'canonical_path', OLD.canonical_path, 'parent_id', OLD.parent_id),
        json_object('name', NEW.name, 'canonical_path', NEW.canonical_path, 'parent_id', NEW.parent_id),
        strftime('%s', 'now'), 'app');
END;

CREATE TRIGGER audit_tree_nodes_delete
AFTER UPDATE OF is_deleted ON tree_nodes
FOR EACH ROW
WHEN NEW.is_deleted = 1 AND OLD.is_deleted = 0
BEGIN
    INSERT INTO audit_log (table_name, operation, node_id, old_values, changed_at, change_source)
    VALUES ('tree_nodes', 'SOFT_DELETE', NEW.id, 
        json_object('name', NEW.name, 'canonical_path', NEW.canonical_path),
        strftime('%s', 'now'), 'app');
END;

-- Views
CREATE VIEW tree_hierarchy AS
WITH RECURSIVE tree_cte AS (
    SELECT *, 0 as level, id as root_id, canonical_path as full_path
    FROM tree_nodes
    WHERE parent_id IS NULL AND is_deleted = 0
    
    UNION ALL
    
    SELECT t.*, tc.level + 1, tc.root_id, tc.full_path || '/' || t.canonical_path
    FROM tree_nodes t
    INNER JOIN tree_cte tc ON t.parent_id = tc.id
    WHERE t.is_deleted = 0
)
SELECT * FROM tree_cte ORDER BY root_id, level, node_type, sort_order, name;

CREATE VIEW category_stats AS
SELECT 
    p.id, p.name, p.canonical_path,
    COUNT(DISTINCT c.id) as child_count,
    SUM(CASE WHEN c.node_type = 'note' THEN 1 ELSE 0 END) as note_count,
    SUM(CASE WHEN c.node_type = 'category' THEN 1 ELSE 0 END) as subcategory_count,
    COALESCE(SUM(c.file_size), 0) as total_size,
    MAX(c.modified_at) as last_modified,
    MIN(c.created_at) as first_created
FROM tree_nodes p
LEFT JOIN tree_nodes c ON c.parent_id = p.id AND c.is_deleted = 0
WHERE p.node_type = 'category' AND p.is_deleted = 0
GROUP BY p.id, p.name, p.canonical_path;

CREATE VIEW recent_items AS
SELECT id, name, canonical_path, node_type, modified_at, file_size, is_pinned
FROM tree_nodes 
WHERE is_deleted = 0
ORDER BY modified_at DESC
LIMIT 50;

CREATE VIEW pinned_items AS
SELECT id, name, canonical_path, node_type, is_pinned, sort_order
FROM tree_nodes 
WHERE is_pinned = 1 AND is_deleted = 0
ORDER BY sort_order, name;

CREATE VIEW health_metrics AS
SELECT 'total_nodes' as metric, COUNT(*) as value, 'count' as unit
FROM tree_nodes WHERE is_deleted = 0
UNION ALL
SELECT 'deleted_nodes' as metric, COUNT(*) as value, 'count' as unit
FROM tree_nodes WHERE is_deleted = 1
UNION ALL
SELECT 'orphaned_nodes' as metric, COUNT(*) as value, 'count' as unit
FROM tree_nodes 
WHERE parent_id IS NOT NULL AND parent_id NOT IN (SELECT id FROM tree_nodes)
UNION ALL
SELECT 'database_size' as metric, page_count * page_size as value, 'bytes' as unit
FROM pragma_page_count, pragma_page_size;
";
        }
    }
    
    // =============================================================================
    // MIGRATION MODEL
    // =============================================================================
    
    public class Migration
    {
        public int Version { get; set; }
        public string Description { get; set; }
        public string UpgradeSql { get; set; }
        public string RollbackSql { get; set; }
    }
}

// Extension methods for cleaner database operations
namespace Microsoft.Data.Sqlite
{
    public static class SqliteConnectionExtensions
    {
        public static async Task<T> ExecuteScalarAsync<T>(this SqliteConnection connection, string sql, object parameters = null, SqliteTransaction transaction = null)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = transaction;
            
            if (parameters != null)
            {
                foreach (var prop in parameters.GetType().GetProperties())
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@" + prop.Name;
                    parameter.Value = prop.GetValue(parameters) ?? DBNull.Value;
                    command.Parameters.Add(parameter);
                }
            }
            
            var result = await command.ExecuteScalarAsync();
            
            if (result == null || result == DBNull.Value)
                return default(T);
                
            return (T)Convert.ChangeType(result, typeof(T));
        }
        
        public static async Task<int> ExecuteAsync(this SqliteConnection connection, string sql, object parameters = null, SqliteTransaction transaction = null)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = transaction;
            
            if (parameters != null)
            {
                foreach (var prop in parameters.GetType().GetProperties())
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@" + prop.Name;
                    parameter.Value = prop.GetValue(parameters) ?? DBNull.Value;
                    command.Parameters.Add(parameter);
                }
            }
            
            return await command.ExecuteNonQueryAsync();
        }
    }
}
