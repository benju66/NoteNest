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
using System.Windows.Shapes;
using System.Windows.Threading;
using NoteNest.Core.Commands;
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
            
            // Initialize professional document styles (copied from FormattedTextEditor)
            InitializeRTFDocumentStyles();
            
            // Wire up events
            TextChanged += OnTextChanged;
            SelectionChanged += OnSelectionChanged;
            GotFocus += (s, e) => Keyboard.Focus(this);
            
            // Modern spell check context menu
            ContextMenuOpening += OnContextMenuOpening;
            
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
            
            // Enable theme-aware spell check configuration
            InitializeSpellCheckWithThemeAwareness();
        }
        
        /// <summary>
        /// Initialize comprehensive spell check configuration with proper timing and visual indicators
        /// </summary>
        private void InitializeSpellCheck()
        {
            try
            {
                // Step 1: Enable spell check on the RichTextBox control
                SpellCheck.SetIsEnabled(this, true);
                SpellCheck.SetSpellingReform(this, SpellingReform.PreAndPostreform);
                
                // Step 2: Set language for spell checking
                Language = System.Windows.Markup.XmlLanguage.GetLanguage("en-US");
                
                // Step 3: Configure FlowDocument language (spell check is handled by RichTextBox)
                if (Document != null)
                {
                    Document.Language = System.Windows.Markup.XmlLanguage.GetLanguage("en-US");
                }
                
                // Step 4: Force spell check to be active immediately
                this.IsDocumentEnabled = true;
                this.IsEnabled = true;
                
                // Step 5: Delay final spell check activation to ensure proper initialization
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Re-enable spell check after UI initialization is complete
                        SpellCheck.SetIsEnabled(this, true);
                        
                        // Force a spell check refresh
                        if (Document != null)
                        {
                            var range = new TextRange(Document.ContentStart, Document.ContentEnd);
                            if (!string.IsNullOrEmpty(range.Text))
                            {
                                // Trigger spell check by simulating text change
                                var currentText = range.Text;
                                range.Text = currentText + " ";
                                range.Text = currentText;
                            }
                        }
                        
                        System.Diagnostics.Debug.WriteLine("[RTF] Spell check initialization completed - red squiggly lines should now appear");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RTF] Delayed spell check initialization failed: {ex.Message}");
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
                
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Spell check initialization failed: {ex.Message}");
            }
        }
        
        #region Document Style Infrastructure (SRP: Document Formatting)
        
        /// <summary>
        /// Initialize professional RTF document styles for consistent spacing and formatting
        /// Single Responsibility: Document style management
        /// Copied from FormattedTextEditor for proven reliability
        /// </summary>
        private void InitializeRTFDocumentStyles()
        {
            try
            {
                if (Document == null) return;
                
                // Clean page layout (no padding)
                Document.PagePadding = new Thickness(0);
                
                // CRITICAL: Enhanced paragraph styles with single-line spacing
                var paragraphStyle = new Style(typeof(Paragraph));
                paragraphStyle.Setters.Add(new Setter(Paragraph.MarginProperty, new Thickness(0, 0, 0, 0))); // SINGLE SPACING: No bottom margin
                paragraphStyle.Setters.Add(new Setter(Paragraph.ForegroundProperty, new DynamicResourceExtension("SystemControlForegroundBaseHighBrush")));
                paragraphStyle.Setters.Add(new Setter(Paragraph.LineHeightProperty, double.NaN)); // Use default single line height
                Document.Resources[typeof(Paragraph)] = paragraphStyle;
                
                // Professional hanging indent list styles (copied from FormattedTextEditor)
                var listStyle = new Style(typeof(List));
                listStyle.Setters.Add(new Setter(List.MarginProperty, new Thickness(0, 4, 0, 4)));        // Professional spacing
                listStyle.Setters.Add(new Setter(List.PaddingProperty, new Thickness(28, 0, 0, 0)));      // Perfect hanging indent
                listStyle.Setters.Add(new Setter(List.ForegroundProperty, new DynamicResourceExtension("SystemControlForegroundBaseHighBrush")));
                Document.Resources[typeof(List)] = listStyle;
                
                // List items with minimal spacing
                var listItemStyle = new Style(typeof(ListItem));
                listItemStyle.Setters.Add(new Setter(ListItem.MarginProperty, new Thickness(0, 0, 0, 0))); // SINGLE SPACING: Minimal margins
                Document.Resources[typeof(ListItem)] = listItemStyle;
                
                // Headers with consistent spacing (for potential future use)
                var headerBaseStyle = new Style(typeof(Paragraph));
                headerBaseStyle.Setters.Add(new Setter(Paragraph.MarginProperty, new Thickness(0, 8, 0, 4))); // Header spacing
                headerBaseStyle.Setters.Add(new Setter(Paragraph.FontWeightProperty, FontWeights.Bold));
                headerBaseStyle.Setters.Add(new Setter(Paragraph.ForegroundProperty, new DynamicResourceExtension("SystemControlForegroundBaseHighBrush")));
                // Note: Headers applied dynamically when needed
                
                System.Diagnostics.Debug.WriteLine("[RTF] Professional document styles initialized with single-line spacing");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Document style initialization failed: {ex.Message}");
                // Fallback to basic document setup
                if (Document != null)
                {
                    Document.PagePadding = new Thickness(0);
                }
            }
        }
        
        /// <summary>
        /// Refresh document styles after content loading to ensure consistent formatting
        /// Single Responsibility: Post-load style application
        /// </summary>
        private void RefreshDocumentStylesAfterLoad()
        {
            try
            {
                // RTF loading can override document styles - reapply them
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Reapply all document styles to ensure consistency
                        InitializeRTFDocumentStyles();
                        
                        // Apply current theme if available
                        var isDarkMode = IsCurrentThemeDark();
                        UpdateDocumentThemeStylesOnly(isDarkMode);
                        
                        System.Diagnostics.Debug.WriteLine("[RTF] Document styles refreshed after content load");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RTF] Style refresh after load failed: {ex.Message}");
                    }
                }), System.Windows.Threading.DispatcherPriority.Normal);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Style refresh setup failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Update only theme-related document styles without affecting spell check or other styling
        /// Single Responsibility: Theme-only document style updates
        /// </summary>
        private void UpdateDocumentThemeStylesOnly(bool isDarkMode)
        {
            try
            {
                if (Document == null) return;
                
                var targetBrush = new DynamicResourceExtension("SystemControlForegroundBaseHighBrush");
                
                // Update paragraph style theme colors only (preserve spacing and other properties)
                if (Document.Resources[typeof(Paragraph)] is Style paraStyle)
                {
                    var foregroundSetter = paraStyle.Setters.OfType<Setter>()
                        .FirstOrDefault(s => s.Property == Paragraph.ForegroundProperty);
                    if (foregroundSetter != null)
                    {
                        foregroundSetter.Value = targetBrush;
                    }
                    else
                    {
                        paraStyle.Setters.Add(new Setter(Paragraph.ForegroundProperty, targetBrush));
                    }
                }
                
                // Update list style theme colors only (preserve spacing and other properties)
                if (Document.Resources[typeof(List)] is Style listStyle)
                {
                    var foregroundSetter = listStyle.Setters.OfType<Setter>()
                        .FirstOrDefault(s => s.Property == List.ForegroundProperty);
                    if (foregroundSetter != null)
                    {
                        foregroundSetter.Value = targetBrush;
                    }
                    else
                    {
                        listStyle.Setters.Add(new Setter(List.ForegroundProperty, targetBrush));
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"[RTF] Document theme styles updated for {(isDarkMode ? "dark" : "light")} mode");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Document theme style update failed: {ex.Message}");
            }
        }
        
        #endregion
        
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
            
            // Re-enable spell check after content loading (RTF loading can reset spell check settings)
            RefreshSpellCheckAfterLoad();
            
            // Refresh document styles after content loading (RTF loading can override document styles)
            RefreshDocumentStylesAfterLoad();
        }
        
        /// <summary>
        /// Refresh spell check after content loading to ensure red squiggly lines appear
        /// </summary>
        private void RefreshSpellCheckAfterLoad()
        {
            try
            {
                // Use dispatcher to ensure proper timing after content load
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Re-apply spell check settings to both control and document
                        SpellCheck.SetIsEnabled(this, true);
                        SpellCheck.SetSpellingReform(this, SpellingReform.PreAndPostreform);
                        Language = System.Windows.Markup.XmlLanguage.GetLanguage("en-US");
                        
                        if (Document != null)
                        {
                            Document.Language = System.Windows.Markup.XmlLanguage.GetLanguage("en-US");
                            
                            // Force spell check to recheck content
                            var range = new TextRange(Document.ContentStart, Document.ContentEnd);
                            if (!string.IsNullOrWhiteSpace(range.Text))
                            {
                                // Trigger spell check refresh by touching the text
                                var originalText = range.Text;
                                var caretPos = CaretPosition;
                                range.Text = originalText + " ";
                                range.Text = originalText;
                                CaretPosition = caretPos; // Restore caret position
                            }
                        }
                        
                        System.Diagnostics.Debug.WriteLine("[RTF] Spell check refreshed after content load");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RTF] Spell check refresh failed: {ex.Message}");
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Spell check refresh setup failed: {ex.Message}");
            }
        }
        
        #region Spell Check Style Preservation (SRP: Spell Check Management)
        
        /// <summary>
        /// Enhanced spell check initialization with theme-aware configuration
        /// Single Responsibility: Spell check visual management
        /// </summary>
        private void InitializeSpellCheckWithThemeAwareness()
        {
            try
            {
                // Step 1: Basic spell check setup
                SpellCheck.SetIsEnabled(this, true);
                SpellCheck.SetSpellingReform(this, SpellingReform.PreAndPostreform);
                Language = System.Windows.Markup.XmlLanguage.GetLanguage("en-US");
                
                if (Document != null)
                {
                    Document.Language = System.Windows.Markup.XmlLanguage.GetLanguage("en-US");
                }
                
                // Step 2: CRITICAL - Clear any potential style conflicts that could hide spell check underlines
                try
                {
                    // Remove any custom adorner decorations that might conflict
                    this.Resources.Remove(typeof(System.Windows.Documents.Adorner));
                    
                    // Ensure spell check adorner layer is not being overridden
                    if (Document != null)
                    {
                        Document.Resources.Remove("SpellCheckError");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RTF] Adorner cleanup failed (non-critical): {ex.Message}");
                }
                
                // Step 3: Force visual refresh with minimal disruption
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Re-enable spell check to ensure it's active
                        SpellCheck.SetIsEnabled(this, true);
                        
                        // Force spell check visual refresh by changing focus briefly
                        if (Document != null && !string.IsNullOrEmpty(new TextRange(Document.ContentStart, Document.ContentEnd).Text))
                        {
                            var currentFocus = Keyboard.FocusedElement;
                            this.Focus();
                            
                            // Minimal text manipulation to trigger spell check
                            var currentSelection = Selection;
                            CaretPosition = Document.ContentEnd;
                            CaretPosition.InsertTextInRun(" ");
                            var newEnd = CaretPosition;
                            new TextRange(newEnd.GetPositionAtOffset(-1), newEnd).Text = "";
                            
                            // Restore original selection/focus
                            Selection.Select(currentSelection.Start, currentSelection.End);
                            if (currentFocus is UIElement element)
                            {
                                element.Focus();
                            }
                        }
                        
                        System.Diagnostics.Debug.WriteLine("[RTF] Theme-aware spell check initialized - red underlines should be visible in both themes");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RTF] Theme-aware spell check setup failed: {ex.Message}");
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
                
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Spell check initialization failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Preserve spell check styles during theme changes
        /// Single Responsibility: Spell check preservation during theming
        /// </summary>
        private void PreserveSpellCheckDuringThemeChange()
        {
            try
            {
                // Store current spell check state before theme changes
                var spellCheckEnabled = SpellCheck.GetIsEnabled(this);
                var currentLanguage = this.Language;
                
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Restore spell check configuration after theme changes
                        SpellCheck.SetIsEnabled(this, spellCheckEnabled);
                        this.Language = currentLanguage;
                        
                        // CRITICAL: Force spell check underline refresh without content disruption
                        if (spellCheckEnabled && Document != null)
                        {
                            // Method 1: Focus change to trigger spell check refresh
                            var hadFocus = this.IsFocused;
                            this.Focus();
                            
                            // Method 2: Force adorner layer refresh by briefly disabling/enabling
                            SpellCheck.SetIsEnabled(this, false);
                            SpellCheck.SetIsEnabled(this, true);
                            
                            // Restore focus state
                            if (!hadFocus)
                            {
                                Keyboard.ClearFocus();
                            }
                        }
                        
                        System.Diagnostics.Debug.WriteLine("[RTF] Spell check preserved and refreshed during theme change");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RTF] Spell check preservation failed: {ex.Message}");
                    }
                }), System.Windows.Threading.DispatcherPriority.Normal);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Spell check preservation setup failed: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Format Fidelity Manager (SRP: Save/Load Consistency)
        
        /// <summary>
        /// Save RTF content with enhanced format preservation
        /// Single Responsibility: RTF content serialization with style preservation
        /// </summary>
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
                // Preserve document styles before saving
                PreserveDocumentStylesForSave();
                
                var range = new TextRange(Document.ContentStart, Document.ContentEnd);
                using (var stream = new MemoryStream())
                {
                    range.Save(stream, DataFormats.Rtf);
                    _cachedContent = Encoding.UTF8.GetString(stream.ToArray());
                    _lastCacheTime = DateTime.Now;
                    
                    // Enhanced RTF content with style preservation
                    _cachedContent = EnhanceRTFFormatConsistency(_cachedContent);
                    
                    System.Diagnostics.Debug.WriteLine($"[RTF] Saved with enhanced format preservation: {_cachedContent.Length} chars");
                    return _cachedContent;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Enhanced save failed: {ex.Message}");
                // Fallback to plain text
                return new TextRange(Document.ContentStart, Document.ContentEnd).Text;
            }
        }
        
        /// <summary>
        /// Preserve document styles before save operation
        /// Single Responsibility: Pre-save style preservation
        /// </summary>
        private void PreserveDocumentStylesForSave()
        {
            try
            {
                if (Document == null) return;
                
                // Ensure all our custom styles are properly applied before saving
                InitializeRTFDocumentStyles();
                
                // Apply current theme to ensure consistent appearance
                var isDarkMode = IsCurrentThemeDark();
                UpdateDocumentThemeStylesOnly(isDarkMode);
                
                System.Diagnostics.Debug.WriteLine("[RTF] Document styles preserved for save operation");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Style preservation for save failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Enhance RTF content for better format consistency across save/load cycles
        /// Single Responsibility: RTF format optimization
        /// </summary>
        private string EnhanceRTFFormatConsistency(string rtfContent)
        {
            try
            {
                if (string.IsNullOrEmpty(rtfContent)) return rtfContent;
                
                var enhanced = rtfContent;
                
                // Ensure consistent line spacing in RTF output
                // Add specific RTF control words for single line spacing
                if (!enhanced.Contains("\\sl0"))
                {
                    // Insert single line spacing control after RTF header
                    enhanced = System.Text.RegularExpressions.Regex.Replace(enhanced, 
                        @"(\\rtf1[^}]*})", 
                        "$1\\sl0\\slmult0", // Single line spacing
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
                
                // Ensure paragraph spacing consistency
                enhanced = System.Text.RegularExpressions.Regex.Replace(enhanced,
                    @"\\sb\d+|\\sa\d+", // Remove existing spacing before/after paragraphs
                    "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                System.Diagnostics.Debug.WriteLine("[RTF] Format consistency enhancements applied");
                return enhanced;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Format enhancement failed: {ex.Message}");
                return rtfContent; // Return original if enhancement fails
            }
        }
        
        #endregion
        
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
        
        /// <summary>
        /// Modern spell check context menu with ModernWPF styling
        /// </summary>
        private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            try
            {
                var contextMenu = new ContextMenu();
                contextMenu.SetResourceReference(ContextMenu.StyleProperty, typeof(ContextMenu));
                
                // Get spelling error at current position
                var spellingError = GetSpellingError(CaretPosition);
                
                if (spellingError != null)
                {
                    // Add spelling suggestions
                    var suggestions = spellingError.Suggestions.Take(6).ToList();
                    
                    if (suggestions.Any())
                    {
                        foreach (var suggestion in suggestions)
                        {
                            var suggestionItem = new MenuItem
                            {
                                Header = suggestion,
                                FontWeight = FontWeights.Bold,
                                Command = new RelayCommand(() => ReplaceMisspelledWord(spellingError, suggestion))
                            };
                            
                            // Add spell check icon
                            var icon = new System.Windows.Shapes.Path
                            {
                                Data = Geometry.Parse("M9,20.42L2.79,14.21L5.62,11.38L9,14.77L18.88,4.88L21.71,7.71L9,20.42Z"),
                                Fill = (Brush)FindResource("SystemControlForegroundAccentBrush"),
                                Stretch = Stretch.Uniform,
                                Width = 12,
                                Height = 12
                            };
                            suggestionItem.Icon = icon;
                            
                            contextMenu.Items.Add(suggestionItem);
                        }
                        
                        contextMenu.Items.Add(new Separator());
                    }
                    else
                    {
                        // No suggestions available
                        var noSuggestionsItem = new MenuItem
                        {
                            Header = "(No suggestions)",
                            IsEnabled = false,
                            FontStyle = FontStyles.Italic
                        };
                        contextMenu.Items.Add(noSuggestionsItem);
                        contextMenu.Items.Add(new Separator());
                    }
                    
                    // Add "Ignore" option
                    var ignoreItem = new MenuItem
                    {
                        Header = "Ignore",
                        Command = new RelayCommand(() => IgnoreSpellingError(spellingError))
                    };
                    var ignoreIcon = new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse("M19,6.41L17.59,5 12,10.59 6.41,5 5,6.41 10.59,12 5,17.59 6.41,19 12,13.41 17.59,19 19,17.59 13.41,12z"),
                        Fill = (Brush)FindResource("SystemControlForegroundBaseHighBrush"),
                        Stretch = Stretch.Uniform,
                        Width = 12,
                        Height = 12
                    };
                    ignoreItem.Icon = ignoreIcon;
                    contextMenu.Items.Add(ignoreItem);
                    
                    contextMenu.Items.Add(new Separator());
                }
                
                // Add standard editing commands
                AddStandardEditingItems(contextMenu);
                
                // Set the context menu
                ContextMenu = contextMenu;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Context menu creation failed: {ex.Message}");
                // Fall back to default context menu
                e.Handled = false;
            }
        }
        
        /// <summary>
        /// Add standard Cut/Copy/Paste items to context menu
        /// </summary>
        private void AddStandardEditingItems(ContextMenu menu)
        {
            // Cut
            var cutItem = new MenuItem
            {
                Header = "Cut",
                InputGestureText = "Ctrl+X",
                Command = ApplicationCommands.Cut,
                CommandTarget = this
            };
            var cutIcon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M19,3L13,9L15,11L22,4V3M12,12.5A0.5,0.5 0 0,1 11.5,12A0.5,0.5 0 0,1 12,11.5A0.5,0.5 0 0,1 12.5,12A0.5,0.5 0 0,1 12,12.5M6,20A2,2 0 0,1 4,18V8H6M7,22A2,2 0 0,0 9,20H7V22Z"),
                Fill = (Brush)FindResource("SystemControlForegroundBaseHighBrush"),
                Stretch = Stretch.Uniform,
                Width = 12,
                Height = 12
            };
            cutItem.Icon = cutIcon;
            menu.Items.Add(cutItem);
            
            // Copy
            var copyItem = new MenuItem
            {
                Header = "Copy",
                InputGestureText = "Ctrl+C", 
                Command = ApplicationCommands.Copy,
                CommandTarget = this
            };
            var copyIcon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M19,21H8V7H19M19,5H8A2,2 0 0,0 6,7V21A2,2 0 0,0 8,23H19A2,2 0 0,0 21,21V7A2,2 0 0,0 19,5M16,1H4A2,2 0 0,0 2,3V17H4V3H16V1Z"),
                Fill = (Brush)FindResource("SystemControlForegroundBaseHighBrush"),
                Stretch = Stretch.Uniform,
                Width = 12,
                Height = 12
            };
            copyItem.Icon = copyIcon;
            menu.Items.Add(copyItem);
            
            // Paste
            var pasteItem = new MenuItem
            {
                Header = "Paste",
                InputGestureText = "Ctrl+V",
                Command = ApplicationCommands.Paste,
                CommandTarget = this
            };
            var pasteIcon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M19,20H5V4H7V7H17V4H19M12,2A1,1 0 0,1 13,3A1,1 0 0,1 12,4A1,1 0 0,1 11,3A1,1 0 0,1 12,2M19,2H14.82C14.4,0.84 13.3,0 12,0C10.7,0 9.6,0.84 9.18,2H5A2,2 0 0,0 3,4V20A2,2 0 0,0 5,22H19A2,2 0 0,0 21,20V4A2,2 0 0,0 19,2Z"),
                Fill = (Brush)FindResource("SystemControlForegroundBaseHighBrush"),
                Stretch = Stretch.Uniform,
                Width = 12,
                Height = 12
            };
            pasteItem.Icon = pasteIcon;
            menu.Items.Add(pasteItem);
            
            menu.Items.Add(new Separator());
            
            // Select All
            var selectAllItem = new MenuItem
            {
                Header = "Select All",
                InputGestureText = "Ctrl+A",
                Command = ApplicationCommands.SelectAll,
                CommandTarget = this
            };
            var selectIcon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M5,3H7V5H5V3M13,3H19V5H13V3M19,13V19H13V13H19M19,21H13V19H19V21M11,3V5H9V3H11M7,13V19H5V13H7M5,7V11H3V7H5M9,7V11H7V7H9M3,3V5H1V3H3Z"),
                Fill = (Brush)FindResource("SystemControlForegroundBaseHighBrush"),
                Stretch = Stretch.Uniform,
                Width = 12,
                Height = 12
            };
            selectAllItem.Icon = selectIcon;
            menu.Items.Add(selectAllItem);
        }
        
        /// <summary>
        /// Replace misspelled word with suggestion
        /// </summary>
        private void ReplaceMisspelledWord(SpellingError error, string suggestion)
        {
            try
            {
                error.Correct(suggestion);
                System.Diagnostics.Debug.WriteLine($"[RTF] Corrected spelling: {suggestion}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Spelling correction failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Ignore spelling error for this session
        /// </summary>
        private void IgnoreSpellingError(SpellingError error)
        {
            try
            {
                error.IgnoreAll();
                System.Diagnostics.Debug.WriteLine("[RTF] Ignored spelling error");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Ignore spelling failed: {ex.Message}");
            }
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
                    e.Handled = HandleEnterKey();
                    break;
                    
                case Key.Tab:
                    if (IsInList())
                    {
                        e.Handled = HandleTabKey();
                    }
                    break;
                    
                case Key.Back:
                    if (IsInList() && IsAtStartOfListItem())
                    {
                        e.Handled = ExecuteListOperation(() => RemoveListFormattingFromCurrentItemSafe(), "Remove List Formatting");
                    }
                    break;
                    
                case Key.Delete:
                    if (IsInList() && IsAtEndOfListItem())
                    {
                        e.Handled = ExecuteListOperation(() => TryMergeWithNextListItem(), "Merge List Items");
                    }
                    break;
            }
        }
        
        #region Enhanced Keyboard Behaviors (SRP: Specific Key Handling)
        
        /// <summary>
        /// Handle Enter key with comprehensive list behavior
        /// Single Responsibility: Enter key logic
        /// </summary>
        private bool HandleEnterKey()
        {
            try
            {
                // Shift+Enter: soft line break (always)
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    EditingCommands.EnterLineBreak.Execute(null, this);
                    System.Diagnostics.Debug.WriteLine("[RTF] Shift+Enter: soft line break");
                    return true;
                }
                
                // Regular Enter in list context
                if (IsInList())
                {
                    if (IsEmptyListItem())
                    {
                        return ExecuteListOperation(() => ExitListSafe(), "Exit List");
                    }
                    else
                    {
                        return ExecuteListOperation(() => CreateNewListItemSafe(), "Create List Item");
                    }
                }
                
                return false; // Let WPF handle normal Enter
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Enter key handling failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Handle Tab key with safe indentation
        /// Single Responsibility: Tab key logic  
        /// </summary>
        private bool HandleTabKey()
        {
            try
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    return ExecuteListOperation(() => {
                        EditingCommands.DecreaseIndentation.Execute(null, this);
                        return true;
                    }, "Decrease Indentation");
                }
                else
                {
                    return ExecuteListOperation(() => {
                        EditingCommands.IncreaseIndentation.Execute(null, this);
                        return true;
                    }, "Increase Indentation");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Tab handling failed: {ex.Message}");
                return false;
            }
        }
        
        #endregion
        
        #region Enhanced List Operations (SRP: Safe List Manipulation)
        
        /// <summary>
        /// Safely exit list with transaction protection and caret restoration
        /// Single Responsibility: List exit logic
        /// </summary>
        private bool ExitListSafe()
        {
            try
            {
                var currentParagraph = CaretPosition.Paragraph;
                if (currentParagraph?.Parent is ListItem listItem && listItem.Parent is List list)
                {
                    var caretOffset = GetCaretCharacterOffset();
                    
                    // Create new paragraph after the list
                    var newParagraph = new Paragraph();
                    Document.Blocks.InsertAfter(list, newParagraph);
                    
                    // Remove empty list item
                    list.ListItems.Remove(listItem);
                    
                    // Clean up empty list
                    if (list.ListItems.Count == 0)
                    {
                        Document.Blocks.Remove(list);
                    }
                    
                    CaretPosition = newParagraph.ContentStart;
                    System.Diagnostics.Debug.WriteLine("[RTF] Safely exited list");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Safe list exit failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Safely create new list item with content splitting
        /// Single Responsibility: List item creation
        /// </summary>
        private bool CreateNewListItemSafe()
        {
            try
            {
                var currentParagraph = CaretPosition.Paragraph;
                if (currentParagraph?.Parent is ListItem currentItem && currentItem.Parent is List list)
                {
                    var paragraphOffset = GetCaretOffsetInParagraph();
                    
                    // Create new list item
                    var newParagraph = new Paragraph();
                    var newListItem = new ListItem();
                    newListItem.Blocks.Add(newParagraph);
                    
                    // Handle content splitting at caret position
                    if (paragraphOffset < new TextRange(currentParagraph.ContentStart, currentParagraph.ContentEnd).Text.Length)
                    {
                        // Split content: move content after caret to new item
                        var contentAfterCaret = new TextRange(CaretPosition, currentParagraph.ContentEnd);
                        if (!string.IsNullOrEmpty(contentAfterCaret.Text))
                        {
                            newParagraph.Inlines.Add(new Run(contentAfterCaret.Text));
                            contentAfterCaret.Text = ""; // Remove from current item
                        }
                    }
                    
                    // Insert new item after current
                    var currentIndex = GetListItemIndex(list, currentItem);
                    InsertListItemAt(list, currentIndex + 1, newListItem);
                    
                    CaretPosition = newParagraph.ContentStart;
                    System.Diagnostics.Debug.WriteLine("[RTF] Safely created new list item with content splitting");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Safe list item creation failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Safely remove list formatting with transaction protection
        /// Single Responsibility: List formatting removal
        /// </summary>
        private bool RemoveListFormattingFromCurrentItemSafe()
        {
            try
            {
                var paragraph = CaretPosition.Paragraph;
                if (paragraph?.Parent is ListItem listItem && listItem.Parent is List list)
                {
                    var caretOffset = GetCaretOffsetInParagraph();
                    
                    // Preserve content with formatting
                    var textRange = new TextRange(listItem.ContentStart, listItem.ContentEnd);
                    var content = textRange.Text;
                    
                    // Create new paragraph with preserved formatting
                    var newParagraph = new Paragraph();
                    try
                    {
                        // Try to preserve rich formatting
                        using (var stream = new MemoryStream())
                        {
                            textRange.Save(stream, DataFormats.Xaml);
                            stream.Position = 0;
                            var targetRange = new TextRange(newParagraph.ContentStart, newParagraph.ContentEnd);
                            targetRange.Load(stream, DataFormats.Xaml);
                        }
                    }
                    catch
                    {
                        // Fallback to plain text
                        newParagraph.Inlines.Add(new Run(content));
                    }
                    
                    // Insert before list and remove list item
                    Document.Blocks.InsertBefore(list, newParagraph);
                    list.ListItems.Remove(listItem);
                    
                    // Remove list if empty
                    if (list.ListItems.Count == 0)
                    {
                        Document.Blocks.Remove(list);
                    }
                    
                    RestoreCaretInParagraph(newParagraph, caretOffset);
                    System.Diagnostics.Debug.WriteLine("[RTF] Safely removed list formatting with formatting preservation");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Safe list formatting removal failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Check if caret is at end of current list item
        /// Single Responsibility: End-of-item detection
        /// </summary>
        private bool IsAtEndOfListItem()
        {
            try
            {
                var paragraph = CaretPosition?.Paragraph;
                if (paragraph?.Parent is ListItem listItem)
                {
                    return CaretPosition.CompareTo(listItem.ContentEnd) >= 0;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Safely merge current list item with next item
        /// Single Responsibility: List item merging
        /// </summary>
        private bool TryMergeWithNextListItem()
        {
            try
            {
                var currentParagraph = CaretPosition?.Paragraph;
                if (currentParagraph?.Parent is ListItem currentItem && currentItem.Parent is List list)
                {
                    var currentIndex = GetListItemIndex(list, currentItem);
                    var nextItem = GetListItemAt(list, currentIndex + 1);
                    
                    if (nextItem?.Blocks.FirstBlock is Paragraph nextParagraph)
                    {
                        var mergePosition = currentParagraph.ContentEnd;
                        
                        // Move content from next item to current
                        var contentRange = new TextRange(nextParagraph.ContentStart, nextParagraph.ContentEnd);
                        if (!string.IsNullOrEmpty(contentRange.Text))
                        {
                            var targetRange = new TextRange(mergePosition, mergePosition);
                            targetRange.Text = contentRange.Text;
                        }
                        
                        // Remove the merged item
                        list.ListItems.Remove(nextItem);
                        
                        // Position caret at merge point
                        CaretPosition = mergePosition;
                        
                        System.Diagnostics.Debug.WriteLine("[RTF] Safely merged with next list item");
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] List item merge failed: {ex.Message}");
                return false;
            }
        }
        
        #endregion
        
        private bool IsEmptyListItem()
        {
            if (CaretPosition.Paragraph?.Parent is ListItem listItem)
            {
                var text = new TextRange(listItem.ContentStart, listItem.ContentEnd).Text;
                return string.IsNullOrWhiteSpace(text?.Trim());
            }
            return false;
        }
        
        /// <summary>
        /// Check if caret is at start of current list item
        /// Single Responsibility: Start-of-item detection
        /// </summary>
        private bool IsAtStartOfListItem()
        {
            try
            {
                var paragraph = CaretPosition?.Paragraph;
                if (paragraph?.Parent is ListItem listItem)
                {
                    // Check if caret is at the very start of the list item content
                    var range = new TextRange(paragraph.ContentStart, CaretPosition);
                    var textBeforeCaret = range.Text;
                    return string.IsNullOrEmpty(textBeforeCaret) || string.IsNullOrWhiteSpace(textBeforeCaret);
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Start-of-item detection failed: {ex.Message}");
                return false;
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
        /// Perform intelligent RTF paste with security validation and theme-aware color conversion
        /// </summary>
        private void PerformSmartRTFPaste()
        {
            try
            {
                // Determine current theme from application resources
                var isDarkMode = IsCurrentThemeDark();
                
                // For RTF editor, we can accept rich formatting but validate security
                if (Clipboard.ContainsData(DataFormats.Rtf))
                {
                    var rtfContent = Clipboard.GetData(DataFormats.Rtf) as string;
                    if (IsValidRTF(rtfContent))
                    {
                        // Sanitize and apply theme-aware color conversion to RTF content
                        var sanitizedRTF = SanitizeRTFContent(rtfContent);
                        var themeAwareRTF = ApplyThemeToRTFContent(sanitizedRTF, isDarkMode);
                        
                        var range = Selection.IsEmpty ? 
                            new TextRange(CaretPosition, CaretPosition) : Selection;
                        
                        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(themeAwareRTF)))
                        {
                            range.Load(stream, DataFormats.Rtf);
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"[RTF] Theme-aware RTF paste completed for {(isDarkMode ? "dark" : "light")} theme");
                        return;
                    }
                }
                
                // Fallback to theme-aware plain text paste
                if (Clipboard.ContainsText())
                {
                    var plainText = CleanPastedText(Clipboard.GetText());
                    
                    if (Selection.IsEmpty)
                    {
                        CaretPosition.InsertTextInRun(plainText);
                        // Apply theme to newly inserted text
                        ApplyThemeToNewText(CaretPosition, plainText.Length, isDarkMode);
                    }
                    else
                    {
                        Selection.Text = plainText;
                        // Apply theme to the selection
                        ApplyThemeToSelection(isDarkMode);
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[RTF] Theme-aware plain text paste completed");
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
        
        #region Transaction Infrastructure (SRP: Operation Safety)
        
        /// <summary>
        /// Begin list operation transaction for undo support and data integrity
        /// Single Responsibility: Transaction management
        /// </summary>
        private void BeginListOperation()
        {
            try
            {
                BeginChange();
                System.Diagnostics.Debug.WriteLine("[RTF] List operation transaction started");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Failed to begin transaction: {ex.Message}");
            }
        }
        
        /// <summary>
        /// End list operation transaction with automatic rollback on failure
        /// Single Responsibility: Transaction completion
        /// </summary>
        private void EndListOperation()
        {
            try
            {
                EndChange();
                System.Diagnostics.Debug.WriteLine("[RTF] List operation transaction completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Failed to end transaction: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Execute list operation with automatic transaction management
        /// Single Responsibility: Safe operation execution
        /// </summary>
        private bool ExecuteListOperation(Func<bool> operation, string operationName)
        {
            BeginListOperation();
            try
            {
                var result = operation();
                System.Diagnostics.Debug.WriteLine($"[RTF] {operationName} {(result ? "succeeded" : "failed")}");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] {operationName} threw exception: {ex.Message}");
                return false;
            }
            finally
            {
                EndListOperation();
            }
        }
        
        #endregion
        
        #region Position Management (SRP: Caret Tracking)
        
        /// <summary>
        /// Get current caret position as character offset from document start
        /// Single Responsibility: Position calculation
        /// </summary>
        private int GetCaretCharacterOffset()
        {
            try
            {
                var start = Document.ContentStart;
                var caret = CaretPosition ?? start;
                var range = new TextRange(start, caret);
                return range.Text.Length;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Caret offset calculation failed: {ex.Message}");
                return 0;
            }
        }
        
        /// <summary>
        /// Get current caret position relative to current paragraph
        /// Single Responsibility: Paragraph-relative positioning
        /// </summary>
        private int GetCaretOffsetInParagraph()
        {
            try
            {
                var paragraph = CaretPosition?.Paragraph;
                if (paragraph == null) return 0;
                
                var range = new TextRange(paragraph.ContentStart, CaretPosition);
                return range.Text.Length;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Paragraph offset calculation failed: {ex.Message}");
                return 0;
            }
        }
        
        /// <summary>
        /// Restore caret position using character offset from document start
        /// Single Responsibility: Position restoration
        /// </summary>
        private void RestoreCaretPosition(int characterOffset)
        {
            try
            {
                var position = Document.ContentStart;
                for (int i = 0; i < characterOffset && position != null; i++)
                {
                    var next = position.GetNextInsertionPosition(LogicalDirection.Forward);
                    if (next == null) break;
                    position = next;
                }
                
                if (position != null)
                {
                    CaretPosition = position;
                    System.Diagnostics.Debug.WriteLine($"[RTF] Caret restored to offset {characterOffset}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Caret restoration failed: {ex.Message}");
                // Fallback to document start
                CaretPosition = Document.ContentStart;
            }
        }
        
        /// <summary>
        /// Restore caret position within specific paragraph using relative offset
        /// Single Responsibility: Paragraph-relative restoration
        /// </summary>
        private void RestoreCaretInParagraph(Paragraph paragraph, int paragraphOffset)
        {
            try
            {
                if (paragraph == null) return;
                
                var position = paragraph.ContentStart;
                for (int i = 0; i < paragraphOffset && position != null; i++)
                {
                    var next = position.GetNextInsertionPosition(LogicalDirection.Forward);
                    if (next == null || next.CompareTo(paragraph.ContentEnd) > 0) break;
                    position = next;
                }
                
                CaretPosition = position ?? paragraph.ContentStart;
                System.Diagnostics.Debug.WriteLine($"[RTF] Caret restored in paragraph at offset {paragraphOffset}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Paragraph caret restoration failed: {ex.Message}");
                CaretPosition = paragraph?.ContentStart ?? Document.ContentStart;
            }
        }
        
        #endregion
        
        #region List Validation & Helper Methods (SRP: Data Integrity)
        
        /// <summary>
        /// Validate list structure integrity
        /// Single Responsibility: Structure validation
        /// </summary>
        private bool ValidateListStructure(List list)
        {
            if (list == null) return false;
            
            try
            {
                int itemCount = 0;
                foreach (var item in list.ListItems)
                {
                    itemCount++;
                    if (item.Blocks.Count == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("[RTF] Warning: Found list item with no blocks");
                        return false;
                    }
                }
                return itemCount > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] List validation failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Get index of list item within its parent list
        /// Single Responsibility: Index calculation
        /// </summary>
        private int GetListItemIndex(List list, ListItem targetItem)
        {
            try
            {
                int index = 0;
                foreach (var item in list.ListItems)
                {
                    if (item == targetItem) return index;
                    index++;
                }
                return -1; // Not found
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] List item index calculation failed: {ex.Message}");
                return -1;
            }
        }
        
        /// <summary>
        /// Get list item at specific index
        /// Single Responsibility: Item retrieval
        /// </summary>
        private ListItem GetListItemAt(List list, int index)
        {
            try
            {
                if (index < 0 || index >= list.ListItems.Count) return null;
                
                int currentIndex = 0;
                foreach (var item in list.ListItems)
                {
                    if (currentIndex == index) return item;
                    currentIndex++;
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] List item retrieval failed: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Insert list item at specific index
        /// Single Responsibility: Item insertion
        /// </summary>
        private void InsertListItemAt(List list, int index, ListItem newItem)
        {
            try
            {
                if (index < 0) index = 0;
                if (index >= list.ListItems.Count)
                {
                    list.ListItems.Add(newItem);
                }
                else
                {
                    // WPF ListItemCollection doesn't have Insert - we need to rebuild the collection
                    var itemsToAdd = new System.Collections.Generic.List<ListItem>();
                    
                    // Collect items before insertion point
                    for (int i = 0; i < index; i++)
                    {
                        itemsToAdd.Add(GetListItemAt(list, i));
                    }
                    
                    // Add new item
                    itemsToAdd.Add(newItem);
                    
                    // Collect remaining items
                    for (int i = index; i < list.ListItems.Count; i++)
                    {
                        itemsToAdd.Add(GetListItemAt(list, i));
                    }
                    
                    // Clear and rebuild collection
                    list.ListItems.Clear();
                    foreach (var item in itemsToAdd)
                    {
                        if (item != null) list.ListItems.Add(item);
                    }
                }
                System.Diagnostics.Debug.WriteLine($"[RTF] Inserted list item at index {index}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] List item insertion failed: {ex.Message}");
                // Fallback to append
                try { list.ListItems.Add(newItem); } catch { }
            }
        }
        
        #endregion
        
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
                
                // Apply comprehensive spell check settings
                ApplySpellCheckSettings(settings.EnableSpellCheck, settings.SpellCheckLanguage);
                
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
        /// Apply comprehensive spell check settings with proper visual indicator setup
        /// </summary>
        private void ApplySpellCheckSettings(bool enableSpellCheck, string spellCheckLanguage)
        {
            try
            {
                // Apply spell check settings to both control and document
                SpellCheck.SetIsEnabled(this, enableSpellCheck);
                SpellCheck.SetSpellingReform(this, SpellingReform.PreAndPostreform);
                Language = System.Windows.Markup.XmlLanguage.GetLanguage(spellCheckLanguage);
                
                if (Document != null)
                {
                    Document.Language = System.Windows.Markup.XmlLanguage.GetLanguage(spellCheckLanguage);
                }
                
                // If spell check is being enabled, trigger a refresh to show red squiggly lines
                if (enableSpellCheck)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            // Force spell check refresh on existing content
                            if (Document != null)
                            {
                                var range = new TextRange(Document.ContentStart, Document.ContentEnd);
                                if (!string.IsNullOrWhiteSpace(range.Text))
                                {
                                    // Trigger spell check by simulating content change
                                    var originalText = range.Text;
                                    var caretPos = CaretPosition;
                                    range.Text = originalText + " ";
                                    range.Text = originalText;
                                    CaretPosition = caretPos;
                                }
                            }
                            
                            System.Diagnostics.Debug.WriteLine($"[RTF] Spell check settings applied and refreshed - EnableSpellCheck: {enableSpellCheck}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[RTF] Spell check refresh after settings failed: {ex.Message}");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Spell check settings application failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Apply theme-aware colors to RTF editor while preserving spell check underlines
        /// Enhanced to prevent spell check visibility issues
        /// </summary>
        public void ApplyTheme(bool isDarkMode)
        {
            if (_disposed) return;
            
            try
            {
                // STEP 1: Preserve spell check state before theme changes
                PreserveSpellCheckDuringThemeChange();
                
                // STEP 2: Update editor-level theme colors
                var foregroundBrush = isDarkMode 
                    ? new SolidColorBrush(Colors.White) 
                    : new SolidColorBrush(Colors.Black);
                
                this.Foreground = foregroundBrush;
                
                // STEP 3: Apply ONLY theme-related styles (avoid spell check interference)
                if (Document != null)
                {
                    // Use careful theme application that preserves spell check
                    UpdateDocumentThemeStylesOnly(isDarkMode);
                    
                    // Apply limited content theming that won't interfere with spell check
                    ApplyMinimalContentTheming(isDarkMode);
                }
                
                System.Diagnostics.Debug.WriteLine($"[RTF] Applied {(isDarkMode ? "dark" : "light")} theme with spell check preservation");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Theme application failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Apply minimal content theming that won't interfere with spell check underlines
        /// Single Responsibility: Careful content theming
        /// </summary>
        private void ApplyMinimalContentTheming(bool isDarkMode)
        {
            try
            {
                // Only update content colors for very problematic cases (black text on dark theme, white text on light theme)
                var targetColor = isDarkMode ? Colors.White : Colors.Black;
                var targetBrush = new SolidColorBrush(targetColor);
                
                // Walk through content carefully - only update obvious problematic colors
                var walker = Document.ContentStart;
                while (walker != null && walker.CompareTo(Document.ContentEnd) < 0)
                {
                    if (walker.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart)
                    {
                        var element = walker.GetAdjacentElement(LogicalDirection.Forward);
                        if (element is Run run && IsObviouslyProblematicColor(run, isDarkMode))
                        {
                            // Only update very problematic colors that would be completely invisible
                            run.Foreground = targetBrush;
                        }
                    }
                    
                    walker = walker.GetNextContextPosition(LogicalDirection.Forward);
                }
                
                System.Diagnostics.Debug.WriteLine($"[RTF] Minimal content theming applied for {(isDarkMode ? "dark" : "light")} theme");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Minimal content theming failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Check if a color is obviously problematic (completely invisible) in current theme
        /// Single Responsibility: Problematic color detection
        /// </summary>
        private bool IsObviouslyProblematicColor(Run run, bool isDarkMode)
        {
            if (run.Foreground is SolidColorBrush brush)
            {
                var color = brush.Color;
                var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
                
                if (isDarkMode)
                {
                    // Only convert pure black or extremely dark colors (luminance < 0.1)
                    return luminance < 0.1;
                }
                else
                {
                    // Only convert pure white or extremely light colors (luminance > 0.9)
                    return luminance > 0.9;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Apply theme colors to existing document content
        /// </summary>
        private void ApplyThemeToDocumentContent(bool isDarkMode)
        {
            try
            {
                var targetColor = isDarkMode ? Colors.White : Colors.Black;
                var targetBrush = new SolidColorBrush(targetColor);
                
                var documentRange = new TextRange(Document.ContentStart, Document.ContentEnd);
                
                // Walk through all runs and update problematic colors
                var walker = Document.ContentStart;
                while (walker != null && walker.CompareTo(Document.ContentEnd) < 0)
                {
                    if (walker.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart)
                    {
                        var element = walker.GetAdjacentElement(LogicalDirection.Forward);
                        if (element is Run run && ShouldUpdateRunColor(run, isDarkMode))
                        {
                            run.Foreground = targetBrush;
                        }
                        else if (element is Span span && ShouldUpdateSpanColor(span, isDarkMode))
                        {
                            span.Foreground = targetBrush;
                        }
                    }
                    
                    walker = walker.GetNextContextPosition(LogicalDirection.Forward);
                }
                
                System.Diagnostics.Debug.WriteLine($"[RTF] Updated document content for {(isDarkMode ? "dark" : "light")} theme");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Content theme update failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Determine if a Run's color should be updated for theme compatibility
        /// </summary>
        private bool ShouldUpdateRunColor(Run run, bool isDarkMode)
        {
            if (run.Foreground is SolidColorBrush brush)
            {
                var color = brush.Color;
                
                if (isDarkMode)
                {
                    // In dark mode, convert very dark text (likely black) to white
                    return IsVeryDarkColor(color);
                }
                else
                {
                    // In light mode, convert very light text (likely white) to black
                    return IsVeryLightColor(color);
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Determine if a Span's color should be updated for theme compatibility
        /// </summary>
        private bool ShouldUpdateSpanColor(Span span, bool isDarkMode)
        {
            if (span.Foreground is SolidColorBrush brush)
            {
                var color = brush.Color;
                
                if (isDarkMode)
                {
                    return IsVeryDarkColor(color);
                }
                else
                {
                    return IsVeryLightColor(color);
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Check if color is very dark (problematic in dark theme)
        /// </summary>
        private bool IsVeryDarkColor(Color color)
        {
            // Calculate luminance using standard formula
            var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
            return luminance < 0.2; // Very dark colors
        }
        
        /// <summary>
        /// Check if color is very light (problematic in light theme)  
        /// </summary>
        private bool IsVeryLightColor(Color color)
        {
            var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
            return luminance > 0.8; // Very light colors
        }
        
        /// <summary>
        /// Update document default styles for theme
        /// </summary>
        private void UpdateDocumentDefaultStyles(Color defaultColor)
        {
            try
            {
                var defaultBrush = new SolidColorBrush(defaultColor);
                
                // Update paragraph style
                if (Document.Resources[typeof(Paragraph)] is Style paraStyle)
                {
                    UpdateStyleForeground(paraStyle, Paragraph.ForegroundProperty, defaultBrush);
                }
                
                // Update list style
                if (Document.Resources[typeof(List)] is Style listStyle)
                {
                    UpdateStyleForeground(listStyle, List.ForegroundProperty, defaultBrush);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Default style update failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Helper method to update style foreground
        /// </summary>
        private void UpdateStyleForeground(Style style, DependencyProperty property, Brush brush)
        {
            var foregroundSetter = style.Setters.OfType<Setter>()
                .FirstOrDefault(s => s.Property == property);
            
            if (foregroundSetter != null)
            {
                foregroundSetter.Value = brush;
            }
            else
            {
                style.Setters.Add(new Setter(property, brush));
            }
        }
        
        /// <summary>
        /// Determine if current theme is dark mode
        /// </summary>
        private bool IsCurrentThemeDark()
        {
            try
            {
                // Check the current theme by examining system colors
                var foregroundColor = ((SolidColorBrush)FindResource("SystemControlForegroundBaseHighBrush")).Color;
                var luminance = (0.299 * foregroundColor.R + 0.587 * foregroundColor.G + 0.114 * foregroundColor.B) / 255.0;
                return luminance > 0.5; // If foreground is light, we're in dark mode
            }
            catch
            {
                // Default to light theme if detection fails
                return false;
            }
        }
        
        /// <summary>
        /// Apply theme-aware color conversion to RTF content string
        /// </summary>
        private string ApplyThemeToRTFContent(string rtfContent, bool isDarkMode)
        {
            try
            {
                if (string.IsNullOrEmpty(rtfContent)) return rtfContent;
                
                var processed = rtfContent;
                
                if (isDarkMode)
                {
                    // Convert black text colors to white in dark mode
                    // RTF uses \cf0 for default color, \cf1 for black, etc.
                    processed = System.Text.RegularExpressions.Regex.Replace(processed, 
                        @"\\red0\\green0\\blue0;", 
                        @"\red255\green255\blue255;", // Convert black to white
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    
                    // Convert very dark colors to lighter equivalents
                    processed = ConvertDarkColorsInRTF(processed);
                }
                else
                {
                    // Convert white text colors to black in light mode
                    processed = System.Text.RegularExpressions.Regex.Replace(processed, 
                        @"\\red255\\green255\\blue255;", 
                        @"\red0\green0\blue0;", // Convert white to black
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    
                    // Convert very light colors to darker equivalents
                    processed = ConvertLightColorsInRTF(processed);
                }
                
                return processed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Theme conversion failed: {ex.Message}");
                return rtfContent; // Return original if conversion fails
            }
        }
        
        /// <summary>
        /// Convert dark colors to lighter ones in RTF content
        /// </summary>
        private string ConvertDarkColorsInRTF(string rtf)
        {
            // Pattern to match RTF color table entries
            var colorPattern = @"\\red(\d+)\\green(\d+)\\blue(\d+);";
            return System.Text.RegularExpressions.Regex.Replace(rtf, colorPattern, (match) =>
            {
                if (int.TryParse(match.Groups[1].Value, out int r) &&
                    int.TryParse(match.Groups[2].Value, out int g) &&
                    int.TryParse(match.Groups[3].Value, out int b))
                {
                    var luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
                    
                    if (luminance < 0.2) // Very dark colors
                    {
                        // Convert to light equivalents
                        r = Math.Min(255, r + 200);
                        g = Math.Min(255, g + 200);
                        b = Math.Min(255, b + 200);
                        return $"\\red{r}\\green{g}\\blue{b};";
                    }
                }
                return match.Value; // Keep original if not very dark
            });
        }
        
        /// <summary>
        /// Convert light colors to darker ones in RTF content
        /// </summary>
        private string ConvertLightColorsInRTF(string rtf)
        {
            var colorPattern = @"\\red(\d+)\\green(\d+)\\blue(\d+);";
            return System.Text.RegularExpressions.Regex.Replace(rtf, colorPattern, (match) =>
            {
                if (int.TryParse(match.Groups[1].Value, out int r) &&
                    int.TryParse(match.Groups[2].Value, out int g) &&
                    int.TryParse(match.Groups[3].Value, out int b))
                {
                    var luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
                    
                    if (luminance > 0.8) // Very light colors
                    {
                        // Convert to dark equivalents
                        r = Math.Max(0, r - 200);
                        g = Math.Max(0, g - 200);
                        b = Math.Max(0, b - 200);
                        return $"\\red{r}\\green{g}\\blue{b};";
                    }
                }
                return match.Value; // Keep original if not very light
            });
        }
        
        /// <summary>
        /// Apply theme colors to current selection
        /// </summary>
        private void ApplyThemeToSelection(bool isDarkMode)
        {
            try
            {
                var targetBrush = isDarkMode 
                    ? new SolidColorBrush(Colors.White) 
                    : new SolidColorBrush(Colors.Black);
                
                Selection.ApplyPropertyValue(TextElement.ForegroundProperty, targetBrush);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] Selection theme application failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Apply theme colors to newly inserted text
        /// </summary>
        private void ApplyThemeToNewText(TextPointer position, int length, bool isDarkMode)
        {
            try
            {
                var targetBrush = isDarkMode 
                    ? new SolidColorBrush(Colors.White) 
                    : new SolidColorBrush(Colors.Black);
                
                // Create selection for the newly inserted text
                var start = position.GetPositionAtOffset(-length) ?? position;
                var range = new TextRange(start, position);
                range.ApplyPropertyValue(TextElement.ForegroundProperty, targetBrush);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] New text theme application failed: {ex.Message}");
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
