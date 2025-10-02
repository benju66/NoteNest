using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Trees;

namespace NoteNest.Infrastructure.Database.Services
{
    /// <summary>
    /// PROTOTYPE: Event-driven service that synchronizes database metadata when notes are saved.
    /// Listens to ISaveManager.NoteSaved events and updates tree_nodes table.
    /// 
    /// Architecture:
    /// - RTFIntegratedSaveEngine (Core) fires NoteSaved event
    /// - This service (Infrastructure) listens and updates database
    /// - No circular dependency (Core doesn't know about Infrastructure)
    /// - File system remains source of truth, database is performance cache
    /// </summary>
    public class DatabaseMetadataUpdateService : IHostedService, IDisposable
    {
        private readonly ISaveManager _saveManager;
        private readonly ITreeDatabaseRepository _repository;
        private readonly IAppLogger _logger;
        private bool _disposed = false;

        public DatabaseMetadataUpdateService(
            ISaveManager saveManager,
            ITreeDatabaseRepository repository,
            IAppLogger logger)
        {
            _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.Info("═══════════════════════════════════════════════════════════════");
            _logger.Info("🔄 DatabaseMetadataUpdateService PROTOTYPE starting...");
            _logger.Info("   Purpose: Test Option A - Event-driven DB metadata sync");
            _logger.Info($"   ISaveManager instance: {_saveManager.GetType().Name} @ {_saveManager.GetHashCode()}");
            _logger.Info("   Subscribing to: ISaveManager.NoteSaved event");
            _logger.Info("═══════════════════════════════════════════════════════════════");
            
            // Subscribe to save events
            _saveManager.NoteSaved += OnNoteSaved;
            
            _logger.Info($"✅ DatabaseMetadataUpdateService subscribed to NoteSaved events");
            _logger.Info("   Listening for: Manual saves, auto-saves, tab switch, tab close");
            
            // 🧪 TEST: Manually trigger to verify it works
            _logger.Info("🧪 TESTING: Manually triggering OnNoteSaved to verify it works...");
            OnNoteSaved(this, new NoteSavedEventArgs 
            { 
                NoteId = "test-note-id", 
                FilePath = "C:\\test\\path.rtf", 
                SavedAt = DateTime.UtcNow, 
                WasAutoSave = false 
            });
            _logger.Info("🧪 TEST COMPLETE: Check logs above for test event handling");
            
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.Info("🛑 DatabaseMetadataUpdateService stopping...");
            
            // Unsubscribe from events
            if (_saveManager != null)
            {
                _saveManager.NoteSaved -= OnNoteSaved;
            }
            
            return Task.CompletedTask;
        }

        /// <summary>
        /// Event handler for NoteSaved - updates database metadata after file save
        /// Pattern follows DatabaseFileWatcherService.ProcessPendingChanges()
        /// </summary>
        private async void OnNoteSaved(object sender, NoteSavedEventArgs e)
        {
            // GUARD: Validate event data
            if (e == null || string.IsNullOrEmpty(e.FilePath))
            {
                _logger.Warning("NoteSaved event received with invalid data");
                return;
            }

            try
            {
                _logger.Info("─────────────────────────────────────────────────────────────");
                _logger.Info($"📝 SAVE EVENT RECEIVED:");
                _logger.Info($"   File: {e.FilePath}");
                _logger.Info($"   NoteId: {e.NoteId}");
                _logger.Info($"   SavedAt: {e.SavedAt:yyyy-MM-dd HH:mm:ss.fff}");
                _logger.Info($"   WasAutoSave: {e.WasAutoSave}");

                // Step 1: Normalize path to canonical format (lowercase, keep backslashes)
                // Database stores paths with backslashes, so we DON'T convert to forward slashes
                var canonicalPath = e.FilePath.ToLowerInvariant();
                _logger.Debug($"   Canonical path: {canonicalPath}");
                
                // Step 2: Get node from database
                _logger.Debug($"🔍 Querying database for node...");
                var node = await _repository.GetNodeByPathAsync(canonicalPath);
                
                if (node == null)
                {
                    _logger.Warning($"⚠️ Node not found in DB (may be new external file): {e.FilePath}");
                    _logger.Info("   FileWatcherService will sync it on next scan");
                    return; // Graceful degradation - file is saved, DB will sync later
                }
                
                _logger.Info($"✅ Node found in DB: {node.Name} (ID: {node.Id})");

                // Step 3: Get fresh file metadata
                if (!File.Exists(e.FilePath))
                {
                    _logger.Warning($"File doesn't exist after save (race condition?): {e.FilePath}");
                    return;
                }
                
                var fileInfo = new FileInfo(e.FilePath);
                _logger.Info($"📊 File metadata: Size={fileInfo.Length} bytes, Modified={fileInfo.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss}");
                
                // Step 4: Create updated node with new metadata
                _logger.Debug("🔧 Creating updated TreeNode with fresh metadata...");
                // TreeNode is immutable (proper domain model), so we create a new instance
                var updatedNode = TreeNode.CreateFromDatabase(
                    id: node.Id,
                    parentId: node.ParentId,
                    canonicalPath: node.CanonicalPath,
                    displayPath: node.DisplayPath,
                    absolutePath: node.AbsolutePath,
                    nodeType: node.NodeType,
                    name: node.Name,
                    fileExtension: node.FileExtension,
                    fileSize: fileInfo.Length,  // ← Updated
                    createdAt: node.CreatedAt,
                    modifiedAt: e.SavedAt,  // ← Updated
                    accessedAt: DateTime.UtcNow,  // ← Updated (accessed when saved)
                    quickHash: node.QuickHash,
                    fullHash: node.FullHash,
                    hashAlgorithm: node.HashAlgorithm,
                    hashCalculatedAt: node.HashCalculatedAt,
                    isExpanded: node.IsExpanded,
                    isPinned: node.IsPinned,
                    isSelected: node.IsSelected,
                    sortOrder: node.SortOrder,
                    colorTag: node.ColorTag,
                    iconOverride: node.IconOverride,
                    isDeleted: node.IsDeleted,
                    deletedAt: node.DeletedAt,
                    metadataVersion: node.MetadataVersion,
                    customProperties: node.CustomProperties
                );
                
                // Step 5: Persist to database
                _logger.Info($"💾 Updating database record...");
                var updateStartTime = DateTime.Now;
                var success = await _repository.UpdateNodeAsync(updatedNode);
                var updateDuration = (DateTime.Now - updateStartTime).TotalMilliseconds;
                
                if (success)
                {
                    _logger.Info($"✅ DATABASE UPDATE SUCCESS:");
                    _logger.Info($"   Node: {node.Name}");
                    _logger.Info($"   New Size: {fileInfo.Length} bytes");
                    _logger.Info($"   New ModifiedAt: {e.SavedAt:yyyy-MM-dd HH:mm:ss.fff}");
                    _logger.Info($"   Update Duration: {updateDuration:F2}ms");
                    _logger.Info("─────────────────────────────────────────────────────────────");
                }
                else
                {
                    _logger.Warning($"⚠️ DATABASE UPDATE FAILED:");
                    _logger.Warning($"   UpdateNodeAsync() returned false for: {node.Name}");
                    _logger.Warning($"   Duration: {updateDuration:F2}ms");
                    _logger.Warning("─────────────────────────────────────────────────────────────");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                // File system permission issue - log but don't crash
                _logger.Warning($"File access denied when updating metadata: {e.FilePath} - {ex.Message}");
            }
            catch (IOException ex)
            {
                // File locked or in use - log but don't crash
                _logger.Warning($"File I/O error when updating metadata: {e.FilePath} - {ex.Message}");
            }
            catch (Exception ex)
            {
                // Catch ALL exceptions - async void handlers must never throw
                _logger.Error(ex, $"❌ Failed to update database metadata for: {e.FilePath}");
                // Non-critical failure: File is saved (source of truth), DB will sync via FileWatcher eventually
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_saveManager != null)
                {
                    _saveManager.NoteSaved -= OnNoteSaved;
                }
                _disposed = true;
            }
        }
    }
}

