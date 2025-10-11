# 🔬 Research Phase 2 - Domain Validation & Data Constraints

**Status:** INVESTIGATING  
**Focus:** Understand what can break during DTO→Domain conversion

---

## 🎯 **CRITICAL FINDING: CreateFromDatabase() Throws on Validation Failure**

### **The Risk:**

**Code Path:**
```csharp
// TodoItemDto.ToAggregate():40-64
var textResult = TodoText.Create(text);  // Validates!
// Then:
TodoAggregate.CreateFromDatabase()
  → TodoText.Create(text)  // Line 129
  → if (textResult.IsFailure) throw new InvalidOperationException  // Line 130-131
```

**What This Means:**
- Database has todo with text = "abc..." (2000 chars)
- TodoText.Create() fails (max 1000 chars)
- **Throws exception**
- **Todo is LOST during loading!**

**This is DATA LOSS!**

---

## 🚨 **DATABASE CONSTRAINT ANALYSIS**

### **From Schema (TodoDatabaseSchema.sql):**

```sql
text TEXT NOT NULL,  -- No max length check!
description TEXT,    -- Can be NULL, no max length
category_id TEXT,    -- Can be NULL, no validation
is_completed INTEGER NOT NULL DEFAULT 0,
CHECK (is_completed IN (0, 1)),
CHECK (is_favorite IN (0, 1)),
CHECK (is_orphaned IN (0, 1)),
CHECK (priority >= 0 AND priority <= 3),
```

**Findings:**
- ✅ text: NOT NULL (can't be empty)
- ❌ text: NO max length constraint (can be > 1000!)
- ✅ Priority: Constrained 0-3
- ✅ Booleans: Constrained 0-1
- ❌ category_id: No format validation

**Risk:** Database CAN contain text > 1000 chars!

---

## 🔍 **DATA INTEGRITY CHECK NEEDED**

**Need to verify:**
```sql
SELECT MAX(LENGTH(text)) as max_length FROM todos;
-- If > 1000, we have a problem!

SELECT COUNT(*) FROM todos WHERE LENGTH(text) > 1000;
-- How many would be lost?
```

**Research Action:** Query user's database to check actual data

---

## ⚠️ **VALIDATION MISMATCH**

### **Domain Rules vs Database Reality:**

| Field | Domain Rule | Database Constraint | Mismatch? |
|-------|-------------|---------------------|-----------|
| Text | Max 1000 chars | None | ❌ YES - Risk! |
| Text | Not empty | NOT NULL | ✅ OK |
| Priority | 0-3 | CHECK 0-3 | ✅ OK |
| DueDate | Any date | None | ✅ OK |
| CategoryId | Valid GUID or null | TEXT or NULL | ✅ OK |

**Critical Mismatch:** Text length!

---

## 📋 **MITIGATION STRATEGIES**

### **Option A: Relax Domain Validation for Database Loading**

```csharp
// In TodoAggregate.CreateFromDatabase()
// Skip validation, accept database as-is
var text = new TodoText(text);  // Private constructor, no validation

// OR
public static Result<TodoText> CreateFromDatabase(string text)
{
    // No max length check for database loading
    if (string.IsNullOrWhiteSpace(text))
        text = "[empty]";  // Fallback instead of failing
    
    return Result.Ok(new TodoText(text));
}
```

**Pros:**
- ✅ Never lose data
- ✅ Database is source of truth
- ✅ Permissive loading

**Cons:**
- ⚠️ Domain rules not enforced on existing data
- ⚠️ Need separate validation for new data

---

### **Option B: Add Database Constraints**

```sql
ALTER TABLE todos ADD CONSTRAINT CHECK (LENGTH(text) <= 1000);
```

**Pros:**
- ✅ Database enforces rules
- ✅ Consistent with domain

**Cons:**
- ❌ Breaks if existing data violates
- ❌ Need data migration

---

### **Option C: Sanitize During Conversion**

```csharp
public TodoAggregate ToAggregate(List<string> tags = null)
{
    // Sanitize text if too long
    var text = Text;
    if (text.Length > 1000)
        text = text.Substring(0, 1000);
    
    return TodoAggregate.CreateFromDatabase(
        text: text,  // Sanitized
        ...
    );
}
```

**Pros:**
- ✅ No data loss (truncates instead of failing)
- ✅ Database unchanged
- ✅ Works with any data

**Cons:**
- ⚠️ Silent data modification
- ⚠️ User doesn't know text was truncated

---

## ✅ **RECOMMENDED: Option A (Permissive Loading)**

**Rationale:**
- Database is source of truth
- Don't lose user data
- Validation applies to NEW data only
- Existing data grandfathered

**Implementation:**
- Add `CreateFromDatabase()` to TodoText value object
- Skip validation for database loading
- Keep strict validation for user input

---

## 📊 **CONFIDENCE IMPACT**

**Before:** 80%  
**Issue Found:** TodoText validation can throw (-15%)  
**With Mitigation:** 85%  
**After Validating:** TBD (need to check database)

---

**Continuing research...**

