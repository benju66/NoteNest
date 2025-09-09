using System;
using System.ComponentModel;
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
                    _saveManager?.UpdateContent(_noteId, value);
                    OnPropertyChanged(nameof(IsDirty));
                    OnPropertyChanged(nameof(Title));
                }
            }
        }
        
        public bool IsDirty 
        {
            get => _saveManager?.IsNoteDirty(_noteId) ?? false;
            set 
            {
                // Note: For interface compliance, but actual dirty state is managed by SaveManager
                // This setter is mainly used by old code during transition
                if (value != IsDirty)
                {
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
            Note = note ?? throw new ArgumentNullException(nameof(note));
            _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            _noteId = note.Id;
            _content = note.Content ?? "";
            
            // Use weak event pattern to prevent memory leaks
            WeakEventManager<ISaveManager, NoteSavedEventArgs>
                .AddHandler(_saveManager, nameof(ISaveManager.NoteSaved), OnNoteSaved);
            
            WeakEventManager<ISaveManager, SaveProgressEventArgs>
                .AddHandler(_saveManager, nameof(ISaveManager.SaveStarted), OnSaveStarted);
                
            WeakEventManager<ISaveManager, SaveProgressEventArgs>
                .AddHandler(_saveManager, nameof(ISaveManager.SaveCompleted), OnSaveCompleted);
        }

        private void OnNoteSaved(object sender, NoteSavedEventArgs e)
        {
            if (e.NoteId == _noteId)
            {
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(Title));
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