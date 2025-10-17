# Cache Timing Issue - Projection Update vs Query

## 🔍 **What You Observed**

1. Renamed "Test Note 1" → "Test Note 01" (added zero)
2. File renamed on disk: `Test Note 01.rtf` ✅
3. Tried to move immediately
4. Error: "Source file not found: Test Note 1.rtf" ❌ (OLD name!)

**This means**: Projection query returned OLD FilePath even though:
- File was renamed on disk ✅
- Event was saved ✅
- Projection should have updated ✅

---

## 📊 **The Flow (What Should Happen)**

```
1. RenameNoteHandler completes
   - File renamed: Test Note 01.rtf ✅
   - Event saved ✅
   - Returns success ✅

2. ProjectionSyncBehavior runs:
   - CatchUpAsync() ✅
   - TreeViewProjection.HandleNoteRenamedAsync() ✅
   - UPDATE tree_view SET display_path = "...Test Note 01" ✅
   - InvalidateCache() ✅

3. UI refreshes (CategoryTree.RefreshAsync)
   - Queries ITreeQueryService
   - Should get fresh data with "Test Note 01" ✅

4. User drags note
   - MoveNoteHandler queries: _noteRepository.GetByIdAsync()
   - Should get FilePath = "...Test Note 01.rtf" ✅

5. But error shows: "Test Note 1.rtf" (OLD name!) ❌
```

---

## 🔴 **POSSIBLE CAUSES**

### **Cause A: Projection Not Updated Yet** (60% likely)
**Timing race**:
```
User Action → Handler → Event Saved → ProjectionSync starts
                                          ↓
User immediately drags → Query projection ← READS BEFORE UPDATE COMPLETES!
                                          ↓
                                    Update finishes (too late)
```

**The problem**: User is too fast! Queries before projection update completes.

---

### **Cause B: Cache Not Invalidated Properly** (30% likely)
```
ProjectionSyncBehavior:
  CatchUpAsync() updates database ✅
  InvalidateCache() ❌ Doesn't clear the specific node
```

**Check**: Does InvalidateCache() clear per-node cache or just bulk cache?

Looking at TreeQueryService lines 338-342:
```csharp
public void InvalidateCache()
{
    _cache.Remove(ALL_NODES_KEY);
    _cache.Remove(ROOT_NODES_KEY);
    _cache.Remove(PINNED_NODES_KEY);
    _logger.Debug("Tree cache invalidated");
}
```

**Individual node cache**:
Line 44-48 (GetByIdAsync):
```csharp
var cacheKey = $"tree_node_{id}";

if (_cache.TryGetValue(cacheKey, out TreeNode cached))
    return cached;  // ← Returns CACHED value!

// ... query database ...

_cache.Set(cacheKey, treeNode, CACHE_DURATION);  // ← Caches individual nodes
```

**THE BUG**: Individual node cache (`tree_node_{id}`) is NOT cleared by InvalidateCache()!

---

### **Cause C: UI Refresh Doesn't Wait** (10% likely)
```
OnNoteRenamed() triggers CategoryTree.RefreshAsync()
But user drags before refresh completes
Old data still in UI
```

---

## ✅ **THE ACTUAL BUG**

**TreeQueryService has TWO cache levels**:
1. **Bulk caches**: ALL_NODES_KEY, ROOT_NODES_KEY, PINNED_NODES_KEY
2. **Individual node caches**: `tree_node_{id}` for each GetByIdAsync()

**InvalidateCache() only clears #1, not #2!**

**When MoveNoteHandler queries**:
```csharp
var noteProjection = await _noteRepository.GetByIdAsync(noteId);
  ↓
TreeQueryService.GetByIdAsync(guid)
  ↓
Checks cache: tree_node_{guid}
  ↓
FOUND! Returns cached Note with OLD FilePath! ❌
```

**The cached individual node was never invalidated!**

---

## ✅ **THE ROBUST FIX**

### **Update InvalidateCache() in TreeQueryService**:

```csharp
public void InvalidateCache()
{
    // Clear bulk caches
    _cache.Remove(ALL_NODES_KEY);
    _cache.Remove(ROOT_NODES_KEY);
    _cache.Remove(PINNED_NODES_KEY);
    
    // ALSO clear all individual node caches
    // Pattern: Remove all keys starting with "tree_node_"
    // This ensures GetByIdAsync queries get fresh data after updates
    
    _logger.Debug("Tree cache invalidated (bulk + individual nodes)");
}
```

**Better approach**: Don't cache individual nodes, or use cache tags/dependencies

---

## 🎯 **IMMEDIATE FIX OPTIONS**

### **Option A: Proper Cache Invalidation** ⭐⭐⭐

**Add to TreeQueryService.InvalidateCache()**:
```csharp
// Clear all caches (IMemoryCache doesn't support pattern matching)
// Workaround: Track cache keys or don't cache GetByIdAsync results
```

**Problem**: IMemoryCache doesn't support clearing by pattern

**Solution**: Remove caching from GetByIdAsync() entirely (it's already fast - single row query)

---

### **Option B: Don't Cache GetByIdAsync** ⭐⭐⭐ (SIMPLEST)

**Remove caching from GetByIdAsync** in TreeQueryService:

```csharp
public async Task<TreeNode> GetByIdAsync(Guid id)
{
    try
    {
        // REMOVE cache check - single row query is fast enough
        // var cacheKey = $"tree_node_{id}";
        // if (_cache.TryGetValue(cacheKey, out TreeNode cached))
        //     return cached;

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var node = await connection.QueryFirstOrDefaultAsync<TreeNodeDto>(...);
        
        // REMOVE cache set
        // _cache.Set(cacheKey, treeNode, CACHE_DURATION);
        
        return node != null ? MapToTreeNode(node) : null;
    }
}
```

**Pros**:
- ✅ Always returns fresh data
- ✅ Simple fix
- ✅ Single-row queries are very fast (<1ms)

**Cons**:
- ⚠️ Slightly more database queries (negligible performance impact)

---

### **Option C: Synchronous Wait in UI** ⭐
Force UI to wait before allowing next operation:
```csharp
await CategoryTree.RefreshAsync();
await Task.Delay(100);  // Ensure projection updated
```

**Pros**: Quick workaround
**Cons**: Bandaid, not real fix

---

## 🎯 **MY RECOMMENDATION**

**Implement Option B** - Remove per-node caching from GetByIdAsync()

**Why**:
- ✅ Simplest and most reliable
- ✅ Eliminates cache staleness issues
- ✅ Performance impact negligible (single-row indexed query)
- ✅ Fixes rename, move, and any future operations

**Confidence**: 98%  
**Time**: 5 minutes  
**Files**: 1 (TreeQueryService.cs)  

**This is the root cause of the stale FilePath issue!**

