using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using System.Threading;

namespace NoteNest.Core.Services
{
    // Centralized state management for open notes and content tracking
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

        // Window association (Core-safe: use window key instead of Window type)
        void AssociateNoteWithWindow(string noteId, string windowKey, bool isDetached);
        bool IsNoteDetached(string noteId);
        string? GetNoteWindowKey(string noteId);
        void ClearWindowAssociation(string noteId);
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
        private readonly ISafeContentBuffer _contentBuffer;
        private readonly Dictionary<string, WorkspaceNote> _openNotes = new();
        private readonly object _noteLock = new object();
        private string? _activeNoteId;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _saveLocks = new();

        // Track window association without referencing WPF types
        private class WindowAssociation
        {
            public string NoteId { get; set; } = string.Empty;
            public string? WindowKey { get; set; }
            public bool IsDetached { get; set; }
            public DateTime AssociatedAt { get; set; }
        }
        private readonly ConcurrentDictionary<string, WindowAssociation> _windowTracking = new();

        public event EventHandler<NoteStateChangedEventArgs>? NoteStateChanged;

        public IReadOnlyDictionary<string, WorkspaceNote> OpenNotes => _openNotes;
        public WorkspaceNote? ActiveNote
        {
            get
            {
                lock (_noteLock)
                {
                    return _activeNoteId != null && _openNotes.TryGetValue(_activeNoteId, out var n) ? n : null;
                }
            }
        }

        public WorkspaceStateService(NoteService noteService, ISafeContentBuffer? contentBuffer = null)
        {
            _noteService = noteService;
            _contentBuffer = contentBuffer ?? new SafeContentBuffer();
        }

        public async Task<WorkspaceNote> OpenNoteAsync(NoteModel note)
        {
            lock (_noteLock)
            {
                if (_openNotes.TryGetValue(note.Id, out var existing))
                {
                    _activeNoteId = note.Id;
                    return existing;
                }
            }

            var wn = new WorkspaceNote
            {
                NoteId = note.Id,
                Model = note,
                OriginalContent = note.Content ?? string.Empty,
                CurrentContent = note.Content ?? string.Empty,
                OpenedAt = DateTime.Now
            };
            lock (_noteLock)
            {
                _openNotes[note.Id] = wn;
                _activeNoteId = note.Id;
            }
            return await Task.FromResult(wn);
        }

        public Task<bool> CloseNoteAsync(string noteId)
        {
            bool removed;
            lock (_noteLock)
            {
                removed = _openNotes.Remove(noteId);
                if (_activeNoteId == noteId) _activeNoteId = null;
            }
            return Task.FromResult(removed);
        }

        public void UpdateNoteContent(string noteId, string content)
        {
            // Always buffer content immediately for safety
            _contentBuffer.BufferContent(noteId, content);
            
            lock (_noteLock)
            {
                if (_openNotes.TryGetValue(noteId, out var note))
                {
                    var wasDirty = note.IsDirty;
                    note.CurrentContent = content;
                    System.Diagnostics.Debug.WriteLine($"[State] UpdateNoteContent noteId={noteId} len={content?.Length ?? 0} wasDirty={wasDirty} nowDirty={note.IsDirty} at={DateTime.Now:HH:mm:ss.fff}");
                    if (wasDirty != note.IsDirty)
                    {
                        NoteStateChanged?.Invoke(this, new NoteStateChangedEventArgs { NoteId = noteId, IsDirty = note.IsDirty });
                    }
                }
                else
                {
                    // This should not happen if WorkspaceService properly registers notes
                    System.Diagnostics.Debug.WriteLine($"[State][WARN] Attempted to update content for untracked note: {noteId} at={DateTime.Now:HH:mm:ss.fff}");
                }
            }
        }

        public bool IsNoteDirty(string noteId)
        {
            lock (_noteLock)
            {
                return _openNotes.TryGetValue(noteId, out var n) && n.IsDirty;
            }
        }
        public IEnumerable<WorkspaceNote> GetDirtyNotes()
        {
            lock (_noteLock)
            {
                return _openNotes.Values.Where(v => v.IsDirty).ToList();
            }
        }

        public async Task<SaveResult> SaveNoteAsync(string noteId)
        {
            System.Diagnostics.Debug.WriteLine($"[State] SaveNoteAsync START noteId={noteId} at={DateTime.Now:HH:mm:ss.fff}");

            var saveLock = _saveLocks.GetOrAdd(noteId, _ => new SemaphoreSlim(1, 1));
            await saveLock.WaitAsync();
            WorkspaceNote note;
            lock (_noteLock)
            {
                if (!_openNotes.TryGetValue(noteId, out note))
                {
                    System.Diagnostics.Debug.WriteLine($"[State][ERROR] SaveNoteAsync noteId={noteId} failed: not open at={DateTime.Now:HH:mm:ss.fff}");
                    saveLock.Release();
                    return new SaveResult { Success = false, NoteId = noteId, ErrorMessage = "Note not open" };
                }
            }
            if (!note.IsDirty)
            {
                var ts = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"[State] SaveNoteAsync noteId={noteId} skipped (not dirty) at={ts:HH:mm:ss.fff}");
                saveLock.Release();
                return new SaveResult { Success = true, NoteId = noteId, SavedAt = ts };
            }
            
            // Check buffer for latest content in case binding flush failed
            var bufferedContent = _contentBuffer.GetLatestContent(noteId);
            if (bufferedContent != null)
            {
                System.Diagnostics.Debug.WriteLine($"[State] SaveNoteAsync using buffered content for noteId={noteId} at={DateTime.Now:HH:mm:ss.fff}");
                note.CurrentContent = bufferedContent;
            }
            
            // On save, push state content into the model that hits disk
            note.Model.Content = note.CurrentContent ?? string.Empty;
            try
            {
                await _noteService.SaveNoteAsync(note.Model);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[State][ERROR] SaveNoteAsync noteId={noteId} threw: {ex.Message} at={DateTime.Now:HH:mm:ss.fff}");
                saveLock.Release();
                return new SaveResult { Success = false, NoteId = noteId, ErrorMessage = ex.Message };
            }
            lock (_noteLock)
            {
                note.OriginalContent = note.CurrentContent;
                note.LastSavedAt = note.Model.LastModified;
            }
            NoteStateChanged?.Invoke(this, new NoteStateChangedEventArgs { NoteId = noteId, IsDirty = false });
            System.Diagnostics.Debug.WriteLine($"[State] SaveNoteAsync OK noteId={noteId} savedAt={note.Model.LastModified:HH:mm:ss.fff} len={note.OriginalContent?.Length ?? 0}");
            
            // Clear buffer after successful save
            _contentBuffer.ClearBuffer(noteId);
            
            saveLock.Release();
            return new SaveResult { Success = true, NoteId = noteId, SavedAt = note.Model.LastModified };
        }

        public async Task<BatchSaveResult> SaveAllDirtyNotesAsync(int maxParallel = 4)
        {
            System.Diagnostics.Debug.WriteLine($"[State] SaveAllDirtyNotesAsync START at={DateTime.Now:HH:mm:ss.fff}");
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
            var batch = new BatchSaveResult
            {
                AllSucceeded = results.All(r => r.Success),
                SuccessCount = results.Count(r => r.Success),
                FailureCount = results.Count(r => !r.Success),
                Results = results
            };
            System.Diagnostics.Debug.WriteLine($"[State] SaveAllDirtyNotesAsync END success={batch.SuccessCount} fail={batch.FailureCount} at={DateTime.Now:HH:mm:ss.fff}");
            return batch;
        }

        // Core-safe window association API
        public void AssociateNoteWithWindow(string noteId, string windowKey, bool isDetached)
        {
            if (string.IsNullOrEmpty(noteId)) return;
            _windowTracking.AddOrUpdate(noteId,
                id => new WindowAssociation
                {
                    NoteId = id,
                    WindowKey = windowKey,
                    IsDetached = isDetached,
                    AssociatedAt = DateTime.Now
                },
                (id, existing) =>
                {
                    existing.WindowKey = windowKey;
                    existing.IsDetached = isDetached;
                    existing.AssociatedAt = DateTime.Now;
                    return existing;
                });
        }

        public bool IsNoteDetached(string noteId)
        {
            return _windowTracking.TryGetValue(noteId, out var assoc) && assoc.IsDetached;
        }

        public string? GetNoteWindowKey(string noteId)
        {
            return _windowTracking.TryGetValue(noteId, out var assoc) ? assoc.WindowKey : null;
        }

        public void ClearWindowAssociation(string noteId)
        {
            if (string.IsNullOrEmpty(noteId)) return;
            _windowTracking.TryRemove(noteId, out _);
        }
    }
}


