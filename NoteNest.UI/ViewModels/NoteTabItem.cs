using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Diagnostics;
using NoteNest.UI.Controls.Editor.RTF;
using NoteNest.UI.Services;

namespace NoteNest.UI.ViewModels
{
    public class NoteTabItem : ViewModelBase, ITabItem, IDisposable
    {
        private readonly ISaveManager _saveManager;
        private readonly string _noteId;
        private RTFEditor _editor;
        private RTFToolbar _toolbar;
        private Grid _contentGrid;
        private string _content;
        private bool _isSaving;
        private bool _localIsDirty;
        private volatile bool _contentLoaded = false;
        private volatile bool _disposed = false;
        
        // PHASE 1A: Thread safety
        private readonly object _contentLock = new object();
        
        // HIGH-IMPACT MEMORY FIX: Content caching
        private string _contentCache;
        private bool _contentCacheValid = false;
        private readonly object _contentCacheLock = new object();
        private volatile bool _fullyDisposed = false;
        
        // PROPER ARCHITECTURE: Each tab manages its own save timing
        private DispatcherTimer _walTimer;
        private DispatcherTimer _autoSaveTimer;
        private DateTime _lastModification;
        private bool _walSaved;
        
        // PHASE 4: Enhanced auto-save intelligence
        private string _lastAutoSavedContent = "";
        private DateTime _lastContentChangeTime;

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
        
        // HIGH-IMPACT PERFORMANCE FIX: Cached content property
        public string Content
        {
            get 
            {
                lock (_contentCacheLock)
                {
                    // Fast path: Return cached content
                    if (_contentCacheValid && _contentCache != null)
                    {
                        return _contentCache;
                    }
                    
                    // Slow path: Extract from editor only when needed
                    if (!_fullyDisposed && _editor != null)
                    {
                        try
                        {
                            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                            _contentCache = _editor.SaveContent();
                            stopwatch.Stop();
                            
                            _contentCacheValid = true;
                            
                            // Log expensive operations
                            if (stopwatch.ElapsedMilliseconds > 50)
                            {
                                DebugLogger.LogPerformance($"Content extraction for {Note.Title}", stopwatch.Elapsed);
                            }
                            
                            return _contentCache ?? "";
                        }
                        catch (Exception ex)
                        {
                            HandleTabError("Content Extraction", ex);
                        }
                    }
                    
                    return _content ?? "";
                }
            }
            set
            {
                var newValue = value ?? "";
                if (_content != newValue)
                {
                    _content = newValue;
                    InvalidateContentCache();
                    OnPropertyChanged();
                }
            }
        }
        
        /// <summary>
        /// Invalidate content cache when editor changes
        /// </summary>
        private void InvalidateContentCache()
        {
            lock (_contentCacheLock)
            {
                _contentCacheValid = false;
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

        public NoteTabItem(NoteModel note, ISaveManager saveManager)
        {
            var instanceId = this.GetHashCode();
            System.Diagnostics.Debug.WriteLine($"[NoteTabItem] NEW TAB-OWNED INSTANCE {instanceId}: note.Id={note?.Id}, note.Title={note?.Title}, saveManager={saveManager != null}");
            
            Note = note ?? throw new ArgumentNullException(nameof(note));
            _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
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
            
            // HIGH-IMPACT MEMORY FIX: Track tab creation
            SimpleMemoryTracker.TrackTabCreation(Note.Title);
            
            DebugLogger.Log($"TAB-OWNED constructor completed for noteId={_noteId} with complete UI");
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
            if (_fullyDisposed || _editor == null) return;
            
            try
            {
                // HIGH-IMPACT PERFORMANCE FIX: Invalidate cache, extract only for save
                InvalidateContentCache();
                
                // Extract content for save operations
                var content = _editor.SaveContent();
                _content = content; // Update backing field
                Note.Content = content; // Update model
                _saveManager.UpdateContent(_noteId, content);
                
                // Update cache with fresh content
                lock (_contentCacheLock)
                {
                    _contentCache = content;
                    _contentCacheValid = true;
                }
                
                // Trigger save timers and UI updates
                _localIsDirty = true;
                NotifyContentChanged();
                
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(Title));
                
                DebugLogger.Log($"Editor content changed for {Note.Title}: {content?.Length ?? 0} chars");
            }
            catch (ObjectDisposedException)
            {
                // Expected during shutdown, ignore
                return;
            }
            catch (Exception ex)
            {
                HandleTabError("Editor Content Change", ex);
            }
        }
        
        // ENHANCED: Lazy content loading (called when tab becomes visible)
        public void EnsureContentLoaded()
        {
            lock (_contentLock)  // ✅ Thread safety fix
            {
                if (!_contentLoaded && _saveManager != null && !_disposed)
                {
                    try
                    {
                        var content = _saveManager.GetContent(_noteId) ?? "";
                        _editor?.LoadContent(content);
                        _editor?.MarkClean();
                    _contentLoaded = true;
                    _localIsDirty = false;
                    
                    // HIGH-IMPACT MEMORY FIX: Track content size
                    SimpleMemoryTracker.TrackContentLoad(content.Length, Note.Title);
                    
                    DebugLogger.Log($"Content loaded for {Note.Title}: {content.Length} chars");
                    }
                    catch (Exception ex)
                    {
                        HandleTabError("Content Loading", ex);
                        // Don't set _contentLoaded = true on failure, allow retry
                    }
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
            
            // Auto-save timer (5 seconds after last change - balanced responsiveness)
            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5),
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
            _lastContentChangeTime = DateTime.Now;  // PHASE 4: Track change time for smart auto-save
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
                    HandleTabError("WAL Protection", ex);
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
                // PHASE 4: Enhanced auto-save intelligence - skip if content unchanged since last auto-save
                var currentContent = _editor?.SaveContent() ?? _content;
                if (currentContent == _lastAutoSavedContent)
                {
                    System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Auto-save skipped - content unchanged for {Note.Title}");
                    return;
                }
                
                // PHASE 4: Enhanced auto-save intelligence - skip if user is actively typing (changed within last second)
                if ((DateTime.Now - _lastContentChangeTime).TotalSeconds < 1.0)
                {
                    System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Auto-save deferred - user still typing {Note.Title}");
                    _autoSaveTimer.Start();  // Restart timer to try again later
                    return;
                }
                
                // Use RTF-integrated save system for tab auto-save (now the only system)
                var app = System.Windows.Application.Current as App;
                var rtfSaveWrapper = app?.ServiceProvider?.GetService(typeof(RTFSaveEngineWrapper)) as RTFSaveEngineWrapper;
                
                if (rtfSaveWrapper != null && _editor != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var result = await rtfSaveWrapper.SaveFromRichTextBoxAsync(
                                _noteId,
                                _editor,
                                Note.Title ?? "Untitled",
                                NoteNest.Core.Services.SaveType.AutoSave
                            );
                            
                            if (result.Success)
                            {
                                // PHASE 4: Update last saved content for future comparison
                                _lastAutoSavedContent = currentContent;
                                
                                // PHASE 1 FIX: NoteSaved event will handle clearing dirty flag
                                // Removed manual clearing to ensure consistency across all save types
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    IsSaving = false;
                                    // IsDirty = false; ← Removed: Let NoteSaved event handle this
                                });
                                
                                System.Diagnostics.Debug.WriteLine($"[NoteTabItem] RTF-integrated auto-save completed for {Note.Title}");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"[NoteTabItem] RTF-integrated auto-save failed for {Note.Title}: {result.Error}");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[NoteTabItem] RTF-integrated auto-save error for {Note.Title}: {ex.Message}");
                        }
                    });
                    
                    return; // Success path
                }
                else
                {
                    // Fallback to ISaveManager interface (still uses RTFIntegratedSaveEngine)
                    System.Diagnostics.Debug.WriteLine($"[NoteTabItem] RTF save wrapper not available, using ISaveManager interface for {Note.Title}");
                    
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _saveManager.SaveNoteAsync(_noteId);
                            
                            // Update UI state on success
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                IsDirty = false;
                                IsSaving = false;
                            });
                            
                            System.Diagnostics.Debug.WriteLine($"[NoteTabItem] ISaveManager auto-save completed for {Note.Title}");
                        }
                        catch (Exception ex)
                        {
                            HandleTabError("Auto-save", ex);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                HandleTabError("Auto-save Timer", ex);
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
                    HandleTabError("LoadContent", ex);
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
        
        // PHASE 1A: Centralized error handling
        private void HandleTabError(string operation, Exception ex)
        {
            var message = $"[Tab-{Note.Title}] {operation} failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine(message);
            
            // For critical operations, consider user notification
            if (operation == "Save" || operation == "Content Loading")
            {
                // Log error with more context
                System.Diagnostics.Debug.WriteLine($"[Tab-{Note.Title}] {operation} error details: {ex.StackTrace}");
            }
        }

        // HIGH-IMPACT MEMORY FIX: Bulletproof disposal
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            DebugLogger.Log($"Starting disposal for {Note.Title}");
            
            try
            {
                // PHASE 1: Stop all timers first (prevents callbacks during disposal)
                SafeDisposeTimer(ref _walTimer, WalTimer_Tick);
                SafeDisposeTimer(ref _autoSaveTimer, AutoSaveTimer_Tick);
                
                // PHASE 2: Unsubscribe all events (prevents memory leaks)
                SafeUnsubscribeEditorEvents();
                SafeUnsubscribeSaveManagerEvents();
                
                // PHASE 3: Dispose UI components
                SafeDisposeEditor();
                SafeDisposeToolbar();
                SafeDisposeContentGrid();
                
                // PHASE 4: Clear all references
                ClearAllReferences();
                
                // Mark as fully disposed
                _fullyDisposed = true;
                
                // HIGH-IMPACT MEMORY FIX: Track disposal
                SimpleMemoryTracker.TrackTabDisposal(Note.Title);
                
                DebugLogger.Log($"Disposal completed for {Note.Title}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Disposal error for {Note.Title}: {ex.Message}");
                _fullyDisposed = true; // Mark as disposed even if cleanup failed
            }
        }
        
        private void SafeDisposeTimer(ref DispatcherTimer timer, EventHandler handler)
        {
            try
            {
                if (timer != null)
                {
                    timer.Stop();
                    timer.Tick -= handler;  // ✅ Remove handler reference
                    timer = null;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Timer disposal error: {ex.Message}");
            }
        }
        
        private void SafeUnsubscribeEditorEvents()
        {
            try
            {
                if (_editor != null)
                {
                    _editor.ContentChanged -= OnEditorContentChanged;  // ✅ Critical cleanup
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Editor event cleanup error: {ex.Message}");
            }
        }
        
        private void SafeUnsubscribeSaveManagerEvents()
        {
            try
            {
                if (_saveManager != null)
                {
                    WeakEventManager<ISaveManager, NoteSavedEventArgs>
                        .RemoveHandler(_saveManager, nameof(ISaveManager.NoteSaved), OnNoteSaved);
                        
                    WeakEventManager<ISaveManager, SaveProgressEventArgs>
                        .RemoveHandler(_saveManager, nameof(ISaveManager.SaveStarted), OnSaveStarted);
                        
                    WeakEventManager<ISaveManager, SaveProgressEventArgs>
                        .RemoveHandler(_saveManager, nameof(ISaveManager.SaveCompleted), OnSaveCompleted);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"SaveManager event cleanup error: {ex.Message}");
            }
        }
        
        private void SafeDisposeEditor()
        {
            try
            {
                if (_editor != null)
                {
                    _editor.Dispose();
                    _editor = null;  // ✅ Clear reference for GC
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Editor disposal error: {ex.Message}");
            }
        }
        
        private void SafeDisposeToolbar()
        {
            try
            {
                if (_toolbar != null)
                {
                    _toolbar.TargetEditor = null;  // ✅ Break circular reference
                    _toolbar = null;  // ✅ Clear reference
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Toolbar disposal error: {ex.Message}");
            }
        }
        
        private void SafeDisposeContentGrid()
        {
            try
            {
                if (_contentGrid != null)
                {
                    _contentGrid.Children.Clear();  // ✅ Break parent-child references
                    _contentGrid = null;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"ContentGrid disposal error: {ex.Message}");
            }
        }
        
        private void ClearAllReferences()
        {
            try
            {
                // Clear content cache
                lock (_contentCacheLock)
                {
                    _contentCache = null;
                    _contentCacheValid = false;
                }
                
                // Clear other references
                _content = null;
                
                DebugLogger.Log("All references cleared");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Reference clearing error: {ex.Message}");
            }
        }
    }
}