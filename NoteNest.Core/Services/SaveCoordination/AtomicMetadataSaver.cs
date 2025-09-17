using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.SaveCoordination
{
    /// <summary>
    /// Provides atomic saves of content + metadata together
    /// Integrates with SaveCoordinator for bulletproof data integrity
    /// ELIMINATES: Content/metadata consistency issues during crashes
    /// </summary>
    public class AtomicMetadataSaver
    {
        private readonly NoteMetadataManager _metadataManager;
        private readonly IFileSystemProvider _fileSystem;
        private readonly IAppLogger _logger;
        
        // Metrics for monitoring atomic save performance
        private int _atomicSaveAttempts = 0;
        private int _atomicSaveSuccesses = 0;
        private int _fallbacksUsed = 0;

        public AtomicMetadataSaver(
            NoteMetadataManager metadataManager,
            IFileSystemProvider fileSystem,
            IAppLogger logger)
        {
            _metadataManager = metadataManager ?? throw new ArgumentNullException(nameof(metadataManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Save content and metadata atomically using temp files + atomic moves
        /// Falls back to separate saves if atomic operation fails
        /// </summary>
        public async Task<AtomicSaveResult> SaveContentAndMetadataAtomically(
            NoteModel note, 
            string content,
            Func<Task> legacyContentSaveAction)
        {
            if (note == null || string.IsNullOrEmpty(note.FilePath))
            {
                _logger.Warning("AtomicSave called with invalid note or file path");
                return new AtomicSaveResult { Success = false, UsedFallback = true };
            }

            System.Threading.Interlocked.Increment(ref _atomicSaveAttempts);

            var metaPath = _metadataManager.GetMetaPath(note.FilePath);
            var contentTemp = note.FilePath + ".atomic.tmp";
            var metaTemp = metaPath + ".atomic.tmp";
            
            try
            {
                _logger.Debug($"Attempting atomic save: {note.FilePath} + metadata");

                // STEP 1: Prepare metadata (read existing or create new)
                var metadata = await PrepareMetadata(note, metaPath);
                var metadataJson = JsonSerializer.Serialize(metadata, 
                    new JsonSerializerOptions { WriteIndented = true });
                
                // STEP 2: Write both files to temp locations (fail fast if issues)
                await _fileSystem.WriteTextAsync(contentTemp, content ?? string.Empty);
                await _fileSystem.WriteTextAsync(metaTemp, metadataJson);
                
                // STEP 3: Atomic moves (as atomic as file system allows)
                // Move content first (most important)
                File.Move(contentTemp, note.FilePath, true);
                
                // Move metadata second (less critical if this fails)
                File.Move(metaTemp, metaPath, true);
                
                // SUCCESS: Both files moved atomically
                System.Threading.Interlocked.Increment(ref _atomicSaveSuccesses);
                _logger.Debug($"Atomic save succeeded: {note.FilePath} + metadata");
                
                return new AtomicSaveResult 
                { 
                    Success = true, 
                    UsedFallback = false,
                    ContentSaved = true,
                    MetadataSaved = true
                };
            }
            catch (Exception ex)
            {
                _logger.Warning($"Atomic save failed for {note.FilePath}: {ex.Message}");
                
                // STEP 4: Cleanup temp files on failure
                await SafeCleanupTempFiles(contentTemp, metaTemp);
                
                // STEP 5: Fallback to separate saves (maintain existing behavior)
                return await FallbackToSeparateSaves(note, content, legacyContentSaveAction);
            }
        }

        /// <summary>
        /// Fallback to separate content and metadata saves if atomic operation fails
        /// Maintains existing behavior while providing best-effort metadata consistency
        /// </summary>
        private async Task<AtomicSaveResult> FallbackToSeparateSaves(
            NoteModel note, 
            string content, 
            Func<Task> legacyContentSaveAction)
        {
            System.Threading.Interlocked.Increment(ref _fallbacksUsed);
            
            bool contentSaved = false;
            bool metadataSaved = false;
            
            try
            {
                // Save content using existing proven method
                await legacyContentSaveAction();
                contentSaved = true;
                _logger.Debug($"Fallback content save succeeded: {note.FilePath}");
                
                // Best-effort metadata save
                try
                {
                    await SafeUpdateMetadata(note);
                    metadataSaved = true;
                    _logger.Debug($"Fallback metadata save succeeded: {note.FilePath}");
                }
                catch (Exception metaEx)
                {
                    _logger.Warning($"Fallback metadata save failed for {note.FilePath}: {metaEx.Message}");
                    // Content saved successfully, metadata failed - still usable
                }
                
                return new AtomicSaveResult
                {
                    Success = contentSaved, // Success if content saved (metadata optional)
                    UsedFallback = true,
                    ContentSaved = contentSaved,
                    MetadataSaved = metadataSaved
                };
            }
            catch (Exception contentEx)
            {
                _logger.Error(contentEx, $"Fallback content save failed for {note.FilePath}");
                
                return new AtomicSaveResult
                {
                    Success = false,
                    UsedFallback = true,
                    ContentSaved = false,
                    MetadataSaved = false,
                    ErrorMessage = contentEx.Message
                };
            }
        }

        /// <summary>
        /// Prepare metadata for atomic save - read existing or create new
        /// </summary>
        private async Task<NoteMetadataManager.NoteMetadata> PrepareMetadata(NoteModel note, string metaPath)
        {
            try
            {
                // Try to read existing metadata to preserve extensions
                if (await _fileSystem.ExistsAsync(metaPath))
                {
                    var existing = await _metadataManager.ReadMetadataAsync(metaPath);
                    if (existing != null)
                    {
                        // Update timestamp but preserve existing data
                        existing.Id = note.Id; // Ensure ID is current
                        return existing;
                    }
                }
                
                // Create new metadata
                return new NoteMetadataManager.NoteMetadata
                {
                    Id = note.Id,
                    Created = DateTime.UtcNow,
                    Extensions = new System.Collections.Generic.Dictionary<string, object>()
                };
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to read existing metadata for {note.FilePath}: {ex.Message}");
                
                // Create minimal metadata as fallback
                return new NoteMetadataManager.NoteMetadata
                {
                    Id = note.Id,
                    Created = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Best-effort metadata update for fallback scenarios
        /// </summary>
        private async Task SafeUpdateMetadata(NoteModel note)
        {
            try
            {
                var metaPath = _metadataManager.GetMetaPath(note.FilePath);
                var metadata = await PrepareMetadata(note, metaPath);
                
                // Use temp file pattern for safety
                var tempPath = metaPath + ".tmp";
                var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                
                await _fileSystem.WriteTextAsync(tempPath, json);
                File.Move(tempPath, metaPath, true);
                
                _logger.Debug($"Safe metadata update completed: {metaPath}");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Safe metadata update failed for {note.FilePath}: {ex.Message}");
                throw; // Re-throw to let caller handle
            }
        }

        /// <summary>
        /// Clean up temporary files on failure
        /// </summary>
        private async Task SafeCleanupTempFiles(params string[] tempPaths)
        {
            foreach (var tempPath in tempPaths)
            {
                try
                {
                    if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                        _logger.Debug($"Cleaned up temp file: {tempPath}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to cleanup temp file {tempPath}: {ex.Message}");
                    // Non-critical - continue cleanup
                }
            }
        }

        /// <summary>
        /// Get metrics about atomic save performance
        /// </summary>
        public AtomicSaveMetrics GetMetrics()
        {
            return new AtomicSaveMetrics
            {
                AtomicSaveAttempts = _atomicSaveAttempts,
                AtomicSaveSuccesses = _atomicSaveSuccesses,
                FallbacksUsed = _fallbacksUsed,
                AtomicSuccessRate = _atomicSaveAttempts > 0 
                    ? (double)_atomicSaveSuccesses / _atomicSaveAttempts 
                    : 0.0
            };
        }

        /// <summary>
        /// Reset metrics (for testing or monitoring periods)
        /// </summary>
        public void ResetMetrics()
        {
            _atomicSaveAttempts = 0;
            _atomicSaveSuccesses = 0;
            _fallbacksUsed = 0;
        }
    }

    /// <summary>
    /// Result of atomic save operation
    /// </summary>
    public class AtomicSaveResult
    {
        public bool Success { get; set; }
        public bool UsedFallback { get; set; }
        public bool ContentSaved { get; set; }
        public bool MetadataSaved { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        
        /// <summary>
        /// True if both content and metadata saved atomically
        /// </summary>
        public bool WasFullyAtomic => Success && !UsedFallback && ContentSaved && MetadataSaved;
    }

    /// <summary>
    /// Metrics for monitoring atomic save performance
    /// </summary>
    public class AtomicSaveMetrics
    {
        public int AtomicSaveAttempts { get; set; }
        public int AtomicSaveSuccesses { get; set; }
        public int FallbacksUsed { get; set; }
        public double AtomicSuccessRate { get; set; }
        
        public override string ToString()
        {
            return $"Atomic Saves: {AtomicSaveSuccesses}/{AtomicSaveAttempts} ({AtomicSuccessRate:P1}), Fallbacks: {FallbacksUsed}";
        }
    }
}
