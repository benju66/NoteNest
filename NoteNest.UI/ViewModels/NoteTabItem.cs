using NoteNest.Core.Models;
using NoteNest.Core.Interfaces.Services;
using System;

namespace NoteNest.UI.ViewModels
{
    public class NoteTabItem : ViewModelBase, ITabItem
    {
        private readonly NoteModel _note;
        private bool _isDirty;
        private string _content;
        private string _wordCount;
        private string _lastSaved;

        public NoteModel Note => _note;
        public string Id => _note?.Id ?? string.Empty;

        public string Title => _note.Title;

        public string Content
        {
            get
            {
                try
                {
                    var state = (System.Windows.Application.Current as UI.App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.IWorkspaceStateService)) as NoteNest.Core.Services.IWorkspaceStateService;
                    if (state != null && !string.IsNullOrEmpty(_note?.Id) && state.OpenNotes.TryGetValue(_note.Id, out var wn))
                    {
                        var value = wn.CurrentContent ?? _content ?? string.Empty;
                        return value;
                    }
                }
                catch { }
                return _content ?? _note?.Content ?? string.Empty;
            }
            set
            {
                var newValue = value ?? string.Empty;
                // Always keep local cache for UI binding responsiveness
                var changed = SetProperty(ref _content, newValue);
                if (!changed) return;

                // Push content changes to WorkspaceStateService (single source of truth)
                try
                {
                    var state = (System.Windows.Application.Current as UI.App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.IWorkspaceStateService)) as NoteNest.Core.Services.IWorkspaceStateService;
                    state?.UpdateNoteContent(_note.Id, newValue);
                    System.Diagnostics.Debug.WriteLine($"[Tab] Content set noteId={_note?.Id} len={newValue.Length} at={DateTime.Now:HH:mm:ss.fff}");
                    // Determine dirty by comparing with state's OriginalContent
                    if (state != null && state.OpenNotes.TryGetValue(_note.Id, out var wn))
                    {
                        IsDirty = !string.Equals(wn.OriginalContent ?? string.Empty, newValue ?? string.Empty, StringComparison.Ordinal);
                    }
                    else
                    {
                        IsDirty = true;
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Tab][ERROR] Content set failed: {ex.Message}"); }
                UpdateWordCount();
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
