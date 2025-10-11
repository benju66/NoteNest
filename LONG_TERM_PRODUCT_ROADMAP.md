# 🗺️ Long-Term Product Roadmap - NoteNest TodoPlugin

**Current State:** Working manual mapping, all critical bugs fixed  
**Vision:** Enterprise-grade todo system with recurring tasks, sync, undo, etc.  
**Status:** COMPLETE ROADMAP

---

## 📊 **WHERE WE ARE NOW (v0.5)**

### **✅ Working Features:**
- Create/edit/delete todos
- RTF bracket extraction ([todo] from notes)
- Auto-categorization by note folder
- Category management
- Orphaned todo handling  
- Delete key functionality
- Event-driven category coordination
- Persistence across restart ✅
- Expanded state preservation
- Soft/hard delete state machine
- Memory leak prevention
- Circular reference protection

### **✅ Architecture Foundation:**
- DDD Domain Layer (TodoAggregate, Value Objects, Domain Events)
- Repository Pattern (with manual mapping)
- EventBus coordination
- CQRS infrastructure (MediatR)
- Clean Architecture separation

### **⚠️ Technical Debt:**
- Manual mapping in GetAllAsync (works but verbose)
- 13 unused repository methods (800 lines of dead code)
- Mixed patterns (inconsistent)

**Assessment:** Production-ready for single-user, development phase ✅

---

## 🎯 **PATH TO LONG-TERM VISION**

### **MILESTONE 1: Clean Foundation** (4-6 hours) ⭐ **NEXT**

**Goal:** Pure DTO pattern, consistent architecture

**Tasks:**
1. Scorched earth TodoRepository rebuild
   - Replace 1000 lines with 400 lines
   - Pure DTO pattern (Database → DTO → Aggregate → UI)
   - Remove 13 unused methods
   - Consistent with main app

2. Add comprehensive error handling
   - Try-catch in DTO.ToAggregate()
   - Graceful fallbacks
   - No data loss scenarios

3. Testing
   - All 4 critical methods tested
   - Restart persistence validated
   - RTF sync confirmed
   - Category cleanup verified

**Deliverable:** Clean, maintainable, enterprise-grade repository

**Confidence:** 90% (with current research)

**Benefits for Future:**
- Foundation for CQRS commands
- Ready for event sourcing
- Consistent pattern for new features
- Easy to extend

---

### **MILESTONE 2: CQRS Commands** (6-8 hours)

**Goal:** Proper command/query separation, validation pipeline

**Tasks:**
1. Create Application Layer Commands
   ```
   Commands/
   ├── CreateTodoCommand.cs
   ├── CompleteTodoCommand.cs
   ├── UpdateTodoCommand.cs
   ├── DeleteTodoCommand.cs
   ├── SetDueDateCommand.cs
   ├── AddTagCommand.cs
   └── Handlers for each
   ```

2. Add FluentValidation
   ```csharp
   public class CreateTodoCommandValidator : AbstractValidator<CreateTodoCommand>
   {
       public CreateTodoCommandValidator()
       {
           RuleFor(x => x.Text).NotEmpty().MaximumLength(1000);
           RuleFor(x => x.CategoryId).NotEmpty().When(x => x.RequiresCategory);
       }
   }
   ```

3. Update ViewModels
   - Replace direct repository calls
   - Use MediatR commands
   - Proper error handling

**Deliverable:** CQRS architecture complete

**Confidence:** 95% (main app proves it works)

**Enables:**
- Validation pipeline
- Logging pipeline
- Undo/redo foundation
- Transaction support

---

### **MILESTONE 3: Recurring Tasks** (8-10 hours)

**Goal:** Complex recurring task logic

**Tasks:**
1. Design RecurrenceRule Value Object
   ```csharp
   public class RecurrenceRule : ValueObject
   {
       public RecurrencePattern Pattern { get; }  // Daily, Weekly, Monthly, Yearly
       public int Interval { get; }
       public DayOfWeek[]? DaysOfWeek { get; }
       public int? DayOfMonth { get; }
       public DateTime? EndDate { get; }
       public int LeadTimeDays { get; }
       
       public DateTime? GetNextOccurrence(DateTime from) { ... }
       public bool ShouldCreateNext(DateTime now) { ... }
   }
   ```

2. Add to TodoAggregate
   ```csharp
   public RecurrenceRule? Recurrence { get; private set; }
   
   public Result SetRecurrence(RecurrenceRule rule)
   {
       if (IsCompleted) return Result.Fail("Can't recur completed todo");
       Recurrence = rule;
       AddDomainEvent(new RecurrenceSetEvent(Id, rule));
   }
   ```

3. Database Schema
   ```sql
   ALTER TABLE todos ADD COLUMN recurrence_rule_json TEXT;
   ```

4. DTO Handling
   ```csharp
   public class TodoItemDto
   {
       public string RecurrenceRuleJson { get; set; }
       
       // Serialize/deserialize automatically
   }
   ```

5. UI Components
   - Recurrence picker dialog
   - Visual indicators for recurring todos
   - "Create Next" button

6. Background Service
   ```csharp
   public class RecurringTodoService : IHostedService
   {
       // Check every hour
       // Create next occurrences when due
       // Based on RecurrenceRule logic
   }
   ```

**Deliverable:** Full recurring task system

**Confidence:** 85% (new feature, needs careful testing)

**Requires:** Milestone 1 (DTO pattern) ✅

---

### **MILESTONE 4: Dependencies/Subtasks** (6-8 hours)

**Goal:** Todo dependencies and hierarchies

**Tasks:**
1. Database Schema
   ```sql
   CREATE TABLE todo_dependencies (
       todo_id TEXT NOT NULL,
       depends_on_todo_id TEXT NOT NULL,
       created_at INTEGER NOT NULL,
       PRIMARY KEY (todo_id, depends_on_todo_id),
       FOREIGN KEY (todo_id) REFERENCES todos(id) ON DELETE CASCADE,
       FOREIGN KEY (depends_on_todo_id) REFERENCES todos(id) ON DELETE CASCADE
   );
   
   CREATE INDEX idx_dependencies_todo ON todo_dependencies(todo_id);
   CREATE INDEX idx_dependencies_dependent ON todo_dependencies(depends_on_todo_id);
   ```

2. Aggregate Logic
   ```csharp
   public class TodoAggregate
   {
       private List<TodoId> _dependencies = new();
       
       public Result AddDependency(TodoId dependentTodo)
       {
           if (WouldCreateCycle(dependentTodo))
               return Result.Fail("Circular dependency");
           
           _dependencies.Add(dependentTodo);
           AddDomainEvent(new DependencyAddedEvent(Id, dependentTodo));
       }
       
       public bool CanComplete()
       {
           return AllDependenciesCompleted();
       }
   }
   ```

3. UI
   - Dependency picker
   - Visual graph/tree
   - Blocking indicators

**Deliverable:** Todo dependencies working

**Confidence:** 90%

**Requires:** Milestone 2 (CQRS) for complex logic

---

### **MILESTONE 5: System-Wide Tags** (4-6 hours)

**Goal:** Unified tagging across notes, todos, categories

**Tasks:**
1. Database (already exists!)
   ```sql
   CREATE TABLE global_tags (tag TEXT PRIMARY KEY, color TEXT, ...);
   CREATE TABLE todo_tags (todo_id TEXT, tag TEXT);
   CREATE TABLE note_tags (note_id TEXT, tag TEXT);
   CREATE TABLE category_tags (category_id TEXT, tag TEXT);
   ```

2. Tag Service
   ```csharp
   public interface ITagService
   {
       Task<Tag> GetOrCreateAsync(string tagName);
       Task<List<Tag>> GetAllAsync();
       Task<List<TaggedItem>> GetItemsByTagAsync(string tag);
       Task AddTagToTodoAsync(Guid todoId, string tag);
       Task AddTagToNoteAsync(Guid noteId, string tag);
   }
   ```

3. Auto-tagging
   ```csharp
   // When todo created in category:
   var categoryTags = await _tagService.GetTagsForCategoryAsync(categoryId);
   foreach (var tag in categoryTags)
   {
       await _tagService.AddTagToTodoAsync(todoId, tag);
   }
   ```

4. UI
   - Tag picker
   - Tag cloud view
   - Filter by tag
   - Color-coded tags

**Deliverable:** System-wide tagging

**Confidence:** 95% (schema exists, straightforward)

**Requires:** Milestone 1 (DTO for tag loading)

---

### **MILESTONE 6: Event Sourcing Foundation** (10-15 hours)

**Goal:** Event log for sync, undo, audit

**Tasks:**
1. Event Store Schema
   ```sql
   CREATE TABLE todo_events (
       event_id TEXT PRIMARY KEY,
       aggregate_id TEXT NOT NULL,
       event_type TEXT NOT NULL,
       payload_json TEXT NOT NULL,
       version INTEGER NOT NULL,
       timestamp INTEGER NOT NULL,
       user_id TEXT,
       synced INTEGER DEFAULT 0
   );
   ```

2. Event Repository
   ```csharp
   public interface IEventStore
   {
       Task SaveEventAsync(IDomainEvent evt, Guid aggregateId);
       Task<List<IDomainEvent>> GetEventsAsync(Guid aggregateId);
       Task<List<IDomainEvent>> GetUnsyncedEventsAsync();
   }
   ```

3. Aggregate Replay
   ```csharp
   public static TodoAggregate ReplayEvents(List<IDomainEvent> events)
   {
       var aggregate = new TodoAggregate();
       foreach (var evt in events)
       {
           aggregate.Apply(evt);  // Rebuild state from events
       }
       return aggregate;
   }
   ```

4. Repository Integration
   ```csharp
   public async Task SaveAsync(TodoAggregate aggregate)
   {
       // Save events
       foreach (var evt in aggregate.DomainEvents)
       {
           await _eventStore.SaveEventAsync(evt, aggregate.Id);
       }
       
       // Save snapshot (DTO)
       var dto = TodoItemDto.FromAggregate(aggregate);
       await connection.ExecuteAsync(sql, dto);
   }
   ```

**Deliverable:** Event sourcing capability

**Confidence:** 85% (complex, needs careful design)

**Requires:** Milestone 2 (CQRS for event publishing)

**Enables:** Multi-user sync, undo/redo, time travel, audit trail

---

### **MILESTONE 7: Undo/Redo** (6-8 hours)

**Goal:** Full undo/redo stack

**Requires:** Milestone 6 (Event Sourcing)

**Tasks:**
1. Command History
   - Store executed commands
   - Track inverse operations
   - Maintain undo/redo stacks

2. Inverse Commands
   ```csharp
   public class CompleteTodoCommand
   {
       public ICommand GetInverseCommand()
       {
           return new UncompleteTodoCommand { TodoId = this.TodoId };
       }
   }
   ```

3. UI
   - Undo/Redo buttons
   - Keyboard shortcuts (Ctrl+Z, Ctrl+Y)
   - Stack visualization

**Deliverable:** Full undo/redo

**Confidence:** 90%

---

### **MILESTONE 8: Multi-User Sync** (20-30 hours)

**Goal:** Real-time collaborative editing

**Requires:** Milestone 6 (Event Sourcing)

**Tasks:**
1. Conflict Resolution (Operational Transform)
2. Sync Service (push/pull events)
3. Offline support (local event log)
4. Real-time updates (SignalR/WebSockets)

**Deliverable:** Multi-user collaboration

**Confidence:** 75% (very complex, needs testing)

---

### **MILESTONE 9: Time Tracking** (8-10 hours)

**Goal:** Track time spent on todos

**Tasks:**
1. TimeEntry Aggregate
2. Start/Stop tracking
3. Time reports
4. Database integration

**Deliverable:** Time tracking

**Confidence:** 85%

---

## 📋 **RECOMMENDED EXECUTION ORDER**

### **Phase 1: Foundation (NOW → 1 week)**
✅ Milestone 1: Clean DTO Repository (4-6 hours) ⭐ **DO THIS FIRST**

**Why:** 
- Enables everything else
- Removes technical debt
- Matches main app
- 90% confidence

**When:** Next development session

---

### **Phase 2: Core Features (Weeks 2-4)**
- Milestone 5: System Tags (4-6 hours) - High user value
- Milestone 3: Recurring Tasks (8-10 hours) - High user value
- Milestone 4: Dependencies (6-8 hours) - Medium value

**Why this order:**
- Tags are easiest, immediate value
- Recurring tasks highly requested
- Dependencies build on solid foundation

---

### **Phase 3: Advanced Architecture (Weeks 5-8)**
- Milestone 2: CQRS Commands (6-8 hours)
- Milestone 6: Event Sourcing (10-15 hours)
- Milestone 7: Undo/Redo (6-8 hours)

**Why later:**
- Need real feature usage to validate patterns
- Complex, benefit from experience
- Foundation must be solid first

---

### **Phase 4: Collaboration (Months 3-4)**
- Milestone 8: Multi-User Sync (20-30 hours)
- Milestone 9: Time Tracking (8-10 hours)

**Why last:**
- Most complex
- Needs all previous milestones
- Solo user can defer

---

## ⏱️ **REALISTIC TIMELINE**

### **Week 1: Foundation**
- Day 1-2: Scorched Earth DTO Refactor (Milestone 1)
- Day 3: Testing and validation
- Day 4-5: Documentation and cleanup

### **Week 2-3: Quick Wins**
- Week 2: System Tags (Milestone 5)
- Week 3: Start Recurring Tasks (Milestone 3)

### **Week 4-6: Core Features**
- Complete Recurring Tasks
- Implement Dependencies (Milestone 4)
- User testing and refinement

### **Week 7-10: Architecture Completion**
- CQRS Commands (Milestone 2)
- Event Sourcing (Milestone 6)
- Undo/Redo (Milestone 7)

### **Month 3+: Advanced Features**
- Multi-user if needed
- Time tracking if needed
- Additional features as discovered

**Total:** ~3 months to full vision (working solo, part-time)

---

## 🎯 **IMMEDIATE NEXT STEPS**

### **Option A: Scorched Earth Now** (RECOMMENDED)
```
Time: 4-6 hours
Benefit: Clean foundation for all future work
Risk: LOW (git backup, working baseline)
Confidence: 90%

Steps:
1. Fresh context/session
2. Follow SCORCHED_EARTH_IMPLEMENTATION_PLAN.md
3. Execute carefully with testing checkpoints
4. Commit clean version
```

### **Option B: Ship Current, Refactor Later**
```
Time: 0 hours now, 4-6 hours later
Benefit: Focus on features immediately
Risk: Technical debt accumulates
Confidence: 100% (works now)

Steps:
1. Use current manual mapping
2. Build features on top
3. Refactor when pain points appear
4. Or never (if it keeps working)
```

---

## 📊 **DEPENDENCIES BETWEEN MILESTONES**

```
Milestone 1 (DTO Refactor)
  ↓
  ├─→ Milestone 2 (CQRS) → Milestone 6 (Event Sourcing)
  │                           ↓
  │                           ├─→ Milestone 7 (Undo/Redo)
  │                           └─→ Milestone 8 (Sync)
  │
  ├─→ Milestone 3 (Recurring)
  ├─→ Milestone 4 (Dependencies)
  ├─→ Milestone 5 (Tags)
  └─→ Milestone 9 (Time Tracking)
```

**Critical Path:** 1 → 2 → 6 → 7/8  
**Independent:** 3, 4, 5, 9 (can do anytime after 1)

---

## ✅ **CONFIDENCE BY MILESTONE**

| Milestone | Confidence | Complexity | User Value |
|-----------|-----------|------------|------------|
| 1. DTO Refactor | 90% | MEDIUM | HIGH (foundation) |
| 2. CQRS | 95% | MEDIUM | MEDIUM (quality) |
| 3. Recurring | 85% | HIGH | HIGH |
| 4. Dependencies | 90% | MEDIUM | HIGH |
| 5. Tags | 95% | LOW | HIGH |
| 6. Event Sourcing | 85% | HIGH | MEDIUM (foundation) |
| 7. Undo/Redo | 90% | MEDIUM | MEDIUM |
| 8. Multi-User Sync | 75% | VERY HIGH | LOW (solo user) |
| 9. Time Tracking | 85% | MEDIUM | MEDIUM |

---

## 🎓 **LEARNING & ITERATION**

### **After Each Milestone:**
1. Use the feature yourself
2. Discover pain points
3. Refine architecture
4. Adjust roadmap

**This is agile development:**
- Build → Test → Learn → Refine
- Don't over-engineer upfront
- Respond to real needs

---

## 🚀 **MY RECOMMENDATION**

### **Execute Milestone 1 Next Session:**

**Why:**
- Clean foundation saves time on all future work
- Removes 800 lines of dead code
- Matches main app (consistency)
- 90% confidence (acceptable)
- Only 4-6 hours investment

**Then:**
- Milestone 5 (Tags) - Quick win, high value
- Milestone 3 (Recurring) - Most requested feature
- Milestone 4 (Dependencies) - Natural progression

**Defer:**
- Milestone 8 (Multi-user) - Solo user, can wait
- Complex milestones until simpler ones proven

---

## 📊 **RISK MITIGATION**

**For Each Milestone:**
1. ✅ Git commit before starting
2. ✅ Incremental implementation
3. ✅ Test at each step
4. ✅ Can rollback anytime
5. ✅ Document decisions

**Safety Net:**
- Working baseline exists
- Each milestone is independent
- Can skip or defer any milestone
- Architecture supports flexibility

---

## ✅ **SUMMARY**

**To land on long-term vision:**

**Immediate (Next Session):**
1. Scorched Earth DTO Refactor (Milestone 1)
2. 4-6 hours
3. Clean foundation established
4. 90% confidence

**Short-term (1-2 months):**
1. Tags, Recurring, Dependencies (Milestones 3-5)
2. High user value
3. Build on solid foundation

**Long-term (3+ months):**
1. CQRS, Event Sourcing, Undo/Redo (Milestones 2, 6, 7)
2. Advanced capabilities
3. Enterprise-grade system

**Very Long-term (As Needed):**
1. Multi-user, Time Tracking (Milestones 8, 9)
2. When requirements become clear

---

## 🎯 **THE PATH IS CLEAR**

**Current:** Working but messy (v0.5)  
**Next:** Clean DTO foundation (v1.0) - Milestone 1  
**Then:** Feature additions (v1.1, 1.2, 1.3...) - Milestones 3-5  
**Finally:** Advanced architecture (v2.0) - Milestones 2, 6-9

**Total time to full vision: ~100-120 hours** (3 months part-time)

**But you get value at EVERY milestone!**

---

**Ready to execute Milestone 1 (Scorched Earth) in next session?**

