# 🧪 TEST PERSISTENCE - QUICK VERIFICATION

**Goal:** Verify todos appear on first load AND persist across restarts

---

## ✅ **PRIMARY TEST (60 seconds)**

### **Step 1: Verify Database Has Todos**
You already confirmed this - the database has **8 active todos**:
- ✅ test task (Projects)
- ✅ test task note test (Founders Ridge)
- ✅ RFI 54 Unit... (OP III)
- ✅ 5 more todos

### **Step 2: Close App Completely**
1. **Close NoteNest** (click X or Alt+F4)
2. **Verify it's closed** (check Task Manager if needed)

### **Step 3: Reopen App**
1. **Launch NoteNest** (from Start menu or desktop)
2. **Wait for app to fully load** (~2 seconds)

### **Step 4: Open Todo Manager (CRITICAL MOMENT)**
1. **Press Ctrl+B** OR click Todo Manager icon
2. **WATCH THE PANEL LOAD** (don't blink!)

---

## 🎯 **EXPECTED RESULTS**

### ✅ **SUCCESS:**
- Todo panel opens
- You see categories: Projects, Founders Ridge, etc.
- **TODOS ARE VISIBLE** under their categories:
  - Projects (1)  ← Expandable, shows "test task"
  - Founders Ridge (2)  ← Shows "test task note test"
  - OP III (1)  ← Shows "RFI 54..."
- **NO FLICKER** (smooth load)
- **NO DELAY** (appears within 200ms)

### ❌ **FAILURE:**
- Categories appear but show (0) counts
- No todos visible
- Need to close/reopen panel to see todos

---

## 📊 **WHAT TO CHECK IN LOGS**

Open: `%LocalAppData%\NoteNest\Logs\notenest-20251010.log`

Search for this sequence (should appear when you open panel):
```
[TodoStore] Starting lazy initialization...
[TodoStore] Initializing from database...
[TodoStore] Loaded 8 active todos from database
[CategoryTree] LoadCategoriesAsync started
[CategoryTree] Loading 1 todos for category: Projects
[CategoryTree] Loading 2 todos for category: Founders Ridge
```

---

## 🔍 **IF IT WORKS**

You should see todos **organized in a tree structure:**

```
📁 Projects (1)
   └─ ☐ test task

📁 Founders Ridge (2)
   ├─ ☐ test task
   └─ ☐ test task note test

📁 25-117 - OP III (1)
   └─ ☐ RFI 54 Unit Patio Privacy screens

... etc
```

**If you see this, the fix is 100% successful!** ✅

---

## 🧪 **BONUS TEST: RTF Auto-Categorization**

While the app is open:

1. **Create new note** in Projects folder
2. **Type:** `[persistence test task]`
3. **Save** (Ctrl+S)
4. **Wait 2 seconds**
5. **Check Todo Manager:** Should see "persistence test task" under Projects
6. **Close app, reopen, check again:** Task should still be there

**This verifies both instant creation AND persistence!**

---

## 💡 **TROUBLESHOOTING**

### If Todos Don't Appear:
1. Check logs for errors during initialization
2. Verify database file exists: `%LocalAppData%\NoteNest\.plugins\NoteNest.TodoPlugin\todos.db`
3. Check if "Starting lazy initialization" appears in logs
4. Report back with log excerpt

### If Performance is Slow (> 500ms):
1. Check database file size
2. Check if SSD vs HDD
3. Report timing from logs

---

**Ready to test!** The app is running now. Just close it, reopen it, and click Todo Manager! 🚀
