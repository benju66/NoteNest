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
    /// Event-driven service that synchronizes database metadata when notes are saved.
    /// Listens to ISaveManager.NoteSaved events and updates tree_nodes table.
    /// 
    /// Architecture:
    /// - RTFIntegratedSaveEngine (Core) fires NoteSaved event
    /// - This service (Infrastructure) listens and updates database
    /// - No circular dependency (Core doesn't know about Infrastructure)
    /// - File system remains source of truth, database is performance cache
    /// 
    /// Performance: 2-13ms per update (validated Oct 1, 2025)
    /// Reliability: Graceful degradation if DB update fails (FileWatcher provides backup sync)
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
            _logger.Info("DatabaseMetadataUpdateService starting - Event-driven DB sync active");
            
            // Subscribe to save events
            _saveManager.NoteSaved += OnNoteSaved;
            
            _logger.Info("✅ Subscribed to save events - Database will stay synchronized with file changes");
            
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.Info("DatabaseMetadataUpdateService stopped");
            
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
                // Normalize path to canonical format (lowercase, preserve backslashes)
                var canonicalPath = e.FilePath.ToLowerInvariant();
                
                // Get node from database
                var node = await _repository.GetNodeByPathAsync(canonicalPath);
                
                if (node == null)
                {
                    _logger.Debug($"Node not found in DB for: {Path.GetFileName(e.FilePath)} - FileWatcher will sync it");
                    return; // Graceful degradation - file is saved, DB will sync later
                }

                // Get fresh file metadata
                if (!File.Exists(e.FilePath))
                {
                    _logger.Warning($"File doesn't exist after save: {e.FilePath}");
                    return;
                }
                
                var fileInfo = new FileInfo(e.FilePath);
                
                // Create updated TreeNode with fresh metadata (TreeNode is immutable)
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
                
                // Persist to database
                var success = await _repository.UpdateNodeAsync(updatedNode);
                
                if (success)
                {
                    _logger.Debug($"DB metadata updated: {node.Name} ({fileInfo.Length} bytes)");
                }
                else
                {
                    _logger.Warning($"Failed to update DB metadata for: {node.Name}");
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

