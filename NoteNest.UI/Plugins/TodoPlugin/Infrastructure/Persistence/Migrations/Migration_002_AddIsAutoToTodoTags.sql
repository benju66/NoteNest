-- ============================================================================
-- Migration 002: Add is_auto column to todo_tags
-- ============================================================================
-- Purpose: Distinguish auto-generated tags (from folder path) from manual tags
-- Date: 2025-10-14
-- Author: Tag MVP Implementation
-- ============================================================================

BEGIN TRANSACTION;

-- Step 1: Add is_auto column (defaults to 0 = manual for existing tags)
ALTER TABLE todo_tags ADD COLUMN is_auto INTEGER NOT NULL DEFAULT 0;

-- Step 2: Create index for is_auto queries
CREATE INDEX IF NOT EXISTS idx_todo_tags_auto ON todo_tags(is_auto);

-- Step 3: Create composite index for common query pattern
CREATE INDEX IF NOT EXISTS idx_todo_tags_todo_auto ON todo_tags(todo_id, is_auto);

-- Step 4: Update schema version
UPDATE schema_version SET version = 2 WHERE version = 1;
INSERT INTO schema_version (version, applied_at, description)
VALUES (2, strftime('%s', 'now'), 'Added is_auto column to todo_tags for auto-tagging feature');

COMMIT;

-- ============================================================================
-- Verification Query (run after migration)
-- ============================================================================
-- SELECT sql FROM sqlite_master WHERE name = 'todo_tags';
-- Expected: Should show is_auto column in CREATE TABLE definition

-- SELECT * FROM schema_version ORDER BY version;
-- Expected: Should show version 2 record

-- PRAGMA index_list('todo_tags');
-- Expected: Should show idx_todo_tags_auto and idx_todo_tags_todo_auto

