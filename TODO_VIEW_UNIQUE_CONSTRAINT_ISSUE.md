# 🔍 Todo View UNIQUE Constraint Issue

**Date:** October 18, 2025  
**Status:** ✅ TodoIdJsonConverter working, NEW issue found  
**Issue:** UNIQUE constraint failed: todo_view.id  
**Root Cause:** Duplicate todo_view entries or stale data

---

## 🎉 **GOOD NEWS - TodoIdJsonConverter Works!**

### **Evidence from Logs (00:27:51):**

```
[INF] [CreateTodoHandler] Creating todo: 'test'
[INF] [CreateTodoHandler] ✅ Todo persisted to event store: ea9f6bc8-...
[INF] [CreateTodoHandler] ✅ Applied inherited tags to todo ea9f6bc8-...
[DBG] Synchronizing projections after CreateTodoCommand...
```

**No deserialization errors!** ✅  
**Event saved successfully!** ✅  
**Tags applied!** ✅

---

## 🚨 **NEW ISSUE FOUND**

### **Error:**
```
[WRN] Projection sync failed after CreateTodoCommand: 
SQLite Error 19: 'UNIQUE constraint failed: todo_view.id'
```

### **What This Means:**

TodoProjection is trying to INSERT a todo into `todo_view` table, but a row with that ID already exists!

**Possible Causes:**
1. Stale data in todo_view from previous tests
2. Todo was created multiple times (retries?)
3. Projection processed same event twice
4. todo_view not cleared when events.db was cleared

---

## 🔍 **INVESTIGATION NEEDED**

### **Questions:**

1. Does todo_view have stale entries?
2. Are todos being created in todos table?
3. Is the issue with projection INSERT or is data actually there?
4. Is TodoStore receiving the event?

---

**Investigating...**

