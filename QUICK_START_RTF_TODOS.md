# 🚀 Quick Start: RTF Bracket Todos

**Status:** ✅ IMPLEMENTED & READY  
**Build:** ✅ 0 Errors  
**Time to Test:** 2 minutes

---

## ⚡ 30-Second Test

```powershell
# 1. Launch
.\Launch-NoteNest.bat

# 2. Open a note (any note)

# 3. Type in the note:
[call John about project]
[send follow-up email]

# 4. Save (Ctrl+S)

# 5. Wait 1 second

# 6. Open todo panel (Ctrl+B)

# Expected: 2 new todos with 📄 icons!
```

---

## 🎯 What to Look For

### **✅ Success Indicators:**

1. **Todos appear in panel** with 📄 icon
2. **Hover over 📄** → Shows note filename & line number
3. **Manual todo** (typed in panel) → No 📄 icon
4. **Note-linked todo** → Has 📄 icon

### **✅ Console Logs (if running from IDE):**

```
[TodoSync] Starting todo sync service - monitoring note saves for bracket todos
✅ TodoSyncService subscribed to note save events
[TodoSync] Processing note: YourNote.rtf
[BracketParser] Extracted 2 todo candidates
[TodoSync] Created todo from note: "call John about project"
[TodoSync] Created todo from note: "send follow-up email"
```

---

## 🧪 Complete Test Sequence

### **Test 1: Basic Extraction** (1 minute)

```
1. Create/open note
2. Type: "[buy groceries]"
3. Save (Ctrl+S)
4. Wait 1 second
5. Open todo panel

Expected: Todo "buy groceries" with 📄 icon
```

---

### **Test 2: Multiple Todos** (1 minute)

```
1. In same note, add more:
   "[call dentist]"
   "[finish report]"
2. Save (Ctrl+S)
3. Check panel

Expected: 3 todos total, all with 📄 icons
```

---

### **Test 3: Reconciliation** (2 minutes)

```
1. Edit note, remove "[call dentist]"
2. Save (Ctrl+S)
3. Wait 1 second
4. Check panel

Expected:
- "buy groceries" → Still there, blue 📄
- "call dentist" → Red 📄 (orphaned)
- "finish report" → Still there, blue 📄
```

---

### **Test 4: Persistence** (2 minutes)

```
1. Close app completely
2. Reopen: .\Launch-NoteNest.bat
3. Open todo panel

Expected: All todos still there (manual + note-linked)
```

---

### **Test 5: Manual vs Note-Linked** (1 minute)

```
1. Add manual todo in panel: "Manual task"
2. Add bracket in note: "[Note task]"
3. Save note

Expected:
- "Manual task" → No 📄 icon
- "Note task" → Has 📄 icon

This shows which todos came from notes!
```

---

## 📄 Example Note

Create a note called "Project Planning.rtf" with this content:

```
Project Planning Meeting - October 2025

Action Items:
[schedule kickoff meeting with team]
[review requirements document]
[send agenda to stakeholders]
[prepare presentation slides]

Follow-up needed:
[call John about budget]
[email Sarah the timeline]

Done:
No completed items yet

Notes:
- Meeting went well
- Team is excited about the project
```

**Save this note (Ctrl+S)** and watch 6 todos appear in the panel! ✨

---

## 🎨 Visual Guide

### **What You'll See:**

```
Todo Panel:
┌─────────────────────────────────────┐
│ Add a task...               [Add]   │
├─────────────────────────────────────┤
│ Filter tasks...                     │
├─────────────────────────────────────┤
│ ☐ Manual task          ⭐           │ ← No 📄
│ ☐ schedule kickoff... 📄 ⭐          │ ← Blue 📄
│ ☐ review requirements 📄 ⭐          │ ← Blue 📄  
│ ☑ send agenda...      📄 ⭐          │ ← Blue 📄
│ ☐ call John          ⚠️ ⭐          │ ← Red 📄 (orphaned)
└─────────────────────────────────────┘
```

**Legend:**
- No icon = Manual todo (typed in panel)
- 📄 Blue = Note-linked todo (from bracket)
- 📄 Red = Orphaned (bracket was removed)
- ⭐ = Favorite

---

## 🔧 Troubleshooting

### **No todos appear after adding bracket:**

**Check:**
1. Did you save the note? (Ctrl+S)
2. Wait 1 second (debounce delay)
3. Is file .rtf? (not .txt)
4. Check console for errors

**Try:**
- Check logs for "[TodoSync]" messages
- Verify brackets are properly formed: `[text]`
- Ensure text inside isn't filtered (not "[TBD]" or "[N/A]")

---

### **Todos appear but no 📄 icon:**

**Check:**
- Is `SourceNoteId` populated in database?
- Check ViewModel: `IsNoteLinked` property
- Check XAML binding

**Try:**
- Close and reopen panel
- Check logs for "Created todo from note"

---

### **All todos marked as orphaned:**

**Causes:**
- Note was deleted
- Note was moved
- Note path changed

**Solution:**
- User can manually un-orphan (future feature)
- Or delete the orphaned todos
- Or re-add brackets to note

---

## ⚡ Quick Commands

```powershell
# Build
dotnet build NoteNest.sln

# Launch
.\Launch-NoteNest.bat

# Check database
cd "$env:LOCALAPPDATA\NoteNest\.plugins\NoteNest.TodoPlugin"
dir todos.db

# View logs (if running from IDE)
# Look for: [TodoSync], [BracketParser] messages
```

---

## 🎯 Success!

**If you see:**
- ✅ Todos appear after typing brackets
- ✅ 📄 icon shows for note-linked todos
- ✅ Tooltip shows note filename
- ✅ Reconciliation detects removed brackets

**Then RTF integration is working!** 🎉

---

## 📈 What's Next

### **Phase 3: Visual Indicators in RTF Editor** (Optional)
- Green highlight over `[completed task]` in note
- Tooltip showing completion date
- Click bracket → Jump to todo in panel

### **Phase 4: Additional Patterns** (Optional)
- `TODO: task` keyword syntax
- `- [ ] task` checkbox syntax
- Confidence scoring for patterns

### **Phase 5: Advanced Features** (Optional)
- Fuzzy text matching (handle slight edits)
- Navigate from todo to note line
- RTF file modification (add ✓ when completed)

**For now, test the core bracket feature and see how it feels!**

---

**Happy testing!** 🚀

Try adding brackets to your existing notes and watch the magic happen! ✨

