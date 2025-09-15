using System;
using System.IO;
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
    public class RTFTextEditor : RichTextBox, INotesEditor
    {
        private bool _isDirty = false;
        private string _originalContent = string.Empty;
        private readonly DispatcherTimer _stateUpdateTimer;
        private readonly DispatcherTimer _contentChangeTimer;
        
        // Performance optimization: Content caching with longer timeout for debouncing
        private string _cachedContent;
        private DateTime _lastCacheTime;
        private readonly TimeSpan _cacheTimeout = TimeSpan.FromMilliseconds(200);
        
        public event EventHandler ContentChanged;
        public event EventHandler<ListStateChangedEventArgs> ListStateChanged;
        
        public NoteFormat Format => NoteFormat.RTF;
        public bool IsDirty => _isDirty;
        public string OriginalContent => _originalContent;
        
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
            
            // Initialize content change timer for proper debouncing (matches FormattedTextEditor pattern)
            _contentChangeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250) // Debounce content notifications
            };
            _contentChangeTimer.Tick += OnContentChangeTimerTick;
            
            // Handle save shortcut - but don't trigger immediate ContentChanged
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, 
                (s, e) => 
                {
                    // Force immediate save via timer trigger
                    _contentChangeTimer.Stop();
                    OnContentChangeTimerTick(this, EventArgs.Empty);
                }));
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
            
            try
            {
                // Load RTF content natively - no conversion!
                var range = new TextRange(Document.ContentStart, Document.ContentEnd);
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
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
            
            // Stop any pending content change notifications since we're clean
            _contentChangeTimer.Stop();
        }
        
        /// <summary>
        /// Force immediate content change notification (bypasses debouncing)
        /// Used for manual saves, tab switches, etc.
        /// </summary>
        public void ForceContentNotification()
        {
            _contentChangeTimer.Stop();
            ContentChanged?.Invoke(this, EventArgs.Empty);
            System.Diagnostics.Debug.WriteLine($"[RTF] Forced immediate content notification");
        }
        
        public void MarkDirty()
        {
            if (!_isDirty)
            {
                _isDirty = true;
                // DON'T fire ContentChanged immediately - use debounced timer instead
            }
        }
        
        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            MarkDirty();
            
            // Start debounced timers - this prevents save-while-typing
            _stateUpdateTimer.Stop();
            _stateUpdateTimer.Start();
            
            _contentChangeTimer.Stop();
            _contentChangeTimer.Start();
            
            // Cache invalidation delayed to allow reuse during rapid typing
            _ = Task.Delay(50).ContinueWith(_ => _cachedContent = null);
        }
        
        private void OnSelectionChanged(object sender, RoutedEventArgs e)
        {
            _stateUpdateTimer.Stop();
            _stateUpdateTimer.Start();
        }
        
        /// <summary>
        /// Debounced content change notification - prevents save-while-typing
        /// </summary>
        private void OnContentChangeTimerTick(object sender, EventArgs e)
        {
            _contentChangeTimer.Stop();
            
            // Fire ContentChanged only after user stops typing for 250ms
            ContentChanged?.Invoke(this, EventArgs.Empty);
            
            System.Diagnostics.Debug.WriteLine($"[RTF] Debounced content change notification fired");
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
    }
}
