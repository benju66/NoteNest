-- ============================================================================
-- Migration 005: Local Todo Tags (SIMPLIFIED - No Data Migration)
-- ============================================================================
-- Purpose: Update todo_tags schema to support display names and source tracking
-- Note: This version does NOT preserve existing tags - fresh start
-- ============================================================================

BEGIN TRANSACTION;

PRAGMA foreign_keys = OFF;

-- Step 1: Drop ALL triggers FIRST (prevents "no such table" errors)
DROP TRIGGER IF EXISTS todos_fts_insert;
DROP TRIGGER IF EXISTS todos_fts_update;
DROP TRIGGER IF EXISTS todos_fts_delete;
DROP TRIGGER IF EXISTS todo_tags_fts_insert;
DROP TRIGGER IF EXISTS todo_tags_fts_update;
DROP TRIGGER IF EXISTS todo_tags_fts_delete;

-- Step 2: Drop old table completely (don't preserve data)
DROP TABLE IF EXISTS todo_tags;

-- Step 3: Create new table with enhanced structure
CREATE TABLE todo_tags (
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

-- Step 4: Create indexes for performance
CREATE INDEX idx_todo_tags_tag ON todo_tags(tag);
CREATE INDEX idx_todo_tags_todo ON todo_tags(todo_id);
CREATE INDEX idx_todo_tags_created ON todo_tags(created_at DESC);

-- Step 5: Recreate ALL triggers with new schema

-- Trigger for todos table INSERT
CREATE TRIGGER todos_fts_insert AFTER INSERT ON todos BEGIN
    INSERT INTO todos_fts(id, text, description, tags)
    VALUES (
        new.id,
        new.text,
        COALESCE(new.description, ''),
        (SELECT COALESCE(GROUP_CONCAT(display_name, ' '), '') FROM todo_tags WHERE todo_id = new.id)
    );
END;

-- Trigger for todos table UPDATE
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

-- Trigger for todos table DELETE
CREATE TRIGGER todos_fts_delete AFTER DELETE ON todos BEGIN
    DELETE FROM todos_fts WHERE id = old.id;
END;

-- Trigger for todo_tags table INSERT
CREATE TRIGGER todo_tags_fts_insert AFTER INSERT ON todo_tags BEGIN
    UPDATE todos_fts 
    SET tags = (SELECT GROUP_CONCAT(display_name, ' ') FROM todo_tags WHERE todo_id = new.todo_id)
    WHERE id = new.todo_id;
END;

-- Trigger for todo_tags table UPDATE
CREATE TRIGGER todo_tags_fts_update AFTER UPDATE ON todo_tags BEGIN
    UPDATE todos_fts 
    SET tags = (SELECT GROUP_CONCAT(display_name, ' ') FROM todo_tags WHERE todo_id = new.todo_id)
    WHERE id = new.todo_id;
END;

-- Trigger for todo_tags table DELETE
CREATE TRIGGER todo_tags_fts_delete AFTER DELETE ON todo_tags BEGIN
    UPDATE todos_fts 
    SET tags = (SELECT COALESCE(GROUP_CONCAT(display_name, ' '), '') FROM todo_tags WHERE todo_id = old.todo_id)
    WHERE id = old.todo_id;
END;

-- Step 6: Update schema version
INSERT OR REPLACE INTO schema_version (version, applied_at, description)
VALUES (5, strftime('%s', 'now'), 'Local todo tags with display names - fresh start (no data migration)');

COMMIT;

PRAGMA foreign_keys = ON;