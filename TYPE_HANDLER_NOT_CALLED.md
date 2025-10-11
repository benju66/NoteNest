# 🚨 TYPE HANDLER NOT BEING CALLED

**Critical Finding:** No GuidTypeHandler logs = Dapper is NOT using our type handler!

---

## 🎯 **WHY THIS HAPPENS**

### **Dapper Type Handler Registration Issue:**

Dapper's `AddTypeHandler<T>()` registers handlers for a SPECIFIC type.

**When Dapper queries:**
```csharp
await connection.QueryAsync<TodoItem>(sql)
```

**For each property:**
- If property type is `Guid?` AND there's a handler for `Guid?` → Use handler
- If no handler → Use default mapping
- **Default mapping for Guid? from TEXT → Returns NULL!**

### **The Problem:**

Dapper might not be recognizing that `TodoItem.CategoryId` needs the `Guid?` type handler!

**Possible causes:**
1. Property name mismatch (category_id vs CategoryId)
2. Dapper's default TEXT → Guid? mapping returns NULL
3. Column is actually NULL in query result (but database shows it's not!)

---

## 🔍 **SOLUTION: Add Raw SQL Logging**

Let me add logging to see what SQL is actually returning:

```csharp
// Before Dapper mapping
var rawResult = await connection.QueryAsync(sql);  // Returns dynamic
foreach (var row in rawResult)
{
    Log.Debug("RAW SQL: category_id = {CategoryId}", row.category_id);
}

// Then map
var todos = (await connection.QueryAsync<TodoItem>(sql)).ToList();
```

This will show if:
- SQL returns the GUID correctly
- Problem is in Dapper mapping

---

**I need to add this diagnostic to see the raw SQL result!**

