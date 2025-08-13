using NoteNest.Core.Models;
using System;

namespace NoteNest.UI.ViewModels
{
    public class NoteTabItem : ViewModelBase
    {
        private readonly NoteModel _note;
        private bool _isDirty;
        private string _content;
        private string _wordCount;
        private string _lastSaved;

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
                    UpdateWordCount();
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
                    UpdateLastSaved();
                }
            }
        }

        public string WordCount
        {
            get => _wordCount ?? "0 words";
            set => SetProperty(ref _wordCount, value);
        }

        public string LastSaved
        {
            get => _lastSaved ?? "Not saved";
            set => SetProperty(ref _lastSaved, value);
        }

        public NoteTabItem(NoteModel note)
        {
            _note = note;
            _content = note.Content;
            _isDirty = false;
            UpdateWordCount();
            UpdateLastSaved();
        }

        public new void OnPropertyChanged(string propertyName)
        {
            base.OnPropertyChanged(propertyName);
        }

        public void UpdateWordCount()
        {
            if (string.IsNullOrEmpty(Content))
            {
                WordCount = "0 words";
            }
            else
            {
                var words = Content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                WordCount = $"{words} words";
            }
        }

        public void UpdateLastSaved()
        {
            LastSaved = IsDirty ? "Unsaved changes" : $"Saved {DateTime.Now:HH:mm}";
        }
    }
}
