using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence
{
    /// <summary>
    /// Initializes the todo database with schema creation and migrations.
    /// Follows the TreeDatabaseInitializer pattern for consistency.
    /// </summary>
    public interface ITodoDatabaseInitializer
    {
        Task<bool> InitializeAsync();
        Task<int> GetCurrentSchemaVersionAsync();
        Task<bool> IsHealthyAsync();
    }
    
    public class TodoDatabaseInitializer : ITodoDatabaseInitializer
    {
        private readonly string _connectionString;
        private readonly IAppLogger _logger;
        
        public TodoDatabaseInitializer(string connectionString, IAppLogger logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> InitializeAsync()
        {
            try
            {
                _logger.Info("[TodoPlugin] Initializing todo database...");
                
                // Ensure database directory exists
                var dbPath = new SqliteConnectionStringBuilder(_connectionString).DataSource;
                var directory = Path.GetDirectoryName(dbPath);
                
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.Info($"[TodoPlugin] Created database directory: {directory}");
                }

                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                // Check if database already initialized
                var tableExists = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='todos'") > 0;
                
                if (tableExists)
                {
                    var version = await GetCurrentSchemaVersionAsync(connection);
                    _logger.Info($"[TodoPlugin] Database already initialized (version {version})");
                    return true;
                }
                
                _logger.Info("[TodoPlugin] Creating fresh database schema...");
                
                // Execute the complete schema
                var schemaScript = GetSchemaScript();
                await connection.ExecuteAsync(schemaScript);
                
                // Verify the schema was created correctly
                if (await VerifySchemaAsync(connection))
                {
                    _logger.Info("[TodoPlugin] Database schema created successfully");
                    return true;
                }
                else
                {
                    _logger.Error("[TodoPlugin] Database schema creation failed verification");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoPlugin] Failed to initialize database");
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
            catch
            {
                return 0;
            }
        }
        
        private async Task<int> GetCurrentSchemaVersionAsync(SqliteConnection connection)
        {
            try
            {
                var versionTableExists = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_version'") > 0;
                    
                if (!versionTableExists)
                    return 0;
                    
                var version = await connection.ExecuteScalarAsync<int?>(
                    "SELECT MAX(version) FROM schema_version");
                    
                return version ?? 0;
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
                
                // Quick integrity check
                var result = await connection.ExecuteScalarAsync<string>("PRAGMA integrity_check(1)");
                return result == "ok";
            }
            catch
            {
                return false;
            }
        }
        
        private async Task<bool> VerifySchemaAsync(SqliteConnection connection)
        {
            try
            {
                // Verify core tables exist
                var tables = new[] { "todos", "todo_tags", "todos_fts", "schema_version" };
                
                foreach (var table in tables)
                {
                    var exists = await connection.ExecuteScalarAsync<int>(
                        $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'") > 0;
                        
                    if (!exists)
                    {
                        _logger.Error($"[TodoPlugin] Table '{table}' was not created");
                        return false;
                    }
                }
                
                // Verify views exist
                var views = new[] { "v_today_todos", "v_overdue_todos", "v_high_priority_todos", "v_favorite_todos" };
                
                foreach (var view in views)
                {
                    var exists = await connection.ExecuteScalarAsync<int>(
                        $"SELECT COUNT(*) FROM sqlite_master WHERE type='view' AND name='{view}'") > 0;
                        
                    if (!exists)
                    {
                        _logger.Warning($"[TodoPlugin] View '{view}' was not created");
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoPlugin] Schema verification failed");
                return false;
            }
        }
        
        private string GetSchemaScript()
        {
            // Inline schema for reliability (no embedded resource issues)
            return @"
-- Todo Plugin Database Schema
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA cache_size = -8000;
PRAGMA temp_store = MEMORY;
PRAGMA mmap_size = 134217728;

CREATE TABLE todos (
    id TEXT PRIMARY KEY NOT NULL,
    text TEXT NOT NULL,
    description TEXT,
    is_completed INTEGER NOT NULL DEFAULT 0,
    completed_date INTEGER,
    category_id TEXT,
    parent_id TEXT,
    sort_order INTEGER NOT NULL DEFAULT 0,
    indent_level INTEGER NOT NULL DEFAULT 0,
    priority INTEGER NOT NULL DEFAULT 1,
    is_favorite INTEGER NOT NULL DEFAULT 0,
    due_date INTEGER,
    due_time INTEGER,
    reminder_date INTEGER,
    recurrence_rule TEXT,
    lead_time_days INTEGER DEFAULT 0,
    source_type TEXT NOT NULL CHECK(source_type IN ('manual', 'note')),
    source_note_id TEXT,
    source_file_path TEXT,
    source_line_number INTEGER,
    source_char_offset INTEGER,
    last_seen_in_source INTEGER,
    is_orphaned INTEGER DEFAULT 0,
    created_at INTEGER NOT NULL,
    modified_at INTEGER NOT NULL,
    FOREIGN KEY (parent_id) REFERENCES todos(id) ON DELETE CASCADE,
    CHECK (is_completed IN (0, 1)),
    CHECK (is_favorite IN (0, 1)),
    CHECK (is_orphaned IN (0, 1)),
    CHECK (priority >= 0 AND priority <= 3),
    CHECK (source_type = 'note' OR (source_note_id IS NULL AND source_file_path IS NULL))
);

CREATE INDEX idx_todos_category ON todos(category_id, is_completed, sort_order);
CREATE INDEX idx_todos_due_date ON todos(due_date, is_completed) WHERE is_completed = 0 AND due_date IS NOT NULL;
CREATE INDEX idx_todos_priority ON todos(priority, due_date, is_completed) WHERE is_completed = 0;
CREATE INDEX idx_todos_favorite ON todos(is_favorite, is_completed, sort_order) WHERE is_favorite = 1 AND is_completed = 0;
CREATE INDEX idx_todos_completed ON todos(completed_date DESC) WHERE is_completed = 1;
CREATE INDEX idx_todos_source_type ON todos(source_type);
CREATE INDEX idx_todos_source_note ON todos(source_note_id, source_line_number) WHERE source_type = 'note';
CREATE INDEX idx_todos_orphaned ON todos(is_orphaned) WHERE is_orphaned = 1;
CREATE INDEX idx_todos_parent ON todos(parent_id, sort_order) WHERE parent_id IS NOT NULL;

CREATE TABLE todo_tags (
    todo_id TEXT NOT NULL,
    tag TEXT NOT NULL,
    created_at INTEGER NOT NULL,
    PRIMARY KEY (todo_id, tag),
    FOREIGN KEY (todo_id) REFERENCES todos(id) ON DELETE CASCADE
);

CREATE INDEX idx_todo_tags_tag ON todo_tags(tag);
CREATE INDEX idx_todo_tags_todo ON todo_tags(todo_id);

CREATE VIRTUAL TABLE todos_fts USING fts5(
    id UNINDEXED,
    text,
    description,
    tags,
    tokenize='porter unicode61'
);

CREATE TRIGGER todos_fts_insert AFTER INSERT ON todos BEGIN
    INSERT INTO todos_fts(id, text, description, tags)
    VALUES (
        new.id,
        new.text,
        COALESCE(new.description, ''),
        (SELECT COALESCE(GROUP_CONCAT(tag, ' '), '') FROM todo_tags WHERE todo_id = new.id)
    );
END;

CREATE TRIGGER todos_fts_update AFTER UPDATE ON todos BEGIN
    DELETE FROM todos_fts WHERE id = old.id;
    INSERT INTO todos_fts(id, text, description, tags)
    VALUES (
        new.id,
        new.text,
        COALESCE(new.description, ''),
        (SELECT COALESCE(GROUP_CONCAT(tag, ' '), '') FROM todo_tags WHERE todo_id = new.id)
    );
END;

CREATE TRIGGER todos_fts_delete AFTER DELETE ON todos BEGIN
    DELETE FROM todos_fts WHERE id = old.id;
END;

CREATE TABLE user_preferences (
    key TEXT PRIMARY KEY NOT NULL,
    value TEXT NOT NULL,
    updated_at INTEGER NOT NULL,
    CHECK (key != '')
);

CREATE INDEX idx_user_preferences_key ON user_preferences(key);

CREATE TABLE global_tags (
    tag TEXT PRIMARY KEY NOT NULL COLLATE NOCASE,
    color TEXT,
    category TEXT,
    icon TEXT,
    usage_count INTEGER NOT NULL DEFAULT 0,
    created_at INTEGER NOT NULL,
    CHECK (tag != '')
);

CREATE INDEX idx_global_tags_category ON global_tags(category);
CREATE INDEX idx_global_tags_usage ON global_tags(usage_count DESC);

CREATE TABLE schema_version (
    version INTEGER PRIMARY KEY,
    applied_at INTEGER NOT NULL,
    description TEXT
);

INSERT INTO schema_version (version, applied_at, description) 
VALUES (1, strftime('%s', 'now'), 'Initial schema with todos, tags, FTS5, and user preferences');

CREATE VIEW v_today_todos AS
SELECT * FROM todos
WHERE is_completed = 0 AND (due_date IS NULL OR date(due_date, 'unixepoch') <= date('now'))
ORDER BY priority DESC, due_date ASC, sort_order ASC;

CREATE VIEW v_overdue_todos AS
SELECT * FROM todos
WHERE is_completed = 0 AND due_date IS NOT NULL AND date(due_date, 'unixepoch') < date('now')
ORDER BY due_date ASC, priority DESC;

CREATE VIEW v_high_priority_todos AS
SELECT * FROM todos
WHERE is_completed = 0 AND priority >= 2
ORDER BY priority DESC, due_date ASC;

CREATE VIEW v_favorite_todos AS
SELECT * FROM todos
WHERE is_favorite = 1 AND is_completed = 0
ORDER BY sort_order ASC;

CREATE VIEW v_recently_completed AS
SELECT * FROM todos
WHERE is_completed = 1
ORDER BY completed_date DESC
LIMIT 100;

CREATE VIEW v_todo_stats AS
SELECT
    COUNT(*) AS total_todos,
    SUM(CASE WHEN is_completed = 0 THEN 1 ELSE 0 END) AS active_todos,
    SUM(CASE WHEN is_completed = 1 THEN 1 ELSE 0 END) AS completed_todos,
    SUM(CASE WHEN source_type = 'manual' THEN 1 ELSE 0 END) AS manual_todos,
    SUM(CASE WHEN source_type = 'note' THEN 1 ELSE 0 END) AS note_linked_todos,
    SUM(CASE WHEN is_orphaned = 1 THEN 1 ELSE 0 END) AS orphaned_todos,
    SUM(CASE WHEN due_date < strftime('%s', 'now') AND is_completed = 0 THEN 1 ELSE 0 END) AS overdue_todos,
    SUM(CASE WHEN priority >= 2 AND is_completed = 0 THEN 1 ELSE 0 END) AS high_priority_todos;
";
        }
    }
}

