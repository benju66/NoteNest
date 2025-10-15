# 🧪 HYBRID FOLDER TAGGING - TESTING GUIDE

**Date:** October 15, 2025  
**Status:** Core Features Complete - Ready for Testing  
**Build:** ✅ 0 Errors

---

## 🎯 **WHAT'S BEEN IMPLEMENTED**

### ✅ **Foundation Layer** (100%)
- Database schema (`folder_tags` table in tree.db)
- Repository layer (`FolderTagRepository`)
- CQRS commands (`SetFolderTagCommand`, `RemoveFolderTagCommand`)
- Domain events (`FolderTaggedEvent`, `FolderUntaggedEvent`)
- Services (`TagInheritanceService`, `FolderTagSuggestionService`)

### ✅ **UI Layer** (90%)
- Context menus on TreeViews (main app + todo plugin)
- Folder Tag Dialog for managing tags
- Event handlers in TodoStore
- Integration with CreateTodo and MoveTodo handlers

### ⏳ **NOT Implemented** (Future Enhancements)
- Visual tag indicators on folders (can be added later)
- Tag suggestion popup (Phase 4 - optional)
- Bulk update for existing todos (removed by design)

---

## 🧪 **TEST SCENARIOS**

### **TEST 1: Set Tags on a Folder** ⭐ PRIMARY TEST

**Steps:**
1. Launch NoteNest
2. Navigate to a folder like "Projects/25-117 - OP III"
3. Right-click on the "25-117 - OP III" folder
4. Select "Set Folder Tags..." from context menu
5. Dialog should open showing:
   - Folder path: "Projects/25-117 - OP III"
   - Empty tags list (first time)
   - Input field to add tags
6. Type "25-117-OP-III" and click "Add Tag"
7. Tag should appear in the list
8. Type "25-117" and click "Add Tag"
9. Second tag should appear
10. Click "Save"

**Expected Result:**
- ✅ Dialog closes
- ✅ Success message appears
- ✅ Tags saved to database
- ✅ Log shows: "Successfully set 2 tags on folder..."

**To Verify in Database:**
```sql
-- Check folder_tags table
SELECT * FROM folder_tags;
-- Should show 2 rows with folder_id, tags, is_auto_suggested=0, inherit_to_children=1
```

---

### **TEST 2: Create New Todo in Tagged Folder** ⭐ PRIMARY TEST

**Prerequisites:** Complete TEST 1 first (folder must have tags)

**Steps:**
1. In the Todo plugin panel, right-click on the "25-117 - OP III" category
2. Click "Add Todo" or use quick-add
3. Enter todo text: "Review blueprints"
4. Save the todo
5. Right-click on the newly created todo
6. Select "Tags" > "Remove Tag..." to view tags

**Expected Result:**
- ✅ Todo has 2 auto-tags: "25-117-OP-III" and "25-117"
- ✅ Tags marked as "Auto"
- ✅ Tag icon visible on todo item
- ✅ Tooltip shows tags

**To Verify in Database:**
```sql
-- Check todo_tags table
SELECT * FROM todo_tags WHERE todo_id = '<your-todo-id>';
-- Should show 2 rows with is_auto=1
```

---

### **TEST 3: Move Todo Between Folders**

**Prerequisites:** Have tagged folder from TEST 1

**Steps:**
1. Create a todo in the tagged folder "25-117 - OP III"
2. Verify it has tags (from TEST 2)
3. Create another folder without tags (e.g., "Personal")
4. Drag the todo to the "Personal" folder (or use move command)

**Expected Result:**
- ✅ Old tags ("25-117-OP-III", "25-117") are REMOVED
- ✅ No new tags added (Personal folder has no tags)
- ✅ Manual tags (if any) are preserved

**To Verify:**
- Right-click todo > Tags > Remove Tag...
- Should show NO tags (or only manual tags)

---

### **TEST 4: Remove Folder Tags**

**Prerequisites:** Folder has tags from TEST 1

**Steps:**
1. Right-click on "25-117 - OP III" folder
2. Select "Remove Folder Tags"
3. Confirm dialog appears: "Existing items will keep their tags"
4. Click "Yes"

**Expected Result:**
- ✅ Success message
- ✅ Folder tags removed from database
- ✅ Existing todos KEEP their tags (not retroactively removed)
- ✅ New todos will NOT get tags

**To Verify:**
1. Check database: `SELECT * FROM folder_tags WHERE folder_id = '<folder-id>'` → Should return 0 rows
2. Create new todo → Should have NO tags
3. Check existing todo → Should still have tags

---

### **TEST 5: Tag Inheritance from Parent Folders**

**Steps:**
1. Create folder structure: "Projects" > "25-117 - OP III" > "Daily Notes"
2. Tag "25-117 - OP III" with "25-117-OP-III" and "25-117"
3. Ensure "Inherit to subfolders" is checked
4. Create todo in "Daily Notes" subfolder

**Expected Result:**
- ✅ Todo in "Daily Notes" inherits tags from parent "25-117 - OP III"
- ✅ Tags: "25-117-OP-III", "25-117"

---

### **TEST 6: Validation & Error Handling**

**Test 6a: Invalid Tag Names**
- Try adding tag with special chars: "test@tag#" → Should reject
- Try adding empty tag → Should reject
- Try adding tag > 50 chars → Should reject
- Try adding duplicate tag → Should show message

**Test 6b: Empty Folder Tags**
- Open dialog, remove all tags, click Save
- Should ask for confirmation
- Click "Yes" → Should remove all tags

**Test 6c: Missing Services**
- (Hard to test - requires service injection failure)

---

## 📊 **DATABASE VERIFICATION QUERIES**

### **Check Migration Applied:**
```sql
-- Connect to: %LocalAppData%\NoteNest\tree.db

-- 1. Check schema version
SELECT * FROM schema_version ORDER BY version;
-- Expected: Should show version 3 with description "Added folder_tags table..."

-- 2. Check folder_tags table exists
SELECT sql FROM sqlite_master WHERE name = 'folder_tags';
-- Expected: Should show CREATE TABLE statement

-- 3. Check indexes
PRAGMA index_list('folder_tags');
-- Expected: Should show 4 indexes

-- 4. Test insert
INSERT INTO folder_tags VALUES ('test-id', 'test-tag', 0, 1, strftime('%s', 'now'), 'user');
SELECT * FROM folder_tags WHERE folder_id = 'test-id';
DELETE FROM folder_tags WHERE folder_id = 'test-id';
```

### **Check Tag Data:**
```sql
-- View all folder tags
SELECT 
    ft.folder_id,
    tn.name as folder_name,
    ft.tag,
    CASE ft.is_auto_suggested WHEN 1 THEN 'Auto' ELSE 'Manual' END as tag_type,
    CASE ft.inherit_to_children WHEN 1 THEN 'Yes' ELSE 'No' END as inherits,
    datetime(ft.created_at, 'unixepoch', 'localtime') as created
FROM folder_tags ft
LEFT JOIN tree_nodes tn ON ft.folder_id = tn.id
ORDER BY tn.name, ft.tag;

-- View all todo tags
SELECT 
    t.text as todo_text,
    tt.tag,
    CASE tt.is_auto WHEN 1 THEN 'Auto' ELSE 'Manual' END as tag_type
FROM todo_tags tt
INNER JOIN todos t ON tt.todo_id = t.id
ORDER BY t.text, tt.tag;

-- Check inheritance (todos in tagged folders)
SELECT 
    tn.name as folder_name,
    ft.tag as folder_tag,
    t.text as todo_text,
    tt.tag as todo_tag
FROM folder_tags ft
INNER JOIN tree_nodes tn ON ft.folder_id = tn.id
INNER JOIN todos t ON t.category_id = tn.id
LEFT JOIN todo_tags tt ON tt.todo_id = t.id AND tt.tag = ft.tag
ORDER BY tn.name, t.text;
```

---

## 🐛 **EDGE CASES TO TEST**

### **Edge Case 1: Deep Folder Nesting**
- Create folder 10 levels deep
- Tag the 3rd level folder
- Create todo at 10th level
- Should inherit tags from 3rd level

### **Edge Case 2: Multiple Tagged Ancestors**
- Folder A: tagged "project-a"
- Folder A > Folder B: tagged "feature-b"
- Create todo in Folder B
- Should inherit BOTH "project-a" and "feature-b"

### **Edge Case 3: Move Between Tagged Folders**
- Folder A: tagged "tag-a"
- Folder B: tagged "tag-b"
- Create todo in A (has "tag-a")
- Move to B
- Should remove "tag-a", add "tag-b"

### **Edge Case 4: Manual Tags + Auto Tags**
- Folder tagged with "auto-tag"
- Create todo (gets "auto-tag" as auto)
- Manually add "manual-tag" to todo
- Move todo to different folder
- Auto tags should update, manual tag should persist

### **Edge Case 5: Very Long Tag Names**
- Try adding tag with 51 characters → Should reject
- Try adding tag with exactly 50 characters → Should accept

### **Edge Case 6: Special Characters**
- Test: "25-117" (hyphens) → Should work
- Test: "Q&A" (ampersand) → Should work
- Test: "Tag with spaces" → Should work
- Test: "tag_underscore" → Should work
- Test: "tag@invalid" → Should reject

---

## 📝 **LOGGING TO MONITOR**

Watch for these log messages:

**Folder Tagging:**
```
[INFO] Setting 2 tags on folder <guid>
[INFO] Successfully set 2 tags on folder <guid>. New items will inherit these tags.
[INFO] [TodoStore] Folder <guid> tagged with 2 tags: 25-117-OP-III, 25-117. New todos will inherit these tags.
```

**Todo Creation:**
```
[INFO] [CreateTodoHandler] Creating todo: '<text>'
[INFO] Updating todo <guid> tags: moving from <null> to <folder-guid>
[INFO] Found 2 applicable tags for folder <folder-guid>
[INFO] Added 2 inherited tags to todo <guid>
```

**Todo Movement:**
```
[INFO] [MoveTodoCategoryHandler] Moved todo <guid> from <old> to <new>
[INFO] Updating todo <guid> tags: moving from <old-guid> to <new-guid>
[INFO] Removed inherited tags from todo <guid>
[INFO] Added X inherited tags to todo <guid>
```

---

## 🎯 **SUCCESS CRITERIA**

### **Must Pass:**
- ✅ Can open Folder Tag Dialog from context menu
- ✅ Can add/remove tags in dialog
- ✅ Tags save to database correctly
- ✅ New todos inherit folder tags automatically
- ✅ Moving todos updates tags correctly
- ✅ Removing folder tags doesn't affect existing todos
- ✅ No UI freezing or crashes
- ✅ No database errors in logs

### **Nice to Have (Future):**
- ⏳ Visual tag icon on folders in tree
- ⏳ Tag count tooltip on hover
- ⏳ Auto-suggestion popup for project-pattern folders
- ⏳ Bulk update option for existing todos

---

## 🚨 **KNOWN LIMITATIONS**

1. **No Visual Indicator Yet**
   - Folders don't show tag icon in tree view
   - Must use context menu to see/manage tags
   - Future enhancement

2. **No Bulk Update**
   - Setting tags on folder with 100 existing todos → todos NOT updated
   - Only NEW todos get tags
   - By design (performance/UX decision)

3. **No Auto-Suggestion Yet**
   - User must manually set tags
   - Pattern detection exists but no UI popup
   - Future enhancement (Phase 4)

4. **Database Location**
   - Folder tags: `%LocalAppData%\NoteNest\tree.db` → folder_tags table
   - Todo tags: `%LocalAppData%\NoteNest\.plugins\NoteNest.TodoPlugin\todos.db` → todo_tags table
   - Different databases by design (folder tags are app-wide, todo tags are plugin-specific)

---

## 🔍 **TROUBLESHOOTING**

### **Issue: Context menu doesn't appear**
- Check: Is LucideTag icon defined in LucideIcons.xaml?
- Check: Is context menu resource correctly defined?
- Check: Is ContextMenu binding correct on Grid?

### **Issue: Dialog doesn't open**
- Check logs for exceptions
- Check: Are services (IMediator, IFolderTagRepository) registered?
- Check: Window.GetWindow(this) returns valid window?

### **Issue: Tags don't save**
- Check logs for SQL errors
- Check: Did migration 003 run successfully?
- Check: `SELECT * FROM schema_version` → should show version 3
- Check: folder_tags table exists?

### **Issue: Tags don't appear on todos**
- Check: CategoryId is set correctly on todo
- Check: CreateTodoHandler logs show "Applied folder-inherited tags"
- Check: TagInheritanceService logs show "Found X applicable tags"
- Check: todo_tags table has entries with is_auto=1

### **Issue: Moving todos doesn't update tags**
- Check: MoveTodoCategoryHandler uses TagInheritanceService
- Check: Logs show "Updating todo X tags: moving from Y to Z"
- Check: Old tags removed, new tags added

---

## 📋 **MANUAL TEST CHECKLIST**

Before marking as complete, verify:

- [ ] Migration 003 applied (schema_version = 3)
- [ ] folder_tags table exists with correct schema
- [ ] Context menu appears on category right-click
- [ ] "Set Folder Tags..." opens dialog
- [ ] Can add tags to folder in dialog
- [ ] Can remove tags from folder in dialog
- [ ] Tags save to database (check with SQL query)
- [ ] Create todo in tagged folder → todo has tags
- [ ] Tags appear in todo's "Remove Tag..." list
- [ ] Move todo to untagged folder → tags removed
- [ ] Move todo to differently tagged folder → tags updated
- [ ] Remove folder tags → existing todos keep tags
- [ ] Create new todo after removing folder tags → no tags
- [ ] Invalid tag names are rejected
- [ ] Empty tag list shows confirmation dialog
- [ ] No crashes or freezes
- [ ] No SQL errors in logs

---

## 🎉 **EXPECTED USER EXPERIENCE**

### **Tagging a Folder:**
1. User right-clicks "Projects/25-117 - OP III"
2. Selects "Set Folder Tags..."
3. Dialog opens
4. User types "25-117-OP-III", clicks Add
5. User types "25-117", clicks Add
6. User clicks Save
7. **Dialog closes instantly** (< 100ms)
8. Success message: "Folder tags removed successfully." (or similar)

### **Creating Todos:**
1. User creates todo in tagged folder
2. Todo appears in list with tag icon
3. Hovering shows tooltip with tags
4. **No delay, no freezing**

### **Moving Todos:**
1. User drags todo to different folder
2. Tags update instantly
3. **No bulk operations, no waiting**

---

## 🚀 **NEXT STEPS AFTER TESTING**

### **If Tests Pass:**
1. Document the feature
2. Update user documentation
3. Consider visual indicators (future enhancement)
4. Consider tag suggestion popup (Phase 4)

### **If Tests Fail:**
1. Check logs for errors
2. Verify database migration
3. Check service registration
4. Debug event flow
5. Report issues for fixing

---

## 📞 **READY TO TEST!**

**Current Status:** ✅ All code complete and building  
**Recommendation:** Launch app and run TEST 1 and TEST 2  
**Estimated Testing Time:** 15-30 minutes

**Database Location:**
- Tree DB: `%LocalAppData%\NoteNest\tree.db`
- Todo DB: `%LocalAppData%\NoteNest\.plugins\NoteNest.TodoPlugin\todos.db`

**Key Logs to Watch:**
- `[SetFolderTagHandler]` - Tag saving
- `[CreateTodoHandler]` - Tag inheritance on create
- `[MoveTodoCategoryHandler]` - Tag updates on move
- `[TagInheritanceService]` - Tag application logic
- `[TodoStore]` - Event handling

---

**Good luck! 🎯**

