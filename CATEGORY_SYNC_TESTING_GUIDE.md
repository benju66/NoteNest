# 🧪 Category Sync - Manual Testing Guide

**Date:** October 10, 2025  
**Implementation Status:** ✅ COMPLETE  
**Build Status:** ✅ SUCCESS

---

## 🎯 TESTING OBJECTIVES

Verify that:
1. ✅ Categories can be added from note tree to todo panel
2. ✅ RTF-extracted todos are auto-categorized by note location
3. ✅ Category changes in tree sync to todo panel automatically
4. ✅ Performance is acceptable (no lag, no flickering)
5. ✅ Edge cases handled gracefully

---

## 🧪 TEST SUITE

### **TEST 1: Context Menu - Add Category to Todos**

**Steps:**
```
1. Launch app: .\Launch-NoteNest.bat
2. Wait for tree to load
3. Right-click any category in note tree (e.g., "Personal")
4. Look for context menu item: "Add to Todo Categories"
5. Click "Add to Todo Categories"
6. Press Ctrl+B to open Todo panel
7. Check if "Personal" appears in todo categories section
```

**Expected Results:**
- ✅ Context menu shows "Add to Todo Categories" with checkmark icon
- ✅ Click shows success message: "'Personal' added to todo categories!"
- ✅ Todo panel shows category "Personal"
- ✅ No errors in logs

**Log Verification:**
```
[CategoryOps] Adding category to todos: Personal
✅ Category added to todos: Personal
[CategoryStore] Added category: Personal
```

---

### **TEST 2: Prevent Duplicate Categories**

**Steps:**
```
1. Right-click "Personal" category again
2. Click "Add to Todo Categories"
```

**Expected Results:**
- ✅ Message shown: "'Personal' is already in todo categories."
- ✅ No duplicate category added
- ✅ Todo panel still shows only one "Personal" entry

**Log Verification:**
```
[CategoryOps] Category already in todos: Personal
```

---

### **TEST 3: RTF Auto-Categorization**

**Steps:**
```
1. Create new category in tree: "TestProject"
2. Right-click "TestProject" → "Add to Todo Categories"
3. Create new note inside "TestProject" folder
4. Open the note
5. Type in note content:
   "Meeting notes
   [call John]
   [send report]
   [review budget]"
6. Save note (Ctrl+S)
7. Wait 1-2 seconds (debounce delay)
8. Press Ctrl+B to open Todo panel
9. Check "TestProject" category in todos
```

**Expected Results:**
- ✅ Three todos appear under "TestProject" category:
  - "call John"
  - "send report"
  - "review budget"
- ✅ Each todo shows source note link
- ✅ Todos marked as from "note" source type

**Log Verification:**
```
[TodoSync] Processing note: <filename>.rtf
[TodoSync] Found 3 todo candidates in <filename>.rtf
[TodoSync] Note is in category: <guid> - todos will be auto-categorized
[TodoSync] ✅ Created todo from note: "call John" [auto-categorized: <guid>]
[TodoSync] ✅ Created todo from note: "send report" [auto-categorized: <guid>]
[TodoSync] ✅ Created todo from note: "review budget" [auto-categorized: <guid>]
[TodoSync] Reconciliation complete: 3 new, 0 orphaned, 0 updated
```

---

### **TEST 4: Category Rename Synchronization**

**Steps:**
```
1. Ensure "TestProject" is in todo categories
2. Create at least one todo under "TestProject"
3. In main tree, right-click "TestProject" → Rename
4. Rename to "ProjectAlpha"
5. Check Todo panel
```

**Expected Results:**
- ✅ Todo panel shows "ProjectAlpha" (updated automatically)
- ✅ Todos still under the category (same GUID)
- ✅ No manual refresh needed
- ✅ No UI flickering

**Log Verification:**
```
[MainShell] Refreshed todo categories after tree change
[CategoryStore] Refreshing categories from tree...
[CategorySync] Cache invalidated
[CategorySync] Loaded X categories from tree (cached for 5 min)
[CategoryStore] Refreshed X categories from tree
```

---

### **TEST 5: Category Deletion & Cleanup**

**Steps:**
```
1. Create temporary category "TempTest"
2. Add "TempTest" to todo categories
3. Create 2 manual todos under "TempTest"
4. Delete "TempTest" category from main tree
5. Restart application
6. Check Todo panel for those 2 todos
```

**Expected Results:**
- ✅ On restart, cleanup service runs automatically
- ✅ 2 todos moved to "Uncategorized" (CategoryId = null)
- ✅ Todos still exist (not deleted)
- ✅ No errors

**Log Verification:**
```
[TodoPlugin] CategoryStore initialized from tree
[CategoryCleanup] Found 1 orphaned categories out of X total
[CategoryCleanup] Found 2 todos in orphaned category <guid>
[CategoryCleanup] ✅ Cleanup complete: 2 todos moved to uncategorized from 1 orphaned categories
```

---

### **TEST 6: Cache Performance**

**Steps:**
```
1. Launch app and initialize TodoPlugin
2. Right-click category → "Add to Todo Categories" (triggers query)
3. Wait 2 seconds
4. Right-click another category → "Add to Todo Categories" (should hit cache)
5. Check logs for cache hit message
6. Wait 6 minutes (cache expiry)
7. Add another category (should miss cache, query database)
```

**Expected Results:**
- ✅ First query logs: "querying tree database..."
- ✅ Second query logs: "Returning cached categories (age: 2.Xs)"
- ✅ After 6 minutes: "Cache expired or empty, querying tree database..."
- ✅ No noticeable lag on any operation

**Log Verification:**
```
[CategorySync] Cache expired or empty, querying tree database...
[CategorySync] Loaded 15 categories from tree (cached for 5 min)
...
[CategorySync] Returning cached categories (age: 2.3s)
...
[CategorySync] Returning cached categories (age: 4.8s)
...
(After 6 min)
[CategorySync] Cache expired or empty, querying tree database...
```

---

### **TEST 7: Large Tree Performance**

**Prerequisites:** Create 50+ categories in tree (or use existing large tree)

**Steps:**
```
1. Launch app
2. Monitor startup time
3. Add multiple categories to todos
4. Monitor response time
5. Create todos in various categories
6. Monitor UI responsiveness
```

**Expected Results:**
- ✅ Startup completes in <5 seconds
- ✅ Category addition responds in <100ms
- ✅ No UI lag or freezing
- ✅ Cache provides instant responses after first query

**Performance Targets:**
- First category query: <200ms
- Cached queries: <5ms
- Context menu response: <100ms
- Auto-categorization overhead: <10ms

---

### **TEST 8: Uncategorized Todos**

**Steps:**
```
1. Create note at root level (not in any folder)
2. Add "[root level todo]" to note
3. Save note
4. Check Todo panel
```

**Expected Results:**
- ✅ Todo created successfully
- ✅ Todo has no category (CategoryId = null)
- ✅ Todo appears in "All" or "Today" smart list
- ✅ No errors logged

**Log Verification:**
```
[TodoSync] Note has no parent category - todos will be uncategorized
[TodoSync] ✅ Created todo from note: "root level todo" [uncategorized]
```

---

### **TEST 9: Error Handling - TodoPlugin Not Loaded**

**Steps:**
```
1. Temporarily comment out TodoPlugin registration in PluginSystemConfiguration
2. Build and run
3. Right-click category → "Add to Todo Categories"
```

**Expected Results:**
- ✅ Error message: "Todo plugin is not loaded or initialized."
- ✅ App doesn't crash
- ✅ Main tree functionality unaffected

---

### **TEST 10: Concurrent Operations**

**Steps:**
```
1. Open Todo panel
2. Rapidly add 5 categories via context menu (quick succession)
3. Immediately rename one of the categories in main tree
4. Add a few todos to different categories
5. Save a note with bracket todos
```

**Expected Results:**
- ✅ All operations complete successfully
- ✅ No race conditions
- ✅ Cache invalidation works correctly
- ✅ UI updates smoothly
- ✅ No deadlocks

---

## 📊 PERFORMANCE BENCHMARKS

### **Metrics to Capture:**

**Startup:**
- [ ] CategoryStore initialization time: ______ ms (Target: <100ms)
- [ ] Category count loaded: ______
- [ ] Orphaned category cleanup time: ______ ms

**Runtime:**
- [ ] First category query: ______ ms (Target: <200ms)
- [ ] Cached query: ______ ms (Target: <5ms)
- [ ] Context menu response: ______ ms (Target: <100ms)
- [ ] Category refresh: ______ ms (Target: <150ms)

**Auto-Categorization:**
- [ ] RTF extraction time: ______ ms
- [ ] Category lookup overhead: ______ ms (Target: <10ms)
- [ ] Total sync time: ______ ms (Target: <500ms)

---

## ✅ ACCEPTANCE CRITERIA

### **Must Pass:**
- [ ] Context menu item appears on right-click
- [ ] Adding category works (shows in todo panel)
- [ ] Duplicate detection works (shows message)
- [ ] RTF todos auto-categorize based on note location
- [ ] Category rename reflects in todo panel
- [ ] Uncategorized todos handled (root-level notes)
- [ ] No crashes or unhandled exceptions
- [ ] Build succeeds

### **Should Pass:**
- [ ] Cache improves performance (second query faster)
- [ ] Event-driven refresh works (no manual refresh needed)
- [ ] Large trees (50+ categories) load quickly
- [ ] UI doesn't flicker during refresh
- [ ] Orphaned category cleanup works on startup

### **Nice to Have:**
- [ ] Cache hit logs appear in debug output
- [ ] Performance metrics within targets
- [ ] All edge cases handled gracefully

---

## 🐛 KNOWN ISSUES TO WATCH FOR

### **Potential Issue #1: Context Menu Binding**
**Symptom:** Menu item doesn't appear or command doesn't execute  
**Check:** 
- Verify `PlacementTarget.Tag` binding in XAML
- Check CategoryOperations has command property
- Ensure MainShellViewModel is in Tag

**Fix:** Already implemented using proven pattern from existing menu items

---

### **Potential Issue #2: Cache Not Invalidating**
**Symptom:** Renamed category doesn't update in todos  
**Check:**
- Verify `RefreshTodoCategoriesAsync()` called in event handlers
- Check cache invalidation in RefreshAsync()
- Look for exception in logs

**Fix:** Event wiring already implemented

---

### **Potential Issue #3: Auto-Categorization Fails**
**Symptom:** RTF todos created but not categorized  
**Check:**
- Verify TodoSyncService has ITreeDatabaseRepository
- Check note has parent category (not at root)
- Look for tree query errors in logs

**Fix:** Already handled with try-catch and logging

---

## 📝 TEST RESULTS TEMPLATE

```
===========================================
CATEGORY SYNC - TEST RESULTS
Date: ___________
Tester: ___________
Build: Debug/Release
===========================================

TEST 1: Context Menu Integration
Status: PASS / FAIL / PARTIAL
Notes: ________________________________

TEST 2: Prevent Duplicates
Status: PASS / FAIL / PARTIAL
Notes: ________________________________

TEST 3: RTF Auto-Categorization
Status: PASS / FAIL / PARTIAL
Notes: ________________________________

TEST 4: Category Rename Sync
Status: PASS / FAIL / PARTIAL
Notes: ________________________________

TEST 5: Category Deletion Cleanup
Status: PASS / FAIL / PARTIAL
Notes: ________________________________

TEST 6: Cache Performance
Status: PASS / FAIL / PARTIAL
Cache hit rate: ________%
Notes: ________________________________

TEST 7: Large Tree Performance
Category count: ________
Load time: ________ ms
Notes: ________________________________

TEST 8: Uncategorized Todos
Status: PASS / FAIL / PARTIAL
Notes: ________________________________

TEST 9: Error Handling
Status: PASS / FAIL / PARTIAL
Notes: ________________________________

TEST 10: Concurrent Operations
Status: PASS / FAIL / PARTIAL
Notes: ________________________________

===========================================
OVERALL: PASS / FAIL / NEEDS WORK
Confidence: ____%
Issues Found: _________________________
===========================================
```

---

## 🎯 NEXT STEPS AFTER TESTING

### **If All Tests Pass (Expected):**
1. ✅ Mark feature as production-ready
2. ✅ Update main documentation
3. ✅ Create user guide
4. ✅ Consider hierarchical category display (future enhancement)

### **If Issues Found:**
1. Document specific failure
2. Check logs for error details
3. Add debug logging if needed
4. Fix and re-test
5. Update this guide with findings

---

## 🚀 READY TO TEST

**Command to Launch:**
```bash
.\Launch-NoteNest.bat
```

**With Console Logging:**
```bash
.\Launch-With-Console.bat
```

**What to Look For:**
- `[CategorySync]` - Cache and sync operations
- `[CategoryStore]` - Store initialization and refresh
- `[CategoryOps]` - Context menu command execution
- `[TodoSync]` - Auto-categorization logging
- `[CategoryCleanup]` - Orphaned category handling

---

**Start testing now to validate the implementation!** ✅

