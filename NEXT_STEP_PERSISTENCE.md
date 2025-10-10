# 📋 Next Step: Category Persistence

**After Display Fix Works:** Add category persistence so they survive app restarts.

---

## 🎯 **THE ISSUE**

**Current:**
- Categories stored in-memory only (SmartObservableCollection)
- Lost on app restart
- User must re-add categories each time

**Goal:**
- Save selected categories
- Restore on app startup
- Seamless UX

---

## 📊 **TWO IMPLEMENTATION OPTIONS**

### **Option A: JSON Settings File** ⭐⭐⭐⭐⭐ **RECOMMENDED**

**Implementation Time:** 30 minutes  
**Complexity:** Low  
**Good For:** Manual selection mode

**File Location:**
```
%LocalAppData%\NoteNest\.plugins\NoteNest.TodoPlugin\selected-categories.json
```

**Implementation:**
```csharp
// In CategoryStore.cs
private readonly string _settingsFile = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "NoteNest", ".plugins", "NoteNest.TodoPlugin", "selected-categories.json");

public async Task SaveAsync()
{
    var data = _categories.Select(c => new
    {
        c.Id,
        c.OriginalParentId,
        c.Name,
        c.DisplayPath,
        c.Order
    }).ToList();
    
    var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(_settingsFile, json);
    _logger.Debug($"[CategoryStore] Saved {data.Count} categories to settings");
}

public async Task LoadAsync()
{
    if (!File.Exists(_settingsFile))
        return;
        
    var json = await File.ReadAllTextAsync(_settingsFile);
    var data = JsonSerializer.Deserialize<List<CategoryDto>>(json);
    
    using (_categories.BatchUpdate())
    {
        _categories.Clear();
        foreach (var dto in data)
        {
            _categories.Add(new Category
            {
                Id = dto.Id,
                ParentId = null,
                OriginalParentId = dto.OriginalParentId,
                Name = dto.Name,
                DisplayPath = dto.DisplayPath,
                Order = dto.Order
            });
        }
    }
    
    _logger.Info($"[CategoryStore] Loaded {data.Count} categories from settings");
}
```

**Pros:**
- ✅ Simple to implement
- ✅ Human-readable (can edit manually)
- ✅ No database schema changes
- ✅ Easy to backup/export

**Cons:**
- ⚠️ File I/O overhead (minimal)
- ⚠️ Less robust than database

---

### **Option B: Database Table** ⭐⭐⭐

**Implementation Time:** 60 minutes  
**Complexity:** Medium  
**Good For:** Future features

**Schema:**
```sql
CREATE TABLE selected_categories (
    id TEXT PRIMARY KEY,
    original_parent_id TEXT,
    name TEXT,
    display_path TEXT,
    order_index INTEGER,
    created_at INTEGER,
    
    -- References tree_nodes.id (informational)
    FOREIGN KEY (original_parent_id) REFERENCES selected_categories(id)
);

CREATE INDEX idx_selected_categories_order ON selected_categories(order_index);
```

**Pros:**
- ✅ More robust
- ✅ Integrated with todo database
- ✅ Better for queries
- ✅ Transactional

**Cons:**
- ⚠️ More complex
- ⚠️ Database migration needed
- ⚠️ Overkill for simple list

---

## 🏆 **RECOMMENDATION: Option A (JSON)**

**Why:**
1. ✅ **Fast to implement** - 30 minutes vs 60 minutes
2. ✅ **Perfect for manual selection** - Small list of user-selected categories
3. ✅ **Simple to maintain** - Easy to understand
4. ✅ **Can migrate to DB later** - If needed for features

**When to Call Save:**
- After adding category
- After removing category
- On app shutdown

**When to Call Load:**
- On CategoryStore.InitializeAsync()
- Before any category operations

---

## 📋 **IMPLEMENTATION PLAN**

### **Phase 1: Save/Load Methods (15 min)**
1. Add SaveAsync() to CategoryStore
2. Add LoadAsync() to CategoryStore  
3. Use System.Text.Json for serialization

### **Phase 2: Auto-Save Trigger (10 min)**
1. Call SaveAsync() after Add/Update/Delete
2. Call SaveAsync() on app shutdown
3. Debounce saves (don't save on every change)

### **Phase 3: Auto-Load (5 min)**
1. Call LoadAsync() in InitializeAsync()
2. Validate loaded categories still exist in tree
3. Remove orphaned categories

### **Total Time:** ~30 minutes

---

## ✅ **AFTER DISPLAY FIX WORKS**

**Test that categories appear in UI, THEN:**
1. Implement JSON persistence (Option A)
2. Test: Add categories → Close app → Reopen → Categories still there
3. Done! ✅

---

**First: Test the display fix. Then we'll add persistence.**

