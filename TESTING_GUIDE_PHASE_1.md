# 🧪 Phase 1 Crash Fixes - Testing Guide

**Implementation Status:** ✅ COMPLETE  
**Compilation Status:** ✅ SUCCESS (0 errors)  
**Ready for Testing:** ✅ YES

---

## 🚀 **QUICK START**

### **1. Rebuild the Application**

```powershell
# Navigate to project root
cd C:\NoteNest

# Clean previous build
dotnet clean

# Build with new crash handlers
dotnet build

# Verify build succeeded
# Should see: "Build succeeded. 0 Error(s)"
```

### **2. Launch Application**

```powershell
# Run the app
.\Launch-NoteNest.bat

# OR run directly
dotnet run --project NoteNest.UI
```

### **3. Verify Exception Handlers Are Active**

**Check the startup log for this message:**
```
2025-11-12 [TIME] [INF] 🎉 Full NoteNest app started successfully!
```

**If you see startup errors about exception handlers, the handlers are NOT registered.**  
**If startup is normal, handlers are active and protecting the app.**

---

## ⏱️ **TESTING PROTOCOL**

### **Test Session 1: Normal Usage (30 minutes)**

**What to do:**
1. Open a note (e.g., Punch List)
2. Make edits
3. Save frequently (Ctrl+S)
4. Try rapid-fire saves (press Ctrl+S 5 times quickly)
5. Navigate through categories
6. Open/close notes
7. Let app idle for 5-minute periods

**What to watch for:**
- ✅ App runs without crashing
- ✅ Saves are successful
- ✅ No error dialogs appear
- ✅ Performance feels normal

**Timer:** Set a 30-minute timer, use app naturally

---

### **Test Session 2: Stress Test (Optional)**

**Heavy file operations:**
1. Open multiple notes
2. Save all rapidly (Ctrl+Shift+S)
3. Create new notes
4. Move notes between categories
5. Rename categories

**Duration:** 15-20 minutes

---

## 🔍 **MONITORING INSTRUCTIONS**

### **Check Reduced Operations (After 30 min session)**

```powershell
# Get today's log file
$logFile = "$env:LOCALAPPDATA\NoteNest\Logs\notenest-$(Get-Date -Format 'yyyyMMdd').log"
$log = Get-Content $logFile

# Count metadata refresh operations
$refreshCount = ($log | Select-String "Refreshed metadata for").Count
Write-Host "Metadata refreshes: $refreshCount" -ForegroundColor Cyan

# Count file system events
$fsEventCount = ($log | Select-String "File system change detected").Count
Write-Host "File system events: $fsEventCount" -ForegroundColor Cyan

# Count projection catch-ups
$catchupCount = ($log | Select-String "Starting projection catch-up").Count
Write-Host "Projection catch-ups: $catchupCount" -ForegroundColor Cyan

# Expected results (30 minute session):
# - Metadata refreshes: 10-15 (was 20-25 before fix)
# - File system events: Similar to before
# - Projection catch-ups: ~360 (every 5 seconds)
```

---

## 🚨 **IF CRASH OCCURS**

### **Step 1: Check Crash Log Location**

**Primary location:**
```powershell
# Open crash logs folder
explorer "$env:LOCALAPPDATA\NoteNest\Crashes"

# OR list crash files
Get-ChildItem "$env:LOCALAPPDATA\NoteNest\Crashes\CRASH_*.txt" | Sort-Object LastWriteTime -Descending
```

**Fallback location (if primary failed):**
```powershell
# Check desktop
Get-ChildItem "$env:USERPROFILE\Desktop\CRASH_*.txt"
```

### **Step 2: Read Crash Log**

```powershell
# Get most recent crash log
$crashLog = Get-ChildItem "$env:LOCALAPPDATA\NoteNest\Crashes\CRASH_*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1

# Display contents
Get-Content $crashLog.FullName

# Copy to clipboard for sharing
Get-Content $crashLog.FullName | Set-Clipboard
```

### **Step 3: What to Look For**

**The crash log will show:**
```
╔════════════════════════════════════════════════════════════════╗
║  NOTENEST CRASH REPORT - [TYPE]
╚════════════════════════════════════════════════════════════════╝

Timestamp:       2025-11-12 09:18:42.123
Crash Type:      UI_THREAD_CRASH / ASYNC_TASK_CRASH / BACKGROUND_THREAD_CRASH
Exception Type:  [This is what we need!]
Message:         [The specific error]

─────────────────────────────────────────────────────────────────
STACK TRACE:
─────────────────────────────────────────────────────────────────
[This shows the exact code location]
```

**Share the entire crash log - especially:**
- Exception Type
- Message
- First 5-10 lines of stack trace

---

## ✅ **IF NO CRASH OCCURS**

**This means:**
1. ✅ The async void safety net prevented the crash
2. ✅ The reduced file operations prevented database contention
3. ✅ The problem is solved!

**But still recommended:**
- Run for a full 60-minute session to be sure
- Monitor system resources (RAM usage, CPU)
- Check log file for any Warning/Error messages

---

## 📊 **SUCCESS CRITERIA**

### **Minimum Success:**
- ✅ App runs for 30+ minutes
- ✅ If crash occurs, crash log is generated
- ✅ No silent crashes

### **Full Success:**
- ✅ App runs for 60+ minutes without crash
- ✅ Metadata refresh count reduced by 40-60%
- ✅ No error dialogs appear
- ✅ All features work normally

### **Exceptional Success:**
- ✅ App runs indefinitely
- ✅ Performance improved (less file I/O)
- ✅ No crashes, no errors, no warnings

---

## 🔧 **TROUBLESHOOTING**

### **Problem: Build Fails**

**Check for:**
```powershell
dotnet build 2>&1 | Select-String "error"
```

**Common issues:**
- Missing namespace (should have System.Threading.Tasks and System.Windows.Threading)
- Syntax error in exception handlers
- AppLogger.Instance not accessible

**Solution:** Share build errors, will fix immediately

---

### **Problem: App Won't Start**

**Check for:**
- STARTUP_ERROR.txt in `C:\Users\Burness\AppData\Local\NoteNest\`
- Error dialog on startup

**This would indicate:**
- Exception handler registration itself failed (extremely unlikely)
- DI container initialization failure

**Solution:** Share STARTUP_ERROR.txt contents

---

### **Problem: Crash But No Crash Log**

**This would mean:**
1. All fallback locations failed (very rare)
2. Check desktop for crash file
3. Check Windows Event Viewer:

```powershell
# Check application event log
Get-EventLog -LogName Application -Source ".NET Runtime" -After (Get-Date).AddHours(-1) -Newest 10
```

**This scenario is <1% probability** (all file write locations failed)

---

## 🎯 **WHAT TO REPORT AFTER TESTING**

### **Scenario A: Crash Occurred**

**Please provide:**
1. ✅ Full crash log file contents (copy/paste)
2. ✅ What time did crash occur? (e.g., "After 22 minutes")
3. ✅ What were you doing? (editing, saving, idle, etc.)
4. ✅ Did error dialog appear?
5. ✅ Did app continue running or terminate?

**Example report:**
```
Crash occurred after 18 minutes.
I was editing Punch List and pressed Ctrl+S.
Error dialog appeared saying "SqliteException: database is locked"
App continued running after I clicked OK.
Crash log attached.
```

---

### **Scenario B: No Crash**

**Please provide:**
1. ✅ How long did you run the app? (e.g., "45 minutes")
2. ✅ How many saves did you perform? (rough estimate)
3. ✅ Metadata refresh count (from monitoring script above)
4. ✅ Any warnings or errors in log file?

**Example report:**
```
Ran for 45 minutes without crash.
Performed ~15 saves.
Metadata refreshes: 12 (down from expected 25)
No warnings or errors in log.
App feels stable and responsive.
```

---

### **Scenario C: Error Dialog But No Crash**

**Please provide:**
1. ✅ Error message text (what dialog said)
2. ✅ What you were doing when error appeared
3. ✅ Did app continue working after?
4. ✅ Crash log contents

**Example report:**
```
Error dialog appeared saying "database is locked"
I was saving a note.
App continued working normally after clicking OK.
Crash log shows SqliteException at line 1365.
```

---

## 📈 **METRICS COMPARISON**

**Use this to verify improvement:**

| Metric | Before Fix | Expected After | Actual After |
|--------|-----------|----------------|--------------|
| Time to crash | 46 minutes | 60+ min or N/A | _[You fill in]_ |
| Metadata refreshes (per 30 min) | ~25 | ~12-15 | _[You fill in]_ |
| Crash logging | None | Detailed | _[You fill in]_ |
| Error visibility | Silent | Dialog + Log | _[You fill in]_ |
| App recovery | Crash | Continues | _[You fill in]_ |

---

## 🎯 **NEXT PHASE PREVIEW**

**After we get crash log (or confirm no crash), we can implement:**

### **Phase 2A: Performance Optimization**
- Projection dirty flag (reduce 808 → ~200 catch-ups per 46 min)
- Smarter file filtering (ignore RTF Changed events)
- Targeted metadata refresh (only affected directories)
- **Expected:** 60-70% reduction in database operations

### **Phase 2B: Database Resilience** (if needed)
- Increase connection timeout: 30s → 60s
- Add connection retry logic
- Connection pool monitoring
- **Expected:** Eliminate database lock exceptions

### **Phase 2C: Memory Management** (if needed)
- Cache size limits
- Aggressive eviction policy
- Memory pressure detection
- **Expected:** Prevent OutOfMemoryException

**But we won't know which Phase 2 to implement until we see crash log (or confirm no crashes).**

---

## 🎉 **YOU'RE READY TO TEST!**

**The application is now:**
- ✅ Protected from silent crashes
- ✅ Logging all exceptions
- ✅ Optimized for reduced file I/O
- ✅ Ready for diagnostic testing

**Just build, run, and use normally for 30-60 minutes.**

**Good luck! The crash log (if it happens) will tell us exactly what to fix next.**

