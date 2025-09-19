using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Core.Services;

namespace NoteNest.Core.Interfaces.Services
{
    /// <summary>
    /// Factory for creating and managing SaveManager instances during storage location changes
    /// </summary>
    public interface ISaveManagerFactory
    {
        /// <summary>
        /// Current active SaveManager instance
        /// </summary>
        ISaveManager Current { get; }
        
        /// <summary>
        /// Create a new SaveManager for the specified data path
        /// </summary>
        Task<ISaveManager> CreateSaveManagerAsync(string dataPath);
        
        /// <summary>
        /// Replace the current SaveManager with a new one atomically
        /// </summary>
        Task ReplaceSaveManagerAsync(ISaveManager newSaveManager);
        
        /// <summary>
        /// Capture the current state of the SaveManager for potential rollback
        /// </summary>
        Task<SaveManagerState> CaptureStateAsync();
        
        /// <summary>
        /// Restore SaveManager state from a previous capture
        /// </summary>
        Task RestoreStateAsync(SaveManagerState state);
        
        /// <summary>
        /// Event fired when SaveManager is replaced (for updating subscribers)
        /// </summary>
        event EventHandler<SaveManagerReplacedEventArgs> SaveManagerReplaced;
    }

    /// <summary>
    /// Captured state of a SaveManager for rollback purposes
    /// </summary>
    public class SaveManagerState
    {
        public string DataPath { get; set; } = string.Empty;
        public Dictionary<string, string> NoteContents { get; set; } = new();
        public Dictionary<string, string> LastSavedContents { get; set; } = new();
        public Dictionary<string, string> NoteFilePaths { get; set; } = new();
        public Dictionary<string, bool> DirtyNotes { get; set; } = new();
        public List<string> PendingWalEntries { get; set; } = new();
        public DateTime CapturedAt { get; set; }
    }

    /// <summary>
    /// Event args for SaveManager replacement notifications
    /// </summary>
    public class SaveManagerReplacedEventArgs : EventArgs
    {
        public ISaveManager OldSaveManager { get; set; }
        public ISaveManager NewSaveManager { get; set; }
        public string NewDataPath { get; set; } = string.Empty;
        public DateTime ReplacedAt { get; set; }
    }
}
