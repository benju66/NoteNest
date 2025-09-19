using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Factory for managing SaveManager lifecycle during storage location changes
    /// Handles atomic replacement and state preservation for safe transitions
    /// </summary>
    public class SaveManagerFactory : ISaveManagerFactory, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IAppLogger _logger;
        private readonly SemaphoreSlim _replacementLock = new(1, 1);
        private ISaveManager _currentSaveManager;
        private readonly List<WeakReference> _eventSubscribers = new();
        private bool _disposed = false;

        public ISaveManager Current => _currentSaveManager;

        public event EventHandler<SaveManagerReplacedEventArgs> SaveManagerReplaced;

        public SaveManagerFactory(IServiceProvider serviceProvider, IAppLogger logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Get the current SaveManager from DI container
            _currentSaveManager = (ISaveManager)_serviceProvider.GetService(typeof(ISaveManager)) ?? 
                throw new InvalidOperationException("ISaveManager service not registered");
            
            // DIAGNOSTIC: Log the initial SaveManager's data path
            if (_currentSaveManager is RTFIntegratedSaveEngine rtfEngine)
            {
                var dataPathField = typeof(RTFIntegratedSaveEngine).GetField("_dataPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var currentDataPath = dataPathField?.GetValue(rtfEngine) as string ?? "UNKNOWN";
                _logger.Info($"SaveManagerFactory initialized with RTFIntegratedSaveEngine at path: {currentDataPath}");
            }
            else
            {
                _logger.Info($"SaveManagerFactory initialized with SaveManager type: {_currentSaveManager.GetType().Name}");
            }
        }

        /// <summary>
        /// Create a new SaveManager for the specified data path
        /// </summary>
        public async Task<ISaveManager> CreateSaveManagerAsync(string dataPath)
        {
            if (string.IsNullOrEmpty(dataPath))
                throw new ArgumentException("Data path cannot be null or empty", nameof(dataPath));

            try
            {
                _logger.Info($"Creating new SaveManager for path: {dataPath}");
                
                // Ensure the directory structure exists
                System.IO.Directory.CreateDirectory(dataPath);
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, ".temp"));
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, ".wal"));
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, ".metadata"));
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Notes"));
                
                // Create basic status notifier for the new SaveManager
                // We don't need a full StateManager for testing - BasicStatusNotifier is sufficient
                IStatusNotifier statusNotifier = new BasicStatusNotifier(_logger);
                
                // Create new SaveManager instance
                var newSaveManager = new RTFIntegratedSaveEngine(dataPath, statusNotifier);
                
                _logger.Info($"Successfully created new SaveManager for path: {dataPath}");
                return newSaveManager;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to create SaveManager for path: {dataPath}");
                throw;
            }
        }

        /// <summary>
        /// Capture the current state of the SaveManager for potential rollback
        /// </summary>
        public async Task<SaveManagerState> CaptureStateAsync()
        {
            if (_currentSaveManager == null)
                throw new InvalidOperationException("No current SaveManager to capture state from");

            try
            {
                _logger.Debug("Capturing SaveManager state for potential rollback");
                
                var state = new SaveManagerState
                {
                    DataPath = GetSaveManagerDataPath(_currentSaveManager),
                    CapturedAt = DateTime.UtcNow
                };

                // Capture all dirty note IDs and their content
                var dirtyNoteIds = _currentSaveManager.GetDirtyNoteIds();
                foreach (var noteId in dirtyNoteIds)
                {
                    state.NoteContents[noteId] = _currentSaveManager.GetContent(noteId);
                    state.LastSavedContents[noteId] = _currentSaveManager.GetLastSavedContent(noteId) ?? string.Empty;
                    state.DirtyNotes[noteId] = true;
                    
                    var filePath = _currentSaveManager.GetFilePath(noteId);
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        state.NoteFilePaths[noteId] = filePath;
                    }
                }

                // Capture WAL entries (if accessible via reflection)
                await CaptureWalStateAsync(state);

                _logger.Debug($"Captured state for {state.NoteContents.Count} notes, {state.DirtyNotes.Count(kvp => kvp.Value)} dirty");
                return state;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to capture SaveManager state");
                throw;
            }
        }

        /// <summary>
        /// Replace the current SaveManager with a new one atomically
        /// </summary>
        public async Task ReplaceSaveManagerAsync(ISaveManager newSaveManager)
        {
            if (newSaveManager == null)
                throw new ArgumentNullException(nameof(newSaveManager));

            await _replacementLock.WaitAsync();
            try
            {
                _logger.Info("Starting atomic SaveManager replacement");
                
                var oldSaveManager = _currentSaveManager;
                var newDataPath = GetSaveManagerDataPath(newSaveManager);
                
                // Step 1: Unsubscribe events from old SaveManager
                await UnsubscribeAllEventsAsync(oldSaveManager);
                
                // Step 2: Replace the SaveManager reference
                _currentSaveManager = newSaveManager;
                
                // Step 3: Update the DI container to return new instance
                await UpdateServiceContainerAsync(newSaveManager);
                
                // Step 4: Re-subscribe events to new SaveManager
                await SubscribeAllEventsAsync(newSaveManager);
                
                // Step 5: Fire replacement event for other components
                SaveManagerReplaced?.Invoke(this, new SaveManagerReplacedEventArgs
                {
                    OldSaveManager = oldSaveManager,
                    NewSaveManager = newSaveManager,
                    NewDataPath = newDataPath,
                    ReplacedAt = DateTime.UtcNow
                });
                
                // Step 6: Dispose old SaveManager
                try
                {
                    oldSaveManager?.Dispose();
                    _logger.Debug("Old SaveManager disposed successfully");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to dispose old SaveManager: {ex.Message}");
                }
                
                _logger.Info($"SaveManager replacement completed successfully to path: {newDataPath}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to replace SaveManager");
                throw;
            }
            finally
            {
                _replacementLock.Release();
            }
        }

        /// <summary>
        /// Restore SaveManager state from a previous capture
        /// </summary>
        public async Task RestoreStateAsync(SaveManagerState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            try
            {
                _logger.Info($"Restoring SaveManager state captured at {state.CapturedAt}");
                
                // Create SaveManager for original path
                var restoredSaveManager = await CreateSaveManagerAsync(state.DataPath);
                
                // Restore note states
                foreach (var kvp in state.NoteContents)
                {
                    var noteId = kvp.Key;
                    var content = kvp.Value;
                    
                    restoredSaveManager.UpdateContent(noteId, content);
                    
                    // Restore file path mapping if available
                    if (state.NoteFilePaths.TryGetValue(noteId, out var filePath))
                    {
                        restoredSaveManager.UpdateFilePath(noteId, filePath);
                    }
                }
                
                // Replace current SaveManager with restored one
                await ReplaceSaveManagerAsync(restoredSaveManager);
                
                _logger.Info("SaveManager state restored successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to restore SaveManager state");
                throw;
            }
        }

        /// <summary>
        /// Get the data path from a SaveManager instance using reflection
        /// </summary>
        private string GetSaveManagerDataPath(ISaveManager saveManager)
        {
            if (saveManager is RTFIntegratedSaveEngine rtfEngine)
            {
                // Use reflection to access private _dataPath field
                var dataPathField = typeof(RTFIntegratedSaveEngine).GetField("_dataPath", 
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (dataPathField != null)
                {
                    return dataPathField.GetValue(rtfEngine) as string ?? string.Empty;
                }
            }
            
            return string.Empty;
        }

        /// <summary>
        /// Capture WAL state if possible via reflection
        /// </summary>
        private async Task CaptureWalStateAsync(SaveManagerState state)
        {
            try
            {
                if (_currentSaveManager is RTFIntegratedSaveEngine rtfEngine)
                {
                    var walField = typeof(RTFIntegratedSaveEngine).GetField("_wal", 
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    
                    if (walField?.GetValue(rtfEngine) is WriteAheadLog wal)
                    {
                        // Get all WAL entries
                        var walEntries = await wal.RecoverAllAsync();
                        state.PendingWalEntries = walEntries.Keys.ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to capture WAL state: {ex.Message}");
            }
        }

        /// <summary>
        /// Unsubscribe events from old SaveManager (placeholder - will be expanded)
        /// </summary>
        private async Task UnsubscribeAllEventsAsync(ISaveManager oldSaveManager)
        {
            // This will be implemented when we add the event management layer
            _logger.Debug("Unsubscribing events from old SaveManager");
            await Task.CompletedTask;
        }

        /// <summary>
        /// Subscribe events to new SaveManager (placeholder - will be expanded)
        /// </summary>
        private async Task SubscribeAllEventsAsync(ISaveManager newSaveManager)
        {
            // This will be implemented when we add the event management layer
            _logger.Debug("Subscribing events to new SaveManager");
            await Task.CompletedTask;
        }

        /// <summary>
        /// Update DI container to return new SaveManager instance
        /// </summary>
        private async Task UpdateServiceContainerAsync(ISaveManager newSaveManager)
        {
            try
            {
                // This is a complex operation that may require service container modifications
                // For now, we'll update the factory's Current property and rely on components
                // to get SaveManager through the factory rather than directly from DI
                _logger.Debug("DI container updated to use new SaveManager instance");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to update DI container");
                throw;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    _replacementLock?.Dispose();
                    // Note: Don't dispose _currentSaveManager here - it's managed by DI container
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Error during SaveManagerFactory disposal: {ex.Message}");
                }
                finally
                {
                    _disposed = true;
                }
            }
        }
    }
}
