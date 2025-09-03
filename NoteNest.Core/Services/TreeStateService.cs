using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    public interface ITreeStateService
    {
        Task SaveExpansionStateAsync(IEnumerable<CategoryModel> expandedCategories);
        Task<HashSet<string>> LoadExpansionStateAsync();
        void CollectExpandedCategories(dynamic rootCategories, List<string> expandedIds);
        void RestoreExpansionState(dynamic rootCategories, HashSet<string> expandedIds);
    }

    public class TreeStateService : ITreeStateService
    {
        private readonly ConfigurationService _configService;
        private readonly IAppLogger _logger;

        public TreeStateService(ConfigurationService configService, IAppLogger logger = null)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _logger = logger; // logger is optional
        }

        public async Task SaveExpansionStateAsync(IEnumerable<CategoryModel> expandedCategories)
        {
            try
            {
                var expandedIds = expandedCategories?.Select(c => c.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList() ?? new List<string>();
                await _configService.SaveExpandedCategoriesAsync(expandedIds);
                _logger?.Debug($"Saved {expandedIds.Count} expanded categories");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to save tree expansion state");
            }
        }

        public async Task<HashSet<string>> LoadExpansionStateAsync()
        {
            try
            {
                var expandedIds = await _configService.LoadExpandedCategoriesAsync();
                _logger?.Debug($"Loaded {expandedIds?.Count ?? 0} expanded categories");
                return expandedIds ?? new HashSet<string>();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to load tree expansion state");
                return new HashSet<string>();
            }
        }

        // Generic helper to work with ViewModels without creating dependency
        public void CollectExpandedCategories(dynamic rootCategories, List<string> expandedIds)
        {
            if (rootCategories == null || expandedIds == null) return;
            foreach (var item in rootCategories)
            {
                try
                {
                    if (item.IsExpanded == true && item.Model != null && !string.IsNullOrWhiteSpace(item.Model.Id))
                    {
                        expandedIds.Add(item.Model.Id);
                    }
                    if (item.SubCategories != null)
                    {
                        CollectExpandedCategories(item.SubCategories, expandedIds);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Error collecting expansion state: {ex.Message}");
                }
            }
        }

        public void RestoreExpansionState(dynamic rootCategories, HashSet<string> expandedIds)
        {
            if (rootCategories == null || expandedIds == null || expandedIds.Count == 0) return;
            foreach (var item in rootCategories)
            {
                try
                {
                    if (item.Model != null && expandedIds.Contains(item.Model.Id))
                    {
                        item.IsExpanded = true;
                    }
                    if (item.SubCategories != null)
                    {
                        RestoreExpansionState(item.SubCategories, expandedIds);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Error restoring expansion state: {ex.Message}");
                }
            }
        }
    }
}


