-- ============================================================================
-- Tree Database Migration 003: Create folder_tags table
-- ============================================================================
-- Location: Apply to tree.db (%LocalAppData%\NoteNest\tree.db)
-- Purpose: Store user-assigned tags for folders (hybrid auto-suggest + manual)
-- Date: 2025-10-14
-- Author: Hybrid Folder Tagging Implementation
-- ============================================================================
-- NOTE: C# ApplyMigrationAsync handles transaction - do not nest!
-- ============================================================================

-- Create folder_tags table
CREATE TABLE IF NOT EXISTS folder_tags (
    folder_id TEXT NOT NULL,              -- References tree_nodes.id
    tag TEXT NOT NULL COLLATE NOCASE,     -- Tag name (case-insensitive)
    is_auto_suggested INTEGER NOT NULL DEFAULT 0,  -- 1 = system suggested, 0 = user added
    inherit_to_children INTEGER NOT NULL DEFAULT 1, -- Apply to subfolders?
    created_at INTEGER NOT NULL,          -- Unix timestamp
    created_by TEXT DEFAULT 'user',       -- 'user', 'system', 'migration'
    
    PRIMARY KEY (folder_id, tag),
    FOREIGN KEY (folder_id) REFERENCES tree_nodes(id) ON DELETE CASCADE,
    CHECK (tag != ''),
    CHECK (is_auto_suggested IN (0, 1)),
    CHECK (inherit_to_children IN (0, 1))
);

-- ============================================================================
-- INDEXES FOR FOLDER TAGS
-- ============================================================================

-- Index 1: Find all tags for a folder (most common query)
CREATE INDEX IF NOT EXISTS idx_folder_tags_folder ON folder_tags(folder_id);

-- Index 2: Find all folders with a specific tag (reverse lookup)
CREATE INDEX IF NOT EXISTS idx_folder_tags_tag ON folder_tags(tag);

-- Index 3: Query auto-suggested tags separately
CREATE INDEX IF NOT EXISTS idx_folder_tags_suggested ON folder_tags(is_auto_suggested);

-- Index 4: Find folders with inheritable tags (for tree walking)
CREATE INDEX IF NOT EXISTS idx_folder_tags_inherit ON folder_tags(inherit_to_children);

-- Schema version is updated by ApplyMigrationAsync in C# code
-- (Migration framework handles version tracking)

-- ============================================================================
-- Verification Queries (run after migration)
-- ============================================================================
-- SELECT sql FROM sqlite_master WHERE name = 'folder_tags';
-- Expected: Should show table definition

-- SELECT * FROM schema_version ORDER BY version;
-- Expected: Should show version 3 record

-- PRAGMA index_list('folder_tags');
-- Expected: Should show 4 indexes

-- PRAGMA foreign_key_list('folder_tags');
-- Expected: Should show foreign key to tree_nodes

-- Test insert (then delete):
-- INSERT INTO folder_tags VALUES ('test-id', 'test-tag', 0, 1, strftime('%s', 'now'), 'user');
-- SELECT * FROM folder_tags;
-- DELETE FROM folder_tags WHERE folder_id = 'test-id';

