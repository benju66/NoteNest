using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Type-safe replacement for TreeStateService that uses dynamic.
    /// Handles tree expansion state persistence with proper types.
    /// </summary>
    public class TreeStateManager : ITreeStateManager
    {
        private readonly ConfigurationService _configService;
        private readonly IAppLogger _logger;

        public TreeStateManager(ConfigurationService configService, IAppLogger logger)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Saves the expansion state of tree categories
        /// </summary>
        public async Task SaveExpansionStateAsync(List<string> expandedCategoryIds)
        {
            try
            {
                var filteredIds = expandedCategoryIds?.Where(id => !string.IsNullOrWhiteSpace(id)).ToList() ?? new List<string>();
                await _configService.SaveExpandedCategoriesAsync(filteredIds);
                _logger.Debug($"TreeStateManager: Saved {filteredIds.Count} expanded categories");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "TreeStateManager: Failed to save tree expansion state");
            }
        }

        /// <summary>
        /// Loads the expansion state from storage
        /// </summary>
        public async Task<TreeExpansionState> LoadExpansionStateAsync()
        {
            try
            {
                var expandedIds = await _configService.LoadExpandedCategoriesAsync();
                var result = expandedIds ?? new HashSet<string>();
                
                _logger.Debug($"TreeStateManager: Loaded {result.Count} expanded categories");
                return TreeExpansionState.CreateSuccess(result);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "TreeStateManager: Failed to load tree expansion state");
                return TreeExpansionState.CreateFailure($"Failed to load expansion state: {ex.Message}");
            }
        }
    }
}
