using System;
using System.IO;
using System.Threading.Tasks;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Utils
{
    /// <summary>
    /// Extension methods for note position management
    /// Provides seamless position retrieval and persistence using the existing metadata system
    /// </summary>
    public static class NotePositionExtensions
    {
        private const string POSITION_KEY = "position";
        
        /// <summary>
        /// Gets the position of a note from its metadata, with fallback to 0
        /// </summary>
        public static async Task<int> GetPositionAsync(this NoteModel note, NoteMetadataManager metadataManager = null)
        {
            if (note == null || string.IsNullOrEmpty(note.FilePath))
                return 0;
                
            try
            {
                if (metadataManager != null)
                {
                    var metaPath = GetMetaPath(note.FilePath);
                    var metadata = await metadataManager.ReadMetadataAsync(metaPath);
                    
                    if (metadata?.Extensions != null && 
                        metadata.Extensions.TryGetValue(POSITION_KEY, out var posValue))
                    {
                        return Convert.ToInt32(posValue);
                    }
                }
            }
            catch (Exception)
            {
                // If anything goes wrong reading position, fall back to 0
                // This ensures the feature degrades gracefully
            }
            
            return 0; // Default position for notes without explicit position
        }
        
        /// <summary>
        /// Synchronous version that returns cached position or 0
        /// Used for performance-critical sorting operations
        /// </summary>
        public static int GetCachedPosition(this NoteModel note)
        {
            // For now, return 0 - this will be enhanced with in-memory caching later
            // The async version above is used for definitive position retrieval
            return 0;
        }
        
        /// <summary>
        /// Sets the position of a note in its metadata
        /// </summary>
        public static async Task<bool> SetPositionAsync(this NoteModel note, int position, 
            NoteMetadataManager metadataManager, IAppLogger logger = null)
        {
            if (note == null || string.IsNullOrEmpty(note.FilePath) || metadataManager == null)
                return false;
                
            try
            {
                var metaPath = GetMetaPath(note.FilePath);
                
                // Read existing metadata or create new
                var metadata = await metadataManager.ReadMetadataAsync(metaPath);
                if (metadata == null)
                {
                    // Create new metadata if it doesn't exist
                    metadata = new NoteMetadataManager.NoteMetadata
                    {
                        Id = note.Id,
                        Created = DateTime.UtcNow
                    };
                }
                
                // Update position in extensions
                metadata.Extensions[POSITION_KEY] = position;
                
                // Save metadata
                await metadataManager.WriteMetadataAsync(metaPath, metadata);
                
                logger?.Debug($"Updated position for note '{note.Title}' to {position}");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Error($"Failed to set position for note '{note.Title}' to {position}: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Batch update positions for multiple notes efficiently
        /// </summary>
        public static async Task<int> BatchUpdatePositionsAsync(
            this System.Collections.Generic.IEnumerable<NoteModel> notes,
            NoteMetadataManager metadataManager,
            IAppLogger logger = null)
        {
            if (notes == null || metadataManager == null)
                return 0;
                
            int successCount = 0;
            int position = 0;
            
            foreach (var note in notes)
            {
                try
                {
                    // Use increments of 10 to leave room for future insertions
                    var success = await note.SetPositionAsync(position * 10, metadataManager, logger);
                    if (success)
                        successCount++;
                        
                    position++;
                }
                catch (Exception ex)
                {
                    logger?.Warning($"Failed to update position for note '{note.Title}': {ex.Message}");
                }
            }
            
            logger?.Debug($"Batch updated positions: {successCount} successful");
            return successCount;
        }
        
        /// <summary>
        /// Helper to get metadata file path - mirrors NoteMetadataManager logic
        /// </summary>
        private static string GetMetaPath(string notePath)
        {
            // Fixed: Use same extension as NoteMetadataManager (.meta, not .meta.json)
            if (string.IsNullOrWhiteSpace(notePath)) return string.Empty;
            var idx = notePath.LastIndexOf('.');
            return idx >= 0 ? notePath.Substring(0, idx) + ".meta" : notePath + ".meta";
        }
    }
    
    /// <summary>
    /// Service for managing note positions with caching and batch operations
    /// </summary>
    public class NotePositionService
    {
        private readonly NoteMetadataManager _metadataManager;
        private readonly IAppLogger _logger;
        
        public NotePositionService(NoteMetadataManager metadataManager, IAppLogger logger)
        {
            _metadataManager = metadataManager ?? throw new ArgumentNullException(nameof(metadataManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Assigns initial positions to notes that don't have them
        /// Safe to call multiple times - only assigns positions to notes without them
        /// </summary>
        public async Task<int> AssignInitialPositionsAsync(System.Collections.Generic.IList<NoteModel> notes)
        {
            if (notes == null || notes.Count == 0)
                return 0;
                
            int assignedCount = 0;
            
            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                var currentPosition = await note.GetPositionAsync(_metadataManager);
                
                // Only assign position if note doesn't have one (position 0 is considered "no position")
                if (currentPosition == 0 && !string.IsNullOrEmpty(note.FilePath))
                {
                    var success = await note.SetPositionAsync(i * 10, _metadataManager, _logger);
                    if (success)
                        assignedCount++;
                }
            }
            
            if (assignedCount > 0)
            {
                _logger.Info($"Assigned initial positions to {assignedCount} notes");
            }
            
            return assignedCount;
        }
        
        /// <summary>
        /// Reorders notes based on their current ObservableCollection order
        /// Updates all positions to match the current UI order
        /// </summary>
        public async Task<bool> UpdatePositionsFromOrderAsync(System.Collections.Generic.IList<NoteModel> orderedNotes)
        {
            if (orderedNotes == null)
                return false;
                
            try
            {
                for (int i = 0; i < orderedNotes.Count; i++)
                {
                    var note = orderedNotes[i];
                    await note.SetPositionAsync(i * 10, _metadataManager, _logger);
                }
                
                _logger.Debug($"Updated positions for {orderedNotes.Count} notes based on current order");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to update positions from order");
                return false;
            }
        }
    }
}
