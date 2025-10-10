# 🎯 RTF Todo Extraction - Complete Master Plan

**Date:** October 10, 2025  
**For:** Complete RTF todo integration with multiple input methods and visual feedback  
**Phased Implementation:** Brackets → Multiple Methods → Visual Feedback → Navigation

---

## 📋 EXECUTIVE SUMMARY

### **Vision: Multiple Ways to Create Todos from Notes**

**User Workflows:**
1. **Bracket Syntax:** `[call John]` → Automatic extraction on save
2. **TODO Prefix:** `TODO: send report` + Enter → Automatic extraction
3. **Text Selection:** Select text → Right-click → "Create Todo"
4. **Toolbar Button:** Select text → Click "➕ Add as Todo" button
5. **Keyboard Shortcut:** Select text → Ctrl+Shift+T → Create todo

**Visual Feedback:**
- ✅ Highlight extracted todos in note (subtle background color)
- ✅ Tooltip on hover (shows todo status, due date)
- ✅ Completion indicator (strikethrough or checkmark)
- ✅ Click to navigate to todo panel
- ✅ Non-breaking RTF formatting (preserves user formatting)

---

## 🏗️ CURRENT STATE (Context)

### **What Exists:**

**RTF Infrastructure:**
- ✅ `BracketTodoParser.cs` (442 lines) - Extracts `[text]` pattern
- ✅ `TodoSyncService.cs` (267 lines) - Background sync on note save
- ✅ `SmartRtfExtractor.cs` - Battle-tested RTF → plain text conversion
- ✅ Database tracking (`source_note_id`, `source_line_number`, `source_char_offset`)

**RTF Editor:**
- ✅ RichTextBox-based editor
- ✅ Syntax highlighting support
- ✅ Toolbar infrastructure exists
- ✅ Context menu support

**Data Model:**
```csharp
public class TodoItem
{
    public Guid? SourceNoteId { get; set; }      // Links to note
    public string? SourceFilePath { get; set; }   // Note file path
    public int? SourceLineNumber { get; set; }    // Line in RTF
    public int? SourceCharOffset { get; set; }    // Character position
    public bool IsOrphaned { get; set; }          // Source deleted
}
```

**Database:**
```sql
source_note_id TEXT,
source_file_path TEXT,
source_line_number INTEGER,
source_char_offset INTEGER,
is_orphaned INTEGER DEFAULT 0
```

**Sync Logic:**
- ✅ Reconciliation (add new, mark orphaned, update seen)
- ✅ Debouncing (500ms to avoid spam)
- ✅ Error handling

---

## 📊 IMPLEMENTATION PHASES

### **PHASE 1: BRACKETS (Priority 1) - CURRENT PRIORITY**

**Status:** ✅ Parser exists, sync service exists  
**Needs:** Testing + Category integration  
**Time:** Already complete, needs 1 hour testing

**User Workflow:**
```
1. User types in note: "[call John about project]"
2. User saves note (Ctrl+S)
3. TodoSyncService detects save
4. BracketTodoParser extracts text
5. Todo created with source tracking
6. Todo appears in panel ✅
```

**Implementation Complete - Just Test!**

---

### **PHASE 2: TODO PREFIX (Priority 2) - NEXT**

**Time to Implement:** 4-6 hours  
**Complexity:** Medium  
**Value:** ⭐⭐⭐⭐⭐

#### **User Workflow:**

```
User types in note:
"TODO: send weekly report to team
 TODO: review budget spreadsheet
 TODO: call client about requirements"

User presses Enter after "TODO: ..."
    ↓
Automatic extraction triggers
    ↓
3 todos created automatically ✅
```

#### **Implementation Details:**

**Step 2.1: Create TodoPrefixParser (2 hours)**

**Location:** `NoteNest.UI/Plugins/TodoPlugin/Infrastructure/Parsing/TodoPrefixParser.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Parsing
{
    /// <summary>
    /// Parses todo items from RTF content using TODO: prefix syntax
    /// Supports variants: TODO:, ToDo:, todo:, [ ], [x]
    /// </summary>
    public class TodoPrefixParser
    {
        private readonly IAppLogger _logger;
        private readonly Regex _todoPrefixPattern;
        
        public TodoPrefixParser(IAppLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Pattern: Matches lines starting with TODO: (case-insensitive)
            // Also matches common checkbox patterns: [ ], [x], [X]
            _todoPrefixPattern = new Regex(
                @"^[\s]*(?:TODO:|ToDo:|todo:|\[\s*\]|\[x\]|\[X\])\s*(.+)$",
                RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase
            );
        }
        
        /// <summary>
        /// Extract todos from RTF using TODO: prefix pattern
        /// </summary>
        public List<TodoCandidate> ExtractFromRtf(string rtfContent)
        {
            if (string.IsNullOrEmpty(rtfContent))
                return new List<TodoCandidate>();
            
            try
            {
                // Convert RTF to plain text
                var plainText = SmartRtfExtractor.ExtractPlainText(rtfContent);
                
                if (string.IsNullOrWhiteSpace(plainText))
                    return new List<TodoCandidate>();
                
                return ExtractFromPlainText(plainText);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoPrefixParser] Failed to extract from RTF");
                return new List<TodoCandidate>();
            }
        }
        
        /// <summary>
        /// Extract todos from plain text
        /// </summary>
        public List<TodoCandidate> ExtractFromPlainText(string plainText)
        {
            var candidates = new List<TodoCandidate>();
            var lines = plainText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var charPosition = 0;
            
            for (int lineNumber = 0; lineNumber < lines.Length; lineNumber++)
            {
                var line = lines[lineNumber];
                var match = _todoPrefixPattern.Match(line);
                
                if (match.Success)
                {
                    var todoText = match.Groups[1].Value.Trim();
                    
                    if (!string.IsNullOrWhiteSpace(todoText))
                    {
                        candidates.Add(new TodoCandidate
                        {
                            Text = todoText,
                            LineNumber = lineNumber,
                            CharacterOffset = charPosition + match.Index,
                            OriginalMatch = match.Value,
                            Confidence = 0.95, // High confidence for explicit prefix
                            LineContext = line.Trim()
                        });
                    }
                }
                
                charPosition += line.Length + 1;
            }
            
            _logger.Debug($"[TodoPrefixParser] Extracted {candidates.Count} todos using prefix pattern");
            return candidates;
        }
    }
}
```

**Examples Supported:**
```
TODO: call John          ✅ Extracts: "call John"
todo: send email         ✅ Extracts: "send email"
ToDo: review docs        ✅ Extracts: "review docs"
[ ] buy milk             ✅ Extracts: "buy milk"
[x] completed task       ✅ Extracts: "completed task" (marked complete)
[X] another done         ✅ Extracts: "another done" (marked complete)
```

---

**Step 2.2: Combine Parsers (1 hour)**

**Location:** `NoteNest.UI/Plugins/TodoPlugin/Infrastructure/Parsing/CompositeTodoParser.cs`

```csharp
/// <summary>
/// Combines multiple parsing strategies to extract todos from various formats
/// </summary>
public class CompositeTodoParser
{
    private readonly BracketTodoParser _bracketParser;
    private readonly TodoPrefixParser _prefixParser;
    private readonly IAppLogger _logger;
    
    public CompositeTodoParser(
        BracketTodoParser bracketParser,
        TodoPrefixParser prefixParser,
        IAppLogger logger)
    {
        _bracketParser = bracketParser;
        _prefixParser = prefixParser;
        _logger = logger;
    }
    
    /// <summary>
    /// Extract todos using all available parsing strategies
    /// </summary>
    public List<TodoCandidate> ExtractFromRtf(string rtfContent)
    {
        var allCandidates = new List<TodoCandidate>();
        
        try
        {
            // Extract using bracket parser
            var bracketTodos = _bracketParser.ExtractFromRtf(rtfContent);
            allCandidates.AddRange(bracketTodos);
            
            // Extract using prefix parser
            var prefixTodos = _prefixParser.ExtractFromRtf(rtfContent);
            allCandidates.AddRange(prefixTodos);
            
            // DEDUPLICATION: Remove duplicates (same line, same text)
            var deduplicated = DeduplicateCandidates(allCandidates);
            
            _logger.Info($"[CompositeParser] Extracted {deduplicated.Count} unique todos " +
                        $"({bracketTodos.Count} brackets, {prefixTodos.Count} prefixes)");
            
            return deduplicated;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[CompositeParser] Failed to extract todos");
            return new List<TodoCandidate>();
        }
    }
    
    /// <summary>
    /// Remove duplicate candidates (same line + similar text)
    /// </summary>
    private List<TodoCandidate> DeduplicateCandidates(List<TodoCandidate> candidates)
    {
        return candidates
            .GroupBy(c => new { c.LineNumber, TextHash = c.Text.GetHashCode() })
            .Select(g => g.First())
            .OrderBy(c => c.LineNumber)
            .ToList();
    }
}
```

**Update TodoSyncService:**
```csharp
public class TodoSyncService
{
    private readonly CompositeTodoParser _parser;  // ← Use composite instead
    
    public TodoSyncService(
        ISaveManager saveManager,
        ITodoRepository repository,
        CompositeTodoParser parser,  // ← Changed from BracketTodoParser
        ITreeDatabaseRepository treeRepository,
        IAppLogger logger)
    {
        _parser = parser;
    }
}
```

---

**Step 2.3: Handle Completed Checkboxes (1 hour)**

**Enhancement:** `[x] completed task` → Create todo as already completed

```csharp
public class TodoCandidate
{
    public string Text { get; set; }
    public int LineNumber { get; set; }
    public bool IsCompleted { get; set; }  // ← NEW
    public string OriginalPrefix { get; set; }  // ← "[x]", "TODO:", etc.
}

// In TodoPrefixParser
var match = _todoPrefixPattern.Match(line);
if (match.Success)
{
    var prefix = match.Value.Trim();
    var todoText = match.Groups[1].Value.Trim();
    var isCompleted = prefix.Contains("[x]") || prefix.Contains("[X]");
    
    candidates.Add(new TodoCandidate
    {
        Text = todoText,
        LineNumber = lineNumber,
        IsCompleted = isCompleted,  // ← Mark if [x]
        OriginalPrefix = prefix
    });
}

// In TodoSyncService
var todo = new TodoItem
{
    Text = candidate.Text,
    IsCompleted = candidate.IsCompleted,  // ← Apply completion status
    CompletedDate = candidate.IsCompleted ? DateTime.UtcNow : null,
    CategoryId = noteCategoryId,
    SourceNoteId = noteGuid
};
```

---

### **PHASE 3: TEXT SELECTION METHODS (Priority 3)**

**Time to Implement:** 6-8 hours  
**Complexity:** Medium-High  
**Value:** ⭐⭐⭐⭐⭐

#### **Method 3A: Context Menu "Create Todo" (3 hours)**

**User Workflow:**
```
1. User selects text: "send weekly status report"
2. Right-click → Context menu appears
3. Click "Create Todo from Selection"
4. Todo created with selected text ✅
```

**Implementation:**

**Step 3A.1: Add Context Menu Command (1 hour)**

**Location:** RTF editor control or ViewModel

```csharp
public class RichTextEditorViewModel : ViewModelBase
{
    public ICommand CreateTodoFromSelectionCommand { get; private set; }
    
    private void InitializeCommands()
    {
        CreateTodoFromSelectionCommand = new AsyncRelayCommand(
            ExecuteCreateTodoFromSelection,
            CanCreateTodoFromSelection
        );
    }
    
    private async Task ExecuteCreateTodoFromSelection()
    {
        try
        {
            // Get selected text from RTF editor
            var selectedText = GetSelectedPlainText();
            
            if (string.IsNullOrWhiteSpace(selectedText))
                return;
            
            // Get current note context
            var currentNoteId = GetCurrentNoteId();
            var currentNotePath = GetCurrentNotePath();
            var categoryId = GetCurrentCategoryId();
            
            // Create todo
            var todoStore = _serviceProvider.GetService<ITodoStore>();
            var todo = new TodoItem
            {
                Text = selectedText.Trim(),
                CategoryId = categoryId,
                SourceNoteId = currentNoteId,
                SourceFilePath = currentNotePath,
                SourceLineNumber = GetSelectionLineNumber(),
                SourceCharOffset = GetSelectionCharOffset()
            };
            
            await todoStore.AddAsync(todo);
            
            _logger.Info($"✅ Todo created from selection: {todo.Text}");
            
            // Show success feedback
            ShowNotification("Todo created successfully!");
            
            // Optional: Insert bracket markup at selection
            if (_settings.InsertBracketAfterCreate)
            {
                InsertBracketMarkup(selectedText);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create todo from selection");
            ShowErrorNotification("Failed to create todo");
        }
    }
    
    private bool CanCreateTodoFromSelection()
    {
        // Enable if text is selected
        return !string.IsNullOrWhiteSpace(GetSelectedPlainText());
    }
    
    /// <summary>
    /// Get selected text as plain text (no RTF formatting)
    /// </summary>
    private string GetSelectedPlainText()
    {
        var rtfSelection = _richTextBox.Selection;
        if (rtfSelection == null || rtfSelection.IsEmpty)
            return string.Empty;
        
        var range = new TextRange(rtfSelection.Start, rtfSelection.End);
        return range.Text;
    }
    
    /// <summary>
    /// Get line number of current selection
    /// </summary>
    private int GetSelectionLineNumber()
    {
        var textBeforeSelection = new TextRange(
            _richTextBox.Document.ContentStart,
            _richTextBox.Selection.Start
        ).Text;
        
        return textBeforeSelection.Count(c => c == '\n');
    }
}
```

**Add to Context Menu XAML:**
```xml
<RichTextBox.ContextMenu>
    <ContextMenu>
        <MenuItem Header="Cut" Command="Cut"/>
        <MenuItem Header="Copy" Command="Copy"/>
        <MenuItem Header="Paste" Command="Paste"/>
        <Separator/>
        
        <!-- NEW: Create Todo from Selection -->
        <MenuItem Header="Create Todo from Selection"
                  Command="{Binding CreateTodoFromSelectionCommand}">
            <MenuItem.Icon>
                <ContentControl Template="{StaticResource LucideCheck}" 
                                Width="14" Height="14"/>
            </MenuItem.Icon>
        </MenuItem>
    </ContextMenu>
</RichTextBox.ContextMenu>
```

---

#### **Method 3B: Toolbar Button (1 hour)**

**User Workflow:**
```
1. User selects text
2. Clicks "➕ Add as Todo" button in toolbar
3. Todo created ✅
```

**Implementation:**

**Add to Toolbar XAML:**
```xml
<ToolBar>
    <!-- Existing formatting buttons -->
    <Button Content="B" Command="{Binding BoldCommand}"/>
    <Button Content="I" Command="{Binding ItalicCommand}"/>
    <Separator/>
    
    <!-- NEW: Add as Todo Button -->
    <Button Command="{Binding CreateTodoFromSelectionCommand}"
            ToolTip="Create Todo from Selection (Ctrl+Shift+T)">
        <StackPanel Orientation="Horizontal">
            <ContentControl Template="{StaticResource LucideCheck}" 
                            Width="14" Height="14" Margin="0,0,4,0"/>
            <TextBlock Text="Add as Todo"/>
        </StackPanel>
    </Button>
</ToolBar>
```

---

#### **Method 3C: Keyboard Shortcut (30 min)**

**User Workflow:**
```
1. User selects text
2. Presses Ctrl+Shift+T
3. Todo created ✅
```

**Implementation:**

**Add Input Binding:**
```xml
<RichTextBox.InputBindings>
    <KeyBinding Key="T" 
                Modifiers="Ctrl+Shift"
                Command="{Binding CreateTodoFromSelectionCommand}"/>
</RichTextBox.InputBindings>
```

---

#### **Method 3D: Enter Key Detection (2 hours)**

**User Workflow:**
```
User types: "TODO: send report"
User presses Enter
    ↓
Real-time extraction triggers
    ↓
Todo created immediately ✅
Optional: "TODO:" text removed or converted to "[todo text]"
```

**Implementation:**

**Hook KeyDown Event:**
```csharp
private void RichTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
{
    if (e.Key == Key.Enter)
    {
        // Get current line text
        var currentLine = GetCurrentLineText();
        
        // Check if matches TODO: pattern
        if (Regex.IsMatch(currentLine, @"^[\s]*TODO:\s*(.+)$", RegexOptions.IgnoreCase))
        {
            // Extract todo text
            var match = Regex.Match(currentLine, @"^[\s]*TODO:\s*(.+)$", RegexOptions.IgnoreCase);
            var todoText = match.Groups[1].Value.Trim();
            
            // Create todo asynchronously
            _ = CreateTodoFromCurrentLine(todoText);
            
            // Optional: Convert "TODO: " to "[todoText]" in note
            if (_settings.ConvertPrefixToBracket)
            {
                ReplaceTodoPrefix(currentLine, todoText);
            }
            
            // Optional: Show quick feedback (subtle highlight or badge)
            ShowQuickFeedback("Todo created");
        }
    }
}

private async Task CreateTodoFromCurrentLine(string todoText)
{
    try
    {
        var todo = new TodoItem
        {
            Text = todoText,
            CategoryId = GetCurrentCategoryId(),
            SourceNoteId = GetCurrentNoteId(),
            SourceFilePath = GetCurrentNotePath(),
            SourceLineNumber = GetCurrentLineNumber()
        };
        
        var todoStore = _serviceProvider.GetService<ITodoStore>();
        await todoStore.AddAsync(todo);
        
        _logger.Info($"✅ Real-time todo created: {todoText}");
    }
    catch (Exception ex)
    {
        _logger.Error(ex, "Failed to create real-time todo");
    }
}

/// <summary>
/// Replace "TODO: text" with "[text]" inline
/// </summary>
private void ReplaceTodoPrefix(string originalLine, string todoText)
{
    // Find and replace current line
    // Change: "TODO: send report" → "[send report]"
    
    var currentLineParagraph = GetCurrentParagraph();
    var newText = $"[{todoText}]";
    
    // Preserve formatting, just change text
    currentLineParagraph.Inlines.Clear();
    currentLineParagraph.Inlines.Add(new Run(newText));
}
```

**Settings:**
```csharp
public class TodoExtractionSettings
{
    public bool EnableBracketExtraction { get; set; } = true;
    public bool EnablePrefixExtraction { get; set; } = true;
    public bool ConvertPrefixToBracket { get; set; } = true;  // Auto-convert on Enter
    public bool InsertBracketAfterCreate { get; set; } = false; // From selection
    public bool RealTimeExtraction { get; set; } = true;  // On Enter vs on Save
}
```

---

### **PHASE 4: VISUAL FEEDBACK (Priority 4)**

**Time to Implement:** 8-12 hours  
**Complexity:** HIGH (RTF manipulation is complex)  
**Value:** ⭐⭐⭐⭐⭐

#### **Feature 4A: Highlight Extracted Todos (4-6 hours)**

**User Experience:**
```
Before:
Normal text [call John] more text

After:
Normal text [call John] more text
            ^^^^^^^^^^^ subtle yellow background
```

**Technical Challenge:** RTF formatting is HARD

**Approaches:**

**Approach A: TextPointer-based Highlighting (Recommended)**

```csharp
/// <summary>
/// Applies subtle background to todo brackets in RTF editor
/// Non-breaking: Preserves all user formatting
/// </summary>
public class TodoHighlighter
{
    private readonly RichTextBox _editor;
    private readonly IAppLogger _logger;
    
    /// <summary>
    /// Highlight all todo brackets in the document
    /// </summary>
    public async Task HighlightTodosAsync(List<TodoCandidate> todos)
    {
        try
        {
            foreach (var todo in todos)
            {
                // Find the TextPointer for this line/offset
                var start = GetTextPointerAtLine(todo.LineNumber);
                if (start == null) continue;
                
                // Find the bracket text
                var range = FindTextRange(start, todo.OriginalMatch);
                if (range == null) continue;
                
                // Apply subtle highlight (preserve existing formatting!)
                ApplyHighlight(range, GetHighlightColorForStatus(todo));
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to highlight todos");
            // Non-critical - app continues working
        }
    }
    
    /// <summary>
    /// Apply background color without breaking existing formatting
    /// </summary>
    private void ApplyHighlight(TextRange range, Color backgroundColor)
    {
        // CRITICAL: Use TextDecorations, not Background property
        // Background property breaks RTF formatting!
        
        var run = new Run(range.Text);
        run.Background = new SolidColorBrush(backgroundColor);
        
        // Replace range while preserving formatting
        range.Start.Paragraph.Inlines.InsertAfter(
            range.Start.Parent as Inline,
            run
        );
    }
    
    /// <summary>
    /// Get highlight color based on todo status
    /// </summary>
    private Color GetHighlightColorForStatus(TodoCandidate candidate)
    {
        // Query todo status from database
        var todo = FindTodoByCandidate(candidate);
        
        if (todo == null)
            return Colors.Yellow;  // Pending extraction
        
        if (todo.IsCompleted)
            return Colors.LightGreen;  // Completed
        
        if (todo.IsOverdue())
            return Colors.LightCoral;  // Overdue
        
        return Color.FromArgb(50, 255, 255, 0);  // Subtle yellow
    }
    
    /// <summary>
    /// Find TextPointer at specific line number
    /// </summary>
    private TextPointer GetTextPointerAtLine(int lineNumber)
    {
        var navigator = _editor.Document.ContentStart;
        int currentLine = 0;
        
        while (navigator != null && currentLine < lineNumber)
        {
            var nextLine = navigator.GetLineStartPosition(1);
            if (nextLine == null) break;
            
            navigator = nextLine;
            currentLine++;
        }
        
        return navigator;
    }
}
```

**Challenges:**
- RTF formatting is complex (nested runs, paragraphs)
- Must preserve user formatting (bold, italic, colors)
- TextPointer navigation is tricky
- Performance with large documents

**Alternative Approach:** Use Adorner Layer (non-intrusive overlay)

```csharp
/// <summary>
/// Uses WPF Adorner layer to highlight without touching RTF
/// More complex but safer for RTF preservation
/// </summary>
public class TodoAdorner : Adorner
{
    private List<Rect> _todoRectangles;
    
    protected override void OnRender(DrawingContext drawingContext)
    {
        foreach (var rect in _todoRectangles)
        {
            var brush = new SolidColorBrush(Colors.Yellow) { Opacity = 0.3 };
            drawingContext.DrawRectangle(brush, null, rect);
        }
    }
}
```

**Recommendation:** Start with simple approach, enhance if needed

---

#### **Feature 4B: Tooltip on Hover (2-3 hours)**

**User Experience:**
```
User hovers over: [call John]
    ↓
Tooltip appears:
┌─────────────────────────────┐
│ 📋 Todo: call John          │
│ ✅ Status: Incomplete       │
│ 📅 Due: Tomorrow            │
│ 📂 Category: Work/ProjectA  │
│ 🔗 Click to view in panel   │
└─────────────────────────────┘
```

**Implementation:**

```csharp
/// <summary>
/// Provides rich tooltips for todo brackets
/// </summary>
public class TodoTooltipProvider
{
    private readonly ITodoStore _todoStore;
    private readonly ICategorySyncService _categorySync;
    
    /// <summary>
    /// Generate tooltip content for todo candidate
    /// </summary>
    public ToolTip CreateTooltip(TodoCandidate candidate, Guid noteId)
    {
        // Find matching todo in database
        var todo = FindTodoBySourceLocation(noteId, candidate.LineNumber, candidate.Text);
        
        if (todo == null)
        {
            return CreatePendingTooltip(candidate);
        }
        
        return CreateTodoTooltip(todo);
    }
    
    private ToolTip CreateTodoTooltip(TodoItem todo)
    {
        var tooltip = new ToolTip();
        var panel = new StackPanel { Margin = new Thickness(8) };
        
        // Title
        panel.Children.Add(new TextBlock
        {
            Text = $"📋 {todo.Text}",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        
        // Status
        var status = todo.IsCompleted ? "✅ Completed" : "⏳ Incomplete";
        panel.Children.Add(new TextBlock { Text = status });
        
        // Due date (if set)
        if (todo.DueDate.HasValue)
        {
            var dueText = $"📅 Due: {FormatDueDate(todo.DueDate.Value)}";
            var dueBlock = new TextBlock { Text = dueText };
            
            if (todo.IsOverdue())
                dueBlock.Foreground = Brushes.Red;
                
            panel.Children.Add(dueBlock);
        }
        
        // Category
        if (todo.CategoryId.HasValue)
        {
            var category = _categorySync.GetCategoryByIdAsync(todo.CategoryId.Value).Result;
            if (category != null)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"📂 {category.Name}"
                });
            }
        }
        
        // Action hint
        panel.Children.Add(new TextBlock
        {
            Text = "🔗 Click to view in panel",
            FontStyle = FontStyles.Italic,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 4, 0, 0)
        });
        
        tooltip.Content = panel;
        return tooltip;
    }
    
    private string FormatDueDate(DateTime dueDate)
    {
        var days = (dueDate.Date - DateTime.Today).Days;
        
        if (days == 0) return "Today";
        if (days == 1) return "Tomorrow";
        if (days == -1) return "Yesterday";
        if (days < 0) return $"{Math.Abs(days)} days overdue";
        if (days <= 7) return $"In {days} days";
        
        return dueDate.ToShortDateString();
    }
}
```

**Hook Mouse Hover:**
```csharp
private void RichTextBox_MouseMove(object sender, MouseEventArgs e)
{
    var position = e.GetPosition(_richTextBox);
    var pointer = _richTextBox.GetPositionFromPoint(position);
    
    if (pointer == null) return;
    
    // Check if hovering over todo bracket
    var todoCandidate = FindTodoAtPointer(pointer);
    
    if (todoCandidate != null)
    {
        // Show tooltip
        var tooltip = _tooltipProvider.CreateTooltip(todoCandidate, _currentNoteId);
        _richTextBox.ToolTip = tooltip;
        
        // Change cursor
        _richTextBox.Cursor = Cursors.Hand;
    }
    else
    {
        _richTextBox.ToolTip = null;
        _richTextBox.Cursor = Cursors.IBeam;
    }
}
```

---

#### **Feature 4C: Click to Navigate (1-2 hours)**

**User Experience:**
```
User clicks on: [call John]
    ↓
Todo panel opens
    ↓
Todo "call John" is selected and scrolled into view ✅
```

**Implementation:**

```csharp
private void RichTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
{
    // Only handle left click
    if (e.LeftButton != MouseButtonState.Pressed)
        return;
    
    var position = e.GetPosition(_richTextBox);
    var pointer = _richTextBox.GetPositionFromPoint(position);
    
    if (pointer == null) return;
    
    // Check if clicking on todo bracket
    var todoCandidate = FindTodoAtPointer(pointer);
    
    if (todoCandidate != null)
    {
        // Navigate to todo panel
        await NavigateToTodoInPanel(todoCandidate);
        
        e.Handled = true; // Don't let click select text
    }
}

private async Task NavigateToTodoInPanel(TodoCandidate candidate)
{
    try
    {
        // Open todo panel
        var mainShell = _serviceProvider.GetService<MainShellViewModel>();
        mainShell.ActivateTodoPanel();
        
        // Find todo in store
        var todoStore = _serviceProvider.GetService<ITodoStore>();
        var todo = FindTodoByCandidate(candidate);
        
        if (todo != null)
        {
            // Select and scroll to todo
            var todoPanel = _serviceProvider.GetService<TodoListViewModel>();
            todoPanel.SelectAndScrollTo(todo.Id);
            
            _logger.Info($"Navigated to todo: {todo.Text}");
        }
    }
    catch (Exception ex)
    {
        _logger.Error(ex, "Failed to navigate to todo");
    }
}
```

**TodoListViewModel Enhancement:**
```csharp
public class TodoListViewModel
{
    /// <summary>
    /// Select todo and scroll it into view
    /// </summary>
    public void SelectAndScrollTo(Guid todoId)
    {
        var todoVm = Todos.FirstOrDefault(t => t.Id == todoId);
        if (todoVm == null) return;
        
        // Select
        SelectedTodo = todoVm;
        
        // Scroll into view (requires ItemsControl reference)
        ScrollIntoView(todoVm);
        
        // Optional: Flash highlight
        todoVm.FlashHighlight();
    }
    
    private void ScrollIntoView(TodoItemViewModel todo)
    {
        // Raise event for view to handle
        RequestScrollIntoView?.Invoke(todo);
    }
}

// In TodoPanelView.xaml.cs
private void OnRequestScrollIntoView(TodoItemViewModel todo)
{
    var container = TodoListControl.ItemContainerGenerator.ContainerFromItem(todo);
    if (container is FrameworkElement element)
    {
        element.BringIntoView();
    }
}
```

---

#### **Feature 4D: Completion Sync (2-3 hours)**

**User Experience:**
```
User completes todo in panel
    ↓
Bracket in note updates: [call John] → [x] call John
    (or subtle strikethrough)
```

**Implementation:**

```csharp
/// <summary>
/// Sync todo completion status back to note
/// </summary>
public class TodoNoteSync
{
    /// <summary>
    /// Update bracket appearance when todo completed
    /// </summary>
    public async Task UpdateBracketStatus(TodoItem todo)
    {
        if (!todo.SourceNoteId.HasValue || string.IsNullOrEmpty(todo.SourceFilePath))
            return;
        
        try
        {
            // Read RTF file
            var rtfContent = await File.ReadAllTextAsync(todo.SourceFilePath);
            
            // Find bracket at source location
            var lines = rtfContent.Split('\n');
            if (todo.SourceLineNumber >= lines.Length)
                return;
            
            var line = lines[todo.SourceLineNumber.Value];
            
            // Option A: Convert [todo] → [x] todo
            var updated = Regex.Replace(line, 
                @"\[" + Regex.Escape(todo.Text) + @"\]",
                $"[x] {todo.Text}");
            
            // Option B: Add strikethrough formatting (complex in RTF!)
            // Requires RTF manipulation
            
            // Write back (if changed)
            if (line != updated)
            {
                lines[todo.SourceLineNumber.Value] = updated;
                var newRtf = string.Join('\n', lines);
                await File.WriteAllTextAsync(todo.SourceFilePath, newRtf);
                
                _logger.Info($"Updated bracket in note for completed todo: {todo.Text}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update bracket status in note");
            // Non-critical - just log error
        }
    }
}
```

**Challenge:** Modifying RTF without breaking formatting is HARD

**Recommendation:** Start simple (text replacement), enhance later

---

#### **Feature 4E: Status Indicators (1-2 hours)**

**Visual Design:**

```
[call John]              - Pending (yellow background)
[x] send report          - Completed (green background, strikethrough)
[!] overdue task         - Overdue (red background)
[⭐] important meeting   - Favorite (gold star)
```

**Implementation:**

```csharp
public enum TodoIndicator
{
    None,           // No special indicator
    Pending,        // [ ] or [todo]
    Completed,      // [x] or [✓]
    Overdue,        // [!]
    Important       // [⭐] or [*]
}

// Parse indicator from bracket
private TodoIndicator ParseIndicator(string bracketContent)
{
    if (bracketContent.StartsWith("x") || bracketContent.StartsWith("✓"))
        return TodoIndicator.Completed;
    
    if (bracketContent.StartsWith("!"))
        return TodoIndicator.Overdue;
    
    if (bracketContent.StartsWith("⭐") || bracketContent.StartsWith("*"))
        return TodoIndicator.Important;
    
    return TodoIndicator.Pending;
}

// Apply visual style
private Brush GetBackgroundBrush(TodoIndicator indicator)
{
    return indicator switch
    {
        TodoIndicator.Completed => new SolidColorBrush(Color.FromArgb(40, 0, 255, 0)),
        TodoIndicator.Overdue => new SolidColorBrush(Color.FromArgb(40, 255, 0, 0)),
        TodoIndicator.Important => new SolidColorBrush(Color.FromArgb(40, 255, 215, 0)),
        _ => new SolidColorBrush(Color.FromArgb(40, 255, 255, 0))
    };
}
```

---

### **PHASE 5: ADVANCED FEATURES (Priority 5 - Future)**

**Time:** 12-20 hours  
**When:** After basic functionality validated by users

#### **Feature 5A: Quick Add Dialog (3 hours)**

**Keyboard Shortcut:** Ctrl+Shift+N (New Todo)

**Dialog:**
```
┌─────────────────────────────────────────┐
│ 📋 Create Todo                          │
├─────────────────────────────────────────┤
│ Text: [________________________]         │
│                                          │
│ Category: [Work > ProjectA     ▼]       │
│ Due Date: [Tomorrow           ▼]       │
│ Priority: [⭐ Normal           ▼]       │
│                                          │
│ ☐ Link to current note                  │
│                                          │
│        [Cancel]  [Create Todo]          │
└─────────────────────────────────────────┘
```

---

#### **Feature 5B: Smart Extraction (4-6 hours)**

**Natural Language Processing:**

```
User types: "Call John tomorrow about the project budget"
    ↓
Parser extracts:
├── Text: "Call John about the project budget"
├── Due Date: Tomorrow (auto-detected)
└── Keywords: "budget" → Tag suggestion
```

**Implementation:**
```csharp
public class SmartTodoExtractor
{
    public TodoCandidate ExtractSmart(string text)
    {
        var candidate = new TodoCandidate { Text = text };
        
        // Extract due date
        var dueDateMatch = Regex.Match(text, 
            @"\b(today|tomorrow|next week|monday|tuesday)\b",
            RegexOptions.IgnoreCase);
        
        if (dueDateMatch.Success)
        {
            candidate.SuggestedDueDate = ParseRelativeDate(dueDateMatch.Value);
            // Remove date from text
            candidate.Text = text.Replace(dueDateMatch.Value, "").Trim();
        }
        
        // Extract priority
        if (text.Contains("urgent", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("ASAP", StringComparison.OrdinalIgnoreCase))
        {
            candidate.SuggestedPriority = Priority.Urgent;
        }
        
        // Extract tags (hashtags or keywords)
        var tags = Regex.Matches(text, @"#(\w+)")
            .Select(m => m.Groups[1].Value)
            .ToList();
        
        candidate.SuggestedTags = tags;
        
        return candidate;
    }
}
```

---

#### **Feature 5C: Bi-Directional Sync (6-8 hours)**

**Workflow:**
```
Scenario 1: Complete in Panel
User checks todo in panel
    ↓
Note updates: [todo] → [x] todo ✅

Scenario 2: Complete in Note
User types: [x] before existing [todo]
    ↓
Save note
    ↓
Todo marked complete in panel ✅
```

**Complexity:** HIGH (RTF modification is risky)

**Recommendation:** Phase 6 or 7, after core functionality validated

---

## 📐 IMPLEMENTATION PHASES (Priority Order)

### **MVP: Brackets Only (CURRENT - Week 1)**

```
✅ BracketTodoParser exists
✅ TodoSyncService exists
⏳ Test extraction works
⏳ Test category integration
⏳ Basic functionality validated
```

**Time:** 1 hour testing  
**Priority:** 🔴 DO THIS FIRST

---

### **Phase 2: Multiple Input Methods (Week 2)**

```
1. TODO: prefix parser (2 hrs)
2. Composite parser (1 hr)
3. Context menu selection (3 hrs)
4. Toolbar button (1 hr)
5. Keyboard shortcut (30 min)
6. Enter key detection (2 hrs)
```

**Time:** 9-10 hours  
**Priority:** 🟠 After brackets validated

---

### **Phase 3: Basic Visual Feedback (Week 3)**

```
1. Subtle highlighting (4 hrs)
2. Basic tooltip (2 hrs)
3. Click navigation (2 hrs)
```

**Time:** 8 hours  
**Priority:** 🟡 After input methods work

---

### **Phase 4: Advanced Visual Feedback (Week 4)**

```
1. Status-based colors (2 hrs)
2. Rich tooltips (2 hrs)
3. Completion indicators (3 hrs)
4. Adorner layer (if needed) (4 hrs)
```

**Time:** 11 hours  
**Priority:** 🟡 Polish phase

---

### **Phase 5: Advanced Features (Month 2+)**

```
1. Quick add dialog (3 hrs)
2. Smart extraction (6 hrs)
3. Bi-directional sync (8 hrs)
4. Performance optimization (4 hrs)
```

**Time:** 21 hours  
**Priority:** 🟢 Future enhancements

---

## 🎯 RECOMMENDED IMPLEMENTATION ORDER

### **For New Chat / Developer:**

**Week 1: Validate Current (1 hour)**
```
✅ Test bracket extraction
✅ Test sync service  
✅ Verify todo creation
✅ Document what works
```

**Week 2: Category Integration (6-7 hours)**
```
✅ Sync categories with tree (from previous plan)
✅ Context menu integration
✅ Auto-categorization
✅ Test thoroughly
```

**Week 3: Expand Input Methods (10 hours)**
```
✅ TODO: prefix parser
✅ Text selection → todo
✅ Toolbar button
✅ Keyboard shortcuts
✅ Real-time extraction (Enter key)
```

**Week 4: Visual Feedback (8 hours)**
```
✅ Basic highlighting
✅ Tooltips
✅ Click navigation
✅ Status colors
```

**Week 5: Testing & Polish (6 hours)**
```
✅ Comprehensive tests
✅ Performance optimization
✅ Error handling
✅ Documentation
```

**Month 2+: Advanced Features (As needed)**
```
⏳ Smart extraction (NLP)
⏳ Bi-directional sync
⏳ Rich visual indicators
```

---

## 🔧 TECHNICAL IMPLEMENTATION DETAILS

### **RTF Highlighting Without Breaking Formatting:**

**Challenge:** RTF documents have complex formatting

**Solution: Use TextDecorations Property (Safe)**

```csharp
/// <summary>
/// Apply highlight using TextElement properties (RTF-safe)
/// </summary>
public void HighlightTodoSafely(TextRange range, TodoStatus status)
{
    // SAFE: These properties don't break RTF
    range.ApplyPropertyValue(
        TextElement.BackgroundProperty,
        GetBackgroundBrush(status)
    );
    
    // For completed: Add strikethrough
    if (status == TodoStatus.Completed)
    {
        range.ApplyPropertyValue(
            Inline.TextDecorationsProperty,
            TextDecorations.Strikethrough
        );
    }
}
```

**AVOID:**
```csharp
// ❌ DON'T DO THIS - Breaks RTF formatting!
var run = new Run(range.Text);
run.Background = Brushes.Yellow;
range.Start.InsertTextInRun(run.Text);  // Loses formatting
```

**DO THIS:**
```csharp
// ✅ DO THIS - Preserves RTF formatting
range.ApplyPropertyValue(
    TextElement.BackgroundProperty,
    new SolidColorBrush(Colors.Yellow)
);
// Existing runs, formatting, colors all preserved ✅
```

---

### **Performance Optimization:**

**For Large Documents:**

```csharp
/// <summary>
/// Incremental highlighting (only visible portion)
/// </summary>
public class IncrementalTodoHighlighter
{
    private Rect _visibleRect;
    
    public void UpdateVisibleRegion(Rect newVisibleRect)
    {
        _visibleRect = newVisibleRect;
        HighlightVisibleTodos(); // Only highlight what's on screen
    }
    
    private void HighlightVisibleTodos()
    {
        foreach (var todo in _todos)
        {
            if (IsInVisibleRegion(todo))
            {
                ApplyHighlight(todo);
            }
            else
            {
                RemoveHighlight(todo); // Save memory
            }
        }
    }
}
```

**Debouncing:**
```csharp
// Don't re-highlight on every character typed
private Timer _highlightDebounceTimer;

private void OnTextChanged(object sender, TextChangedEventArgs e)
{
    // Reset timer
    _highlightDebounceTimer?.Stop();
    _highlightDebounceTimer = new Timer(500); // 500ms delay
    _highlightDebounceTimer.Elapsed += (s, e) => 
    {
        Dispatcher.Invoke(() => RefreshHighlights());
    };
    _highlightDebounceTimer.Start();
}
```

---

### **Robustness Patterns:**

**1. Non-Breaking Errors:**
```csharp
public async Task HighlightTodosAsync()
{
    try
    {
        // Attempt highlighting
        ApplyHighlights();
    }
    catch (Exception ex)
    {
        _logger.Error(ex, "Highlighting failed - continuing without highlights");
        // App continues working, just no visual feedback
    }
}
```

**2. Graceful Degradation:**
```csharp
if (_highlightingSupported)
{
    HighlightTodos(); // Visual enhancement
}
// App works without highlighting if feature unavailable
```

**3. Validation:**
```csharp
private bool IsValidTodoRange(TextRange range)
{
    return range != null && 
           !range.IsEmpty && 
           range.Start != null && 
           range.End != null &&
           range.Start.CompareTo(range.End) < 0;
}
```

---

## 📊 ARCHITECTURE DIAGRAM

### **Complete RTF Todo System:**

```
┌─────────────────────────────────────────────────────┐
│             RTF EDITOR LAYER                        │
├─────────────────────────────────────────────────────┤
│                                                      │
│  RichTextBox                                        │
│  ├── KeyDown (Enter detection)                     │
│  ├── MouseMove (Tooltip)                           │
│  ├── MouseClick (Navigation)                       │
│  └── TextChanged (Highlighting)                    │
│                  ↓                                   │
│  ┌────────────────────────────────────────────┐    │
│  │   TodoHighlighter (Visual Feedback)         │    │
│  │   TodoTooltipProvider (Status Display)      │    │
│  │   TodoNavigator (Click to Panel)            │    │
│  └────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────┘
                     ↓
┌─────────────────────────────────────────────────────┐
│          PARSING & EXTRACTION LAYER                 │
├─────────────────────────────────────────────────────┤
│                                                      │
│  CompositeTodoParser                                │
│  ├── BracketTodoParser ([text])                    │
│  ├── TodoPrefixParser (TODO: text)                 │
│  ├── CheckboxParser ([ ] text)                     │
│  └── SelectionParser (from UI command)             │
│                  ↓                                   │
│  TodoCandidate (extracted data)                     │
│  ├── Text, LineNumber, CharOffset                  │
│  ├── Confidence, IsCompleted                       │
│  └── SuggestedCategory, Tags, DueDate             │
└─────────────────────────────────────────────────────┘
                     ↓
┌─────────────────────────────────────────────────────┐
│            SYNC & PERSISTENCE LAYER                 │
├─────────────────────────────────────────────────────┤
│                                                      │
│  TodoSyncService (IHostedService)                   │
│  ├── OnNoteSaved event handler                     │
│  ├── Debouncing (500ms)                            │
│  ├── Auto-categorization (note location)           │
│  └── Reconciliation logic                          │
│                  ↓                                   │
│  TodoRepository → todos.db                          │
│  ├── Insert/Update/Delete                          │
│  └── Source tracking (note_id, line, offset)       │
└─────────────────────────────────────────────────────┘
                     ↓
┌─────────────────────────────────────────────────────┐
│              TODO PANEL LAYER                       │
├─────────────────────────────────────────────────────┤
│                                                      │
│  TodoListViewModel                                  │
│  ├── Observable todos collection                   │
│  ├── SelectAndScrollTo(todoId)                     │
│  └── Category filtering                            │
│                  ↓                                   │
│  TodoPanelView (UI)                                │
│  └── Displays todos, categories, smart lists       │
└─────────────────────────────────────────────────────┘
                     ↕
              [Events & Navigation]
                     ↕
            RichTextBox (highlights, clicks)
```

---

## 🎯 DESIGN PATTERNS APPLIED

### **1. Strategy Pattern (Multiple Parsers)**
```csharp
interface ITodoParser
{
    List<TodoCandidate> Extract(string text);
}

class BracketTodoParser : ITodoParser { ... }
class TodoPrefixParser : ITodoParser { ... }
class CompositeTodoParser // Combines strategies
```

### **2. Observer Pattern (Events)**
```csharp
ISaveManager.NoteSaved event
    ↓
TodoSyncService listens
    ↓
Extracts todos
    ↓
Raises TodoExtracted event
    ↓
TodoHighlighter updates visual feedback
```

### **3. Command Pattern (UI Actions)**
```csharp
CreateTodoFromSelectionCommand
NavigateToTodoCommand
HighlightTodosCommand
└── Encapsulated, testable, undoable
```

### **4. Decorator Pattern (Highlighting)**
```csharp
RichTextBox (base)
    ↓
TodoHighlightDecorator (adds highlighting)
    ↓
TodoTooltipDecorator (adds tooltips)
    ↓
TodoNavigationDecorator (adds click handling)
```

---

## 📋 TESTING STRATEGY FOR RTF FEATURES

### **Unit Tests:**

```csharp
[TestFixture]
public class BracketTodoParserTests
{
    [Test]
    public void Extract_SimpleBracket_Success()
    {
        var text = "[call John]";
        var todos = _parser.ExtractFromPlainText(text);
        Assert.AreEqual(1, todos.Count);
        Assert.AreEqual("call John", todos[0].Text);
    }
    
    [Test]
    public void Extract_MultipleBrackets_ExtractsAll()
    {
        var text = "[todo 1] and [todo 2] and [todo 3]";
        var todos = _parser.ExtractFromPlainText(text);
        Assert.AreEqual(3, todos.Count);
    }
}

[TestFixture]
public class TodoPrefixParserTests
{
    [Test]
    public void Extract_TodoPrefix_Success()
    {
        var text = "TODO: send report";
        var todos = _parser.ExtractFromPlainText(text);
        Assert.AreEqual(1, todos.Count);
        Assert.AreEqual("send report", todos[0].Text);
    }
    
    [Test]
    public void Extract_CompletedCheckbox_MarksCompleted()
    {
        var text = "[x] completed task";
        var todos = _parser.ExtractFromPlainText(text);
        Assert.IsTrue(todos[0].IsCompleted);
    }
}

[TestFixture]
public class CompositeTodoParserTests
{
    [Test]
    public void Extract_MixedFormats_ExtractsAll()
    {
        var text = @"
            [bracket todo]
            TODO: prefix todo
            [ ] checkbox todo
        ";
        var todos = _compositeParser.ExtractFromPlainText(text);
        Assert.AreEqual(3, todos.Count);
    }
    
    [Test]
    public void Extract_Duplicates_Deduplicates()
    {
        var text = "[todo] TODO: todo"; // Same line, same text
        var todos = _compositeParser.ExtractFromPlainText(text);
        Assert.AreEqual(1, todos.Count); // Deduplicated
    }
}
```

---

### **Integration Tests:**

```csharp
[TestFixture]
public class RtfExtractionIntegrationTests
{
    [Test]
    public async Task SaveNote_WithBracket_CreatesTodo()
    {
        // Arrange
        var rtfContent = "{\\rtf1 Meeting [call John]}";
        var noteId = Guid.NewGuid();
        await SaveNote(noteId, rtfContent);
        
        // Act
        await TriggerSyncService(noteId, rtfContent);
        
        // Assert
        var todos = await _todoRepo.GetByNoteIdAsync(noteId);
        Assert.AreEqual(1, todos.Count);
        Assert.AreEqual("call John", todos[0].Text);
        Assert.AreEqual(noteId, todos[0].SourceNoteId);
    }
    
    [Test]
    public async Task UpdateNote_RemoveBracket_MarkOrphaned()
    {
        // Arrange: Create todo from bracket
        var rtfV1 = "{\\rtf1 [call John]}";
        var noteId = Guid.NewGuid();
        await SaveAndSync(noteId, rtfV1);
        
        // Act: Remove bracket and save
        var rtfV2 = "{\\rtf1 Meeting notes}";
        await SaveAndSync(noteId, rtfV2);
        
        // Assert: Todo marked orphaned (not deleted)
        var todos = await _todoRepo.GetByNoteIdAsync(noteId);
        Assert.AreEqual(1, todos.Count);
        Assert.IsTrue(todos[0].IsOrphaned);
    }
}
```

---

### **UI Tests (Manual initially, then automated):**

```
Test Script 1: Basic Extraction
├── Open note
├── Type "[call John]"
├── Save (Ctrl+S)
├── Open Todo panel
└── ✅ Verify: Todo appears

Test Script 2: Multiple Methods
├── Type "TODO: send report"
├── Press Enter
├── Select "review budget"
├── Right-click → "Create Todo"
└── ✅ Verify: 2 todos created

Test Script 3: Visual Feedback
├── Note with existing brackets
├── Hover over bracket
└── ✅ Verify: Tooltip shows status

Test Script 4: Navigation
├── Click on bracket in note
└── ✅ Verify: Todo panel opens, todo selected

Test Script 5: Completion Sync
├── Complete todo in panel
├── Return to note
└── ✅ Verify: Bracket shows completion (if implemented)
```

---

## ⚠️ CHALLENGES & SOLUTIONS

### **Challenge 1: RTF Manipulation is Fragile**

**Problem:** Modifying RTF can break formatting

**Solutions:**
1. **Read-Only Visual Feedback** (Phase 1)
   - Highlight using TextElement properties (safe)
   - Don't modify underlying RTF structure
   - Highlighting is ephemeral (recalculated on load)

2. **Separate Formatting Layer** (Phase 2)
   - Use Adorner layer (overlay, doesn't touch RTF)
   - WPF Adorners render on top of content
   - Zero risk to user's formatting

3. **Metadata-Based Approach** (Phase 3)
   - Store todo locations in memory
   - Recalculate positions on document load
   - Apply highlighting dynamically

**Recommendation:** Start with TextElement properties (safe, simple)

---

### **Challenge 2: Finding Text in RTF**

**Problem:** TextPointer navigation is complex

**Solution: Build Helper Service**

```csharp
/// <summary>
/// Helps locate text ranges in FlowDocument for highlighting
/// </summary>
public class RtfTextLocator
{
    /// <summary>
    /// Find all occurrences of pattern in document
    /// Returns TextRange for each match
    /// </summary>
    public List<TextRange> FindAll(FlowDocument document, string pattern)
    {
        var ranges = new List<TextRange>();
        var start = document.ContentStart;
        
        while (start != null)
        {
            var text = new TextRange(start, document.ContentEnd).Text;
            var index = text.IndexOf(pattern);
            
            if (index == -1) break;
            
            var matchStart = start.GetPositionAtOffset(index);
            var matchEnd = matchStart?.GetPositionAtOffset(pattern.Length);
            
            if (matchStart != null && matchEnd != null)
            {
                ranges.Add(new TextRange(matchStart, matchEnd));
                start = matchEnd;
            }
            else
            {
                break;
            }
        }
        
        return ranges;
    }
    
    /// <summary>
    /// Find text at specific line number
    /// </summary>
    public TextRange FindAtLine(FlowDocument document, int lineNumber)
    {
        var navigator = document.ContentStart;
        int currentLine = 0;
        
        while (navigator != null && currentLine < lineNumber)
        {
            navigator = navigator.GetLineStartPosition(1);
            currentLine++;
        }
        
        if (navigator != null)
        {
            var lineEnd = navigator.GetLineStartPosition(1) ?? document.ContentEnd;
            return new TextRange(navigator, lineEnd);
        }
        
        return null;
    }
}
```

---

### **Challenge 3: Performance with Large Notes**

**Problem:** 1000+ line notes with 50+ todos might lag

**Solutions:**

**Lazy Highlighting:**
```csharp
// Only highlight when note is visible
public void OnNoteOpened(Guid noteId)
{
    if (_currentNoteId != noteId)
    {
        ClearHighlights(); // Remove old highlights
        _currentNoteId = noteId;
        await HighlightCurrentNote(); // Highlight new note
    }
}
```

**Incremental Parsing:**
```csharp
// Parse visible portion first, rest in background
public async Task ParseIncrementallyAsync(string rtfContent)
{
    // Parse first 500 lines (visible)
    var visibleTodos = ParseLines(rtfContent, 0, 500);
    ApplyHighlights(visibleTodos);
    
    // Parse rest in background
    await Task.Run(() =>
    {
        var remainingTodos = ParseLines(rtfContent, 500, int.MaxValue);
        Dispatcher.Invoke(() => ApplyHighlights(remainingTodos));
    });
}
```

**Caching:**
```csharp
// Cache parsed todos per note
private Dictionary<Guid, List<TodoCandidate>> _parsedTodosCache = new();

public List<TodoCandidate> GetTodosForNote(Guid noteId, string rtfContent)
{
    if (_parsedTodosCache.TryGetValue(noteId, out var cached))
        return cached;
    
    var todos = _parser.ExtractFromRtf(rtfContent);
    _parsedTodosCache[noteId] = todos;
    return todos;
}

// Invalidate cache on note save
public void OnNoteSaved(Guid noteId)
{
    _parsedTodosCache.Remove(noteId);
}
```

---

## 🎯 SETTINGS & CONFIGURATION

### **User Preferences:**

```csharp
public class TodoExtractionSettings
{
    // Extraction Methods
    public bool EnableBracketExtraction { get; set; } = true;
    public bool EnablePrefixExtraction { get; set; } = true;
    public bool EnableCheckboxExtraction { get; set; } = true;
    
    // Behavior
    public bool AutoExtractOnSave { get; set; } = true;
    public bool AutoExtractOnEnter { get; set; } = false;  // More aggressive
    public bool ConvertPrefixToBracket { get; set; } = true;
    
    // Visual Feedback
    public bool HighlightTodosInNotes { get; set; } = true;
    public bool ShowTooltipsOnHover { get; set; } = true;
    public bool EnableClickNavigation { get; set; } = true;
    
    // Colors
    public Color PendingTodoColor { get; set; } = Color.FromArgb(40, 255, 255, 0);
    public Color CompletedTodoColor { get; set; } = Color.FromArgb(40, 0, 255, 0);
    public Color OverdueTodoColor { get; set; } = Color.FromArgb(40, 255, 0, 0);
    
    // Advanced
    public int DebounceDelayMs { get; set; } = 500;
    public bool ShowExtractionNotifications { get; set; } = true;
    public bool AutoCategorizeFromNoteLocation { get; set; } = true;
}
```

**Settings UI:**
```xml
<TabItem Header="Todo Extraction">
    <StackPanel Margin="12">
        <TextBlock Text="Extraction Methods" FontWeight="Bold"/>
        <CheckBox Content="Extract from [brackets]" 
                  IsChecked="{Binding EnableBracketExtraction}"/>
        <CheckBox Content="Extract from TODO: prefix" 
                  IsChecked="{Binding EnablePrefixExtraction}"/>
        
        <TextBlock Text="Visual Feedback" FontWeight="Bold" Margin="0,12,0,0"/>
        <CheckBox Content="Highlight todos in notes" 
                  IsChecked="{Binding HighlightTodosInNotes}"/>
        <CheckBox Content="Show tooltips on hover" 
                  IsChecked="{Binding ShowTooltipsOnHover}"/>
        <CheckBox Content="Click to navigate to todo panel" 
                  IsChecked="{Binding EnableClickNavigation}"/>
    </StackPanel>
</TabItem>
```

---

## 📊 IMPLEMENTATION ESTIMATES

### **Core Functionality (Week 1-2):**

| Feature | Time | Complexity | Priority |
|---------|------|------------|----------|
| **Test brackets** | 1 hr | Low | 🔴 Critical |
| **TODO: prefix** | 4 hrs | Medium | 🟠 High |
| **Composite parser** | 2 hrs | Low | 🟠 High |
| **Context menu todo** | 3 hrs | Medium | 🟡 Medium |
| **Toolbar button** | 1 hr | Low | 🟡 Medium |
| **Keyboard shortcut** | 30 min | Low | 🟡 Medium |
| **Enter detection** | 2 hrs | Medium | 🟡 Medium |

**Subtotal:** ~13 hours

---

### **Visual Feedback (Week 3-4):**

| Feature | Time | Complexity | Priority |
|---------|------|------------|----------|
| **Basic highlighting** | 4 hrs | High | 🟡 Medium |
| **Tooltips** | 3 hrs | Medium | 🟡 Medium |
| **Click navigation** | 2 hrs | Medium | 🟡 Medium |
| **Status colors** | 2 hrs | Medium | 🟢 Low |
| **RTF-safe implementation** | 3 hrs | High | 🟡 Medium |

**Subtotal:** ~14 hours

---

### **Advanced Features (Month 2+):**

| Feature | Time | Complexity | Priority |
|---------|------|------------|----------|
| **Bi-directional sync** | 8 hrs | High | 🟢 Future |
| **Smart extraction (NLP)** | 6 hrs | High | 🟢 Future |
| **Quick add dialog** | 3 hrs | Medium | 🟢 Future |
| **Adorner layer** | 4 hrs | High | 🟢 Future |
| **Performance optimization** | 4 hrs | Medium | 🟢 Future |

**Subtotal:** ~25 hours

---

### **TOTAL EFFORT:**
- **MVP (Brackets + Prefix):** 13 hours
- **Visual Feedback:** 14 hours
- **Advanced Features:** 25 hours
- **Grand Total:** ~52 hours for complete system

---

## ✅ RECOMMENDED ROADMAP

### **Week 1: Core Extraction**
```
Day 1: Test brackets (1 hr)
Day 2: TODO: prefix parser (4 hrs)
Day 3: Composite parser (2 hrs) + Text selection (3 hrs)
Day 4: Toolbar + shortcuts (2 hrs)
Day 5: Testing (3 hrs)
```

**Deliverable:** Multiple ways to create todos ✅

---

### **Week 2: Visual Feedback**
```
Day 1-2: Basic highlighting (4 hrs) + RTF safety (3 hrs)
Day 3: Tooltips (3 hrs)
Day 4: Click navigation (2 hrs)
Day 5: Status colors + testing (4 hrs)
```

**Deliverable:** Rich visual feedback ✅

---

### **Month 2+: Advanced** (If users love it)
```
Week 5: Bi-directional sync (8 hrs)
Week 6: Smart extraction (6 hrs)
Week 7: Polish + optimization (8 hrs)
```

**Deliverable:** Production-grade RTF integration ✅

---

## 🎯 SUCCESS CRITERIA

### **Phase 1 (Brackets - MVP):**
- ✅ `[text]` extracts todo
- ✅ Saves on note save
- ✅ Auto-categorizes
- ✅ No crashes or data loss

### **Phase 2 (Multiple Methods):**
- ✅ TODO: prefix works
- ✅ Text selection → todo
- ✅ Toolbar button works
- ✅ Keyboard shortcut works
- ✅ All methods tested

### **Phase 3 (Visual Feedback):**
- ✅ Brackets highlighted (subtle)
- ✅ Tooltips show status
- ✅ Click navigates to panel
- ✅ RTF formatting preserved
- ✅ Performance acceptable

### **Phase 4 (Advanced):**
- ✅ Bi-directional sync
- ✅ Smart NLP extraction
- ✅ Rich visual indicators

---

## 📋 FILES TO CREATE/MODIFY

### **New Files (~8):**
```
Infrastructure/Parsing/
├── TodoPrefixParser.cs              (150 lines)
├── CompositeTodoParser.cs           (100 lines)
└── RtfTextLocator.cs                (200 lines)

UI/Services/
├── TodoHighlighter.cs               (300 lines)
├── TodoTooltipProvider.cs           (150 lines)
└── TodoNavigationService.cs         (100 lines)

Tests/
├── TodoPrefixParserTests.cs         (150 lines)
└── CompositeTodoParserTests.cs      (100 lines)
```

### **Modified Files (~6):**
```
Infrastructure/Sync/
└── TodoSyncService.cs               (use CompositeTodoParser)

UI/ViewModels/
├── RichTextEditorViewModel.cs       (add commands)
└── TodoListViewModel.cs             (add SelectAndScrollTo)

UI/Views/
├── RichTextEditor.xaml              (context menu, toolbar)
└── RichTextEditor.xaml.cs           (event handlers)

Composition/
└── PluginSystemConfiguration.cs     (register new services)
```

---

## 🎯 IMPLEMENTATION FOR NEW CHAT

### **Prerequisites:**
1. Understand existing BracketTodoParser
2. Understand TodoSyncService pattern
3. Understand RTF editor structure
4. Review WPF TextPointer/TextRange APIs

### **Step-by-Step:**
1. Test current bracket extraction (validate baseline)
2. Add TODO: prefix parser (expand extraction)
3. Create composite parser (combine strategies)
4. Add UI commands (selection, toolbar, shortcuts)
5. Implement highlighting (visual feedback)
6. Add tooltips (status display)
7. Enable navigation (click to panel)
8. Test thoroughly (all methods + visual feedback)

---

## ✅ CONFIDENCE ASSESSMENT

**Implementation Confidence:**

| Phase | Confidence | Risk Level |
|-------|-----------|------------|
| **Bracket testing** | 95% | LOW |
| **TODO: prefix** | 90% | LOW |
| **Text selection** | 90% | MEDIUM |
| **Basic highlighting** | 75% | HIGH |
| **Tooltips** | 85% | MEDIUM |
| **Navigation** | 90% | LOW |
| **Bi-directional sync** | 60% | HIGH |

**Overall:** 85% confidence for Phases 1-3, 65% for Phase 4

**Biggest Risk:** RTF highlighting without breaking formatting (mitigated with safe APIs)

---

## 🚀 SUMMARY FOR NEW CHAT

**What's Being Built:**
> Comprehensive todo extraction from RTF notes with multiple input methods (brackets, TODO: prefix, text selection, toolbar, keyboard) and rich visual feedback (highlighting, tooltips, navigation)

**Existing Infrastructure:**
> - BracketTodoParser (442 lines) - Working
> - TodoSyncService (267 lines) - Background sync
> - RichTextBox editor with toolbar
> - Source tracking in database (line number, offset)

**What's Needed:**
> 1. TodoPrefixParser (TODO: syntax)
> 2. CompositeTodoParser (combine strategies)
> 3. UI commands (selection, toolbar, shortcuts)
> 4. TodoHighlighter (visual feedback)
> 5. TodoTooltipProvider (status display)
> 6. TodoNavigationService (click to panel)

**Time Estimates:**
> - Core extraction (multiple methods): 13 hours
> - Visual feedback: 14 hours
> - Advanced features: 25 hours
> - Total: ~52 hours for complete system

**Phased Approach:**
> Week 1: Core extraction methods
> Week 2: Visual feedback
> Month 2+: Advanced features (if validated)

**Design Patterns:**
> Strategy (multiple parsers), Observer (events), Command (UI actions), Decorator (highlighting)

**Architecture:**
> Clean Architecture, MVVM, Repository Pattern, Async/Await, Error Handling

---

**This plan provides complete context for implementing a production-grade RTF todo extraction system with excellent UX.** ✅

