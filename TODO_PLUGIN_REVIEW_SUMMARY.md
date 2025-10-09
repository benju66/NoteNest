# 📊 Todo Plugin Review - Issues & Recommendations

**Date:** October 9, 2025  
**Review Type:** Pre-Implementation Analysis  
**Status:** Awaiting User Confirmation

---

## 🐛 ISSUE #1: UI Visibility Problem

### **Your Observation:**
> "Even if a Todo item is added, the user cannot see it in the todo tree view"

### **Analysis:** ✅ **YOU ARE CORRECT**

**Root Cause Found:**

The default smart list is "Today", which uses this logic:

```csharp
// In TodoStore.cs - CURRENT (BUGGY)
var items = _todos.Where(t => !t.IsCompleted && t.IsDueToday());

// And TodoItem.IsDueToday() requires:
return DueDate.HasValue && DueDate.Value.Date == DateTime.UtcNow.Date;
```

**Problem:** New todos have `DueDate = null`, so they're **filtered out** of the Today view!

**Expected Behavior:** Today view should show:
- Todos due today
- Todos with NO due date (general inbox items)
- Overdue todos

**Why it worked before:** The in-memory implementation was never tested with persistence!

---

### **Fix Required:** ⭐ **CRITICAL - BLOCKING**

**Option A: Quick LINQ Fix** (5 minutes)
```csharp
// In TodoStore.cs - Change GetTodayItems():
var items = _todos.Where(t => !t.IsCompleted && 
                            (t.DueDate == null ||  // ← ADD THIS LINE
                             t.DueDate.Value.Date <= DateTime.Today))
                 .OrderBy(t => t.Priority)
                 .ThenBy(t => t.Order);
```

**Option B: Use Database Views** (30 minutes - better architecture)
```csharp
// Make TodoStore methods async:
public async Task<ObservableCollection<TodoItem>> GetSmartListAsync(SmartListType type)
{
    List<TodoItem> items = type switch
    {
        SmartListType.Today => await _repository.GetTodayTodosAsync(),  // ← Query DB view
        SmartListType.Overdue => await _repository.GetOverdueTodosAsync(),
        // ...
    };
    
    var collection = new SmartObservableCollection<TodoItem>();
    collection.AddRange(items);
    return collection;
}
```

**My Recommendation:** 
- **Now:** Option A (unblock testing)
- **Next week:** Option B (proper architecture)

---

## 🎯 ISSUE #2: RTF Integration Plan

### **Your Requirements (Prioritized):**

1. ✅ **Bracket syntax** `[todo text]` - **PRIMARY FOCUS**
2. ⏳ **TODO: keyword** - Consider later
3. ⏳ **Toolbar button** - Add much later
4. ⏳ **Bidirectional update** - Work on later

### **Analysis:** ✅ **EXCELLENT PRIORITIZATION**

You've correctly identified the **80/20 value**:
- Brackets are explicit, unambiguous, high-value
- Everything else is incremental polish

---

### **Bracket Parser: Implementation Assessment**

**Complexity:** LOW ⭐  
**Timeline:** 2-3 days  
**Confidence:** 95%  
**Blockers:** None!

**Why Low Complexity:**
1. ✅ **SmartRtfExtractor exists** - Just use it!
```csharp
var plainText = SmartRtfExtractor.ExtractPlainText(rtfContent);
var pattern = new Regex(@"\[([^\[\]]+)\]");
var matches = pattern.Matches(plainText);
// Done! Simple text search on cleaned content
```

2. ✅ **Event system exists** - Just subscribe!
```csharp
await _eventBus.SubscribeAsync<NoteSavedEvent>(OnNoteSaved);
```

3. ✅ **Database schema ready** - All columns exist!
```sql
source_type, source_note_id, source_file_path, source_line_number
```

**What's Actually Needed:**
- 100 lines of parsing code
- 150 lines of sync logic
- 50 lines of event subscription
- **Total: ~300 lines** (vs 1,787 for database!)

---

### **TODO: Keyword - Why Defer?**

**Challenges with TODO: syntax:**

```csharp
// Example note content:
"Project meeting notes
TODO: call John
TODO: send follow-up email
Need to review TODO list later
TODO items: design, code, test
TODO: TBD"
```

**Problems:**
1. **Ambiguous** - "TODO list" is not a todo item
2. **Context-dependent** - "TODO: TBD" is placeholder text
3. **Requires NLP** - Need sentence boundary detection
4. **False positives** - Common in prose

**Recommendation:**
- Get brackets working (2-3 days)
- Use it for 1-2 weeks
- See if users request TODO: keyword
- If yes, add it with confidence scoring
- If no, saved yourself 3-4 days of work

---

### **Toolbar Button - Why Much Later?**

**Implementation Requirements:**

```csharp
// User selects text in RTF editor
// Clicks "Add to Todo" button
// Need to:

1. Get selected text range
2. Determine selection boundaries
3. Insert "[" before selection
4. Insert "]" after selection
5. Maintain formatting
6. Handle undo/redo
7. Update RTF content
8. Trigger save
9. Trigger sync
```

**Complexity:** MEDIUM-HIGH  
**Timeline:** 1 week  
**Value:** LOW (typing `[text]` is already 2 keystrokes)

**Cost-Benefit:**
- 1 week of work for marginal UX improvement
- Brackets are already fast: `[`, type text, `]`
- Power users will use keyboard anyway

**Recommendation:** **DEFER to Phase 4+** (maybe never if users don't request it)

---

### **Bidirectional Update - Recommendation**

**Your instinct is correct: Work on this later!**

Here's the phased approach:

#### **Phase 2 (Week 1-3): One-Way Sync Only**
```
Note → Todo: ✅ Implemented
Todo → Note: ❌ Not yet
```

**User Experience:**
- Type `[call John]` → Todo appears ✅
- Complete todo in panel → Nothing visible in note yet
- User must manually update note

**Why this is OK for Phase 2:**
- Proves the concept works
- Reduces complexity by 50%
- Lets users test and provide feedback

---

#### **Phase 3 (Week 4-5): Visual Indicators**
```
Note → Todo: ✅ Working
Todo → Note: ✅ Visual only (green highlight, tooltip)
```

**User Experience:**
- Type `[call John]` → Todo appears ✅
- Complete todo → Opens note, see green highlight ✅
- File unchanged (safe!)

**Why this is better than RTF modification:**
- Non-destructive (file never corrupted)
- Faster to implement (3-4 days vs 1-2 weeks)
- Safer (no file write risks)
- Good enough for 90% of users

---

#### **Phase 5+ (Week 8+): RTF Modification** - **OPTIONAL**
```
Note → Todo: ✅ Working
Todo → Note: ✅ File modified (strikethrough/checkmark)
```

**Only if users request it:**
- Poll users after Phase 3
- "Do you want the file modified, or are visual indicators enough?"
- Most will say visual is fine
- Only implement if strong demand

---

## 🎯 RECOMMENDED IMPLEMENTATION ORDER

### **IMMEDIATE (Critical Path):**

**Step 1: Fix UI Bug** (5 minutes) ⚡
- Update `GetTodayItems()` to include null due dates
- Test: Add todo → Appears in list
- **Status:** BLOCKING EVERYTHING

**Step 2: Verify Persistence** (15 minutes)
- Add 3 todos
- Restart app
- Verify todos load
- **Status:** VALIDATION

---

### **WEEK 1: Bracket Parser Foundation**

**Day 1: Parser Implementation**
- Create `BracketTodoParser` class
- Use `SmartRtfExtractor` for plain text
- Regex pattern: `@"\[([^\[\]]+)\]"`
- Unit tests for edge cases
- **Deliverable:** Can extract brackets from text

**Day 2: Event Integration**
- Find `NoteSavedEvent` in codebase
- Create `TodoSyncService`
- Subscribe to event
- Log when event fires
- **Deliverable:** Service runs on note save

**Day 3: Basic Sync**
- Extract todos from saved note
- Create `TodoItem` with `SourceType.Note`
- Link to note (ID, path, line number)
- Insert into database
- **Deliverable:** Typing `[call John]` creates todo!

---

### **WEEK 2: Reconciliation**

**Day 4-5: Reconciliation Logic**
- Compare new brackets vs existing todos
- New → Create
- Missing → Mark orphaned
- Same → Update last_seen
- **Deliverable:** Editing notes updates todos

**Day 6: Orphan Handling**
- UI indicator for orphaned todos
- User can convert to manual or delete
- **Deliverable:** Graceful handling of deleted brackets

**Day 7: Testing**
- End-to-end scenarios
- Edge cases
- Performance with multiple notes
- **Deliverable:** Robust sync system

---

### **WEEK 3-4: Visual Feedback** (Optional but Recommended)

**Day 8-9: Highlight Overlay**
- WPF Adorner for green highlight
- Detect bracket positions in RTF
- Query completion status from DB
- Show overlay when note opened
- **Deliverable:** Visual feedback in notes

**Day 10: Polish**
- Tooltip with completion date
- Navigation (click todo → jump to note)
- Performance optimization
- **Deliverable:** Professional UX

---

### **DEFER:**
- ⏳ TODO: keyword (Week 5+ if requested)
- ⏳ Toolbar button (Phase 4+ if requested)
- ⏳ RTF modification (Phase 5+ if strong user demand)

---

## 💡 KEY INSIGHTS

### **1. Start Simple, Add Complexity Later**
- Brackets are sufficient for 90% of use cases
- TODO: keyword adds ambiguity without much value
- Toolbar button is low-value (typing `[text]` is already fast)

### **2. Non-Destructive First**
- Visual indicators avoid file corruption risks
- Easier to implement (days vs weeks)
- Reversible (can always add RTF modification later)
- Probably sufficient for most users

### **3. Leverage Existing Infrastructure**
- SmartRtfExtractor: ✅ Already solves RTF complexity
- Event system: ✅ Already exists
- Database: ✅ Already has all columns needed
- **Net new code: ~300 lines** (not thousands!)

---

## 🎯 MY RECOMMENDATION

### **Phase 2A: RTF Bracket Integration (2-3 weeks)**

**Week 1: Foundation**
1. Fix UI bug (5 min)
2. Bracket parser (2 days)
3. Event subscription (1 day)
4. Basic sync (2 days)

**Week 2: Reconciliation**
1. Reconciliation logic (3 days)
2. Orphan handling (1 day)
3. Testing (1 day)

**Week 3: Visual Feedback** (Optional)
1. Highlight overlay (2 days)
2. Tooltip & navigation (1 day)
3. Polish (2 days)

**Deliverable:** Working bracket→todo integration with visual feedback

---

### **Defer Until After User Feedback:**
- TODO: keyword (might not need it!)
- Toolbar button (might not want it!)
- RTF modification (visual indicators might be enough!)

---

## ❓ Questions for You

Before I implement, please confirm:

**1. UI Bug Fix:**
- ✅ Proceed with fix? (5 minutes)
- Option A (quick LINQ fix) or Option C (refactor to async)?

**2. RTF Parser Scope:**
- ✅ Start with brackets only?
- ✅ Defer TODO: keyword until brackets proven?
- ✅ Defer toolbar button to Phase 4+?

**3. Bidirectional Sync:**
- ✅ Visual indicators (non-destructive)?
- ❌ RTF file modification (risky, complex)?
- Or both, with visual first, modification later?

**4. Timeline:**
- Can you allocate 2-3 weeks for full RTF integration?
- Or prefer faster path (skip visual indicators, just do basic sync)?

**Once confirmed, I'll proceed with implementation in the recommended order!** 🚀

