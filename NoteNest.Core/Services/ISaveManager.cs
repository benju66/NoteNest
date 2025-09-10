using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NoteNest.Core.Services
{
    public interface ISaveManager : IDisposable
    {
        // Core operations
        Task<string> OpenNoteAsync(string filePath);
        void UpdateContent(string noteId, string content);
        Task<bool> SaveNoteAsync(string noteId);
        Task<BatchSaveResult> SaveAllDirtyAsync();
        Task<bool> CloseNoteAsync(string noteId); // Changed to async
        
        // State queries
        bool IsNoteDirty(string noteId);
        bool IsSaving(string noteId);
        string GetContent(string noteId);
        string? GetLastSavedContent(string noteId);
        string? GetFilePath(string noteId);
        string? GetNoteIdForPath(string filePath);
        IReadOnlyList<string> GetDirtyNoteIds();
        
        // Conflict resolution
        Task<bool> ResolveExternalChangeAsync(string noteId, ConflictResolution resolution);
        
        // File path management
        void UpdateFilePath(string noteId, string newFilePath);
        
        // Events
        event EventHandler<NoteSavedEventArgs> NoteSaved;
        event EventHandler<SaveProgressEventArgs> SaveStarted;
        event EventHandler<SaveProgressEventArgs> SaveCompleted;
        event EventHandler<ExternalChangeEventArgs> ExternalChangeDetected;
    }
    
    public enum ConflictResolution
    {
        KeepLocal,
        KeepExternal,
        Merge
    }
    
    public class SaveProgressEventArgs : EventArgs
    {
        public string NoteId { get; set; }
        public string FilePath { get; set; }
        public SavePriority Priority { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
    }
    
    public class NoteSavedEventArgs : EventArgs
    {
        public string NoteId { get; set; }
        public string FilePath { get; set; }
        public DateTime SavedAt { get; set; }
        public bool WasAutoSave { get; set; }
    }
    
    public class ExternalChangeEventArgs : EventArgs
    {
        public string NoteId { get; set; }
        public string FilePath { get; set; }
        public string ExternalContent { get; set; }
        public DateTime DetectedAt { get; set; }
    }
    
    public class BatchSaveResult
    {
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<string> FailedNoteIds { get; set; } = new();
        public Dictionary<string, string> FailureReasons { get; set; } = new();
    }
    
    public enum SavePriority
    {
        AutoSave = 0,
        UserSave = 1,
        ShutdownSave = 2
    }
    
    public class SaveRequest
    {
        public string NoteId { get; set; }
        public SavePriority Priority { get; set; }
        public DateTime QueuedAt { get; set; }
        public TaskCompletionSource<bool> CompletionSource { get; set; }
        public bool IsCoalesced { get; set; }
    }
}
