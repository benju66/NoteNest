using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NoteNest.Core.Interfaces.Services
{
    /// <summary>
    /// Service for managing note pin states as user preferences
    /// Treats pins as workspace preferences separate from note metadata
    /// </summary>
    public interface IPinService
    {
        /// <summary>
        /// Check if a note is currently pinned
        /// </summary>
        /// <param name="noteId">The unique note ID</param>
        /// <returns>True if the note is pinned</returns>
        Task<bool> IsPinnedAsync(string noteId);
        
        /// <summary>
        /// Toggle the pin state of a note
        /// </summary>
        /// <param name="noteId">The unique note ID</param>
        /// <param name="filePath">The note's file path (for tracking)</param>
        /// <returns>True if the operation succeeded</returns>
        Task<bool> TogglePinAsync(string noteId, string filePath);
        
        /// <summary>
        /// Get all currently pinned note IDs
        /// </summary>
        /// <returns>List of pinned note IDs</returns>
        Task<IReadOnlyList<string>> GetPinnedNoteIdsAsync();
        
        /// <summary>
        /// Pin a specific note
        /// </summary>
        /// <param name="noteId">The unique note ID</param>
        /// <param name="filePath">The note's file path</param>
        /// <returns>True if the operation succeeded</returns>
        Task<bool> PinAsync(string noteId, string filePath);
        
        /// <summary>
        /// Unpin a specific note
        /// </summary>
        /// <param name="noteId">The unique note ID</param>
        /// <returns>True if the operation succeeded</returns>
        Task<bool> UnpinAsync(string noteId);
        
        /// <summary>
        /// Update the file path for a pinned note (for rename/move operations)
        /// </summary>
        /// <param name="noteId">The unique note ID</param>
        /// <param name="newPath">The new file path</param>
        Task UpdateFilePathAsync(string noteId, string newPath);
        
        /// <summary>
        /// Event raised when a pin state changes
        /// </summary>
        event EventHandler<PinChangedEventArgs> PinChanged;
    }
    
    /// <summary>
    /// Event arguments for pin state changes
    /// </summary>
    public class PinChangedEventArgs : EventArgs
    {
        public string NoteId { get; }
        public string FilePath { get; }
        public bool IsPinned { get; }
        
        public PinChangedEventArgs(string noteId, string filePath, bool isPinned)
        {
            NoteId = noteId ?? throw new ArgumentNullException(nameof(noteId));
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            IsPinned = isPinned;
        }
    }
}
