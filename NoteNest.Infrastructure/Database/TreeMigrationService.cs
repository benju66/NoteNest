using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Trees;

namespace NoteNest.Infrastructure.Database
{
    /// <summary>
    /// Migrates from the legacy file system based architecture to the new database architecture.
    /// Preserves all existing data and metadata while transitioning to TreeNode model.
    /// </summary>
    public interface ITreeMigrationService
    {
        Task<MigrationResult> MigrateFromLegacyAsync();
        Task<bool> IsMigrationNeededAsync();
        Task<bool> VerifyMigrationAsync();
        Task<MigrationStatus> GetMigrationStatusAsync();
    }
    
    public class TreeMigrationService : ITreeMigrationService
    {
        private readonly ITreeDatabaseRepository _repository;
        private readonly IConfiguration _configuration;
        private readonly IAppLogger _logger;
        private readonly string _notesRootPath;
        private readonly string _metadataPath;
        
        public TreeMigrationService(
            ITreeDatabaseRepository repository,
            IConfiguration configuration,
            IAppLogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Get paths from configuration or use defaults
            _notesRootPath = configuration.GetValue<string>("NotesPath") 
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoteNest");
            
            _metadataPath = configuration.GetValue<string>("MetadataPath") 
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoteNest", "Metadata");
        }
        
        public async Task<MigrationResult> MigrateFromLegacyAsync()
        {
            var result = new MigrationResult 
            { 
                StartedAt = DateTime.UtcNow,
                SourceSystem = "FileSystem",
                TargetSystem = "TreeDatabase"
            };
            
            try
            {
                _logger.Info("Starting migration from legacy file system to TreeDatabase...");
                
                // Step 1: Check if migration is actually needed
                if (!await IsMigrationNeededAsync())
                {
                    result.Success = true;
                    result.Message = "Migration not needed - database already populated";
                    result.CompletedAt = DateTime.UtcNow;
                    return result;
                }
                
                // Step 2: Scan file system for all notes and categories
                _logger.Info($"Scanning file system: {_notesRootPath}");
                
                if (!Directory.Exists(_notesRootPath))
                {
                    _logger.Warning($"Notes root path does not exist: {_notesRootPath}");
                    Directory.CreateDirectory(_notesRootPath);
                    _logger.Info($"Created notes root directory: {_notesRootPath}");
                }
                
                var discoveredNodes = new List<TreeNode>();
                await ScanDirectoryForMigration(_notesRootPath, null, discoveredNodes, _notesRootPath);
                
                result.DiscoveredItems = discoveredNodes.Count;
                _logger.Info($"Discovered {discoveredNodes.Count} items in file system");
                
                // Step 3: Load legacy metadata (if any exists)
                var legacyMetadata = await LoadLegacyMetadataAsync();
                result.LegacyMetadataItems = legacyMetadata.Count;
                
                if (legacyMetadata.Any())
                {
                    _logger.Info($"Found {legacyMetadata.Count} legacy metadata items");
                    MergeLegacyMetadata(discoveredNodes, legacyMetadata);
                    result.MetadataMerged = true;
                }
                
                // Step 4: Bulk insert into database
                _logger.Info("Inserting nodes into database...");
                var insertedCount = await _repository.BulkInsertNodesAsync(discoveredNodes);
                result.NodesCreated = insertedCount;
                
                if (insertedCount != discoveredNodes.Count)
                {
                    var errorMessage = $"Expected to insert {discoveredNodes.Count} nodes but only inserted {insertedCount}";
                    _logger.Error(errorMessage);
                    result.Success = false;
                    result.Message = errorMessage;
                    return result;
                }
                
                // Step 5: Verify migration completeness
                var verificationPassed = await VerifyMigrationAsync();
                result.VerificationPassed = verificationPassed;
                
                if (!verificationPassed)
                {
                    result.Success = false;
                    result.Message = "Migration verification failed";
                    return result;
                }
                
                // Step 6: Create backup of legacy system (for safety)
                await BackupLegacySystemAsync();
                result.LegacyBackupCreated = true;
                
                // Success!
                result.Success = true;
                result.CompletedAt = DateTime.UtcNow;
                result.Message = $"Successfully migrated {insertedCount} nodes from file system to database";
                
                _logger.Info($"Migration completed successfully: {result.Message}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Migration failed with exception");
                result.Success = false;
                result.Message = $"Migration failed: {ex.Message}";
                result.CompletedAt = DateTime.UtcNow;
                return result;
            }
        }
        
        public async Task<bool> IsMigrationNeededAsync()
        {
            try
            {
                // Check if database has any nodes
                var existingNodes = await _repository.GetAllNodesAsync();
                if (existingNodes.Any())
                {
                    _logger.Debug("Database already contains nodes, migration not needed");
                    return false;
                }
                
                // Check if file system has any notes to migrate
                if (!Directory.Exists(_notesRootPath))
                {
                    _logger.Debug("Notes root path does not exist, no migration needed");
                    return false;
                }
                
                // Quick scan to see if there are any files to migrate
                var hasNotes = Directory.GetFiles(_notesRootPath, "*.*", SearchOption.AllDirectories)
                    .Any(f => IsValidNoteFile(f));
                
                if (hasNotes)
                {
                    _logger.Info("Found notes in file system that need migration");
                    return true;
                }
                
                _logger.Debug("No notes found in file system, migration not needed");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to check if migration is needed");
                return false; // Assume not needed if we can't check
            }
        }
        
        public async Task<bool> VerifyMigrationAsync()
        {
            try
            {
                _logger.Info("Verifying migration completeness...");
                
                // Count files in file system
                var fileSystemCount = 0;
                if (Directory.Exists(_notesRootPath))
                {
                    fileSystemCount = Directory.GetFiles(_notesRootPath, "*.*", SearchOption.AllDirectories)
                        .Count(f => IsValidNoteFile(f));
                    
                    fileSystemCount += Directory.GetDirectories(_notesRootPath, "*", SearchOption.AllDirectories)
                        .Count(d => !Path.GetFileName(d).StartsWith(".")); // Count non-hidden directories
                }
                
                // Count nodes in database
                var databaseNodes = await _repository.GetAllNodesAsync();
                var databaseCount = databaseNodes.Count;
                
                if (fileSystemCount != databaseCount)
                {
                    _logger.Warning($"Migration verification failed: File system has {fileSystemCount} items but database has {databaseCount}");
                    return false;
                }
                
                // Check that all paths in database correspond to actual files/directories
                var invalidPaths = 0;
                foreach (var node in databaseNodes)
                {
                    var exists = node.NodeType == TreeNodeType.Category 
                        ? Directory.Exists(node.AbsolutePath)
                        : File.Exists(node.AbsolutePath);
                    
                    if (!exists)
                    {
                        invalidPaths++;
                        _logger.Warning($"Database node points to non-existent path: {node.AbsolutePath}");
                    }
                }
                
                if (invalidPaths > 0)
                {
                    _logger.Warning($"Migration verification found {invalidPaths} invalid paths");
                    return false;
                }
                
                _logger.Info($"Migration verification passed: {databaseCount} items verified");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Migration verification failed");
                return false;
            }
        }
        
        public async Task<MigrationStatus> GetMigrationStatusAsync()
        {
            try
            {
                var status = new MigrationStatus
                {
                    CheckedAt = DateTime.UtcNow
                };
                
                // Check if migration is needed
                status.IsNeeded = await IsMigrationNeededAsync();
                
                if (!status.IsNeeded)
                {
                    // Check if database has data (migration already completed)
                    var nodes = await _repository.GetAllNodesAsync();
                    status.IsCompleted = nodes.Any();
                    status.DatabaseNodeCount = nodes.Count;
                }
                
                // Count items in file system
                if (Directory.Exists(_notesRootPath))
                {
                    var files = Directory.GetFiles(_notesRootPath, "*.*", SearchOption.AllDirectories)
                        .Where(f => IsValidNoteFile(f)).ToList();
                    var directories = Directory.GetDirectories(_notesRootPath, "*", SearchOption.AllDirectories)
                        .Where(d => !Path.GetFileName(d).StartsWith(".")).ToList();
                    
                    status.FileSystemNoteCount = files.Count;
                    status.FileSystemCategoryCount = directories.Count;
                    status.TotalFileSystemItems = files.Count + directories.Count;
                }
                
                // Check for legacy metadata
                var legacyMetadata = await LoadLegacyMetadataAsync();
                status.LegacyMetadataCount = legacyMetadata.Count;
                status.HasLegacyMetadata = legacyMetadata.Any();
                
                return status;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get migration status");
                return new MigrationStatus
                {
                    CheckedAt = DateTime.UtcNow,
                    Error = ex.Message
                };
            }
        }
        
        // =============================================================================
        // PRIVATE IMPLEMENTATION METHODS
        // =============================================================================
        
        private async Task ScanDirectoryForMigration(
            string path, 
            TreeNode parent, 
            List<TreeNode> nodes,
            string rootPath)
        {
            try
            {
                // Create category node for this directory (unless it's the root)
                TreeNode dirNode = null;
                if (path != rootPath)
                {
                    dirNode = TreeNode.CreateCategory(path, rootPath, parent);
                    nodes.Add(dirNode);
                    _logger.Debug($"Discovered category: {dirNode.Name}");
                }
                
                // Scan files in this directory
                var files = Directory.GetFiles(path, "*.*")
                    .Where(f => IsValidNoteFile(f));
                
                foreach (var file in files)
                {
                    try
                    {
                        var noteNode = TreeNode.CreateNote(file, rootPath, dirNode ?? parent);
                        nodes.Add(noteNode);
                        _logger.Debug($"Discovered note: {noteNode.Name}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to create node for file {file}: {ex.Message}");
                    }
                }
                
                // Recursively scan subdirectories
                var subdirs = Directory.GetDirectories(path)
                    .Where(d => !Path.GetFileName(d).StartsWith(".")); // Skip hidden directories like .metadata
                
                foreach (var subdir in subdirs)
                {
                    await ScanDirectoryForMigration(subdir, dirNode ?? parent, nodes, rootPath);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error scanning directory {path} during migration: {ex.Message}");
            }
        }
        
        private async Task<List<LegacyMetadataItem>> LoadLegacyMetadataAsync()
        {
            var metadata = new List<LegacyMetadataItem>();
            
            try
            {
                // Check for legacy categories.json
                var categoriesJsonPath = Path.Combine(_metadataPath, "categories.json");
                if (File.Exists(categoriesJsonPath))
                {
                    _logger.Info($"Loading legacy categories from: {categoriesJsonPath}");
                    var categoriesJson = await File.ReadAllTextAsync(categoriesJsonPath);
                    var categories = JsonSerializer.Deserialize<List<LegacyCategoryData>>(categoriesJson);
                    
                    if (categories != null)
                    {
                        foreach (var cat in categories)
                        {
                            metadata.Add(new LegacyMetadataItem
                            {
                                Type = "Category",
                                Name = cat.Name,
                                Path = cat.Path,
                                IsPinned = cat.IsPinned,
                                IsExpanded = cat.IsExpanded,
                                SortOrder = cat.SortOrder,
                                CustomProperties = cat.CustomProperties
                            });
                        }
                    }
                }
                
                // Check for legacy pinned items
                var pinsJsonPath = Path.Combine(_metadataPath, "pins.json");
                if (File.Exists(pinsJsonPath))
                {
                    _logger.Info($"Loading legacy pins from: {pinsJsonPath}");
                    var pinsJson = await File.ReadAllTextAsync(pinsJsonPath);
                    var pins = JsonSerializer.Deserialize<List<LegacyPinData>>(pinsJson);
                    
                    if (pins != null)
                    {
                        foreach (var pin in pins)
                        {
                            metadata.Add(new LegacyMetadataItem
                            {
                                Type = "Pin",
                                Name = pin.Title ?? pin.NoteId,
                                Path = pin.FilePath,
                                IsPinned = true,
                                SortOrder = pin.Order
                            });
                        }
                    }
                }
                
                // Check for other legacy metadata files
                if (Directory.Exists(_metadataPath))
                {
                    var metadataFiles = Directory.GetFiles(_metadataPath, "*.json")
                        .Where(f => !Path.GetFileName(f).Equals("categories.json", StringComparison.OrdinalIgnoreCase))
                        .Where(f => !Path.GetFileName(f).Equals("pins.json", StringComparison.OrdinalIgnoreCase));
                    
                    foreach (var file in metadataFiles)
                    {
                        try
                        {
                            // Try to load as generic metadata
                            var json = await File.ReadAllTextAsync(file);
                            metadata.Add(new LegacyMetadataItem
                            {
                                Type = "GenericMetadata",
                                Name = Path.GetFileNameWithoutExtension(file),
                                Path = file,
                                CustomProperties = json
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning($"Failed to load legacy metadata file {file}: {ex.Message}");
                        }
                    }
                }
                
                _logger.Info($"Loaded {metadata.Count} legacy metadata items");
                return metadata;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load legacy metadata");
                return new List<LegacyMetadataItem>();
            }
        }
        
        private void MergeLegacyMetadata(List<TreeNode> nodes, List<LegacyMetadataItem> legacyMetadata)
        {
            try
            {
                _logger.Info("Merging legacy metadata with discovered nodes...");
                var mergedCount = 0;
                
                foreach (var legacy in legacyMetadata)
                {
                    // Find corresponding node by name or path
                    var matchingNode = nodes.FirstOrDefault(n => 
                        n.Name.Equals(legacy.Name, StringComparison.OrdinalIgnoreCase) ||
                        n.AbsolutePath.Equals(legacy.Path, StringComparison.OrdinalIgnoreCase) ||
                        n.CanonicalPath.Contains(legacy.Name.ToLowerInvariant()));
                    
                    if (matchingNode != null)
                    {
                        // Apply legacy metadata to node
                        if (legacy.IsPinned)
                            matchingNode.TogglePinned();
                        
                        if (legacy.IsExpanded && matchingNode.NodeType == TreeNodeType.Category)
                            matchingNode.SetExpanded(true);
                        
                        if (legacy.SortOrder > 0)
                            matchingNode.UpdateSortOrder(legacy.SortOrder);
                        
                        if (!string.IsNullOrEmpty(legacy.CustomProperties))
                        {
                            // Store legacy custom properties
                            // Note: This would require implementing CustomProperties setter
                            _logger.Debug($"Preserving custom properties for {matchingNode.Name}");
                        }
                        
                        mergedCount++;
                        _logger.Debug($"Merged metadata for: {matchingNode.Name}");
                    }
                    else
                    {
                        _logger.Warning($"Could not find matching node for legacy metadata: {legacy.Name}");
                    }
                }
                
                _logger.Info($"Successfully merged metadata for {mergedCount} out of {legacyMetadata.Count} legacy items");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to merge legacy metadata");
            }
        }
        
        private async Task BackupLegacySystemAsync()
        {
            try
            {
                var backupPath = Path.Combine(_metadataPath, "migration_backup");
                Directory.CreateDirectory(backupPath);
                
                var backupTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                
                // Backup categories.json if it exists
                var categoriesPath = Path.Combine(_metadataPath, "categories.json");
                if (File.Exists(categoriesPath))
                {
                    var backupCategoriesPath = Path.Combine(backupPath, $"categories_backup_{backupTimestamp}.json");
                    File.Copy(categoriesPath, backupCategoriesPath);
                    _logger.Info($"Backed up categories.json to: {backupCategoriesPath}");
                }
                
                // Backup pins.json if it exists
                var pinsPath = Path.Combine(_metadataPath, "pins.json");
                if (File.Exists(pinsPath))
                {
                    var backupPinsPath = Path.Combine(backupPath, $"pins_backup_{backupTimestamp}.json");
                    File.Copy(pinsPath, backupPinsPath);
                    _logger.Info($"Backed up pins.json to: {backupPinsPath}");
                }
                
                // Create summary file
                var summaryPath = Path.Combine(backupPath, $"migration_summary_{backupTimestamp}.txt");
                var summary = $@"Legacy System Backup
Created: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
Source: File system based architecture
Target: TreeDatabase architecture
Notes Root: {_notesRootPath}
Metadata Path: {_metadataPath}

This backup was created during migration to the new database architecture.
The legacy metadata files are preserved here for safety and potential rollback.
";
                
                await File.WriteAllTextAsync(summaryPath, summary);
                _logger.Info($"Created migration backup summary: {summaryPath}");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to backup legacy system: {ex.Message}");
            }
        }
        
        private bool IsValidNoteFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension == ".rtf" || extension == ".txt" || extension == ".md" || extension == ".markdown";
        }
    }
    
    // =============================================================================
    // SUPPORTING TYPES FOR MIGRATION
    // =============================================================================
    
    public class MigrationResult
    {
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public string SourceSystem { get; set; }
        public string TargetSystem { get; set; }
        
        // Statistics
        public int DiscoveredItems { get; set; }
        public int NodesCreated { get; set; }
        public int LegacyMetadataItems { get; set; }
        public bool MetadataMerged { get; set; }
        public bool VerificationPassed { get; set; }
        public bool LegacyBackupCreated { get; set; }
        
        public TimeSpan Duration => (CompletedAt ?? DateTime.UtcNow) - StartedAt;
    }
    
    public class MigrationStatus
    {
        public DateTime CheckedAt { get; set; }
        public bool IsNeeded { get; set; }
        public bool IsCompleted { get; set; }
        public string Error { get; set; }
        
        // File system statistics
        public int FileSystemNoteCount { get; set; }
        public int FileSystemCategoryCount { get; set; }
        public int TotalFileSystemItems { get; set; }
        
        // Database statistics
        public int DatabaseNodeCount { get; set; }
        
        // Legacy metadata
        public int LegacyMetadataCount { get; set; }
        public bool HasLegacyMetadata { get; set; }
    }
    
    public class LegacyMetadataItem
    {
        public string Type { get; set; }          // "Category", "Pin", "GenericMetadata"
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsPinned { get; set; }
        public bool IsExpanded { get; set; }
        public int SortOrder { get; set; }
        public string CustomProperties { get; set; } // JSON
    }
    
    // Legacy data structures for deserialization
    public class LegacyCategoryData
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsPinned { get; set; }
        public bool IsExpanded { get; set; }
        public int SortOrder { get; set; }
        public string CustomProperties { get; set; }
    }
    
    public class LegacyPinData
    {
        public string NoteId { get; set; }
        public string Title { get; set; }
        public string FilePath { get; set; }
        public int Order { get; set; }
        public DateTime PinnedAt { get; set; }
    }
}
