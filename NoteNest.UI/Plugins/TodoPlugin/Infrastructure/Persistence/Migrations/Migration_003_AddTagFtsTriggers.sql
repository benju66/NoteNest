-- ============================================================================
-- Migration 003: Add FTS5 triggers for todo_tags table
-- ============================================================================
-- Purpose: Keep todos_fts.tags column in sync when tags are added/removed
-- Date: 2025-10-14
-- Author: Tag MVP Implementation
-- ============================================================================

BEGIN TRANSACTION;

-- Drop existing triggers if they exist (idempotent migration)
DROP TRIGGER IF EXISTS todo_tags_fts_insert;
DROP TRIGGER IF EXISTS todo_tags_fts_delete;

-- Trigger: Update FTS when tag added to todo
CREATE TRIGGER todo_tags_fts_insert 
AFTER INSERT ON todo_tags BEGIN
    UPDATE todos_fts
    SET tags = (SELECT GROUP_CONCAT(tag, ' ') FROM todo_tags WHERE todo_id = new.todo_id)
    WHERE id = new.todo_id;
END;

-- Trigger: Update FTS when tag removed from todo
CREATE TRIGGER todo_tags_fts_delete 
AFTER DELETE ON todo_tags BEGIN
    UPDATE todos_fts
    SET tags = (SELECT GROUP_CONCAT(tag, ' ') FROM todo_tags WHERE todo_id = old.todo_id)
    WHERE id = old.todo_id;
END;

-- Note: todos_fts_insert and todos_fts_update already exist and handle
-- the todos table changes. These new triggers handle todo_tags table changes.

-- Update schema version (idempotent - safe to run multiple times)
INSERT OR REPLACE INTO schema_version (version, applied_at, description)
VALUES (3, strftime('%s', 'now'), 'Added FTS5 triggers for todo_tags table');

COMMIT;

-- ============================================================================
-- Verification Query (run after migration)
-- ============================================================================
-- SELECT name, sql FROM sqlite_master 
-- WHERE type = 'trigger' AND tbl_name IN ('todo_tags', 'todos');
-- Expected: Should show todo_tags_fts_insert and todo_tags_fts_delete

-- SELECT * FROM schema_version ORDER BY version;
-- Expected: Should show version 3 record

