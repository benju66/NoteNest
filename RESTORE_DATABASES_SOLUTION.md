# 🔧 RESTORE DATABASES SOLUTION

**Date:** October 18, 2025  
**Problem:** Deleted databases aren't being recreated automatically  
**Root Cause:** ProjectionsInitializer registered but never called on startup  
**Solution:** Manually trigger database initialization or restore original databases

---

## 🚨 **THE PROBLEM**

When we deleted `events.db` and `projections.db`:
- ❌ App started but databases not recreated
- ❌ ProjectionsInitializer.InitializeAsync() never called
- ❌ No schema = "no such table: projection_metadata"
- ❌ Tree queries fail
- ❌ Note tree doesn't load

---

## ✅ **SOLUTION: Restore Original Databases**

### **Option A: Copy from .gitignore Backup**

If you have git-ignored backups:
```powershell
# Check for backups
Get-ChildItem "$env:LOCALAPPDATA\NoteNest" -Recurse | Where-Object { $_.Name -match "backup|bak" }
```

### **Option B: Rebuild from Migration (RECOMMENDED)**

The app has migrations that should rebuild everything. Let me create a proper fix.

---

## 🔧 **ACTUAL FIX NEEDED**

The real issue: **Old events need TodoId JSON converter, not deletion!**

**Better approach:**
1. Restore events.db and projections.db (from before we deleted them)
2. Add TodoId JSON converter (handles old format)
3. Projection can then deserialize old events
4. Continue processing new events

**But:** We already deleted the databases...

---

## 🎯 **IMMEDIATE SOLUTION**

Since databases are gone, let's check if tree.db still exists and has the structure:

```powershell
Test-Path "$env:LOCALAPPDATA\NoteNest\tree.db"
```

If YES: We can use LegacyDataMigrator to rebuild events.db from tree.db!

---

**Investigating solution...**

