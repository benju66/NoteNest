using System;
using System.ComponentModel;
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
        private bool _disposed;

        public string NoteId => _noteId;
        public NoteModel Note { get; }
        public string Id => _noteId;
        
        public string Title => IsDirty ? $"{Note.Title} *" : Note.Title;
        
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

        public NoteTabItem(NoteModel note, ISaveManager saveManager)
        {
            Note = note ?? throw new ArgumentNullException(nameof(note));
            _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            _noteId = note.Id;
            _content = note.Content ?? "";
            
            // Subscribe to save events
            _saveManager.NoteSaved += OnNoteSaved;
        }

        private void OnNoteSaved(object sender, NoteSavedEventArgs e)
        {
            if (e.NoteId == _noteId)
            {
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(Title));
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _saveManager.NoteSaved -= OnNoteSaved;
                _disposed = true;
            }
        }
    }
}