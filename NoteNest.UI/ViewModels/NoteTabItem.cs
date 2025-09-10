using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
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
                return IsDirty ? $"{baseTitle} *" : baseTitle;
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

        public void Dispose()
        {
            WeakEventManager<ISaveManager, NoteSavedEventArgs>
                .RemoveHandler(_saveManager, nameof(ISaveManager.NoteSaved), OnNoteSaved);
                
            WeakEventManager<ISaveManager, SaveProgressEventArgs>
                .RemoveHandler(_saveManager, nameof(ISaveManager.SaveStarted), OnSaveStarted);
                
            WeakEventManager<ISaveManager, SaveProgressEventArgs>
                .RemoveHandler(_saveManager, nameof(ISaveManager.SaveCompleted), OnSaveCompleted);
        }
    }
}