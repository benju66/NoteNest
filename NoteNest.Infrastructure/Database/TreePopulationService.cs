using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Trees;

namespace NoteNest.Infrastructure.Database
{
    /// <summary>
    /// Service to populate the TreeNode database from file system on first run
    /// </summary>
    public interface ITreePopulationService
    {
        Task<bool> PopulateFromFileSystemAsync(string rootPath, bool forceRebuild = false);
        Task<bool> IsDatabaseEmptyAsync();
    }

    public class TreePopulationService : ITreePopulationService
    {
        private readonly ITreeDatabaseRepository _repository;
        private readonly IAppLogger _logger;

        public TreePopulationService(ITreeDatabaseRepository repository, IAppLogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> IsDatabaseEmptyAsync()
        {
            try
            {
                var allNodes = await _repository.GetAllNodesAsync();
                return allNodes.Count == 0;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to check if database is empty");
                return true; // Assume empty on error for safety
            }
        }

        public async Task<bool> PopulateFromFileSystemAsync(string rootPath, bool forceRebuild = false)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
            {
                _logger.Warning($"Root path does not exist: {rootPath}");
                return false;
            }

            try
            {
                _logger.Info($"Starting population of TreeNode database from: {rootPath}");

                if (!forceRebuild && !await IsDatabaseEmptyAsync())
                {
                    _logger.Info("Database already has data, skipping population. Use forceRebuild=true to rebuild.");
                    return true;
                }

                var nodes = new List<TreeNode>();
                await ScanDirectoryRecursive(rootPath, null, nodes);

                _logger.Info($"Found {nodes.Count} nodes, inserting into database...");

                // Use bulk insert for performance
                var insertCount = await _repository.BulkInsertNodesAsync(nodes);
                _logger.Info($"Successfully inserted {insertCount} nodes into TreeNode database");

                return insertCount > 0;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to populate database from: {rootPath}");
                return false;
            }
        }

        private async Task ScanDirectoryRecursive(string path, Guid? parentId, List<TreeNode> nodes)
        {
            try
            {
                var dirInfo = new DirectoryInfo(path);
                if (!dirInfo.Exists)
                    return;

                // Create category node for this directory
                var categoryId = Guid.NewGuid();
                var categoryNode = TreeNode.CreateFromDatabase(
                    id: categoryId,
                    parentId: parentId,
                    canonicalPath: path.ToLowerInvariant(),
                    displayPath: path,
                    absolutePath: path,
                    nodeType: TreeNodeType.Category,
                    name: dirInfo.Name,
                    createdAt: dirInfo.CreationTimeUtc,
                    modifiedAt: dirInfo.LastWriteTimeUtc,
                    isExpanded: false,
                    isPinned: false
                );

                nodes.Add(categoryNode);

                // Scan for note files
                var supportedExtensions = new[] { ".txt", ".rtf", ".md" };
                foreach (var extension in supportedExtensions)
                {
                    var files = dirInfo.GetFiles($"*{extension}");
                    foreach (var file in files)
                    {
                        var noteId = Guid.NewGuid();
                        var noteNode = TreeNode.CreateFromDatabase(
                            id: noteId,
                            parentId: categoryId,
                            canonicalPath: file.FullName.ToLowerInvariant(),
                            displayPath: file.FullName,
                            absolutePath: file.FullName,
                            nodeType: TreeNodeType.Note,
                            name: Path.GetFileNameWithoutExtension(file.Name),
                            fileExtension: file.Extension,
                            fileSize: file.Length,
                            createdAt: file.CreationTimeUtc,
                            modifiedAt: file.LastWriteTimeUtc,
                            isExpanded: false,
                            isPinned: false
                        );

                        nodes.Add(noteNode);
                    }
                }

                // Recursively scan subdirectories
                var subdirectories = dirInfo.GetDirectories()
                    .Where(d => !d.Name.StartsWith(".") && !d.Attributes.HasFlag(FileAttributes.Hidden))
                    .OrderBy(d => d.Name);

                foreach (var subdir in subdirectories)
                {
                    await ScanDirectoryRecursive(subdir.FullName, categoryId, nodes);
                }
            }
            catch (UnauthorizedAccessException)
            {
                _logger.Warning($"Access denied to directory: {path}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error scanning directory: {path}");
            }
        }
    }
}
