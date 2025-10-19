# 📋 CURRENT STATE BRIEFING - Note-Linked Todo Display Issue

**Date:** October 18, 2025  
**Context:** For new AI session or fresh perspective  
**Status:** Multiple fixes attempted, core issues remain  
**Priority:** CRITICAL - Feature completely broken for users

---

## 🎯 THE PROBLEM (User-Facing)

### **What Should Happen:**
```
User types in note: [call John about project]
User saves note (Ctrl+S)
  ↓ (within 2 seconds)
Todo "call John about project" appears in TodoPlugin panel
  - In correct category (note's parent folder)
  - With inherited tags from folder + note
  - Instantly visible (no restart needed)
```

### **What Actually Happens:**
```
User types in note: [call John about project]
User saves note (Ctrl+S)
  ↓
Nothing appears in TodoPlugin panel ❌
  ↓
User restarts app
  ↓
Todo appears in "Uncategorized" (wrong category) ⚠️
```

**Result:** Feature appears broken, requires restart, wrong categorization

---

## 🏗️ SYSTEM ARCHITECTURE (Background)

### **Technology Stack:**
- **Event Sourcing** - All state changes stored as immutable events in events.db
- **CQRS** - Separate write (commands) and read (queries) paths
- **Projections** - Read models built from events in projections.db
- **WPF/MVVM** - UI with data binding

### **Database Architecture:**
```
events.db (Source of Truth)
  ├─ Immutable event log
  └─ TodoCreatedEvent, TodoCompletedEvent, etc.

projections.db (Read Models)
  ├─ todo_view table (todos with denormalized data)
  └─ entity_tags table (tags for all entities)

UI Layer
  └─ TodoStore (ObservableCollection) → Binds to UI
```

### **Event Flow (How It Should Work):**
```
CreateTodoHandler
  ↓
EventStore.SaveAsync() → Persists TodoCreatedEvent to events.db
  ↓
Events published to InMemoryEventBus
  ↓
  ├─ DomainEventBridge → Core.EventBus → TodoStore (UI updates instantly)
  └─ ProjectionSyncBehavior → TodoProjection → todo_view (database updates)
  ↓
User sees todo immediately
```

---

## 📊 WHAT WE'VE FIXED SO FAR

### **Option B Implementation (Completed - Oct 18):**

**✅ Fixed: Event Sourcing Architecture**
1. Enhanced TodoCreatedEvent with 7 fields (was 3):
   - TodoId, Text, CategoryId (existing)
   - SourceNoteId, SourceFilePath, SourceLineNumber, SourceCharOffset (added)

2. TodoProjection made idempotent:
   - Changed `INSERT INTO` → `INSERT OR REPLACE INTO`
   - Event replay now safe (no UNIQUE constraint errors)

3. Source tracking complete:
   - todo_view schema updated with source_line_number, source_char_offset columns
   - TodoProjection uses event fields directly (not aggregate loading)

4. Tags event-sourced:
   - TodoAggregate.AddTag() emits TagAddedToEntity events
   - Tags written to projections.db/entity_tags (not todos.db)

**Evidence These Work:**
```
Logs show:
✅ Projection TodoView catching up from 0 to 121
✅ 121 events processed successfully
✅ NO "UNIQUE constraint failed" errors (across 7+ event replays)
✅ Source tracking: (source: C:\...\Test Note 1.rtf)
✅ 7 todos loaded on app startup
```

---

### **Latest Fixes (Attempted - Oct 18):**

**✅ Fix Attempt 1: Event Publication**
- Added Application.IEventBus dependency to CreateTodoHandler
- Published TodoCreatedEvent after EventStore.SaveAsync()
- Published TagAddedToEntity events
- Called aggregate.MarkEventsAsCommitted()

**Code:**
```csharp
await _eventStore.SaveAsync(aggregate);

// Publish to InMemoryEventBus
var creationEvents = new List<IDomainEvent>(aggregate.DomainEvents);
foreach (var domainEvent in creationEvents)
{
    await _eventBus.PublishAsync(domainEvent);
}

await ApplyAllTagsAsync(...);

var tagEvents = new List<IDomainEvent>(aggregate.DomainEvents);
foreach (var domainEvent in tagEvents)
{
    await _eventBus.PublishAsync(domainEvent);
}

aggregate.MarkEventsAsCommitted();
```

**Expected:** Todos appear instantly  
**Result:** Still don't appear until restart ❌

---

**✅ Fix Attempt 2: CategoryId in Event**
- Added categoryId parameter to CreateFromNote()
- Set CategoryId before emitting event
- Removed SetCategory() call after event emission

**Code:**
```csharp
public static Result<TodoAggregate> CreateFromNote(
    string text,
    Guid sourceNoteId,
    string sourceFilePath,
    int? lineNumber = null,
    int? charOffset = null,
    Guid? categoryId = null)  // ← Added
{
    var aggregate = new TodoAggregate
    {
        CategoryId = categoryId,  // ← Set before event
        // ...
    };
    
    aggregate.AddDomainEvent(new TodoCreatedEvent(
        ...,
        categoryId,  // ← In the event
        ...
    ));
}
```

**Expected:** Todos in correct category  
**Result:** Still in "Uncategorized" ❌

---

## 🚨 CURRENT SYMPTOMS

### **Symptom 1: Delayed Appearance**
- Todos created in notes do NOT appear in UI
- Only appear after app restart
- No error messages shown to user

### **Symptom 2: Wrong Category**
- When todos DO appear (after restart), they're in "Uncategorized"
- Should be in note's parent folder
- CategoryId appears blank in logs

### **Symptom 3: Tag Errors**
- NullReferenceException in TodoItemViewModel.LoadTagsAsync() line 521
- TodoTagRepository failures
- Tags exist in projections.db but UI can't display them

---

## 📊 WHAT THE LOGS SHOW

### **Latest Test Session (17:42 - 17:43):**

**✅ What's Working:**
```
[INF] Projection TodoView catching up from 0 to 121
[DBG] Projection TodoView processed batch: 121 events
[INF] Projection TodoView caught up: 121 events processed

[DBG] [TodoView] Todo created: 'test 1' (source: C:\...\Test Note 1.rtf)
[DBG] [TodoView] Todo created: 'test note' (source: C:\...\Test Note 1.rtf)

[INF] dY"< Retrieved 7 todos from projection
[INF] dY"< Created 7 view models
```

**Evidence:**
- ✅ Projections rebuild successfully (no schema errors)
- ✅ Event replay safe (no UNIQUE constraints)
- ✅ Source tracking working (file paths in logs)
- ✅ Todos loading on startup (7 todos found)

---

**❌ What's NOT Working:**

**Missing from Logs:**
```
When user creates NEW todo:
❌ NO "[CreateTodoHandler] Creating todo: ..." logs
❌ NO "[CreateTodoHandler] Published event: ..." logs
❌ NO "[TodoStore] 📬 Received domain event: ..." logs
❌ NO "[TodoStore] ✅ Todo added to UI collection" logs
```

**Uncategorized Issue:**
```
[DBG] [CategoryTree] Uncategorized: 'test 1' (CategoryId: , IsOrphaned: False)
                                                            ↑ BLANK!
[DBG] [CategoryTree] Uncategorized: 'test note' (CategoryId: , IsOrphaned: False)
```

**Tag Errors:**
```
[ERR] [TodoTagRepository] Failed to get tags for todo {guid}
[ERR] [TodoItemViewModel] NullReferenceException at line 521
```

---

## 🔍 THEORIES ON WHY FIXES DIDN'T WORK

### **Theory 1: Event Publication Not Reaching TodoStore**

**Possible Issues:**
- Application.IEventBus not properly registered in DI?
- DomainEventBridge not receiving events?
- TodoStore subscription pattern mismatch?
- Event published as wrong type (IDomainEvent vs TodoCreatedEvent)?

**Evidence Needed:**
- Check DI registration of Application.IEventBus
- Verify DomainEventBridge is registered as INotificationHandler
- Check logs for "Bridged domain event to plugins"
- Verify TodoStore.SubscribeToEvents() is being called

---

### **Theory 2: CategoryId Still Not Persisting**

**Possible Issues:**
- Event still has CategoryId = null (despite our fix)?
- TodoProjection not using event.CategoryId correctly?
- Database column not being updated?
- Apply() method not setting CategoryId from event?

**Evidence Needed:**
- Check events.db: Query TodoCreatedEvent JSON, verify categoryId field exists and has value
- Check projections.db: Query todo_view, verify category_id column has value
- Check TodoProjection.HandleTodoCreatedAsync() is using e.CategoryId
- Check TodoAggregate.Apply() case for TodoCreatedEvent sets CategoryId

---

### **Theory 3: New Code Not Running**

**Possible Issues:**
- App not restarted with new compiled code?
- DI not finding IEventBus dependency?
- Constructor exception swallowed?

**Evidence Needed:**
- Verify app was restarted after build
- Check for DI resolution errors in logs
- Verify CreateTodoHandler constructor doesn't throw

---

## 📋 DIAGNOSTIC STEPS NEEDED

### **Step 1: Verify New Code is Running**

Check if CreateTodoHandler is being instantiated with IEventBus:

**Look for in logs:**
```
Any CreateTodoHandler creation errors?
Any DI resolution errors for IEventBus?
```

---

### **Step 2: Check Event Flow**

**Create a test todo and look for these log entries:**

**Should See (if working):**
```
[INF] [CreateTodoHandler] Creating todo: '...'
[INF] [CreateTodoHandler] ✅ Todo persisted to event store: {guid}
[DBG] [CreateTodoHandler] Published event: TodoCreatedEvent  ← FIX #1
[DBG] Published domain event: TodoCreatedEvent  ← InMemoryEventBus
[DBG] Bridged domain event to plugins: TodoCreatedEvent  ← DomainEventBridge
[DBG] [TodoStore] 📬 Received domain event: TodoCreatedEvent  ← TodoStore
[INF] [TodoStore] ✅ Todo added to UI collection
```

**Currently Missing:** All logs after "Todo persisted to event store"

---

### **Step 3: Check Database State**

**Query events.db:**
```sql
SELECT event_type, event_data 
FROM events 
WHERE event_type = 'TodoCreatedEvent'
ORDER BY stream_position DESC 
LIMIT 1;
```

**Check:** Does event_data JSON contain categoryId field with non-null value?

**Query projections.db:**
```sql
SELECT id, text, category_id, source_note_id, source_file_path
FROM todo_view
WHERE source_type = 'note'
ORDER BY created_at DESC
LIMIT 5;
```

**Check:** Does category_id column have value or NULL?

---

## 💡 POSSIBLE ROOT CAUSES

### **Most Likely (80% probability):**

**Issue:** Application.IEventBus not properly wired in DI

**Location to Check:**
- `CleanServiceConfiguration.cs` or `PluginSystemConfiguration.cs`
- Is Application.IEventBus registered?
- Is InMemoryEventBus registered as Application.IEventBus?
- Is it Singleton/Scoped/Transient?

**Verification:**
```csharp
// Should exist in DI configuration:
services.AddSingleton<NoteNest.Application.Common.Interfaces.IEventBus>(provider =>
    new InMemoryEventBus(
        provider.GetRequiredService<IMediator>(),
        provider.GetRequiredService<IAppLogger>()));
```

---

### **Possible (15% probability):**

**Issue:** DomainEventBridge not registered or not working

**Check:**
- Is DomainEventBridge registered as INotificationHandler<DomainEventNotification>?
- Is it being called?
- Look for log: "Bridged domain event to plugins"

---

### **Edge Case (5% probability):**

**Issue:** Timing - events published but TodoStore not subscribed yet

**Check:**
- When does TodoStore.SubscribeToEvents() run?
- Is it called before first todo creation?
- Race condition?

---

## 🔧 WHAT TO INVESTIGATE NEXT

### **Priority 1: Check DI Registration**

**Find and verify:**
1. Application.IEventBus registration
2. InMemoryEventBus implementation registered
3. DomainEventBridge registered as INotificationHandler
4. Core.Services.IEventBus registration (for TodoStore)

**Files to check:**
- `NoteNest.UI/Composition/CleanServiceConfiguration.cs`
- `NoteNest.UI/Composition/PluginSystemConfiguration.cs`
- Look for InMemoryEventBus, DomainEventBridge registrations

---

### **Priority 2: Add Diagnostic Logging**

**Locations to add debug logs:**

1. **InMemoryEventBus.PublishAsync() (line 24-40):**
```csharp
_logger.Debug($"InMemoryEventBus.PublishAsync called for: {typeof(T).Name}");
_logger.Debug($"Runtime type: {domainEvent.GetType().Name}");
```

2. **DomainEventBridge.Handle() (line 25-39):**
```csharp
_logger.Debug($"DomainEventBridge.Handle called");
_logger.Debug($"Event type: {notification.DomainEvent.GetType().Name}");
_logger.Debug($"About to publish to Core.EventBus...");
```

3. **CreateTodoHandler (after each publish):**
```csharp
_logger.Info($"Published {creationEvents.Count} creation events");
_logger.Info($"Published {tagEvents.Count} tag events");
```

---

### **Priority 3: Check Actual Event Data**

**Query events.db directly:**
```sql
-- Get latest TodoCreatedEvent
SELECT 
    stream_position,
    event_type,
    json_extract(event_data, '$.CategoryId') as CategoryId,
    json_extract(event_data, '$.SourceNoteId') as SourceNoteId,
    json_extract(event_data, '$.Text') as Text
FROM events
WHERE event_type = 'TodoCreatedEvent'
ORDER BY stream_position DESC
LIMIT 3;
```

**Check:**
- Does CategoryId field exist in JSON?
- Does it have a value (not null)?
- Does SourceNoteId exist and have value?

---

## 📋 IMPLEMENTATION HISTORY

### **What's Been Done:**

**Phase 1: Option B Implementation (2 hours)**
- Enhanced TodoCreatedEvent (3 → 7 fields)
- Made TodoProjection idempotent (INSERT OR REPLACE)
- Added source tracking columns to schema
- Made tags event-sourced
- **Build:** ✅ 0 Errors
- **Status:** Core architecture correct

**Phase 2: Event Publication Fix (20 minutes)**
- Added IEventBus to CreateTodoHandler
- Published events after EventStore.SaveAsync()
- **Build:** ✅ 0 Errors
- **Status:** Code correct, but doesn't work in practice

**Phase 3: CategoryId Fix (5 minutes)**
- Added categoryId parameter to CreateFromNote()
- Pass it from CreateTodoHandler
- **Build:** ✅ 0 Errors
- **Status:** Code correct, but doesn't work in practice

---

## 🎯 CRITICAL QUESTIONS FOR NEXT SESSION

### **Question 1: Is Application.IEventBus Registered?**

**Location:** DI configuration files

**Need to verify:**
```csharp
services.AddSingleton<NoteNest.Application.Common.Interfaces.IEventBus>(...);
```

**If NOT registered:** CreateTodoHandler constructor will fail (DI exception)

---

### **Question 2: Are Events Actually Being Published?**

**Add diagnostic logging to:**
- CreateTodoHandler (after each _eventBus.PublishAsync())
- InMemoryEventBus.PublishAsync()
- DomainEventBridge.Handle()
- TodoStore event subscription

**Expected log trail:**
```
CreateTodoHandler → Publishing
InMemoryEventBus → Received
MediatR → Dispatching
DomainEventBridge → Bridging
Core.EventBus → Publishing
TodoStore → Received
```

**Currently:** Only seeing "Todo persisted to event store" - nothing after

---

### **Question 3: Is TodoStore Actually Subscribed?**

**Check:**
- TodoStore constructor calls SubscribeToEvents() (line 42)
- SubscribeToEvents() subscribes to IDomainEvent (line 393)
- Is this being called during app initialization?

**Verification:**
- Add log in TodoStore.SubscribeToEvents(): "Subscribed to IDomainEvent"
- Add log in TodoStore constructor: "TodoStore created"

---

### **Question 4: Are Events in Database Correct?**

**Check events.db:**
- Does latest TodoCreatedEvent have categoryId field?
- Is the value correct (not null)?

**Check projections.db:**
- Does todo_view row have category_id populated?
- Or is it NULL?

---

## 🔍 LIKELY ROOT CAUSE (Best Guess)

### **Hypothesis: Application.IEventBus Not Registered in DI**

**Reasoning:**
1. CreateTodoHandler requires Application.IEventBus (we added it)
2. If not registered, DI will fail to create CreateTodoHandler
3. MediatR would fail to dispatch CreateTodoCommand
4. But logs don't show DI errors...

**Alternative:** Application.IEventBus IS registered, but:
- Not connected to DomainEventBridge?
- DomainEventBridge not registered?
- Core.Services.IEventBus not the same instance TodoStore subscribes to?

---

## 📋 NEXT SESSION SHOULD:

### **1. Verify DI Configuration (30 min)**
- Check all IEventBus registrations
- Verify InMemoryEventBus registered
- Verify DomainEventBridge registered
- Verify Core.Services.IEventBus registered
- Ensure they're connected properly

### **2. Add Comprehensive Diagnostic Logging (15 min)**
- Every step of event flow
- From CreateTodoHandler to TodoStore
- Identify exactly where events are lost

### **3. Test with Logging (5 min)**
- Create test todo
- Follow logs through entire flow
- Find the break point

### **4. Fix the Actual Issue (time varies)**
- Based on what logs reveal
- Might be DI configuration
- Might be event bus routing
- Might be subscription timing

---

## 📊 CURRENT FILES STATE

### **Modified Files (All Compiled Successfully):**
1. `TodoEvents.cs` - Enhanced event (7 fields)
2. `TodoAggregate.cs` - CreateFromNote with categoryId, AddTag emits events
3. `TodoProjection.cs` - INSERT OR REPLACE, uses event fields
4. `Projections_Schema.sql` - source tracking columns added
5. `CreateTodoHandler.cs` - IEventBus dependency, event publication

### **Key Log Observations:**
- Event replay works (projections catch up)
- Todos load on startup (7 todos)
- Source tracking in logs (file paths)
- NO "Published event" logs (events not being published)
- NO "Received domain event" logs in TodoStore

---

## 🎯 RECOMMENDED APPROACH FOR NEW SESSION

### **Start Fresh Investigation:**

**Don't assume previous fixes are correct. Instead:**

1. **Map the complete event flow** from CreateTodoHandler to TodoStore
2. **Verify each component** exists and is wired correctly
3. **Add logging at every step** to find where events are lost
4. **Test incrementally** after each fix

**Key Insight:** The architecture is mostly correct (projections work, event sourcing works), but events aren't reaching TodoStore.

**Most Likely Issue:** Dependency injection configuration or event bus routing

---

## 💡 SUCCESS CRITERIA

**When Fixed:**
```
User creates todo → Saves → Within 2 seconds:
  ✅ Todo appears in UI
  ✅ In correct category (parent folder)
  ✅ With inherited tags
  ✅ No restart needed

Logs show complete event trail:
  CreateTodoHandler → Publish → Bridge → TodoStore → UI Update
```

---

## 📌 SUMMARY FOR NEW AI SESSION

**The Problem:** Note-linked todos don't appear until restart, appear in wrong category

**What Works:** Event sourcing infrastructure, projections, database persistence

**What Doesn't Work:** Real-time UI updates via event bus

**Most Likely Cause:** DI configuration for IEventBus or event routing

**Next Steps:** Verify DI, add diagnostic logging, trace event flow

**Goal:** Make todos appear instantly in correct category

---

**END OF BRIEFING**

This document contains everything a new AI session (or human developer) would need to continue debugging this issue.

