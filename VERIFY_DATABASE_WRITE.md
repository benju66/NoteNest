# 🔬 Critical Database Write Verification

**Issue:** Todo saved with category_id but appears as NULL on restart  
**Need:** Verify database is actually writing category_id

---

## **PLEASE DO THIS EXACT SEQUENCE:**

### **Step 1: Download DB Browser for SQLite**
https://sqlitebrowser.org/dl/
(It's free, portable, no install needed)

### **Step 2: Close the app**

### **Step 3: Open the database**
- Run DB Browser for SQLite
- File → Open Database
- Navigate to: `C:\Users\Burness\AppData\Local\NoteNest\.plugins\NoteNest.TodoPlugin\todos.db`

### **Step 4: Check the todos table**
- Click "Browse Data" tab
- Select "todos" table from dropdown
- Look at the rows

### **Step 5: Critical Questions:**

**For the "final diagnostic task" row:**
1. What is the value in the `category_id` column?
   - Is it NULL?
   - Is it a GUID like "54256f7f-812a-47be-9de8-1570e95e7beb"?
   - Is it something else?

2. What is the value in `is_orphaned` column?
   - 0 or 1?

3. What is the value in `source_note_id` column?

### **Step 6: Take a screenshot or copy the row data**

Share:
- `text` column
- `category_id` column  
- `is_orphaned` column
- `source_type` column

---

## **This Will Tell Us:**

**If category_id is NULL in database:**
- ❌ INSERT is not saving it correctly
- Bug in TodoRepository.InsertAsync() or DTO mapping

**If category_id has a GUID in database:**
- ❌ SELECT/Dapper is not reading it correctly
- Bug in type handler or GetAllAsync()

**If category_id has the correct GUID (54256f7f...):**
- ❌ Something else is clearing it
- Different bug entirely

---

**Please check the database and report what you see in the category_id column!**

This is the fastest way to pinpoint the exact issue.

