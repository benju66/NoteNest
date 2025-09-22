using System;
using System.Windows.Input;
using System.Windows.Documents;
using System.Linq;
using NoteNest.UI.Controls.Editor.RTF.Features;
using NoteNest.UI.Controls.Editor.RTF.Core;
using NoteNest.Core.Models;
using NoteNest.Core.Commands;

namespace NoteNest.UI.Controls.Editor.RTF
{
    /// <summary>
    /// Complete RTF editor with all features composed together
    /// Single Responsibility: RTF editing with integrated features and memory management
    /// Clean composition of focused components, ~80 lines as designed
    /// </summary>
    public class RTFEditor : RTFEditorCore, IDisposable
    {
        // Feature modules (SRP compliance)
        private readonly HighlightFeature _highlight = new();
        private readonly LinkFeature _links = new();
        
        // Memory management services (preserved from robust implementation)
        private EditorMemoryManager _memoryManager;
        private EditorEventManager _eventManager;
        
        // State management
        private bool _isDirty = false;
        private string _originalContent = string.Empty;
        private bool _disposed = false;
        private NoteModel _currentNote;
        
        // Smart list behavior state
        private DateTime _lastEnterTime = DateTime.MinValue;

        public bool IsDirty => _isDirty;
        public string OriginalContent => _originalContent;
        public NoteModel CurrentNote 
        {
            get => _currentNote;
            set => _currentNote = value;
        }
        
        public RTFEditor() : this(new EditorSettings())
        {
        }
        
        public RTFEditor(EditorSettings settings)
        {
            InitializeMemoryManagement(settings);
            InitializeFeatures();
            InitializeKeyboardShortcuts();
            WireUpEvents();
            
            System.Diagnostics.Debug.WriteLine("[RTFEditor] Initialized with clean SRP architecture");
        }
        
        private void InitializeMemoryManagement(EditorSettings settings)
        {
            try
            {
                // Use our robust memory management services
                _memoryManager = new EditorMemoryManager(settings);
                _eventManager = new EditorEventManager(settings);
                
                // Configure memory optimization
                _memoryManager.ConfigureEditor(this);
                
                System.Diagnostics.Debug.WriteLine("[RTFEditor] Memory management initialized");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Memory management initialization failed: {ex.Message}");
            }
        }
        
        private void InitializeFeatures()
        {
            try
            {
                // Attach feature modules
                _links.Attach(this);
                
                System.Diagnostics.Debug.WriteLine("[RTFEditor] Features attached");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Feature initialization failed: {ex.Message}");
            }
        }
        
        private void InitializeKeyboardShortcuts()
        {
            try
            {
                // Add highlight shortcut
                var highlightCommand = new RelayCommand(() => _highlight.CycleHighlight(this));
                InputBindings.Add(new KeyBinding(highlightCommand, Key.H, ModifierKeys.Control));
                
                System.Diagnostics.Debug.WriteLine("[RTFEditor] Enhanced keyboard shortcuts registered");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Keyboard shortcut initialization failed: {ex.Message}");
            }
        }
        
        private void WireUpEvents()
        {
            try
            {
                // Use managed event subscriptions for bulletproof cleanup
                _eventManager?.SubscribeToTextChanged(this, OnTextChanged);
                _eventManager?.SubscribeToPreviewKeyDown(this, OnPreviewKeyDown);
                
                System.Diagnostics.Debug.WriteLine("[RTFEditor] Managed events wired up (including smart list behavior)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Event wiring failed: {ex.Message}");
                // Fallback to direct subscription
                TextChanged += OnTextChanged;
                PreviewKeyDown += OnPreviewKeyDown;
            }
        }
        
        private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_disposed)
            {
                _isDirty = true;
                System.Diagnostics.Debug.WriteLine("[RTFEditor] Text changed, setting dirty flag");
                
                // The ContentChanged event is handled by the base class RTFEditorCore
                // We don't need to fire it manually here - it's already fired by:
                // RTFEditorCore: TextChanged += (s, e) => ContentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        
        /// <summary>
        /// Smart keyboard event handler for enhanced list behavior
        /// </summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_disposed) return;
            
            try
            {
                switch (e.Key)
                {
                    case Key.Enter:
                        if (HandleEnterInList(e))
                            e.Handled = true;
                        break;
                        
                    case Key.Tab:
                        if (e.KeyboardDevice.Modifiers == ModifierKeys.Shift)
                        {
                            if (HandleShiftTabInList(e))
                                e.Handled = true;
                        }
                        else if (HandleTabInList(e))
                        {
                            e.Handled = true;
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] PreviewKeyDown failed: {ex.Message}");
            }
        }
        
        #region Smart List Behavior Methods
        
        /// <summary>
        /// Handle Enter key in list context with smart exit behavior
        /// </summary>
        private bool HandleEnterInList(KeyEventArgs e)
        {
            var context = GetCurrentListContext();
            if (context == null) return false;
            
            var currentTime = DateTime.Now;
            var timeSinceLastEnter = currentTime - _lastEnterTime;
            var isDoubleEnter = timeSinceLastEnter.TotalMilliseconds < 1000; // Within 1 second
            
            // Check for list exit conditions
            if (IsCaretInEmptyListItem())
            {
                // Exit list on empty item + Enter (any nesting level)
                ExitListMode();
                _lastEnterTime = DateTime.MinValue; // Reset tracking
                System.Diagnostics.Debug.WriteLine("[RTFEditor] Exited list from empty item");
                return true;
            }
            else if (isDoubleEnter)
            {
                // Exit list on double-enter (rapid consecutive Enter presses)
                ExitListMode();
                _lastEnterTime = DateTime.MinValue; // Reset tracking  
                System.Diagnostics.Debug.WriteLine("[RTFEditor] Exited list from double-enter");
                return true;
            }
            
            // Create new list item and track this Enter press
            CreateNewListItem();
            _lastEnterTime = currentTime;
            System.Diagnostics.Debug.WriteLine("[RTFEditor] Created new list item, tracking Enter time");
            return true;
        }
        
        /// <summary>
        /// Handle Tab key in list context (increase indentation/nesting)
        /// </summary>
        private bool HandleTabInList(KeyEventArgs e)
        {
            var context = GetCurrentListContext();
            if (context == null) return false;
            
            try
            {
                // Use WPF's built-in indentation, which handles list nesting
                EditingCommands.IncreaseIndentation.Execute(null, this);
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Increased list indentation");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Tab handling failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Handle Shift+Tab in list context (decrease indentation/nesting)
        /// </summary>
        private bool HandleShiftTabInList(KeyEventArgs e)
        {
            var context = GetCurrentListContext();
            if (context == null) return false;
            
            try
            {
                // Use WPF's built-in outdentation, which handles list nesting
                EditingCommands.DecreaseIndentation.Execute(null, this);
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Decreased list indentation");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Shift+Tab handling failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Get current list context information
        /// </summary>
        private ListItemContext GetCurrentListContext()
        {
            if (CaretPosition == null) return null;
            
            try
            {
                // Find the current ListItem by checking adjacent elements
                var listItem = CaretPosition.GetAdjacentElement(LogicalDirection.Backward) as ListItem ??
                              CaretPosition.GetAdjacentElement(LogicalDirection.Forward) as ListItem;
                
                if (listItem == null)
                {
                    // Check if we're inside a paragraph that's within a list item
                    var paragraph = CaretPosition.Paragraph;
                    listItem = paragraph?.Parent as ListItem;
                }
                
                if (listItem?.Parent is List list)
                {
                    return new ListItemContext
                    {
                        ListItem = listItem,
                        List = list,
                        NestingLevel = GetListNestingLevel(listItem)
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] GetCurrentListContext failed: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Check if caret is in an empty list item
        /// </summary>
        private bool IsCaretInEmptyListItem()
        {
            var context = GetCurrentListContext();
            if (context?.ListItem == null) return false;
            
            try
            {
                // Get text content of the list item, excluding the bullet marker
                var textRange = new TextRange(context.ListItem.ContentStart, context.ListItem.ContentEnd);
                var text = textRange.Text?.Trim();
                
                // Consider empty if no text or only whitespace/bullet characters
                var isEmpty = string.IsNullOrWhiteSpace(text) || 
                             text == "•" || 
                             text == "\u2022" || 
                             text.All(c => char.IsWhiteSpace(c) || c == '•' || c == '\u2022');
                             
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] IsCaretInEmptyListItem: '{text}' -> {isEmpty}");
                return isEmpty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] IsCaretInEmptyListItem failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Exit list mode and return to normal paragraph
        /// </summary>
        private void ExitListMode()
        {
            try
            {
                var context = GetCurrentListContext();
                if (context?.ListItem == null) return;
                
                // Remove the empty list item first
                var listToCheck = context.List;
                listToCheck.ListItems.Remove(context.ListItem);
                
                // Create new paragraph after the list
                var newParagraph = new Paragraph();
                
                // Handle different parent types
                if (listToCheck.Parent is FlowDocument flowDoc)
                {
                    flowDoc.Blocks.InsertAfter(listToCheck, newParagraph);
                    
                    // If list is now empty, remove it entirely
                    if (listToCheck.ListItems.Count == 0)
                    {
                        flowDoc.Blocks.Remove(listToCheck);
                    }
                }
                else if (listToCheck.Parent is ListItem parentListItem)
                {
                    parentListItem.Blocks.InsertAfter(listToCheck, newParagraph);
                    
                    // If list is now empty, remove it entirely
                    if (listToCheck.ListItems.Count == 0)
                    {
                        parentListItem.Blocks.Remove(listToCheck);
                    }
                }
                else
                {
                    // Fallback: insert at document level
                    var rootParagraph = new Paragraph();
                    Document.Blocks.Add(rootParagraph);
                    newParagraph = rootParagraph;
                }
                
                // Position caret in the new paragraph
                CaretPosition = newParagraph.ContentStart;
                Focus(); // Ensure editor maintains focus
                
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Successfully exited list mode");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] ExitListMode failed: {ex.Message}");
                
                // Fallback: try to position caret at document end
                try
                {
                    CaretPosition = Document.ContentEnd;
                }
                catch
                {
                    // Final fallback: do nothing rather than crash
                }
            }
        }
        
        /// <summary>
        /// Create a new list item with format-preserving text splitting and smart sub-item creation
        /// Phase 1: Fixed fragile remove/re-add pattern with direct Insert()
        /// Phase 2: Added format-preserving text extraction using XAML serialization  
        /// Phase 3: Added structure-based smart sub-item logic
        /// </summary>
        private void CreateNewListItem()
        {
            try
            {
                BeginUndoGroup(); // Group all operations for single undo
                
                var context = GetCurrentListContext();
                if (context?.List == null) return;
                
                // PHASE 2: Extract formatted text that will move to new item
                var formattedTextAfterCaret = ExtractFormattedTextAfterCaret();
                
                // PHASE 3: Structure-based decision (consistent behavior)
                if (HasNestedList(context.ListItem))
                {
                    // CREATE SUB-ITEM: Current item has nested list
                    var nestedList = GetFirstNestedList(context.ListItem);
                    if (nestedList != null)
                    {
                        var newSubItem = CreateListItemWithFormattedText(formattedTextAfterCaret);
                        // Insert as first child using available API
                        InsertListItemAtBeginning(nestedList, newSubItem);
                        CaretPosition = newSubItem.ContentStart;
                        
                        System.Diagnostics.Debug.WriteLine($"[RTFEditor] Created sub-item with formatted text: {!string.IsNullOrEmpty(formattedTextAfterCaret)}");
                    }
                }
                else
                {
                    // CREATE SIBLING: No nested list - standard behavior
                    var newListItem = CreateListItemWithFormattedText(formattedTextAfterCaret);
                    
                    // PHASE 1: Reliable insertion using correct API
                    InsertListItemAfter(context.List, context.ListItem, newListItem);
                    CaretPosition = newListItem.ContentStart;
                    
                    System.Diagnostics.Debug.WriteLine($"[RTFEditor] Created sibling with formatted text: {!string.IsNullOrEmpty(formattedTextAfterCaret)}");
                }
                
                Focus(); // Ensure editor maintains focus
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] CreateNewListItem failed: {ex.Message}");
            }
            finally
            {
                EndUndoGroup(); // Complete atomic operation
            }
        }
        
        /// <summary>
        /// Begin undo group for atomic list operations
        /// </summary>
        private void BeginUndoGroup()
        {
            try
            {
                BeginChange();
                System.Diagnostics.Debug.WriteLine("[RTFEditor] Started undo group");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] BeginUndoGroup failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// End undo group for atomic list operations
        /// </summary>
        private void EndUndoGroup()
        {
            try
            {
                EndChange();
                System.Diagnostics.Debug.WriteLine("[RTFEditor] Completed undo group");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] EndUndoGroup failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Extract formatted text after caret position (preserves bold, italic, highlights)
        /// Phase 2: XAML serialization for format preservation
        /// </summary>
        private string ExtractFormattedTextAfterCaret()
        {
            if (CaretPosition?.Paragraph == null) return string.Empty;
            
            try
            {
                var paragraph = CaretPosition.Paragraph;
                var range = new TextRange(CaretPosition, paragraph.ContentEnd);
                
                // Check if there's any text to extract
                if (string.IsNullOrWhiteSpace(range.Text)) return string.Empty;
                
                // Extract as XAML to preserve ALL formatting (bold, italic, highlights, etc.)
                string xamlContent;
                using (var stream = new System.IO.MemoryStream())
                {
                    range.Save(stream, System.Windows.DataFormats.Xaml);
                    stream.Position = 0;
                    xamlContent = new System.IO.StreamReader(stream).ReadToEnd();
                }
                
                // Remove the text from original location
                range.Text = string.Empty;
                
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Extracted formatted text: {range.Text?.Length ?? 0} chars");
                return xamlContent;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] ExtractFormattedTextAfterCaret failed: {ex.Message}");
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Create list item with XAML content (preserves all formatting)
        /// Phase 2: Format-preserving list item creation
        /// </summary>
        private ListItem CreateListItemWithFormattedText(string xamlContent)
        {
            var listItem = new ListItem(new Paragraph());
            
            if (!string.IsNullOrEmpty(xamlContent))
            {
                try
                {
                    // Load XAML content into the paragraph to preserve formatting
                    var paragraph = listItem.Blocks.FirstBlock as Paragraph;
                    if (paragraph != null)
                    {
                        using (var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(xamlContent)))
                        {
                            var range = new TextRange(paragraph.ContentStart, paragraph.ContentEnd);
                            range.Load(stream, System.Windows.DataFormats.Xaml);
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine("[RTFEditor] Created list item with preserved formatting");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RTFEditor] XAML loading failed, fallback to plain text: {ex.Message}");
                    
                    // Fallback to plain text if XAML fails
                    var paragraph = listItem.Blocks.FirstBlock as Paragraph;
                    paragraph?.Inlines.Add(new System.Windows.Documents.Run(xamlContent));
                }
            }
            
            return listItem;
        }
        
        /// <summary>
        /// Check if list item has nested list (structure-based detection)
        /// Phase 2: Helper for smart sub-item logic
        /// </summary>
        private bool HasNestedList(ListItem listItem)
        {
            if (listItem == null) return false;
            return listItem.Blocks.OfType<List>().Any();
        }
        
        /// <summary>
        /// Get first nested list from list item (structure-based retrieval)
        /// Phase 2: Helper for smart sub-item creation
        /// </summary>
        private List GetFirstNestedList(ListItem listItem)
        {
            if (listItem == null) return null;
            return listItem.Blocks.OfType<List>().FirstOrDefault();
        }
        
        /// <summary>
        /// Insert list item after another item using available WPF API
        /// Phase 1: Corrected insertion method that works with actual ListItemCollection
        /// </summary>
        private void InsertListItemAfter(List list, ListItem afterItem, ListItem newItem)
        {
            if (list == null || newItem == null) return;
            
            try
            {
                // Find position using ToArray (only available method for position detection)
                var listItems = list.ListItems.ToArray();
                var insertIndex = Array.IndexOf(listItems, afterItem);
                
                if (insertIndex >= 0 && insertIndex < listItems.Length - 1)
                {
                    // IMPROVED: Only remove/re-add items AFTER the insertion point
                    var itemsAfterInsertion = listItems.Skip(insertIndex + 1).ToList();
                    
                    // Remove only the items after insertion point
                    foreach (var item in itemsAfterInsertion)
                    {
                        list.ListItems.Remove(item);
                    }
                    
                    // Add new item
                    list.ListItems.Add(newItem);
                    
                    // Re-add the items that were after insertion point
                    foreach (var item in itemsAfterInsertion)
                    {
                        list.ListItems.Add(item);
                    }
                }
                else
                {
                    // Fallback: add at end
                    list.ListItems.Add(newItem);
                }
                
                System.Diagnostics.Debug.WriteLine("[RTFEditor] Inserted list item using corrected API");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] InsertListItemAfter failed: {ex.Message}");
                // Final fallback
                list.ListItems.Add(newItem);
            }
        }
        
        /// <summary>
        /// Insert list item at beginning using available WPF API
        /// Phase 2: Helper for sub-item insertion
        /// </summary>
        private void InsertListItemAtBeginning(List list, ListItem newItem)
        {
            if (list == null || newItem == null) return;
            
            try
            {
                if (list.ListItems.Count == 0)
                {
                    // Empty list - just add
                    list.ListItems.Add(newItem);
                }
                else
                {
                    // OPTIMIZED: Store all existing items, clear, add new item first, then re-add others
                    var existingItems = list.ListItems.ToArray();
                    list.ListItems.Clear();
                    list.ListItems.Add(newItem);
                    
                    foreach (var item in existingItems)
                    {
                        list.ListItems.Add(item);
                    }
                }
                
                System.Diagnostics.Debug.WriteLine("[RTFEditor] Inserted item at beginning using corrected API");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] InsertListItemAtBeginning failed: {ex.Message}");
                // Final fallback
                list.ListItems.Add(newItem);
            }
        }
        
        /// <summary>
        /// Get the nesting level of a list item
        /// </summary>
        private int GetListNestingLevel(ListItem listItem)
        {
            int level = 1;
            var parent = listItem?.Parent;
            
            while (parent != null)
            {
                if (parent is List)
                {
                    level++;
                }
                
                if (parent is Block block)
                    parent = block.Parent;
                else if (parent is Inline inline)
                    parent = inline.Parent;
                else
                    break;
            }
            
            return level;
        }
        
        #endregion
        
        /// <summary>
        /// Save RTF content using static operations
        /// </summary>
        public string SaveContent()
        {
            try
            {
                var content = RTFOperations.SaveToRTF(this);
                _isDirty = false;
                return content;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Save failed: {ex.Message}");
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Load RTF content using static operations
        /// </summary>
        public void LoadContent(string rtfContent)
        {
            try
            {
                RTFOperations.LoadFromRTF(this, rtfContent);
                _originalContent = rtfContent ?? string.Empty;
                _isDirty = false;
                
                // Reapply document styles after RTF loading (RTF might override them)
                RefreshDocumentStylesAfterLoad();
                
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Loaded {rtfContent?.Length ?? 0} chars");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Load failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Mark content as clean (after save)
        /// </summary>
        public void MarkClean()
        {
            _isDirty = false;
        }
        
        /// <summary>
        /// Insert bulleted list at current position - WPF Native approach
        /// </summary>
        public void InsertBulletList()
        {
            try
            {
                // Always use WPF native command for consistent list structure
                EditingCommands.ToggleBullets.Execute(null, this);
                
                // Ensure editor maintains focus for continued editing
                Focus();
                
                System.Diagnostics.Debug.WriteLine("[RTFEditor] Created WPF native bullet list");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Insert bullet list failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Insert numbered list at current position - WPF Native approach
        /// </summary>
        public void InsertNumberedList()
        {
            try
            {
                // Always use WPF native command for consistent list structure
                EditingCommands.ToggleNumbering.Execute(null, this);
                
                // Ensure editor maintains focus for continued editing
                Focus();
                
                System.Diagnostics.Debug.WriteLine("[RTFEditor] Created WPF native numbered list");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Insert numbered list failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get current highlight color for UI binding
        /// </summary>
        public System.Windows.Media.Color CurrentHighlightColor => _highlight.CurrentColor;
        
        /// <summary>
        /// Cycle highlight colors - exposed for toolbar integration
        /// </summary>
        public void CycleHighlight()
        {
            _highlight.CycleHighlight(this);
        }
        
        /// <summary>
        /// Refresh document styles after RTF content loading
        /// RTF loading can override our single spacing styles, so reapply them
        /// Now processes ALL lists recursively including nested ones
        /// </summary>
        private void RefreshDocumentStylesAfterLoad()
        {
            try
            {
                // Reapply document-level single spacing
                Document.PagePadding = new System.Windows.Thickness(0);
                Document.LineHeight = double.NaN;
                
                // Recursively process all blocks including nested lists
                ProcessBlocksRecursively(Document.Blocks);
                
                System.Diagnostics.Debug.WriteLine("[RTFEditor] Document styles refreshed recursively after RTF load");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Document style refresh failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Recursively process all blocks to apply single spacing styles
        /// Handles nested lists properly by traversing the entire document tree
        /// </summary>
        private void ProcessBlocksRecursively(BlockCollection blocks)
        {
            if (blocks == null) return;
            
            try
            {
                foreach (var block in blocks)
                {
                    if (block is Paragraph para)
                    {
                        // Apply single spacing to paragraphs
                        para.Margin = new System.Windows.Thickness(0, 0, 0, 0);
                        para.LineHeight = double.NaN;
                    }
                    else if (block is List list)
                    {
                        // Apply single spacing styles to this list
                        ApplySingleSpacingToList(list);
                        
                        // Recursively process nested lists within list items
                        foreach (var listItem in list.ListItems)
                        {
                            // Apply styles to the list item itself
                            listItem.Margin = new System.Windows.Thickness(0, 0, 0, 0);
                            listItem.Padding = new System.Windows.Thickness(0, 0, 0, 2);
                            
                            // Recursively process any nested content (including nested lists)
                            ProcessBlocksRecursively(listItem.Blocks);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] ProcessBlocksRecursively failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Apply single spacing styles to a specific list
        /// </summary>
        private void ApplySingleSpacingToList(List list)
        {
            if (list == null) return;
            
            try
            {
                list.Margin = new System.Windows.Thickness(0, 0, 0, 6);
                list.Padding = new System.Windows.Thickness(20, 0, 0, 0); // Left padding for bullets
                
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Applied single spacing to list with {list.ListItems.Count} items");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] ApplySingleSpacingToList failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Apply editor settings for integration with settings system
        /// </summary>
        public void ApplySettings(EditorSettings settings)
        {
            if (settings == null || _disposed) return;
            
            try
            {
                // Update memory management services with new settings
                InitializeMemoryManagement(settings);
                
                // Apply document formatting
                Document.FontFamily = new System.Windows.Media.FontFamily(settings.FontFamily);
                Document.FontSize = settings.FontSize;
                
                // Apply line height settings (override default single spacing if user specifies)
                if (settings.LineHeight > 0 && Math.Abs(settings.LineHeight - 1.0) > 0.1)
                {
                    // User wants custom line height
                    Document.LineHeight = settings.FontSize * settings.LineHeight;
                }
                else
                {
                    // Maintain single spacing
                    Document.LineHeight = double.NaN;
                }
                
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Settings applied: Font={settings.FontFamily}, Size={settings.FontSize}, UndoLimit={settings.UndoStackLimit}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Settings application failed: {ex.Message}");
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            try
            {
                _disposed = true;
                
                // Dispose feature modules
                _links?.Dispose();
                
                // Dispose memory management services (bulletproof cleanup)
                _eventManager?.Dispose();
                _memoryManager?.Dispose();
                
                System.Diagnostics.Debug.WriteLine("[RTFEditor] Disposed with clean architecture");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Disposal failed: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Context information for list item operations
    /// </summary>
    internal class ListItemContext
    {
        public ListItem ListItem { get; set; }
        public List List { get; set; }
        public int NestingLevel { get; set; }
    }
}
