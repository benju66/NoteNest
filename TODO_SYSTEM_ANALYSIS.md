# Todo System - Comprehensive Analysis & Fix Plan

## 🔴 CRITICAL ISSUES IDENTIFIED

### **Issue #1: Delete Key on Todo Deletes Source Note (CRITICAL BUG)**

**Reported Behavior:**
> "Using the delete key to delete a task created from a note does not delete the task from the todo treeview but instead deletes the note from the note treeview."

**Root Cause:**
- `TodoPanelView.xaml.cs` line 99-103: Delete key on `TodoItemViewModel` is handled but marked as "ignored"
- `e.Handled` is NOT set to `true`, causing event to bubble up
- The TreeView's Delete key event propagates to parent controls
- Likely triggers note deletion in main note tree or editor

**Impact:** SEVERE - Data loss (unintended note deletion)

**Files Affected:**
- `TodoPanelView.xaml.cs:99-103`

**Fix Required:**
```csharp
else if (selectedItem is TodoItemViewModel todoVm)
{
    // Implement proper todo deletion
    if (DataContext is TodoPanelViewModel panelVm)
    {
        await panelVm.TodoList.DeleteTodoAsync(todoVm.Id);
        _logger.Info($"[TodoPanelView] Todo deleted: {todoVm.Text}");
    }
    e.Handled = true; // ← CRITICAL: Prevent event bubbling!
}
```

---

### **Issue #2: Category Deletion Does Not Clean Up Todos**

**Reported Behavior:**
> "At one point I deleted all the previous categories from the todo pane and only added back Test Notes. Is there an issue with items not being cleaned up?"

**Root Cause:**
- `CategoryStore.Delete()` only removes category from ObservableCollection
- Todos remain in database with stale `category_id` references
- No cascading cleanup logic when categories are deleted
- Orphaned todos are invisible in UI (category no longer exists)

**Database Evidence:**
From user's database dump:
```
test task → category: 64daff0e... (Projects) ← Category deleted from todo tree
RFI 54 → category: 5915eb21... (OP III) ← Category deleted from todo tree
```

**Impact:** HIGH - Data loss appearance (todos exist but invisible)

**Files Affected:**
- `CategoryStore.cs:183-195` (Delete method)
- `TodoSyncService.cs:388-425` (No cleanup on category removal)

**Fix Required:**
1. Add `DeleteCategoryAndCleanupTodos(Guid categoryId)` to CategoryStore
2. Options for orphaned todos:
   - **Option A**: Set `category_id = NULL` (uncategorized)
   - **Option B**: Create "Orphaned Tasks" system category
   - **Option C**: Delete all todos in that category (with confirmation)
   - **RECOMMENDED**: Option A (least destructive)

---

### **Issue #3: Todos Not Appearing After App Restart**

**Reported Behavior:**
> "When I close the app and reopen it they are gone."

**Current Status:** PARTIALLY FIXED
- Added `EnsureInitializedAsync()` to CategoryTreeViewModel
- TodoStore now uses thread-safe lazy initialization

**Remaining Issue:**
- User deleted categories, so todos appear as "gone"
- Todos exist in database but have deleted category IDs
- Need to handle orphaned todos gracefully

**Diagnosis from Logs:**
```
Line 90: [TodoStore] Loaded 13 active todos from database ✅
Line 142: [CategoryTree] Building tree for root category: Test Notes
Line 143-145: No log about loading todos for "Test Notes"
```

**Why:** `GetByCategory(54256f7f-812a-47be-9de8-1570e95e7beb)` returns 0 todos because:
- All todos have different category IDs (Projects, OP III, etc.)
- Those categories were deleted by user
- Orphaned todos are not shown anywhere

**Impact:** HIGH - User confusion (data appears lost)

**Fix Required:**
- Implement "Uncategorized" or "Orphaned" system category
- Show todos with deleted category_id in special location

---

### **Issue #4: No Todo Deletion Functionality**

**Current Behavior:**
- Delete key on todo is intentionally disabled (line 102 in TodoPanelView.xaml.cs)
- Only way to "remove" a todo is to mark it completed (checkbox)
- No permanent deletion mechanism

**Files Affected:**
- `TodoPanelView.xaml.cs:99-103`
- `TodoItemViewModel.cs` (no DeleteCommand)
- `TodoListViewModel.cs` (no DeleteTodoAsync method)

**Impact:** MEDIUM - User has no way to clean up unwanted todos

**Fix Required:**
1. Add `DeleteTodoAsync(Guid id)` to TodoListViewModel
2. Update Delete key handler to actually delete
3. Consider confirmation dialog for note-linked todos
4. Update TodoStore to remove from ObservableCollection

---

### **Issue #5: Event Bubbling in TreeView**

**Root Cause:**
- TreeView has different event handling than ListBox
- KeyDown events can bubble up to parent containers
- Not setting `e.Handled = true` causes unexpected behavior

**Impact:** HIGH - Triggers unintended actions (like deleting notes)

**Files Affected:**
- `TodoPanelView.xaml.cs:77-105`

**Fix Required:**
- Always set `e.Handled = true` after handling TreeView events
- Prevent event propagation to parent controls

---

## 🟡 MEDIUM PRIORITY ISSUES

### **Issue #6: TodoSyncService Auto-Adding Categories**

**Current Behavior:**
- `EnsureCategoryAddedAsync()` auto-adds categories when todos are extracted from notes
- Creates flat structure (sets `ParentId = null`)
- User has no control over this

**Potential Problem:**
- Categories keep re-appearing after user deletes them
- Todos from old notes in deleted categories keep auto-adding categories back

**Files Affected:**
- `TodoSyncService.cs:388-425`

**Fix Required:**
- Add user preference: "Auto-add categories from note todos"
- OR: Only auto-add if note is saved AFTER category was deleted
- OR: Don't auto-add, create as uncategorized

---

### **Issue #7: Category Validation Race Condition**

**Current Behavior:**
- `CategoryStore.InitializeAsync()` validates categories against tree database
- Removes categories that no longer exist in tree
- BUT: Doesn't update todos that reference those categories

**Files Affected:**
- `CategoryStore.cs:66-78`

**Fix Required:**
- When removing orphaned category, also update todos
- Set their `category_id = NULL` or move to "Uncategorized"

---

### **Issue #8: Orphaned Todos Are Hidden**

**Current Behavior:**
- When a category is deleted from CategoryStore, its todos become invisible
- No "Orphaned" or "Uncategorized" category to catch them
- `OrphanedCategoryCleanupService` handles note-deleted todos, not category-deleted todos

**Fix Required:**
- Create system category "Uncategorized" (ID: Guid.Empty or special constant)
- Show todos with `category_id = NULL` or deleted category IDs
- Add to UI automatically

---

## 🟢 ARCHITECTURAL IMPROVEMENTS

### **Issue #9: Missing Cascading Delete Patterns**

**Current Architecture:**
- CategoryStore, TodoStore, TodoRepository are independent
- No referential integrity between them
- No cascade delete logic

**Recommended:**
- Add `ICascadeDeleteService` to handle related data cleanup
- Implement cascade patterns:
  - Delete category → Set todos' category_id = NULL
  - Delete note → Mark todos as orphaned (already implemented ✅)

---

### **Issue #10: Data Consistency Between Stores**

**Current Architecture:**
- TodoStore has in-memory `ObservableCollection`
- CategoryStore has in-memory `ObservableCollection`
- Repository has database persistence
- No transaction coordination between them

**Potential Issues:**
- CategoryStore.Delete() succeeds, but TodoRepository update fails
- Left with inconsistent state

**Recommended:**
- Implement Unit of Work pattern
- OR: Add consistency checks on startup
- OR: Add reconciliation service

---

## 📋 SYSTEMATIC FIX PLAN

### **Phase 1: Critical Bug Fixes (Immediate)**

1. **Fix Delete Key Event Handling**
   - Add `e.Handled = true` to prevent bubbling
   - Implement proper todo deletion
   - Add confirmation for note-linked todos

2. **Implement Cascading Category Deletion**
   - Update `CategoryStore.Delete()` to handle todos
   - Add `UpdateTodosCategoryToNull(Guid categoryId)` method
   - Call from TodoStore when category is deleted

3. **Add "Uncategorized" System Category**
   - Show todos with `category_id = NULL`
   - Display automatically in category tree
   - Can't be deleted (system category)

### **Phase 2: Data Consistency (High Priority)**

4. **Implement Todo Deletion**
   - Add DeleteTodoAsync to TodoListViewModel
   - Add DeleteCommand to TodoItemViewModel
   - Update UI to show delete icon/menu

5. **Fix Orphaned Todo Visibility**
   - Create "Uncategorized" category node
   - Query todos where category_id is NULL or not in CategoryStore
   - Display in special section

6. **Add Consistency Validation**
   - On startup, find todos with deleted category IDs
   - Set their category_id = NULL
   - Log warning for user

### **Phase 3: User Experience (Medium Priority)**

7. **Add Category Auto-Add Control**
   - User preference: "Auto-add categories from note todos"
   - Default: OFF (user controls categories)
   - TodoSyncService respects this setting

8. **Add Confirmation Dialogs**
   - Confirm category deletion if it contains todos
   - Show count of affected todos
   - Offer to keep/delete/move todos

9. **Add Visual Indicators**
   - Show orphaned todo icon (different from note-linked)
   - Badge on "Uncategorized" category
   - Highlight incomplete categories

### **Phase 4: Architecture (Long-term)**

10. **Implement Cascade Service**
    - Centralized cascade delete logic
    - Handle all related data cleanup
    - Maintain referential integrity

11. **Add Unit of Work Pattern**
    - Coordinate changes across stores
    - Transaction support
    - Rollback on failure

12. **Add Reconciliation Service**
    - Background service to fix inconsistencies
    - Scheduled health checks
    - Auto-repair common issues

---

## 🎯 RECOMMENDED IMMEDIATE ACTIONS

### **Action 1: Fix Delete Key Bug (30 min)**
**Priority:** CRITICAL
**Files:** `TodoPanelView.xaml.cs`

### **Action 2: Implement Uncategorized Category (1 hour)**
**Priority:** HIGH
**Files:** `CategoryTreeViewModel.cs`, `CategoryStore.cs`, `TodoStore.cs`

### **Action 3: Add Cascading Category Delete (1 hour)**
**Priority:** HIGH
**Files:** `CategoryStore.cs`, `TodoStore.cs`, `TodoRepository.cs`

### **Action 4: Add Todo Delete Functionality (1 hour)**
**Priority:** HIGH
**Files:** `TodoListViewModel.cs`, `TodoItemViewModel.cs`, `TodoPanelView.xaml.cs`

---

## 🧪 TESTING CHECKLIST

After fixes, test:
- [ ] Delete key on todo deletes todo (not note!)
- [ ] Delete category moves todos to "Uncategorized"
- [ ] App restart shows all todos (including uncategorized)
- [ ] Delete todo actually removes it from UI and database
- [ ] Orphaned todos appear in "Uncategorized"
- [ ] Can't delete "Uncategorized" system category
- [ ] Confirmation when deleting category with todos
- [ ] Event doesn't bubble up to parent controls

---

## 📝 SUMMARY

**Total Issues Found:** 10 (4 critical, 3 medium, 3 architectural)

**Estimated Fix Time:**
- Phase 1 (Critical): 3-4 hours
- Phase 2 (High): 4-5 hours
- Phase 3 (Medium): 2-3 hours
- Phase 4 (Long-term): 8-10 hours (future sprint)

**Root Causes:**
1. Missing cascade delete patterns
2. Event bubbling not handled correctly
3. No "Uncategorized" fallback for orphaned todos
4. Incomplete data consistency checks
5. User actions (delete category) don't update related data

**Key Principles for Fixes:**
- **Data Safety:** Never lose user data silently
- **Visibility:** Show orphaned/uncategorized todos clearly
- **Consistency:** Keep stores and database in sync
- **User Control:** Confirm destructive actions
- **Graceful Degradation:** Handle inconsistencies automatically

