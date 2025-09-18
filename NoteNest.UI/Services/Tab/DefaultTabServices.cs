using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services;
using NoteNest.UI.Controls.Editor.RTF;
using NoteNest.Core.Models;

namespace NoteNest.UI.Services.Tab
{
    // PHASE 1B: Default implementations that maintain current behavior
    // These provide the same functionality as the current embedded code
    // but in a testable, replaceable form

    /// <summary>
    /// Default implementation of tab UI building
    /// Maintains exact behavior of current BuildEditorUI method
    /// </summary>
    public class DefaultTabUIBuilder : ITabUIBuilder
    {
        private readonly NoteModel _note;
        private RTFEditor _editor;
        private RTFToolbar _toolbar;
        
        public DefaultTabUIBuilder(NoteModel note)
        {
            _note = note ?? throw new ArgumentNullException(nameof(note));
        }
        
        // Expose created components for external access
        public RTFEditor Editor => _editor;
        public RTFToolbar Toolbar => _toolbar;

        public async Task<object> BuildTabUIAsync()
        {
            return await Task.FromResult(BuildTabUI());
        }

        public object BuildTabUI()
        {
            var grid = new Grid();
            
            try
            {
                // Setup grid structure (same as current code)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                
                // Apply grid style from resources if available
                ApplyStyleSafely(grid, "TabContentGridStyle");
                
                // Create toolbar
                _toolbar = CreateToolbarSafely();
                if (_toolbar != null)
                {
                    Grid.SetRow(_toolbar, 0);
                    ApplyStyleSafely(_toolbar, "RTFToolbarStyle");
                }
                
                // Create editor
                _editor = CreateEditorSafely();
                if (_editor != null)
                {
                    Grid.SetRow(_editor, 1);
                    ApplyStyleSafely(_editor, "RTFEditorStyle");
                }
                
                // Add to grid
                if (_toolbar != null) grid.Children.Add(_toolbar);
                if (_editor != null) grid.Children.Add(_editor);
                
                // Wire up toolbar to editor
                if (_toolbar != null && _editor != null)
                {
                    _toolbar.TargetEditor = _editor;
                }
                
                System.Diagnostics.Debug.WriteLine($"[DefaultTabUIBuilder] UI built successfully for {_note.Title}");
                return grid;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DefaultTabUIBuilder] UI creation failed for {_note.Title}: {ex.Message}");
                
                // Return fallback UI
                return CreateFallbackUI();
            }
        }
        
        private RTFToolbar CreateToolbarSafely()
        {
            try
            {
                return new RTFToolbar();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DefaultTabUIBuilder] Toolbar creation failed: {ex.Message}");
                return null;
            }
        }
        
        private RTFEditor CreateEditorSafely()
        {
            try
            {
                return new RTFEditor();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DefaultTabUIBuilder] Editor creation failed: {ex.Message}");
                return null;
            }
        }
        
        private void ApplyStyleSafely(FrameworkElement element, string styleKey)
        {
            try
            {
                if (Application.Current.TryFindResource(styleKey) is Style style)
                {
                    element.Style = style;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DefaultTabUIBuilder] Style application failed for {styleKey}: {ex.Message}");
            }
        }
        
        private UIElement CreateFallbackUI()
        {
            var errorGrid = new Grid();
            var errorText = new TextBlock 
            { 
                Text = "Failed to create tab editor", 
                Foreground = System.Windows.Media.Brushes.Red,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            errorGrid.Children.Add(errorText);
            return errorGrid;
        }
    }

    /// <summary>
    /// Default implementation of save coordination
    /// Maintains exact behavior of current timer and save logic
    /// </summary>
    public class DefaultTabSaveCoordinator : ITabSaveCoordinator, IDisposable
    {
        private ISaveManager _saveManager;
        private string _noteId;
        private bool _disposed = false;
        
        // Timers (same as current implementation)
        private DispatcherTimer _walTimer;
        private DispatcherTimer _autoSaveTimer;
        private DateTime _lastModification;
        private bool _walSaved;
        
        public event EventHandler<bool> IsSavingChanged;
        
        private bool _isSaving;
        public bool IsSaving 
        { 
            get => _isSaving; 
            private set 
            { 
                if (_isSaving != value)
                {
                    _isSaving = value;
                    IsSavingChanged?.Invoke(this, value);
                }
            } 
        }

        public void Initialize(object saveManager, object taskRunner, string noteId)
        {
            _saveManager = saveManager as ISaveManager ?? throw new ArgumentNullException(nameof(saveManager));
            // taskRunner parameter ignored - simplified coordination now
            _noteId = noteId ?? throw new ArgumentNullException(nameof(noteId));
            
            InitializeSaveTimers();
        }

        public void HandleContentChanged(string content)
        {
            if (_disposed || _saveManager == null) return;
            
            try
            {
                _saveManager.UpdateContent(_noteId, content);
                
                // Trigger save timers (same logic as current)
                _lastModification = DateTime.Now;
                _walSaved = false;
                
                StartTimers();
                
                System.Diagnostics.Debug.WriteLine($"[DefaultTabSaveCoordinator] Content changed for {_noteId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DefaultTabSaveCoordinator] HandleContentChanged failed for {_noteId}: {ex.Message}");
            }
        }

        public async Task SaveAsync()
        {
            if (_disposed || string.IsNullOrEmpty(_noteId) || _saveManager == null)
                return;

            try
            {
                IsSaving = true;
                var success = await _saveManager.SaveNoteAsync(_noteId);
                
                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"[DefaultTabSaveCoordinator] Manual save completed for {_noteId}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[DefaultTabSaveCoordinator] Manual save failed for {_noteId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DefaultTabSaveCoordinator] SaveAsync failed for {_noteId}: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
            }
        }

        public void StartTimers()
        {
            if (_disposed) return;
            
            // Restart both timers for proper debouncing
            _walTimer?.Stop();
            _walTimer?.Start();
            
            _autoSaveTimer?.Stop();
            _autoSaveTimer?.Start();
        }

        public void StopTimers()
        {
            _walTimer?.Stop();
            _autoSaveTimer?.Stop();
        }
        
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
            
            System.Diagnostics.Debug.WriteLine($"[DefaultTabSaveCoordinator] Save timers initialized for {_noteId}");
        }
        
        private void WalTimer_Tick(object sender, EventArgs e)
        {
            _walTimer.Stop();
            
            if (!_walSaved && !_disposed)
            {
                try
                {
                    // WAL protection is handled by SaveManager.UpdateContent
                    _walSaved = true;
                    System.Diagnostics.Debug.WriteLine($"[DefaultTabSaveCoordinator] WAL protection triggered for {_noteId}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DefaultTabSaveCoordinator] WAL protection failed for {_noteId}: {ex.Message}");
                }
            }
        }
        
        private void AutoSaveTimer_Tick(object sender, EventArgs e)
        {
            _autoSaveTimer.Stop();
            
            if (_disposed) return;
            
            try
            {
                // Simplified auto-save - uses ISaveManager interface (RTFIntegratedSaveEngine)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _saveManager.SaveNoteAsync(_noteId);
                        System.Diagnostics.Debug.WriteLine($"[DefaultTabSaveCoordinator] Auto-save completed for {_noteId}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DefaultTabSaveCoordinator] Auto-save failed for {_noteId}: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DefaultTabSaveCoordinator] Auto-save timer failed for {_noteId}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            StopTimers();
            
            // DispatcherTimer doesn't implement IDisposable, just stop and null
            _walTimer?.Stop();
            _autoSaveTimer?.Stop();
            _walTimer = null;
            _autoSaveTimer = null;
            
            System.Diagnostics.Debug.WriteLine($"[DefaultTabSaveCoordinator] Disposed for {_noteId}");
        }
    }

    /// <summary>
    /// Default implementation of event management
    /// Maintains current event wiring behavior
    /// </summary>
    public class DefaultTabEventManager : ITabEventManager, IDisposable
    {
        private object _editor;
        private Action<string> _contentChangedCallback;
        private bool _disposed = false;
        private bool _eventsWired = false;

        public bool AreEventsWired => _eventsWired && !_disposed;

        public void WireEvents(object editor, Action<string> onContentChanged)
        {
            if (_disposed) return;
            
            UnwireEvents(); // Clean up any existing events
            
            _editor = editor;
            _contentChangedCallback = onContentChanged;
            
            if (editor is RTFEditor rtfEditor)
            {
                rtfEditor.ContentChanged += OnEditorContentChanged;
                _eventsWired = true;
                System.Diagnostics.Debug.WriteLine("[DefaultTabEventManager] Events wired successfully");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[DefaultTabEventManager] Unsupported editor type: {editor?.GetType()}");
            }
        }

        public void UnwireEvents()
        {
            if (_editor is RTFEditor rtfEditor)
            {
                rtfEditor.ContentChanged -= OnEditorContentChanged;
                System.Diagnostics.Debug.WriteLine("[DefaultTabEventManager] Events unwired successfully");
            }
            
            _editor = null;
            _contentChangedCallback = null;
            _eventsWired = false;
        }
        
        private void OnEditorContentChanged(object sender, EventArgs e)
        {
            if (_disposed || _editor == null) return;
            
            try
            {
                if (_editor is RTFEditor rtfEditor)
                {
                    var content = rtfEditor.SaveContent();
                    _contentChangedCallback?.Invoke(content);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DefaultTabEventManager] Content changed handling failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            UnwireEvents();
            System.Diagnostics.Debug.WriteLine("[DefaultTabEventManager] Disposed");
        }
    }

    /// <summary>
    /// Default implementation of tab state management
    /// Thread-safe state tracking
    /// </summary>
    public class DefaultTabStateManager : ITabStateManager
    {
        private readonly object _stateLock = new object();
        private bool _isDirty;
        private bool _isContentLoaded;
        private bool _isSaving;

        public event EventHandler<bool> DirtyStateChanged;
        public event EventHandler<bool> SavingStateChanged;

        public bool IsDirty
        {
            get 
            {
                lock (_stateLock)
                {
                    return _isDirty;
                }
            }
            set
            {
                bool changed = false;
                lock (_stateLock)
                {
                    if (_isDirty != value)
                    {
                        _isDirty = value;
                        changed = true;
                    }
                }
                
                if (changed)
                {
                    DirtyStateChanged?.Invoke(this, value);
                }
            }
        }

        public bool IsContentLoaded
        {
            get 
            {
                lock (_stateLock)
                {
                    return _isContentLoaded;
                }
            }
            set
            {
                lock (_stateLock)
                {
                    _isContentLoaded = value;
                }
            }
        }

        public bool IsSaving
        {
            get 
            {
                lock (_stateLock)
                {
                    return _isSaving;
                }
            }
            set
            {
                bool changed = false;
                lock (_stateLock)
                {
                    if (_isSaving != value)
                    {
                        _isSaving = value;
                        changed = true;
                    }
                }
                
                if (changed)
                {
                    SavingStateChanged?.Invoke(this, value);
                }
            }
        }
    }

    /// <summary>
    /// Default implementation of content management
    /// Handles content extraction and loading for RTF editors
    /// </summary>
    public class DefaultTabContentManager : ITabContentManager
    {
        private readonly object _editor;
        
        public DefaultTabContentManager(object editor)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        }

        public string ExtractContent()
        {
            try
            {
                if (_editor is RTFEditor rtfEditor)
                {
                    return rtfEditor.SaveContent() ?? "";
                }
                return "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DefaultTabContentManager] ExtractContent failed: {ex.Message}");
                return "";
            }
        }

        public async Task<string> ExtractContentAsync()
        {
            return await Task.FromResult(ExtractContent());
        }

        public async Task LoadContentAsync(string content)
        {
            await Task.Run(() =>
            {
                try
                {
                    if (_editor is RTFEditor rtfEditor)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            rtfEditor.LoadContent(content ?? "");
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DefaultTabContentManager] LoadContentAsync failed: {ex.Message}");
                }
            });
        }

        public object GetVisualElement()
        {
            return _editor;
        }

        public void MarkClean()
        {
            try
            {
                if (_editor is RTFEditor rtfEditor)
                {
                    rtfEditor.MarkClean();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DefaultTabContentManager] MarkClean failed: {ex.Message}");
            }
        }
    }
}
