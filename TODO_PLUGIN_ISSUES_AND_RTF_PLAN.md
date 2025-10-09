# 🔍 Todo Plugin - Issue Analysis & RTF Integration Plan

**Date:** October 9, 2025  
**Status:** Pre-Implementation Review  
**Purpose:** Analyze UI issue and plan RTF integration strategy

---

## 🐛 ISSUE #1: Todos Not Visible After Adding

### **Problem Analysis:**

You're correct - there's a **logic mismatch** between the database view and in-memory filtering!

#### **The Bug:**

**Default Smart List:**
```csharp
// TodoListViewModel defaults to "Today" smart list
_selectedSmartList = SmartListType.Today;
todos = _todoStore.GetSmartList(SmartListType.Today);
```

**In-Memory Filter (TodoStore.cs):**
```csharp
private ObservableCollection<TodoItem> GetTodayItems()
{
    var items = _todos.Where(t => !t.IsCompleted && t.IsDueToday());
    // ...
}

// And TodoItem.IsDueToday():
public bool IsDueToday()
{
    return !IsCompleted && DueDate.HasValue && DueDate.Value.Date == DateTime.UtcNow.Date;
    //                     ^^^^^^^^^^^^^^^^^^^
    //                     REQUIRES due date!
}
```

**Problem:** New todos have `DueDate = null`, so `IsDueToday()` returns `false`, so they're **excluded from Today view**!

#### **Database View (Correct Logic):**
```sql
CREATE VIEW v_today_todos AS
SELECT * FROM todos
WHERE is_completed = 0
  AND (
    due_date IS NULL              ← Includes todos with no due date!
    OR date(due_date, 'unixepoch') <= date('now')
  )
```

The SQL view INCLUDES null due dates, but the in-memory LINQ filter EXCLUDES them!

### **Impact:**
- ✅ Database query would show new todos correctly
- ❌ In-memory ObservableCollection filter hides them
- **Result:** User adds todo → Disappears from view!

### **Root Cause:**
TodoStore currently uses **in-memory LINQ filtering** instead of querying the database views.

---

### **Fix Options:**

#### **Option A: Fix In-Memory Logic** (Quick fix)
```csharp
// In TodoStore.cs
private ObservableCollection<TodoItem> GetTodayItems()
{
    var today = new SmartObservableCollection<TodoItem>();
    var items = _todos.Where(t => !t.IsCompleted && 
                                (t.DueDate == null ||                    // ← ADD THIS
                                 t.DueDate.Value.Date <= DateTime.UtcNow.Date))
                     .OrderBy(t => t.Priority)
                     .ThenBy(t => t.Order);
    today.AddRange(items);
    return today;
}
```

**Pros:** 
- Quick fix (2 minutes)
- Minimal changes
- Works immediately

**Cons:**
- Still using in-memory LINQ (not leveraging database)
- Duplicates logic (database view + C# code)
- Performance: O(n) filter every time

---

#### **Option B: Query Database Views Directly** ⭐ **RECOMMENDED**
```csharp
// In TodoStore.cs
private ObservableCollection<TodoItem> GetTodayItems()
{
    var today = new SmartObservableCollection<TodoItem>();
    
    // Query the database view directly!
    var items = Task.Run(async () => await _repository.GetTodayTodosAsync()).Result;
    
    today.AddRange(items);
    return today;
}
```

**Pros:**
- ✅ Uses database views (leverage indexes!)
- ✅ Single source of truth (SQL defines logic)
- ✅ Performance: Database query is faster than LINQ
- ✅ Consistent with database-first architecture

**Cons:**
- Synchronous call to async method (not ideal)
- Need to ensure _repository is available in TodoStore

---

#### **Option C: Refactor TodoStore to be Async** ⭐⭐ **BEST LONG-TERM**
```csharp
// Change interface
public interface ITodoStore
{
    ObservableCollection<TodoItem> AllTodos { get; }
    Task<ObservableCollection<TodoItem>> GetSmartListAsync(SmartListType type);  // ← Async!
    // ...
}

// In TodoListViewModel
private async Task LoadTodosAsync()
{
    if (_selectedSmartList.HasValue)
    {
        todos = await _todoStore.GetSmartListAsync(_selectedSmartList.Value);  // ← Await!
    }
}
```

**Pros:**
- ✅ Proper async/await pattern
- ✅ Leverages database views
- ✅ Best architecture
- ✅ No blocking calls

**Cons:**
- More refactoring required
- Changes interface (breaking change for existing code)
- Takes ~30 minutes

---

### **Recommendation for Issue #1:**

**Short-term (5 minutes):** Option A - Fix the LINQ logic  
**Medium-term (30 minutes):** Option C - Refactor to async  

**Why both?** 
- Option A unblocks testing TODAY
- Option C makes architecture correct for long-term

**Priority:** HIGH - This is blocking basic functionality!

---

## 🎯 ISSUE #2: RTF Integration Strategy

### **Your Requirements:**

1. **Priority 1:** `[bracket]` syntax for todos
2. **Future:** `TODO: text` keyword syntax
3. **Future:** Toolbar button for selected text
4. **Future:** Handling updates when todo marked done

### **Excellent prioritization!** Start simple, add complexity later.

---

### **Phase 2A: Bracket Parser (Week 1-2)** ⭐ **START HERE**

#### **What to Build:**

**1. Simple Bracket Pattern Matcher**
```csharp
public class BracketTodoParser
{
    private readonly Regex _bracketPattern = new Regex(
        @"\[([^\[\]]+)\]",  // Matches: [any text without nested brackets]
        RegexOptions.Compiled | RegexOptions.Multiline
    );
    
    public List<TodoCandidate> ExtractFromPlainText(string plainText)
    {
        var matches = _bracketPattern.Matches(plainText);
        var candidates = new List<TodoCandidate>();
        
        foreach (Match match in matches)
        {
            candidates.Add(new TodoCandidate
            {
                Text = match.Groups[1].Value.Trim(),
                StartIndex = match.Index,
                EndIndex = match.Index + match.Length,
                OriginalMatch = match.Value,
                Confidence = 0.9  // High confidence for explicit brackets
            });
        }
        
        return candidates;
    }
}
```

**Test Cases:**
- `[call John about project]` → "call John about project"
- `[buy milk]` → "buy milk"
- `Meeting notes [send follow-up email] tomorrow` → "send follow-up email"
- `[[nested brackets]]` → Skip (too complex for now)

**Complexity:** LOW  
**Timeline:** 2-3 days  
**Blocker:** None! Can use existing `SmartRtfExtractor.ExtractPlainText()`

---

**2. Subscribe to NoteSavedEvent**
```csharp
public class TodoSyncService
{
    public async Task InitializeAsync()
    {
        await _eventBus.SubscribeAsync<NoteSavedEvent>(OnNoteSaved);
    }
    
    private async Task OnNoteSaved(NoteSavedEvent e)
    {
        // Extract plain text from RTF
        var plainText = SmartRtfExtractor.ExtractPlainText(e.Content);
        
        // Find todos
        var candidates = _bracketParser.ExtractFromPlainText(plainText);
        
        // Create TodoItems
        foreach (var candidate in candidates)
        {
            var todo = new TodoItem
            {
                Text = candidate.Text,
                SourceType = TodoSource.Note,
                SourceNoteId = e.NoteId,
                SourceFilePath = e.FilePath
            };
            
            await _repository.InsertAsync(todo);
        }
        
        _logger.Info($"Extracted {candidates.Count} todos from note: {e.NoteTitle}");
    }
}
```

**Complexity:** MEDIUM  
**Timeline:** 3-4 days  
**Blocker:** Need to identify NoteSavedEvent in codebase

---

**3. Reconciliation Logic**
```csharp
// Challenge: What if user edits note?
// Before: "Meeting [call John] and [send email]"
// After:  "Meeting [call Sarah] and [send email]"
// 
// Need to:
// 1. Detect "call John" was removed → Mark orphaned
// 2. Detect "call Sarah" is new → Create new todo
// 3. Keep "send email" (unchanged)

public async Task ReconcileTodosForNote(Guid noteId, List<TodoCandidate> newCandidates)
{
    var existingTodos = await _repository.GetByNoteIdAsync(noteId);
    
    // Simple approach: Match by text
    var newTexts = newCandidates.Select(c => c.Text).ToHashSet();
    var existingTexts = existingTodos.ToDictionary(t => t.Text, t => t);
    
    // Find removed todos
    foreach (var todo in existingTodos)
    {
        if (!newTexts.Contains(todo.Text))
        {
            todo.IsOrphaned = true;
            await _repository.UpdateAsync(todo);
        }
    }
    
    // Find new todos
    foreach (var candidate in newCandidates)
    {
        if (!existingTexts.ContainsKey(candidate.Text))
        {
            // New todo!
            await CreateTodoFromCandidate(candidate, noteId);
        }
    }
}
```

**Complexity:** MEDIUM-HIGH  
**Timeline:** 4-5 days  
**Challenge:** Text matching isn't perfect (what if text changes slightly?)

---

### **Phase 2B: TODO: Keyword** (Week 3) - **DEFER**

**Syntax:** `TODO: call John about project`

**Why Defer:**
- More complex pattern (ambiguous, needs context analysis)
- Bracket syntax is explicit and unambiguous
- Get bracket parser working first, learn from it
- Can add TODO: as Phase 3

**Recommendation:** Wait until brackets work perfectly, then add this.

---

### **Phase 2C: Toolbar Button** (Week 4+) - **DEFER**

**Concept:** Select text in RTF editor → Click button → Adds to todo tree

**Why Defer:**
- Requires RTF editor integration
- Needs to modify RTF content (add brackets around selection)
- Complex UX (where does button live? Selection handling?)
- Not in implementation guide

**Recommendation:** Consider this a "power user" feature for Phase 6+

---

### **Phase 2D: Bidirectional Update** (Week 5+) - **DEFER BUT IMPORTANT**

**Scenario:**
```
1. Note contains: "[call John]"
2. Todo created in panel
3. User completes todo in panel
4. What happens to note?
```

**Options:**

**A. Visual Indicator Only** (Easier)
- Don't modify RTF file
- Show green highlight overlay when viewing note
- Non-destructive (safer)

**B. Modify RTF Content** (Harder)
- Change `[call John]` to `[✓ call John]` or strikethrough
- Requires RTF editing
- Risk of corrupting file

**Recommendation:** 
- Start with Option A (visual indicators)
- Add Option B in Phase 6 if users request it

---

## 🎯 RECOMMENDED IMPLEMENTATION ORDER

### **IMMEDIATE (This Week):**

**Task 1: Fix UI Visibility Bug** ⚡ **CRITICAL**
- Time: 5 minutes
- Fix: Option A (update LINQ logic) or Option C (make async)
- Why: Unblocks testing
- **Status:** BLOCKING

**Task 2: Verify Persistence Works**
- Time: 15 minutes
- Test: Add todo, restart app, verify it loads
- Why: Confirms database layer is solid
- **Status:** VALIDATION

---

### **WEEK 1-2: Bracket Parser Foundation**

**Task 3: Create BracketTodoParser**
- Extract `[todo text]` from plain text
- Simple regex pattern
- High confidence scoring (0.9 for explicit brackets)
- Unit tests for edge cases

**Task 4: Find NoteSavedEvent**
- Locate event in codebase
- Understand event payload (has Content, FilePath, NoteId?)
- Test subscription works

**Task 5: Basic One-Way Sync**
- Subscribe to NoteSavedEvent
- Extract brackets from saved note
- Create todos in database
- Link to note (SourceNoteId, SourceFilePath)

**Deliverable:** Typing `[call John]` in note creates todo ✨

---

### **WEEK 3-4: Reconciliation**

**Task 6: Implement Reconciliation Logic**
- On note save, compare new brackets vs existing todos
- New bracket → Create todo
- Missing bracket → Mark orphaned (don't delete!)
- Same bracket → Update last_seen_in_source

**Task 7: Orphan Management UI**
- Show orphaned todos with indicator
- User can: Keep as manual todo, or Delete
- Batch cleanup option

**Deliverable:** Editing notes updates todos correctly

---

### **WEEK 5+: Bidirectional (Phase 2, Later)**

**Task 8: Visual Indicators** (Easier path)
- Detect completed todos when viewing note
- Highlight `[completed text]` with green overlay
- Add tooltip showing completion date
- Non-destructive (doesn't modify RTF file)

**Task 9: RTF Content Modification** (Harder path - OPTIONAL)
- Modify RTF to show completion
- Options: `[✓ text]`, strikethrough, color change
- Atomic file updates (don't corrupt on failure)
- Backup before modification

**Deliverable:** Complete todo → Visual feedback in note

---

## 📊 Feature Complexity Analysis

| Feature | Complexity | Timeline | Value | Priority |
|---------|-----------|----------|-------|----------|
| Fix UI visibility bug | LOW | 5 min | HIGH | ⭐⭐⭐ DO NOW |
| Bracket parser | LOW | 2-3 days | HIGH | ⭐⭐⭐ Week 1 |
| NoteSavedEvent subscription | LOW | 1 day | HIGH | ⭐⭐⭐ Week 1 |
| Basic one-way sync | MEDIUM | 3-4 days | HIGH | ⭐⭐⭐ Week 2 |
| Reconciliation | MEDIUM-HIGH | 4-5 days | MEDIUM | ⭐⭐ Week 3 |
| Visual indicators | MEDIUM | 3-4 days | MEDIUM | ⭐⭐ Week 4 |
| **TODO: keyword** | MEDIUM | 3-4 days | LOW | ⭐ Phase 3 |
| **Toolbar button** | MEDIUM-HIGH | 1 week | LOW | ⭐ Phase 4+ |
| **RTF modification** | HIGH | 1-2 weeks | MEDIUM | ⭐ Phase 5+ |

---

## 🎯 RTF Integration Architecture

### **Leveraging Existing Infrastructure:**

✅ **SmartRtfExtractor** - Already exists! Perfect for extracting plain text
```csharp
var plainText = SmartRtfExtractor.ExtractPlainText(rtfContent);
// Then search for [brackets] in plain text
```

✅ **Event System** - NoteSavedEvent should exist
```csharp
// Pattern already used throughout NoteNest
await _eventBus.SubscribeAsync<NoteSavedEvent>(handler);
```

✅ **Database Schema** - Already has all source tracking columns
```sql
source_type, source_note_id, source_file_path, source_line_number, is_orphaned
```

### **New Components Needed:**

❌ **IRtfService** - Not required yet!
- SmartRtfExtractor is static utility, works fine for now
- Can refactor to interface later if needed

❌ **Complex RTF Parsing** - Not required for brackets!
- Just extract plain text, find `[text]` patterns
- No need to parse RTF tokens/structure

✅ **TodoSyncService** - New service for sync logic
- Subscribe to events
- Coordinate parsing + reconciliation
- Background service (BackgroundService base class)

---

## 🎨 Implementation Strategy

### **Minimal Viable RTF Integration (2 weeks):**

**Week 1: Foundation**
1. Fix UI bug (5 min)
2. Create `BracketTodoParser` (1 day)
3. Create `TodoSyncService` (1 day)
4. Subscribe to NoteSavedEvent (1 day)
5. Basic integration test (1 day)

**Week 2: Reconciliation**
1. Implement reconciliation logic (3 days)
2. Handle orphaned todos (1 day)
3. UI indicator for note-linked todos (1 day)
4. End-to-end testing (2 days)

**Deliverable:** 
- Type `[call John]` → Todo created ✅
- Edit note, remove `[call John]` → Todo marked orphaned ✅
- Note-linked todos show 📄 icon ✅

---

### **Extended RTF Features (Week 3+):**

**Week 3: TODO: Keyword**
- Add keyword pattern
- Same sync logic
- Combined with bracket results

**Week 4+: Bidirectional Feedback**
- Visual indicators (green highlight)
- Tooltip system
- Navigation (click todo → jump to note)

**Week 5+: Advanced**
- Toolbar button
- RTF content modification
- Conflict resolution

---

## ⚠️ Risks & Mitigation

### **Risk 1: Text-Based Matching is Imperfect**

**Problem:**
```
Note before: "[Call John about project meeting]"
Note after:  "[Call John about the project meeting]"
                         ^^^
```
Is this the same todo, or different?

**Mitigation:**
- **Phase 1:** Simple text match (exact)
- **Phase 2:** Add fuzzy matching (Levenshtein distance)
- **Phase 3:** Use line number + partial text match
- **Best:** Generate stable ID from (noteId + lineNumber + text hash)

---

### **Risk 2: Performance with Many Notes**

**Problem:** User has 1000 notes, each note save triggers full scan?

**Mitigation:**
- Only scan the note that was saved (not all notes)
- Use `source_note_id` index for fast lookup
- Debounce rapid saves (wait 500ms after last save)
- Background task queue (don't block UI)

---

### **Risk 3: Race Conditions**

**Problem:** User edits note while sync is processing?

**Mitigation:**
- Use `last_seen_in_source` timestamp
- Compare note modified date vs last sync
- Skip if note modified during processing
- Retry on next save

---

### **Risk 4: RTF Complexity**

**Problem:** RTF has formatting codes, Unicode, special characters

**Solution:** ✅ **Already solved!**
- `SmartRtfExtractor.ExtractPlainText()` handles all this
- Regex patterns, character mapping, cleanup all done
- Just use it!

---

## 🎯 Answers to Your Questions

### **Q1: Should TODO: keyword come before or after brackets?**

**Answer: AFTER brackets.** Here's why:

| Aspect | Bracket `[text]` | Keyword `TODO: text` |
|--------|------------------|----------------------|
| **Ambiguity** | Low - explicit delimiter | High - appears in prose |
| **Complexity** | Simple regex | Need context analysis |
| **User Intent** | 100% clear | Maybe 70% clear |
| **False Positives** | Rare | Common ("TODO: TBD", "TODO list:") |
| **Implementation** | 2-3 days | 3-4 days + tuning |

**Recommendation:**
1. **Week 1-2:** Bracket parser (simple, unambiguous)
2. **Week 3:** Test with real notes, gather feedback
3. **Week 4:** If users want TODO: keyword, add it then

**Learning:** See what patterns users actually use in notes before adding more complexity.

---

### **Q2: Should toolbar button come before or after brackets?**

**Answer: MUCH LATER (Phase 4+).** Here's why:

**Toolbar button requires:**
- RTF editor integration (where does button go?)
- Selection API (get selected text from RTF)
- RTF modification (insert `[` and `]` around selection)
- Undo support
- Edge cases (selection spans paragraphs, contains formatting, etc.)

**Complexity:** HIGH  
**Value:** MEDIUM (nice to have, but typing `[text]` is already fast)  
**Blocker:** Need full RTF editing integration

**Recommendation:** 
- Get automatic bracket parsing working first
- See if users even want this feature
- If they do, add it as polish in Phase 4+

---

### **Q3: What happens when note-linked todo is marked done?**

**Great question!** Two approaches:

#### **Approach A: Visual Indicator Only** ⭐ **START HERE**

**What happens:**
```
1. User types in note: "[call John]"
2. Todo created automatically
3. User completes todo in panel
4. When viewing note: Green highlight over "[call John]"
5. RTF file UNCHANGED (non-destructive)
```

**Implementation:**
- WPF Adorner layer over RichTextBox
- Detect bracket positions when rendering
- Query database for completion status
- Show overlay if completed

**Pros:**
- ✅ Non-destructive (safe!)
- ✅ Works even if RTF file modified externally
- ✅ No file corruption risk
- ✅ Fast to implement (3-4 days)

**Cons:**
- ⚠️ Visual only (not visible in other editors)
- ⚠️ Requires note to be open to see indicator

---

#### **Approach B: Modify RTF Content** ⚠️ **DEFER TO PHASE 5+**

**What happens:**
```
1. User types in note: "[call John]"
2. Todo created automatically
3. User completes todo in panel
4. RTF file modified: "[✓ call John]" or strikethrough
5. Change persists in file
```

**Implementation:**
- Parse RTF to find bracket position
- Insert RTF formatting codes for strikethrough/checkmark
- Atomic file write (backup + write + verify)
- Trigger note reload in UI

**Pros:**
- ✅ Visible in any RTF editor
- ✅ Persists with note file

**Cons:**
- ❌ Modifies source file (risky!)
- ❌ Complex RTF editing logic
- ❌ Potential file corruption
- ❌ Conflicts if note edited elsewhere
- ❌ Harder to implement (1-2 weeks)

---

**Recommendation:** 
- **Week 1-3:** Focus on note → todo (one-way)
- **Week 4:** Add visual indicators (Approach A)
- **Week 8+:** Consider RTF modification (Approach B) if users request it

**Rationale:** 80/20 rule - Visual indicators give 80% of the value with 20% of the complexity.

---

## 🎯 FINAL RECOMMENDATIONS

### **Fix Order:**

**TODAY (5 minutes):**
1. ✅ Fix UI visibility bug (Option A: LINQ logic fix)

**THIS WEEK (2-3 days):**
2. ✅ Create `BracketTodoParser` 
3. ✅ Subscribe to NoteSavedEvent
4. ✅ Basic one-way sync (note → todo)

**NEXT WEEK (1 week):**
5. ✅ Reconciliation logic
6. ✅ Orphan handling
7. ✅ Note-linked todo indicator (📄 icon)

**WEEK 3-4 (1 week):**
8. ✅ Visual indicators in notes (green highlight)
9. ✅ Tooltip with completion info
10. ✅ End-to-end testing

**DEFER TO LATER:**
- ⏳ TODO: keyword syntax (Phase 3)
- ⏳ Toolbar button (Phase 4+)
- ⏳ RTF content modification (Phase 5+)

---

## 📋 Implementation Plan Document

### **Should we:**

1. ✅ **Fix UI bug first** (5 minutes) - CRITICAL
2. ✅ **Test persistence** (15 minutes) - Verify database works
3. ✅ **Start bracket parser** (2-3 days) - Simple, high value
4. ✅ **Add TODO: keyword later** (after brackets work)
5. ✅ **Toolbar button much later** (Phase 4+, maybe never)
6. ✅ **Visual indicators before RTF modification** (safer, faster)

### **Skip for now:**
- ❌ Rich domain model (DTOs work fine)
- ❌ TODO: keyword (wait until brackets proven)
- ❌ Toolbar button (low priority)
- ❌ RTF file modification (too risky for Phase 1)

---

## 🎯 Next Steps

**Before implementing anything:**

1. **Confirm the approach:**
   - Start with UI bug fix?
   - Then bracket parser?
   - Defer TODO: keyword and toolbar?

2. **Clarify requirements:**
   - Visual indicators OK for completed todos?
   - Or must modify RTF file?

3. **Set expectations:**
   - Bracket parsing: 2-3 days
   - Full RTF integration: 3-4 weeks total
   - Todo → Note feedback: Week 4+

**Once confirmed, I'll implement in this order:**
1. Fix UI bug (5 min)
2. Create BracketTodoParser (1 day)
3. Wire up NoteSavedEvent (1 day)
4. Test end-to-end (1 day)

**Total to working bracket parser: 3-4 days**

---

**Ready to proceed?** Please confirm:
- ✅ Fix UI bug first?
- ✅ Start with bracket parser?
- ✅ Defer TODO: keyword?
- ✅ Visual indicators (not RTF modification)?

