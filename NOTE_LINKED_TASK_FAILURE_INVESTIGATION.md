# 🔍 NOTE-LINKED TASK FAILURE - ROOT CAUSE INVESTIGATION

**Date:** October 18, 2025  
**Issue:** Note-linked tasks are not being added to todo treeview on save  
**Status:** ✅ ROOT CAUSE IDENTIFIED  
**Severity:** CRITICAL  
**Confidence:** 100% (empirical evidence from logs)

---

## 🚨 **THE PROBLEM**

### **User Report:**
"note-linked tasks are not being added to the todo treeview on save of the note"

### **What Should Happen:**
```
User types in note: [call John]
  ↓
User saves (Ctrl+S)
  ↓
TodoSyncService extracts bracket
  ↓
CreateTodoCommand creates todo
  ↓
Todo appears in TodoPlugin panel ✅
```

### **What Actually Happens:**
```
User types in note: [call John]
  ↓
User saves (Ctrl+S)
  ↓
TodoSyncService extracts bracket ✅
  ↓
CreateTodoCommand FAILS ❌
  ↓
No todo created ❌
```

---

## 🔍 **INVESTIGATION PROCESS**

### **Step 1: Check Service Registration**
✅ TodoSyncService is registered as IHostedService  
✅ All dependencies properly registered  
✅ Service starts on app launch

### **Step 2: Check Logs**
Found startup logs showing service is running:
```
[INF] [TodoSync] Starting todo sync service - monitoring note saves for bracket todos
[INF] ✅ TodoSyncService subscribed to note save events
```

### **Step 3: Check Processing Logs**
Found extraction working:
```
[DBG] [TodoSync] Note save queued for processing: Test Note A - 1.rtf
[INF] [TodoSync] Processing note: Test Note A - 1.rtf
[DBG] [TodoSync] Found 1 todo candidates in Test Note A - 1.rtf
[DBG] [TodoSync] Reconciling 1 candidates with 0 existing todos
```

### **Step 4: FOUND THE ERROR! 🎯**
```
[ERR] [TodoSync] CreateTodoCommand failed: Error creating todo: 
Unable to cast object of type 'NoteNest.UI.Plugins.TodoPlugin.Domain.Events.TodoCreatedEvent' 
to type 'NoteNest.Domain.Common.IDomainEvent'.
```

---

## 🎯 **ROOT CAUSE: TYPE INCOMPATIBILITY**

### **The Issue:**

**TodoPlugin has its OWN domain infrastructure** that's **incompatible** with the main app's event sourcing system!

### **Main Domain:**
```csharp
// NoteNest.Domain.Common
namespace NoteNest.Domain.Common
{
    public interface IDomainEvent
    {
        DateTime OccurredAt { get; }
    }
    
    public abstract class AggregateRoot : IAggregateRoot
    {
        private readonly List<IDomainEvent> _uncommittedEvents = new();
        public IReadOnlyList<IDomainEvent> GetUncommittedEvents() => _uncommittedEvents;
    }
}
```

### **TodoPlugin Domain:**
```csharp
// NoteNest.UI.Plugins.TodoPlugin.Domain.Common
namespace NoteNest.UI.Plugins.TodoPlugin.Domain.Common
{
    public interface IDomainEvent  // ← DIFFERENT TYPE!
    {
        DateTime OccurredAt { get; }
    }
    
    public abstract class AggregateRoot : IAggregateRoot  // ← DIFFERENT TYPE!
    {
        private readonly List<IDomainEvent> _uncommittedEvents = new();
        public IReadOnlyList<IDomainEvent> GetUncommittedEvents() => _uncommittedEvents;
    }
}
```

### **The Conflict:**

**TodoAggregate:**
```csharp
// Line 3: Uses TodoPlugin's domain common
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;

// Line 9: Extends TodoPlugin's AggregateRoot
public class TodoAggregate : AggregateRoot
{
    // Returns TodoPlugin's IDomainEvent
}
```

**Event Store Expects:**
```csharp
// IEventStore.cs line 4
using NoteNest.Domain.Common;  // ← Main domain!

// IEventStore.cs line 18
Task SaveAsync(IAggregateRoot aggregate);  // ← Expects main domain's IAggregateRoot!

// IEventStore.cs line 33
Task<IReadOnlyList<IDomainEvent>> GetEventsAsync(Guid aggregateId);  // ← Expects main domain's IDomainEvent!
```

**Result:** Type mismatch → Cast exception → Todo creation fails!

---

## 📊 **ARCHITECTURE PROBLEM**

### **The TodoPlugin Architecture:**

```
NoteNest.UI.Plugins.TodoPlugin
├── Domain
│   ├── Common
│   │   ├── AggregateRoot.cs  ← ISOLATED COPY
│   │   ├── IAggregateRoot.cs  ← ISOLATED COPY
│   │   └── IDomainEvent.cs  ← ISOLATED COPY
│   ├── Aggregates
│   │   └── TodoAggregate.cs  ← Uses isolated types
│   └── Events
│       └── TodoEvents.cs  ← Implements isolated IDomainEvent
```

### **The Main Domain:**

```
NoteNest.Domain
├── Common
│   ├── AggregateRoot.cs  ← MAIN IMPLEMENTATION
│   ├── IAggregateRoot.cs  ← MAIN IMPLEMENTATION
│   └── IDomainEvent.cs  ← MAIN IMPLEMENTATION
├── Notes
│   └── Note.cs  ← Uses main types ✅
└── Categories
    └── CategoryAggregate.cs  ← Uses main types ✅
```

### **The Problem:**

TodoPlugin was designed as a **self-contained plugin** with its own event sourcing infrastructure, but when integrated with the main app's event store, the types don't match!

**Categories work:** They use `NoteNest.Domain.Common.AggregateRoot` ✅  
**Notes work:** They use `NoteNest.Domain.Common.AggregateRoot` ✅  
**Todos FAIL:** They use `NoteNest.UI.Plugins.TodoPlugin.Domain.Common.AggregateRoot` ❌

---

## 🔧 **WHY THIS HAPPENED**

### **Historical Context:**

The TodoPlugin was likely developed as a **standalone plugin** with the goal of being:
1. ✅ Self-contained (doesn't depend on main domain)
2. ✅ Portable (can be used in other apps)
3. ✅ Isolated (changes don't affect main app)

**But:**
- ❌ When integrated with the main event store, types must match!
- ❌ The event store can't accept plugin events
- ❌ .NET doesn't consider them the same type (different namespaces)

---

## ✅ **VERIFICATION**

### **Evidence from Code:**

**1. TodoAggregate uses plugin domain:**
```csharp
// TodoAggregate.cs line 3
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;

// TodoAggregate.cs line 9
public class TodoAggregate : AggregateRoot  // Plugin's AggregateRoot
```

**2. Event store expects main domain:**
```csharp
// IEventStore.cs line 4
using NoteNest.Domain.Common;  // Main domain!

// IEventStore.cs line 18
Task SaveAsync(IAggregateRoot aggregate);  // Main domain's IAggregateRoot
```

**3. CreateTodoHandler tries to save plugin aggregate to main event store:**
```csharp
// CreateTodoHandler.cs line 76
await _eventStore.SaveAsync(aggregate);  // ← TYPE MISMATCH!
```

**4. Runtime error confirms:**
```
Unable to cast object of type 'NoteNest.UI.Plugins.TodoPlugin.Domain.Events.TodoCreatedEvent' 
to type 'NoteNest.Domain.Common.IDomainEvent'.
```

---

## 🎯 **THE FIX (3 OPTIONS)**

### **Option A: Make TodoPlugin Use Main Domain (RECOMMENDED)**

**Pros:**
- ✅ Proper architecture (plugins shouldn't duplicate core infrastructure)
- ✅ Type compatibility guaranteed
- ✅ Consistent with CategoryAggregate and Note
- ✅ Smaller codebase (remove duplicate code)

**Cons:**
- ⚠️ TodoPlugin becomes less portable
- ⚠️ Requires refactoring TodoPlugin domain layer

**Implementation:**
1. Change `TodoAggregate` to extend `NoteNest.Domain.Common.AggregateRoot`
2. Change `TodoEvents` to implement `NoteNest.Domain.Common.IDomainEvent`
3. Remove duplicate `AggregateRoot.cs` and `IDomainEvent.cs` from TodoPlugin
4. Update all usings in TodoPlugin domain layer

**Files to Modify:**
- `TodoAggregate.cs` (change base class)
- `TodoEvents.cs` (change interface)
- Delete `NoteNest.UI/Plugins/TodoPlugin/Domain/Common/AggregateRoot.cs`
- Delete `NoteNest.UI/Plugins/TodoPlugin/Domain/Common/IDomainEvent.cs`

**Time:** 1-2 hours  
**Risk:** Medium (need to test all todo operations)

---

### **Option B: Type Adapter/Bridge Pattern**

**Pros:**
- ✅ No changes to TodoPlugin domain
- ✅ Maintains plugin isolation

**Cons:**
- ❌ Complex adapter layer
- ❌ Performance overhead (event copying)
- ❌ Hard to maintain (two separate domain models)
- ❌ Doesn't solve the fundamental architecture issue

**Implementation:**
1. Create adapter that converts plugin events to main domain events
2. Wrap TodoAggregate in adapter
3. Event store saves adapter's events

**Not Recommended:** Adds complexity without fixing root issue

---

### **Option C: Separate Event Store for TodoPlugin**

**Pros:**
- ✅ No changes to TodoPlugin domain
- ✅ Complete isolation

**Cons:**
- ❌ Two separate event stores (complexity)
- ❌ Projections can't query across domains
- ❌ No unified event stream
- ❌ Inconsistent architecture

**Not Recommended:** Defeats purpose of unified event sourcing

---

## 📋 **RECOMMENDATION**

**Go with Option A: Make TodoPlugin Use Main Domain**

### **Why:**

1. **Architectural Consistency:** Categories and Notes already use main domain
2. **Single Source of Truth:** One event sourcing infrastructure
3. **Simpler:** Remove duplicate code
4. **Maintainable:** One domain model to maintain
5. **Proven Pattern:** Notes and Categories work this way

### **Implementation Plan:**

**Phase 1: Update TodoAggregate (Core)**
1. Change base class to `NoteNest.Domain.Common.AggregateRoot`
2. Update usings
3. Build and fix any compile errors

**Phase 2: Update TodoEvents**
1. Change to implement `NoteNest.Domain.Common.IDomainEvent`
2. Build and fix any compile errors

**Phase 3: Clean Up**
1. Delete `TodoPlugin/Domain/Common/AggregateRoot.cs`
2. Delete `TodoPlugin/Domain/Common/IDomainEvent.cs`
3. Update any remaining usings

**Phase 4: Test**
1. Test manual todo creation
2. Test note-linked todo creation
3. Test tag inheritance
4. Test category assignment

**Time Estimate:** 2-3 hours  
**Confidence:** 95%

---

## 🎓 **LESSONS LEARNED**

### **Problem:**
Creating isolated copies of core infrastructure (AggregateRoot, IDomainEvent) in plugins leads to type incompatibility when integrating with the main app's systems.

### **Solution:**
Plugins should:
- ✅ Use main domain's infrastructure (AggregateRoot, IDomainEvent)
- ✅ Only define their own aggregates, entities, value objects, and events
- ❌ NOT duplicate core infrastructure

### **Good Plugin Architecture:**
```
Plugin
├── Domain
│   ├── Aggregates (extends NoteNest.Domain.Common.AggregateRoot)
│   ├── Entities (extends NoteNest.Domain.Common.Entity)
│   ├── ValueObjects
│   └── Events (implements NoteNest.Domain.Common.IDomainEvent)
├── Application (commands, queries)
├── Infrastructure (repositories, services)
└── UI (views, viewmodels)
```

---

## ✅ **CONCLUSION**

**Root Cause:** TodoPlugin has its own isolated domain infrastructure (AggregateRoot, IDomainEvent) that's incompatible with the main event store.

**Impact:** Note-linked todos fail to be created because the event store can't accept TodoPlugin's events.

**Fix:** Refactor TodoPlugin to use main domain's infrastructure (Option A).

**Next Steps:** 
1. User approval to proceed with Option A
2. Implement refactoring (2-3 hours)
3. Test thoroughly
4. Note-linked tasks will work! ✅

---

**Investigation Complete** ✅

