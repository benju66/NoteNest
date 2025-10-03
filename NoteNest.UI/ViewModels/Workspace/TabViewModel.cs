using System;
using System.Windows.Input;
using System.Windows.Threading;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.UI.ViewModels.Common;

namespace NoteNest.UI.ViewModels.Workspace
{
    /// <summary>
    /// Clean ViewModel for individual tabs - no UI dependencies
    /// Follows MVVM pattern and integrates with existing SaveManager
    /// </summary>
    public class TabViewModel : ViewModelBase, IDisposable
    {
        private readonly ISaveManager _saveManager;
        private DispatcherTimer _autoSaveTimer;
        private DateTime _lastChangeTime;
        private string _lastAutoSavedContent = "";
        private bool _disposed;
        
        public string TabId { get; }
        public string NoteId => TabId; // Alias for compatibility
        public NoteModel Note { get; }
        
        // UI Properties
        public string Title => Note.Title;
        public bool IsDirty { get; private set; }
        public bool IsSaving { get; private set; }
        
        // Events for UI layer to handle RTF editor operations
        public event Action LoadContentRequested;
        public event Func<string> SaveContentRequested; // Returns current RTF from editor
        
        public TabViewModel(string tabId, NoteModel note, ISaveManager saveManager)
        {
            TabId = tabId ?? throw new ArgumentNullException(nameof(tabId));
            Note = note ?? throw new ArgumentNullException(nameof(note));
            _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            
            InitializeAutoSave();
            SubscribeToSaveEvents();
            
            System.Diagnostics.Debug.WriteLine($"[TabViewModel] Created: {Title} (ID: {TabId})");
        }
        
        /// <summary>
        /// Called by TabContentView when editor content changes
        /// </summary>
        public void OnContentChanged(string rtfContent)
        {
            if (_disposed) return;
            
            try
            {
                // Update SaveManager (includes WAL protection automatically)
                _saveManager.UpdateContent(TabId, rtfContent);
                
                System.Diagnostics.Debug.WriteLine($"[TabViewModel] Content changed for {Title}: {rtfContent?.Length ?? 0} chars");
                System.Diagnostics.Debug.WriteLine($"[TabViewModel] TabId: {TabId}, IsDirty: true");
                
                // Update UI state
                IsDirty = true;
                _lastChangeTime = DateTime.Now;
                
                // Restart auto-save timer (debouncing)
                _autoSaveTimer.Stop();
                _autoSaveTimer.Start();
                
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(Title));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TabViewModel] Content change error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get content from SaveManager for loading into editor
        /// </summary>
        public string GetContentToLoad()
        {
            try
            {
                return _saveManager.GetContent(TabId) ?? "";
            }
            catch
            {
                return "";
            }
        }
        
        /// <summary>
        /// Request content load from UI layer
        /// </summary>
        public void RequestContentLoad()
        {
            LoadContentRequested?.Invoke();
        }
        
        private void InitializeAutoSave()
        {
            _autoSaveTimer = new DispatcherTimer 
            { 
                Interval = TimeSpan.FromSeconds(5),
                IsEnabled = false
            };
            _autoSaveTimer.Tick += async (s, e) => await AutoSaveAsync();
        }
        
        private async System.Threading.Tasks.Task AutoSaveAsync()
        {
            _autoSaveTimer.Stop();
            
            if (!IsDirty || IsSaving || _disposed) return;
            
            // Check if user is still typing (defer if changed within last second)
            if ((DateTime.Now - _lastChangeTime).TotalSeconds < 1.0)
            {
                _autoSaveTimer.Start(); // Try again later
                return;
            }
            
            // Get current content from editor via event
            var currentContent = SaveContentRequested?.Invoke() ?? "";
            
            // Skip if content unchanged since last auto-save
            if (currentContent == _lastAutoSavedContent)
                return;
            
            try
            {
                IsSaving = true;
                OnPropertyChanged(nameof(IsSaving));
                
                var success = await _saveManager.SaveNoteAsync(TabId);
                
                if (success)
                {
                    _lastAutoSavedContent = currentContent;
                    System.Diagnostics.Debug.WriteLine($"[TabViewModel] Auto-saved: {Title}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TabViewModel] Auto-save error: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
                OnPropertyChanged(nameof(IsSaving));
            }
        }
        
        /// <summary>
        /// Manual save (Ctrl+S)
        /// </summary>
        public async System.Threading.Tasks.Task SaveAsync()
        {
            if (IsSaving || _disposed) return;
            
            try
            {
                IsSaving = true;
                OnPropertyChanged(nameof(IsSaving));
                
                System.Diagnostics.Debug.WriteLine($"[TabViewModel] Manual save STARTING: {Title}, TabId: {TabId}");
                System.Diagnostics.Debug.WriteLine($"[TabViewModel] Content in SaveManager: {_saveManager.GetContent(TabId)?.Length ?? 0} chars");
                System.Diagnostics.Debug.WriteLine($"[TabViewModel] File path: {_saveManager.GetFilePath(TabId)}");
                
                var result = await _saveManager.SaveNoteAsync(TabId);
                
                System.Diagnostics.Debug.WriteLine($"[TabViewModel] Manual save RESULT: {result} for {Title}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TabViewModel] Save error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[TabViewModel] Stack trace: {ex.StackTrace}");
                throw;
            }
            finally
            {
                IsSaving = false;
                OnPropertyChanged(nameof(IsSaving));
            }
        }
        
        private void SubscribeToSaveEvents()
        {
            System.Windows.WeakEventManager<ISaveManager, NoteSavedEventArgs>
                .AddHandler(_saveManager, nameof(ISaveManager.NoteSaved), OnNoteSaved);
        }
        
        private void OnNoteSaved(object sender, NoteSavedEventArgs e)
        {
            if (e?.NoteId == TabId)
            {
                IsDirty = false;
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(Title));
                System.Diagnostics.Debug.WriteLine($"[TabViewModel] Saved event received: {Title}");
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            try
            {
                // Stop auto-save timer
                _autoSaveTimer?.Stop();
                _autoSaveTimer = null;
                
                // Unsubscribe from events
                System.Windows.WeakEventManager<ISaveManager, NoteSavedEventArgs>
                    .RemoveHandler(_saveManager, nameof(ISaveManager.NoteSaved), OnNoteSaved);
                
                System.Diagnostics.Debug.WriteLine($"[TabViewModel] Disposed: {Title}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TabViewModel] Disposal error: {ex.Message}");
            }
        }
    }
}

