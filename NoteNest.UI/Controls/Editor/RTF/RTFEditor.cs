using System;
using System.Windows.Input;
using System.Windows.Documents;
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
                
                System.Diagnostics.Debug.WriteLine("[RTFEditor] Managed events wired up");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Event wiring failed: {ex.Message}");
                // Fallback to direct subscription
                TextChanged += OnTextChanged;
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
        /// Insert bulleted list at current position
        /// </summary>
        public void InsertBulletList()
        {
            try
            {
                if (Selection.IsEmpty)
                {
                    Selection.Text = "• ";
                }
                else
                {
                    // Apply bullet formatting to selection
                    EditingCommands.ToggleBullets.Execute(null, this);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Insert bullet list failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Insert numbered list at current position
        /// </summary>
        public void InsertNumberedList()
        {
            try
            {
                if (Selection.IsEmpty)
                {
                    Selection.Text = "1. ";
                }
                else
                {
                    // Apply numbered formatting to selection
                    EditingCommands.ToggleNumbering.Execute(null, this);
                }
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
        /// </summary>
        private void RefreshDocumentStylesAfterLoad()
        {
            try
            {
                // Reapply document-level single spacing
                Document.PagePadding = new System.Windows.Thickness(0);
                Document.LineHeight = double.NaN;
                
                // Apply single spacing to all existing paragraphs
                foreach (var block in Document.Blocks)
                {
                    if (block is Paragraph para)
                    {
                        para.Margin = new System.Windows.Thickness(0, 0, 0, 0);
                        para.LineHeight = double.NaN;
                    }
                    else if (block is List list)
                    {
                        list.Margin = new System.Windows.Thickness(0, 0, 0, 6);
                        list.Padding = new System.Windows.Thickness(0);
                        
                        // Apply to list items
                        foreach (var listItem in list.ListItems)
                        {
                            listItem.Margin = new System.Windows.Thickness(0);
                            listItem.Padding = new System.Windows.Thickness(0, 0, 0, 2);
                            
                            // Apply to paragraphs within list items
                            foreach (var itemBlock in listItem.Blocks)
                            {
                                if (itemBlock is Paragraph itemPara)
                                {
                                    itemPara.Margin = new System.Windows.Thickness(0);
                                    itemPara.LineHeight = double.NaN;
                                }
                            }
                        }
                    }
                }
                
                System.Diagnostics.Debug.WriteLine("[RTFEditor] Document styles refreshed after RTF load");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditor] Document style refresh failed: {ex.Message}");
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
                
                // Apply spell check settings (when implemented in clean architecture)
                System.Windows.Controls.SpellCheck.SetIsEnabled(this, settings.EnableSpellCheck);
                if (!string.IsNullOrEmpty(settings.SpellCheckLanguage))
                {
                    Language = System.Windows.Markup.XmlLanguage.GetLanguage(settings.SpellCheckLanguage);
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
}
