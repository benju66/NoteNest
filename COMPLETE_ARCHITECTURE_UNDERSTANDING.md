# 🎯 COMPLETE ARCHITECTURE UNDERSTANDING

**Purpose:** Fully understand how categorization works before more fixes  
**Status:** Investigation in progress

---

## 📊 WHAT I NOW UNDERSTAND

### **The Category System (Dual-Layer):**

**Layer 1: Main App (tree.db)**
- All folders in file system
- Used by main note tree
- Source of truth for folder structure

**Layer 2: TodoPlugin (CategoryStore)**
- SUBSET of folders user explicitly added
- Stored in todos.db/user_preferences
- Only shows categories user wants in todo panel

**Key Insight:** TodoPlugin categories are NOT auto-synced with tree.db!

---

## 🔍 THE ACTUAL FLOW

### **When TodoSync Calls EnsureCategoryAddedAsync:**

```csharp
// Line 439-470 in TodoSyncService
await EnsureCategoryAddedAsync(categoryId.Value);
```

**This should:**
1. Check if category in CategoryStore
2. If not, query tree.db for category
3. Build display path
4. Add to CategoryStore
5. Category appears in todo panel UI

**Then:**
```csharp
await CreateTodoFromCandidate(..., categoryId);
```

**Creates todo with that CategoryId.**

---

## 🚨 POTENTIAL ISSUES

### **Issue A: EnsureCategoryAddedAsync Fails Silently**
- Queries tree.db for category
- Category not found (same path format issue?)
- Returns without adding
- Todo created with CategoryId but category not in UI

### **Issue B: CategoryId Mismatch**
- Todo has CategoryId = X
- CategoryStore has category with Id = Y  
- X != Y even though same folder
- Todo appears uncategorized

### **Issue C: CategoryStore.GetById Doesn't Match**
- Todo filtering uses categoryId
- But CategoryStore uses different ID
- Lookup fails

---

## 📋 WHAT I NEED TO INVESTIGATE

1. **Does EnsureCategoryAddedAsync actually work?**
   - Check logs for "Auto-added category"
   - Or does it fail silently?

2. **Do the CategoryIds match?**
   - Todo.CategoryId vs CategoryStore category.Id
   - Are they the same GUID?
   - Or different tracking?

3. **How does GetByCategory filter work?**
   - What ID does it use?
   - Does it match TodoItem.CategoryId?

---

**I should trace through EnsureCategoryAddedAsync and the category matching logic before proposing more fixes.**

