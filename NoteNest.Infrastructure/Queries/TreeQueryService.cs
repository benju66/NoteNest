using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Queries;
using NoteNest.Domain.Trees;

namespace NoteNest.Infrastructure.Queries
{
    /// <summary>
    /// Query service for tree view projection.
    /// Queries projections.db tree_view table with aggressive caching.
    /// Replaces TreeDatabaseRepository for read operations.
    /// </summary>
    public class TreeQueryService : ITreeQueryService
    {
        private readonly string _connectionString;
        private readonly IMemoryCache _cache;
        private readonly IAppLogger _logger;
        
        private const string ALL_NODES_KEY = "tree_all_nodes";
        private const string ROOT_NODES_KEY = "tree_root_nodes";
        private const string PINNED_NODES_KEY = "tree_pinned_nodes";
        private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(5);

        public TreeQueryService(
            string projectionsConnectionString,
            IMemoryCache cache,
            IAppLogger logger)
        {
            _connectionString = projectionsConnectionString ?? throw new ArgumentNullException(nameof(projectionsConnectionString));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<TreeNode> GetByIdAsync(Guid id)
        {
            try
            {
                // NOTE: Per-node caching removed to prevent stale data issues
                // Single-row indexed queries are fast enough (<1ms) that caching isn't needed
                // This ensures commands always get fresh FilePath and other updated properties
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var node = await connection.QueryFirstOrDefaultAsync<TreeNodeDto>(
                    @"SELECT 
                        id AS Id,
                        parent_id AS ParentId,
                        canonical_path AS CanonicalPath,
                        display_path AS DisplayPath,
                        node_type AS NodeType,
                        name AS Name,
                        file_extension AS FileExtension,
                        is_pinned AS IsPinned,
                        sort_order AS SortOrder,
                        created_at AS CreatedAt,
                        modified_at AS ModifiedAt
                      FROM tree_view 
                      WHERE id = @Id",
                    new { Id = id.ToString() });

                if (node == null)
                    return null;

                return MapToTreeNode(node);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get tree node by ID: {id}", ex);
                return null;
            }
        }

        public async Task<List<TreeNode>> GetAllNodesAsync(bool includeDeleted = false)
        {
            try
            {
                // Try cache first
                if (!includeDeleted && _cache.TryGetValue(ALL_NODES_KEY, out List<TreeNode> cached))
                {
                    _logger.Debug("Tree nodes loaded from cache");
                    return cached;
                }

                var startTime = DateTime.Now;

                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"SELECT 
                    id AS Id,
                    parent_id AS ParentId,
                    canonical_path AS CanonicalPath,
                    display_path AS DisplayPath,
                    node_type AS NodeType,
                    name AS Name,
                    file_extension AS FileExtension,
                    is_pinned AS IsPinned,
                    sort_order AS SortOrder,
                    created_at AS CreatedAt,
                    modified_at AS ModifiedAt
                  FROM tree_view 
                  ORDER BY canonical_path";
                var nodes = await connection.QueryAsync<TreeNodeDto>(sql);

                var treeNodes = nodes.Select(MapToTreeNode).Where(n => n != null).ToList();

                // Cache the result
                if (!includeDeleted)
                {
                    _cache.Set(ALL_NODES_KEY, treeNodes, new MemoryCacheEntryOptions
                    {
                        SlidingExpiration = CACHE_DURATION,
                        Priority = CacheItemPriority.High
                    });
                }

                var loadTime = (DateTime.Now - startTime).TotalMilliseconds;
                _logger.Info($"⚡ Loaded {treeNodes.Count} tree nodes from projection in {loadTime}ms");

                return treeNodes;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to get all tree nodes", ex);
                return new List<TreeNode>();
            }
        }

        public async Task<List<TreeNode>> GetChildrenAsync(Guid? parentId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                string sql;
                object param;

                var selectClause = @"SELECT 
                    id AS Id,
                    parent_id AS ParentId,
                    canonical_path AS CanonicalPath,
                    display_path AS DisplayPath,
                    node_type AS NodeType,
                    name AS Name,
                    file_extension AS FileExtension,
                    is_pinned AS IsPinned,
                    sort_order AS SortOrder,
                    created_at AS CreatedAt,
                    modified_at AS ModifiedAt
                  FROM tree_view";

                if (parentId.HasValue)
                {
                    sql = selectClause + " WHERE parent_id = @ParentId ORDER BY sort_order, name";
                    param = new { ParentId = parentId.Value.ToString() };
                }
                else
                {
                    sql = selectClause + " WHERE parent_id IS NULL ORDER BY sort_order, name";
                    param = null;
                }

                var nodes = await connection.QueryAsync<TreeNodeDto>(sql, param);
                return nodes.Select(MapToTreeNode).Where(n => n != null).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get children for parent {parentId}", ex);
                return new List<TreeNode>();
            }
        }

        public async Task<List<TreeNode>> GetRootNodesAsync()
        {
            try
            {
                // Try cache
                if (_cache.TryGetValue(ROOT_NODES_KEY, out List<TreeNode> cached))
                    return cached;

                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var nodes = await connection.QueryAsync<TreeNodeDto>(
                    @"SELECT 
                        id AS Id,
                        parent_id AS ParentId,
                        canonical_path AS CanonicalPath,
                        display_path AS DisplayPath,
                        node_type AS NodeType,
                        name AS Name,
                        file_extension AS FileExtension,
                        is_pinned AS IsPinned,
                        sort_order AS SortOrder,
                        created_at AS CreatedAt,
                        modified_at AS ModifiedAt
                      FROM tree_view 
                      WHERE parent_id IS NULL 
                      ORDER BY sort_order, name");

                var treeNodes = nodes.Select(MapToTreeNode).Where(n => n != null).ToList();

                // Cache
                _cache.Set(ROOT_NODES_KEY, treeNodes, CACHE_DURATION);

                return treeNodes;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to get root nodes", ex);
                return new List<TreeNode>();
            }
        }

        public async Task<List<TreeNode>> GetPinnedAsync()
        {
            try
            {
                // Try cache
                if (_cache.TryGetValue(PINNED_NODES_KEY, out List<TreeNode> cached))
                    return cached;

                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var nodes = await connection.QueryAsync<TreeNodeDto>(
                    @"SELECT 
                        id AS Id,
                        parent_id AS ParentId,
                        canonical_path AS CanonicalPath,
                        display_path AS DisplayPath,
                        node_type AS NodeType,
                        name AS Name,
                        file_extension AS FileExtension,
                        is_pinned AS IsPinned,
                        sort_order AS SortOrder,
                        created_at AS CreatedAt,
                        modified_at AS ModifiedAt
                      FROM tree_view 
                      WHERE is_pinned = 1 
                      ORDER BY sort_order");

                var treeNodes = nodes.Select(MapToTreeNode).Where(n => n != null).ToList();

                // Cache
                _cache.Set(PINNED_NODES_KEY, treeNodes, CACHE_DURATION);

                return treeNodes;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to get pinned nodes", ex);
                return new List<TreeNode>();
            }
        }

        public async Task<TreeNode> GetByPathAsync(string canonicalPath)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var node = await connection.QueryFirstOrDefaultAsync<TreeNodeDto>(
                    @"SELECT 
                        id AS Id,
                        parent_id AS ParentId,
                        canonical_path AS CanonicalPath,
                        display_path AS DisplayPath,
                        node_type AS NodeType,
                        name AS Name,
                        file_extension AS FileExtension,
                        is_pinned AS IsPinned,
                        sort_order AS SortOrder,
                        created_at AS CreatedAt,
                        modified_at AS ModifiedAt
                      FROM tree_view 
                      WHERE canonical_path = @Path",
                    new { Path = canonicalPath.ToLowerInvariant() });

                return node != null ? MapToTreeNode(node) : null;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get node by path: {canonicalPath}", ex);
                return null;
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
                            id, parent_id, canonical_path, display_path, node_type, name,
                            file_extension, is_pinned, sort_order, created_at, modified_at
                        FROM tree_view WHERE parent_id = @NodeId
                        UNION ALL
                        SELECT 
                            t.id, t.parent_id, t.canonical_path, t.display_path, t.node_type, t.name,
                            t.file_extension, t.is_pinned, t.sort_order, t.created_at, t.modified_at
                        FROM tree_view t
                        INNER JOIN descendants d ON t.parent_id = d.id
                    )
                    SELECT * FROM descendants ORDER BY canonical_path";

                var nodes = await connection.QueryAsync<TreeNodeDto>(sql, new { NodeId = nodeId.ToString() });
                return nodes.Select(MapToTreeNode).Where(n => n != null).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get descendants for node: {nodeId}", ex);
                return new List<TreeNode>();
            }
        }

        public void InvalidateCache()
        {
            _cache.Remove(ALL_NODES_KEY);
            _cache.Remove(ROOT_NODES_KEY);
            _cache.Remove(PINNED_NODES_KEY);
            _logger.Debug("Tree cache invalidated");
        }

        private TreeNode MapToTreeNode(TreeNodeDto dto)
        {
            try
            {
                var nodeType = dto.NodeType == "category" ? TreeNodeType.Category : TreeNodeType.Note;
                Guid? parentGuid = string.IsNullOrEmpty(dto.ParentId) ? null : Guid.Parse(dto.ParentId);

                return TreeNode.CreateFromDatabase(
                    id: Guid.Parse(dto.Id),
                    parentId: parentGuid,
                    canonicalPath: dto.CanonicalPath,
                    displayPath: dto.DisplayPath,
                    absolutePath: dto.DisplayPath, // Use displayPath as absolute for now
                    nodeType: nodeType,
                    name: dto.Name,
                    fileExtension: dto.FileExtension,
                    createdAt: DateTimeOffset.FromUnixTimeSeconds(dto.CreatedAt).DateTime,
                    modifiedAt: DateTimeOffset.FromUnixTimeSeconds(dto.ModifiedAt).DateTime,
                    isPinned: dto.IsPinned == 1,
                    sortOrder: dto.SortOrder);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to map TreeNode: {dto?.Name}", ex);
                return null;
            }
        }

        // DTO for Dapper mapping
        private class TreeNodeDto
        {
            public string Id { get; set; }
            public string ParentId { get; set; }
            public string CanonicalPath { get; set; }
            public string DisplayPath { get; set; }
            public string NodeType { get; set; }
            public string Name { get; set; }
            public string FileExtension { get; set; }
            public int IsPinned { get; set; }
            public int SortOrder { get; set; }
            public long CreatedAt { get; set; }
            public long ModifiedAt { get; set; }
        }
    }
}

