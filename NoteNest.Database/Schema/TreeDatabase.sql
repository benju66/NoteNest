-- NoteNest Tree Database Schema - Complete Implementation
-- Location: LocalApplicationData/NoteNest/tree.db (NOT synced with OneDrive)
-- Purpose: High-performance metadata cache, rebuildable from Documents/NoteNest files

-- Enable foreign keys and configure for performance
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA cache_size = -64000;  -- 64MB cache
PRAGMA temp_store = MEMORY;
PRAGMA mmap_size = 268435456;  -- 256MB memory mapping

-- =============================================================================
-- CORE TREE STRUCTURE
-- =============================================================================

-- Main tree nodes table with GUID-based identity
CREATE TABLE tree_nodes (
    -- Identity (stable across file moves)
    id TEXT PRIMARY KEY,                    -- GUID generated from file path
    parent_id TEXT,                         -- GUID of parent node
    
    -- Path information (for file operations, not identity)
    canonical_path TEXT NOT NULL,           -- Normalized relative path (lowercase, forward slashes)
    display_path TEXT NOT NULL,             -- Original case for UI display
    absolute_path TEXT NOT NULL,            -- Full path for file operations
    
    -- Node information
    node_type TEXT NOT NULL CHECK(node_type IN ('category', 'note')),
    name TEXT NOT NULL,                     -- File/folder name without extension
    file_extension TEXT,                    -- .rtf, .txt, .md (null for categories)
    
    -- File metadata (cached to avoid repeated file system access)
    file_size INTEGER,                      -- Size in bytes (null for categories)
    created_at INTEGER NOT NULL,            -- Unix timestamp
    modified_at INTEGER NOT NULL,           -- Unix timestamp  
    accessed_at INTEGER,                    -- Unix timestamp (optional)
    
    -- Hash information for change detection (xxHash64 for speed)
    quick_hash TEXT,                        -- First 4KB hash for fast change detection
    full_hash TEXT,                         -- Complete file hash for integrity
    hash_algorithm TEXT DEFAULT 'xxHash64',
    hash_calculated_at INTEGER,             -- When hash was calculated
    
    -- UI state persistence
    is_expanded INTEGER DEFAULT 0,          -- Tree expansion state (0/1)
    is_pinned INTEGER DEFAULT 0,            -- Pinned status (0/1)
    is_selected INTEGER DEFAULT 0,          -- Current selection (0/1)
    
    -- Organization and customization
    sort_order INTEGER DEFAULT 0,           -- Manual sort order within parent
    color_tag TEXT,                         -- Optional color coding (#RRGGBB)
    icon_override TEXT,                     -- Custom icon identifier
    
    -- Soft delete support (for recovery)
    is_deleted INTEGER DEFAULT 0,           -- Soft delete flag (0/1)
    deleted_at INTEGER,                     -- When it was deleted (Unix timestamp)
    
    -- Extensibility and versioning
    metadata_version INTEGER DEFAULT 1,     -- Schema version for migrations
    custom_properties TEXT,                 -- JSON for extensible properties
    
    -- Constraints and relationships
    FOREIGN KEY (parent_id) REFERENCES tree_nodes(id) ON DELETE CASCADE,
    CHECK (node_type = 'category' OR file_extension IS NOT NULL),
    CHECK (is_deleted IN (0, 1)),
    CHECK (is_expanded IN (0, 1)),
    CHECK (is_pinned IN (0, 1)),
    CHECK (is_selected IN (0, 1))
);

-- =============================================================================
-- OPTIMIZED INDEXES FOR TREE OPERATIONS
-- =============================================================================

-- Primary tree traversal (parent -> children with sorting)
CREATE INDEX idx_tree_parent ON tree_nodes(parent_id, node_type, sort_order) 
    WHERE is_deleted = 0;

-- Path-based lookups for file operations
CREATE INDEX idx_tree_path ON tree_nodes(canonical_path) 
    WHERE is_deleted = 0;

-- Type-based queries (all categories, all notes)
CREATE INDEX idx_tree_type ON tree_nodes(node_type) 
    WHERE is_deleted = 0;

-- Pinned items for quick access
CREATE INDEX idx_tree_pinned ON tree_nodes(is_pinned) 
    WHERE is_pinned = 1 AND is_deleted = 0;

-- Soft-deleted items management
CREATE INDEX idx_tree_deleted ON tree_nodes(is_deleted, deleted_at) 
    WHERE is_deleted = 1;

-- Recently modified items
CREATE INDEX idx_tree_modified ON tree_nodes(modified_at DESC) 
    WHERE is_deleted = 0;

-- Hash-based change detection
CREATE INDEX idx_tree_hash ON tree_nodes(quick_hash);

-- =============================================================================
-- UI STATE PERSISTENCE
-- =============================================================================

-- Extended UI state for complex scenarios
CREATE TABLE tree_state (
    node_id TEXT PRIMARY KEY,
    is_expanded INTEGER DEFAULT 0,          -- Tree expansion state
    scroll_position REAL DEFAULT 0,         -- Scroll position in tree
    last_selected_at INTEGER,               -- When this was last selected
    view_mode TEXT,                         -- 'list', 'tree', 'grid', etc.
    custom_state TEXT,                      -- JSON for extensible state
    FOREIGN KEY (node_id) REFERENCES tree_nodes(id) ON DELETE CASCADE
);

CREATE INDEX idx_state_selected ON tree_state(last_selected_at DESC);

-- =============================================================================
-- FILE OPERATION TRACKING
-- =============================================================================

-- Track all file operations for debugging and recovery
CREATE TABLE file_operations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    operation_type TEXT NOT NULL,           -- 'create', 'update', 'delete', 'move', 'rename'
    node_id TEXT,
    old_path TEXT,                          -- Previous path (for moves/renames)
    new_path TEXT,                          -- New path (for moves/renames)
    performed_at INTEGER NOT NULL,          -- When operation was performed
    performed_by TEXT,                      -- 'user', 'sync', 'external', 'system'
    status TEXT DEFAULT 'pending',          -- 'pending', 'completed', 'failed'
    error_message TEXT,                     -- Error details if failed
    retry_count INTEGER DEFAULT 0,          -- How many times we've retried
    FOREIGN KEY (node_id) REFERENCES tree_nodes(id) ON DELETE SET NULL
);

CREATE INDEX idx_operations_status ON file_operations(status, performed_at);
CREATE INDEX idx_operations_node ON file_operations(node_id);
CREATE INDEX idx_operations_time ON file_operations(performed_at DESC);

-- =============================================================================
-- METADATA CACHE
-- =============================================================================

-- General-purpose cache for expensive computations
CREATE TABLE metadata_cache (
    key TEXT PRIMARY KEY,                   -- Cache key
    value TEXT,                            -- Cached value (JSON or simple string)
    cached_at INTEGER NOT NULL,            -- When cached
    expires_at INTEGER,                    -- When expires (null = never)
    category TEXT,                         -- Cache category for bulk operations
    size_bytes INTEGER                     -- Size for cache management
);

CREATE INDEX idx_cache_expiry ON metadata_cache(expires_at);
CREATE INDEX idx_cache_category ON metadata_cache(category);

-- =============================================================================
-- AUDIT TRAIL
-- =============================================================================

-- Complete audit trail for all database changes
CREATE TABLE audit_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    table_name TEXT NOT NULL,              -- Which table was affected
    operation TEXT NOT NULL,               -- 'INSERT', 'UPDATE', 'DELETE'
    node_id TEXT,                         -- Related node (if applicable)
    old_values TEXT,                      -- Previous values (JSON)
    new_values TEXT,                      -- New values (JSON)
    changed_at INTEGER NOT NULL,          -- When change occurred
    changed_by TEXT,                      -- Who/what made the change
    change_source TEXT,                   -- 'app', 'sync', 'external', 'migration'
    session_id TEXT,                      -- Session identifier
    INDEX idx_audit_node (node_id),
    INDEX idx_audit_time (changed_at DESC),
    INDEX idx_audit_session (session_id)
);

-- =============================================================================
-- SCHEMA VERSIONING
-- =============================================================================

-- Track schema version for safe migrations
CREATE TABLE schema_version (
    version INTEGER PRIMARY KEY,
    applied_at INTEGER NOT NULL,
    description TEXT,
    upgrade_sql TEXT,                     -- SQL to apply this version
    rollback_sql TEXT                     -- SQL to rollback this version
);

-- Insert initial version
INSERT INTO schema_version (version, applied_at, description) 
VALUES (1, strftime('%s', 'now'), 'Initial database schema with TreeNode architecture');

-- =============================================================================
-- TRIGGERS FOR AUTOMATIC MAINTENANCE
-- =============================================================================

-- Automatically update modified timestamp on changes
CREATE TRIGGER update_tree_nodes_timestamp 
AFTER UPDATE ON tree_nodes
FOR EACH ROW
WHEN NEW.is_deleted = 0 AND OLD.modified_at = NEW.modified_at
BEGIN
    UPDATE tree_nodes 
    SET modified_at = strftime('%s', 'now') 
    WHERE id = NEW.id;
END;

-- Audit logging for tree_nodes insertions
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

-- Audit logging for tree_nodes updates
CREATE TRIGGER audit_tree_nodes_update
AFTER UPDATE ON tree_nodes
FOR EACH ROW
WHEN OLD.name != NEW.name OR OLD.canonical_path != NEW.canonical_path OR OLD.parent_id != NEW.parent_id
BEGIN
    INSERT INTO audit_log (table_name, operation, node_id, old_values, new_values, changed_at, change_source)
    VALUES ('tree_nodes', 'UPDATE', NEW.id, 
        json_object(
            'name', OLD.name, 
            'canonical_path', OLD.canonical_path,
            'parent_id', OLD.parent_id
        ),
        json_object(
            'name', NEW.name, 
            'canonical_path', NEW.canonical_path,
            'parent_id', NEW.parent_id
        ),
        strftime('%s', 'now'), 'app');
END;

-- Audit logging for deletions
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

-- =============================================================================
-- VIEWS FOR COMMON QUERIES
-- =============================================================================

-- Recursive tree hierarchy with levels
CREATE VIEW tree_hierarchy AS
WITH RECURSIVE tree_cte AS (
    -- Root nodes (no parent)
    SELECT *, 0 as level, id as root_id, canonical_path as full_path
    FROM tree_nodes
    WHERE parent_id IS NULL AND is_deleted = 0
    
    UNION ALL
    
    -- Child nodes
    SELECT t.*, tc.level + 1, tc.root_id, tc.full_path || '/' || t.canonical_path
    FROM tree_nodes t
    INNER JOIN tree_cte tc ON t.parent_id = tc.id
    WHERE t.is_deleted = 0
)
SELECT * FROM tree_cte
ORDER BY root_id, level, node_type, sort_order, name;

-- Category statistics and metrics
CREATE VIEW category_stats AS
SELECT 
    p.id,
    p.name,
    p.canonical_path,
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

-- Recently modified items
CREATE VIEW recent_items AS
SELECT 
    id,
    name,
    canonical_path,
    node_type,
    modified_at,
    file_size,
    is_pinned
FROM tree_nodes 
WHERE is_deleted = 0
ORDER BY modified_at DESC
LIMIT 50;

-- Pinned items for quick access
CREATE VIEW pinned_items AS
SELECT 
    id,
    name,
    canonical_path,
    node_type,
    is_pinned,
    sort_order
FROM tree_nodes 
WHERE is_pinned = 1 AND is_deleted = 0
ORDER BY sort_order, name;

-- =============================================================================
-- PERFORMANCE ANALYSIS VIEWS
-- =============================================================================

-- Database health metrics
CREATE VIEW health_metrics AS
SELECT 
    'total_nodes' as metric,
    COUNT(*) as value,
    'count' as unit
FROM tree_nodes WHERE is_deleted = 0

UNION ALL

SELECT 
    'deleted_nodes' as metric,
    COUNT(*) as value,
    'count' as unit
FROM tree_nodes WHERE is_deleted = 1

UNION ALL

SELECT 
    'orphaned_nodes' as metric,
    COUNT(*) as value,
    'count' as unit
FROM tree_nodes 
WHERE parent_id IS NOT NULL 
AND parent_id NOT IN (SELECT id FROM tree_nodes)

UNION ALL

SELECT 
    'database_size' as metric,
    page_count * page_size as value,
    'bytes' as unit
FROM pragma_page_count, pragma_page_size;
