-- ============================================================================
-- Migration 005: Local Todo Tags (Respecting Database Isolation)
-- ============================================================================
-- Purpose: Move todo tags back to todos.db where they belong
-- This respects the architecture where todos.db is permanent and tree.db is rebuildable
-- ============================================================================

PRAGMA foreign_keys = OFF;

-- Create local todo_tags table in todos.db
CREATE TABLE IF NOT EXISTS todo_tags_v2 (
    todo_id TEXT NOT NULL,
    tag TEXT NOT NULL COLLATE NOCASE,
    display_name TEXT NOT NULL,
    source TEXT NOT NULL DEFAULT 'manual',
    created_at INTEGER NOT NULL,
    created_by TEXT,
    PRIMARY KEY (todo_id, tag),
    FOREIGN KEY (todo_id) REFERENCES todos(id) ON DELETE CASCADE,
    CHECK (source IN ('manual', 'auto-path', 'auto-inherit'))
);

-- Create indexes for performance
CREATE INDEX IF NOT EXISTS idx_todo_tags_v2_tag ON todo_tags_v2(tag);
CREATE INDEX IF NOT EXISTS idx_todo_tags_v2_todo ON todo_tags_v2(todo_id);
CREATE INDEX IF NOT EXISTS idx_todo_tags_v2_created ON todo_tags_v2(created_at DESC);

-- Tag metadata table (local cache of tag properties)
CREATE TABLE IF NOT EXISTS tag_metadata (
    tag TEXT PRIMARY KEY COLLATE NOCASE,
    display_name TEXT NOT NULL,
    category TEXT,
    icon TEXT,
    color TEXT,
    description TEXT,
    usage_count INTEGER NOT NULL DEFAULT 0,
    last_used_at INTEGER,
    created_at INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_tag_metadata_category ON tag_metadata(category);
CREATE INDEX IF NOT EXISTS idx_tag_metadata_usage ON tag_metadata(usage_count DESC);

-- Migrate existing todo_tags data if it exists
INSERT OR IGNORE INTO todo_tags_v2 (todo_id, tag, display_name, source, created_at, created_by)
SELECT 
    todo_id,
    LOWER(TRIM(tag)) as tag,
    tag as display_name,
    CASE WHEN is_auto = 1 THEN 'auto-inherit' ELSE 'manual' END as source,
    created_at,
    NULL as created_by
FROM todo_tags;

-- Drop old todo_tags table and rename new one
DROP TABLE IF EXISTS todo_tags;
ALTER TABLE todo_tags_v2 RENAME TO todo_tags;

-- Update FTS triggers to use new structure
DROP TRIGGER IF EXISTS todos_fts_insert;
DROP TRIGGER IF EXISTS todos_fts_update;
DROP TRIGGER IF EXISTS todo_tags_fts_insert;
DROP TRIGGER IF EXISTS todo_tags_fts_update;
DROP TRIGGER IF EXISTS todo_tags_fts_delete;

-- Recreate FTS triggers with new schema
CREATE TRIGGER todos_fts_insert AFTER INSERT ON todos BEGIN
    INSERT INTO todos_fts(id, text, description, tags)
    VALUES (
        new.id,
        new.text,
        COALESCE(new.description, ''),
        (SELECT COALESCE(GROUP_CONCAT(display_name, ' '), '') FROM todo_tags WHERE todo_id = new.id)
    );
END;

CREATE TRIGGER todos_fts_update AFTER UPDATE ON todos BEGIN
    DELETE FROM todos_fts WHERE id = old.id;
    INSERT INTO todos_fts(id, text, description, tags)
    VALUES (
        new.id,
        new.text,
        COALESCE(new.description, ''),
        (SELECT COALESCE(GROUP_CONCAT(display_name, ' '), '') FROM todo_tags WHERE todo_id = new.id)
    );
END;

CREATE TRIGGER todo_tags_fts_insert AFTER INSERT ON todo_tags BEGIN
    UPDATE todos_fts 
    SET tags = (SELECT GROUP_CONCAT(display_name, ' ') FROM todo_tags WHERE todo_id = new.todo_id)
    WHERE id = new.todo_id;
END;

CREATE TRIGGER todo_tags_fts_update AFTER UPDATE ON todo_tags BEGIN
    UPDATE todos_fts 
    SET tags = (SELECT GROUP_CONCAT(display_name, ' ') FROM todo_tags WHERE todo_id = new.todo_id)
    WHERE id = new.todo_id;
END;

CREATE TRIGGER todo_tags_fts_delete AFTER DELETE ON todo_tags BEGIN
    UPDATE todos_fts 
    SET tags = (SELECT COALESCE(GROUP_CONCAT(display_name, ' '), '') FROM todo_tags WHERE todo_id = old.todo_id)
    WHERE id = old.todo_id;
END;

-- Update schema version
INSERT OR REPLACE INTO schema_version (version, applied_at, description)
VALUES (5, strftime('%s', 'now'), 'Local todo tags with metadata - respecting database isolation');

PRAGMA foreign_keys = ON;
