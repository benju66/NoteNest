-- ============================================================================
-- NoteNest Todo Plugin Database Schema
-- ============================================================================
-- Location: %LocalAppData%\NoteNest\.plugins\NoteNest.TodoPlugin\todos.db
-- Purpose: Plugin-isolated todo storage with performance optimization
-- Pattern: Follows tree.db architecture (rebuildable for note-linked todos)
-- ============================================================================

-- Performance and reliability settings
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA cache_size = -8000;        -- 8MB cache
PRAGMA temp_store = MEMORY;
PRAGMA mmap_size = 134217728;     -- 128MB memory mapping

-- ============================================================================
-- CORE TODO TABLE
-- ============================================================================

CREATE TABLE todos (
    -- Identity
    id TEXT PRIMARY KEY NOT NULL,
    
    -- Content
    text TEXT NOT NULL,
    description TEXT,
    
    -- Status
    is_completed INTEGER NOT NULL DEFAULT 0,
    completed_date INTEGER,                  -- Unix timestamp
    
    -- Organization
    category_id TEXT,                        -- References tree_nodes.id (informational)
    parent_id TEXT,                          -- Parent todo (subtasks)
    sort_order INTEGER NOT NULL DEFAULT 0,
    indent_level INTEGER NOT NULL DEFAULT 0,
    
    -- Priority and importance
    priority INTEGER NOT NULL DEFAULT 1,     -- 0=Low, 1=Normal, 2=High, 3=Urgent
    is_favorite INTEGER NOT NULL DEFAULT 0,
    
    -- Scheduling
    due_date INTEGER,                        -- Unix timestamp
    due_time INTEGER,                        -- Minutes since midnight
    reminder_date INTEGER,                   -- Unix timestamp
    recurrence_rule TEXT,                    -- JSON for recurrence (future)
    lead_time_days INTEGER DEFAULT 0,
    
    -- Source tracking (dual source architecture)
    source_type TEXT NOT NULL CHECK(source_type IN ('manual', 'note')),
    
    -- For note-linked todos (rebuildable from RTF files)
    source_note_id TEXT,                     -- Note GUID from tree_nodes
    source_file_path TEXT,                   -- Full path to RTF file
    source_line_number INTEGER,              -- Line number in RTF
    source_char_offset INTEGER,              -- Character offset in RTF
    last_seen_in_source INTEGER,             -- Last time found during scan
    is_orphaned INTEGER DEFAULT 0,           -- Source deleted but todo kept
    
    -- Timestamps
    created_at INTEGER NOT NULL,
    modified_at INTEGER NOT NULL,
    
    -- Constraints
    FOREIGN KEY (parent_id) REFERENCES todos(id) ON DELETE CASCADE,
    CHECK (is_completed IN (0, 1)),
    CHECK (is_favorite IN (0, 1)),
    CHECK (is_orphaned IN (0, 1)),
    CHECK (priority >= 0 AND priority <= 3),
    CHECK (source_type = 'note' OR (source_note_id IS NULL AND source_file_path IS NULL))
);

-- ============================================================================
-- PERFORMANCE INDEXES
-- ============================================================================

-- Primary query patterns
CREATE INDEX idx_todos_category ON todos(category_id, is_completed, sort_order);

CREATE INDEX idx_todos_due_date ON todos(due_date, is_completed) 
    WHERE is_completed = 0 AND due_date IS NOT NULL;

CREATE INDEX idx_todos_priority ON todos(priority, due_date, is_completed) 
    WHERE is_completed = 0;

CREATE INDEX idx_todos_favorite ON todos(is_favorite, is_completed, sort_order) 
    WHERE is_favorite = 1 AND is_completed = 0;

CREATE INDEX idx_todos_completed ON todos(completed_date DESC) 
    WHERE is_completed = 1;

-- Source tracking indexes
CREATE INDEX idx_todos_source_type ON todos(source_type);
CREATE INDEX idx_todos_source_note ON todos(source_note_id, source_line_number) 
    WHERE source_type = 'note';
CREATE INDEX idx_todos_orphaned ON todos(is_orphaned) 
    WHERE is_orphaned = 1;

-- Parent-child relationship
CREATE INDEX idx_todos_parent ON todos(parent_id, sort_order) 
    WHERE parent_id IS NOT NULL;

-- ============================================================================
-- TAGS TABLE (Many-to-many)
-- ============================================================================

CREATE TABLE todo_tags (
    todo_id TEXT NOT NULL,
    tag TEXT NOT NULL,
    created_at INTEGER NOT NULL,
    
    PRIMARY KEY (todo_id, tag),
    FOREIGN KEY (todo_id) REFERENCES todos(id) ON DELETE CASCADE
);

CREATE INDEX idx_todo_tags_tag ON todo_tags(tag);
CREATE INDEX idx_todo_tags_todo ON todo_tags(todo_id);

-- ============================================================================
-- FULL-TEXT SEARCH (FTS5)
-- ============================================================================

-- FTS5 virtual table for fast text search
CREATE VIRTUAL TABLE todos_fts USING fts5(
    id UNINDEXED,
    text,
    description,
    tags,
    tokenize='porter unicode61',
    content='todos',
    content_rowid='rowid'
);

-- Triggers to keep FTS index in sync
CREATE TRIGGER todos_fts_insert AFTER INSERT ON todos BEGIN
    INSERT INTO todos_fts(rowid, id, text, description, tags)
    SELECT 
        rowid,
        new.id,
        new.text,
        new.description,
        (SELECT GROUP_CONCAT(tag, ' ') FROM todo_tags WHERE todo_id = new.id);
END;

CREATE TRIGGER todos_fts_update AFTER UPDATE ON todos BEGIN
    UPDATE todos_fts 
    SET text = new.text,
        description = new.description,
        tags = (SELECT GROUP_CONCAT(tag, ' ') FROM todo_tags WHERE todo_id = new.id)
    WHERE id = new.id;
END;

CREATE TRIGGER todos_fts_delete AFTER DELETE ON todos BEGIN
    DELETE FROM todos_fts WHERE id = old.id;
END;

-- ============================================================================
-- USER PREFERENCES TABLE (UI State, Settings, Selected Categories)
-- ============================================================================

CREATE TABLE user_preferences (
    key TEXT PRIMARY KEY NOT NULL,
    value TEXT NOT NULL,              -- JSON for flexibility
    updated_at INTEGER NOT NULL,
    
    CHECK (key != '')
);

-- Store selected categories as JSON array
-- Key: 'selected_categories'
-- Value: JSON array of category objects with id, originalParentId, name, displayPath, isExpanded

CREATE INDEX idx_user_preferences_key ON user_preferences(key);

-- ============================================================================
-- GLOBAL TAG VOCABULARY (Shared between Notes and Todos)
-- ============================================================================

CREATE TABLE global_tags (
    tag TEXT PRIMARY KEY NOT NULL COLLATE NOCASE,
    color TEXT,                       -- Hex color: #FF5733
    category TEXT,                    -- Work, Personal, Project, etc.
    icon TEXT,                        -- Emoji or icon identifier
    usage_count INTEGER NOT NULL DEFAULT 0,
    created_at INTEGER NOT NULL,
    
    CHECK (tag != '')
);

CREATE INDEX idx_global_tags_category ON global_tags(category);
CREATE INDEX idx_global_tags_usage ON global_tags(usage_count DESC);

-- ============================================================================
-- SCHEMA VERSION TABLE
-- ============================================================================

CREATE TABLE schema_version (
    version INTEGER PRIMARY KEY,
    applied_at INTEGER NOT NULL,
    description TEXT
);

INSERT INTO schema_version (version, applied_at, description) 
VALUES (1, strftime('%s', 'now'), 'Initial schema with todos, tags, FTS5, and user preferences');

-- ============================================================================
-- VIEWS FOR COMMON QUERIES
-- ============================================================================

-- Today's todos (due today or overdue)
CREATE VIEW v_today_todos AS
SELECT * FROM todos
WHERE is_completed = 0
  AND (
    due_date IS NULL 
    OR date(due_date, 'unixepoch') <= date('now')
  )
ORDER BY priority DESC, due_date ASC, sort_order ASC;

-- Overdue todos
CREATE VIEW v_overdue_todos AS
SELECT * FROM todos
WHERE is_completed = 0
  AND due_date IS NOT NULL
  AND date(due_date, 'unixepoch') < date('now')
ORDER BY due_date ASC, priority DESC;

-- High priority todos
CREATE VIEW v_high_priority_todos AS
SELECT * FROM todos
WHERE is_completed = 0
  AND priority >= 2
ORDER BY priority DESC, due_date ASC;

-- Favorite todos
CREATE VIEW v_favorite_todos AS
SELECT * FROM todos
WHERE is_favorite = 1
  AND is_completed = 0
ORDER BY sort_order ASC;

-- Recently completed
CREATE VIEW v_recently_completed AS
SELECT * FROM todos
WHERE is_completed = 1
ORDER BY completed_date DESC
LIMIT 100;

-- ============================================================================
-- STATISTICS VIEW
-- ============================================================================

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

