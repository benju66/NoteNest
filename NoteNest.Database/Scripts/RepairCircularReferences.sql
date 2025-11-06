-- ============================================================================
-- Repair Script for Circular References in tree_view
-- ============================================================================
-- Purpose: Fix circular references that cause infinite loops
-- Usage: Run this ONLY if StartupDiagnosticsService reports circular references
-- Backup: ALWAYS backup projections.db before running
-- ============================================================================

-- Step 1: Identify self-referencing nodes (node is its own parent)
-- This is the most obvious and dangerous type of circular reference

SELECT '=== SELF-REFERENCING NODES (CRITICAL) ===' as check_type;

SELECT 
    id,
    name,
    node_type,
    display_path,
    parent_id,
    'Self-referencing: id = parent_id' as issue
FROM tree_view
WHERE id = parent_id;

-- Step 2: Find nodes with non-existent parents (orphaned nodes)

SELECT '=== ORPHANED NODES (parent_id points to non-existent node) ===' as check_type;

SELECT 
    t1.id,
    t1.name,
    t1.node_type,
    t1.display_path,
    t1.parent_id,
    'Orphaned: parent does not exist' as issue
FROM tree_view t1
WHERE t1.parent_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM tree_view t2 WHERE t2.id = t1.parent_id);

-- ============================================================================
-- MANUAL FIX OPTIONS
-- ============================================================================
-- Option 1: Set self-referencing nodes to have NULL parent (make them root nodes)
-- This is the safest fix - it breaks the cycle without data loss

-- UNCOMMENT TO RUN:
-- UPDATE tree_view
-- SET parent_id = NULL
-- WHERE id = parent_id;

-- ============================================================================
-- Option 2: Set orphaned nodes to NULL parent (make them root nodes)
-- This fixes orphaned nodes by promoting them to root level

-- UNCOMMENT TO RUN:
-- UPDATE tree_view
-- SET parent_id = NULL
-- WHERE parent_id IS NOT NULL
--   AND NOT EXISTS (SELECT 1 FROM tree_view t2 WHERE t2.id = tree_view.parent_id);

-- ============================================================================
-- Option 3: Delete self-referencing nodes entirely (DESTRUCTIVE!)
-- Only use this if the circular reference is causing critical issues
-- and you're willing to lose the affected nodes

-- UNCOMMENT TO RUN (DANGEROUS):
-- DELETE FROM tree_view
-- WHERE id = parent_id;

-- ============================================================================
-- Verification Query
-- Run this AFTER applying fixes to verify the issues are resolved
-- ============================================================================

SELECT '=== VERIFICATION: Check for remaining issues ===' as check_type;

-- Should return 0 rows
SELECT COUNT(*) as self_referencing_count
FROM tree_view
WHERE id = parent_id;

-- Should return 0 rows  
SELECT COUNT(*) as orphaned_count
FROM tree_view t1
WHERE t1.parent_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM tree_view t2 WHERE t2.id = t1.parent_id);

-- Check total nodes
SELECT COUNT(*) as total_nodes FROM tree_view;

-- Check root nodes
SELECT COUNT(*) as root_nodes FROM tree_view WHERE parent_id IS NULL;

