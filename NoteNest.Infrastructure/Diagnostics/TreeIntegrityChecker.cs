using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Infrastructure.Diagnostics
{
    /// <summary>
    /// Diagnostic tool to detect data integrity issues in tree_view projection.
    /// Specifically checks for circular references that could cause infinite loops.
    /// </summary>
    public class TreeIntegrityChecker
    {
        private readonly string _connectionString;
        private readonly IAppLogger _logger;

        public TreeIntegrityChecker(string projectionsConnectionString, IAppLogger logger)
        {
            _connectionString = projectionsConnectionString ?? throw new ArgumentNullException(nameof(projectionsConnectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Performs comprehensive integrity check on tree_view.
        /// Returns list of issues found.
        /// </summary>
        public async Task<TreeIntegrityReport> CheckIntegrityAsync()
        {
            var report = new TreeIntegrityReport();
            
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Check 1: Self-referencing nodes (node is its own parent)
                report.SelfReferencingNodes = await CheckSelfReferencingNodesAsync(connection);
                
                // Check 2: Circular reference chains (A -> B -> C -> A)
                report.CircularReferenceChains = await CheckCircularReferencesAsync(connection);
                
                // Check 3: Orphaned nodes (parent_id points to non-existent node)
                report.OrphanedNodes = await CheckOrphanedNodesAsync(connection);
                
                // Check 4: Excessive depth (legitimate deep trees that might be slow)
                report.ExcessivelyDeepNodes = await CheckExcessiveDepthAsync(connection);
                
                // Check 5: Missing root nodes
                report.HasRootNodes = await CheckRootNodesExistAsync(connection);
                
                report.IsHealthy = report.SelfReferencingNodes.Count == 0 &&
                                  report.CircularReferenceChains.Count == 0 &&
                                  report.OrphanedNodes.Count == 0 &&
                                  report.HasRootNodes;
                
                _logger.Info($"[TreeIntegrityChecker] Integrity check complete - Healthy: {report.IsHealthy}");
                return report;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TreeIntegrityChecker] Failed to run integrity check");
                report.CheckFailed = true;
                report.ErrorMessage = ex.Message;
                return report;
            }
        }

        /// <summary>
        /// Check for nodes where id = parent_id (most obvious circular reference).
        /// </summary>
        private async Task<List<TreeIntegrityIssue>> CheckSelfReferencingNodesAsync(SqliteConnection connection)
        {
            var sql = @"
                SELECT id, name, node_type, display_path, parent_id
                FROM tree_view
                WHERE id = parent_id";
            
            var results = await connection.QueryAsync<TreeNodeInfo>(sql);
            var issues = results.Select(r => new TreeIntegrityIssue
            {
                NodeId = r.Id,
                NodeName = r.Name,
                NodeType = r.NodeType,
                DisplayPath = r.DisplayPath,
                IssueType = "Self-Referencing",
                Description = $"Node '{r.Name}' is its own parent (id = parent_id)",
                Severity = "CRITICAL"
            }).ToList();
            
            if (issues.Any())
            {
                _logger.Error($"[TreeIntegrityChecker] Found {issues.Count} self-referencing nodes!");
                foreach (var issue in issues)
                {
                    _logger.Error($"  - {issue.NodeId}: {issue.NodeName} ({issue.DisplayPath})");
                }
            }
            
            return issues;
        }

        /// <summary>
        /// Check for circular reference chains using iterative traversal with cycle detection.
        /// This simulates what GetAncestorCategoryTagsAsync does but reports all cycles.
        /// </summary>
        private async Task<List<TreeIntegrityIssue>> CheckCircularReferencesAsync(SqliteConnection connection)
        {
            var issues = new List<TreeIntegrityIssue>();
            
            // Get all categories (notes can't be parents, so only check categories)
            var categories = await connection.QueryAsync<TreeNodeInfo>(
                "SELECT id, name, node_type, display_path, parent_id FROM tree_view WHERE node_type = 'category'");
            
            foreach (var category in categories)
            {
                var visited = new HashSet<string>();
                var path = new List<string>();
                var currentId = category.Id;
                var currentName = category.Name;
                int depth = 0;
                const int MAX_DEPTH = 50; // Set higher than normal to detect deep cycles
                
                // Walk up the tree
                while (!string.IsNullOrEmpty(currentId) && depth < MAX_DEPTH)
                {
                    // Check for cycle
                    if (visited.Contains(currentId))
                    {
                        // Found a cycle!
                        var cycleStartIndex = path.IndexOf(currentId);
                        var cyclePath = string.Join(" -> ", path.Skip(cycleStartIndex).Concat(new[] { currentId }));
                        
                        issues.Add(new TreeIntegrityIssue
                        {
                            NodeId = category.Id,
                            NodeName = category.Name,
                            NodeType = category.NodeType,
                            DisplayPath = category.DisplayPath,
                            IssueType = "Circular Reference",
                            Description = $"Circular reference detected in ancestry: {cyclePath}",
                            Severity = "CRITICAL",
                            CyclePath = cyclePath
                        });
                        
                        _logger.Error($"[TreeIntegrityChecker] Circular reference: {category.Name} -> {cyclePath}");
                        break;
                    }
                    
                    visited.Add(currentId);
                    path.Add($"{currentName}({currentId.Substring(0, 8)})");
                    
                    // Get parent
                    var parent = await connection.QueryFirstOrDefaultAsync<TreeNodeInfo>(
                        "SELECT id, name, node_type, parent_id FROM tree_view WHERE id = @Id",
                        new { Id = currentId });
                    
                    if (parent == null || string.IsNullOrEmpty(parent.ParentId))
                    {
                        // Reached root, no cycle
                        break;
                    }
                    
                    currentId = parent.ParentId;
                    currentName = parent.Name;
                    depth++;
                }
                
                if (depth >= MAX_DEPTH)
                {
                    issues.Add(new TreeIntegrityIssue
                    {
                        NodeId = category.Id,
                        NodeName = category.Name,
                        NodeType = category.NodeType,
                        DisplayPath = category.DisplayPath,
                        IssueType = "Possible Infinite Loop",
                        Description = $"Reached maximum depth ({MAX_DEPTH}) without finding root - possible circular reference",
                        Severity = "CRITICAL"
                    });
                }
            }
            
            return issues;
        }

        /// <summary>
        /// Check for nodes whose parent_id points to a non-existent node.
        /// </summary>
        private async Task<List<TreeIntegrityIssue>> CheckOrphanedNodesAsync(SqliteConnection connection)
        {
            var sql = @"
                SELECT t1.id, t1.name, t1.node_type, t1.display_path, t1.parent_id
                FROM tree_view t1
                WHERE t1.parent_id IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM tree_view t2 WHERE t2.id = t1.parent_id)";
            
            var results = await connection.QueryAsync<TreeNodeInfo>(sql);
            var issues = results.Select(r => new TreeIntegrityIssue
            {
                NodeId = r.Id,
                NodeName = r.Name,
                NodeType = r.NodeType,
                DisplayPath = r.DisplayPath,
                IssueType = "Orphaned Node",
                Description = $"Node '{r.Name}' has parent_id '{r.ParentId}' which doesn't exist",
                Severity = "HIGH"
            }).ToList();
            
            if (issues.Any())
            {
                _logger.Warning($"[TreeIntegrityChecker] Found {issues.Count} orphaned nodes");
            }
            
            return issues;
        }

        /// <summary>
        /// Check for nodes at excessive depth (> 15 levels) that might cause performance issues.
        /// </summary>
        private async Task<List<TreeIntegrityIssue>> CheckExcessiveDepthAsync(SqliteConnection connection)
        {
            var sql = @"
                WITH RECURSIVE tree_depth AS (
                    SELECT id, name, node_type, display_path, parent_id, 0 as depth
                    FROM tree_view
                    WHERE parent_id IS NULL
                    
                    UNION ALL
                    
                    SELECT t.id, t.name, t.node_type, t.display_path, t.parent_id, td.depth + 1
                    FROM tree_view t
                    INNER JOIN tree_depth td ON t.parent_id = td.id
                    WHERE td.depth < 50  -- Safety limit to prevent infinite recursion
                )
                SELECT id, name, node_type, display_path, parent_id, depth
                FROM tree_depth
                WHERE depth > 15
                ORDER BY depth DESC";
            
            try
            {
                var results = await connection.QueryAsync<TreeNodeDepthInfo>(sql);
                var issues = results.Select(r => new TreeIntegrityIssue
                {
                    NodeId = r.Id,
                    NodeName = r.Name,
                    NodeType = r.NodeType,
                    DisplayPath = r.DisplayPath,
                    IssueType = "Excessive Depth",
                    Description = $"Node '{r.Name}' is at depth {r.Depth} (may be slow)",
                    Severity = "WARNING",
                    Depth = r.Depth
                }).ToList();
                
                if (issues.Any())
                {
                    _logger.Warning($"[TreeIntegrityChecker] Found {issues.Count} nodes with excessive depth (> 15 levels)");
                }
                
                return issues;
            }
            catch (Exception ex)
            {
                // If the recursive CTE fails, it might be due to a circular reference
                _logger.Error(ex, "[TreeIntegrityChecker] Recursive CTE failed - possible circular reference");
                return new List<TreeIntegrityIssue>
                {
                    new TreeIntegrityIssue
                    {
                        IssueType = "CTE Failure",
                        Description = "Recursive CTE failed to calculate depths - possible circular reference",
                        Severity = "CRITICAL"
                    }
                };
            }
        }

        /// <summary>
        /// Check that at least one root node exists.
        /// </summary>
        private async Task<bool> CheckRootNodesExistAsync(SqliteConnection connection)
        {
            var count = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM tree_view WHERE parent_id IS NULL");
            
            if (count == 0)
            {
                _logger.Error("[TreeIntegrityChecker] No root nodes found in tree_view!");
            }
            
            return count > 0;
        }
    }

    #region DTOs

    public class TreeIntegrityReport
    {
        public bool IsHealthy { get; set; }
        public bool CheckFailed { get; set; }
        public string ErrorMessage { get; set; }
        public bool HasRootNodes { get; set; }
        public List<TreeIntegrityIssue> SelfReferencingNodes { get; set; } = new();
        public List<TreeIntegrityIssue> CircularReferenceChains { get; set; } = new();
        public List<TreeIntegrityIssue> OrphanedNodes { get; set; } = new();
        public List<TreeIntegrityIssue> ExcessivelyDeepNodes { get; set; } = new();
        
        public int TotalIssueCount => 
            SelfReferencingNodes.Count + 
            CircularReferenceChains.Count + 
            OrphanedNodes.Count + 
            ExcessivelyDeepNodes.Count +
            (HasRootNodes ? 0 : 1);
    }

    public class TreeIntegrityIssue
    {
        public string NodeId { get; set; }
        public string NodeName { get; set; }
        public string NodeType { get; set; }
        public string DisplayPath { get; set; }
        public string IssueType { get; set; }
        public string Description { get; set; }
        public string Severity { get; set; } // CRITICAL, HIGH, WARNING
        public string CyclePath { get; set; }
        public int? Depth { get; set; }
    }

    public class TreeNodeInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string NodeType { get; set; }
        public string DisplayPath { get; set; }
        public string ParentId { get; set; }
    }

    public class TreeNodeDepthInfo : TreeNodeInfo
    {
        public int Depth { get; set; }
    }

    #endregion
}

