# 🔍 Simple Diagnostic Test

**Just launch and check logs!**

---

## ⚡ QUICK TEST

### **Step 1: Launch**
```powershell
.\Launch-NoteNest.bat
```

### **Step 2: Open Panel**
- Click ✓ icon in activity bar (far right)
- OR try Ctrl+B if it works

### **Step 3: Add Todo**
- Type "test" in textbox
- Press Enter

### **Step 4: Check Logs**
```powershell
.\CHECK_LOGS.ps1
```

---

## 📝 WHAT THE SCRIPT CHECKS

The PowerShell script will show you:

✅ **ViewModel created?** (Constructor logs)  
✅ **Command executed?** (ExecuteQuickAdd logs)  
✅ **Database saved?** (TodoStore logs)  
✅ **UI updated?** (Todo added to UI logs)  
❌ **Any errors?** (Exception logs)

---

## 🎯 EXPECTED OUTPUT

**If working:**
```
=== ViewModel Initialization ===
📋 TodoListViewModel constructor called
📋 TodoListViewModel initialized, commands ready

=== QuickAdd Execution ===
🚀 ExecuteQuickAdd CALLED! Text='test'
✅ Todo added to UI! New count=1

=== Database Operations ===
[TodoStore] ✅ Todo saved to database: test

=== Errors/Exceptions ===
✅ No errors found
```

**If broken, script will show WHERE it breaks!**

---

## 🚀 RUN NOW

```powershell
# 1. Launch app
.\Launch-NoteNest.bat

# 2. Try to add a todo

# 3. Check what happened
.\CHECK_LOGS.ps1
```

**Share the CHECK_LOGS output with me!** 📊

