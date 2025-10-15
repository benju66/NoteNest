# 🔍 NOTE-LINKED TODO INVESTIGATION CHECKLIST

## 📋 **INFORMATION NEEDED**

To properly diagnose this issue, I need to verify several things systematically:

---

## 1️⃣ **DATABASE STATE**

### **Run this PowerShell script:**
```powershell
.\DIAGNOSE_NOTE_LINKED_TODO.ps1
```

This will generate `note_linked_todo_diagnosis.txt` with database state.

**OR manually check:**

### **Tree Database (`%LocalAppData%\NoteNest\tree.db`):**
```sql
-- Check if note exists in tree
SELECT id, name, node_type, parent_id, canonical_path 
FROM tree_nodes 
WHERE node_type = 'note' 
ORDER BY modified_at DESC 
LIMIT 5;

-- Check note's parent category
SELECT 
    n.id as note_id,
    n.name as note_name,
    c.id as category_id,
    c.name as category_name
FROM tree_nodes n
LEFT JOIN tree_nodes c ON n.parent_id = c.id
WHERE n.node_type = 'note'
ORDER BY n.modified_at DESC
LIMIT 5;
```

### **Todos Database (`%LocalAppData%\NoteNest\.plugins\NoteNest.TodoPlugin\todos.db`):**
```sql
-- Check if note-linked todo was created
SELECT 
    id,
    text,
    category_id,
    source_type,
    source_note_id,
    is_orphaned
FROM todos
WHERE source_type = 'note'
ORDER BY created_at DESC
LIMIT 5;

-- Check categories in plugin database
SELECT id, name, display_path, parent_id
FROM categories;
```

---

## 2️⃣ **LOG OUTPUT**

### **What I Need to See:**

When you create a note-linked todo, look for these log messages **in order**:

#### **A. TodoSync Phase:**
```
[INFO] [TodoSync] Processing note: Meeting.rtf
[DEBUG] [TodoSync] Found X todo candidates in Meeting.rtf
[DEBUG] [TodoSync] Note is in category: <GUID> - todos will be auto-categorized
[DEBUG] [TodoSync] Category already in store: <GUID>  
   OR
[INFO] [CategoryStore] ADDING category: Name='...'
[INFO] [CategoryStore] ✅ Category added: ...
```

**Questions:**
- Does it find the note's category?
- Is the category GUID correct?
- Is "already in store" or "ADDING category" shown?

#### **B. Todo Creation Phase:**
```
[INFO] [TodoSync] ✅ Created todo from note via command: "..." [auto-categorized: <GUID>]
[INFO] [CreateTodoHandler] Creating todo: '...'
[INFO] [CreateTodoHandler] ✅ Todo persisted: <GUID>
```

**Questions:**
- Does CreateTodoCommand succeed?
- What CategoryId is shown?

#### **C. Tag Inheritance Phase:**
```
[DEBUG] [CreateTodoHandler] No category for todo <GUID>, skipping folder tag inheritance
   OR
[INFO] Updating todo <GUID> tags: moving from <null> to <GUID>
[INFO] Found X applicable tags for folder <GUID>
[INFO] Added X inherited tags to todo <GUID>
```

**Questions:**
- Does it find the category?
- Are tags applied (if folder is tagged)?

#### **D. Event Flow Phase:**
```
[INFO] [TodoStore] 🎯 HandleTodoCreatedAsync STARTED for TodoId: <GUID>
[INFO] [TodoStore] ✅ Todo loaded from database: '...'
[INFO] [TodoStore] ➕ Adding todo to _todos collection...
[INFO] [TodoStore] ✅ Todo added to _todos collection: '...'
[INFO] [CategoryTree] 🔄 TodoStore.AllTodos CollectionChanged!
[INFO] [CategoryTree] ➕ New todo: ... (CategoryId: <GUID>)
[INFO] [CategoryTree] 🔄 Refreshing tree after TodoStore change...
[INFO] [CategoryTree] ✅ Tree refresh complete
```

**Questions:**
- Is TodoCreatedEvent received?
- Is todo added to TodoStore collection?
- Does CategoryTree refresh?
- What CategoryId does the todo have?

---

## 3️⃣ **UI STATE**

### **Check in Todo Panel:**

After creating `[TODO: Test item]` in a note:

**Question 1:** Where does the todo appear?
- [ ] In the correct category (e.g., "25-117 - OP III")
- [ ] In "Uncategorized" 
- [ ] Doesn't appear at all
- [ ] Appears after closing/reopening app

**Question 2:** Is the category visible?
- [ ] Category appears in the tree
- [ ] Category is missing
- [ ] Category is collapsed (expand it - does todo appear?)

**Question 3:** Check database manually:
```sql
-- What's the todo's category_id?
SELECT text, category_id FROM todos WHERE source_type = 'note' ORDER BY created_at DESC LIMIT 1;

-- Is that category in the CategoryStore?
SELECT id, name FROM categories WHERE id = '<category-id-from-above>';
```

---

## 4️⃣ **SPECIFIC DIAGNOSTIC STEPS**

### **Test A: Basic Note-Linked Todo**
1. Create note: `C:\Users\YourName\Documents\NoteNest\Projects\25-117 - OP III\Test.rtf`
2. Type in note: `[TODO: Diagnostic test]`
3. Save note
4. **IMMEDIATELY check logs** (before doing anything else)
5. Copy the log output showing [TodoSync], [CreateTodoHandler], [TodoStore], [CategoryTree]

### **Test B: Check Database Immediately**
Right after creating the todo, run:
```sql
-- In todos.db:
SELECT * FROM todos ORDER BY created_at DESC LIMIT 1;
-- Note the category_id

-- In tree.db:
SELECT id, name, node_type FROM tree_nodes WHERE id = '<category-id-from-above>';
-- Does the category exist?

-- In todos.db:
SELECT * FROM categories WHERE id = '<category-id-from-above>';
-- Is the category in the plugin's CategoryStore database?
```

### **Test C: Manual Refresh**
1. After creating note-linked todo
2. Right-click in Todo Panel category tree
3. Click "Refresh" (if available) or close/reopen panel
4. Does todo appear now?

**If YES:** UI refresh issue  
**If NO:** Data issue

---

## 5️⃣ **KEY QUESTIONS TO ANSWER**

Please provide answers to these:

### **Question 1: What's in the database?**
Run this after creating a note-linked todo:
```sql
SELECT 
    t.id,
    t.text,
    t.category_id,
    t.source_note_id,
    datetime(t.created_at, 'unixepoch', 'localtime') as created
FROM todos t
WHERE t.source_type = 'note'
ORDER BY t.created_at DESC
LIMIT 5;
```

**Result:** (paste output here)

### **Question 2: Does the category exist in tree.db?**
```sql
SELECT id, name, node_type, parent_id
FROM tree_nodes
WHERE node_type = 'category'
ORDER BY modified_at DESC
LIMIT 10;
```

**Result:** (paste output here)

### **Question 3: Is the category in the plugin's CategoryStore?**
```sql
SELECT id, name, display_path, parent_id
FROM categories;
```

**Result:** (paste output here)

### **Question 4: What do the logs show?**

**Please copy/paste log output containing:**
- `[TodoSync]` messages
- `[CreateTodoHandler]` messages
- `[TodoStore]` messages
- `[CategoryTree]` messages
- Any ERROR or WARNING messages

---

## 6️⃣ **HYPOTHESES TO TEST**

Based on the data you provide, I'll check:

### **Hypothesis #1: Category Not Auto-Added**
- Todo is created with correct category_id ✅
- But category is NOT in CategoryStore ❌
- Result: Todo appears in "Uncategorized"

**Evidence Needed:**
- Database shows todo has category_id
- But `SELECT * FROM categories` returns empty or doesn't include that category

### **Hypothesis #2: Event Not Firing**
- Todo is created ✅
- TodoCreatedEvent is published ✅
- But TodoStore.HandleTodoCreatedAsync is NOT called ❌
- Result: Todo not added to UI collection

**Evidence Needed:**
- Logs show "Creating todo" but NOT "HandleTodoCreatedAsync STARTED"

### **Hypothesis #3: UI Refresh Timing**
- Todo is in database ✅
- Todo is in TodoStore._todos ✅
- But CategoryTree refresh happens too early ❌
- Result: Todo not visible in UI tree

**Evidence Needed:**
- Logs show "Todo added to _todos collection"
- But CategoryTree refresh completes BEFORE todo is added

### **Hypothesis #4: Category ID Mismatch**
- Note is in folder A (GUID-A)
- Todo is created with category_id = GUID-B
- Result: Todo looking for wrong category

**Evidence Needed:**
- Note's parent_id != todo's category_id

---

## 🎯 **NEXT STEPS**

**DO NOT PROCEED WITHOUT:**
1. ✅ Database query results (todos, categories, tree_nodes)
2. ✅ Log output from todo creation
3. ✅ Answers to the 4 key questions above

**Once I have this information, I can:**
- Identify the exact point of failure
- Determine root cause
- Propose targeted fix
- Verify fix will work

---

## 📝 **HOW TO COLLECT THIS INFO**

### **Option 1: Run the diagnostic script**
```powershell
.\DIAGNOSE_NOTE_LINKED_TODO.ps1
```
Then share the contents of `note_linked_todo_diagnosis.txt`

### **Option 2: Manual collection**
1. Close NoteNest if running
2. Open PowerShell in this directory
3. Run the SQL queries above manually
4. Launch NoteNest
5. Create a note-linked todo
6. Copy all log output
7. Run SQL queries again
8. Share all results

---

**I'm ready to investigate once I have the data! 🔍**

