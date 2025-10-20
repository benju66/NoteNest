# 🔍 CRITICAL ISSUE - CategoryId Not Persisting

**Status:** Code is matching correctly, but CategoryId not being saved to database!

---

## 📊 **What the Logs Show**

### **Matching Logic Works:**
```
[TodoSync] ✅ MATCH! Found user's category at level 2: 25-117 - OP III (ID: b9d84b31...)
[TodoSync] ✅ Created todo from note: "task test" [matched to user category: b9d84b31...]
```

✅ Hierarchical lookup: WORKING  
✅ CategoryStore check: WORKING  
✅ CategoryId set in command: WORKING

### **But Database Doesn't Have It:**
```
[TodoStore] Event details - Text: 'task test', CategoryId: b9d84b31... ← EVENT HAS IT
[TodoStore] ✅ Todo loaded from database: 'task test', CategoryId:  ← DATABASE DOESN'T!
```

❌ TodoProjection writes CategoryId → ❌ Database doesn't store it → ❌ Query returns null

---

## 🎯 **The Real Problem**

**Data Flow:**
1. CreateTodoCommand with CategoryId → ✅ Working
2. TodoCreatedEvent with CategoryId → ✅ Working
3. TodoProjection handles event → ✅ Running
4. Writes to todo_view with CategoryId → ❓ Unknown
5. Query reads from todo_view → ❌ CategoryId is NULL!

**Somewhere between step 3 and 5, the CategoryId is being lost!**

---

## 🔍 **Possible Causes**

### **1. TodoProjection Query for Category Info Failing**

**Code (lines 136-143):**
```csharp
if (e.CategoryId.HasValue)
{
    var category = await connection.QueryFirstOrDefaultAsync<CategoryInfo>(
        "SELECT name, display_path as Path FROM tree_view WHERE id = @Id AND node_type = 'category'",
        new { Id = e.CategoryId.Value.ToString() });
    
    categoryName = category?.Name;
    categoryPath = category?.Path;
}
```

**Issue:** If this query finds NO category in tree_view, then:
- categoryName = null
- categoryPath = null
- BUT e.CategoryId IS still passed to INSERT (line 164)

**So CategoryId SHOULD still be written even if name lookup fails!**

### **2. INSERT Statement Has Wrong Column Name**

**SQL (line 148):**
```sql
category_id, category_name, category_path
```

**Value (line 164):**
```csharp
CategoryId = e.CategoryId?.ToString(),
```

**Schema (Projections_Schema.sql line 83):**
```sql
category_id TEXT,
```

**Column name matches!** ✅

### **3. TodoView Projection Position Not Persisting**

**Log shows:**
```
Projection TodoView catching up from 0 to 196
```

**Why?** 
- projection_metadata might not have TodoView entry
- OR position is being reset somehow
- Result: Replays all events every time

**But this shouldn't affect CategoryId storage...**

### **4. Parameter Binding Issue**

**Code writes:**
```csharp
CategoryId = e.CategoryId?.ToString(),
```

If `e.CategoryId` is null, this passes null (correct).  
If `e.CategoryId` has value, this passes string (correct).

**Should work!**

---

## 🎯 **Next Steps to Diagnose**

### **Option A: Add More Logging to TodoProjection**

**Add after line 164:**
```csharp
CategoryId = e.CategoryId?.ToString(),
```

**Add:**
```csharp
_logger.Info($"[TodoProjection] Writing CategoryId to todo_view: {e.CategoryId?.ToString() ?? "NULL"}");
```

### **Option B: Query Database Directly**

**Check todo_view table:**
```sql
SELECT id, text, category_id FROM todo_view WHERE text = 'task test'
```

**This will show if CategoryId is actually in the database or not.**

### **Option C: Check if TodoProjection is Even Running**

**The log shows:**
```
[TodoView] Todo created: 'task test'
```

But does it show the category lookup succeeding? Need to add logging at line 137-142.

---

## 💡 **My Suspicion**

**I think the category lookup is failing:**

```csharp
var category = await connection.QueryFirstOrDefaultAsync<CategoryInfo>(
    "SELECT name, display_path as Path FROM tree_view 
     WHERE id = @Id AND node_type = 'category'",
    new { Id = e.CategoryId.Value.ToString() });
```

**Why it might fail:**
- CategoryId from event: `b9d84b31-86f5-4ee1-8293-67223fc895e5`
- But tree_view might have it as: `B9D84B31-86F5-4EE1-8293-67223FC895E5` (uppercase)
- SQLite ID comparison might be case-sensitive!

**OR:**
- The category "25-117 - OP III" might not be in tree_view at all
- It's only in CategoryStore (user's todo panel selections)
- tree_view might only have categories that exist as actual folders

---

## 🚀 **Immediate Action**

**I'll add logging to TodoProjection to show:**
1. What CategoryId the event has
2. Whether the category lookup in tree_view succeeds
3. What's actually being written to database

**Then test again and check logs.**

This will definitively show where the CategoryId is being lost.

