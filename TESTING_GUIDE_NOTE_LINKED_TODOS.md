# 🧪 TESTING GUIDE - Note-Linked Todos (Option B Fix)

**Date:** October 18, 2025  
**What Was Fixed:** Note-linked todos not appearing in UI  
**Implementation:** Option B (Proper Event Sourcing)  
**Build Status:** ✅ 0 Errors  
**Database Preparation:** ✅ projections.db deleted (will be recreated)

---

## 🚀 TESTING SEQUENCE

### **Step 1: Launch NoteNest**

**What to Expect:**
```
App will:
1. Detect projections.db is missing
2. Create fresh projections.db with NEW schema (includes source tracking columns)
3. Replay all events from events.db
4. Build all projections (tree_view, tag_vocabulary, entity_tags, todo_view)
5. Load UI
```

**Expected Startup Logs:**
```
[INF] 🔧 Initializing event store and projections...
[INF] ✅ Event store initialized successfully
[INF] ✅ Projections initialized successfully
[INF] Starting projection catch-up...
[INF] Projection TodoView catching up from 0 to XXX
[DBG] Projection TodoView processed batch: XXX events
[INF] Projection TodoView caught up: XXX events processed
[INF] Application started
```

**⚠️ CRITICAL: Look for any errors about:**
- ❌ "no such column: source_line_number" → Would indicate schema not rebuilt
- ❌ "UNIQUE constraint failed" → Would indicate INSERT OR REPLACE didn't work
- ❌ "Failed to deserialize event" → Would indicate event compatibility issue

**If you see errors:** Stop and share logs with me.

**If no errors:** Proceed to Step 2!

---

### **Step 2: Test Manual Todo Creation (Regression Test)**

**Purpose:** Ensure we didn't break existing manual todo functionality

**Actions:**
1. Open TodoPlugin panel (Ctrl+B or right sidebar)
2. In quick-add textbox, type: "Buy milk"
3. Press Enter

**Expected Result:**
```
✅ Todo appears immediately: "Buy milk"
✅ Shows in "Uncategorized" or selected category
✅ No errors in logs
```

**Expected Logs:**
```
[INF] [CreateTodoHandler] Creating todo: 'Buy milk'
[INF] [CreateTodoHandler] ✅ Todo persisted to event store: {guid}
[DBG] Synchronizing projections after CreateTodoCommand
[DBG] [TodoView] Todo created: 'Buy milk' (source: manual)  ← Note: "manual"
[INF] [TodoStore] ✅ Todo added to UI collection
```

**If this works:** Manual todos still work! ✅  
**If this fails:** Stop, share logs - regression detected

---

### **Step 3: Test Note-Linked Todo Creation (THE BIG TEST)**

**Purpose:** Verify the main fix - note-linked todos now appear

**Actions:**
1. Create or open an existing note
2. In the note content, type:
   ```
   Meeting Notes - Q4 Planning
   ============================
   
   Agenda:
   - Budget review
   - Timeline discussion
   
   Action Items:
   [call John to confirm budget by Friday]
   [send revised proposal to Sarah]
   [schedule follow-up meeting next week]
   
   Notes:
   - All approved!
   ```

3. Save the note (Ctrl+S)
4. Wait 2-3 seconds (for debouncing + processing)
5. Open TodoPlugin panel (if not already open)

**Expected Result:**
```
✅ 3 todos appear in TodoPlugin panel:
   1. "call John to confirm budget by Friday"
   2. "send revised proposal to Sarah"  
   3. "schedule follow-up meeting next week"

✅ Todos auto-categorized under note's parent folder
✅ Todos have inherited tags (if folder/note have tags)
✅ Todos show 📄 icon (note-linked indicator)
```

**Expected Logs:**
```
[DBG] [TodoSync] Note save queued for processing: Q4 Planning.rtf
[INF] [TodoSync] Processing note: Q4 Planning.rtf
[DBG] [BracketParser] Extracted 3 todo candidates
[INF] [TodoSync] Note is in category: {guid} - todos will be auto-categorized
[DBG] [TodoSync] Reconciling 3 candidates with 0 existing todos

For each todo:
[INF] [CreateTodoHandler] Creating todo: 'call John to confirm budget by Friday'
[INF] [CreateTodoHandler] ✅ Todo persisted to event store: {guid}
[DBG] Synchronizing projections after CreateTodoCommand
[DBG] [TodoView] Todo created: 'call John...' (source: C:\...\Q4 Planning.rtf)  ← KEY!
[INF] [CreateTodoHandler] ✅ Applied X inherited tags to todo ... via events
[DBG] [TagView] Tag added: 'work' to todo ...  ← If folder has tags
[INF] [TodoStore] ✅ Todo added to UI collection: 'call John...'
[INF] [TodoSync] ✅ Created todo from note: "call John..." [auto-categorized: {guid}]
```

**⚠️ KEY SUCCESS INDICATORS:**
- ✅ Log says: `(source: C:\...\Q4 Planning.rtf)` NOT `(source: manual)`
- ✅ No UNIQUE constraint errors
- ✅ No "Todo not found in database" errors
- ✅ Tag events emitted (if folder/note have tags)
- ✅ Todos appear in UI within 2 seconds

---

### **Step 4: Verify Source Tracking (Jump to Source)**

**Purpose:** Confirm source tracking is complete

**Actions:**
1. Right-click one of the created todos
2. Look for "Jump to Source" option (if UI implemented)
3. OR verify data directly

**Direct Verification (if Jump to Source not in UI yet):**

You can check the database directly:

```sql
-- Open: %LOCALAPPDATA%\NoteNest\projections.db

SELECT 
    id,
    text,
    source_type,
    source_note_id,
    source_file_path,
    source_line_number,
    source_char_offset
FROM todo_view
WHERE source_type = 'note'
LIMIT 5;
```

**Expected Result:**
```
id: ea9f6bc8-...
text: "call John to confirm budget by Friday"
source_type: "note"
source_note_id: {note-guid}  ← NOT NULL!
source_file_path: "C:\Users\...\Q4 Planning.rtf"  ← NOT NULL!
source_line_number: 10  ← Line number in RTF file!
source_char_offset: 234  ← Character position!
```

**If all fields populated:** Source tracking WORKS! ✅  
**If NULL:** Something wrong, share logs

---

### **Step 5: Test Event Replay (Idempotency)**

**Purpose:** Verify projections can rebuild (no UNIQUE constraint errors)

**Actions:**
1. Note how many todos you have (e.g., 3 todos)
2. **Close app completely**
3. **Restart app**
4. **Wait for startup** (~5-10 seconds)
5. **Open TodoPlugin panel**

**Expected Result:**
```
✅ All 3 todos still appear
✅ Same todos, same data
✅ No duplicates
✅ Tags still present
```

**Expected Startup Logs:**
```
[INF] Starting projection catch-up...
[INF] Projection TodoView catching up from 0 to XXX
[DBG] Projection TodoView processed batch: XXX events (including multiple TodoCreatedEvents)
[INF] Projection TodoView caught up: XXX events processed
[INF] Application started
```

**⚠️ KEY: Look for:**
- ✅ NO "UNIQUE constraint failed: todo_view.id" errors
- ✅ All events process successfully
- ✅ Projections rebuild cleanly

**If UNIQUE constraint error appears:** INSERT OR REPLACE didn't work - share logs  
**If no errors:** Event replay is SAFE! ✅

---

### **Step 6: Test Tag Inheritance**

**Purpose:** Verify tags are event-sourced and visible

**Prerequisites:**
- Set tags on a folder (right-click folder → "Set Folder Tags" → add "work", "urgent")
- OR ensure note has tags

**Actions:**
1. Create note in tagged folder
2. Add bracket: `[test todo with tags]`
3. Save
4. Check TodoPlugin panel

**Expected Result:**
```
✅ Todo appears: "test todo with tags"
✅ Todo has inherited tags: ["work", "urgent"]
✅ Tags visible in UI (chip badges or tooltip)
```

**Expected Logs:**
```
[INF] [CreateTodoHandler] ✅ Applied 2 inherited tags to todo ... via events (folder: 2, note: 0)
[DBG] [TagView] Tag added: 'work' to todo ...
[DBG] [TagView] Tag added: 'urgent' to todo ...
```

**Verify in database:**
```sql
-- Open: %LOCALAPPDATA%\NoteNest\projections.db

SELECT 
    et.entity_id,
    et.tag,
    et.display_name,
    et.source,
    tv.text
FROM entity_tags et
JOIN todo_view tv ON tv.id = et.entity_id
WHERE et.entity_type = 'todo'
AND tv.text LIKE '%test todo with tags%';
```

**Expected:**
```
entity_id: {todo-guid}
tag: "work"
display_name: "work"
source: "auto-inherit"  ← Correct!
text: "test todo with tags"

entity_id: {todo-guid}
tag: "urgent"
display_name: "urgent"
source: "auto-inherit"
text: "test todo with tags"
```

**If tags present in entity_tags:** Tags are event-sourced! ✅  
**If tags missing:** Tag events not processed, share logs

---

### **Step 7: Test Multiple Todos in One Note**

**Purpose:** Stress test the system

**Actions:**
1. Create a note with many brackets:
   ```
   Project Tasks:
   [design mockups]
   [review with team]
   [implement feedback]
   [test on staging]
   [deploy to production]
   [write documentation]
   [notify stakeholders]
   ```

2. Save
3. Wait 2-3 seconds
4. Check TodoPlugin panel

**Expected Result:**
```
✅ All 7 todos appear
✅ All auto-categorized to same folder
✅ All have same inherited tags
✅ All have correct source tracking
✅ No duplicates
```

**If all appear:** Batch processing works! ✅  
**If some missing:** Check logs for which failed

---

## 📊 SUCCESS CHECKLIST

After all tests, you should have:

**Basic Functionality:**
- [ ] Manual todos create successfully (Step 2)
- [ ] Note-linked todos appear in UI (Step 3)
- [ ] Multiple todos from one note work (Step 7)

**Data Integrity:**
- [ ] Source tracking populated (Step 4)
- [ ] Tags inherited and visible (Step 6)
- [ ] Event replay safe - no errors on restart (Step 5)

**No Errors:**
- [ ] No UNIQUE constraint errors
- [ ] No deserialization errors
- [ ] No "Todo not found" errors
- [ ] No schema errors

---

## 🚨 TROUBLESHOOTING

### **Issue: "no such column: source_line_number"**

**Cause:** projections.db not rebuilt with new schema

**Solution:**
1. Close app
2. Delete: `%LOCALAPPDATA%\NoteNest\projections.db`
3. Restart app (will recreate)

---

### **Issue: "UNIQUE constraint failed: todo_view.id"**

**Cause:** INSERT OR REPLACE didn't apply (shouldn't happen - we built successfully)

**Check:**
1. Open `TodoProjection.cs` line 146
2. Verify says: `INSERT OR REPLACE INTO todo_view`
3. If says `INSERT INTO` → code didn't save, reapply changes

---

### **Issue: Todos appear but no tags**

**Cause:** Tag events not processing

**Check logs for:**
```
[DBG] [TagView] Tag added: ...
```

**If missing:** TagAddedToEntity events not emitted

**Verify:**
1. Open `TodoAggregate.cs` line 271
2. Should have: `AddDomainEvent(new TagAddedToEntity(...))`

---

### **Issue: Source tracking NULL**

**Cause:** Event doesn't contain source fields (shouldn't happen)

**Verify:**
1. Check logs for: `(source: manual)` vs `(source: C:\...\filename.rtf)`
2. If says "manual" for note-linked todo → event fields not populated

**Check:**
1. `TodoAggregate.CreateFromNote()` line 103-110
2. Should emit event with sourceNoteId, sourceFilePath, etc.

---

## 📋 WHAT TO REPORT

### **If Everything Works:**

Report:
```
✅ Manual todos: Working
✅ Note-linked todos: Appearing in UI
✅ Source tracking: Complete
✅ Tags: Inherited and visible
✅ Event replay: No errors
✅ No UNIQUE constraint errors

READY FOR PRODUCTION!
```

---

### **If Issues Found:**

Share with me:
1. **Which test failed** (Step number)
2. **Error message** (exact text from logs)
3. **What you expected** vs **what happened**
4. **Relevant log section** (last 50 lines)

I can quickly diagnose and fix any issues!

---

## 🎯 EXPECTED TIMELINE

**Total Testing Time:** 15-20 minutes

- Step 1 (Launch): 1 minute
- Step 2 (Manual todo): 2 minutes
- Step 3 (Note-linked): 5 minutes
- Step 4 (Source tracking): 3 minutes
- Step 5 (Event replay): 3 minutes
- Step 6 (Tags): 3 minutes
- Step 7 (Multiple todos): 3 minutes

**If all pass:** Implementation is SUCCESSFUL! 🎉

---

## 🎉 SUCCESS CRITERIA

**Minimum (Critical):**
- [x] Build: 0 errors ✅
- [x] projections.db deleted ✅
- [ ] Note-linked todos appear in UI
- [ ] No UNIQUE constraint errors
- [ ] Source tracking populated

**Complete (Full Success):**
- [ ] Tags inherited from folder + note
- [ ] Event replay works (restart app)
- [ ] Multiple todos in one note
- [ ] All source fields populated
- [ ] No regressions

---

## 🚀 READY TO TEST!

**Next Action:** Launch NoteNest and follow Steps 1-7

**I'm standing by to help with any issues that arise!**

Good luck! 🎯

