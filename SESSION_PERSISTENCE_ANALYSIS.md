# 🔍 Session Persistence Issue Analysis

**Issue:** Todos don't reappear after closing and reopening the app  
**Symptom:** `[TodoStore] Loaded 0 active todos from database`

---

## 📊 **The Evidence**

### **Startup Sequence (from logs):**

```
16:16:58 - NoteNest application started
16:16:59 - [TodoStore] Initializing from database...
16:16:59 - [TodoStore] Loaded 0 active todos from database  ← EMPTY!
16:17:01 - Projection TodoView catching up from 0 to 208
16:17:01 - Projection TodoView caught up: 208 events processed
```

**Key Finding:**
- TodoStore loads at 16:16:59 (0 todos)
- Projections catch up at 16:17:01 (2 seconds later!)
- **Race condition:** TodoStore loads BEFORE projections update todo_view!

---

## 🎯 **The Root Cause: Timing Issue**

**Initialization Order:**
1. ✅ App starts
2. ✅ TodoStore initializes → Loads from `todo_view`
3. ❌ todo_view is EMPTY (projections haven't run yet!)
4. ✅ TodoStore shows 0 todos
5. ✅ 2 seconds later: Projections catch up
6. ✅ todo_view NOW has data
7. ❌ But TodoStore already loaded! (won't reload automatically)

**Result:** TodoStore has empty collection, even though todo_view has data.

---

## 💡 **Why This Happens**

### **TodoProjection Position Issue:**

```
Projection TodoView catching up from 0 to 208
```

**TodoView ALWAYS starts at position 0!**

This suggests:
1. Position isn't being saved to projection_metadata
2. OR projection_metadata isn't being queried correctly
3. OR there are TWO different projections.db files

---

## 🔍 **Additional Clue**

Looking at earlier logs (line 1048-1055):

```
[TodoSync] HIERARCHICAL Level 1: Checking 'Daily Notes'
[TodoSync]   Full path: 'c:\users\burness\mynotes\notes\projects\25-117 - op iii\daily notes'
[TodoSync] Found 'Daily Notes' but not in user's todo panel - continuing up...
[TodoSync] HIERARCHICAL Level 2: Checking '25-117 - OP III'
[TodoSync]   Full path: 'c:\users\burness\mynotes\notes\projects\25-117 - op iii'
[TodoSync] ✅ MATCH! Found user's category at level 2: 25-117 - OP III (ID: b9d84b31...)
[TodoSync] Auto-categorizing 2 todos under category: b9d84b31...
[TodoSync] Reconciling 2 candidates with 2 existing todos
[ERR] [TodoSync] Failed to reconcile todos
System.NotSupportedException: Update operations not supported in query repository. Todo sync uses UpdateTodoCommand instead.
```

**There was an ERROR in the reconciliation!**

The sync tried to update existing todos but failed because TodoQueryRepository doesn't support updates!

---

## 🎯 **Two Issues**

### **Issue 1: TodoStore Loads Before Projections**
- TodoStore.InitializeAsync() calls GetAllAsync()
- This queries todo_view
- But todo_view hasn't been updated yet (projections run after)
- Result: Loads empty list

### **Issue 2: TodoView Projection Position Not Persisting**
- TodoView always catches up from position 0
- This means projection_metadata doesn't have TodoView entry
- OR it's being reset somehow
- OR querying wrong database

---

## 🚀 **Solutions**

### **Option A: Wait for Projections Before Loading TodoStore**

**Change startup order:**
1. Initialize databases
2. Run projection catch-up
3. THEN initialize TodoStore

**Pros:** Ensures data is ready
**Cons:** Slower startup

### **Option B: TodoStore Subscribes to Projection Events**

**Have TodoStore reload when:**
- Projection catch-up completes
- New todos are added

**Pros:** Reactive, always up to date
**Cons:** More complex

### **Option C: Fix TodoView Projection Position (Root Cause)**

**Why is TodoView always at position 0?**

Check:
1. Is projection_metadata table accessible from TodoProjection?
2. Is SetLastProcessedPositionAsync actually being called?
3. Is it writing to the correct database?

**This is the real fix!**

---

## 🔍 **Next Diagnostic Steps**

1. **Check if SetLastProcessedPositionAsync is called:**
   - Add logging in TodoProjection.SetLastProcessedPositionAsync
   - Verify it writes to projection_metadata

2. **Check projection_metadata table:**
   - Query: `SELECT * FROM projection_metadata WHERE projection_name = 'TodoView'`
   - See if entry exists and what position it has

3. **Check if TodoProjection uses correct database:**
   - Verify _connectionString points to projections.db (not todos.db)

---

**I'll implement diagnostic logging to find which of these is the issue.**

