# ✅ Todo Plugin - Ready to Test!

## 🚀 Latest Updates (Just Fixed)

### **Activity Bar & Right Panel Added to NewMainWindow.xaml**
- ✅ Column 3: Activity Bar (48px fixed width)
- ✅ Column 4: Right Panel (animated 0-300px width)
- ✅ ActivityBarToggleButtonStyle added
- ✅ Ctrl+B keyboard shortcut added
- ✅ Animation logic in code-behind
- ✅ PropertyChanged subscription for smooth panel toggle

### **MainShellViewModel Enhanced**
- ✅ IServiceProvider injected for plugin access
- ✅ InitializePlugins() method auto-registers Todo plugin
- ✅ ActivateTodoPlugin() method creates and shows panel
- ✅ Activity bar items collection populated on startup

---

## 🧪 How to Test (Step by Step)

### **1. Build the Application**
```powershell
dotnet build NoteNest.sln
```
**Expected Result:** ✅ Build succeeded with 0 errors

### **2. Launch the Application**
```powershell
.\Launch-NoteNest.bat
```
**OR** run from Visual Studio/Rider

### **3. Look for the Activity Bar**
Once the app launches, look at the **far right side** of the window (after the workspace area).

**What to expect:**
- A new vertical bar (48px wide)
- Dark/grey background
- A checkmark icon (✓) at the top
- Hovering over it shows "Todo Manager" tooltip

**Screenshot location to look:**
```
+------------------+-----+------------------+----+
|  Category Tree   | | | |    Workspace     | ✓ |  ← Activity Bar
+------------------+-----+------------------+----+
```

### **4. Click the Checkmark Icon**
**What should happen:**
1. Right panel slides open (300px wide) with smooth animation
2. Panel header shows "Todo Manager"
3. Panel contains:
   - "Add a task..." textbox at top
   - "Filter tasks..." textbox below that
   - Empty todo list (ready for todos)
4. Checkmark icon gets a blue indicator bar on its left
5. Icon background highlights

### **5. Add Your First Todo**
1. Click in the "Add a task..." textbox
2. Type: `Buy milk`
3. Press **Enter** (or click "Add" button)

**Expected Result:**
- Todo appears in the list below
- Checkbox on the left (unchecked)
- Text "Buy milk" in the middle
- Star icon on the right (unfilled)

### **6. Test Todo Operations**

**Complete a todo:**
- Click the checkbox next to "Buy milk"
- Text gets strikethrough decoration

**Favorite a todo:**
- Click the star icon
- Star fills with blue/accent color

**Edit a todo:**
- Double-click the "Buy milk" text
- Textbox appears in place
- Change text to "Buy milk and eggs"
- Press Enter to save (or Escape to cancel)

**Add more todos:**
- Add "Call dentist"
- Add "Finish report"
- Add "Walk the dog"

### **7. Test Filtering**
1. Type "milk" in the "Filter tasks..." box
2. Only "Buy milk and eggs" should be visible
3. Press Escape to clear filter
4. All todos appear again

### **8. Test Keyboard Shortcut**
1. Press `Ctrl+B`
2. Right panel slides closed
3. Press `Ctrl+B` again
4. Right panel slides open
5. All your todos are still there (in-memory persistence)

### **9. Test Smart Lists** (Not yet wired to UI)
Smart lists will be accessible via category tree in future phase.

---

## 🐛 Troubleshooting

### **Problem: Don't see Activity Bar**

**Possible causes:**
1. **Wrong window launched** - Check console output for "NewMainWindow" vs "MinimalMainWindow"
2. **Build not deployed** - Try `dotnet clean` then `dotnet build`
3. **Column definitions not updated** - Verify NewMainWindow.xaml has 5 columns

**Quick fix:**
```powershell
# Clean and rebuild
dotnet clean
dotnet build NoteNest.sln
.\Launch-NoteNest.bat
```

### **Problem: Activity Bar visible but no checkmark icon**

**Possible causes:**
1. LucideCheck icon not found in resources
2. MainShellViewModel.InitializePlugins() not running
3. TodoPlugin not registered in DI

**Check console logs:**
Look for:
- `"Todo plugin registered in activity bar"`
- Any errors mentioning "TodoPlugin"

### **Problem: Clicking checkmark does nothing**

**Possible causes:**
1. Command not wired up
2. TodoPlugin.CreatePanel() failing
3. TodoListViewModel not created

**Check console logs:**
Look for:
- `"Todo plugin activated"`
- Any exceptions during plugin activation

### **Problem: Panel opens but is empty/white**

**Possible causes:**
1. TodoPanelView.xaml not compiling
2. DataContext not set on TodoPanelView
3. Converter resources missing

**Check:**
- Build output for XAML errors
- TodoListViewModel is being injected into TodoPanelView

---

## 📊 Expected Console Output

When the app starts successfully, you should see logs like:

```
🚀 MINIMAL APP STARTUP: 2025-10-08 ...
✅ Theme system initialized: Light
✅ Multi-window theme coordinator initialized
🔍 Search service initialized - Indexed documents: 42
✅ CategoryTreeViewModel created - Categories count: 5
✅ MainShellViewModel created - CategoryTree.Categories count: 5
✅ Workspace state restored
✅ Todo plugin registered in activity bar
```

When you click the activity bar button:
```
✅ Todo plugin activated
Right panel toggled: True
```

---

## 🎯 What Should Work

| Feature | Expected Behavior |
|---------|-------------------|
| Activity Bar | Visible on far right, 48px wide |
| Checkmark Icon | LucideCheck icon, tooltip "Todo Manager" |
| Click Icon | Panel slides open (200ms animation) |
| Ctrl+B | Toggles panel open/closed |
| Quick Add | Enter key adds todo to list |
| Checkbox | Toggles completion (strikethrough) |
| Star Icon | Toggles favorite (fills with color) |
| Filter | Live search through todos |
| In-Memory | Todos persist during app session |

---

## 🔍 Diagnostic Commands

### **Check if plugin is registered:**
Look in the DI container logs during startup for "Todo plugin"

### **Check XAML compilation:**
```powershell
dotnet build NoteNest.UI\NoteNest.UI.csproj --verbosity detailed 2>&1 | Select-String "TodoPlugin"
```

### **Check for runtime errors:**
Look in `%LOCALAPPDATA%\NoteNest\STARTUP_ERROR.txt` if app crashes

---

## 📝 Next Steps After Testing

### **If it works:**
- ✅ Take screenshots
- ✅ Document any UX issues
- ✅ Proceed to Phase 5 for persistence & note integration

### **If it doesn't work:**
- Share what you see (or don't see)
- Check console logs
- Share any error messages
- I'll debug and fix immediately

---

## 💡 Remember

**Data is currently in-memory only!** 
- Todos will disappear when you close the app
- This is by design for Phase 4 MVP
- Phase 5 will add persistence

**Try it now:**
```powershell
.\Launch-NoteNest.bat
```

Then look for the checkmark icon (✓) on the far right side of the window!

