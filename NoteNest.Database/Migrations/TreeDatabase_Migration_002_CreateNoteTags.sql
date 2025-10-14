-- ============================================================================
-- Tree Database Migration 002: Create note_tags table
-- ============================================================================
-- Location: Apply to tree.db (%LocalAppData%\NoteNest\tree.db)
-- Purpose: Store tags for notes (auto-generated from folder path + manual)
-- Date: 2025-10-14
-- Author: Tag MVP Implementation
-- ============================================================================

BEGIN TRANSACTION;

-- Create note_tags table
CREATE TABLE IF NOT EXISTS note_tags (
    note_id TEXT NOT NULL,               -- References tree_nodes.id
    tag TEXT NOT NULL COLLATE NOCASE,    -- Case-insensitive tag matching
    is_auto INTEGER NOT NULL DEFAULT 0,  -- 1 = auto-generated, 0 = manual
    created_at INTEGER NOT NULL,         -- Unix timestamp
    
    PRIMARY KEY (note_id, tag),
    FOREIGN KEY (note_id) REFERENCES tree_nodes(id) ON DELETE CASCADE,
    CHECK (is_auto IN (0, 1)),
    CHECK (tag != '')
);

-- Create indexes for note_tags
-- Index 1: Find all tags for a note (most common query)
CREATE INDEX IF NOT EXISTS idx_note_tags_note ON note_tags(note_id);

-- Index 2: Find all notes with a specific tag (search)
CREATE INDEX IF NOT EXISTS idx_note_tags_tag ON note_tags(tag);

-- Index 3: Query auto-generated tags separately
CREATE INDEX IF NOT EXISTS idx_note_tags_auto ON note_tags(is_auto);

-- Index 4: Combined index for "find all auto tags for note"
CREATE INDEX IF NOT EXISTS idx_note_tags_note_auto ON note_tags(note_id, is_auto);

-- Update schema version
UPDATE schema_version SET version = 2 WHERE version = 1;
INSERT INTO schema_version (version, applied_at, description)
VALUES (2, strftime('%s', 'now'), 'Added note_tags table for note tagging feature');

COMMIT;

-- ============================================================================
-- Verification Queries (run after migration)
-- ============================================================================
-- SELECT sql FROM sqlite_master WHERE name = 'note_tags';
-- Expected: Should show table definition

-- SELECT * FROM schema_version ORDER BY version;
-- Expected: Should show version 2 record

-- PRAGMA index_list('note_tags');
-- Expected: Should show 4 indexes

-- PRAGMA foreign_key_list('note_tags');
-- Expected: Should show foreign key to tree_nodes

-- Test insert (then delete):
-- INSERT INTO note_tags VALUES ('test-id', 'test-tag', 1, strftime('%s', 'now'));
-- SELECT * FROM note_tags;
-- DELETE FROM note_tags WHERE note_id = 'test-id';

