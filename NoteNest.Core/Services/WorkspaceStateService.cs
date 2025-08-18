using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;

namespace NoteNest.Core.Services
{
    // Feature-flagged scaffold; not wired yet
    public interface IWorkspaceStateService
    {
        IReadOnlyDictionary<string, WorkspaceNote> OpenNotes { get; }
        WorkspaceNote? ActiveNote { get; }
        Task<WorkspaceNote> OpenNoteAsync(NoteModel note);
        Task<bool> CloseNoteAsync(string noteId);
        void UpdateNoteContent(string noteId, string content);
        bool IsNoteDirty(string noteId);
        IEnumerable<WorkspaceNote> GetDirtyNotes();
        Task<SaveResult> SaveNoteAsync(string noteId);
        Task<BatchSaveResult> SaveAllDirtyNotesAsync(int maxParallel = 4);
        event EventHandler<NoteStateChangedEventArgs> NoteStateChanged;
    }

    public class WorkspaceNote
    {
        public string NoteId { get; set; } = string.Empty;
        public NoteModel Model { get; set; } = null!;
        public string OriginalContent { get; set; } = string.Empty;
        public string CurrentContent { get; set; } = string.Empty;
        public DateTime OpenedAt { get; set; }
        public DateTime? LastSavedAt { get; set; }
        public bool IsDirty => !string.Equals(OriginalContent, CurrentContent, StringComparison.Ordinal);
    }

    public class NoteStateChangedEventArgs : EventArgs
    {
        public string NoteId { get; set; } = string.Empty;
        public bool IsDirty { get; set; }
    }

    public class SaveResult
    {
        public bool Success { get; set; }
        public string? NoteId { get; set; }
        public DateTime SavedAt { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class BatchSaveResult
    {
        public bool AllSucceeded { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<SaveResult> Results { get; set; } = new();
    }

    public class WorkspaceStateService : IWorkspaceStateService
    {
        private readonly NoteService _noteService;
        private readonly Dictionary<string, WorkspaceNote> _openNotes = new();
        private string? _activeNoteId;

        public event EventHandler<NoteStateChangedEventArgs>? NoteStateChanged;

        public IReadOnlyDictionary<string, WorkspaceNote> OpenNotes => _openNotes;
        public WorkspaceNote? ActiveNote => _activeNoteId != null && _openNotes.TryGetValue(_activeNoteId, out var n) ? n : null;

        public WorkspaceStateService(NoteService noteService)
        {
            _noteService = noteService;
        }

        public async Task<WorkspaceNote> OpenNoteAsync(NoteModel note)
        {
            if (_openNotes.TryGetValue(note.Id, out var existing))
            {
                _activeNoteId = note.Id;
                return existing;
            }

            var wn = new WorkspaceNote
            {
                NoteId = note.Id,
                Model = note,
                OriginalContent = note.Content ?? string.Empty,
                CurrentContent = note.Content ?? string.Empty,
                OpenedAt = DateTime.Now
            };
            _openNotes[note.Id] = wn;
            _activeNoteId = note.Id;
            return await Task.FromResult(wn);
        }

        public Task<bool> CloseNoteAsync(string noteId)
        {
            var removed = _openNotes.Remove(noteId);
            if (_activeNoteId == noteId) _activeNoteId = null;
            return Task.FromResult(removed);
        }

        public void UpdateNoteContent(string noteId, string content)
        {
            if (_openNotes.TryGetValue(noteId, out var note))
            {
                var wasDirty = note.IsDirty;
                note.CurrentContent = content;
                if (wasDirty != note.IsDirty)
                {
                    NoteStateChanged?.Invoke(this, new NoteStateChangedEventArgs { NoteId = noteId, IsDirty = note.IsDirty });
                }
            }
        }

        public bool IsNoteDirty(string noteId) => _openNotes.TryGetValue(noteId, out var n) && n.IsDirty;
        public IEnumerable<WorkspaceNote> GetDirtyNotes() => _openNotes.Values.Where(v => v.IsDirty);

        public async Task<SaveResult> SaveNoteAsync(string noteId)
        {
            if (!_openNotes.TryGetValue(noteId, out var note))
            {
                return new SaveResult { Success = false, NoteId = noteId, ErrorMessage = "Note not open" };
            }
            if (!note.IsDirty)
            {
                return new SaveResult { Success = true, NoteId = noteId, SavedAt = DateTime.Now };
            }
            note.Model.Content = note.CurrentContent;
            await _noteService.SaveNoteAsync(note.Model);
            note.OriginalContent = note.CurrentContent;
            note.LastSavedAt = note.Model.LastModified;
            NoteStateChanged?.Invoke(this, new NoteStateChangedEventArgs { NoteId = noteId, IsDirty = false });
            return new SaveResult { Success = true, NoteId = noteId, SavedAt = note.Model.LastModified };
        }

        public async Task<BatchSaveResult> SaveAllDirtyNotesAsync(int maxParallel = 4)
        {
            var dirty = GetDirtyNotes().Select(d => d.NoteId).ToList();
            var results = new List<SaveResult>();
            using var gate = new System.Threading.SemaphoreSlim(maxParallel);
            var tasks = dirty.Select(async id =>
            {
                await gate.WaitAsync();
                try
                {
                    var r = await SaveNoteAsync(id);
                    lock (results) results.Add(r);
                }
                finally
                {
                    gate.Release();
                }
            });
            await Task.WhenAll(tasks);
            return new BatchSaveResult
            {
                AllSucceeded = results.All(r => r.Success),
                SuccessCount = results.Count(r => r.Success),
                FailureCount = results.Count(r => !r.Success),
                Results = results
            };
        }
    }
}


