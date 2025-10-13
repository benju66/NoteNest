# 🎯 CQRS Implementation Plan - Step-by-Step Guide

**Goal:** Implement CQRS for TodoPlugin with maximum reliability  
**Time:** 9 hours  
**Confidence:** 90%  
**Approach:** Systematic, test as we go

---

## 📋 **PHASE 1: INFRASTRUCTURE** (2 hours)

### **Step 1.1: Create Application Layer Structure** (30 min)
```
Create folders:
NoteNest.UI/Plugins/TodoPlugin/Application/Commands/
  ├── CreateTodo/
  ├── CompleteTodo/
  ├── SetPriority/
  ├── SetDueDate/
  ├── UpdateTodoText/
  ├── ToggleFavorite/
  ├── MarkOrphaned/
  ├── MoveTodoToCategory/
  └── DeleteTodo/
```

**Success:** Folders created ✅

---

### **Step 1.2: Register TodoPlugin with MediatR** (30 min)

**File:** `CleanServiceConfiguration.cs`

**Change:**
```csharp
services.AddMediatR(cfg => {
    cfg.RegisterServicesFromAssembly(typeof(CreateNoteCommand).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(TodoPlugin).Assembly);  ← ADD
});

services.AddValidatorsFromAssembly(typeof(TodoPlugin).Assembly);  ← ADD
```

**Test:** Build succeeds ✅

---

### **Step 1.3: Add IMediator to ViewModels** (1 hour)

**Files to Update:**
1. TodoListViewModel.cs - Add IMediator parameter
2. TodoItemViewModel.cs - Add IMediator parameter
3. TodoSyncService.cs - Add IMediator parameter

**Pattern:**
```csharp
private readonly IMediator _mediator;

public TodoListViewModel(
    ITodoStore todoStore,
    IMediator mediator,  ← ADD
    IAppLogger logger)
{
    _mediator = mediator;
}
```

**Test:** Build succeeds, app runs ✅

---

## 📋 **PHASE 2: CREATE COMMANDS** (4 hours)

### **Commands in Priority Order:**

**Step 2.1: CreateTodoCommand** (30 min) 🔥 **CRITICAL**
- CreateTodoCommand.cs
- CreateTodoHandler.cs  
- CreateTodoValidator.cs
- **Test:** QuickAdd works

---

**Step 2.2: CompleteTodoCommand** (30 min)
- CompleteTodoCommand.cs
- CompleteTodoHandler.cs
- CompleteTodoValidator.cs (minimal)
- **Test:** Checkbox works

---

**Step 2.3: DeleteTodoCommand** (30 min)
- DeleteTodoCommand.cs
- DeleteTodoHandler.cs
- DeleteTodoValidator.cs (minimal)
- **Test:** Delete works

---

**Step 2.4: UpdateTodoTextCommand** (30 min)
- UpdateTodoTextCommand.cs
- UpdateTodoTextHandler.cs
- UpdateTodoTextValidator.cs
- **Test:** Editing works

---

**Step 2.5: SetPriorityCommand** (30 min)
- SetPriorityCommand.cs
- SetPriorityHandler.cs
- SetPriorityValidator.cs
- **Test:** Priority cycling works

---

**Step 2.6: SetDueDateCommand** (30 min)
- SetDueDateCommand.cs
- SetDueDateHandler.cs
- SetDueDateValidator.cs
- **Test:** Date picker works

---

**Step 2.7: ToggleFavoriteCommand** (30 min)
- ToggleFavoriteCommand.cs
- ToggleFavoriteHandler.cs
- ToggleFavoriteValidator.cs (minimal)
- **Test:** Favorite toggle works

---

**Step 2.8: MarkOrphanedCommand** (30 min)
- MarkOrphanedCommand.cs
- MarkOrphanedHandler.cs
- MarkOrphanedValidator.cs (minimal)
- **Test:** Sync service works

---

**Step 2.9: MoveTodoCategoryCommand** (30 min)
- MoveTodoCategoryCommand.cs
- MoveTodoCategoryHandler.cs
- MoveTodoCategoryValidator.cs
- **Test:** Category cleanup works

---

## 📋 **PHASE 3: UPDATE VIEWMODELS** (2 hours)

### **File-by-File Updates:**

**Step 3.1: TodoListViewModel.cs** (45 min)
- ExecuteQuickAdd → CreateTodoCommand
- ExecuteToggleCompletion → CompleteTodoCommand
- ExecuteDeleteTodo → DeleteTodoCommand
- **Test:** All operations work

---

**Step 3.2: TodoItemViewModel.cs** (45 min)
- ToggleCompletionAsync → CompleteTodoCommand
- ToggleFavoriteAsync → ToggleFavoriteCommand
- UpdateTextAsync → UpdateTodoTextCommand
- SetPriorityAsync → SetPriorityCommand
- SetDueDate → SetDueDateCommand
- DeleteAsync → DeleteTodoCommand
- **Test:** All item operations work

---

**Step 3.3: TodoSyncService.cs** (15 min)
- CreateTodoFromCandidate → CreateTodoCommand
- MarkTodoAsOrphaned → MarkOrphanedCommand
- **Test:** RTF sync works

---

**Step 3.4: CategoryCleanupService.cs** (15 min)
- CleanupOrphanedCategoriesAsync → MoveTodoCategoryCommand
- **Test:** Category cleanup works

---

## 📋 **PHASE 4: TESTING & POLISH** (1 hour)

### **Comprehensive Testing:**

**Step 4.1: Manual Testing** (30 min)
1. ✅ Create todo (QuickAdd)
2. ✅ Create from note [bracket]
3. ✅ Edit todo text
4. ✅ Complete/uncomplete
5. ✅ Set priority
6. ✅ Set due date
7. ✅ Toggle favorite
8. ✅ Delete todo
9. ✅ Category operations
10. ✅ **Restart persistence**

**Step 4.2: Error Handling** (15 min)
- Try invalid inputs
- Verify validation messages
- Check error logging
- Confirm rollback works

**Step 4.3: Performance Check** (15 min)
- Create 100 todos
- Test responsiveness
- Check memory usage

---

## ✅ **SUCCESS CRITERIA**

**Must Pass:**
- ✅ All 9 commands execute correctly
- ✅ Validation rules enforce correctly
- ✅ Error messages are user-friendly
- ✅ Domain events are published
- ✅ Logging is automatic
- ✅ **Restart persistence still works**
- ✅ Build passes
- ✅ No regressions in existing features

---

## 🚨 **ROLLBACK PLAN**

**If Major Issues:**
```bash
git stash  # Save work
git checkout master  # Return to working version
git stash pop  # Review what went wrong
```

**Current master has:**
- ✅ Working persistence
- ✅ Working UI features
- ✅ Everything functional

**Can always rollback!** ✅

---

## ⏱️ **TIME BREAKDOWN**

Phase 1: Infrastructure (2 hrs)  
Phase 2: Commands (4 hrs)  
Phase 3: ViewModels (2 hrs)  
Phase 4: Testing (1 hr)  

**Total:** 9 hours

**Breaks:**
- After Phase 1: Verify build
- After each 3 commands: Test
- After Phase 3: Full test
- After Phase 4: Done!

---

## 🎯 **EXECUTION STRATEGY**

**Incremental with Testing:**
1. Build infrastructure → Test build
2. Create 3 commands → Test those features
3. Create 3 more → Test those
4. Create final 3 → Test all
5. Update ViewModels → Test everything
6. Polish → Final test

**Not:** Build everything then test (risky!)  
**But:** Test after every 2-3 hours (safe!)

---

## ✅ **CONFIDENCE: 90%**

**Ready to execute with:**
- Complete scope understanding
- Clear step-by-step plan
- Testing checkpoints
- Rollback strategy
- Realistic time estimate

**After testing:** 95% ✅

---

**Plan complete. Ready to proceed with implementation!** 🚀

