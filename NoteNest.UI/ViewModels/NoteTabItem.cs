using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Interfaces.Services;

namespace NoteNest.UI.ViewModels
{
    public class NoteTabItem : ViewModelBase, ITabItem, IDisposable
    {
        private readonly ISaveManager _saveManager;
        private readonly string _noteId;
        private string _content;
        private bool _isSaving;
        private bool _localIsDirty;
        
        // PROPER ARCHITECTURE: Each tab manages its own save timing
        private DispatcherTimer _walTimer;
        private DispatcherTimer _autoSaveTimer;
        private DateTime _lastModification;
        private bool _walSaved;

        public string NoteId => _noteId;
        public NoteModel Note { get; }
        public string Id => _noteId;
        
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

        public NoteTabItem(NoteModel note, ISaveManager saveManager)
        {
            var instanceId = this.GetHashCode();
            System.Diagnostics.Debug.WriteLine($"[NoteTabItem] NEW INSTANCE {instanceId}: note.Id={note?.Id}, note.Title={note?.Title}, saveManager={saveManager != null}");
            
            Note = note ?? throw new ArgumentNullException(nameof(note));
            _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            _noteId = note.Id;
            _content = note.Content ?? "";
            
            System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Instance {instanceId} initialized: _noteId={_noteId}, contentLength={_content.Length}");
            
            // Use weak event pattern to prevent memory leaks
            WeakEventManager<ISaveManager, NoteSavedEventArgs>
                .AddHandler(_saveManager, nameof(ISaveManager.NoteSaved), OnNoteSaved);
            
            WeakEventManager<ISaveManager, SaveProgressEventArgs>
                .AddHandler(_saveManager, nameof(ISaveManager.SaveStarted), OnSaveStarted);
                
            WeakEventManager<ISaveManager, SaveProgressEventArgs>
                .AddHandler(_saveManager, nameof(ISaveManager.SaveCompleted), OnSaveCompleted);
                
            // PROPER ARCHITECTURE: Initialize save timers for this tab
            InitializeSaveTimers();
                
            System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Instance {instanceId} constructor completed for noteId={_noteId}");
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
                    // Quick markdown extraction for WAL (would need editor reference)
                    // For now, use current content as approximation
                    _saveManager.UpdateContent(_noteId, _content);
                    _walSaved = true;
                    
                    System.Diagnostics.Debug.WriteLine($"[NoteTabItem] WAL protection triggered for {Note.Title}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[NoteTabItem] WAL protection failed for {Note.Title}: {ex.Message}");
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
                // Trigger full save (would need fresh content from editor)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _saveManager.SaveNoteAsync(_noteId);
                        System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Auto-save completed for {Note.Title}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Auto-save failed for {Note.Title}: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NoteTabItem] Auto-save timer failed for {Note.Title}: {ex.Message}");
            }
        }

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
        }
    }
}