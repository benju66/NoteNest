using System;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Services.SaveCoordination;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Helper class for easy access to save operations throughout the application
    /// Provides unified interface for manual saves, batch saves, and emergency saves
    /// </summary>
    public class SaveOperationsHelper
    {
        private readonly SaveCoordinator _saveCoordinator;
        private readonly CentralSaveManager _centralSaveManager;
        private readonly IAppLogger _logger;

        public SaveOperationsHelper(
            SaveCoordinator saveCoordinator,
            CentralSaveManager centralSaveManager,
            IAppLogger logger)
        {
            _saveCoordinator = saveCoordinator ?? throw new ArgumentNullException(nameof(saveCoordinator));
            _centralSaveManager = centralSaveManager ?? throw new ArgumentNullException(nameof(centralSaveManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Safe save with retry logic - use this instead of direct SaveManager calls
        /// </summary>
        public async Task<bool> SafeSaveAsync(Func<Task> saveAction, string filePath, string noteTitle)
        {
            return await _saveCoordinator.SafeSaveWithRetry(saveAction, filePath, noteTitle);
        }

        /// <summary>
        /// Emergency save all dirty tabs (for shutdown, crashes, etc.)
        /// </summary>
        public async Task<BatchSaveResult> EmergencySaveAllAsync()
        {
            return await _centralSaveManager.SaveAllAsync();
        }

        /// <summary>
        /// Get current save coordination statistics
        /// </summary>
        public SaveCoordinatorStats GetStats()
        {
            return _saveCoordinator.GetStats();
        }
    }
}
