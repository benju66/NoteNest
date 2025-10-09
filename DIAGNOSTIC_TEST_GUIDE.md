# 🔍 TODO PLUGIN DIAGNOSTIC TEST GUIDE

**Build:** ✅ SUCCESS  
**Extensive Logging:** ✅ ADDED  
**Purpose:** Find exactly where the flow breaks

---

## 🚀 RUN THIS TEST

```powershell
.\Launch-NoteNest.bat
```

**Then perform these exact steps and check console after EACH step:**

---

## 📋 STEP-BY-STEP DIAGNOSTIC

### **Step 1: Open Panel**
```
Action: Press Ctrl+B
```

**Check Console For:**
```
✅ Expected:
   "🎯 ActivateTodoPlugin() called"
   "📦 TodoPlugin.CreatePanel() called"
   "📦 TodoPanelView created: True"
   "🎨 TodoPanelView constructor called"
   "📋 TodoListViewModel constructor called"
   "📋 TodoListViewModel initialized, commands ready"
   "📋 Initial todos loaded"
   
❌ If Missing:
   Panel isn't loading → Share what logs you DO see
```

---

### **Step 2: Type in Textbox**
```
Action: Click in top textbox
Action: Type "test"
```

**Check Console For:**
```
✅ Expected:
   "📋 QuickAddText changed to: 't'"
   "📋 QuickAddText changed to: 'te'"
   "📋 QuickAddText changed to: 'tes'"
   "📋 QuickAddText changed to: 'test'"
   "📋 CanExecuteQuickAdd called: True, Text='test'"

❌ If Missing:
   Textbox binding not working → DataContext issue
```

---

### **Step 3: Click Add Button**
```
Action: Click "Add" button
```

**Check Console For:**
```
✅ Expected:
   "🚀 ExecuteQuickAdd CALLED! Text='test'"
   "📋 Setting IsLoading = true"
   "📋 Created TodoItem: test, Id={guid}"
   "📋 Calling TodoStore.AddAsync..."
   "[TodoStore] ✅ Todo saved to database: test"
   "✅ TodoStore.AddAsync completed successfully"
   "📋 Cleared QuickAddText"
   "📋 Creating TodoItemViewModel..."
   "📋 TodoItemViewModel created"
   "📋 Todo added to UI! New count=1"
   "✅ Todo added to UI: test"

❌ If you see "🚀 ExecuteQuickAdd CALLED" but nothing after:
   → Exception happened, look for "❌ EXCEPTION in ExecuteQuickAdd!"
   
❌ If you DON'T see "🚀 ExecuteQuickAdd CALLED":
   → Command not executing, check button binding
```

---

### **Step 4: Press Enter Instead**
```
Action: Type "second" in textbox
Action: Press Enter key
```

**Check Console For:**
```
✅ Expected: Same logs as Step 3
   "🚀 ExecuteQuickAdd CALLED! Text='second'"
   ... all the same logs ...
   "✅ Todo added to UI: second"
   "New count=2"

❌ If doesn't work:
   KeyDown event handler not wired
```

---

## 🔍 DIAGNOSTIC SCENARIOS

### **Scenario A: No Logs At All**

**Means:** Panel isn't loading or ViewModel isn't created

**Check:**
1. Did you press Ctrl+B?
2. Did panel slide open?
3. Do you see a textbox and "Add" button?

**If panel doesn't open:**
- Look for "[TodoPlugin]" logs
- Share what you see

---

### **Scenario B: Logs Show QuickAdd Called, Then Exception**

**Means:** ExecuteQuickAdd is running but crashing

**Look for:**
```
"❌ EXCEPTION in ExecuteQuickAdd!"
"❌ Exception details: {type}: {message}"
"❌ Stack trace: ..."
```

**Share:**
- The exception type
- The exception message
- The stack trace

---

### **Scenario C: Logs Show Success, But No UI Update**

**Means:** Database saves work, but UI binding broken

**Look for:**
```
"✅ Todo added to UI! New count=1"  ← This line is key!
```

**If you see this but todo not visible:**
- XAML binding issue
- ItemsControl not rendering
- Check if Todos.Count actually increased

---

### **Scenario D: Logs Stop at "Calling TodoStore.AddAsync"**

**Means:** Database operation hanging or crashing

**Look for:**
```
"📋 Calling TodoStore.AddAsync..."
[Then nothing, or crash]
```

**Means:** Database init failed or repository issue

---

## 📊 COMPLETE LOG CHECKLIST

**When you add "test" todo, you should see ALL of these:**

```
Panel Opens:
[ ] 🎯 ActivateTodoPlugin() called
[ ] 📦 TodoPlugin.CreatePanel() called
[ ] 🎨 TodoPanelView constructor called
[ ] 📋 TodoListViewModel constructor called
[ ] 📋 TodoListViewModel initialized
[ ] 📋 Initial todos loaded

You Type:
[ ] 📋 QuickAddText changed to: 'test'
[ ] 📋 CanExecuteQuickAdd called: True

You Press Enter:
[ ] 🚀 ExecuteQuickAdd CALLED! Text='test'
[ ] 📋 Setting IsLoading = true
[ ] 📋 Created TodoItem: test
[ ] 📋 Calling TodoStore.AddAsync...
[ ] [TodoStore] ✅ Todo saved to database: test
[ ] ✅ TodoStore.AddAsync completed successfully
[ ] 📋 Cleared QuickAddText
[ ] 📋 Creating TodoItemViewModel...
[ ] 📋 TodoItemViewModel created
[ ] ✅ Todo added to UI! New count=1
[ ] 📋 PropertyChanged raised
[ ] 📋 IsLoading = false

UI Shows:
[ ] Todo "test" visible in list
```

**Count how many checkboxes you can tick** - this tells me exactly where it breaks!

---

## 🎯 WHAT TO SHARE

**Copy and share these console log sections:**

### **1. Panel Opening:**
```powershell
Select-String "ActivateTodoPlugin|TodoPlugin.CreatePanel|TodoPanelView constructor|TodoListViewModel constructor"
```

### **2. Adding Todo:**
```powershell
Select-String "ExecuteQuickAdd|TodoStore.AddAsync|Todo added to UI|New count="
```

### **3. Any Errors:**
```powershell
Select-String "EXCEPTION|ERROR|FATAL|Failed"
```

---

## 🔧 QUICK DIAGNOSTIC

**After testing, run this:**

```powershell
# Check database file:
$db = "$env:LOCALAPPDATA\NoteNest\.plugins\NoteNest.TodoPlugin\todos.db"
if (Test-Path $db) {
    $size = (Get-Item $db).Length
    Write-Output "Database: $size bytes"
    if ($size -gt 10000) { Write-Output "✅ Has data" }
} else {
    Write-Output "❌ Database doesn't exist!"
}

# Check logs:
Get-Content "$env:LOCALAPPDATA\NoteNest\debug.log" -ErrorAction SilentlyContinue | Select-String "ExecuteQuickAdd|Todo added to UI" | Select-Object -Last 10
```

---

## 🎯 CRITICAL QUESTION

**After you type "test" and press Enter:**

**Do you see in console:**
```
🚀 ExecuteQuickAdd CALLED! Text='test'
```

**YES** → Command is executing, problem is later in the flow  
**NO** → Command not executing, binding issue

**This one log line tells us everything!**

---

## 🚀 LAUNCH & TEST NOW

```powershell
.\Launch-NoteNest.bat
```

**Watch the console carefully!**

**After each action (Ctrl+B, type, Enter), check console for new logs.**

**Share with me:**
1. How many checkboxes from the checklist you can tick
2. The last log message you see
3. Whether todo appears in UI or not

**This will pinpoint the exact problem!** 🔍

