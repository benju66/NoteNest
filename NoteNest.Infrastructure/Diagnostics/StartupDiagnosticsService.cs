using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Infrastructure.Diagnostics
{
    /// <summary>
    /// Runs diagnostic checks at application startup to detect data integrity issues early.
    /// Phase 1: Auto-repairs orphaned nodes by promoting them to root level.
    /// </summary>
    public class StartupDiagnosticsService
    {
        private readonly TreeIntegrityChecker _treeIntegrityChecker;
        private readonly string _connectionString;
        private readonly IAppLogger _logger;

        public StartupDiagnosticsService(
            TreeIntegrityChecker treeIntegrityChecker,
            string projectionsConnectionString,
            IAppLogger logger)
        {
            _treeIntegrityChecker = treeIntegrityChecker ?? throw new ArgumentNullException(nameof(treeIntegrityChecker));
            _connectionString = projectionsConnectionString ?? throw new ArgumentNullException(nameof(projectionsConnectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Run all startup diagnostics. Logs warnings but doesn't prevent app startup.
        /// </summary>
        public async Task RunDiagnosticsAsync()
        {
            _logger.Info("🔍 Running startup diagnostics...");
            
            try
            {
                // Check tree integrity
                var treeReport = await _treeIntegrityChecker.CheckIntegrityAsync();
                
                if (treeReport.CheckFailed)
                {
                    _logger.Error($"❌ Tree integrity check failed: {treeReport.ErrorMessage}");
                    return;
                }
                
                if (treeReport.IsHealthy)
                {
                    _logger.Info("✅ Tree integrity check passed - no issues found");
                }
                else
                {
                    _logger.Warning($"⚠️ Tree integrity issues detected: {treeReport.TotalIssueCount} issue(s) found");
                    
                    // Log details of each issue type
                    if (treeReport.SelfReferencingNodes.Count > 0)
                    {
                        _logger.Error($"❌ CRITICAL: Found {treeReport.SelfReferencingNodes.Count} self-referencing nodes:");
                        foreach (var issue in treeReport.SelfReferencingNodes)
                        {
                            _logger.Error($"   - {issue.NodeName} (ID: {issue.NodeId}): {issue.Description}");
                        }
                    }
                    
                    if (treeReport.CircularReferenceChains.Count > 0)
                    {
                        _logger.Error($"❌ CRITICAL: Found {treeReport.CircularReferenceChains.Count} circular reference chains:");
                        foreach (var issue in treeReport.CircularReferenceChains)
                        {
                            _logger.Error($"   - {issue.NodeName} (ID: {issue.NodeId}): {issue.Description}");
                            if (!string.IsNullOrEmpty(issue.CyclePath))
                            {
                                _logger.Error($"     Cycle: {issue.CyclePath}");
                            }
                        }
                    }
                    
                    if (treeReport.OrphanedNodes.Count > 0)
                    {
                        _logger.Warning($"⚠️ Found {treeReport.OrphanedNodes.Count} orphaned nodes (parent doesn't exist):");
                        foreach (var issue in treeReport.OrphanedNodes.Take(10)) // Limit to first 10
                        {
                            _logger.Warning($"   - {issue.NodeName} (ID: {issue.NodeId}): parent_id = {issue.Description}");
                        }
                        if (treeReport.OrphanedNodes.Count > 10)
                        {
                            _logger.Warning($"   ... and {treeReport.OrphanedNodes.Count - 10} more");
                        }
                    }
                    
                    if (treeReport.ExcessivelyDeepNodes.Count > 0)
                    {
                        _logger.Warning($"⚠️ Found {treeReport.ExcessivelyDeepNodes.Count} nodes with excessive depth (> 15 levels):");
                        foreach (var issue in treeReport.ExcessivelyDeepNodes.Take(5)) // Limit to first 5
                        {
                            _logger.Warning($"   - {issue.NodeName} at depth {issue.Depth}: {issue.DisplayPath}");
                        }
                    }
                    
                    if (!treeReport.HasRootNodes)
                    {
                        _logger.Error("❌ CRITICAL: No root nodes found in tree_view!");
                    }
                    
                    // AUTO-REPAIR: Fix orphaned nodes automatically
                    if (treeReport.OrphanedNodes.Count > 0)
                    {
                        _logger.Info($"🔧 Auto-repairing {treeReport.OrphanedNodes.Count} orphaned nodes...");
                        var repaired = await AutoRepairOrphanedNodesAsync(treeReport.OrphanedNodes);
                        
                        if (repaired > 0)
                        {
                            _logger.Info($"✅ Successfully repaired {repaired} orphaned nodes by promoting them to root level");
                            _logger.Info($"💡 These nodes are now at the root of your tree. You can move or delete them if needed.");
                        }
                    }
                    
                    // Provide guidance for remaining issues
                    if (treeReport.SelfReferencingNodes.Count > 0 || treeReport.CircularReferenceChains.Count > 0)
                    {
                        _logger.Warning("⚠️ CRITICAL DATA CORRUPTION DETECTED!");
                        _logger.Warning("⚠️ These circular references will cause the app to freeze when accessing affected categories.");
                        _logger.Warning("⚠️ Recommendation: Run database repair or restore from backup.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ Startup diagnostics failed");
            }
        }
        
        /// <summary>
        /// Automatically repair orphaned nodes by setting their parent_id to NULL (promotes to root).
        /// This is a safe operation that doesn't delete data, just reorganizes the tree.
        /// </summary>
        private async Task<int> AutoRepairOrphanedNodesAsync(List<TreeIntegrityIssue> orphanedNodes)
        {
            if (orphanedNodes == null || orphanedNodes.Count == 0)
                return 0;
            
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                int repairedCount = 0;
                
                foreach (var issue in orphanedNodes)
                {
                    try
                    {
                        // Set parent_id to NULL (promotes to root level)
                        var rowsAffected = await connection.ExecuteAsync(
                            "UPDATE tree_view SET parent_id = NULL WHERE id = @NodeId",
                            new { NodeId = issue.NodeId });
                        
                        if (rowsAffected > 0)
                        {
                            _logger.Info($"🔧 Repaired orphaned node: '{issue.NodeName}' (ID: {issue.NodeId}) - promoted to root");
                            repairedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Failed to repair orphaned node: {issue.NodeName} (ID: {issue.NodeId})");
                        // Continue with other repairs even if one fails
                    }
                }
                
                if (repairedCount > 0)
                {
                    _logger.Info($"🔧 Auto-repair complete: {repairedCount}/{orphanedNodes.Count} orphaned nodes fixed");
                }
                
                return repairedCount;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to auto-repair orphaned nodes");
                return 0;
            }
        }
    }
}

