# Event Flow Diagnosis

## 🔍 Root Cause Found!

**Problem:** `Dispatcher.InvokeAsync()` is non-blocking!

**Current Flow:**
```csharp
// In TodoStore.HandleTodoCreatedAsync
await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
{
    _todos.Add(todo);  // Queued to UI thread, not immediate
});
// Returns immediately, before todo is actually added!
```

**What Happens:**
1. Command saves to DB ✅
2. Event published ✅
3. TodoStore.HandleTodoCreatedAsync starts ✅
4. `InvokeAsync(() => _todos.Add(todo))` QUEUES the add
5. Handler returns (todo not yet in collection)
6. CategoryTreeViewModel might rebuild before todo is added
7. Tree is built from OLD collection state
8. Later, todo gets added to collection
9. Tree rebuilds again from OnTodoStoreChanged
10. BUT... something in step 9 isn't working

**The Fix:**
Use `Invoke` (synchronous) instead of `InvokeAsync`, OR ensure proper awaiting.


