using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Interfaces.Services;
using NoteNest.UI.Controls.Editor.RTF;

namespace NoteNest.UI.ViewModels
{
    public class NoteTabItem : ViewModelBase, ITabItem, IDisposable
    {
        private readonly ISaveManager _saveManager;
        private readonly ISupervisedTaskRunner _taskRunner;
        private readonly string _noteId;
        private RTFEditor _editor;
        private RTFToolbar _toolbar;
        private Grid _contentGrid;
        private string _content;
        private bool _isSaving;
        private bool _localIsDirty;
        private bool _contentLoaded = false;
        private bool _disposed = false;
        
        // PROPER ARCHITECTURE: Each tab manages its own save timing
        private DispatcherTimer _walTimer;
        private DispatcherTimer _autoSaveTimer;
        private DateTime _lastModification;
        private bool _walSaved;

        public string NoteId => _noteId;
        public NoteModel Note { get; }
        public string Id => _noteId;
        
        // ENHANCED: Direct access for code (toolbar, save operations)
        public RTFEditor Editor => _editor;
        
        // ENHANCED: Complete UI container for tab content (WPF binding)
        public Grid TabContent => _contentGrid;
        
        // ENHANCED: Individual component access
        public Grid EditorContainer => _contentGrid;
        public RTFToolbar Toolbar => _toolbar;
        
        public string Title
        {
            get
            {
                var baseTitle = Note.Title;
                if (IsSaving)
                    return $"{baseTitle} (saving...)";
                return IsDirty ? $"{baseTitle} •" : baseTitle;
            }
        }
        
        public string Content
        {
            get => _content;
            set
            {
                if (SetProperty(ref _content, value))
                {
                    UpdateContent(value);
                }
            }
        }

        public void UpdateContent(string content)
        {
            if (string.IsNullOrEmpty(_noteId))
                return;
                
            // CRITICAL: Update the backing field!
            _content = content;
            Note.Content = content;
            
            // Update SaveManager
            _saveManager?.UpdateContent(_noteId, content);
            
            // Set dirty flag immediately for instant UI feedback
            IsDirty = true;
        }
        
        public bool IsDirty
        {
            get => _localIsDirty;
            set
            {
                if (_localIsDirty != value)
                {
                    _localIsDirty = value;
                    OnPropertyChanged(nameof(IsDirty));
                    OnPropertyChanged(nameof(Title));
                }
            }
        }
        
        public bool IsSaving
        {
            get => _isSaving;
            private set
            {
                if (SetProperty(ref _isSaving, value))
                {
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        public NoteTabItem(NoteModel note, ISaveManager saveManager, ISupervisedTaskRunner taskRunner = null)
        {
            var instanceId = this.GetHashCode();
            System.Diagnostics.Debug.WriteLine($"[NoteTabItem] NEW TAB-OWNED INSTANCE {instanceId}: note.Id={note?.Id}, note.Title={note?.Title}, saveManager={saveManager != null}");
            
            Note = note ?? throw new ArgumentNullException(nameof(note));
            _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            _taskRunner = taskRunner; // Allow null for backward compatibility 
            _noteId = note.Id;
            _content = note.Content ?? "";
            
            System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Instance {instanceId} initialized: _noteId={_noteId}, contentLength={_content.Length}");
            
            try
            {
                // BUILD THE COMPLETE UI IN CODE - WE OWN EVERYTHING
                _contentGrid = BuildEditorUI();
                
                // ENHANCED: Wire up save chain in constructor (bulletproof event chain)
                _editor.ContentChanged += OnEditorContentChanged;
                
                System.Diagnostics.Debug.WriteLine($"[NoteTabItem] UI built successfully for {Note.Title}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NoteTabItem] UI creation failed: {ex.Message}");
                
                // Fallback to basic editor
                try
                {
                    _editor = new RTFEditor();
                    _editor.ContentChanged += OnEditorContentChanged;
                    _contentGrid = new Grid();
                    _contentGrid.Children.Add(_editor);
                    System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Fallback editor created for {Note.Title}");
                }
                catch (Exception fallbackEx)
                {
                    // Ultimate fallback
                    System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Complete failure: {fallbackEx.Message}");
                    _contentGrid = new Grid();
                    var errorText = new System.Windows.Controls.TextBlock 
                    { 
                        Text = "Failed to create editor", 
                        Foreground = System.Windows.Media.Brushes.Red 
                    };
                    _contentGrid.Children.Add(errorText);
                }
            }
            
            // Use weak event pattern to prevent memory leaks
            WeakEventManager<ISaveManager, NoteSavedEventArgs>
                .AddHandler(_saveManager, nameof(ISaveManager.NoteSaved), OnNoteSaved);
            
            WeakEventManager<ISaveManager, SaveProgressEventArgs>
                .AddHandler(_saveManager, nameof(ISaveManager.SaveStarted), OnSaveStarted);
                
            WeakEventManager<ISaveManager, SaveProgressEventArgs>
                .AddHandler(_saveManager, nameof(ISaveManager.SaveCompleted), OnSaveCompleted);
                
            // PROPER ARCHITECTURE: Initialize save timers for this tab
            InitializeSaveTimers();
            
            System.Diagnostics.Debug.WriteLine($"[NoteTabItem] TAB-OWNED constructor completed for noteId={_noteId} with complete UI");
        }
        
        /// <summary>
        /// Build the complete editor UI in code - toolbar + editor in grid
        /// This is the core of the Tab-Owned Editor Pattern
        /// </summary>
        private Grid BuildEditorUI()
        {
            var grid = new Grid();
            
            // Setup grid structure
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // Toolbar
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Editor
            
            // Apply grid style from resources if available
            try
            {
                if (Application.Current.TryFindResource("TabContentGridStyle") is Style gridStyle)
                {
                    grid.Style = gridStyle;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BuildUI] Grid styling failed: {ex.Message}");
            }
            
            // Create toolbar
            _toolbar = new RTFToolbar();
            Grid.SetRow(_toolbar, 0);
            
            // Apply toolbar style if available
            try
            {
                if (Application.Current.TryFindResource("RTFToolbarStyle") is Style toolbarStyle)
                {
                    _toolbar.Style = toolbarStyle;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BuildUI] Toolbar styling failed: {ex.Message}");
            }
            
            // Create editor
            _editor = new RTFEditor();
            Grid.SetRow(_editor, 1);
            
            // Apply editor style if available
            try
            {
                if (Application.Current.TryFindResource("RTFEditorStyle") is Style editorStyle)
                {
                    _editor.Style = editorStyle;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BuildUI] Editor styling failed: {ex.Message}");
            }
            
            // Add controls to grid
            grid.Children.Add(_toolbar);
            grid.Children.Add(_editor);
            
            // CRITICAL: Wire up toolbar to editor (this is the magic connection)
            _toolbar.TargetEditor = _editor;
            
            System.Diagnostics.Debug.WriteLine($"[BuildUI] Complete UI built for {Note.Title}: Grid + Toolbar + Editor");
            
            return grid;
        }

        private void OnNoteSaved(object? sender, NoteSavedEventArgs e)
        {
            if (e?.NoteId == _noteId)
            {
                // Clear dirty flag when save completes
                IsDirty = false;
            }
        }
        
        private void OnSaveStarted(object sender, SaveProgressEventArgs e)
        {
            if (e.NoteId == _noteId)
            {
                IsSaving = true;
            }
        }
        
        private void OnSaveCompleted(object sender, SaveProgressEventArgs e)
        {
            if (e.NoteId == _noteId)
            {
                IsSaving = false;
            }
        }
        
        // ENHANCED: Editor content changed handler (bulletproof save chain)
        private void OnEditorContentChanged(object sender, EventArgs e)
        {
            if (_editor == null || _disposed) return;
            
            try
            {
                // Extract and save content
                var content = _editor.SaveContent();
                _content = content; // Update backing field
                Note.Content = content; // Update model
                _saveManager.UpdateContent(_noteId, content);
                
                // Trigger save timers
                _localIsDirty = true;
                NotifyContentChanged();
                
                // Notify UI
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(Title));
                
                System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Editor content changed for {Note.Title}: {content?.Length ?? 0} chars");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Editor content change failed: {ex.Message}");
            }
        }
        
        // ENHANCED: Lazy content loading (called when tab becomes visible)
        public void EnsureContentLoaded()
        {
            if (!_contentLoaded && _saveManager != null)
            {
                try
                {
                    var content = _saveManager.GetContent(_noteId) ?? "";
                    _editor.LoadContent(content);
                    _editor.MarkClean();
                    _contentLoaded = true;
                    _localIsDirty = false;
                    
                    System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Content loaded for {Note.Title}: {content.Length} chars");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Content load failed for {Note.Title}: {ex.Message}");
                }
            }
        }

        public async Task SaveAsync()
        {
            if (string.IsNullOrEmpty(_noteId) || _saveManager == null)
                return;

            var success = await _saveManager.SaveNoteAsync(_noteId);
            if (success)
            {
                // Dirty flag will be cleared by NoteSaved event
                System.Diagnostics.Debug.WriteLine($"Manual save completed for {Note.Title}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Manual save failed for {Note.Title}");
            }
        }

        /// <summary>
        /// PROPER ARCHITECTURE: Initialize save timers for this tab
        /// </summary>
        private void InitializeSaveTimers()
        {
            // WAL protection timer (500ms after last change)
            _walTimer = new DispatcherTimer 
            { 
                Interval = TimeSpan.FromMilliseconds(500),
                IsEnabled = false
            };
            _walTimer.Tick += WalTimer_Tick;
            
            // Auto-save timer (2 seconds after last change)
            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2),
                IsEnabled = false  
            };
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
            
            System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Save timers initialized for {Note.Title}");
        }

        /// <summary>
        /// PROPER ARCHITECTURE: Notify this tab that content has changed
        /// Called directly from editor TextChanged (no complex event wiring needed)
        /// </summary>
        public void NotifyContentChanged()
        {
            _lastModification = DateTime.Now;
            _walSaved = false;
            
            // Restart both timers for proper debouncing
            _walTimer.Stop();
            _walTimer.Start();
            
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
            
            // Mark tab as dirty for UI feedback
            IsDirty = true;
            
            System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Content changed for {Note.Title}, timers restarted");
        }

        /// <summary>
        /// WAL protection timer - protects against crashes
        /// </summary>
        private void WalTimer_Tick(object sender, EventArgs e)
        {
            _walTimer.Stop();
            
            if (!_walSaved)
            {
                try
                {
                    // ENHANCED: Extract content from editor for WAL protection
                    var currentContent = _editor?.SaveContent() ?? _content;
                    _saveManager.UpdateContent(_noteId, currentContent);
                    _walSaved = true;
                    
                    System.Diagnostics.Debug.WriteLine($"[NoteTabItem] WAL protection triggered for {Note.Title}: {currentContent?.Length ?? 0} chars");
                }
                catch (Exception ex)
                {
                    // ZERO-RISK IMPROVEMENT: Enhanced error logging for diagnostics
                    System.Diagnostics.Debug.WriteLine($"[NoteTabItem] WAL protection failed for {Note.Title}: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[NoteTabItem] WAL error details: {ex.StackTrace}");
                }
            }
        }

        /// <summary>
        /// Auto-save timer - performs full save to disk
        /// </summary>
        private void AutoSaveTimer_Tick(object sender, EventArgs e)
        {
            _autoSaveTimer.Stop();
            
            try
            {
                // FIXED: No more silent auto-save failures 
                if (_taskRunner != null)
                {
                    _ = _taskRunner.RunAsync(
                        async () =>
                        {
                            await _saveManager.SaveNoteAsync(_noteId);
                            
                            // Update UI state on success
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                IsDirty = false;
                                IsSaving = false;
                            });
                            
                            System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Auto-save completed for {Note.Title}");
                        },
                        $"Auto-save for {Note.Title}",
                        NoteNest.Core.Services.OperationType.AutoSave
                    );
                }
                else
                {
                    // Fallback for backward compatibility
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _saveManager.SaveNoteAsync(_noteId);
                            System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Auto-save completed for {Note.Title}");
                        }
                        catch (Exception ex)
                        {
                            // ZERO-RISK IMPROVEMENT: Enhanced error logging for diagnostics
                            System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Auto-save failed for {Note.Title}: {ex.Message}");
                            System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Auto-save error details: {ex.StackTrace}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                // ZERO-RISK IMPROVEMENT: Enhanced error logging for diagnostics
                System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Auto-save timer failed for {Note.Title}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Timer error details: {ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Load content into the tab's editor (Tab-Owned pattern)
        /// </summary>
        public void LoadContent(string rtfContent)
        {
            if (_editor != null)
            {
                try
                {
                    _editor.LoadContent(rtfContent ?? "");
                    _editor.MarkClean();
                    _localIsDirty = false;
                    _contentLoaded = true;
                    
                    OnPropertyChanged(nameof(IsDirty));
                    OnPropertyChanged(nameof(Title));
                    
                    System.Diagnostics.Debug.WriteLine($"[NoteTabItem] LoadContent completed for {Note.Title}: {(rtfContent?.Length ?? 0)} chars");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[NoteTabItem] LoadContent failed for {Note.Title}: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Check if content has been loaded
        /// </summary>
        public bool ContentLoaded => _contentLoaded;

        /// <summary>
        /// Update content from editor (called by SplitPaneView)
        /// </summary>
        public void UpdateContentFromEditor(string editorContent)
        {
            if (string.IsNullOrEmpty(_noteId))
                return;
                
            // Update backing field AND SaveManager
            _content = editorContent;
            Note.Content = editorContent;
            _saveManager.UpdateContent(_noteId, editorContent);
            
            System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Content updated from editor for {Note.Title}: {editorContent?.Length ?? 0} chars");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Disposing tab for {Note.Title}");
            
            // ENHANCED: Clean disconnect from complete UI
            if (_editor != null)
            {
                _editor.ContentChanged -= OnEditorContentChanged;
                _editor.Dispose();
                System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Editor disposed for {Note.Title}");
            }
            
            // Clean up toolbar
            if (_toolbar != null)
            {
                try
                {
                    // Disconnect toolbar from editor
                    _toolbar.TargetEditor = null;
                    // Note: UserControl disposal is automatic, but we clear the reference
                    System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Toolbar disposed for {Note.Title}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Toolbar disposal warning: {ex.Message}");
                }
            }
            
            // Clean up timers
            _walTimer?.Stop();
            _walTimer = null;
            _autoSaveTimer?.Stop();
            _autoSaveTimer = null;
            
            // Clean up existing event handlers
            WeakEventManager<ISaveManager, NoteSavedEventArgs>
                .RemoveHandler(_saveManager, nameof(ISaveManager.NoteSaved), OnNoteSaved);
                
            WeakEventManager<ISaveManager, SaveProgressEventArgs>
                .RemoveHandler(_saveManager, nameof(ISaveManager.SaveStarted), OnSaveStarted);
                
            WeakEventManager<ISaveManager, SaveProgressEventArgs>
                .RemoveHandler(_saveManager, nameof(ISaveManager.SaveCompleted), OnSaveCompleted);
                
            System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Disposal completed for {Note.Title}");
        }
    }
}