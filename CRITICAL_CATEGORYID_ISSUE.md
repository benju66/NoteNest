# 🚨 CRITICAL: CategoryId Persistence Issue - AGAIN

**Status:** Same issue as before - todos moving to Uncategorized after restart  
**Root Cause:** Dapper not mapping `category_id` TEXT column correctly

---

## 🔍 **THE PROBLEM**

**User Reports:**
> "todo tasks are moved from the category they belong to, to uncategorized after the app is restarted"

**This is THE EXACT SAME ISSUE as before!**

---

## 📊 **WHAT'S HAPPENING**

**Current Clean Repository:**
```csharp
// GetAllAsync uses Dapper direct mapping
var dtos = (await connection.QueryAsync<TodoItemDto>(sql)).ToList();

// Then converts DTO → Aggregate → TodoItem
var aggregate = dto.ToAggregate(tags);
var todoItem = TodoItem.FromAggregate(aggregate);
```

**TodoItemDto.CategoryId:**
```csharp
public string CategoryId { get; set; }  // Should contain Guid string
```

**Database Column:**
```sql
category_id TEXT  -- Contains "54256f7f-812a-47be-9de8-1570e95e7beb"
```

**Problem:** Dapper is mapping `category_id` → `CategoryId` as **NULL**!

---

## ⚠️ **DÉJÀ VU**

**We had this EXACT issue before and fixed it with manual mapping:**

**Working Manual Mapping (from before):**
```csharp
var rawResults = (await connection.QueryAsync(sql)).ToList();

foreach (var row in rawResults)
{
    var dict = (IDictionary<string, object>)row;
    var todo = new TodoItem
    {
        CategoryId = dict["category_id"] != null && dict["category_id"] is not DBNull 
            ? Guid.Parse((string)dict["category_id"])
            : null,
        // ... other fields
    };
}
```

**This WORKED!** Restart persistence was confirmed!

---

## ❌ **WHY CLEAN DTO DOESN'T WORK**

**Dapper Cannot Map:**
```
SQLite TEXT → TodoItemDto.CategoryId (string)
```

**Even though it should be simple!**

**We've tried:**
1. ❌ Direct `QueryAsync<TodoItemDto>` - CategoryId = NULL
2. ❌ Custom TypeHandlers (NullableGuidTypeHandler) - Not called
3. ✅ Manual mapping dynamic objects - **THIS WORKED!**

---

## 🎯 **THE HARD TRUTH**

**Scorched Earth DTO Refactor has the same fundamental flaw:**
- Beautiful architecture ✅
- Clean code ✅
- Proper DDD pattern ✅
- **BUT CategoryId doesn't persist!** ❌

**The manual mapping was the ONLY thing that worked!**

---

## ✅ **SOLUTION OPTIONS**

### **Option A: Revert to Manual Mapping** ⭐ **PROVEN TO WORK**

**What to do:**
1. Keep the clean architecture (DTO → Aggregate → TodoItem)
2. But use manual mapping in `GetAllAsync()` instead of `QueryAsync<TodoItemDto>`
3. Hybrid approach: Clean architecture + proven persistence

**Code:**
```csharp
public async Task<List<TodoItem>> GetAllAsync(bool includeCompleted = true)
{
    using var connection = new SqliteConnection(_connectionString);
    await connection.OpenAsync();
    
    var sql = includeCompleted 
        ? "SELECT * FROM todos ORDER BY sort_order ASC"
        : "SELECT * FROM todos WHERE is_completed = 0 ORDER BY sort_order ASC";
    
    // Manual mapping (PROVEN TO WORK!)
    var rawResults = (await connection.QueryAsync(sql)).ToList();
    var todos = new List<TodoItem>();
    
    foreach (var row in rawResults)
    {
        var dict = (IDictionary<string, object>)row;
        
        // Manual parse CategoryId
        Guid? categoryId = null;
        if (dict["category_id"] != null && dict["category_id"] is not DBNull)
        {
            var categoryIdStr = dict["category_id"].ToString();
            if (!string.IsNullOrWhiteSpace(categoryIdStr))
                categoryId = Guid.Parse(categoryIdStr);
        }
        
        // Create DTO manually
        var dto = new TodoItemDto
        {
            Id = (string)dict["id"],
            Text = (string)dict["text"],
            CategoryId = categoryId?.ToString(),  // ✅ Works!
            // ... other fields
        };
        
        // Then use DTO → Aggregate → TodoItem flow
        var aggregate = dto.ToAggregate(tags);
        var todoItem = TodoItem.FromAggregate(aggregate);
        todos.Add(todoItem);
    }
    
    return todos;
}
```

**Confidence:** 95% (we know this works!)  
**Time:** 30 minutes to add manual mapping back

---

### **Option B: Investigate Dapper Further**

**What to do:**
1. Check diagnostic logs to see if CategoryId is NULL in DTO
2. Try different Dapper mapping techniques
3. Debug why TypeHandlers don't work

**Confidence:** 40% (we've tried this extensively)  
**Time:** 2-3 hours, might not succeed

---

### **Option C: Give Up on Dapper for Guid Columns**

**Accept:**
- Dapper + SQLite TEXT + Guid? = Problems
- Manual mapping is the only reliable way
- Hybrid approach is acceptable

**Result:**
- Clean architecture (DTO → Aggregate → TodoItem) ✅
- Manual mapping for persistence ✅
- Everything works ✅

---

## 🎯 **MY RECOMMENDATION**

**Do Option A: Hybrid Approach** ⭐

**Why:**
1. ✅ Keeps clean DTO → Aggregate → TodoItem flow
2. ✅ Uses manual mapping (proven to work!)
3. ✅ Best of both worlds
4. ✅ CategoryId will persist correctly
5. ✅ 30 minutes to implement

**Compromise:**
- GetAllAsync() has manual mapping (verbose but works)
- Everything else uses clean DTO pattern
- Architecture still sound for advanced features

---

## ⏭️ **NEXT STEP**

**Please check logs after restart:**

```powershell
Get-Content "$env:LOCALAPPDATA\NoteNest\Logs\notenest-*.log" | Select-String "DTO loaded.*CategoryId" | Select-Object -Last 10
```

**If you see:**
```
[DBG] [TodoRepository] DTO loaded: "test" - CategoryId=NULL
```

**Then Dapper is failing again!** We need manual mapping.

**If you see:**
```
[DBG] [TodoRepository] DTO loaded: "test" - CategoryId=54256f7f-812a-47be-9de8-1570e95e7beb
```

**Then something else is wrong** - and we can fix it differently.

**Please share the diagnostic log output!** 🎯

