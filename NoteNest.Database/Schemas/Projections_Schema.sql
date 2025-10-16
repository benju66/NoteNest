-- ============================================================================
-- NoteNest Projections Database Schema
-- ============================================================================
-- Location: %LocalAppData%\NoteNest\projections.db
-- Purpose: Denormalized read models rebuilt from events
-- Pattern: CQRS Read Side - optimized for queries
-- ============================================================================

PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA foreign_keys = ON;
PRAGMA cache_size = -16000;  -- 16MB cache
PRAGMA temp_store = MEMORY;

-- ============================================================================
-- TREE VIEW PROJECTION - Replaces tree.db
-- ============================================================================

CREATE TABLE tree_view (
    id TEXT PRIMARY KEY,
    parent_id TEXT,
    canonical_path TEXT NOT NULL UNIQUE,
    display_path TEXT NOT NULL,
    node_type TEXT NOT NULL,  -- 'category', 'note'
    name TEXT NOT NULL,
    file_extension TEXT,
    is_pinned INTEGER DEFAULT 0,
    sort_order INTEGER DEFAULT 0,
    created_at INTEGER NOT NULL,
    modified_at INTEGER NOT NULL,
    
    CHECK (node_type IN ('category', 'note')),
    CHECK (is_pinned IN (0, 1))
);

CREATE INDEX idx_tree_parent ON tree_view(parent_id, sort_order);
CREATE INDEX idx_tree_path ON tree_view(canonical_path);
CREATE INDEX idx_tree_type ON tree_view(node_type);
CREATE INDEX idx_tree_pinned ON tree_view(is_pinned) WHERE is_pinned = 1;

-- ============================================================================
-- TAG VIEW PROJECTION - Unified tags for all entities
-- ============================================================================

CREATE TABLE tag_vocabulary (
    tag TEXT PRIMARY KEY COLLATE NOCASE,
    display_name TEXT NOT NULL,  -- Original casing
    usage_count INTEGER DEFAULT 0,
    first_used_at INTEGER NOT NULL,
    last_used_at INTEGER NOT NULL,
    category TEXT,
    color TEXT,
    description TEXT
);

CREATE TABLE entity_tags (
    entity_id TEXT NOT NULL,
    entity_type TEXT NOT NULL,  -- 'note', 'category', 'todo'
    tag TEXT NOT NULL COLLATE NOCASE,
    display_name TEXT NOT NULL,
    source TEXT NOT NULL,       -- 'manual', 'auto-path', 'auto-inherit'
    created_at INTEGER NOT NULL,
    
    PRIMARY KEY (entity_id, tag),
    CHECK (entity_type IN ('note', 'category', 'todo')),
    CHECK (source IN ('manual', 'auto-path', 'auto-inherit'))
);

CREATE INDEX idx_entity_tags_entity ON entity_tags(entity_id, entity_type);
CREATE INDEX idx_entity_tags_tag ON entity_tags(tag);
CREATE INDEX idx_entity_tags_type ON entity_tags(entity_type);

-- ============================================================================
-- TODO VIEW PROJECTION - Todo items with full denormalization
-- ============================================================================

CREATE TABLE todo_view (
    id TEXT PRIMARY KEY,
    text TEXT NOT NULL,
    description TEXT,
    is_completed INTEGER DEFAULT 0,
    completed_date INTEGER,
    category_id TEXT,
    category_name TEXT,          -- Denormalized for quick display
    category_path TEXT,           -- Full path for context
    parent_id TEXT,
    sort_order INTEGER DEFAULT 0,
    priority INTEGER DEFAULT 1,
    is_favorite INTEGER DEFAULT 0,
    due_date INTEGER,
    reminder_date INTEGER,
    source_type TEXT NOT NULL,   -- 'manual', 'note'
    source_note_id TEXT,
    source_file_path TEXT,
    is_orphaned INTEGER DEFAULT 0,
    created_at INTEGER NOT NULL,
    modified_at INTEGER NOT NULL,
    
    CHECK (is_completed IN (0, 1)),
    CHECK (is_favorite IN (0, 1)),
    CHECK (is_orphaned IN (0, 1)),
    CHECK (priority >= 0 AND priority <= 3),
    CHECK (source_type IN ('manual', 'note'))
);

CREATE INDEX idx_todo_category ON todo_view(category_id, is_completed, sort_order);
CREATE INDEX idx_todo_due ON todo_view(due_date, is_completed) WHERE is_completed = 0 AND due_date IS NOT NULL;
CREATE INDEX idx_todo_priority ON todo_view(priority, due_date, is_completed) WHERE is_completed = 0;
CREATE INDEX idx_todo_favorite ON todo_view(is_favorite, is_completed) WHERE is_favorite = 1 AND is_completed = 0;
CREATE INDEX idx_todo_completed ON todo_view(completed_date DESC) WHERE is_completed = 1;
CREATE INDEX idx_todo_source ON todo_view(source_note_id) WHERE source_type = 'note';

-- ============================================================================
-- CATEGORY VIEW PROJECTION - Categories with metadata
-- ============================================================================

CREATE TABLE category_view (
    id TEXT PRIMARY KEY,
    parent_id TEXT,
    name TEXT NOT NULL,
    path TEXT NOT NULL UNIQUE,
    display_path TEXT NOT NULL,
    level INTEGER NOT NULL,
    is_pinned INTEGER DEFAULT 0,
    sort_order INTEGER DEFAULT 0,
    note_count INTEGER DEFAULT 0,     -- Denormalized count
    todo_count INTEGER DEFAULT 0,     -- Denormalized count
    tag_count INTEGER DEFAULT 0,      -- Denormalized count
    created_at INTEGER NOT NULL,
    modified_at INTEGER NOT NULL,
    
    CHECK (is_pinned IN (0, 1))
);

CREATE INDEX idx_category_parent ON category_view(parent_id, sort_order);
CREATE INDEX idx_category_path ON category_view(path);
CREATE INDEX idx_category_pinned ON category_view(is_pinned) WHERE is_pinned = 1;

-- ============================================================================
-- NOTE VIEW PROJECTION - Notes with enriched metadata
-- ============================================================================

CREATE TABLE note_view (
    id TEXT PRIMARY KEY,
    category_id TEXT NOT NULL,
    category_name TEXT,           -- Denormalized
    title TEXT NOT NULL,
    file_path TEXT NOT NULL,
    is_pinned INTEGER DEFAULT 0,
    position INTEGER DEFAULT 0,
    tag_count INTEGER DEFAULT 0,  -- Denormalized
    todo_count INTEGER DEFAULT 0, -- Denormalized (from note-linked todos)
    created_at INTEGER NOT NULL,
    modified_at INTEGER NOT NULL,
    
    CHECK (is_pinned IN (0, 1))
);

CREATE INDEX idx_note_category ON note_view(category_id, position);
CREATE INDEX idx_note_pinned ON note_view(is_pinned) WHERE is_pinned = 1;
CREATE INDEX idx_note_modified ON note_view(modified_at DESC);

-- ============================================================================
-- SEARCH VIEW - Full-text search across all content
-- ============================================================================

CREATE VIRTUAL TABLE search_fts USING fts5(
    entity_id UNINDEXED,
    entity_type UNINDEXED,  -- 'note', 'todo', 'category'
    title,
    content,
    tags,  -- Space-separated tags for search
    tokenize = 'porter unicode61'
);

-- ============================================================================
-- PROJECTION METADATA
-- ============================================================================

CREATE TABLE projection_metadata (
    projection_name TEXT PRIMARY KEY,
    last_processed_position INTEGER DEFAULT 0,
    last_rebuilt_at INTEGER,
    last_updated_at INTEGER NOT NULL,
    event_count INTEGER DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'ready',
    
    CHECK (status IN ('ready', 'rebuilding', 'error'))
);

-- Insert initial metadata
INSERT INTO projection_metadata (projection_name, last_updated_at, status)
VALUES 
    ('TreeView', strftime('%s', 'now'), 'ready'),
    ('TagView', strftime('%s', 'now'), 'ready'),
    ('TodoView', strftime('%s', 'now'), 'ready'),
    ('CategoryView', strftime('%s', 'now'), 'ready'),
    ('NoteView', strftime('%s', 'now'), 'ready'),
    ('SearchView', strftime('%s', 'now'), 'ready');

-- ============================================================================
-- SCHEMA VERSION
-- ============================================================================

CREATE TABLE schema_version (
    version INTEGER PRIMARY KEY,
    applied_at INTEGER NOT NULL,
    description TEXT
);

INSERT INTO schema_version (version, applied_at, description)
VALUES (1, strftime('%s', 'now'), 'Initial projections schema');

