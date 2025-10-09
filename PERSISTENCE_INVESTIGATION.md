# 🔍 Persistence Investigation

**Issue:** Todos save successfully but don't load on restart  
**Status:** Analyzing logs and code flow

---

## 📊 LOG ANALYSIS

### **First Session (14:50:30 - 14:51:02):**

```
14:50:30 - App starts
14:50:30 - [TodoPlugin] Database schema created successfully
14:50:30 - [TodoStore] Loaded 0 active todos from database ← Fresh DB, expected

14:50:39 - [TodoStore] ✅ Todo saved to database: Testing
14:50:46 - [TodoStore] ✅ Todo saved to database: Hello world
14:50:53 - [TodoStore] ✅ Todo saved to database: Check 123

14:51:02 - App closes
```

**Result:** 3 todos saved to database ✅

---

### **Second Session (14:51:09 - restart):**

```
14:51:09 - App starts
14:51:09 - [TodoPlugin] Database already initialized (version 0)
14:51:09 - [TodoPlugin] Database initialized successfully
14:51:09 - [TodoStore] Loaded 0 active todos from database ← PROBLEM!
```

**Result:** Database exists, but loads 0 todos ❌

**Why?** Todos were saved but not being loaded!

---

### **Third Session (15:00:20 - another restart):**

```
15:00:20 - App starts
15:00:20 - [TodoPlugin] Database already initialized
15:00:20 - [TodoStore] Loaded 0 active todos from database ← Still 0!
```

**Database File:** 98KB (has data, not empty!)

---

## 🔍 HYPOTHESIS

### **Possible Causes:**

**1. Repository.GetAllAsync() Query Issue**
- Query returns empty list even though data exists
- Possible SQL query problem
- Possible Dapper mapping issue

**2. TodoStore Singleton Issue**
- TodoStore loaded once at startup
- But panel creates new ViewModel each time
- ViewModel might not see the loaded todos

**3. Smart List Filtering**
- Loads all todos into _todos collection
- But GetSmartList(Today) returns filtered subset
- Filtering might exclude all todos (due date issue we fixed earlier?)

**4. Dapper Deserialization Issue**
- Todos in database but can't deserialize
- Column name mismatch
- Type conversion error

---

## 🔍 KEY EVIDENCE

### **From Logs:**

**Saves ARE Working:**
```
[TodoStore] ✅ Todo saved to database: Testing
[TodoStore] ✅ Todo saved to database: Hello world
[TodoStore] ✅ Todo saved to database: Check 123
```

**Loads Return 0:**
```
[TodoStore] Loaded 0 active todos from database
```

**Database File EXISTS:**
- Size: 98KB (not empty!)
- Modified: 15:04:04 (recently updated)
- Path exists

**Conclusion:** Data IS in database, but query returns empty!

---

## 🔍 INVESTIGATION NEEDED

### **Need to Check:**

1. **Repository.GetAllAsync() SQL Query**
   - What SELECT statement is being used?
   - Are there WHERE clauses filtering everything out?
   - Is Dapper mapping failing silently?

2. **Dapper Mapping**
   - Does TodoItem property names match column names?
   - Any case sensitivity issues?
   - Type mismatches (Guid vs string)?

3. **Error Handling**
   - Is GetAllAsync catching and swallowing exceptions?
   - Returning empty list on error instead of throwing?

4. **TodoStore.InitializeAsync()**
   - Is it actually being called?
   - Is it completing successfully?
   - Is the _todos collection populated but then cleared?

---

## 📊 CODE FLOW TO VERIFY

### **Startup Flow:**

```
App Starts
    ↓
MainShellViewModel.InitializeTodoPluginAsync()
    ↓
TodoDatabaseInitializer.InitializeAsync()
    ↓
TodoStore.InitializeAsync()
    ↓
TodoRepository.GetAllAsync(includeCompleted: false)
    ↓
SQL: SELECT * FROM todos WHERE is_completed = 0
    ↓
Dapper maps rows to List<TodoItem>
    ↓
TodoStore._todos.AddRange(results)
    ↓
Log: "Loaded X todos from database"
```

**Log shows:** "Loaded 0 active todos"

**Either:**
- SQL returns 0 rows (filtering issue?)
- Dapper returns empty list (mapping issue?)
- Exception caught and swallowed (error handling too aggressive?)

---

## 🎯 NEXT INVESTIGATION STEPS

### **Step 1: Check Repository.GetAllAsync() Implementation**
- Line 55-75 in TodoRepository
- Verify SQL query
- Check error handling
- See if exceptions are being caught

### **Step 2: Check Dapper Mapping**
- Does TodoItem match database columns?
- Case sensitivity?
- Type mismatches?

### **Step 3: Check TodoStore.InitializeAsync()**
- Is it being called?
- Does it complete?
- Does it populate _todos?

### **Step 4: Check Smart List Filter**
- GetSmartList(Today) might filter out all todos
- If DueDate = null, does Today view include them?
- We fixed this in TodoStore but maybe not in Repository query?

---

## 📋 SPECIFIC FILES TO REVIEW

1. `TodoRepository.cs` Lines 55-75 (GetAllAsync)
2. `TodoStore.cs` Lines 34-60 (InitializeAsync)
3. `TodoStore.cs` Lines 180-190 (GetTodayItems)
4. Database query: Need to see actual SQL and results

---

**Next: Deep dive into GetAllAsync and Dapper mapping**

