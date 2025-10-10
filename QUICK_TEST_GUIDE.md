# 🧪 QUICK TEST GUIDE - TODO UI REFRESH FIX

**2-Minute Test to Verify the Fix**

---

## ✅ **FASTEST TEST (30 seconds)**

1. **Open NoteNest** (already running)
2. **Press Ctrl+B** to open Todo Manager
3. **Right-click "Projects" folder** in note tree → "Add to Todo Categories"
4. **Create note** in Projects folder (File → New or Ctrl+N)
5. **Type:** `[test task]`
6. **Press Ctrl+S** to save
7. **LOOK AT TODO MANAGER** (don't close it!)

### ✅ **SUCCESS:**
- Todo appears under "Projects" within 2 seconds
- You see: `☐ test task` in the tree

### ❌ **FAILURE:**
- Todo doesn't appear
- You only see the Projects folder with (0) count

---

## 📋 **IF IT WORKS (Next Tests)**

### Test 2: Multiple Todos
```
[first task]
[second task]
[third task]
```
Save → All 3 appear instantly

### Test 3: Different Category
1. Add "Founders Ridge" to todos
2. Create note in Founders Ridge folder
3. Type `[ridge task]`
4. Save → Appears under Founders Ridge

---

## 🔍 **IF IT DOESN'T WORK**

### Check Logs:
Open: `%LocalAppData%\NoteNest\Logs\notenest-YYYYMMDD.log`

Search for:
```
[TodoSync] ✅ Created todo from note
[CategoryTree] Loading X todos for category
```

**If you see:**
- `Created todo from note` but NO `Loading X todos` → Different issue
- Neither message → RTF parser not running

---

## 🎯 **EXPECTED LOG SEQUENCE**

```
[TodoSync] Processing note: Test.rtf
[BracketParser] Extracted 1 todo candidates from 1 lines
[TodoSync] Found 1 todo candidates in Test.rtf
[TodoSync] Note is in category: 64daff0e-eb7d-43e3-b231-56b32ec1b8f4
[TodoSync] ✅ Created todo from note: "test task" [auto-categorized: 64daff0e-...] - UI will auto-refresh
[TodoStore] ✅ Todo saved to database: test task
```

---

**That's it!** If the todo appears within 2 seconds of saving, the fix is working! 🎉

