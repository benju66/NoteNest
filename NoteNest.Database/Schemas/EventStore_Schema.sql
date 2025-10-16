-- ============================================================================
-- NoteNest Event Store Database Schema
-- ============================================================================
-- Location: %LocalAppData%\NoteNest\events.db
-- Purpose: Immutable event log - single source of truth
-- Pattern: Event Sourcing - all state changes stored as events
-- ============================================================================

PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA foreign_keys = ON;
PRAGMA cache_size = -10000;  -- 10MB cache
PRAGMA temp_store = MEMORY;

-- ============================================================================
-- EVENTS TABLE - Immutable append-only log
-- ============================================================================

CREATE TABLE events (
    event_id INTEGER PRIMARY KEY AUTOINCREMENT,
    aggregate_id TEXT NOT NULL,
    aggregate_type TEXT NOT NULL,  -- 'Note', 'Category', 'Todo', 'Tag'
    event_type TEXT NOT NULL,      -- 'NoteCreated', 'TagAdded', etc.
    event_data TEXT NOT NULL,      -- JSON serialized event
    metadata TEXT NOT NULL,        -- JSON: user, timestamp, correlation_id, etc.
    sequence_number INTEGER NOT NULL,  -- Version within aggregate
    stream_position INTEGER NOT NULL,  -- Global position in event stream
    created_at INTEGER NOT NULL,   -- Unix timestamp
    
    UNIQUE(aggregate_id, sequence_number)
);

-- Indexes for fast queries
CREATE INDEX idx_events_aggregate ON events(aggregate_id, sequence_number);
CREATE INDEX idx_events_type ON events(aggregate_type, event_type);
CREATE INDEX idx_events_stream ON events(stream_position);
CREATE INDEX idx_events_created ON events(created_at DESC);

-- ============================================================================
-- SNAPSHOTS TABLE - Performance optimization
-- ============================================================================

CREATE TABLE snapshots (
    snapshot_id INTEGER PRIMARY KEY AUTOINCREMENT,
    aggregate_id TEXT NOT NULL,
    aggregate_type TEXT NOT NULL,
    version INTEGER NOT NULL,  -- Sequence number at snapshot
    state TEXT NOT NULL,       -- JSON serialized aggregate state
    created_at INTEGER NOT NULL,
    
    UNIQUE(aggregate_id, version)
);

CREATE INDEX idx_snapshots_aggregate ON snapshots(aggregate_id, version DESC);

-- ============================================================================
-- STREAM POSITION - Global event sequence
-- ============================================================================

CREATE TABLE stream_position (
    id INTEGER PRIMARY KEY CHECK (id = 1),  -- Singleton table
    current_position INTEGER NOT NULL DEFAULT 0
);

INSERT INTO stream_position (id, current_position) VALUES (1, 0);

-- ============================================================================
-- PROJECTION CHECKPOINTS - Track projection progress
-- ============================================================================

CREATE TABLE projection_checkpoints (
    projection_name TEXT PRIMARY KEY,
    last_processed_position INTEGER NOT NULL,
    last_processed_at INTEGER NOT NULL,
    status TEXT NOT NULL DEFAULT 'running',  -- 'running', 'stopped', 'rebuilding', 'error'
    error_message TEXT,
    
    CHECK (status IN ('running', 'stopped', 'rebuilding', 'error'))
);

-- ============================================================================
-- EVENT METADATA - Schema versioning
-- ============================================================================

CREATE TABLE event_schema_versions (
    event_type TEXT PRIMARY KEY,
    current_version INTEGER NOT NULL,
    description TEXT
);

-- ============================================================================
-- INITIALIZATION
-- ============================================================================

CREATE TABLE schema_version (
    version INTEGER PRIMARY KEY,
    applied_at INTEGER NOT NULL,
    description TEXT
);

INSERT INTO schema_version (version, applied_at, description)
VALUES (1, strftime('%s', 'now'), 'Initial event store schema');

