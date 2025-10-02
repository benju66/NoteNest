using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Trees;
using NoteNest.Domain.Common;

namespace NoteNest.Infrastructure.Database
{
    /// <summary>
    /// High-performance SQLite repository for tree operations.
    /// Implements complete CRUD, bulk operations, and tree-specific queries.
    /// Files remain source of truth - this is a rebuildable performance cache.
    /// </summary>
    public interface ITreeDatabaseRepository
    {
        // =============================================================================
        // CORE QUERY OPERATIONS
        // =============================================================================
        
        Task<TreeNode> GetNodeByIdAsync(Guid id);
        Task<TreeNode> GetNodeByPathAsync(string canonicalPath);
        Task<List<TreeNode>> GetChildrenAsync(Guid parentId);
        Task<List<TreeNode>> GetAllNodesAsync(bool includeDeleted = false);
        
        // Tree-specific queries
        Task<List<TreeNode>> GetTreeHierarchyAsync();
        Task<List<TreeNode>> GetRootNodesAsync();
        Task<List<TreeNode>> GetPinnedNodesAsync();
        Task<List<TreeNode>> GetRecentlyModifiedAsync(int count = 50);
        
        // =============================================================================
        // SEARCH OPERATIONS - Database-powered search
        // =============================================================================
        
        Task<List<TreeNode>> SearchNotesByTitleAsync(string searchTerm);
        Task<List<TreeNode>> SearchNotesByContentAsync(string searchTerm);
        
        // =============================================================================
        // CRUD OPERATIONS
        // =============================================================================
        
        Task<bool> InsertNodeAsync(TreeNode node);
        Task<bool> UpdateNodeAsync(TreeNode node);
        Task<bool> DeleteNodeAsync(Guid id, bool softDelete = true);
        Task<int> BulkInsertNodesAsync(IEnumerable<TreeNode> nodes);
        Task<int> BulkUpdateNodesAsync(IEnumerable<TreeNode> nodes);
        
        // =============================================================================
        // TREE OPERATIONS
        // =============================================================================
        
        Task<bool> MoveNodeAsync(Guid nodeId, Guid newParentId);
        Task<bool> RenameNodeAsync(Guid nodeId, string newName);
        Task<List<TreeNode>> GetNodeDescendantsAsync(Guid nodeId);
        Task<TreeNode> GetNodeAncestorAsync(Guid nodeId, TreeNodeType ancestorType);
        
        // =============================================================================
        // MAINTENANCE OPERATIONS
        // =============================================================================
        
        Task<bool> PurgeDeletedNodesAsync(int daysOld = 30);
        Task<bool> RebuildFromFileSystemAsync(string rootPath, IProgress<RebuildProgress> progress = null, CancellationToken cancellationToken = default);
        Task<DatabaseHealth> CheckHealthAsync();
        Task<bool> OptimizeAsync();
        Task<bool> VacuumAsync();
        
        // =============================================================================
        // CHANGE DETECTION
        // =============================================================================
        
        Task<List<TreeNode>> GetNodesWithOutdatedHashAsync();
        Task<bool> UpdateNodeHashAsync(Guid nodeId, string quickHash, string fullHash = null);
        Task<int> RefreshAllNodeMetadataAsync();
    }

    public class TreeDatabaseRepository : ITreeDatabaseRepository
    {
        private readonly string _connectionString;
        private readonly IAppLogger _logger;
        private readonly SemaphoreSlim _dbLock = new(1, 1);
        private readonly string _rootPath;
        
        public TreeDatabaseRepository(string connectionString, IAppLogger logger, string rootPath)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        }

        // =============================================================================
        // CORE QUERY OPERATIONS
        // =============================================================================
        
        public async Task<TreeNode> GetNodeByIdAsync(Guid id)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    SELECT 
                        id as Id,
                        name as Name,
                        node_type as NodeType,
                        parent_id as ParentId,
                        canonical_path as CanonicalPath,
                        display_path as DisplayPath,
                        absolute_path as AbsolutePath,
                        file_extension as FileExtension,
                        file_size as FileSize,
                        created_at as CreatedAt,
                        modified_at as ModifiedAt,
                        accessed_at as AccessedAt,
                        quick_hash as QuickHash,
                        full_hash as FullHash,
                        hash_algorithm as HashAlgorithm,
                        hash_calculated_at as HashCalculatedAt,
                        is_expanded as IsExpanded,
                        is_pinned as IsPinned,
                        is_selected as IsSelected,
                        sort_order as SortOrder,
                        color_tag as ColorTag,
                        icon_override as IconOverride,
                        is_deleted as IsDeleted,
                        deleted_at as DeletedAt,
                        metadata_version as MetadataVersion,
                        custom_properties as CustomProperties
                    FROM tree_nodes 
                    WHERE id = @Id AND is_deleted = 0";
                
                var dto = await QuerySingleAsync<TreeNodeDto>(connection, sql, new { Id = id.ToString() });
                return dto?.ToDomainModel();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get node by id: {id}");
                return null;
            }
        }
        
        public async Task<TreeNode> GetNodeByPathAsync(string canonicalPath)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    SELECT 
                        id as Id,
                        name as Name,
                        node_type as NodeType,
                        parent_id as ParentId,
                        canonical_path as CanonicalPath,
                        display_path as DisplayPath,
                        absolute_path as AbsolutePath,
                        file_extension as FileExtension,
                        file_size as FileSize,
                        created_at as CreatedAt,
                        modified_at as ModifiedAt,
                        accessed_at as AccessedAt,
                        quick_hash as QuickHash,
                        full_hash as FullHash,
                        hash_algorithm as HashAlgorithm,
                        hash_calculated_at as HashCalculatedAt,
                        is_expanded as IsExpanded,
                        is_pinned as IsPinned,
                        is_selected as IsSelected,
                        sort_order as SortOrder,
                        color_tag as ColorTag,
                        icon_override as IconOverride,
                        is_deleted as IsDeleted,
                        deleted_at as DeletedAt,
                        metadata_version as MetadataVersion,
                        custom_properties as CustomProperties
                    FROM tree_nodes 
                    WHERE canonical_path = @Path AND is_deleted = 0";
                
                var dto = await QuerySingleAsync<TreeNodeDto>(connection, sql, new { Path = canonicalPath });
                return dto?.ToDomainModel();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get node by path: {canonicalPath}");
                return null;
            }
        }
        
        public async Task<List<TreeNode>> GetChildrenAsync(Guid parentId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    SELECT 
                        id as Id,
                        name as Name,
                        node_type as NodeType,
                        parent_id as ParentId,
                        canonical_path as CanonicalPath,
                        display_path as DisplayPath,
                        absolute_path as AbsolutePath,
                        file_extension as FileExtension,
                        file_size as FileSize,
                        created_at as CreatedAt,
                        modified_at as ModifiedAt,
                        accessed_at as AccessedAt,
                        quick_hash as QuickHash,
                        full_hash as FullHash,
                        hash_algorithm as HashAlgorithm,
                        hash_calculated_at as HashCalculatedAt,
                        is_expanded as IsExpanded,
                        is_pinned as IsPinned,
                        is_selected as IsSelected,
                        sort_order as SortOrder,
                        color_tag as ColorTag,
                        icon_override as IconOverride,
                        is_deleted as IsDeleted,
                        deleted_at as DeletedAt,
                        metadata_version as MetadataVersion,
                        custom_properties as CustomProperties
                    FROM tree_nodes 
                    WHERE parent_id = @ParentId AND is_deleted = 0
                    ORDER BY node_type, sort_order, name";
                
                var dtos = await QueryAsync<TreeNodeDto>(connection, sql, new { ParentId = parentId.ToString() });
                return dtos.Select(dto => dto.ToDomainModel()).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get children for parent: {parentId}");
                return new List<TreeNode>();
            }
        }
        
        public async Task<List<TreeNode>> GetTreeHierarchyAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                // Use the recursive CTE view for efficient tree loading
                var sql = @"
                    SELECT 
                        id as Id,
                        parent_id as ParentId,
                        canonical_path as CanonicalPath,
                        display_path as DisplayPath,
                        absolute_path as AbsolutePath,
                        node_type as NodeType,
                        name as Name,
                        file_extension as FileExtension,
                        file_size as FileSize,
                        created_at as CreatedAt,
                        modified_at as ModifiedAt,
                        accessed_at as AccessedAt,
                        quick_hash as QuickHash,
                        full_hash as FullHash,
                        hash_algorithm as HashAlgorithm,
                        hash_calculated_at as HashCalculatedAt,
                        is_expanded as IsExpanded,
                        is_pinned as IsPinned,
                        is_selected as IsSelected,
                        sort_order as SortOrder,
                        color_tag as ColorTag,
                        icon_override as IconOverride,
                        is_deleted as IsDeleted,
                        deleted_at as DeletedAt,
                        metadata_version as MetadataVersion,
                        custom_properties as CustomProperties,
                        level as Level,
                        root_id as RootId
                    FROM tree_hierarchy";
                
                var dtos = await QueryAsync<TreeNodeDto>(connection, sql);
                return dtos.Select(dto => dto.ToDomainModel()).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get tree hierarchy");
                return new List<TreeNode>();
            }
        }
        
        public async Task<List<TreeNode>> GetRootNodesAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    SELECT 
                        id as Id,
                        name as Name,
                        node_type as NodeType,
                        parent_id as ParentId,
                        canonical_path as CanonicalPath,
                        display_path as DisplayPath,
                        absolute_path as AbsolutePath,
                        file_extension as FileExtension,
                        file_size as FileSize,
                        created_at as CreatedAt,
                        modified_at as ModifiedAt,
                        accessed_at as AccessedAt,
                        quick_hash as QuickHash,
                        full_hash as FullHash,
                        hash_algorithm as HashAlgorithm,
                        hash_calculated_at as HashCalculatedAt,
                        is_expanded as IsExpanded,
                        is_pinned as IsPinned,
                        is_selected as IsSelected,
                        sort_order as SortOrder,
                        color_tag as ColorTag,
                        icon_override as IconOverride,
                        is_deleted as IsDeleted,
                        deleted_at as DeletedAt,
                        metadata_version as MetadataVersion,
                        custom_properties as CustomProperties
                    FROM tree_nodes 
                    WHERE parent_id IS NULL AND is_deleted = 0
                    ORDER BY node_type, sort_order, name";
                
                var dtos = await QueryAsync<TreeNodeDto>(connection, sql);
                return dtos.Select(dto => dto.ToDomainModel()).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get root nodes");
                return new List<TreeNode>();
            }
        }
        
        public async Task<List<TreeNode>> GetPinnedNodesAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "SELECT * FROM pinned_items";
                var dtos = await QueryAsync<TreeNodeDto>(connection, sql);
                return dtos.Select(dto => dto.ToDomainModel()).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get pinned nodes");
                return new List<TreeNode>();
            }
        }

        // =============================================================================
        // CRUD OPERATIONS
        // =============================================================================
        
        public async Task<bool> InsertNodeAsync(TreeNode node)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    INSERT INTO tree_nodes (
                        id, parent_id, canonical_path, display_path, absolute_path,
                        node_type, name, file_extension, file_size,
                        created_at, modified_at, accessed_at, quick_hash, full_hash,
                        hash_algorithm, hash_calculated_at, is_expanded, is_pinned,
                        is_selected, sort_order, color_tag, icon_override,
                        is_deleted, deleted_at, metadata_version, custom_properties
                    ) VALUES (
                        @Id, @ParentId, @CanonicalPath, @DisplayPath, @AbsolutePath,
                        @NodeType, @Name, @FileExtension, @FileSize,
                        @CreatedAt, @ModifiedAt, @AccessedAt, @QuickHash, @FullHash,
                        @HashAlgorithm, @HashCalculatedAt, @IsExpanded, @IsPinned,
                        @IsSelected, @SortOrder, @ColorTag, @IconOverride,
                        @IsDeleted, @DeletedAt, @MetadataVersion, @CustomProperties
                    )";
                
                var parameters = MapToParameters(node);
                await connection.ExecuteAsync(sql, parameters);
                
                _logger.Debug($"Inserted node: {node.Name} ({node.Id})");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to insert node: {node.Name}");
                return false;
            }
        }
        
        public async Task<bool> UpdateNodeAsync(TreeNode node)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    UPDATE tree_nodes SET
                        parent_id = @ParentId,
                        canonical_path = @CanonicalPath,
                        display_path = @DisplayPath,
                        absolute_path = @AbsolutePath,
                        name = @Name,
                        file_extension = @FileExtension,
                        file_size = @FileSize,
                        created_at = @CreatedAt,
                        modified_at = @ModifiedAt,
                        accessed_at = @AccessedAt,
                        quick_hash = @QuickHash,
                        full_hash = @FullHash,
                        hash_calculated_at = @HashCalculatedAt,
                        is_expanded = @IsExpanded,
                        is_pinned = @IsPinned,
                        is_selected = @IsSelected,
                        sort_order = @SortOrder,
                        color_tag = @ColorTag,
                        icon_override = @IconOverride,
                        custom_properties = @CustomProperties
                    WHERE id = @Id";
                
                var parameters = MapToParameters(node);
                var rowsAffected = await connection.ExecuteAsync(sql, parameters);
                
                if (rowsAffected > 0)
                {
                    _logger.Debug($"Updated node: {node.Name} ({node.Id})");
                    return true;
                }
                else
                {
                    _logger.Warning($"No rows updated for node: {node.Id}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to update node: {node.Name}");
                return false;
            }
        }

        public async Task<int> BulkInsertNodesAsync(IEnumerable<TreeNode> nodes)
        {
            await _dbLock.WaitAsync();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                using var transaction = await connection.BeginTransactionAsync();
                
                try
                {
                    var sql = @"
                        INSERT INTO tree_nodes (
                            id, parent_id, canonical_path, display_path, absolute_path,
                            node_type, name, file_extension, file_size,
                            created_at, modified_at, accessed_at, quick_hash, full_hash,
                            hash_algorithm, hash_calculated_at, is_expanded, is_pinned,
                            is_selected, sort_order, color_tag, icon_override,
                            is_deleted, deleted_at, metadata_version, custom_properties
                        ) VALUES (
                            @Id, @ParentId, @CanonicalPath, @DisplayPath, @AbsolutePath,
                            @NodeType, @Name, @FileExtension, @FileSize,
                            @CreatedAt, @ModifiedAt, @AccessedAt, @QuickHash, @FullHash,
                            @HashAlgorithm, @HashCalculatedAt, @IsExpanded, @IsPinned,
                            @IsSelected, @SortOrder, @ColorTag, @IconOverride,
                            @IsDeleted, @DeletedAt, @MetadataVersion, @CustomProperties
                        )";
                    
                    var count = 0;
                    
                    // Process in batches for better performance
                    foreach (var batch in nodes.Chunk(100))
                    {
                        foreach (var node in batch)
                        {
                            var parameters = MapToParameters(node);
                            await connection.ExecuteAsync(sql, parameters, transaction);
                            count++;
                        }
                    }
                    
                    await transaction.CommitAsync();
                    _logger.Info($"Bulk inserted {count} nodes successfully");
                    return count;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            finally
            {
                _dbLock.Release();
            }
        }

        // =============================================================================
        // FILE SYSTEM REBUILD
        // =============================================================================
        
        public async Task<bool> RebuildFromFileSystemAsync(string rootPath, IProgress<RebuildProgress> progress = null, CancellationToken cancellationToken = default)
        {
            await _dbLock.WaitAsync(cancellationToken);
            try
            {
                _logger.Info($"Starting rebuild from file system: {rootPath}");
                
                var nodes = new List<TreeNode>();
                var rebuildProgress = new RebuildProgress();
                
                // Phase 1: Scan file system
                progress?.Report(rebuildProgress.UpdatePhase("Scanning file system..."));
                await ScanDirectoryRecursive(rootPath, null, nodes, rootPath, rebuildProgress, progress, cancellationToken);
                
                rebuildProgress.TotalItems = nodes.Count;
                progress?.Report(rebuildProgress.UpdatePhase($"Found {nodes.Count} items, updating database..."));
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);
                using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                
                try
                {
                    // Phase 2: Soft delete all existing nodes
                    await connection.ExecuteAsync(
                        "UPDATE tree_nodes SET is_deleted = 1, deleted_at = @Now WHERE is_deleted = 0",
                        new { Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                        transaction);
                    
                    // Phase 3: Insert new nodes
                    var sql = @"
                        INSERT INTO tree_nodes (
                            id, parent_id, canonical_path, display_path, absolute_path,
                            node_type, name, file_extension, file_size,
                            created_at, modified_at, accessed_at, quick_hash,
                            is_expanded, is_pinned, sort_order
                        ) VALUES (
                            @Id, @ParentId, @CanonicalPath, @DisplayPath, @AbsolutePath,
                            @NodeType, @Name, @FileExtension, @FileSize,
                            @CreatedAt, @ModifiedAt, @AccessedAt, @QuickHash,
                            @IsExpanded, @IsPinned, @SortOrder
                        )";
                    
                    rebuildProgress.ProcessedItems = 0;
                    
                    foreach (var node in nodes)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        var parameters = MapToParametersForInsert(node);
                        await connection.ExecuteAsync(sql, parameters, transaction);
                        
                        rebuildProgress.ProcessedItems++;
                        rebuildProgress.CurrentItem = node.Name;
                        
                        if (rebuildProgress.ProcessedItems % 50 == 0)
                        {
                            progress?.Report(rebuildProgress);
                        }
                    }
                    
                    await transaction.CommitAsync(cancellationToken);
                    
                    _logger.Info($"Successfully rebuilt database with {nodes.Count} nodes");
                    progress?.Report(rebuildProgress.Complete($"Rebuilt {nodes.Count} items"));
                    
                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Info("Rebuild operation was cancelled");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to rebuild from file system");
                return false;
            }
            finally
            {
                _dbLock.Release();
            }
        }
        
        private async Task ScanDirectoryRecursive(
            string path, 
            TreeNode parent, 
            List<TreeNode> nodes,
            string rootPath,
            RebuildProgress progress,
            IProgress<RebuildProgress> progressReporter,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Create category node for this directory
                var dirNode = TreeNode.CreateCategory(path, rootPath, parent);
                nodes.Add(dirNode);
                
                progress.CurrentItem = Path.GetFileName(path);
                progressReporter?.Report(progress);
                
                // Scan files in this directory
                var files = Directory.GetFiles(path, "*.*")
                    .Where(f => IsValidNoteFile(f));
                
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    try
                    {
                        var noteNode = TreeNode.CreateNote(file, rootPath, dirNode);
                        nodes.Add(noteNode);
                        
                        progress.CurrentItem = Path.GetFileName(file);
                        progressReporter?.Report(progress);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to process file {file}: {ex.Message}");
                    }
                }
                
                // Recursively scan subdirectories
                var subdirs = Directory.GetDirectories(path)
                    .Where(d => !Path.GetFileName(d).StartsWith(".")); // Skip hidden directories
                
                foreach (var subdir in subdirs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ScanDirectoryRecursive(subdir, dirNode, nodes, rootPath, progress, progressReporter, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error scanning directory {path}: {ex.Message}");
            }
        }

        // =============================================================================
        // HEALTH AND MAINTENANCE
        // =============================================================================
        
        public async Task<DatabaseHealth> CheckHealthAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var health = new DatabaseHealth
                {
                    CheckedAt = DateTime.UtcNow
                };
                
                // Check integrity
                var integrityResult = await connection.ExecuteScalarAsync<string>("PRAGMA integrity_check");
                health.IsCorrupted = integrityResult != "ok";
                health.IntegrityMessage = integrityResult;
                
                // Get statistics from health_metrics view
                var metrics = await QueryAsync<HealthMetric>(connection, "SELECT * FROM health_metrics");
                
                foreach (var metric in metrics)
                {
                    switch (metric.Metric)
                    {
                        case "total_nodes":
                            health.TotalNodes = (int)metric.Value;
                            break;
                        case "deleted_nodes":
                            health.DeletedNodes = (int)metric.Value;
                            break;
                        case "orphaned_nodes":
                            health.OrphanedNodes = (int)metric.Value;
                            break;
                        case "database_size":
                            health.DatabaseSizeBytes = metric.Value;
                            break;
                    }
                }
                
                // Check WAL file size
                var dbPath = new SqliteConnectionStringBuilder(_connectionString).DataSource;
                var walPath = dbPath + "-wal";
                if (File.Exists(walPath))
                {
                    health.WalSizeBytes = new FileInfo(walPath).Length;
                }
                
                // Determine overall health status
                health.Status = health.IsCorrupted ? DatabaseHealthStatus.Corrupted :
                               health.OrphanedNodes > 0 ? DatabaseHealthStatus.Warning :
                               DatabaseHealthStatus.Healthy;
                
                return health;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Health check failed");
                return new DatabaseHealth
                {
                    CheckedAt = DateTime.UtcNow,
                    Status = DatabaseHealthStatus.Error,
                    IntegrityMessage = ex.Message
                };
            }
        }
        
        public async Task<bool> OptimizeAsync()
        {
            try
            {
                _logger.Info("Starting database optimization...");
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                // Update statistics for query optimizer
                await connection.ExecuteAsync("ANALYZE");
                
                // Clean up deleted nodes older than 30 days
                var purgedCount = await connection.ExecuteAsync(@"
                    DELETE FROM tree_nodes 
                    WHERE is_deleted = 1 
                    AND deleted_at < strftime('%s', 'now', '-30 days')");
                
                if (purgedCount > 0)
                {
                    _logger.Info($"Purged {purgedCount} old deleted nodes");
                }
                
                // Clean old audit logs (keep 90 days)
                var auditPurgedCount = await connection.ExecuteAsync(@"
                    DELETE FROM audit_log 
                    WHERE changed_at < strftime('%s', 'now', '-90 days')");
                
                if (auditPurgedCount > 0)
                {
                    _logger.Info($"Purged {auditPurgedCount} old audit log entries");
                }
                
                _logger.Info("Database optimization completed");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Database optimization failed");
                return false;
            }
        }

        // =============================================================================
        // HELPER METHODS
        // =============================================================================
        
        private bool IsValidNoteFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension == ".rtf" || extension == ".txt" || extension == ".md" || extension == ".markdown";
        }
        
        private object MapToParameters(TreeNode node)
        {
            var nodeTypeString = node.NodeType.ToString().ToLowerInvariant();
            _logger.Debug($"[DIAGNOSTIC] Mapping node {node.Id} with NodeType='{nodeTypeString}' (from enum {node.NodeType})");
            
            return new
            {
                Id = node.Id.ToString(),
                ParentId = node.ParentId?.ToString(),
                CanonicalPath = node.CanonicalPath,
                DisplayPath = node.DisplayPath,
                AbsolutePath = node.AbsolutePath,
                NodeType = nodeTypeString,
                Name = node.Name,
                FileExtension = node.FileExtension,
                FileSize = node.FileSize,
                CreatedAt = ((DateTimeOffset)node.CreatedAt).ToUnixTimeSeconds(),
                ModifiedAt = ((DateTimeOffset)node.ModifiedAt).ToUnixTimeSeconds(),
                AccessedAt = node.AccessedAt.HasValue ? ((DateTimeOffset)node.AccessedAt.Value).ToUnixTimeSeconds() : (long?)null,
                QuickHash = node.QuickHash,
                FullHash = node.FullHash,
                HashAlgorithm = node.HashAlgorithm,
                HashCalculatedAt = node.HashCalculatedAt.HasValue ? ((DateTimeOffset)node.HashCalculatedAt.Value).ToUnixTimeSeconds() : (long?)null,
                IsExpanded = node.IsExpanded ? 1 : 0,
                IsPinned = node.IsPinned ? 1 : 0,
                IsSelected = node.IsSelected ? 1 : 0,
                SortOrder = node.SortOrder,
                ColorTag = node.ColorTag,
                IconOverride = node.IconOverride,
                IsDeleted = node.IsDeleted ? 1 : 0,
                DeletedAt = node.DeletedAt.HasValue ? ((DateTimeOffset)node.DeletedAt.Value).ToUnixTimeSeconds() : (long?)null,
                MetadataVersion = node.MetadataVersion,
                CustomProperties = node.CustomProperties
            };
        }
        
        private object MapToParametersForInsert(TreeNode node)
        {
            // Simplified parameters for bulk insert (only essential fields)
            var nodeTypeString = node.NodeType.ToString().ToLowerInvariant();
            _logger.Debug($"[DIAGNOSTIC] Mapping for insert node {node.Id} with NodeType='{nodeTypeString}' (from enum {node.NodeType})");
            
            return new
            {
                Id = node.Id.ToString(),
                ParentId = node.ParentId?.ToString(),
                CanonicalPath = node.CanonicalPath,
                DisplayPath = node.DisplayPath,
                AbsolutePath = node.AbsolutePath,
                NodeType = nodeTypeString,  // FIXED: Now using lowercase like MapToParameters
                Name = node.Name,
                FileExtension = node.FileExtension,
                FileSize = node.FileSize,
                CreatedAt = ((DateTimeOffset)node.CreatedAt).ToUnixTimeSeconds(),
                ModifiedAt = ((DateTimeOffset)node.ModifiedAt).ToUnixTimeSeconds(),
                AccessedAt = node.AccessedAt.HasValue ? ((DateTimeOffset)node.AccessedAt.Value).ToUnixTimeSeconds() : (long?)null,
                QuickHash = node.QuickHash,
                IsExpanded = node.IsExpanded ? 1 : 0,
                IsPinned = node.IsPinned ? 1 : 0,
                SortOrder = node.SortOrder
            };
        }
        
        // Database query methods using Dapper
        private async Task<T> QuerySingleAsync<T>(SqliteConnection connection, string sql, object parameters = null)
        {
            return await connection.QuerySingleOrDefaultAsync<T>(sql, parameters);
        }
        
        private async Task<List<T>> QueryAsync<T>(SqliteConnection connection, string sql, object parameters = null)
        {
            var results = await connection.QueryAsync<T>(sql, parameters);
            return results.ToList();
        }

        // =============================================================================
        // COMPLETE REPOSITORY IMPLEMENTATION
        // =============================================================================
        
        public async Task<bool> DeleteNodeAsync(Guid id, bool softDelete = true)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                if (softDelete)
                {
                    var sql = @"
                        UPDATE tree_nodes 
                        SET is_deleted = 1, deleted_at = @DeletedAt 
                        WHERE id = @Id";
                    
                    var rowsAffected = await connection.ExecuteAsync(sql, new 
                    { 
                        Id = id.ToString(), 
                        DeletedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() 
                    });
                    
                    return rowsAffected > 0;
                }
                else
                {
                    var sql = "DELETE FROM tree_nodes WHERE id = @Id";
                    var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id.ToString() });
                    return rowsAffected > 0;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to delete node: {id}");
                return false;
            }
        }
        
        public async Task<List<TreeNode>> GetAllNodesAsync(bool includeDeleted = false)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = includeDeleted 
                    ? @"SELECT 
                        id as Id,
                        name as Name,
                        node_type as NodeType,
                        parent_id as ParentId,
                        canonical_path as CanonicalPath,
                        display_path as DisplayPath,
                        absolute_path as AbsolutePath,
                        file_extension as FileExtension,
                        file_size as FileSize,
                        created_at as CreatedAt,
                        modified_at as ModifiedAt,
                        accessed_at as AccessedAt,
                        quick_hash as QuickHash,
                        full_hash as FullHash,
                        hash_algorithm as HashAlgorithm,
                        hash_calculated_at as HashCalculatedAt,
                        is_expanded as IsExpanded,
                        is_pinned as IsPinned,
                        is_selected as IsSelected,
                        sort_order as SortOrder,
                        color_tag as ColorTag,
                        icon_override as IconOverride,
                        is_deleted as IsDeleted,
                        deleted_at as DeletedAt,
                        metadata_version as MetadataVersion,
                        custom_properties as CustomProperties
                    FROM tree_nodes ORDER BY canonical_path"
                    : @"SELECT 
                        id as Id,
                        name as Name,
                        node_type as NodeType,
                        parent_id as ParentId,
                        canonical_path as CanonicalPath,
                        display_path as DisplayPath,
                        absolute_path as AbsolutePath,
                        file_extension as FileExtension,
                        file_size as FileSize,
                        created_at as CreatedAt,
                        modified_at as ModifiedAt,
                        accessed_at as AccessedAt,
                        quick_hash as QuickHash,
                        full_hash as FullHash,
                        hash_algorithm as HashAlgorithm,
                        hash_calculated_at as HashCalculatedAt,
                        is_expanded as IsExpanded,
                        is_pinned as IsPinned,
                        is_selected as IsSelected,
                        sort_order as SortOrder,
                        color_tag as ColorTag,
                        icon_override as IconOverride,
                        is_deleted as IsDeleted,
                        deleted_at as DeletedAt,
                        metadata_version as MetadataVersion,
                        custom_properties as CustomProperties
                    FROM tree_nodes WHERE is_deleted = 0 ORDER BY canonical_path";
                
                var dtos = await QueryAsync<TreeNodeDto>(connection, sql);
                return dtos.Select(dto => dto.ToDomainModel()).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get all nodes");
                return new List<TreeNode>();
            }
        }
        
        public async Task<List<TreeNode>> GetRecentlyModifiedAsync(int count = 50)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "SELECT * FROM recent_items LIMIT @Count";
                var dtos = await QueryAsync<TreeNodeDto>(connection, sql, new { Count = count });
                return dtos.Select(dto => dto.ToDomainModel()).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get recently modified nodes");
                return new List<TreeNode>();
            }
        }
        
        public async Task<int> BulkUpdateNodesAsync(IEnumerable<TreeNode> nodes)
        {
            await _dbLock.WaitAsync();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                using var transaction = await connection.BeginTransactionAsync();
                
                try
                {
                    var sql = @"
                        UPDATE tree_nodes SET
                            parent_id = @ParentId, canonical_path = @CanonicalPath, display_path = @DisplayPath,
                            absolute_path = @AbsolutePath, name = @Name, file_extension = @FileExtension,
                            file_size = @FileSize, modified_at = @ModifiedAt, quick_hash = @QuickHash,
                            is_expanded = @IsExpanded, is_pinned = @IsPinned, sort_order = @SortOrder
                        WHERE id = @Id";
                    
                    var count = 0;
                    foreach (var node in nodes)
                    {
                        var parameters = MapToParameters(node);
                        await connection.ExecuteAsync(sql, parameters, transaction);
                        count++;
                    }
                    
                    await transaction.CommitAsync();
                    return count;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            finally
            {
                _dbLock.Release();
            }
        }
        
        public async Task<bool> MoveNodeAsync(Guid nodeId, Guid newParentId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    UPDATE tree_nodes 
                    SET parent_id = @NewParentId, modified_at = @ModifiedAt 
                    WHERE id = @NodeId";
                
                var rowsAffected = await connection.ExecuteAsync(sql, new 
                { 
                    NodeId = nodeId.ToString(), 
                    NewParentId = newParentId.ToString(),
                    ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
                
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to move node {nodeId} to parent {newParentId}");
                return false;
            }
        }
        
        public async Task<bool> RenameNodeAsync(Guid nodeId, string newName)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    UPDATE tree_nodes 
                    SET name = @NewName, modified_at = @ModifiedAt 
                    WHERE id = @NodeId";
                
                var rowsAffected = await connection.ExecuteAsync(sql, new 
                { 
                    NodeId = nodeId.ToString(), 
                    NewName = newName,
                    ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
                
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to rename node {nodeId} to {newName}");
                return false;
            }
        }
        
        public async Task<List<TreeNode>> GetNodeDescendantsAsync(Guid nodeId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    WITH RECURSIVE descendants AS (
                        SELECT 
                            id, name, node_type, parent_id, canonical_path, display_path, absolute_path,
                            file_extension, file_size, created_at, modified_at, accessed_at,
                            quick_hash, full_hash, hash_algorithm, hash_calculated_at,
                            is_expanded, is_pinned, is_selected, sort_order, color_tag, icon_override,
                            is_deleted, deleted_at, metadata_version, custom_properties
                        FROM tree_nodes WHERE parent_id = @NodeId AND is_deleted = 0
                        UNION ALL
                        SELECT 
                            t.id, t.name, t.node_type, t.parent_id, t.canonical_path, t.display_path, t.absolute_path,
                            t.file_extension, t.file_size, t.created_at, t.modified_at, t.accessed_at,
                            t.quick_hash, t.full_hash, t.hash_algorithm, t.hash_calculated_at,
                            t.is_expanded, t.is_pinned, t.is_selected, t.sort_order, t.color_tag, t.icon_override,
                            t.is_deleted, t.deleted_at, t.metadata_version, t.custom_properties
                        FROM tree_nodes t
                        INNER JOIN descendants d ON t.parent_id = d.id
                        WHERE t.is_deleted = 0
                    )
                    SELECT 
                        id as Id,
                        name as Name,
                        node_type as NodeType,
                        parent_id as ParentId,
                        canonical_path as CanonicalPath,
                        display_path as DisplayPath,
                        absolute_path as AbsolutePath,
                        file_extension as FileExtension,
                        file_size as FileSize,
                        created_at as CreatedAt,
                        modified_at as ModifiedAt,
                        accessed_at as AccessedAt,
                        quick_hash as QuickHash,
                        full_hash as FullHash,
                        hash_algorithm as HashAlgorithm,
                        hash_calculated_at as HashCalculatedAt,
                        is_expanded as IsExpanded,
                        is_pinned as IsPinned,
                        is_selected as IsSelected,
                        sort_order as SortOrder,
                        color_tag as ColorTag,
                        icon_override as IconOverride,
                        is_deleted as IsDeleted,
                        deleted_at as DeletedAt,
                        metadata_version as MetadataVersion,
                        custom_properties as CustomProperties
                    FROM descendants ORDER BY canonical_path";
                
                var dtos = await QueryAsync<TreeNodeDto>(connection, sql, new { NodeId = nodeId.ToString() });
                return dtos.Select(dto => dto.ToDomainModel()).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get descendants for node: {nodeId}");
                return new List<TreeNode>();
            }
        }
        
        public async Task<TreeNode> GetNodeAncestorAsync(Guid nodeId, TreeNodeType ancestorType)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    WITH RECURSIVE ancestors AS (
                        SELECT 
                            id, name, node_type, parent_id, canonical_path, display_path, absolute_path,
                            file_extension, file_size, created_at, modified_at, accessed_at,
                            quick_hash, full_hash, hash_algorithm, hash_calculated_at,
                            is_expanded, is_pinned, is_selected, sort_order, color_tag, icon_override,
                            is_deleted, deleted_at, metadata_version, custom_properties
                        FROM tree_nodes WHERE id = @NodeId
                        UNION ALL
                        SELECT 
                            t.id, t.name, t.node_type, t.parent_id, t.canonical_path, t.display_path, t.absolute_path,
                            t.file_extension, t.file_size, t.created_at, t.modified_at, t.accessed_at,
                            t.quick_hash, t.full_hash, t.hash_algorithm, t.hash_calculated_at,
                            t.is_expanded, t.is_pinned, t.is_selected, t.sort_order, t.color_tag, t.icon_override,
                            t.is_deleted, t.deleted_at, t.metadata_version, t.custom_properties
                        FROM tree_nodes t
                        INNER JOIN ancestors a ON t.id = a.parent_id
                        WHERE t.is_deleted = 0
                    )
                    SELECT 
                        id as Id,
                        name as Name,
                        node_type as NodeType,
                        parent_id as ParentId,
                        canonical_path as CanonicalPath,
                        display_path as DisplayPath,
                        absolute_path as AbsolutePath,
                        file_extension as FileExtension,
                        file_size as FileSize,
                        created_at as CreatedAt,
                        modified_at as ModifiedAt,
                        accessed_at as AccessedAt,
                        quick_hash as QuickHash,
                        full_hash as FullHash,
                        hash_algorithm as HashAlgorithm,
                        hash_calculated_at as HashCalculatedAt,
                        is_expanded as IsExpanded,
                        is_pinned as IsPinned,
                        is_selected as IsSelected,
                        sort_order as SortOrder,
                        color_tag as ColorTag,
                        icon_override as IconOverride,
                        is_deleted as IsDeleted,
                        deleted_at as DeletedAt,
                        metadata_version as MetadataVersion,
                        custom_properties as CustomProperties
                    FROM ancestors 
                    WHERE node_type = @AncestorType AND id != @NodeId
                    ORDER BY canonical_path
                    LIMIT 1";
                
                var dto = await QuerySingleAsync<TreeNodeDto>(connection, sql, new 
                { 
                    NodeId = nodeId.ToString(),
                    AncestorType = ancestorType.ToString()
                });
                
                return dto?.ToDomainModel();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get ancestor for node: {nodeId}");
                return null;
            }
        }
        
        public async Task<bool> PurgeDeletedNodesAsync(int daysOld = 30)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var cutoffTime = DateTimeOffset.UtcNow.AddDays(-daysOld).ToUnixTimeSeconds();
                var sql = @"
                    DELETE FROM tree_nodes 
                    WHERE is_deleted = 1 AND deleted_at < @CutoffTime";
                
                var rowsAffected = await connection.ExecuteAsync(sql, new { CutoffTime = cutoffTime });
                
                _logger.Info($"Purged {rowsAffected} old deleted nodes (older than {daysOld} days)");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to purge deleted nodes");
                return false;
            }
        }
        
        public async Task<bool> VacuumAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                await connection.ExecuteAsync("VACUUM");
                _logger.Info("Database vacuum completed");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Database vacuum failed");
                return false;
            }
        }
        
        public async Task<List<TreeNode>> GetNodesWithOutdatedHashAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    SELECT 
                        id as Id,
                        name as Name,
                        node_type as NodeType,
                        parent_id as ParentId,
                        canonical_path as CanonicalPath,
                        display_path as DisplayPath,
                        absolute_path as AbsolutePath,
                        file_extension as FileExtension,
                        file_size as FileSize,
                        created_at as CreatedAt,
                        modified_at as ModifiedAt,
                        accessed_at as AccessedAt,
                        quick_hash as QuickHash,
                        full_hash as FullHash,
                        hash_algorithm as HashAlgorithm,
                        hash_calculated_at as HashCalculatedAt,
                        is_expanded as IsExpanded,
                        is_pinned as IsPinned,
                        is_selected as IsSelected,
                        sort_order as SortOrder,
                        color_tag as ColorTag,
                        icon_override as IconOverride,
                        is_deleted as IsDeleted,
                        deleted_at as DeletedAt,
                        metadata_version as MetadataVersion,
                        custom_properties as CustomProperties
                    FROM tree_nodes 
                    WHERE node_type = 'note' 
                    AND is_deleted = 0 
                    AND (quick_hash IS NULL OR hash_calculated_at IS NULL)
                    ORDER BY modified_at DESC";
                
                var dtos = await QueryAsync<TreeNodeDto>(connection, sql);
                return dtos.Select(dto => dto.ToDomainModel()).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get nodes with outdated hash");
                return new List<TreeNode>();
            }
        }
        
        public async Task<bool> UpdateNodeHashAsync(Guid nodeId, string quickHash, string fullHash = null)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    UPDATE tree_nodes 
                    SET quick_hash = @QuickHash, full_hash = @FullHash, 
                        hash_calculated_at = @CalculatedAt, modified_at = @ModifiedAt
                    WHERE id = @NodeId";
                
                var rowsAffected = await connection.ExecuteAsync(sql, new 
                { 
                    NodeId = nodeId.ToString(),
                    QuickHash = quickHash,
                    FullHash = fullHash,
                    CalculatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
                
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to update hash for node: {nodeId}");
                return false;
            }
        }
        
        public async Task<int> RefreshAllNodeMetadataAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                // Get all note nodes that exist on disk
                var sql = @"
                    SELECT 
                        id as Id,
                        name as Name,
                        node_type as NodeType,
                        parent_id as ParentId,
                        canonical_path as CanonicalPath,
                        display_path as DisplayPath,
                        absolute_path as AbsolutePath,
                        file_extension as FileExtension,
                        file_size as FileSize,
                        created_at as CreatedAt,
                        modified_at as ModifiedAt,
                        accessed_at as AccessedAt,
                        quick_hash as QuickHash,
                        full_hash as FullHash,
                        hash_algorithm as HashAlgorithm,
                        hash_calculated_at as HashCalculatedAt,
                        is_expanded as IsExpanded,
                        is_pinned as IsPinned,
                        is_selected as IsSelected,
                        sort_order as SortOrder,
                        color_tag as ColorTag,
                        icon_override as IconOverride,
                        is_deleted as IsDeleted,
                        deleted_at as DeletedAt,
                        metadata_version as MetadataVersion,
                        custom_properties as CustomProperties
                    FROM tree_nodes 
                    WHERE node_type = 'note' AND is_deleted = 0
                    ORDER BY canonical_path";
                
                var dtos = await QueryAsync<TreeNodeDto>(connection, sql);
                var refreshedCount = 0;
                
                foreach (var dto in dtos)
                {
                    try
                    {
                        if (File.Exists(dto.AbsolutePath))
                        {
                            var fileInfo = new FileInfo(dto.AbsolutePath);
                            
                            var updateSql = @"
                                UPDATE tree_nodes 
                                SET file_size = @FileSize, modified_at = @ModifiedAt
                                WHERE id = @Id";
                            
                            await connection.ExecuteAsync(updateSql, new
                            {
                                Id = dto.Id,
                                FileSize = fileInfo.Length,
                                ModifiedAt = ((DateTimeOffset)fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds()
                            });
                            
                            refreshedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to refresh metadata for node {dto.Id}: {ex.Message}");
                    }
                }
                
                _logger.Info($"Refreshed metadata for {refreshedCount} nodes");
                return refreshedCount;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to refresh all node metadata");
                return 0;
            }
        }

        // =============================================================================
        // SEARCH OPERATIONS - Lightning-fast database search
        // =============================================================================

        public async Task<List<TreeNode>> SearchNotesByTitleAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return new List<TreeNode>();

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    SELECT 
                        id as Id,
                        name as Name,
                        node_type as NodeType,
                        parent_id as ParentId,
                        canonical_path as CanonicalPath,
                        display_path as DisplayPath,
                        absolute_path as AbsolutePath,
                        file_extension as FileExtension,
                        file_size as FileSize,
                        created_at as CreatedAt,
                        modified_at as ModifiedAt,
                        accessed_at as AccessedAt,
                        quick_hash as QuickHash,
                        full_hash as FullHash,
                        hash_algorithm as HashAlgorithm,
                        hash_calculated_at as HashCalculatedAt,
                        is_expanded as IsExpanded,
                        is_pinned as IsPinned,
                        is_selected as IsSelected,
                        sort_order as SortOrder,
                        color_tag as ColorTag,
                        icon_override as IconOverride,
                        is_deleted as IsDeleted,
                        deleted_at as DeletedAt,
                        metadata_version as MetadataVersion,
                        custom_properties as CustomProperties
                    FROM tree_nodes 
                    WHERE node_type = 'Note' 
                    AND is_deleted = 0
                    AND name LIKE @SearchTerm 
                    ORDER BY name";
                
                var searchPattern = $"%{searchTerm}%";
                var dtos = await QueryAsync<TreeNodeDto>(connection, sql, new { SearchTerm = searchPattern });
                
                _logger.Info($"🔍 Title search for '{searchTerm}' found {dtos.Count} matches");
                return dtos.Select(dto => dto.ToDomainModel()).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to search notes by title: {searchTerm}");
                return new List<TreeNode>();
            }
        }

        public async Task<List<TreeNode>> SearchNotesByContentAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return new List<TreeNode>();

            try
            {
                _logger.Info($"🔍 Starting content search for: '{searchTerm}'");
                
                // Get all note nodes from database (fast)
                var allNotes = await GetAllNodesAsync();
                var noteNodes = allNotes.Where(n => n.NodeType == TreeNodeType.Note).ToList();
                
                var matchingNotes = new List<TreeNode>();
                
                // Search through file content (slower but thorough)
                foreach (var note in noteNodes)
                {
                    try
                    {
                        if (File.Exists(note.AbsolutePath))
                        {
                            var content = await File.ReadAllTextAsync(note.AbsolutePath);
                            if (content.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                            {
                                matchingNotes.Add(note);
                            }
                        }
                    }
                    catch (Exception fileEx)
                    {
                        _logger.Warning($"Failed to read file for search: {note.AbsolutePath} - {fileEx.Message}");
                    }
                }
                
                _logger.Info($"🔍 Content search for '{searchTerm}' found {matchingNotes.Count} matches in {noteNodes.Count} notes");
                return matchingNotes;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to search notes by content: {searchTerm}");
                return new List<TreeNode>();
            }
        }
    }
    
    // =============================================================================
    // SUPPORTING TYPES
    // =============================================================================
    
    public class RebuildProgress
    {
        public string Phase { get; set; } = "Starting...";
        public string CurrentItem { get; set; } = "";
        public int ProcessedItems { get; set; } = 0;
        public int TotalItems { get; set; } = 0;
        public bool IsComplete { get; set; } = false;
        public string Message { get; set; } = "";
        
        public RebuildProgress UpdatePhase(string phase)
        {
            Phase = phase;
            return this;
        }
        
        public RebuildProgress Complete(string message)
        {
            IsComplete = true;
            Message = message;
            Phase = "Complete";
            return this;
        }
    }
    
    public class DatabaseHealth
    {
        public DateTime CheckedAt { get; set; }
        public DatabaseHealthStatus Status { get; set; }
        public bool IsCorrupted { get; set; }
        public string IntegrityMessage { get; set; }
        public int TotalNodes { get; set; }
        public int DeletedNodes { get; set; }
        public int OrphanedNodes { get; set; }
        public long DatabaseSizeBytes { get; set; }
        public long WalSizeBytes { get; set; }
        
        public double DatabaseSizeMB => DatabaseSizeBytes / (1024.0 * 1024.0);
        public double WalSizeMB => WalSizeBytes / (1024.0 * 1024.0);
    }
    
    public enum DatabaseHealthStatus
    {
        Healthy,
        Warning,
        Corrupted,
        Error
    }
    
    public class HealthMetric
    {
        public string Metric { get; set; }
        public long Value { get; set; }
        public string Unit { get; set; }
    }
    
}
