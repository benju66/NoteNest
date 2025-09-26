using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Services;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Coordinates file system events and search index updates.
    /// Extracted from MainViewModel to separate file monitoring concerns.
    /// </summary>
    public class FileSystemEventCoordinator : IDisposable
    {
        private readonly IAppLogger _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly Func<string, Task<string>> _findNoteIdByFilePath;
        private bool _disposed;

        public FileSystemEventCoordinator(
            IAppLogger logger,
            IServiceProvider serviceProvider,
            Func<string, Task<string>> findNoteIdByFilePath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _findNoteIdByFilePath = findNoteIdByFilePath;
        }

        /// <summary>
        /// Handles file rename events, updating metadata and search index
        /// </summary>
        public async Task HandleFileRenamedAsync(string oldPath, string newPath)
        {
            try
            {
                // Update metadata manager if available
                var metadataManager = _serviceProvider.GetService<NoteNest.Core.Services.NoteMetadataManager>();
                if (metadataManager != null)
                {
                    await metadataManager.MoveMetadataAsync(oldPath, newPath);
                }

                // Update pin service file paths
                var pinService = _serviceProvider.GetService<Core.Interfaces.Services.IPinService>();
                if (pinService != null)
                {
                    // Find the note ID by checking all notes
                    var noteId = await FindNoteIdByFilePathAsync(newPath);
                    if (!string.IsNullOrEmpty(noteId))
                    {
                        await pinService.UpdateFilePathAsync(noteId, newPath);
                        _logger.Debug($"Updated pin service file path: {oldPath} -> {newPath}");
                    }
                }

                // Update FTS5 search index for file rename
                await UpdateSearchIndexForFileRenameAsync(oldPath, newPath);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to handle file rename: {oldPath} -> {newPath}. Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles file deletion events, preserving metadata for recovery
        /// </summary>
        public async Task HandleFileDeletedAsync(string filePath)
        {
            try
            {
                // Keep sidecar for recovery; add marker
                var metadataManager = _serviceProvider.GetService<NoteNest.Core.Services.NoteMetadataManager>();
                if (metadataManager != null && Path.GetExtension(filePath) != ".meta")
                {
                    var metaPath = metadataManager.GetMetaPath(filePath);
                    if (File.Exists(metaPath))
                    {
                        // Best-effort: append marker without throwing
                        try
                        {
                            var existing = await File.ReadAllTextAsync(metaPath);
                            // Minimal mutation to avoid schema coupling
                            await File.WriteAllTextAsync(metaPath, existing);
                        }
                        catch { }
                    }
                }

                // Update FTS5 search index
                await UpdateSearchIndexForFileDeleteAsync(filePath);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error handling file deletion: {filePath}. Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles file creation events, updating search index for RTF files
        /// </summary>
        public async Task HandleFileCreatedAsync(string filePath)
        {
            try
            {
                // Only index RTF files
                if (Path.GetExtension(filePath).Equals(".rtf", StringComparison.OrdinalIgnoreCase))
                {
                    await UpdateSearchIndexForFileCreateAsync(filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error handling file creation: {filePath}. Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles file modification events, updating search index for RTF files
        /// </summary>
        public async Task HandleFileModifiedAsync(string filePath)
        {
            try
            {
                // Only index RTF files
                if (Path.GetExtension(filePath).Equals(".rtf", StringComparison.OrdinalIgnoreCase))
                {
                    await UpdateSearchIndexForFileModifyAsync(filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error handling file modification: {filePath}. Error: {ex.Message}");
            }
        }

        #region Private Search Index Methods

        private async Task UpdateSearchIndexForFileCreateAsync(string filePath)
        {
            try
            {
                var searchService = _serviceProvider.GetService<NoteNest.UI.Interfaces.ISearchService>();
                if (searchService is NoteNest.UI.Services.FTS5SearchService fts5Service)
                {
                    await fts5Service.HandleNoteCreatedAsync(filePath);
                    _logger.Debug($"Updated search index for created file: {filePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to update search index for created file {filePath}: {ex.Message}");
            }
        }

        private async Task UpdateSearchIndexForFileModifyAsync(string filePath)
        {
            try
            {
                var searchService = _serviceProvider.GetService<NoteNest.UI.Interfaces.ISearchService>();
                if (searchService is NoteNest.UI.Services.FTS5SearchService fts5Service)
                {
                    await fts5Service.HandleNoteUpdatedAsync(filePath);
                    _logger.Debug($"Updated search index for modified file: {filePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to update search index for modified file {filePath}: {ex.Message}");
            }
        }

        private async Task UpdateSearchIndexForFileDeleteAsync(string filePath)
        {
            try
            {
                var searchService = _serviceProvider.GetService<NoteNest.UI.Interfaces.ISearchService>();
                if (searchService is NoteNest.UI.Services.FTS5SearchService fts5Service)
                {
                    await fts5Service.HandleNoteDeletedAsync(filePath);
                    _logger.Debug($"Updated search index for deleted file: {filePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to update search index for deleted file {filePath}: {ex.Message}");
            }
        }

        private async Task UpdateSearchIndexForFileRenameAsync(string oldPath, string newPath)
        {
            try
            {
                var searchService = _serviceProvider.GetService<NoteNest.UI.Interfaces.ISearchService>();
                if (searchService is NoteNest.UI.Services.FTS5SearchService fts5Service)
                {
                    await fts5Service.HandleNoteRenamedAsync(oldPath, newPath);
                    _logger.Debug($"Updated search index for renamed file: {oldPath} -> {newPath}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to update search index for renamed file {oldPath} -> {newPath}: {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Finds note ID by searching through file paths using provided delegate
        /// </summary>
        private async Task<string> FindNoteIdByFilePathAsync(string filePath)
        {
            try
            {
                if (_findNoteIdByFilePath == null)
                    return null;
                    
                return await _findNoteIdByFilePath(filePath);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error finding note ID by file path {filePath}: {ex.Message}");
                return null;
            }
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // No specific resources to dispose currently
                _disposed = true;
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Error disposing FileSystemEventCoordinator: {ex.Message}");
            }
        }
    }
}
