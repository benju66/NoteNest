using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Transaction
{
    /// <summary>
    /// Transaction step that updates all path-dependent services to use new storage location
    /// Handles PathService static state, ConfigurationService, FileWatcher, etc.
    /// </summary>
    public class PathUpdateStep : TransactionStepBase
    {
        private readonly string _newPath;
        private readonly IServiceProvider _serviceProvider;
        private PathUpdateState _originalState;

        public override string Description => $"Update path-dependent services to: {_newPath}";
        public override bool CanRollback => true;

        public PathUpdateStep(string newPath, IServiceProvider serviceProvider, IAppLogger logger) : base(logger)
        {
            _newPath = newPath ?? throw new ArgumentNullException(nameof(newPath));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        protected override async Task<TransactionStepResult> ExecuteStepAsync()
        {
            try
            {
                // Capture current state for potential rollback
                _originalState = await CaptureCurrentStateAsync();

                var updateCount = 0;

                // Update PathService static state
                var oldRootPath = PathService.RootPath;
                PathService.RootPath = _newPath;
                updateCount++;
                _logger.Debug($"Updated PathService.RootPath: {oldRootPath} -> {_newPath}");

                // Update ConfigurationService settings
                var configService = _serviceProvider.GetService(typeof(ConfigurationService)) as ConfigurationService;
                if (configService != null)
                {
                    var oldDefaultPath = configService.Settings.DefaultNotePath;
                    var oldMetadataPath = configService.Settings.MetadataPath;
                    
                    configService.Settings.DefaultNotePath = _newPath;
                    configService.Settings.MetadataPath = Path.Combine(_newPath, ".metadata");
                    
                    // Save updated settings
                    await configService.SaveSettingsAsync();
                    updateCount++;
                    
                    _logger.Debug($"Updated ConfigurationService paths: DefaultNotePath {oldDefaultPath} -> {_newPath}, MetadataPath {oldMetadataPath} -> {Path.Combine(_newPath, ".metadata")}");
                }

                // Update FileWatcher service if available
                var fileWatcher = _serviceProvider.GetService(typeof(FileWatcherService)) as FileWatcherService;
                if (fileWatcher != null)
                {
                    try
                    {
                        // Stop watching old location - note we need to stop all watchers
                        fileWatcher.StopAllWatchers();
                        
                        // Start watching new location
                        var newNotesPath = Path.Combine(_newPath, "Notes");
                        fileWatcher.StartWatching(newNotesPath, "*.*", includeSubdirectories: true);
                        updateCount++;
                        
                        _logger.Debug($"Updated FileWatcher to monitor: {newNotesPath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to update FileWatcher: {ex.Message}");
                        // Continue - FileWatcher failure shouldn't fail the whole transaction
                    }
                }

                // Update TabPersistenceService (it reads from ConfigurationService, so should automatically pick up changes)
                // But we might want to trigger a reload if there's an API for it

                _logger.Info($"Successfully updated {updateCount} path-dependent services");
                return TransactionStepResult.Succeeded(new { UpdatedServices = updateCount });
            }
            catch (Exception ex)
            {
                return TransactionStepResult.Failed($"Exception while updating path-dependent services: {ex.Message}", ex);
            }
        }

        protected override async Task<TransactionStepResult> RollbackStepAsync()
        {
            if (_originalState == null)
                return TransactionStepResult.Failed("No original state captured for rollback");

            try
            {
                var rollbackCount = 0;

                // Restore PathService static state
                PathService.RootPath = _originalState.OriginalRootPath;
                rollbackCount++;
                _logger.Debug($"Rolled back PathService.RootPath to: {_originalState.OriginalRootPath}");

                // Restore ConfigurationService settings
                var configService = _serviceProvider.GetService(typeof(ConfigurationService)) as ConfigurationService;
                if (configService != null)
                {
                    configService.Settings.DefaultNotePath = _originalState.OriginalDefaultNotePath;
                    configService.Settings.MetadataPath = _originalState.OriginalMetadataPath;
                    
                    // Save restored settings
                    await configService.SaveSettingsAsync();
                    rollbackCount++;
                    
                    _logger.Debug($"Rolled back ConfigurationService paths to: DefaultNotePath={_originalState.OriginalDefaultNotePath}, MetadataPath={_originalState.OriginalMetadataPath}");
                }

                // Restore FileWatcher service
                var fileWatcher = _serviceProvider.GetService(typeof(FileWatcherService)) as FileWatcherService;
                if (fileWatcher != null)
                {
                    try
                    {
                        fileWatcher.StopAllWatchers();
                        fileWatcher.StartWatching(_originalState.OriginalWatchPath, "*.*", includeSubdirectories: true);
                        rollbackCount++;
                        
                        _logger.Debug($"Rolled back FileWatcher to monitor: {_originalState.OriginalWatchPath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to rollback FileWatcher: {ex.Message}");
                    }
                }

                _logger.Info($"Successfully rolled back {rollbackCount} path-dependent services");
                return TransactionStepResult.Succeeded(new { RolledBackServices = rollbackCount });
            }
            catch (Exception ex)
            {
                return TransactionStepResult.Failed($"Exception during path services rollback: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Capture current state of all path-dependent services for rollback
        /// </summary>
        private async Task<PathUpdateState> CaptureCurrentStateAsync()
        {
            var state = new PathUpdateState
            {
                OriginalRootPath = PathService.RootPath
            };

            var configService = _serviceProvider.GetService(typeof(ConfigurationService)) as ConfigurationService;
            if (configService != null)
            {
                state.OriginalDefaultNotePath = configService.Settings.DefaultNotePath;
                state.OriginalMetadataPath = configService.Settings.MetadataPath;
            }

            // For FileWatcher, we'll assume it's watching the Notes subfolder
            state.OriginalWatchPath = Path.Combine(state.OriginalRootPath, "Notes");

            await Task.CompletedTask;
            return state;
        }
    }

    /// <summary>
    /// Captured state of path-dependent services for rollback
    /// </summary>
    internal class PathUpdateState
    {
        public string OriginalRootPath { get; set; } = string.Empty;
        public string OriginalDefaultNotePath { get; set; } = string.Empty;
        public string OriginalMetadataPath { get; set; } = string.Empty;
        public string OriginalWatchPath { get; set; } = string.Empty;
    }
}
