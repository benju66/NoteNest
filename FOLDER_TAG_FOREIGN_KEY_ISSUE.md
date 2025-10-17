# Folder Tag Foreign Key Issue - Root Cause

## 🔴 **THE PROBLEM**

**Error**: `FOREIGN KEY constraint failed`

**Why**:
```sql
-- folder_tags table in tree.db has:
FOREIGN KEY (folder_id) REFERENCES tree_nodes(id) ON DELETE CASCADE
```

**But**:
1. UI shows categories from **projections.db** (via CategoryTreeViewModel)
2. User clicks category with ID from **projections.db**
3. FolderTagRepository tries to INSERT into **tree.db** folder_tags
4. Foreign key check looks for folder_id in **tree.db** tree_nodes
5. **ID doesn't exist there!** (Only in projections.db) ❌

**Same split-brain we've been fixing all day!**

---

## 📊 **Current State**

After migration:
- ✅ **events.db**: Has category events
- ✅ **projections.db**: Has categories in tree_view
- ❌ **tree.db**: Stale (not updated since migration)

folder_tags references tree.db, but categories only exist in projections!

---

## ✅ **SOLUTION OPTIONS**

### **Option A: Remove Foreign Key Constraint** ⭐⭐⭐ (QUICKEST)

**Modify folder_tags table** to not enforce foreign key:
```sql
-- Remove FK, add comment about referential integrity
-- folder_id references tree_view.id in projections.db (not enforced)
```

**Pros**:
- ✅ Quick fix (5 minutes)
- ✅ Keeps folder_tags in tree.db
- ✅ inherit_to_children column preserved

**Cons**:
- ⚠️ No referential integrity enforcement
- ⚠️ Could have orphaned tags if category deleted

---

### **Option B: Keep tree.db Synchronized** ⭐⭐ (COMPLEX)

**Add tree.db writes** to TreeViewProjection:
- When category created in projection, also write to tree.db
- Duplicate data in two databases

**Pros**:
- ✅ Foreign key works
- ✅ Referential integrity

**Cons**:
- ❌ Violates event sourcing (two sources of truth)
- ❌ More complexity
- ❌ Defeats purpose of projections

---

### **Option C: Move to Event-Sourced Tags** ⭐⭐⭐ (PROPER)

**Use entity_tags in projections.db**:
- Folder tags → entity_tags with entity_type='category'
- Add inherit_to_children column to entity_tags
- Fully event-sourced

**Pros**:
- ✅ Architecturally pure
- ✅ Single source of truth
- ✅ Consistent with rest of system

**Cons**:
- ⚠️ More work (schema change)
- ⚠️ Need to migrate existing folder_tags

---

## 🎯 **MY RECOMMENDATION**

### **Immediate Fix: Option A** (Remove FK)

**Why**:
- Fastest to implement
- Gets system working immediately
- Can upgrade to Option C later

**How**: Update FolderTagRepository to create table without FK if needed:

```csharp
// In FolderTagRepository constructor or init method:
await EnsureTableExistsAsync();

private async Task EnsureTableExistsAsync()
{
    using var connection = new SqliteConnection(_connectionString);
    await connection.OpenAsync();
    
    // Create folder_tags without FK constraint if missing
    await connection.ExecuteAsync(@"
        CREATE TABLE IF NOT EXISTS folder_tags_noFK (
            folder_id TEXT NOT NULL,
            tag TEXT NOT NULL COLLATE NOCASE,
            is_auto_suggested INTEGER NOT NULL DEFAULT 0,
            inherit_to_children INTEGER NOT NULL DEFAULT 1,
            created_at INTEGER NOT NULL,
            created_by TEXT DEFAULT 'user',
            PRIMARY KEY (folder_id, tag),
            CHECK (tag != ''),
            CHECK (is_auto_suggested IN (0, 1)),
            CHECK (inherit_to_children IN (0, 1))
        )");
    
    // If old table exists, migrate data
    // Then use folder_tags_noFK table
}
```

**Or simpler**: Just disable foreign keys for the insert:
```csharp
await connection.ExecuteAsync("PRAGMA foreign_keys = OFF");
// ... do insert ...
await connection.ExecuteAsync("PRAGMA foreign_keys = ON");
```

---

## ✅ **QUICK FIX IMPLEMENTATION**

Simplest solution - disable FK check temporarily:

```csharp
// In FolderTagRepository.SetFolderTagsAsync()
using var connection = new SqliteConnection(_connectionString);
await connection.OpenAsync();

// Temporarily disable foreign keys (folder IDs are from projections, not tree.db)
await connection.ExecuteAsync("PRAGMA foreign_keys = OFF");

using var transaction = connection.BeginTransaction();
try
{
    // ... existing code ...
    transaction.Commit();
}
finally
{
    // Re-enable foreign keys
    await connection.ExecuteAsync("PRAGMA foreign_keys = ON");
}
```

**Time**: 5 minutes  
**Risk**: Very low  
**Works**: Immediately

---

## 🎯 **PROPER LONG-TERM FIX**

Eventually migrate folder tags to event sourcing:
- Store in projections.db/entity_tags
- Add inherit column to entity_tags
- Full event-sourced lifecycle

**But for now**: Disable FK check gets system working!

