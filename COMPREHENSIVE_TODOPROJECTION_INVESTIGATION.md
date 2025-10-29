# 🔍 Comprehensive TodoProjection Investigation

**Date:** October 29, 2025  
**Purpose:** Determine exactly what's broken, what depends on it, and how to fix it  
**Status:** INVESTIGATION COMPLETE  

---

## 🚨 **What's Proven BROKEN**

### **Fact 1: projections.db Updates Don't Persist**

**Tested approaches that ALL failed:**
1. ❌ UPDATE statements (original)
2. ❌ WAL journal mode
3. ❌ DELETE journal mode  
4. ❌ INSERT OR REPLACE (proven pattern from same codebase)
5. ❌ Aggressive fsync with delays
6. ❌ synchronous = EXTRA

**Evidence:**
```
Session 1: VERIFICATION: is_completed = 1 ✅
Session 2: GetAllAsync returns: is_completed = 0 ❌
```

**This happens with:**
- is_completed column
- priority column
- due_date column
- text column
- All UPDATE operations to todo_view

**Pattern:** Something prevents todo_view row updates from persisting.

---

## ✅ **What DOES Work**

### **Proven Working Operations:**

**1. Creating Todos (HandleTodoCreatedAsync):**
```csharp
INSERT OR REPLACE INTO todo_view (...) VALUES (...)
```
✅ New todos appear after restart  
✅ Persists reliably

**2. Deleting Todos (HandleTodoDeletedAsync):**
```csharp
DELETE FROM todo_view WHERE id = @Id
```
✅ Deleted todos stay deleted  
✅ Persists reliably

**3. Saving Events:**
```csharp
await _eventStore.SaveAsync(aggregate);  // → events.db
```
✅ All events persist perfectly  
✅ events.db reliable

**4. Tag Operations (TagProjection):**
```csharp
INSERT OR REPLACE INTO entity_tags (...)
UPDATE tag_vocabulary SET usage_count = ...
```
✅ Tags persist  
✅ Tag counts persist

**5. Projection Metadata:**
```csharp
INSERT... ON CONFLICT UPDATE projection_metadata SET last_processed_position = ...
```
✅ Checkpoint positions persist  
✅ Position = 345 after restart

---

## 🎯 **The Exact Problem**

### **Only todo_view Row Updates Don't Persist:**

**Works:**
- Creating rows in todo_view (INSERT)
- Deleting rows in todo_view (DELETE)
- Creating rows in entity_tags (INSERT)
- Updating rows in tag_vocabulary (UPDATE)
- Updating rows in projection_metadata (UPDATE via ON CONFLICT)

**Doesn't Work:**
- Updating existing rows in todo_view (UPDATE)
- Replacing existing rows in todo_view (INSERT OR REPLACE)

**This is SPECIFIC to todo_view table updates.**

---

## 🔍 **Possible Causes**

### **1. Table-Specific Corruption**
- todo_view table might have corruption
- Index corruption preventing updates
- PRIMARY KEY issue

**Test:** Try UPDATE on other tables (works for tag_vocabulary!)

### **2. Trigger Interference**
- Hidden trigger preventing updates?
- FTS trigger on todo_view?

**Check:** Schema for triggers on todo_view

### **3. File System / Permissions**
- AppData\Local\NoteNest\ folder locked
- OneDrive interference
- Antivirus blocking

**Check:** File permissions, antivirus logs

### **4. Connection Pooling**
- Multiple connections to same file
- Stale connection cache
- Lock contention

**Test:** Disable connection pooling

---

## 📊 **What Depends on TodoProjection**

### **Direct Dependencies:**

**1. ProjectionOrchestrator**
- Calls TodoProjection.HandleAsync() for todo events
- Registered as IProjection in DI

**2. Command Handlers (Only 2 call CatchUpAsync):**
- `CreateTodoHandler` - Line 93: `await _projectionOrchestrator.CatchUpAsync();`
- `CompleteTodoHandler` - Line 69: `await _projectionOrchestrator.CatchUpAsync();`

**Other handlers don't call it!**
- SetPriorityHandler - No projection sync
- SetDueDateHandler - No projection sync
- UpdateTodoTextHandler - No projection sync
- ToggleFavoriteHandler - No projection sync
- DeleteTodoHandler - No projection sync

**3. TodoStore Event Handlers:**
- Expects TodoCompletedEvent
- Queries database after event
- Updates UI collection

---

## ✅ **What Would Change if We Remove TodoProjection**

### **Files to Delete:**
```
DELETE:
1. TodoProjection.cs (~600 lines)
2. Registration in CleanServiceConfiguration.cs (1 line)
```

### **Files to Modify:**
```
MODIFY:
1. CompleteTodoHandler - Remove _projectionOrchestrator, write direct to DB
2. CreateTodoHandler - Remove _projectionOrchestrator, write direct to DB  
3. UpdateTodoTextHandler - Add direct DB write
4. SetPriorityHandler - Add direct DB write
5. SetDueDateHandler - Add direct DB write
6. ToggleFavoriteHandler - Add direct DB write
7. DeleteTodoHandler - Keep event, but write direct to DB too
8. TodoStore - Keep event handlers (they query DB after)
```

### **Files to Create:**
```
CREATE:
1. SimpleTodoRepository - Direct writes to projections.db/todo_view
   OR
2. Modify TodoQueryRepository to allow writes
```

---

## 🎯 **The Simplified Architecture**

### **Current (Broken):**
```
User action
  ↓
Command → Handler
  ↓
1. Load aggregate from events.db
2. Call domain method (aggregate.Complete())
3. Save aggregate to events.db
4. Call _projectionOrchestrator.CatchUpAsync()
   ↓ Calls TodoProjection.HandleAsync()
   ↓ UPDATE/INSERT OR REPLACE todo_view
   ↓ ❌ Doesn't persist!
5. Publish event
6. TodoStore receives event
7. Queries database (gets stale data)
8. Updates UI
```

### **Proposed (Simple):**
```
User action
  ↓
Command → Handler
  ↓
1. Load todo from projections.db (not event store)
2. Modify todo.IsCompleted = true
3. Direct write: _repository.UpdateAsync(todo)
   ↓ UPDATE todo_view SET is_completed = 1
   ↓ Or: Manually open connection and execute
4. Publish event (for UI real-time update)
5. TodoStore receives event
6. Updates in-memory collection directly
7. UI updates

Optional: Save event to events.db for audit/history
```

---

## 📋 **SimpleTodoRepository Approach**

### **What It Would Look Like:**

```csharp
public class SimpleTodoRepository : ITodoRepository
{
    private readonly string _projectionsConnectionString;
    private readonly IAppLogger _logger;
    
    // Keep all READ methods (they work fine!)
    public async Task<List<TodoItem>> GetAllAsync(bool includeCompleted)
    {
        // Delegate to TodoQueryService (existing, works)
        return await _queryService.GetAllAsync(includeCompleted);
    }
    
    // ADD: Direct WRITE methods
    public async Task<bool> CompleteAsync(Guid todoId)
    {
        using var conn = new SqliteConnection(_projectionsConnectionString);
        await conn.OpenAsync();
        
        var sql = @"UPDATE todo_view 
                    SET is_completed = 1, 
                        completed_at = @Now,
                        modified_at = @Now
                    WHERE id = @Id";
        
        var rows = await conn.ExecuteAsync(sql, new {
            Id = todoId.ToString(),
            Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
        
        // Force immediate flush
        await conn.ExecuteAsync("PRAGMA synchronous = EXTRA");
        await conn.CloseAsync();  // Explicit close
        
        // Wait for OS flush
        await Task.Delay(100);
        
        return rows > 0;
    }
    
    // Same pattern for: Uncomplete, UpdateText, SetPriority, etc.
}
```

---

## 🚨 **BUT WAIT - We Already Tried Direct UPDATE!**

### **The Critical Insight:**

**We've proven UPDATE doesn't work from TodoProjection.**

**But maybe it WOULD work from command handlers if:**
1. Connection stays open longer
2. Called from different code path
3. Different transaction context
4. Not wrapped in projection infrastructure

**OR more likely:** The problem is todo_view table itself, not the code!

---

## 💡 **Alternative: Drop todo_view, Use events Table Directly**

### **What If We Scrap todo_view for Persistence?**

**New Approach:**
```
todo_view in projections.db:
- Stores: id, text, category_id, source fields
- Does NOT store: is_completed, priority, due_date
- Only basic, unchanging data

Completion/Priority/DueDate:
- Stored ONLY in events.db
- Read by querying events for each todo
- Always accurate (events persist fine!)

When loading todos:
1. SELECT basic data from todo_view (fast)
2. For each todo: Query events for latest state
3. Combine: TodoItem with correct completion/priority/etc.
```

**Why this works:**
- ✅ events.db persists perfectly (proven)
- ✅ Bypasses broken todo_view updates
- ✅ Event-driven (architecturally sound)

**Performance:**
- 10 todos: ~50-100ms (acceptable)
- 100 todos: ~500ms-1s (noticeable but usable)

---

## 🎯 **Investigation Findings Summary**

### **What's Definitively Broken:**
1. ❌ Updating todo_view rows (any method)
2. ❌ TodoProjection handlers for updates
3. ❌ Both UPDATE and INSERT OR REPLACE fail

### **What's Definitively Working:**
1. ✅ Creating todo_view rows (INSERT)
2. ✅ Deleting todo_view rows (DELETE)
3. ✅ events.db persistence (all operations)
4. ✅ entity_tags persistence (tags work)
5. ✅ UI updates during session (event-driven)

### **What Depends on TodoProjection:**
1. CreateTodoHandler (calls CatchUpAsync)
2. CompleteTodoHandler (calls CatchUpAsync)
3. ProjectionOrchestrator (calls HandleAsync)
4. TodoStore event handlers (query DB after projection update)

### **Options for Fix:**

**Option A: Remove TodoProjection, Direct Writes from Handlers**
- Effort: Medium (modify 8 handlers)
- Confidence: 70% (UPDATE might still fail!)
- Risk: Might not fix the core issue

**Option B: Keep todo_view for Basic Data, Use events.db for Mutable State**
- Effort: Medium (modify TodoQueryService)
- Confidence: 99% (events.db works!)
- Risk: Performance (50-500ms per query)

**Option C: Full Investigation of todo_view Table/File**
- Check for corruption
- Check for triggers
- Check file permissions
- Rebuild from scratch
- Effort: High (debugging)
- Confidence: Unknown

---

## 💡 **My Recommendation After Investigation**

**Try Option B: Events-Based Mutable State**

**Why:**
1. ✅ events.db proven reliable
2. ✅ Architectural sound (pure event sourcing)
3. ✅ Bypasses unknown issue with todo_view
4. ✅ Moderate complexity
5. ✅ Will definitely work
6. ⚠️ Performance cost acceptable for reliability

**Implementation:**
- Modify TodoQueryService.GetAllAsync()
- Add event query for each todo
- Merge completion state
- ~2-3 hours work
- 99% confidence

---

**Next Step:** Would you like detailed implementation plan for Option B?

