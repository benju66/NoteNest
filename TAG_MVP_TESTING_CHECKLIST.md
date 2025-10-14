# 🧪 TAG MVP - COMPREHENSIVE TESTING CHECKLIST

**Status:** Ready for Testing  
**Build:** ✅ 0 Errors  
**DI Registration:** ✅ Complete  

---

## 🎯 **CRITICAL PATH TESTS (Must Pass)**

These are the core user scenarios that MUST work for the Tag MVP to be considered successful.

### **CP-1: App Startup**
- [ ] App starts without DI resolution errors
- [ ] No database migration errors in logs
- [ ] Todo panel loads without crashes
- [ ] Existing todos still display correctly

**Expected:** Clean startup, no errors, existing functionality untouched

---

### **CP-2: Auto-Tag Generation (Project Folders)**
**Test Scenario:** Create todo in project folder

**Setup:**
1. Navigate to a project category like "Projects/25-117 - OP III/Daily Notes"
2. Create a new todo via quick-add: "Review documents"

**Verify:**
- [ ] 🏷️ tag icon appears next to the todo item
- [ ] Hover over icon → Tooltip shows:
  ```
  Auto: 25-117-OP-III, 25-117
  ```
- [ ] Tags are NOT editable/removable (they're auto-tags)
- [ ] Check database: `SELECT * FROM todo_tags WHERE is_auto = 1;` → Should show 2 tags

**Expected:** Two auto-tags generated from project path

---

### **CP-3: Manual Tag Addition**
**Test Scenario:** Add manual tag to existing todo

**Setup:**
1. Right-click on the todo from CP-2
2. Context Menu → Tags → Add Tag...
3. Enter "urgent" in dialog
4. Click OK

**Verify:**
- [ ] Dialog closes without error
- [ ] 🏷️ icon still visible (was already there)
- [ ] Hover over icon → Tooltip now shows:
  ```
  Auto: 25-117-OP-III, 25-117
  Manual: urgent
  ```
- [ ] Check database: `SELECT * FROM todo_tags WHERE is_auto = 0;` → Should show "urgent"

**Expected:** Manual tag added, auto-tags preserved

---

### **CP-4: Auto-Tag Updates on Move**
**Test Scenario:** Move todo to different category, verify auto-tags update

**Setup:**
1. Use the todo from CP-3 (has 2 auto-tags + 1 manual tag)
2. Move todo to different project: "Projects/24-099 - Building Renovations"

**Verify:**
- [ ] Hover over 🏷️ icon → Tooltip shows:
  ```
  Auto: 24-099-Building-Renovations, 24-099
  Manual: urgent
  ```
- [ ] Old auto-tags (25-117-OP-III, 25-117) are GONE
- [ ] New auto-tags (24-099-Building-Renovations, 24-099) are present
- [ ] Manual tag "urgent" is PRESERVED
- [ ] Check database: `SELECT * FROM todo_tags WHERE todo_id = ?;` → Verify correct tags

**Expected:** Auto-tags update, manual tags preserved

---

### **CP-5: Search Integration (FTS5)**
**Test Scenario:** Verify tags are searchable

**Setup:**
1. Use the todo from CP-4 (tagged with "24-099-Building-Renovations")
2. Open search
3. Search for: "Building Renovations"

**Verify:**
- [ ] Todo appears in search results
- [ ] Search for: "24-099" → Todo appears
- [ ] Search for: "urgent" → Todo appears (manual tag)
- [ ] Search for: "Renovations" (partial) → Todo appears (FTS5 tokenization)

**Expected:** All tag searches return the todo

---

## 📋 **FUNCTIONAL TESTS (Should Pass)**

These tests verify all implemented features work correctly.

### **F-1: Tag Removal**
**Test Scenario:** Remove a manual tag

**Setup:**
1. Right-click todo with manual tag "urgent"
2. Context Menu → Tags → Remove Tag...
3. Select "urgent" from list
4. Click Remove

**Verify:**
- [ ] Dialog closes without error
- [ ] Hover tooltip → "urgent" no longer listed
- [ ] Auto-tags still present
- [ ] If no manual tags remain, tooltip shows only auto-tags
- [ ] Check database: "urgent" tag deleted

**Expected:** Manual tag removed, auto-tags untouched

---

### **F-2: Auto-Tag Generation (Non-Project Folders)**
**Test Scenario:** Create todo in top-level category

**Setup:**
1. Navigate to top-level category like "Personal" or "Work"
2. Create todo: "Call dentist"

**Verify:**
- [ ] 🏷️ icon appears
- [ ] Hover tooltip shows:
  ```
  Auto: Personal
  ```
  (or "Work", depending on category)
- [ ] Single tag generated from top-level category name
- [ ] Tag is lowercase and hyphenated (e.g., "personal" not "Personal")

**Expected:** Single auto-tag from category name

---

### **F-3: No Auto-Tags (Uncategorized)**
**Test Scenario:** Create todo without category

**Setup:**
1. Move todo to "Uncategorized" (null category)
2. Or create todo in uncategorized section

**Verify:**
- [ ] 🏷️ icon does NOT appear (if no manual tags)
- [ ] Hover over todo → No tag tooltip
- [ ] Check database: No auto-tags for this todo
- [ ] Manual tags can still be added

**Expected:** No auto-tags in uncategorized

---

### **F-4: Tag Icon Visibility**
**Test Scenario:** Verify icon appears only when tags exist

**Setup:**
1. Todo with tags → 🏷️ visible
2. Todo without tags → No 🏷️
3. Add tag → 🏷️ appears
4. Remove all tags → 🏷️ disappears

**Verify:**
- [ ] Icon visibility matches tag presence
- [ ] No visual glitches or flickering
- [ ] Icon positioned correctly (after favorite star)

**Expected:** Icon shows/hides based on tag presence

---

### **F-5: Duplicate Tag Prevention**
**Test Scenario:** Try to add duplicate tag

**Setup:**
1. Todo already has manual tag "urgent"
2. Try to add "urgent" again via context menu

**Verify:**
- [ ] Either: Dialog prevents duplicate (validation)
- [ ] Or: Command fails gracefully with log message
- [ ] No duplicate tags in database
- [ ] No crash or error visible to user

**Expected:** Duplicate tags prevented gracefully

---

### **F-6: Tag Persistence Across App Restarts**
**Test Scenario:** Verify tags survive app restart

**Setup:**
1. Create todo with auto-tags and manual tags
2. Note the tags
3. Close app
4. Restart app

**Verify:**
- [ ] Todo still displays 🏷️ icon
- [ ] Hover tooltip shows same tags
- [ ] Auto-tags preserved
- [ ] Manual tags preserved
- [ ] No duplicate tags created

**Expected:** Tags persist across restarts

---

### **F-7: Multiple Manual Tags**
**Test Scenario:** Add multiple manual tags to one todo

**Setup:**
1. Add tags: "urgent", "review", "client-meeting"
2. One at a time via context menu

**Verify:**
- [ ] All tags appear in tooltip
- [ ] Tooltip format:
  ```
  Auto: 25-117-OP-III, 25-117
  Manual: urgent, review, client-meeting
  ```
- [ ] Can remove individual manual tags
- [ ] Auto-tags unaffected

**Expected:** Multiple manual tags supported

---

### **F-8: Tag Generation from Complex Paths**
**Test Scenario:** Verify tag generation from nested paths

**Test Cases:**
- [ ] "Projects/25-117 - OP III/Subfolder/Deep/Nested" → Tags: "25-117-OP-III", "25-117"
- [ ] "Personal Projects/24-099/Daily" → Tags: "24-099"
- [ ] "Archive/2024/Projects/25-117 - OP III" → Tags: "25-117-OP-III", "25-117"

**Expected:** Tag generation finds project patterns regardless of depth

---

## 🔍 **EDGE CASE TESTS (Nice to Pass)**

These tests verify the system handles unusual scenarios gracefully.

### **E-1: Empty Tag Name**
**Setup:**
1. Add Tag dialog → Leave input blank
2. Click OK

**Verify:**
- [ ] Either: OK button disabled when empty
- [ ] Or: Validation error shown
- [ ] Dialog doesn't close
- [ ] No empty tag in database

**Expected:** Empty tags prevented

---

### **E-2: Special Characters in Tags**
**Setup:**
1. Try to add tag with special chars: "urgent!!!", "review@work", "meeting#1"

**Verify:**
- [ ] Tags are normalized/sanitized (special chars removed or accepted)
- [ ] No crashes
- [ ] Tags searchable
- [ ] Check TagGeneratorService normalization

**Expected:** Special chars handled gracefully

---

### **E-3: Very Long Tag Names**
**Setup:**
1. Add tag with 100+ characters

**Verify:**
- [ ] Either: Input limited to reasonable length
- [ ] Or: Tag accepted and stored
- [ ] Tooltip displays correctly (truncated if needed)
- [ ] No UI layout breaks

**Expected:** Long tags don't break UI

---

### **E-4: Rapid Tag Operations**
**Setup:**
1. Quickly add/remove tags multiple times
2. Stress test the system

**Verify:**
- [ ] No crashes
- [ ] Final state is consistent
- [ ] No orphaned tags in database
- [ ] UI updates correctly

**Expected:** System handles rapid operations

---

### **E-5: Move to Same Category**
**Setup:**
1. Move todo to its current category (no actual move)

**Verify:**
- [ ] Auto-tags unchanged
- [ ] Manual tags unchanged
- [ ] No duplicate tags created
- [ ] No unnecessary database operations (check logs)

**Expected:** No-op move doesn't affect tags

---

### **E-6: Category with No Project Pattern**
**Setup:**
1. Create todo in "Random Folder/Subfolder/Deep"

**Verify:**
- [ ] Tag generated: "Random-Folder" (from top-level)
- [ ] No crash looking for project pattern
- [ ] Single tag from first segment

**Expected:** Non-project folders get single tag from top-level

---

## 🎨 **UI/UX VALIDATION**

Visual and interaction tests for user experience.

### **UI-1: Tooltip Formatting**
**Verify:**
- [ ] Auto-tags section labeled "Auto:"
- [ ] Manual tags section labeled "Manual:"
- [ ] Tags comma-separated within each section
- [ ] Multi-line if both sections present
- [ ] Readable font size and colors
- [ ] Tooltip appears on hover without delay
- [ ] Tooltip disappears when mouse leaves

**Expected:** Clean, readable tooltip

---

### **UI-2: Context Menu Layout**
**Verify:**
- [ ] "Tags" menu item visible in context menu
- [ ] "Add Tag..." with 🏷️ icon
- [ ] Separator before tag section
- [ ] "Current Tags:" header appears only when tags exist
- [ ] "Remove Tag..." appears only when tags exist
- [ ] Menu items aligned consistently
- [ ] Keyboard shortcuts work (if any)

**Expected:** Professional, discoverable UI

---

### **UI-3: Dialog Appearance**
**Verify:**
- [ ] Add Tag dialog centers on screen
- [ ] Dialog has clear title "Add Tag"
- [ ] Input field has focus on open
- [ ] OK/Cancel buttons styled correctly
- [ ] Enter key submits (OK is default)
- [ ] Escape key cancels
- [ ] Dialog modal (blocks main window)

**Expected:** Standard Windows dialog behavior

---

### **UI-4: Icon Positioning**
**Verify:**
- [ ] 🏷️ icon appears after favorite star
- [ ] Icon size matches other icons (⭐, flag)
- [ ] Icon alignment with other todo elements
- [ ] No overlap with text
- [ ] No visual artifacts

**Expected:** Icon integrates seamlessly

---

## 🔄 **INTEGRATION TESTS**

Tests verifying integration with existing systems.

### **I-1: CQRS Event Flow**
**Setup:**
1. Add tag via context menu
2. Monitor logs

**Verify:**
- [ ] AddTagCommand published
- [ ] AddTagHandler executes
- [ ] Event published (if applicable)
- [ ] TodoStore receives update (if wired)
- [ ] UI refreshes automatically

**Expected:** Clean event-driven architecture

---

### **I-2: Database Migration**
**Verify:**
- [ ] Check logs: "Migration V002" applied
- [ ] Check logs: "Migration V003" applied
- [ ] Check schema: `PRAGMA table_info(todo_tags);` shows `is_auto` column
- [ ] Check triggers: `SELECT * FROM sqlite_master WHERE type='trigger';` shows tag triggers
- [ ] No migration errors in logs

**Expected:** Migrations applied successfully

---

### **I-3: FTS5 Trigger Validation**
**Setup:**
1. Add tag "urgent" to todo
2. Check FTS5 table: `SELECT tags FROM todos_fts WHERE id = ?;`

**Verify:**
- [ ] `tags` column updated with new tag
- [ ] All tags present (auto + manual)
- [ ] Space-separated format
- [ ] Remove tag → FTS5 updated

**Expected:** FTS5 stays synchronized

---

### **I-4: Existing Todo Functionality**
**Verify:**
- [ ] Create todo (without tags) → Works
- [ ] Complete todo → Works
- [ ] Delete todo → Works
- [ ] Edit todo text → Works
- [ ] Change priority → Works
- [ ] Set due date → Works
- [ ] Toggle favorite → Works
- [ ] Move todo → Works

**Expected:** All existing features unaffected

---

### **I-5: Category Operations**
**Verify:**
- [ ] Rename category → Todos update auto-tags? (Future: maybe not in MVP)
- [ ] Delete category → Todos moved to uncategorized, auto-tags removed
- [ ] Create new category → Ready for todos with tags

**Expected:** Category operations don't break tagging

---

## 📊 **PERFORMANCE TESTS**

Basic performance validation (not formal benchmarks).

### **P-1: Tag Loading Performance**
**Setup:**
1. Todo list with 100+ todos
2. Some with tags, some without

**Verify:**
- [ ] List loads in < 2 seconds
- [ ] No noticeable lag when scrolling
- [ ] Tag tooltips appear instantly on hover
- [ ] No UI freezing

**Expected:** Acceptable performance with many todos

---

### **P-2: Search Performance**
**Setup:**
1. Database with 1000+ todos, many tagged
2. Search for common tag

**Verify:**
- [ ] Search completes in < 1 second
- [ ] Results accurate
- [ ] No timeout errors
- [ ] FTS5 index used (check query plan if possible)

**Expected:** Fast search even with many tags

---

### **P-3: Tag Generation Performance**
**Setup:**
1. Create 50 todos rapidly in project folder

**Verify:**
- [ ] No noticeable delay during creation
- [ ] All todos get correct auto-tags
- [ ] Database writes don't queue up
- [ ] No database lock errors

**Expected:** Tag generation doesn't slow todo creation

---

## 🐛 **ERROR HANDLING TESTS**

Verify graceful error handling.

### **EH-1: Database Errors**
**Simulate:** Temporarily corrupt database or lock file

**Verify:**
- [ ] App doesn't crash
- [ ] Error logged with context
- [ ] User sees friendly message (not stack trace)
- [ ] Graceful degradation (todos work without tags)

**Expected:** Graceful failure, not crash

---

### **EH-2: Missing Dependencies**
**Simulate:** Comment out DI registration (temporarily)

**Verify:**
- [ ] App fails fast at startup with clear error
- [ ] Error message indicates missing service
- [ ] Logs show DI resolution error

**Expected:** Clear error message for developers

---

### **EH-3: Null Handling**
**Test Cases:**
- [ ] Todo with null category → No crash, no auto-tags
- [ ] Empty tag input → Prevented or handled
- [ ] Malformed path → No crash, best-effort tagging

**Expected:** Null safety throughout

---

## 📝 **TESTING PRIORITIES**

### **Phase 1: CRITICAL PATH (Must complete first)**
Complete CP-1 through CP-5 before moving forward.  
**Time Estimate:** 15-20 minutes

### **Phase 2: FUNCTIONAL (Core features)**
Complete F-1 through F-8 to verify all features.  
**Time Estimate:** 30-40 minutes

### **Phase 3: EDGE CASES (Robustness)**
Complete E-1 through E-6 for production readiness.  
**Time Estimate:** 20-30 minutes

### **Phase 4: UI/UX (Polish)**
Complete UI-1 through UI-4 for user validation.  
**Time Estimate:** 15-20 minutes

### **Phase 5: INTEGRATION (System verification)**
Complete I-1 through I-5 for system health.  
**Time Estimate:** 20-30 minutes

### **Phase 6: PERFORMANCE & ERRORS (Optional)**
Complete P-1 through EH-3 for confidence.  
**Time Estimate:** 30-40 minutes

**Total Estimated Testing Time:** 2-3 hours for comprehensive validation

---

## ✅ **TESTING CHECKLIST SUMMARY**

**Critical Path:** 5 tests ⚠️ MUST PASS  
**Functional:** 8 tests ✅ Should pass  
**Edge Cases:** 6 tests 🔍 Nice to pass  
**UI/UX:** 4 tests 🎨 User validation  
**Integration:** 5 tests 🔄 System health  
**Performance:** 3 tests 📊 Optional  
**Error Handling:** 3 tests 🐛 Optional  

**Total:** 34 test scenarios

---

## 🎯 **SUCCESS CRITERIA**

The Tag MVP is considered successful if:
1. ✅ All 5 Critical Path tests pass
2. ✅ At least 7/8 Functional tests pass
3. ✅ At least 4/6 Edge Case tests pass
4. ✅ UI/UX is acceptable (subjective but no major issues)
5. ✅ No critical bugs discovered

---

## 📋 **TESTING TIPS**

1. **Use Fresh Database:** Test with clean database first, then with existing data
2. **Check Logs:** Keep log viewer open, watch for errors/warnings
3. **SQLite Browser:** Use DB Browser for SQLite to inspect database state
4. **Take Notes:** Document any issues with repro steps
5. **Test Systematically:** Follow the order (Critical Path → Functional → etc.)
6. **Don't Skip Restarts:** Some issues only appear after restart
7. **User Perspective:** Think like a user, not just testing checkboxes

---

## 🚀 **READY TO TEST!**

Start with **CP-1: App Startup** and work through the Critical Path tests.  
Document any failures or unexpected behavior.

**Good luck!** 🎉

