# 🔬 INVESTIGATION: Tab Content Inheritance Bug

## 📋 **BUG REPORT**
**Issue:** When a user creates a new note while another note is open in a tab, the new note inherits the content from the open tab instead of starting blank.

**Persistence:** The inherited content remains saved in the new note until manually cleared.

**Trigger:** Stops occurring once the user adds any content to the new note.

---

## 🎯 **INVESTIGATION PLAN**

### **Phase 1: Evidence Collection** ✅ IN PROGRESS

#### ✅ **Step 1: Review Existing Debug Logging**
**Found:**
- TabContentView has debug logs for Bound/Unbound from ViewModels
- TabViewModel logs content changes with character counts
- RTFEditor logs content loading
- SaveManager has content tracking

**Key Logs:**
```csharp
[TabContentView] Bound to: {_viewModel.Title}
[TabContentView] Loaded content: {content?.Length ?? 0} chars for {_viewModel.Title}
[TabViewModel] Content changed for {Title}: {rtfContent?.Length ?? 0} chars
[TabViewModel] TabId: {TabId}, IsDirty: true
[RTFEditor] Loaded {rtfContent?.Length ?? 0} chars
```

#### ✅ **Step 2: Code Flow Analysis**

**Note Creation Flow:**
```
1. NoteOperationsViewModel.ExecuteCreateNote()
   ├─ InitialContent = "" (empty string)
   └─ Sends CreateNoteCommand

2. CreateNoteHandler.Handle()
   ├─ Writes empty RTF to file
   └─ Returns NoteId, FilePath

3. NoteOperationsViewModel
   └─ NoteCreated event fires

4. MainShellViewModel.OnNoteCreated()
   └─ Just refreshes tree (doesn't open note)
```

**Note Opening Flow (User Clicks):**
```
1. TreeView double-click
   └─ CategoryTreeViewModel.OpenNote(note)

2. MainShellViewModel.OnNoteOpenRequested()
   └─ Workspace.OpenNoteAsync(note.Note)

3. WorkspaceViewModel.OpenNoteAsync()
   ├─ Load content from file (async)
   ├─ Register with SaveManager
   ├─ SaveManager.UpdateContent(noteId, content)
   ├─ Create TabViewModel
   ├─ ActivePane.AddTab(tabVm)
   └─ tabVm.RequestContentLoad()

4. TabViewModel.RequestContentLoad()
   └─ LoadContentRequested event fires

5. TabContentView.LoadContentIntoEditor()
   ├─ _isLoading = true
   ├─ Get content from SaveManager
   ├─ Editor.LoadContent(content)
   └─ _isLoading = false

6. Editor.LoadContent()
   └─ RTFOperations.LoadFromRTF(this, content)
```

#### ✅ **Step 3: Identified Critical Code Paths**

**TabContentView.xaml.cs - Data Context Change:**
```csharp
private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
{
    // Clean up old ViewModel
    if (_viewModel != null)
    {
        _viewModel.LoadContentRequested -= LoadContentIntoEditor;
        _viewModel.SaveContentRequested -= SaveContentFromEditor;
        System.Diagnostics.Debug.WriteLine($"[TabContentView] Unbound from: {_viewModel.Title}");
    }
    
    // Bind to new ViewModel
    _viewModel = DataContext as TabViewModel;
    if (_viewModel != null)
    {
        _viewModel.LoadContentRequested += LoadContentIntoEditor;
        _viewModel.SaveContentRequested += SaveContentFromEditor;
        
        // Load initial content  ← CRITICAL: Happens AFTER DataContext change
        LoadContentIntoEditor();
        
        System.Diagnostics.Debug.WriteLine($"[TabContentView] Bound to: {_viewModel.Title}");
    }
}
```

**Problem Areas Identified:**
1. ⚠️ `Editor.TextChanged` is subscribed BEFORE DataContext changes
2. ⚠️ `_isLoading` flag set AFTER `OnDataContextChanged` starts
3. ⚠️ `Editor.LoadContent()` doesn't explicitly clear existing content
4. ⚠️ WPF TabControl reuses ContentPresenter (same visual tree)

---

### **Phase 2: Root Cause Hypothesis**

#### **Hypothesis A: TextChanged Fires During Load** 🎯 **MOST LIKELY**
**Theory:**
```
1. Tab A is open with content "Hello World"
2. User creates new Note B (empty)
3. User clicks Note B in tree
4. WorkspaceViewModel.OpenNoteAsync() loads empty content
5. SaveManager.UpdateContent(noteB_id, "")  ← Empty string stored
6. TabViewModel B is created
7. Tab B added to TabControl
8. TabControl switches to Tab B (DataContext changes)
9. RTFEditor STILL contains "Hello World" from Tab A
10. OnDataContextChanged() fires
11. LoadContentIntoEditor() called
12. _isLoading = true  ← Set
13. Editor.LoadContent("")  ← Loading empty content
14. BUT Editor.TextChanged fires BEFORE content fully cleared
15. OnEditorTextChanged() called
16. Check: _isLoading = true, so SHOULD return early ✅
17. BUT... what if timing issue?
```

**Potential Timing Issue:**
```csharp
private void LoadContentIntoEditor()
{
    _isLoading = true;  // ← Line 105
    try
    {
        var content = _viewModel.GetContentToLoad();
        Editor.LoadContent(content);  // ← This can trigger TextChanged
        Editor.MarkClean();
    }
    finally
    {
        _isLoading = false;
    }
}

private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
{
    if (_isLoading || _viewModel == null) return;  // ← Should protect
    
    // But what if TextChanged fires on different thread?
    // Or if LoadContent is async/delayed?
}
```

#### **Hypothesis B: SaveManager Content Cache Leak** 🔍 **POSSIBLE**
**Theory:**
```
SaveManager might be returning wrong content due to:
- Hash collision in noteId generation
- Race condition in ConcurrentDictionary
- Content not properly cleared when file is empty
```

**Check:**
```csharp
// RTFIntegratedSaveEngine.cs:856
public string GetContent(string noteId)
{
    return _noteContents.TryGetValue(noteId, out var content) ? content : "";
}

// Question: What if TryGetValue returns wrong content?
```

#### **Hypothesis C: RTFOperations.LoadFromRTF Doesn't Clear** 🔍 **POSSIBLE**
**Theory:**
```
TextRange.Load() might APPEND content instead of replacing it
```

**Need to check:**
```csharp
// RTFOperations.cs:204
range = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd);
range.Load(stream, DataFormats.Rtf);

// Question: Does this clear existing content or merge?
```

---

### **Phase 3: Diagnostic Testing Plan** 📊

#### **Test 1: Add Enhanced Logging**
**Add to TabContentView.xaml.cs:**
```csharp
private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
{
    System.Diagnostics.Debug.WriteLine($"[TabContentView] ===== DataContext Changing =====");
    System.Diagnostics.Debug.WriteLine($"[TabContentView] Old VM: {_viewModel?.Title ?? "null"}");
    System.Diagnostics.Debug.WriteLine($"[TabContentView] Editor has content: {Editor.SaveContent()?.Length ?? 0} chars");
    
    // Unsubscribe TextChanged FIRST to prevent any events
    Editor.TextChanged -= OnEditorTextChanged;
    
    // Clean up old ViewModel
    if (_viewModel != null)
    {
        _viewModel.LoadContentRequested -= LoadContentIntoEditor;
        _viewModel.SaveContentRequested -= SaveContentFromEditor;
        System.Diagnostics.Debug.WriteLine($"[TabContentView] Unbound from: {_viewModel.Title}");
    }
    
    // Bind to new ViewModel
    _viewModel = DataContext as TabViewModel;
    if (_viewModel != null)
    {
        System.Diagnostics.Debug.WriteLine($"[TabContentView] New VM: {_viewModel.Title}");
        System.Diagnostics.Debug.WriteLine($"[TabContentView] Content to load: {_viewModel.GetContentToLoad()?.Length ?? 0} chars");
        
        _viewModel.LoadContentRequested += LoadContentIntoEditor;
        _viewModel.SaveContentRequested += SaveContentFromEditor;
        
        // Load initial content
        LoadContentIntoEditor();
        
        System.Diagnostics.Debug.WriteLine($"[TabContentView] After load, editor has: {Editor.SaveContent()?.Length ?? 0} chars");
    }
    
    // Resubscribe AFTER content loaded
    Editor.TextChanged += OnEditorTextChanged;
    System.Diagnostics.Debug.WriteLine($"[TabContentView] ===== DataContext Changed Complete =====");
}
```

**Add to LoadContentIntoEditor:**
```csharp
private void LoadContentIntoEditor()
{
    if (_viewModel == null) return;
    
    System.Diagnostics.Debug.WriteLine($"[TabContentView] LoadContentIntoEditor START for: {_viewModel.Title}");
    System.Diagnostics.Debug.WriteLine($"[TabContentView] Editor BEFORE load: {Editor.SaveContent()?.Length ?? 0} chars");
    
    _isLoading = true;
    try
    {
        var content = _viewModel.GetContentToLoad();
        System.Diagnostics.Debug.WriteLine($"[TabContentView] Content from SaveManager: {content?.Length ?? 0} chars");
        
        Editor.LoadContent(content);
        System.Diagnostics.Debug.WriteLine($"[TabContentView] Editor AFTER LoadContent: {Editor.SaveContent()?.Length ?? 0} chars");
        
        Editor.MarkClean();
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[TabContentView] Load error: {ex.Message}");
    }
    finally
    {
        _isLoading = false;
        System.Diagnostics.Debug.WriteLine($"[TabContentView] LoadContentIntoEditor COMPLETE");
    }
}
```

**Add to OnEditorTextChanged:**
```csharp
private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
{
    System.Diagnostics.Debug.WriteLine($"[TabContentView] TextChanged fired: _isLoading={_isLoading}, _viewModel={_viewModel?.Title ?? "null"}");
    
    if (_isLoading || _viewModel == null) 
    {
        System.Diagnostics.Debug.WriteLine($"[TabContentView] TextChanged IGNORED (loading or no VM)");
        return;
    }
    
    try
    {
        var rtfContent = Editor.SaveContent();
        System.Diagnostics.Debug.WriteLine($"[TabContentView] TextChanged processing: {rtfContent?.Length ?? 0} chars");
        
        _viewModel.OnContentChanged(rtfContent);
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[TabContentView] Content extraction failed: {ex.Message}");
    }
}
```

#### **Test 2: Reproduction Steps**
1. Open NoteNest with Debug Output visible
2. Create Note A with content "Test Content A"
3. Save Note A (Ctrl+S)
4. Leave Note A open in tab
5. Right-click category → Create New Note
6. Enter title "Note B"
7. Click Note B in tree to open it
8. **Watch Debug Output** for:
   - Content lengths at each stage
   - Whether TextChanged fires during load
   - What content SaveManager returns
   - What content actually loads into editor

**Expected Good Behavior:**
```
[TabContentView] ===== DataContext Changing =====
[TabContentView] Old VM: Note A
[TabContentView] Editor has content: 5000 chars
[TabContentView] Unbound from: Note A
[TabContentView] New VM: Note B
[TabContentView] Content to load: 120 chars  ← Empty RTF template
[TabContentView] LoadContentIntoEditor START for: Note B
[TabContentView] Editor BEFORE load: 5000 chars  ← Still has Note A
[TabContentView] Content from SaveManager: 120 chars
[TabContentView] Editor AFTER LoadContent: 120 chars  ← Cleared!
[TabContentView] ===== DataContext Changed Complete =====
```

**Expected Bad Behavior (Bug):**
```
[TabContentView] ===== DataContext Changing =====
[TabContentView] Old VM: Note A
[TabContentView] Editor has content: 5000 chars
[TabContentView] TextChanged fired: _isLoading=false  ← FIRES TOO EARLY!
[TabContentView] TextChanged processing: 5000 chars  ← Wrong content!
[TabViewModel] Content changed for Note B: 5000 chars  ← BUG!
[SaveManager] UpdateContent(noteB_id, [5000 chars])  ← SAVED WRONG CONTENT!
```

#### **Test 3: Verify SaveManager Content**
Add to WorkspaceViewModel.OpenNoteAsync():
```csharp
// After line 286
_saveManager.UpdateContent(noteId, noteContent);
System.Diagnostics.Debug.WriteLine($"[WorkspaceViewModel] SaveManager updated with: {noteContent?.Length ?? 0} chars");

// Verify it was stored correctly
var verifyContent = _saveManager.GetContent(noteId);
System.Diagnostics.Debug.WriteLine($"[WorkspaceViewModel] SaveManager now returns: {verifyContent?.Length ?? 0} chars");

if (verifyContent != noteContent)
{
    System.Diagnostics.Debug.WriteLine($"[WorkspaceViewModel] ⚠️ MISMATCH! Content not stored correctly!");
}
```

#### **Test 4: Check RTFOperations.LoadFromRTF Behavior**
Add to RTFOperations.cs:
```csharp
public static void LoadFromRTF(RichTextBox editor, string rtfContent)
{
    if (editor?.Document == null || string.IsNullOrEmpty(rtfContent)) return;
    
    var beforeLength = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text.Length;
    System.Diagnostics.Debug.WriteLine($"[RTFOperations] LoadFromRTF: Document has {beforeLength} chars BEFORE");
    
    // ... existing code ...
    
    var afterLength = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text.Length;
    System.Diagnostics.Debug.WriteLine($"[RTFOperations] LoadFromRTF: Document has {afterLength} chars AFTER");
}
```

---

### **Phase 4: Verification Tests**

#### **Test A: Empty Note Loading**
1. Create completely empty note
2. Load in tab
3. Verify editor truly empty
4. Check SaveManager content
5. Check file content

#### **Test B: Tab Switching**
1. Open Note A (large content)
2. Open Note B (empty) in new tab
3. Switch back to Note A
4. Switch to Note B
5. Verify content doesn't leak

#### **Test C: Rapid Creation**
1. Have Note A open
2. Create 5 new notes rapidly
3. Open each one
4. Verify none have Note A's content

---

## 🎯 **NEXT ACTIONS**

1. ✅ Add enhanced diagnostic logging (above)
2. ⏳ Run reproduction test
3. ⏳ Analyze debug output
4. ⏳ Identify exact timing/sequence where bug occurs
5. ⏳ Implement targeted fix based on evidence

---

## 📝 **FINDINGS** (To Be Updated)

### **Finding 1:** [Pending test results]
### **Finding 2:** [Pending test results]
### **Finding 3:** [Pending test results]

---

**Investigation Status:** Phase 1 Complete, Ready for Phase 2 Testing
**Next Step:** Add diagnostic logging and reproduce bug with Debug Output captured

