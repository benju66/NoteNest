# 🔬 Final Diagnostic - Critical Commands

**Issue:** Even with fresh database, todos lose category_id on restart  
**Need:** Verify database state before and after restart

---

## **PLEASE RUN THESE EXACT COMMANDS:**

### **1. Close the app completely**

### **2. Delete database again (to be absolutely sure)**
```powershell
Remove-Item "$env:LOCALAPPDATA\NoteNest\.plugins\NoteNest.TodoPlugin\todos.db" -Force -ErrorAction SilentlyContinue
```

### **3. Verify it's gone**
```powershell
Test-Path "$env:LOCALAPPDATA\NoteNest\.plugins\NoteNest.TodoPlugin\todos.db"
```
**Expected output:** `False`

### **4. Start app**
```powershell
.\Launch-NoteNest.bat
```

### **5. Add category**
- Right-click "Test Notes" → "Add to Todo Categories"

### **6. Create todo**
- Open note in Test Notes folder
- Type: `[final diagnostic task]`
- Save (Ctrl+S)

### **7. Verify it appears in "Test Notes" category**
- Open Todo Manager (Ctrl+B)
- Should see: "Test Notes (1)"

### **8. BEFORE CLOSING APP - Check database**
```powershell
# Get the newest log entry with the todo
Get-Content "$env:LOCALAPPDATA\NoteNest\Logs\notenest-$(Get-Date -Format 'yyyyMMdd').log" | Select-String "final diagnostic task" | Select-Object -Last 3
```

### **9. Close app**

### **10. Check database file modification time**
```powershell
Get-ChildItem "$env:LOCALAPPDATA\NoteNest\.plugins\NoteNest.TodoPlugin\todos.db" | Select-Object LastWriteTime
```

### **11. Reopen app**
```powershell
.\Launch-NoteNest.bat
```

### **12. Check where todo appears**
- Open Todo Manager
- Is it in "Test Notes" or "Uncategorized"?

### **13. Get startup logs**
```powershell
Get-Content "$env:LOCALAPPDATA\NoteNest\Logs\notenest-$(Get-Date -Format 'yyyyMMdd').log" | Select-String "CategoryCleanup|Found.*distinct categories|final diagnostic task" | Select-Object -Last 15
```

---

**Share all outputs** - this will pinpoint EXACTLY when and why the category_id is being cleared.

