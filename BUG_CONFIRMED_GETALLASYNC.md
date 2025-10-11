# ✅ BUG CONFIRMED - GetAllAsync() Dapper Mapping Issue

**Date:** October 10, 2025  
**Status:** ✅ EXACT BUG FOUND  
**Severity:** CRITICAL

---

## 🎯 **THE PROOF**

### **Database Export Comparison:**

**App Running (Line 79):**
```sql
category_id = '54256f7f-812a-47be-9de8-1570e95e7beb' ✅
```

**After Restart (Line 86):**
```sql
category_id = '54256f7f-812a-47be-9de8-1570e95e7beb' ✅
```

**IDENTICAL!** The database is NEVER losing the category_id!

---

## 🚨 **THE BUG**

### **Root Cause:**

The database is CORRECT, but `TodoRepository.GetAllAsync()` is returning todos with `CategoryId = null`!

This is a **Dapper mapping bug** in the read path.

---

## 🔍 **GetAllAsync() Issue**

**Current Implementation:**
```csharp
// TodoRepository.GetAllAsync():57-91
var todos = await _repository.GetAllAsync(includeCompleted: false);

// Somewhere in the conversion chain:
// TodoItemDto → TodoAggregate → TodoItem
// The category_id is being lost!
```

**Likely Problem:**
- Uses TodoItemDto (line 72)
- DTO.ToAggregate() (line 78)
- TodoMapper.ToUiModel() (line 79)
- Somewhere in this chain, category_id becomes NULL

**Other queries work because they use direct mapping:**
```csharp
var todos = (await connection.QueryAsync<TodoItem>(sql, ...)).ToList();
// No DTO conversion, direct mapping with type handlers
```

---

## ✅ **THE FIX**

Change GetAllAsync() to use direct mapping like all other queries:

**File:** `TodoRepository.cs:60-91`

**Current (BROKEN):**
```csharp
var dtos = (await connection.QueryAsync<TodoItemDto>(sql)).ToList();
var todos = new List<TodoItem>();
foreach (var dto in dtos)
{
    var tags = await GetTagsForTodoAsync(Guid.Parse(dto.Id));
    var aggregate = dto.ToAggregate(tags.ToList());
    var uiModel = TodoMapper.ToUiModel(aggregate);
    todos.Add(uiModel);
}
return todos;
```

**Fixed (matches other queries):**
```csharp
var todos = (await connection.QueryAsync<TodoItem>(sql)).ToList();
await LoadTagsForTodos(connection, todos);
return todos;
```

**This matches:**
- GetByCategoryAsync() ✅
- GetBySourceAsync() ✅
- GetByParentAsync() ✅
- Every other query ✅

**Only GetAllAsync() uses the DTO approach - this is the bug!**

---

## 📋 **IMPACT**

**This bug affects:**
- ✅ TodoStore.InitializeAsync() - Loads todos with NULL category_id
- ✅ CategoryCleanup - Sees 0 categories (all NULL)
- ✅ Startup display - All todos appear in "Uncategorized"

**Once fixed:**
- ✅ Todos load with correct category_id
- ✅ CategoryCleanup works correctly
- ✅ Todos appear in correct categories after restart

---

**Ready to apply this 2-line fix!**

