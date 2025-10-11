# 🔍 Critical Diagnostic - Please Run This

To find the exact root cause, please do the following **in order**:

---

## **Test Sequence:**

### **1. Create Fresh Todo (App Running)**
```
1. Open a note in "Test Notes" folder
2. Type: [diagnostic test task]
3. Save (Ctrl+S)
4. Wait 1 second
```

### **2. Check Current State**
```
Open Todo Manager (Ctrl+B)
Question: Where does "diagnostic test task" appear?
  - In "Test Notes" category? 
  - OR in "Uncategorized"?

Please answer: _______________
```

### **3. Check Logs IMMEDIATELY (Before Closing App)**
```powershell
Get-Content "$env:LOCALAPPDATA\NoteNest\Logs\notenest-$(Get-Date -Format 'yyyyMMdd').log" | Select-String "diagnostic test task" | Select-Object -Last 10
```

**Copy and paste the output here.**

---

### **4. Close App, Then Reopen**
```
Close app completely
Run: .\Launch-NoteNest.bat
```

### **5. Check State After Restart**
```
Open Todo Manager (Ctrl+B)
Question: Where does "diagnostic test task" appear NOW?
  - In "Test Notes" category?
  - OR in "Uncategorized"?

Please answer: _______________
```

### **6. Check Logs AFTER Restart**
```powershell
Get-Content "$env:LOCALAPPDATA\NoteNest\Logs\notenest-$(Get-Date -Format 'yyyyMMdd').log" | Select-String "diagnostic test task|CategoryCleanup|Found.*distinct categories" | Select-Object -Last 20
```

**Copy and paste the output here.**

---

## **What This Will Tell Us:**

- If task appears in "Test Notes" before restart → TodoSync working ✅
- If task appears in "Uncategorized" after restart → Something clears category_id during shutdown/startup
- Logs will show if CategoryCleanup or EventBus is involved

---

**Please run this exact sequence and share the answers + log outputs.**

This will pinpoint the exact moment and cause of the category_id loss.

