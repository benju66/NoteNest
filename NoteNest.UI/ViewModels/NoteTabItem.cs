using NoteNest.Core.Models;

namespace NoteNest.UI.ViewModels
{
    public class NoteTabItem : ViewModelBase
    {
        private readonly NoteModel _note;
        private bool _isDirty;
        private string _content;

        public NoteModel Note => _note;

        public string Title => _note.Title + (IsDirty ? " *" : "");

        public string Content
        {
            get => _content;
            set
            {
                if (SetProperty(ref _content, value))
                {
                    _note.Content = value;
                    IsDirty = true;
                }
            }
        }

        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (SetProperty(ref _isDirty, value))
                {
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        public NoteTabItem(NoteModel note)
        {
            _note = note;
            _content = note.Content;
            _isDirty = false;
        }
    }
}
