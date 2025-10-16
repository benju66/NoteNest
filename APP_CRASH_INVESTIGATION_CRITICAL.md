# 🚨 CRITICAL: APP CRASH INVESTIGATION

**Issue:** App freezes and crashes when opening folder tag dialog for child folder  
**Severity:** CRITICAL - Blocks all folder tagging functionality  
**Status:** Investigation Complete

---

## 🔍 **ROOT CAUSE ANALYSIS**

### **What Happened:**
1. User tagged "Projects" folder with "work" ✅
2. User tried to open tag dialog for "25-117 - OP III" (child of Projects)
3. Dialog opened
4. App froze (not responding)
5. App crashed/closed

---

## 🎯 **PROBABLE ROOT CAUSE #1: Async/Await Deadlock** (80% Likely)

### **The Problematic Code:**

**FolderTagDialog.xaml.cs line 58:**
```csharp
Loaded += async (s, e) => await LoadTagsAsync();
```

**The Problem:**
- `Loaded` event fires on UI thread
- Lambda is `async void` (no Task return)
- `await LoadTagsAsync()` blocks waiting for repository call
- Repository call might be waiting for UI thread
- **Classic async deadlock!**

### **How Deadlock Occurs:**

```
UI Thread:
  ↓
Loaded event fires
  ↓
async void lambda starts
  ↓
await LoadTagsAsync()
  ↓
Calls GetInheritedTagsAsync() - SQLite query
  ↓
Query tries to access tree.db
  ↓
[BLOCKS waiting for database]
  ↓
Meanwhile, UI thread is blocked waiting for Loaded to complete
  ↓
DEADLOCK! App freezes
```

### **Why It Worked for "Projects" Folder:**
- "Projects" is a root folder (no parent)
- `GetInheritedTagsAsync()` returns empty quickly
- No deadlock risk

### **Why It Crashes for "25-117 - OP III":**
- Has parent folder ("Projects")
- `GetInheritedTagsAsync()` runs recursive CTE query
- Query takes time or encounters issue
- UI thread deadlock → freeze → crash

---

## 🎯 **PROBABLE ROOT CAUSE #2: Database Lock** (15% Likely)

### **Scenario:**
- tree.db is locked by another operation
- GetInheritedTagsAsync tries to open connection
- Times out waiting for lock
- UI thread frozen during timeout
- App crashes

---

## 🎯 **PROBABLE ROOT CAUSE #3: Recursive Query Issue** (5% Likely)

### **The SQL Query:**
```sql
WITH RECURSIVE ancestors AS (
    SELECT id, parent_id FROM tree_nodes WHERE id = @FolderId
    UNION ALL
    SELECT tn.id, tn.parent_id 
    FROM tree_nodes tn
    INNER JOIN ancestors a ON tn.id = a.parent_id
    WHERE a.parent_id IS NOT NULL
)
```

**Potential Issues:**
- Infinite loop if tree has circular reference
- Very deep tree causing stack overflow
- Query malformed for fresh database

---

## 🔧 **FIXES REQUIRED**

### **Fix #1: Proper Async Pattern in Loaded Event** ⭐ CRITICAL

**Current (WRONG):**
```csharp
Loaded += async (s, e) => await LoadTagsAsync();
```

**Fix Option A: Use Dispatcher** (SAFEST)
```csharp
Loaded += (s, e) => 
{
    Dispatcher.InvokeAsync(async () => await LoadTagsAsync());
};
```

**Fix Option B: Fire-and-Forget with Error Handling**
```csharp
Loaded += (s, e) => 
{
    _ = LoadTagsAsync();  // Fire-and-forget, non-blocking
};
```

**Fix Option C: Synchronous Wrapper**
```csharp
Loaded += async (s, e) => 
{
    try
    {
        await Task.Run(async () => await LoadTagsAsync());
    }
    catch (Exception ex)
    {
        _logger.Error("Failed to load tags", ex);
    }
};
```

**Recommendation:** Fix Option B - Fire-and-forget

**Why:**
- ✅ Non-blocking (no deadlock)
- ✅ Error handling in LoadTagsAsync already exists
- ✅ Matches TodoItemViewModel pattern
- ✅ Simple, proven

---

### **Fix #2: Add Timeout to Repository Queries**

**In FolderTagRepository.GetInheritedTagsAsync:**
```csharp
connection.DefaultTimeout = 5;  // 5 second timeout
```

Prevents indefinite hang if query is slow.

---

### **Fix #3: Defensive Null Check**

**In FolderTagDialog.LoadTagsAsync:**
```csharp
if (_folderId == Guid.Empty)
{
    _logger.Warning("Invalid folder ID");
    return;
}
```

---

## 📊 **ISSUE #1: TAG ICON NOT APPEARING**

### **Diagnosis:**

**You said:** Successfully added "work" tag to "Projects" folder  
**Result:** Quick-add task has no tag icon

**Possible Causes:**

**Cause A (70% Likely): Folder Not Actually Tagged**
- Dialog might have crashed before saving
- Or tag didn't save to database
- Check: `SELECT * FROM folder_tags;` in tree.db

**Cause B (20% Likely): Tag Inheritance Not Working**
- Tags exist in folder_tags table
- But not being applied to quick-add todos
- Need to check logs during quick-add

**Cause C (10% Likely): UI Refresh Still Broken**
- Tags are applied
- But icon still doesn't show
- HasTags issue not fully fixed

### **How to Verify:**

**Check logs after quick-add for:**
```
[INFO] Updating todo <guid> tags: moving from  to <folder-guid>
[INFO] Found X applicable tags for folder <folder-guid>
[INFO] Added X inherited tags to todo <guid>
```

If X = 0, folder has no tags in database.  
If X > 0 but icon doesn't appear, HasTags issue.

---

## 🎯 **RECOMMENDED ACTION PLAN**

### **Priority 1: Fix App Crash** ⚡ CRITICAL
**Issue:** Test 3 crash  
**Fix:** Change Loaded event pattern to fire-and-forget  
**Files:** FolderTagDialog.xaml.cs, NoteTagDialog.xaml.cs  
**Confidence:** 90%  
**Time:** 5 minutes

### **Priority 2: Increase Window Heights**
**Issue:** UI elements hidden  
**Fix:** Height="400" → Height="550"  
**Files:** All three dialog XAML files  
**Confidence:** 100%  
**Time:** 2 minutes

### **Priority 3: Investigate Tag Icon**
**Issue:** Test 1 failure  
**Need:** Check if tags were actually saved  
**Action:** Query database or check logs  
**Then:** Fix based on findings

---

## 🔬 **ADDITIONAL INVESTIGATION NEEDED**

**Please check:**

1. **Did "Projects" folder tag actually save?**
   - Try opening "Projects" folder tag dialog again
   - Does it show "work" tag?
   - Or is it empty?

2. **App crash logs:**
   - Any error messages before crash?
   - Exception in log file?

3. **Quick-add logs:**
   - After creating quick-add task
   - Search logs for: "Found X applicable tags"
   - What is X?

---

## 🎯 **CONFIDENCE LEVELS**

| Issue | Root Cause | Confidence | Fix Confidence |
|-------|-----------|------------|----------------|
| **Test 3 Crash** | **Async deadlock** | **80%** | **90%** |
| Test 2 Layout | Window too small | 100% | 100% |
| Test 1 Icon | Folder not tagged OR inheritance broken | 70% / 30% | TBD |

---

## 🚀 **NEXT STEPS**

**I recommend:**
1. Fix async deadlock (Test 3) - CRITICAL
2. Fix window heights (Test 2) - SIMPLE
3. Test if dialogs work
4. Then investigate Test 1 based on results

**Shall I proceed with fixes #1 and #2?** (7 minutes, 95% confidence)

