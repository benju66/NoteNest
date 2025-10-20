# 🧪 TESTING GUIDE - Simplified Category Matching

**Goal:** Verify note-linked todos go to the parent category YOU added (not auto-created subfolders)

---

## 📋 **Pre-Test Setup**

### **1. Close the App**
- If running, close NoteNest completely
- Wait for process to exit

### **2. Clean Slate (Optional but Recommended)**
```powershell
# Delete todo database for fresh start
Remove-Item "C:\Users\Burness\AppData\Local\NoteNest\todos.db"
```

### **3. Clear Logs (Optional)**
```powershell
# Clear today's log file
Clear-Content "C:\Users\Burness\AppData\Local\NoteNest\Logs\notenest-20251020.log"
```

---

## 🎯 **Test Case 1: Subfolder Note → Parent Category**

### **Scenario:**
- You add parent category: **"25-117 - OP III"**
- Note is in subfolder: `25-117 - OP III\Daily Notes\Note.rtf`
- Expected: Todo under **"25-117 - OP III"** (NOT "Daily Notes")

### **Steps:**

1. **Launch the app**

2. **Add parent category to todo panel:**
   - Go to Note TreeView (left sidebar)
   - Right-click **"25-117 - OP III"**
   - Select **"Add to todos"**
   - ✅ Verify: "25-117 - OP III" appears in todo panel

3. **Open note in subfolder:**
   - Navigate to: `Projects\25-117 - OP III\Daily Notes\`
   - Open: `Note 2025.10.20 - 10.24.rtf`

4. **Create note-linked todo:**
   - Type: `[test parent category match]`
   - Save note (Ctrl+S)

5. **Check result:**
   - Go to Todo Panel (right sidebar)
   - ✅ **Expected:** Todo under **"25-117 - OP III"**
   - ❌ **NOT expected:** "Daily Notes" category created

---

## 📊 **Expected Log Output**

### **Successful Match:**

```
[TodoSync] Processing note: Note 2025.10.20 - 10.24.rtf
[TodoSync] Found 1 todo candidates
[TodoSync] Note not in tree_view - starting HIERARCHICAL folder lookup
[TodoSync] HIERARCHICAL Level 1: Checking 'Daily Notes'
[TodoSync]   Full path: 'c:\users\burness\mynotes\notes\projects\25-117 - op iii\daily notes'
[TodoSync] Found 'Daily Notes' but not in user's todo panel - continuing up...
[TodoSync] HIERARCHICAL Level 2: Checking '25-117 - OP III'
[TodoSync]   Full path: 'c:\users\burness\mynotes\notes\projects\25-117 - op iii'
[TodoSync] ✅ MATCH! Found user's category at level 2: 25-117 - OP III (ID: b9d84b31...)
[CreateTodoHandler] Creating todo: 'test parent category match'
[TodoSync] ✅ Created todo from note: "test parent category match" [matched to user category: b9d84b31...]
```

**Key Lines:**
- ✅ "Found 'Daily Notes' but not in user's todo panel - continuing up..."
- ✅ "MATCH! Found user's category at level 2: 25-117 - OP III"
- ✅ "matched to user category"

---

## 🎯 **Test Case 2: No Category Added → Uncategorized**

### **Steps:**

1. **Clear todo panel:**
   - Remove all categories from todo panel
   - CategoryStore should be empty

2. **Create note-linked todo:**
   - Open any note
   - Add: `[test uncategorized]`
   - Save

3. **Check result:**
   - ✅ **Expected:** Todo in **"Uncategorized"**
   - ✅ **Expected:** NO categories auto-created

### **Expected Log:**
```
[TodoSync] HIERARCHICAL Level 1: Checking 'Daily Notes'
[TodoSync] Found 'Daily Notes' but not in user's todo panel - continuing up...
[TodoSync] HIERARCHICAL Level 2: Checking '25-117 - OP III'
[TodoSync] Found '25-117 - OP III' but not in user's todo panel - continuing up...
[TodoSync] HIERARCHICAL Level 3: Checking 'Projects'
[TodoSync] Found 'Projects' but not in user's todo panel - continuing up...
[TodoSync] Reached notes root at level 3
[TodoSync] No user category matched after 3 levels - creating uncategorized
[TodoSync] ✅ Created todo from note: "test uncategorized" [uncategorized - no matching user category found]
```

---

## 🎯 **Test Case 3: Multiple Ancestor Match → Uses Closest**

### **Steps:**

1. **Add BOTH categories:**
   - Add "Projects" to todo panel
   - Add "25-117 - OP III" to todo panel

2. **Create note-linked todo:**
   - Open note in: `Projects\25-117 - OP III\Daily Notes\Note.rtf`
   - Add: `[test closest match]`
   - Save

3. **Check result:**
   - ✅ **Expected:** Todo under **"25-117 - OP III"** (closer match, not "Projects")

### **Expected Log:**
```
[TodoSync] HIERARCHICAL Level 2: Checking '25-117 - OP III'
[TodoSync] ✅ MATCH! Found user's category at level 2: 25-117 - OP III
```

**Should NOT reach Level 3 (Projects) because it found match at Level 2**

---

## ✅ **Success Criteria**

After all tests:

1. ✅ Todos appear under the parent category YOU added
2. ✅ NO auto-created subcategories (like "Daily Notes")
3. ✅ If no category matches → Uncategorized
4. ✅ Closest matching ancestor is used
5. ✅ Logs show clear matching process

---

## ⚠️ **If Something Goes Wrong**

### **Issue: Todo still in "Uncategorized"**

**Check:**
1. Did you add "25-117 - OP III" to todo panel BEFORE creating the todo?
2. Check logs for: "Found user's category at level X"
3. If log says "not in user's todo panel" → category wasn't added properly

### **Issue: "Daily Notes" still auto-created**

**Check:**
1. Is app using the NEW build? (check DLL timestamp)
2. Did you restart the app after building?
3. Check logs for the NEW messages (should see "continuing up...")

### **Issue: Build errors**

**Check:**
```powershell
dotnet build NoteNest.UI/NoteNest.UI.csproj --configuration Debug
```

Look for any compilation errors.

---

## 📝 **Quick Test Command**

**After closing app:**
```powershell
# Clean start
Remove-Item "C:\Users\Burness\AppData\Local\NoteNest\todos.db" -ErrorAction SilentlyContinue
Clear-Content "C:\Users\Burness\AppData\Local\NoteNest\Logs\notenest-20251020.log" -ErrorAction SilentlyContinue

# Launch app
Start-Process "NoteNest.UI\bin\Debug\net9.0-windows\NoteNest.UI.exe"

# Wait 5 seconds for app to start
Start-Sleep -Seconds 5

# Open log in real-time
Get-Content "C:\Users\Burness\AppData\Local\NoteNest\Logs\notenest-20251020.log" -Wait
```

Then perform the test manually in the UI.

---

**READY TO TEST! Follow Test Case 1 for your exact scenario.** ✅

