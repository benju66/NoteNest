# 📋 Brief Summary for Fresh Eyes

**Project:** NoteNest Todo Plugin  
**Current Issue:** Todos save but don't persist across app restart  
**Status:** Root cause identified, fix ready

---

## 🎯 WHAT WE'RE BUILDING

**Goal:** Add todo/task management to NoteNest note-taking app with these features:

1. **Manual todos** - User adds tasks directly in a todo panel
2. **RTF integration** - Extract todos from notes using `[bracket]` syntax
3. **SQLite persistence** - Todos survive app restart
4. **Smart synchronization** - Notes and todos stay in sync

---

## 📊 WHAT WORKS

✅ **UI** - Todo panel opens, textbox and buttons work  
✅ **Add todos** - Can type and add todos, they appear in list  
✅ **Database saves** - Logs show "✅ Todo saved to database"  
✅ **Operations** - Checkbox, favorite, edit all work in UI  
✅ **Database file** - Exists, 98KB, has data

---

## ❌ WHAT DOESN'T WORK

**Persistence:** Todos save successfully but don't load on app restart

**Logs show:**
```
Session 1:
- [TodoStore] ✅ Todo saved to database: Test
- 3 todos saved successfully

Session 2 (after restart):
- [TodoStore] Loaded 0 active todos from database ← PROBLEM!
```

---

## 🔍 ROOT CAUSE

**Type Mismatch:** Database ↔ C# Model

**Database Schema:**
```sql
CREATE TABLE todos (
    id TEXT PRIMARY KEY,              -- String/TEXT type
    category_id TEXT,                 -- String/TEXT type
    parent_id TEXT,                   -- String/TEXT type
    ...
)
```

**C# Model:**
```csharp
public class TodoItem
{
    public Guid Id { get; set; }           // Guid type (not string!)
    public Guid? CategoryId { get; set; }  // Guid type (not string!)
    public Guid? ParentId { get; set; }    // Guid type (not string!)
    ...
}
```

**Problem:**
- Dapper tries to auto-map: `QueryAsync<TodoItem>(sql)`
- Database returns: `id = "guid-string"`
- TodoItem expects: `Id` as `Guid` type
- **Dapper can't auto-convert string → Guid**
- Throws: `InvalidCastException: Invalid cast from 'System.String' to 'System.Guid'`
- Exception caught, returns empty list
- **Result:** 0 todos loaded!

---

## 🔧 THE FIX

### **Option A: Dapper Type Handler (Recommended)**

Register a type handler to automatically convert TEXT ↔ Guid:

```csharp
public class GuidTypeHandler : SqlMapper.TypeHandler<Guid>
{
    public override Guid Parse(object value) 
        => Guid.Parse((string)value);
    
    public override void SetValue(IDbDataParameter parameter, Guid value) 
        => parameter.Value = value.ToString();
}

// Register at startup:
SqlMapper.AddTypeHandler(new GuidTypeHandler());
```

**Pros:** Automatic conversion everywhere, industry standard

---

### **Option B: Manual Mapping**

Explicitly map each column:

```csharp
var rows = await connection.QueryAsync(sql);
var todos = rows.Select(row => new TodoItem
{
    Id = Guid.Parse((string)row.id),
    CategoryId = string.IsNullOrEmpty(row.category_id) 
        ? null 
        : Guid.Parse(row.category_id),
    ...
}).ToList();
```

**Pros:** Full control  
**Cons:** Tedious, repetitive

---

## 📊 ADDITIONAL CONTEXT

### **Implementation So Far:**

**Completed:**
- ✅ SQLite database (1,787 lines)
- ✅ RTF bracket parser (442 lines)
- ✅ UI working (todos appear when added)
- ✅ Database saves working

**Current Blocker:**
- ❌ Dapper can't deserialize TEXT GUIDs to Guid type
- Need type handler or manual mapping

### **Code Locations:**

**The failing query:**
- File: `TodoRepository.cs`
- Line: 69
- Code: `await connection.QueryAsync<TodoItem>(sql)`

**The type mismatch:**
- Database: `id TEXT` (sqlite column type)
- Model: `public Guid Id` (C# property type)

---

## 🎯 RECOMMENDATION

**Implement Dapper GuidTypeHandler:**
1. Add TypeHandler class
2. Register in startup (PluginSystemConfiguration)
3. Test GetAllAsync
4. Verify persistence works

**Estimated Time:** 15-20 minutes  
**Confidence:** 99%  
**Risk:** Minimal (standard Dapper pattern)

---

## 📝 FILES TO REVIEW

If you want to understand the full context:

1. **TodoRepository.cs** - Line 69 (failing query)
2. **TodoItem.cs** - Model with Guid properties
3. **TodoDatabaseInitializer.cs** - Schema with TEXT columns
4. **PluginSystemConfiguration.cs** - DI registration (where to add type handler)
5. **Logs** - Lines 494-502 show the InvalidCastException clearly

---

## ✅ SUMMARY

**What:** Todo plugin for NoteNest  
**Progress:** UI works, saves work, only persistence broken  
**Issue:** Dapper can't convert TEXT → Guid automatically  
**Fix:** Add Dapper type handler (standard pattern)  
**Confidence:** 99%  
**Next:** Implement type handler, test, done

---

**This is a clean, well-understood issue with a proven solution.** ✅

