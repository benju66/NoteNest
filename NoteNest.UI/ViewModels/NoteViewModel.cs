using System;
using NoteNest.Core.Services;

namespace NoteNest.UI.ViewModels
{
    // Compatibility: implements ITab-like surface via properties (not formally implementing ITabItem to avoid ripple changes yet)
    public class NoteViewModel : ViewModelBase, IDisposable
    {
        private readonly IWorkspaceStateService _workspace;
        private readonly WorkspaceNote _note;
        private string _content;

        public string NoteId => _note.NoteId;
        public string Title => _note.Model.Title;
        public string FilePath => _note.Model.FilePath;
        public bool IsDirty => _workspace.IsNoteDirty(NoteId);
        public string DisplayTitle => IsDirty ? $"{Title} *" : Title;

        public string Content
        {
            get => _content;
            set
            {
                if (SetProperty(ref _content, value))
                {
                    _workspace.UpdateNoteContent(NoteId, value);
                    OnPropertyChanged(nameof(IsDirty));
                    OnPropertyChanged(nameof(DisplayTitle));
                }
            }
        }

        public NoteViewModel(IWorkspaceStateService workspace, WorkspaceNote note)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _note = note ?? throw new ArgumentNullException(nameof(note));
            _content = note.CurrentContent;
            _workspace.NoteStateChanged += WorkspaceOnNoteStateChanged;
        }

        private void WorkspaceOnNoteStateChanged(object? sender, NoteStateChangedEventArgs e)
        {
            if (e.NoteId == NoteId)
            {
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }

        public async System.Threading.Tasks.Task SaveAsync()
        {
            await _workspace.SaveNoteAsync(NoteId);
        }

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            try { _workspace.NoteStateChanged -= WorkspaceOnNoteStateChanged; } catch { }
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}


