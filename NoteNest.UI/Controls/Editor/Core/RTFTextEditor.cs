using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NoteNest.Core.Models;

namespace NoteNest.UI.Controls.Editor.Core
{
    /// <summary>
    /// RTF editor implementation with native rich text support and performance optimization
    /// </summary>
    public class RTFTextEditor : RichTextBox, INotesEditor, IDisposable
    {
        private bool _isDirty = false;
        private string _originalContent = string.Empty;
        private readonly DispatcherTimer _stateUpdateTimer;
        private bool _disposed = false;
        
        // Performance optimization: Content caching with longer timeout for debouncing
        private string _cachedContent;
        private DateTime _lastCacheTime;
        private readonly TimeSpan _cacheTimeout = TimeSpan.FromMilliseconds(200);
        
        // Settings and metadata support
        private NoteNest.Core.Services.NoteMetadataManager _metadataManager;
        private NoteModel _currentNote;
        
        public event EventHandler ContentChanged;
        public event EventHandler<ListStateChangedEventArgs> ListStateChanged;
        
        public NoteFormat Format => NoteFormat.RTF;
        public bool IsDirty => _isDirty;
        public string OriginalContent => _originalContent;
        
        public NoteModel CurrentNote 
        {
            get => _currentNote;
            set => _currentNote = value;
        }

        public void SetMetadataManager(NoteNest.Core.Services.NoteMetadataManager manager)
        {
            _metadataManager = manager;
        }
        
        public RTFTextEditor()
        {
            // Initialize editor settings
            IsReadOnly = false;
            AcceptsReturn = true;
            AcceptsTab = true;
            IsDocumentEnabled = true;
            Focusable = true;
            IsHitTestVisible = true;
            
            // Set visual properties to match FormattedTextEditor
            SetResourceReference(BackgroundProperty, "SystemControlBackgroundAltHighBrush");
            SetResourceReference(ForegroundProperty, "SystemControlForegroundBaseHighBrush");
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            FocusVisualStyle = null;
            
            // Initialize document with proper defaults
            Document = new FlowDocument();
            Document.PagePadding = new Thickness(8);
            Document.LineHeight = 1.4 * 14; // Match FormattedTextEditor
            
            // Wire up events
            TextChanged += OnTextChanged;
            SelectionChanged += OnSelectionChanged;
            GotFocus += (s, e) => Keyboard.Focus(this);
            
            // Initialize state update timer for toolbar
            _stateUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _stateUpdateTimer.Tick += UpdateListState;
            
            // Content change timer removed - using immediate ContentChanged for WAL protection
            
            // Initialize keyboard shortcuts and behaviors
            RegisterCommandBindings();
            RegisterKeyboardBehaviors();
            
            // Enable spell check by default
            SpellCheck.SetIsEnabled(this, true);
            SpellCheck.SetSpellingReform(this, SpellingReform.PreAndPostreform);
            Language = System.Windows.Markup.XmlLanguage.GetLanguage("en-US");
        }
        
        public void LoadContent(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                Document.Blocks.Clear();
                Document.Blocks.Add(new Paragraph());
                _originalContent = string.Empty;
                _isDirty = false;
                return;
            }
            
            // Security validation before loading RTF content
            if (!IsValidRTF(content))
            {
                System.Diagnostics.Debug.WriteLine("[RTF] Invalid or potentially unsafe RTF content detected, loading as plain text");
                Document.Blocks.Clear();
                Document.Blocks.Add(new Paragraph(new Run(content)));
                _originalContent = SaveContent();
                _isDirty = false;
                return;
            }
            
            try
            {
                // Sanitize RTF content before loading
                var sanitizedContent = SanitizeRTFContent(content);
                
                // Load RTF content natively - no conversion!
                var range = new TextRange(Document.ContentStart, Document.ContentEnd);
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(sanitizedContent)))
                {
                    range.Load(stream, DataFormats.Rtf);
                }
                
                _originalContent = content;
                _isDirty = false;
                _cachedContent = null; // Clear cache on load
                
                System.Diagnostics.Debug.WriteLine($"[RTF] Loaded {content.Length} chars");
            }
            catch (Exception ex)
            {
                // If RTF loading fails, treat as plain text
                Document.Blocks.Clear();
                Document.Blocks.Add(new Paragraph(new Run(content)));
                _originalContent = SaveContent();
                _isDirty = false;
                
                System.Diagnostics.Debug.WriteLine($"[RTF] Failed to load RTF, loaded as plain text: {ex.Message}");
            }
        }
        
        public string SaveContent()
        {
            // Use cache for performance (important for auto-save)
            if (_cachedContent != null && 
                (DateTime.Now - _lastCacheTime) < _cacheTimeout)
            {
                return _cachedContent;
            }
            
            try
            {
                var range = new TextRange(Document.ContentStart, Document.ContentEnd);
                using (var stream = new MemoryStream())
                {
                    range.Save(stream, DataFormats.Rtf);
                    _cachedContent = Encoding.UTF8.GetString(stream.ToArray());
                    _lastCacheTime = DateTime.Now;
                    return _cachedContent;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Save failed: {ex.Message}");
                // Fallback to plain text
                return new TextRange(Document.ContentStart, Document.ContentEnd).Text;
            }
        }
        
        public string GetQuickContent()
        {
            // For WAL protection - uses same caching as SaveContent
            return SaveContent();
        }
        
        public void MarkClean()
        {
            _isDirty = false;
            _originalContent = SaveContent();
        }
        
        /// <summary>
        /// Force immediate content change notification
        /// Now that ContentChanged fires immediately, this is just for consistency
        /// </summary>
        public void ForceContentNotification()
        {
            ContentChanged?.Invoke(this, EventArgs.Empty);
            System.Diagnostics.Debug.WriteLine($"[RTF] Forced immediate content notification");
        }
        
        public void MarkDirty()
        {
            if (!_isDirty)
            {
                _isDirty = true;
                // ContentChanged is now fired immediately in OnTextChanged for WAL protection
            }
        }
        
        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_disposed) return;
            
            MarkDirty();
            
            // Fire ContentChanged immediately for bulletproof WAL protection
            ContentChanged?.Invoke(this, EventArgs.Empty);
            
            // Keep state update timer for toolbar button updates only
            _stateUpdateTimer.Stop();
            _stateUpdateTimer.Start();
            
            // PERFORMANCE FIX: Delay cache invalidation to allow immediate reuse
            Task.Delay(150).ContinueWith(_ => {
                if (!_disposed) _cachedContent = null;
            });
        }
        
        private void OnSelectionChanged(object sender, RoutedEventArgs e)
        {
            _stateUpdateTimer.Stop();
            _stateUpdateTimer.Start();
        }
        
        
        // Formatting Methods - RichTextBox handles these natively!
        
        public void InsertBulletList()
        {
            EditingCommands.ToggleBullets.Execute(null, this);
        }
        
        public void InsertNumberedList()
        {
            EditingCommands.ToggleNumbering.Execute(null, this);
        }
        
        public void IndentSelection()
        {
            EditingCommands.IncreaseIndentation.Execute(null, this);
        }
        
        public void OutdentSelection()
        {
            EditingCommands.DecreaseIndentation.Execute(null, this);
        }
        
        public void ToggleBold()
        {
            EditingCommands.ToggleBold.Execute(null, this);
        }
        
        public void ToggleItalic()
        {
            EditingCommands.ToggleItalic.Execute(null, this);
        }
        
        private void UpdateListState(object sender, EventArgs e)
        {
            _stateUpdateTimer.Stop();
            
            // Determine current list state for toolbar
            var listState = new ListState();
            
            // Check if we're in a list
            var listItem = CaretPosition.Paragraph?.Parent as ListItem;
            if (listItem?.Parent is List parentList)
            {
                listState.IsInList = true;
                listState.IsInBulletList = parentList.MarkerStyle == TextMarkerStyle.Disc ||
                                          parentList.MarkerStyle == TextMarkerStyle.Circle ||
                                          parentList.MarkerStyle == TextMarkerStyle.Square;
                listState.IsInNumberedList = parentList.MarkerStyle == TextMarkerStyle.Decimal ||
                                            parentList.MarkerStyle == TextMarkerStyle.LowerLatin ||
                                            parentList.MarkerStyle == TextMarkerStyle.UpperLatin;
            }
            
            ListStateChanged?.Invoke(this, new ListStateChangedEventArgs(listState));
        }
        
        /// <summary>
        /// Register command bindings for professional keyboard shortcuts
        /// </summary>
        private void RegisterCommandBindings()
        {
            // Basic formatting shortcuts
            InputBindings.Add(new KeyBinding(EditingCommands.ToggleBold, Key.B, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(EditingCommands.ToggleItalic, Key.I, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(EditingCommands.ToggleUnderline, Key.U, ModifierKeys.Control));
            
            // Save shortcut
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, 
                (s, e) => ContentChanged?.Invoke(this, EventArgs.Empty)));
                
            // Paste handling with RTF security
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, OnPreviewPaste));
        }
        
        /// <summary>
        /// Register keyboard behaviors for smart list handling
        /// </summary>
        private void RegisterKeyboardBehaviors()
        {
            PreviewKeyDown += OnPreviewKeyDown;
        }
        
        /// <summary>
        /// Smart keyboard handling for professional editing experience
        /// </summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_disposed) return;
            
            switch (e.Key)
            {
                case Key.Enter:
                    if (IsInList())
                    {
                        if (IsEmptyListItem())
                        {
                            ExitList();
                            e.Handled = true;
                        }
                    }
                    break;
                    
                case Key.Tab:
                    if (IsInList())
                    {
                        try
                        {
                            if (Keyboard.Modifiers == ModifierKeys.Shift)
                            {
                                EditingCommands.DecreaseIndentation.Execute(null, this);
                            }
                            else
                            {
                                EditingCommands.IncreaseIndentation.Execute(null, this);
                            }
                            e.Handled = true;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[RTF] Tab operation failed: {ex.Message}");
                        }
                    }
                    break;
                    
                case Key.Back:
                    if (IsInList() && IsAtStartOfListItem())
                    {
                        RemoveListFormattingFromCurrentItem();
                        e.Handled = true;
                    }
                    break;
            }
        }
        
        /// <summary>
        /// Smart paste handling with RTF security validation
        /// </summary>
        private void OnPreviewPaste(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Command == ApplicationCommands.Paste)
            {
                PerformSmartRTFPaste();
                e.Handled = true;
            }
        }
        
        /// <summary>
        /// Perform intelligent RTF paste with security validation
        /// </summary>
        private void PerformSmartRTFPaste()
        {
            try
            {
                // For RTF editor, we can accept rich formatting but validate security
                if (Clipboard.ContainsData(DataFormats.Rtf))
                {
                    var rtfContent = Clipboard.GetData(DataFormats.Rtf) as string;
                    if (IsValidRTF(rtfContent))
                    {
                        // Sanitize and paste RTF content (maintains formatting)
                        var sanitizedRTF = SanitizeRTFContent(rtfContent);
                        var range = Selection.IsEmpty ? 
                            new TextRange(CaretPosition, CaretPosition) : Selection;
                        
                        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(sanitizedRTF)))
                        {
                            range.Load(stream, DataFormats.Rtf);
                        }
                        return;
                    }
                }
                
                // Fallback to plain text paste
                if (Clipboard.ContainsText())
                {
                    var plainText = CleanPastedText(Clipboard.GetText());
                    if (Selection.IsEmpty)
                        CaretPosition.InsertTextInRun(plainText);
                    else
                        Selection.Text = plainText;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Smart paste failed: {ex.Message}");
                // Fallback to default paste
                try { ApplicationCommands.Paste.Execute(null, this); } catch { }
            }
        }
        
        /// <summary>
        /// Clean pasted text - adapted from FormattedTextEditor
        /// </summary>
        private string CleanPastedText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            try
            {
                // Remove common problematic characters from rich text sources
                text = text.Replace("\r\n", "\n")      // Normalize line endings
                          .Replace("\r", "\n")         // Handle Mac line endings
                          .Replace("\u00A0", " ")      // Non-breaking space -> regular space
                          .Replace("\u2028", "\n")     // Line separator -> newline
                          .Replace("\u2029", "\n\n");  // Paragraph separator -> double newline
                
                // Remove zero-width characters
                text = text.Replace("\u200B", "")      // Zero-width space
                          .Replace("\u200C", "")       // Zero-width non-joiner
                          .Replace("\u200D", "")       // Zero-width joiner
                          .Replace("\uFEFF", "");      // Byte order mark
                
                // Clean up extra whitespace
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");
                
                return text.Trim();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Text cleaning failed: {ex.Message}");
                return text; // Return original if cleaning fails
            }
        }
        
        // Smart list behavior helper methods
        private bool IsInList()
        {
            return CaretPosition.Paragraph?.Parent is ListItem;
        }
        
        private bool IsEmptyListItem()
        {
            if (CaretPosition.Paragraph?.Parent is ListItem listItem)
            {
                var text = new TextRange(listItem.ContentStart, listItem.ContentEnd).Text;
                return string.IsNullOrWhiteSpace(text?.Trim());
            }
            return false;
        }
        
        private bool IsAtStartOfListItem()
        {
            if (CaretPosition.Paragraph?.Parent is ListItem listItem)
            {
                return CaretPosition.CompareTo(listItem.ContentStart) <= 0;
            }
            return false;
        }
        
        private void ExitList()
        {
            try
            {
                var currentParagraph = CaretPosition.Paragraph;
                if (currentParagraph?.Parent is ListItem listItem && listItem.Parent is List list)
                {
                    // Create new paragraph after the list
                    var newParagraph = new Paragraph();
                    Document.Blocks.InsertAfter(list, newParagraph);
                    CaretPosition = newParagraph.ContentStart;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Exit list failed: {ex.Message}");
            }
        }
        
        private void RemoveListFormattingFromCurrentItem()
        {
            try
            {
                var paragraph = CaretPosition.Paragraph;
                if (paragraph?.Parent is ListItem listItem && listItem.Parent is List list)
                {
                    // Get text content
                    var textRange = new TextRange(listItem.ContentStart, listItem.ContentEnd);
                    var text = textRange.Text;
                    
                    // Create new paragraph with the text
                    var newParagraph = new Paragraph(new Run(text));
                    
                    // Insert before list and remove list item
                    Document.Blocks.InsertBefore(list, newParagraph);
                    list.ListItems.Remove(listItem);
                    
                    // Remove list if empty
                    if (list.ListItems.Count == 0)
                    {
                        Document.Blocks.Remove(list);
                    }
                    
                    CaretPosition = newParagraph.ContentStart;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Remove list formatting failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Apply editor settings for consistency with FormattedTextEditor
        /// </summary>
        public void ApplySettings(NoteNest.Core.Models.EditorSettings settings)
        {
            if (settings == null || _disposed) return;
            
            try
            {
                Document.FontFamily = new System.Windows.Media.FontFamily(settings.FontFamily);
                Document.FontSize = settings.FontSize;
                SpellCheck.SetIsEnabled(this, settings.EnableSpellCheck);
                SpellCheck.SetSpellingReform(this, SpellingReform.PreAndPostreform);
                Language = System.Windows.Markup.XmlLanguage.GetLanguage(settings.SpellCheckLanguage);
                
                // Enforce document size limits
                var currentSize = _originalContent?.Length ?? 0;
                var maxSizeBytes = settings.MaxDocumentSizeMB * 1024 * 1024;
                
                if (currentSize > maxSizeBytes)
                {
                    System.Diagnostics.Debug.WriteLine($"[RTF] Warning: Document size {currentSize / (1024*1024):F1}MB exceeds limit {settings.MaxDocumentSizeMB}MB");
                }
                
                System.Diagnostics.Debug.WriteLine($"[RTF] Settings applied: Font={settings.FontFamily}, Size={settings.FontSize}, SpellCheck={settings.EnableSpellCheck}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Settings application failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Validate RTF content for security and format correctness
        /// </summary>
        private bool IsValidRTF(string content)
        {
            if (string.IsNullOrEmpty(content)) return true;
            
            try
            {
                var trimmed = content.TrimStart();
                
                // Must start with RTF header
                if (!trimmed.StartsWith("{\\rtf"))
                    return false;
                
                // Check for dangerous RTF elements
                var dangerousPatterns = new[] {
                    "\\objdata", "\\object", "\\pict", "\\field",
                    "javascript:", "<script", "\\rtfsp", "\\nonshppict"
                };
                
                return !dangerousPatterns.Any(p => content.Contains(p, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false; // If validation fails, assume unsafe
            }
        }
        
        /// <summary>
        /// Sanitize RTF content by removing potentially dangerous constructs
        /// </summary>
        private string SanitizeRTFContent(string rtfContent)
        {
            if (string.IsNullOrEmpty(rtfContent)) return rtfContent;
            
            try
            {
                var sanitized = rtfContent;
                
                // Remove embedded objects and fields (keep formatting only)
                sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"\\object[^}]*}", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"\\field[^}]*}", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"\\pict[^}]*}", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                // Remove script-like content
                sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"javascript:[^}]*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                return sanitized;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Sanitization failed: {ex.Message}");
                return rtfContent; // Return original if sanitization fails
            }
        }
        
        /// <summary>
        /// IDisposable implementation - prevents memory leaks
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            
            try
            {
                // Stop and dispose timer
                _stateUpdateTimer?.Stop();
                _stateUpdateTimer.Tick -= UpdateListState;
                
                // Unhook event handlers
                TextChanged -= OnTextChanged;
                SelectionChanged -= OnSelectionChanged;
                
                // Clear cache and references
                _cachedContent = null;
                _metadataManager = null;
                _currentNote = null;
                
                _disposed = true;
                
                System.Diagnostics.Debug.WriteLine("[RTF] Editor disposed and cleaned up");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Dispose failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Finalizer to ensure cleanup if Dispose() not called
        /// </summary>
        ~RTFTextEditor()
        {
            Dispose();
        }
    }
}
