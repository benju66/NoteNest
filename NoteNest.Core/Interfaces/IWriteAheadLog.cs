using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NoteNest.Core.Interfaces
{
    /// <summary>
    /// Interface for Write-Ahead Log implementation
    /// Provides crash protection by persisting content before file operations
    /// </summary>
    public interface IWriteAheadLog : IDisposable
    {
        /// <summary>
        /// Write content to WAL for crash protection (new API)
        /// </summary>
        Task<WALEntry> WriteAsync(string noteId, string content);
        
        /// <summary>
        /// Append content to WAL (legacy API for compatibility)
        /// </summary>
        Task AppendAsync(string noteId, string content);
        
        /// <summary>
        /// Commit/clear WAL entry after successful save (legacy API)
        /// </summary>
        Task CommitAsync(string noteId);
        
        /// <summary>
        /// Remove WAL entry after successful save (new API)
        /// </summary>
        Task RemoveAsync(string walId);
        
        /// <summary>
        /// Recover content for a specific note
        /// </summary>
        Task<string?> RecoverAsync(string noteId);
        
        /// <summary>
        /// Recover all unsaved content
        /// </summary>
        Task<Dictionary<string, string>> RecoverAllAsync();
        
        /// <summary>
        /// Clean up old WAL entries
        /// </summary>
        Task CleanupOldEntriesAsync(TimeSpan maxAge);
    }

    /// <summary>
    /// Write-Ahead Log entry structure
    /// </summary>
    public class WALEntry
    {
        public string Id { get; set; } = string.Empty;
        public string NoteId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
