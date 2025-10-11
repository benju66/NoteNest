# 🔬 Research Phase 1 - Query Method Analysis

**Date:** October 10, 2025  
**Status:** IN PROGRESS  
**Goal:** Identify which query methods are broken vs working

---

## 📊 **QUERY METHOD INVENTORY (16 Total)**

### **Category A: Manual Mapping** (WORKS)
1. ✅ `GetAllAsync()` - Manual dict mapping (PROVEN WORKING)

### **Category B: Direct TodoItem Mapping** (UNKNOWN STATUS)
2. ❓ `GetByIdAsync()` - QueryAsync<TodoItem>
3. ❓ `GetByCategoryAsync()` - QueryAsync<TodoItem>
4. ❓ `GetBySourceAsync()` - QueryAsync<TodoItem>
5. ❓ `GetByParentAsync()` - QueryAsync<TodoItem>
6. ❓ `GetRootTodosAsync()` - QueryAsync<TodoItem>
7. ❓ `GetTodayTodosAsync()` - QueryAsync<TodoItem> (view)
8. ❓ `GetOverdueTodosAsync()` - QueryAsync<TodoItem> (view)
9. ❓ `GetHighPriorityTodosAsync()` - QueryAsync<TodoItem> (view)
10. ❓ `GetFavoriteTodosAsync()` - QueryAsync<TodoItem> (view)
11. ❓ `GetRecentlyCompletedAsync()` - QueryAsync<TodoItem> (view)
12. ❓ `GetScheduledTodosAsync()` - QueryAsync<TodoItem>
13. ❓ `SearchAsync()` - QueryAsync<TodoItem>
14. ❓ `GetByTagAsync()` - QueryAsync<TodoItem>
15. ❓ `GetOrphanedTodosAsync()` - QueryAsync<TodoItem>
16. ❓ `GetByNoteIdAsync()` - QueryAsync<TodoItem>

**CRITICAL FINDING:** 15 out of 16 methods use `QueryAsync<TodoItem>` direct mapping!

**Same pattern that failed in GetAllAsync when we tried it!**

---

## 🚨 **HYPOTHESIS: ALL 15 Direct Mapping Queries Are Broken**

### **Evidence:**

1. **GetAllAsync failed with direct mapping**
   - Tried: `QueryAsync<TodoItem>`
   - Result: CategoryId = NULL
   - Cause: Type handler not invoked

2. **Other 15 methods use SAME pattern**
   - All use: `QueryAsync<TodoItem>`
   - All expect: Type handlers to work
   - **Likely result: ALL have CategoryId = NULL bug!**

3. **Why we didn't discover this:**
   - Only tested GetAllAsync with restart
   - Other methods never tested with CategoryId
   - They work for other fields (text, priority, etc.)
   - CategoryId bug is silent

---

## 🔍 **TESTING PLAN TO VALIDATE HYPOTHESIS**

### **Test Each Query:**

```
For each of 15 queries:
1. Create todo with CategoryId in database
2. Call the query method
3. Check if CategoryId is populated in result
4. Log: WORKS or BROKEN

Expected Result: ALL 15 will show CategoryId = NULL
```

**If hypothesis is correct:**
- ✅ Scope is clear (all 15 need fixing)
- ✅ DTO refactor makes sense (fixes all at once)
- ✅ Manual mapping would need 15 separate fixes (tedious!)

**Time to test:** 2-3 hours

---

## 📋 **DOMAIN VALIDATION ANALYSIS**

### **TodoText.Create() Rules:**

```csharp
if (string.IsNullOrWhiteSpace(text))
    return Result.Fail("Todo text cannot be empty");

if (text.Length > 1000)
    return Result.Fail("Todo text cannot exceed 1000 characters");
```

**Risk Assessment:**
- Can database have empty text? **NO** (TEXT NOT NULL in schema)
- Can database have >1000 chars? **POSSIBLE** (no CHECK constraint)
- What happens if validation fails? **THROWS InvalidOperationException!**

**Impact:** If database has text > 1000 chars, DTO loading FAILS and todo is LOST!

**Mitigation Options:**
1. Relax validation for CreateFromDatabase (no max length check)
2. Truncate to 1000 chars if too long
3. Add CHECK constraint to database

---

### **DueDate.Create() Rules:**

```csharp
public static Result<DueDate> Create(DateTime date)
{
    return Result.Ok(new DueDate(date));  // No validation!
}
```

**Risk Assessment:** ✅ LOW - Accepts any date

---

### **TodoAggregate.CreateFromDatabase() Behavior:**

**Line 129-131:**
```csharp
var textResult = TodoText.Create(text);
if (textResult.IsFailure)
    throw new InvalidOperationException($"Invalid text in database: {text}");
```

**CRITICAL ISSUE:** Throws exception if validation fails!

**This means:**
- Database with text > 1000 chars → Exception → Todo lost!
- Database with empty text (shouldn't happen) → Exception → Todo lost!

**Confidence Impact:** -25% (data loss risk!)

**Research Needed:**
- Query actual database for max text length
- Check if any todos have >1000 chars
- Decide on mitigation strategy

---

## 📊 **PRELIMINARY FINDINGS**

### **Good News:**
1. ✅ TodoItemDto exists and has proper conversions
2. ✅ ToAggregate() method properly implemented
3. ✅ FromAggregate() handles all fields
4. ✅ DueDate validation is permissive

### **Concerns:**
1. ⚠️ ALL 15 direct mapping queries likely broken (need testing)
2. ⚠️ TodoText validation can THROW (data loss risk)
3. ⚠️ CreateFromDatabase doesn't handle validation failures gracefully
4. ⚠️ No try-catch in DTO.ToAggregate()

### **Unknowns:**
1. ❓ Do other queries actually return CategoryId correctly? (need testing)
2. ❓ Does database have any text > 1000 chars? (need query)
3. ❓ Performance impact of DTO conversion? (need benchmark)
4. ❓ Are there other validation rules we're missing? (need review)

---

## 🎯 **NEXT RESEARCH STEPS**

### **Immediate (30 min):**
1. Check database for max text length
2. Add try-catch to ToAggregate() for safety
3. Review all value object validation

### **Critical (2 hours):**
4. Test GetByCategoryAsync with restart
5. Test GetBySourceAsync with restart
6. Test smart list queries with restart
7. Document which are broken

### **Validation (1 hour):**
8. Create test suite for DTO conversions
9. Test edge cases (null, max values, special chars)
10. Validate no data loss scenarios

---

**Research continuing...**

