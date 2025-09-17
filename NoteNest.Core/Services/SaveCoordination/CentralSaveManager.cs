using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Models;

namespace NoteNest.Core.Services.SaveCoordination
{
    /// <summary>
    /// Central save manager that replaces distributed tab timers
    /// DAY 2: Timer consolidation - manages auto-save and WAL for ALL tabs
    /// </summary>
    public class CentralSaveManager : IDisposable
    {
        private readonly SaveCoordinator _saveCoordinator;
        private readonly IWorkspaceService _workspace;
        private readonly IWriteAheadLog _walManager;
        private readonly IAppLogger _logger;
        private readonly SaveStatusManager _statusManager;
        
        // Consolidated timers (replaces per-tab timers)
        private Timer _autoSaveTimer;
        private Timer _walTimer;
        private readonly SemaphoreSlim _autoSaveLock = new(1, 1);
        private readonly SemaphoreSlim _walLock = new(1, 1);
        
        // Configuration
        private readonly TimeSpan _autoSaveInterval = TimeSpan.FromSeconds(30);
        private readonly TimeSpan _walInterval = TimeSpan.FromSeconds(10);
        
        private bool _disposed = false;

        public CentralSaveManager(
            SaveCoordinator saveCoordinator,
            IWorkspaceService workspace,
            IWriteAheadLog walManager,
            IAppLogger logger,
            SaveStatusManager statusManager)
        {
            _saveCoordinator = saveCoordinator ?? throw new ArgumentNullException(nameof(saveCoordinator));
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _walManager = walManager ?? throw new ArgumentNullException(nameof(walManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _statusManager = statusManager ?? throw new ArgumentNullException(nameof(statusManager));
        }

        /// <summary>
        /// Start the consolidated timers (replaces individual tab timers)
        /// </summary>
        public void StartTimers()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CentralSaveManager));

            _logger.Info("Starting centralized save timers");

            // Consolidated auto-save timer for ALL dirty tabs
            _autoSaveTimer = new Timer(async _ => await ProcessAutoSaves(), 
                null, _autoSaveInterval, _autoSaveInterval);

            // Consolidated WAL timer for ALL tabs
            _walTimer = new Timer(async _ => await ProcessWALFlush(),
                null, _walInterval, _walInterval);

            _logger.Info($"Central timers started: Auto-save every {_autoSaveInterval.TotalSeconds}s, WAL every {_walInterval.TotalSeconds}s");
        }

        /// <summary>
        /// Stop the consolidated timers
        /// </summary>
        public void StopTimers()
        {
            _logger.Info("Stopping centralized save timers");

            _autoSaveTimer?.Dispose();
            _autoSaveTimer = null;

            _walTimer?.Dispose();
            _walTimer = null;
        }

        /// <summary>
        /// Process auto-saves for all dirty tabs across all panes
        /// </summary>
        private async Task ProcessAutoSaves()
        {
            if (_disposed || !await _autoSaveLock.WaitAsync(100)) 
                return; // Skip if already processing or disposed

            try
            {
                var dirtyTabs = GetAllDirtyTabs();
                if (!dirtyTabs.Any())
                {
                    _logger.Debug("No dirty tabs found for auto-save");
                    return;
                }

                _logger.Debug($"Processing auto-save for {dirtyTabs.Count} dirty tabs");

                // Create save operations for batch processing
                var saveOperations = dirtyTabs.Select(tab => new
                {
                    saveAction = (Func<Task>)(() => SaveTabContent(tab)),
                    filePath = tab.Note?.FilePath ?? "",
                    noteTitle = tab.Title ?? "Unknown"
                }).Where(op => !string.IsNullOrEmpty(op.filePath))
                .Select(op => (op.saveAction, op.filePath, op.noteTitle));

                // Execute batch save with coordinated status updates
                var result = await _saveCoordinator.SafeBatchSave(saveOperations);
                
                _logger.Info($"Auto-save batch completed: {result.SuccessCount} succeeded, {result.FailureCount} failed");
                
                // Log any failures for debugging
                if (result.FailureCount > 0)
                {
                    foreach (var failedItem in result.FailedItems)
                    {
                        _logger.Warning($"Auto-save failed: {failedItem}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during auto-save processing");
                _statusManager.ShowStatus("Auto-save error - see logs", StatusType.Error);
            }
            finally
            {
                _autoSaveLock.Release();
            }
        }

        /// <summary>
        /// Process WAL flush for all tabs
        /// </summary>
        private async Task ProcessWALFlush()
        {
            if (_disposed || !await _walLock.WaitAsync(100))
                return; // Skip if already processing or disposed

            try
            {
                var allTabs = GetAllTabs();
                if (!allTabs.Any())
                {
                    _logger.Debug("No tabs found for WAL processing");
                    return;
                }

                _logger.Debug($"Processing WAL flush for {allTabs.Count} tabs");

                var walTasks = allTabs.Select(async tab =>
                {
                    try
                    {
                        if (tab.IsDirty && !string.IsNullOrEmpty(tab.Content))
                        {
                            await _walManager.AppendAsync(tab.NoteId, tab.Content);
                        }
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"WAL write failed for {tab.Title}: {ex.Message}");
                        return false;
                    }
                });

                var results = await Task.WhenAll(walTasks);
                var successCount = results.Count(r => r);
                var failureCount = results.Length - successCount;

                if (failureCount > 0)
                {
                    _logger.Warning($"WAL flush completed with failures: {successCount} succeeded, {failureCount} failed");
                }
                else
                {
                    _logger.Debug($"WAL flush completed successfully for {successCount} tabs");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during WAL processing");
            }
            finally
            {
                _walLock.Release();
            }
        }

        /// <summary>
        /// Get all dirty tabs across all panes (main and detached)
        /// </summary>
        private List<ITabItem> GetAllDirtyTabs()
        {
            var dirtyTabs = new List<ITabItem>();

            try
            {
                // Search main panes
                if (_workspace.Panes != null)
                {
                    foreach (var pane in _workspace.Panes)
                    {
                        dirtyTabs.AddRange(pane.Tabs.Where(t => t.IsDirty));
                    }
                }

                // Search detached panes  
                if (_workspace.DetachedPanes != null)
                {
                    foreach (var pane in _workspace.DetachedPanes)
                    {
                        dirtyTabs.AddRange(pane.Tabs.Where(t => t.IsDirty));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error retrieving dirty tabs");
            }

            return dirtyTabs;
        }

        /// <summary>
        /// Get all tabs across all panes (for WAL processing)
        /// </summary>
        private List<ITabItem> GetAllTabs()
        {
            var allTabs = new List<ITabItem>();

            try
            {
                // Search main panes
                if (_workspace.Panes != null)
                {
                    foreach (var pane in _workspace.Panes)
                    {
                        allTabs.AddRange(pane.Tabs);
                    }
                }

                // Search detached panes
                if (_workspace.DetachedPanes != null)
                {
                    foreach (var pane in _workspace.DetachedPanes)
                    {
                        allTabs.AddRange(pane.Tabs);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error retrieving all tabs");
            }

            return allTabs;
        }

        /// <summary>
        /// Save content for a specific tab (integrates with existing save manager)
        /// </summary>
        private async Task SaveTabContent(ITabItem tab)
        {
            try
            {
                // Use existing save infrastructure through service locator pattern
                // This maintains compatibility with existing save logic
                var saveManager = GetSaveManager();
                if (saveManager != null)
                {
                    await saveManager.SaveNoteAsync(tab.NoteId);
                }
                else
                {
                    throw new InvalidOperationException("SaveManager not available");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to save tab content: {tab.Title}");
                throw; // Re-throw to be handled by SaveCoordinator
            }
        }

        /// <summary>
        /// Get the existing save manager (dependency injection - should be provided via constructor)
        /// Note: This is a temporary workaround - ideally ISaveManager should be injected
        /// </summary>
        private ISaveManager GetSaveManager()
        {
            // TODO: Inject ISaveManager directly instead of service locator pattern
            // For now, return null and handle gracefully in calling code
            _logger.Warning("SaveManager not available - consider injecting ISaveManager directly");
            return null;
        }

        /// <summary>
        /// Force save all dirty tabs (for shutdown scenarios)
        /// </summary>
        public async Task<BatchSaveResult> SaveAllAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CentralSaveManager));

            _logger.Info("Starting emergency save of all dirty tabs");

            var dirtyTabs = GetAllDirtyTabs();
            if (!dirtyTabs.Any())
            {
                _logger.Info("No dirty tabs to save");
                return new BatchSaveResult { SuccessCount = 0, FailureCount = 0 };
            }

            var saveOperations = dirtyTabs.Select(tab => new
            {
                saveAction = (Func<Task>)(() => SaveTabContent(tab)),
                filePath = tab.Note?.FilePath ?? "",
                noteTitle = tab.Title ?? "Unknown"
            }).Where(op => !string.IsNullOrEmpty(op.filePath))
            .Select(op => (op.saveAction, op.filePath, op.noteTitle));

            return await _saveCoordinator.SafeBatchSave(saveOperations);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _logger.Info("Disposing CentralSaveManager");

            StopTimers();
            
            _autoSaveLock?.Dispose();
            _walLock?.Dispose();
        }
    }
}
