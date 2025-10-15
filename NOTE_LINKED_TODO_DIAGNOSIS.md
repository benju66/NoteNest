# 🐛 NOTE-LINKED TODO NOT APPEARING - DIAGNOSIS

**Issue:** When creating a todo from a note (RTF bracket), it doesn't appear in the category tree  
**Likely Cause:** Race condition in event flow  
**Status:** Investigating

---

## 🔍 **EXPECTED FLOW**

### **When You Type `[TODO: Review blueprints]` in a note:**

```
1. TodoSyncService detects bracket in RTF
   ↓
2. Gets note's parent folder as CategoryId
   ↓
3. Calls EnsureCategoryAddedAsync(categoryId)
   ├─ Adds category to CategoryStore
   ├─ Triggers CategoryTreeViewModel.OnCategoryStoreChanged
   └─ LoadCategoriesAsync() runs (FIRST REFRESH)
   ↓
4. Calls CreateTodoFromCandidate()
   ├─ Sends CreateTodoCommand with CategoryId
   ├─ CreateTodoHandler saves to database
   ├─ Publishes TodoCreatedEvent
   └─ Returns
   ↓
5. TodoStore.HandleTodoCreatedAsync()
   ├─ Loads todo from database
   ├─ Adds to _todos collection
   └─ Triggers _todos.CollectionChanged
   ↓
6. CategoryTreeViewModel.OnTodoStoreChanged
   └─ LoadCategoriesAsync() runs (SECOND REFRESH)
   ↓
7. LoadCategoriesAsync → CreateCategoryNode
   └─ Calls GetByCategory(categoryId) to get todos
   ↓
EXPECTED: Todo appears in category ✅
```

---

## 🚨 **POTENTIAL RACE CONDITIONS**

### **Issue #1: Async Fire-and-Forget in CategoryStore.Add()**

**Code:**
```csharp
// NoteNest.UI/Plugins/TodoPlugin/Services/CategoryStore.cs:171
_ = SaveToDatabaseAsync();  // Fire-and-forget

// NoteNest.UI/Plugins/TodoPlugin/Services/CategoryStore.cs:174
_ = _eventBus.PublishAsync(new CategoryAddedEvent(...));  // Fire-and-forget
```

**Problem:**
- CategoryStore.Add() returns immediately
- But database save and event publish happen in background
- CategoryTreeViewModel might refresh BEFORE these complete

### **Issue #2: Multiple Async Refreshes**

**Problem:**
- CategoryStore.Add triggers refresh #1
- TodoStore.Add triggers refresh #2
- Both call LoadCategoriesAsync() asynchronously
- They might interleave or run in wrong order

**Code:**
```csharp
// Refresh #1 (from category add)
Dispatcher.InvokeAsync(async () => await LoadCategoriesAsync());

// Refresh #2 (from todo add)
Dispatcher.InvokeAsync(async () => await LoadCategoriesAsync());
```

### **Issue #3: Todo Added to Store BEFORE Dispatcher Completes**

**Timing:**
```
T0: CategoryStore.Add() called
T1: _categories.Add(category) - CollectionChanged fires
T2: Dispatcher.InvokeAsync queued (not yet executed)
T3: EnsureCategoryAddedAsync returns
T4: CreateTodoFromCandidate called
T5: TodoCreatedEvent published
T6: TodoStore adds todo
T7: Refresh #1 finally executes
T8: Refresh #2 executes
```

**If Refresh #1 runs at T7 AFTER todo is added at T6:**
- Category exists ✅
- Todo exists ✅
- Should work ✅

**But if operations interleave differently, todo might not appear.**

---

## 🧪 **DIAGNOSTIC TESTS**

### **Test 1: Check Logs**

After creating a note-linked todo, check logs for this sequence:

```
[INFO] [TodoSync] Note is in category: <GUID> - todos will be auto-categorized
[DEBUG] [TodoSync] Category already in store: <GUID>  OR
[INFO] [CategoryStore] ADDING category: Name='...'
[INFO] [CategoryStore] ✅ Category added: ...
[INFO] [TodoSync] ✅ Created todo from note via command: "..." [auto-categorized: <GUID>]
[INFO] [TodoStore] 🎯 HandleTodoCreatedAsync STARTED for TodoId: <GUID>
[INFO] [TodoStore] ✅ Todo loaded from database: '...'
[INFO] [TodoStore] ➕ Adding todo to _todos collection...
[INFO] [TodoStore] ✅ Todo added to _todos collection: '...'
[INFO] [CategoryTree] 🔄 TodoStore.AllTodos CollectionChanged!
[INFO] [CategoryTree] ➕ New todo: ... (CategoryId: <GUID>)
[INFO] [CategoryTree] 🔄 Refreshing tree after TodoStore change...
[INFO] [CategoryTree] ✅ Tree refresh complete
```

**Expected:** Todo should appear after "Tree refresh complete"

**If Missing:**
- Check: Does todo have correct CategoryId in logs?
- Check: Is category in CategoryStore before refresh?
- Check: Does GetByCategory return the todo?

### **Test 2: Check Database**

```sql
-- Connect to todos.db
SELECT id, text, category_id, source_note_id FROM todos WHERE source_note_id IS NOT NULL;

-- Expected: Todo exists with correct category_id
```

### **Test 3: Manual Refresh**

After creating note-linked todo:
1. Click away from the category
2. Click back to the category
3. Does todo appear now?

**If YES:** Refresh issue  
**If NO:** Data issue

---

## 🔧 **PROPOSED FIXES**

### **Fix #1: Await EnsureCategoryAddedAsync Properly**

**Current:**
```csharp
await EnsureCategoryAddedAsync(categoryId.Value);
await ReconcileTodosAsync(noteNode.Id, filePath, candidates, categoryId);
```

**Problem:** This SHOULD work, but CategoryStore.Add() uses fire-and-forget

**Better:**
```csharp
// Make CategoryStore.Add() return Task
public async Task AddAsync(Category category)
{
    _categories.Add(category);
    await SaveToDatabaseAsync();  // Await
    await _eventBus.PublishAsync(new CategoryAddedEvent(...));  // Await
}
```

### **Fix #2: Force Synchronous Refresh**

**In OnCategoryStoreChanged:**
```csharp
private void OnCategoryStoreChanged(...)
{
    // Block until refresh completes
    System.Windows.Application.Current?.Dispatcher.Invoke(async () =>
    {
        await LoadCategoriesAsync();
    });
}
```

### **Fix #3: Add Delay/Retry**

**In TodoSyncService:**
```csharp
await EnsureCategoryAddedAsync(categoryId.Value);

// Small delay to ensure category refresh completes
await Task.Delay(100);

await ReconcileTodosAsync(noteNode.Id, filePath, candidates, categoryId);
```

---

## 🎯 **RECOMMENDED FIX**

I recommend **Fix #1** - Make CategoryStore.Add() properly async:

**Why:**
- Fixes root cause (fire-and-forget)
- Ensures category is fully added before continuing
- No race conditions
- Clean, reliable

**Implementation:**
1. Change `Add(Category category)` to `async Task AddAsync(Category category)`
2. Await SaveToDatabaseAsync()
3. Await PublishAsync()
4. Update callers to await AddAsync()

---

## 📝 **IMMEDIATE WORKAROUND**

If you want a quick workaround without code changes:

1. Create the todo in the note
2. Close and reopen the todo panel
3. Todo should appear (database has it, just not in UI yet)

**OR:**

1. Manually add the category to the todo panel first
2. Then create todos in notes in that folder
3. Should work because category is already there

---

Would you like me to implement **Fix #1** to properly resolve this race condition?

