# 🔬 Deep Investigation - Startup Issue Root Cause

**Date:** October 10, 2025  
**Issue:** Tasks move to Uncategorized after app restart  
**Status:** INVESTIGATING

---

## 🔍 **CRITICAL LOG ANALYSIS**

### **Pattern Observed:**

**At 15:25:31 (After restart):**
```
[CategoryStore] Contains 4 categories ✅
  - Test Notes (54256f7f...)
  - Projects (64daff0e...)
  - Daily Notes (5915eb21...)
  - Meetings (944ab545...)

[CategoryTree] Found 6 uncategorized/orphaned todos ❌
[CategoryTree] Loading 1 todos for category: Daily Notes ✅
[CategoryTree] Loading 0 todos for: Projects ❌
[CategoryTree] Loading 0 todos for: Meetings ❌
[CategoryTree] Loading 0 todos for: Test Notes ❌
```

**Only Daily Notes is loading todos!**

---

## 🚨 **HYPOTHESIS: GetByCategory Returns 0**

### **Why Would GetByCategory Return 0?**

**Current Query:**
```csharp
var items = _todos.Where(t => t.CategoryId == categoryId && 
                              !t.IsOrphaned &&
                              !t.IsCompleted);
```

**Possible Reasons:**
1. ❌ `t.CategoryId != categoryId` (category ID mismatch)
2. ❌ `t.IsOrphaned == true` (orphaned flag set)
3. ❌ `t.IsCompleted == true` (completed)
4. ❌ `_todos` collection is empty (not loaded)

---

## 🔍 **TESTING HYPOTHESIS #1: Category ID Mismatch**

**Question:** Do the todos in the database have different category IDs than what's in CategoryStore?

**Evidence Needed:**
- What are the actual category_id values in todos table?
- Do they match the IDs in CategoryStore?

**If Mismatch:**
- Categories get NEW GUIDs each time they're added?
- Todos keep OLD category GUIDs?
- **Result:** category_id != categoryId → returns 0

---

## 🔍 **TESTING HYPOTHESIS #2: IsOrphaned Flag Being Set**

**Question:** Are todos getting IsOrphaned = true somehow between sessions?

**Possible Causes:**
- HandleCategoryDeletedAsync sets it? NO - sets category_id = NULL
- TodoSyncService sets it? MAYBE - during reconciliation?
- Database corruption? Unlikely

**Evidence from Logs:**
```
[TodoStore] Loaded 6 active todos from database
```

Only 6 todos loaded (includeCompleted: false).

**If IsOrphaned:**
- GetByCategory filters them out
- They all appear in Uncategorized
- **Matches observed behavior!**

---

## 🔍 **TESTING HYPOTHESIS #3: TodoSyncService Reconciliation**

### **Critical Question:** Does TodoSyncService mark todos as orphaned during startup?

**Reconciliation Logic:**
```csharp
// Phase 2: Find orphaned todos (in existing but not in candidates)
var orphanedIds = existingByStableId.Keys
    .Except(candidatesByStableId.Keys)
    .Select(id => existingByStableId[id].Id)
    .ToList();

foreach (var todoId in orphanedIds)
{
    await MarkTodoAsOrphaned(todoId);  // ← Sets IsOrphaned = true!
}
```

**When Does This Fire?**
- On every note save
- During app startup? NO - only fires on NoteSaved event
- After restart? NO - unless notes are saved

**BUT WAIT:** What if there's a background scan happening?

---

## 🔍 **TESTING HYPOTHESIS #4: GetAllAsync Filters**

**Question:** Is TodoRepository.GetAllAsync() filtering out some todos?

**Current Code:**
```csharp
var sql = includeCompleted 
    ? "SELECT * FROM todos ORDER BY sort_order ASC"
    : "SELECT * FROM todos WHERE is_completed = 0 ORDER BY sort_order ASC";
```

**Does NOT filter is_orphaned!** ✅

So orphaned todos ARE loaded into TodoStore._todos collection.

---

## 🎯 **MOST LIKELY ROOT CAUSE**

Based on log analysis:

**Hypothesis:** Todos in database have `is_orphaned = 1`

**Why:**
1. User tested soft delete during previous session
2. Todos got `IsOrphaned = true`
3. Database persisted this
4. On restart, TodoStore loads them with `IsOrphaned = true`
5. GetByCategory excludes them (`!t.IsOrphaned`)
6. CreateUncategorizedNode includes them (`t.IsOrphaned`)
7. **Result:** All appear in Uncategorized

**Logs Support This:**
```
[TodoStore] Loaded 6 active todos  ← 6 total (not completed)
[CategoryTree] Found 6 uncategorized/orphaned todos  ← ALL 6 are uncategorized!
[CategoryTree] Loading 1 todos for category: Daily Notes  ← Only 1 non-orphaned
```

**Math:** 6 total - 6 uncategorized = 0 in categories
But Daily Notes has 1... so maybe 7 total?

---

## 🔧 **VERIFICATION NEEDED**

Need to check database to confirm:
```sql
SELECT id, text, category_id, is_orphaned, source_type FROM todos WHERE is_completed = 0;
```

**Expected Finding:**
- Most/all todos have `is_orphaned = 1`
- This is from previous testing sessions
- Need to either:
  - Clear is_orphaned flags in database
  - OR understand why they're being set

---

## 🚨 **POTENTIAL BUG: CategoryDeletedEvent**

**Wait!** Let me check the event handler:

```csharp
// TodoStore.HandleCategoryDeletedAsync():355-359
foreach (var todo in affectedTodos)
{
    todo.CategoryId = null;  // ← Sets to NULL
    todo.ModifiedDate = DateTime.UtcNow;
    await UpdateAsync(todo);
}
```

**Does NOT set IsOrphaned!** This is correct.

But during testing, user deleted categories. This would have set category_id = NULL for those todos.

**On restart:**
- CategoryStore loads the categories back (user re-added them? Or they were never saved as deleted?)
- Todos still have category_id = NULL (from EventBus)
- CreateUncategorizedNode finds them (category_id == null)
- **Result:** Appear in Uncategorized

**This might be the issue!**

---

## 🎯 **ACTION REQUIRED**

Need user to provide:
1. Contents of todos.db todos table (via DB Browser or export)
2. Confirm: After creating todo in category, what does database show?
3. Confirm: After restart, what does database show?

OR

Provide PowerShell command to export the data.

---

**Analysis in progress - need database state to confirm root cause.**

