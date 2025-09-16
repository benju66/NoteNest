using System;
using System.Threading.Tasks;
using System.Windows;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.UI.ViewModels;
using NoteNest.UI.Controls.Editor.RTF;

namespace NoteNest.UI.Services.Tab
{
    /// <summary>
    /// PHASE 1B: Enhanced tab item using dependency injection
    /// Clean, testable implementation following Single Responsibility Principle
    /// Each component handles exactly one concern
    /// </summary>
    public class EnhancedNoteTabItem : ViewModelBase, ITabItem, IAsyncDisposable
    {
        // Injected dependencies (testable)
        private readonly ITabUIBuilder _uiBuilder;
        private readonly ITabSaveCoordinator _saveCoordinator;
        private readonly ITabEventManager _eventManager;
        private readonly ITabStateManager _stateManager;
        private ITabContentManager _contentManager;
        private readonly ISaveManager _saveManager;
        private readonly ISupervisedTaskRunner _taskRunner;
        
        // State
        private bool _initialized = false;
        private bool _disposed = false;
        private UIElement _tabContent;

        // Properties required by ITabItem interface
        public string Id { get; }
        public string NoteId { get; }
        public NoteModel Note { get; }

        public string Title
        {
            get
            {
                var baseTitle = Note.Title;
                if (_stateManager.IsSaving)
                    return $"{baseTitle} (saving...)";
                return _stateManager.IsDirty ? $"{baseTitle} •" : baseTitle;
            }
        }

        // PHASE 1A: Backward-compatible Content property
        public string Content
        {
            get
            {
                try
                {
                    return _contentManager?.ExtractContent() ?? "";
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[EnhancedNoteTabItem] Content extraction failed: {ex.Message}");
                    return "";
                }
            }
            set
            {
                if (value != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _contentManager?.LoadContentAsync(value);
                            _stateManager.IsDirty = false;
                            _stateManager.IsContentLoaded = true;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[EnhancedNoteTabItem] Content loading failed: {ex.Message}");
                        }
                    });
                }
            }
        }

        // New property for Tab-Owned Editor Pattern
        public UIElement TabContent => _tabContent;

        // Direct access to components (for backward compatibility)
        public RTFEditor Editor
        {
            get
            {
                if (_uiBuilder is DefaultTabUIBuilder defaultBuilder)
                {
                    return defaultBuilder.Editor;
                }
                return null;
            }
        }

        public bool IsDirty
        {
            get => _stateManager.IsDirty;
            set => _stateManager.IsDirty = value;
        }

        // Constructor uses dependency injection
        public EnhancedNoteTabItem(
            NoteModel note,
            ITabUIBuilder uiBuilder,
            ITabSaveCoordinator saveCoordinator,
            ITabEventManager eventManager,
            ITabStateManager stateManager,
            ISaveManager saveManager,
            ISupervisedTaskRunner taskRunner = null)
        {
            // Validate dependencies
            Note = note ?? throw new ArgumentNullException(nameof(note));
            _uiBuilder = uiBuilder ?? throw new ArgumentNullException(nameof(uiBuilder));
            _saveCoordinator = saveCoordinator ?? throw new ArgumentNullException(nameof(saveCoordinator));
            _eventManager = eventManager ?? throw new ArgumentNullException(nameof(eventManager));
            _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
            _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            _taskRunner = taskRunner; // Allow null

            Id = note.Id;
            NoteId = note.Id;

            // Subscribe to state changes for UI updates
            _stateManager.DirtyStateChanged += OnDirtyStateChanged;
            _stateManager.SavingStateChanged += OnSavingStateChanged;
            _saveCoordinator.IsSavingChanged += OnSavingStateChanged;

            System.Diagnostics.Debug.WriteLine($"[EnhancedNoteTabItem] Created for {Note.Title}");
        }

        /// <summary>
        /// Async initialization - heavy operations moved out of constructor
        /// This is the key to making the architecture testable and robust
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_initialized || _disposed) return;

            try
            {
                System.Diagnostics.Debug.WriteLine($"[EnhancedNoteTabItem] Initializing {Note.Title}");

                // Step 1: Build UI asynchronously
                _tabContent = await _uiBuilder.BuildTabUIAsync() as UIElement;
                
                // Step 2: Create content manager
                var editor = GetEditorFromUIBuilder();
                if (editor != null)
                {
                    _contentManager = new DefaultTabContentManager(editor);
                }

                // Step 3: Initialize save coordinator
                _saveCoordinator.Initialize(_saveManager, _taskRunner, NoteId);

                // Step 4: Wire up events
                if (editor != null)
                {
                    _eventManager.WireEvents(editor, OnContentChanged);
                }

                _initialized = true;
                System.Diagnostics.Debug.WriteLine($"[EnhancedNoteTabItem] Initialization completed for {Note.Title}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EnhancedNoteTabItem] Initialization failed for {Note.Title}: {ex.Message}");
                throw; // Re-throw to let factory handle the error
            }
        }

        /// <summary>
        /// Ensure the tab is initialized (for lazy loading)
        /// </summary>
        public async Task EnsureInitializedAsync()
        {
            if (!_initialized)
            {
                await InitializeAsync();
            }
        }

        /// <summary>
        /// Load content into the tab (thread-safe)
        /// </summary>
        public async Task LoadContentAsync(string content)
        {
            if (_disposed) return;

            try
            {
                await EnsureInitializedAsync();
                await _contentManager?.LoadContentAsync(content);
                _contentManager?.MarkClean();
                _stateManager.IsDirty = false;
                _stateManager.IsContentLoaded = true;

                System.Diagnostics.Debug.WriteLine($"[EnhancedNoteTabItem] Content loaded for {Note.Title}: {content?.Length ?? 0} chars");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EnhancedNoteTabItem] LoadContentAsync failed for {Note.Title}: {ex.Message}");
            }
        }

        /// <summary>
        /// Save content manually
        /// </summary>
        public async Task SaveAsync()
        {
            if (_disposed || !_initialized) return;

            try
            {
                // Ensure content is up to date before saving
                var content = _contentManager?.ExtractContent();
                if (!string.IsNullOrEmpty(content))
                {
                    _saveManager?.UpdateContent(NoteId, content);
                }

                await _saveCoordinator?.SaveAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EnhancedNoteTabItem] SaveAsync failed for {Note.Title}: {ex.Message}");
            }
        }

        // Event handlers
        private void OnContentChanged(string content)
        {
            if (_disposed) return;

            try
            {
                _saveCoordinator?.HandleContentChanged(content);
                _stateManager.IsDirty = true;

                System.Diagnostics.Debug.WriteLine($"[EnhancedNoteTabItem] Content changed for {Note.Title}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EnhancedNoteTabItem] OnContentChanged failed for {Note.Title}: {ex.Message}");
            }
        }

        private void OnDirtyStateChanged(object sender, bool isDirty)
        {
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(Title));
        }

        private void OnSavingStateChanged(object sender, bool isSaving)
        {
            OnPropertyChanged(nameof(Title));
        }

        // Helper method to get editor from UI builder
        private RTFEditor GetEditorFromUIBuilder()
        {
            if (_uiBuilder is DefaultTabUIBuilder defaultBuilder)
            {
                return defaultBuilder.Editor;
            }
            return null;
        }

        // IAsyncDisposable implementation
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            System.Diagnostics.Debug.WriteLine($"[EnhancedNoteTabItem] Disposing {Note.Title}");

            try
            {
                // Unsubscribe from events
                _stateManager.DirtyStateChanged -= OnDirtyStateChanged;
                _stateManager.SavingStateChanged -= OnSavingStateChanged;
                _saveCoordinator.IsSavingChanged -= OnSavingStateChanged;

                // Dispose components
                _eventManager?.UnwireEvents();
                _saveCoordinator?.StopTimers();

                // Dispose disposable components
                if (_eventManager is IDisposable eventManagerDisposable)
                    eventManagerDisposable.Dispose();
                if (_saveCoordinator is IDisposable saveCoordinatorDisposable)
                    saveCoordinatorDisposable.Dispose();

                System.Diagnostics.Debug.WriteLine($"[EnhancedNoteTabItem] Disposed {Note.Title}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EnhancedNoteTabItem] Disposal error for {Note.Title}: {ex.Message}");
            }

            // Call synchronous dispose for backward compatibility
            Dispose();
        }

        // IDisposable implementation (for backward compatibility)
        public void Dispose()
        {
            if (!_disposed)
            {
                _ = Task.Run(async () => await DisposeAsync());
            }
        }
    }
}
