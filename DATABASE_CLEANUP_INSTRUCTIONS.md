# 🗑️ DATABASE CLEANUP - CRITICAL INSTRUCTIONS

**Status:** Migrations Fixed, Now Need Clean Database  
**Next Step:** Delete corrupted todos.db

---

## ⚠️ **IMPORTANT: CLOSE THE APP FIRST**

Before proceeding, you MUST:

1. ✅ **Close NoteNest completely**
2. ✅ **Check Task Manager:** Ensure no `NoteNest.exe` or `NoteNest.UI.exe` processes
3. ✅ **Wait 5 seconds** for database connections to fully close

**DO NOT proceed until app is fully closed!**

---

## 📍 **DATABASE FILES TO DELETE**

**Location:**
```
C:\Users\Burness\AppData\Local\NoteNest\.plugins\NoteNest.TodoPlugin\
```

**Files to delete (all 3):**
- `todos.db` (main database)
- `todos.db-shm` (shared memory file - if exists)
- `todos.db-wal` (write-ahead log - if exists)

---

## ✅ **AUTOMATED CLEANUP (RUN AFTER CLOSING APP)**

I'll run this command once you confirm the app is closed.

---

## 🎯 **WHAT HAPPENS NEXT**

After database deletion and app restart:

1. ✅ Fresh `todos.db` created
2. ✅ Initial schema applied (version 1)
3. ✅ **Migration 002 applies successfully** (fixed SQL!)
4. ✅ **Migration 003 applies successfully** (fixed SQL!)
5. ✅ Database at version 3
6. ✅ Tag system fully functional
7. ✅ **Persistence restored!**

Expected logs:
```
[TodoPlugin] Creating fresh database schema...
[TodoPlugin] Database schema created successfully
[MigrationRunner] Current database version: 1
[MigrationRunner] Applying migration 2: Migration_002_AddIsAutoToTodoTags.sql
[MigrationRunner] ✅ Migration 2 applied successfully
[MigrationRunner] Applying migration 3: Migration_003_AddTagFtsTriggers.sql
[MigrationRunner] ✅ Migration 3 applied successfully
[MigrationRunner] Database migrations complete. Final version: 3
[TodoPlugin] ✅ Database initialization successful
```

---

## 📋 **POST-CLEANUP TESTING**

After successful cleanup and restart:

1. ✅ Create a test category in Todo plugin
2. ✅ Create a test todo
3. ✅ **Close and restart app**
4. ✅ Verify category still exists
5. ✅ Verify todo still exists
6. ✅ **Persistence confirmed!**

Then continue with tag testing!

---

**Please confirm the app is closed, then I'll run the cleanup command!**

