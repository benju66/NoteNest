using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Adapter that bridges between UI ViewModels and type-safe Core tree state management.
    /// Replaces the dynamic-based approach with proper type safety.
    /// </summary>
    public class TreeStateAdapter
    {
        private readonly ITreeStateManager _treeStateManager;

        public TreeStateAdapter(ITreeStateManager treeStateManager)
        {
            _treeStateManager = treeStateManager;
        }

        /// <summary>
        /// Collects expanded category IDs from UI tree ViewModels
        /// </summary>
        public List<string> CollectExpandedCategoryIds(ObservableCollection<CategoryTreeItem> rootCategories)
        {
            var expandedIds = new List<string>();
            
            if (rootCategories != null)
            {
                foreach (var category in rootCategories)
                {
                    CollectExpandedCategoriesRecursive(category, expandedIds);
                }
            }
            
            return expandedIds;
        }

        /// <summary>
        /// Restores expansion state to UI tree ViewModels
        /// </summary>
        public void RestoreExpansionState(ObservableCollection<CategoryTreeItem> rootCategories, HashSet<string> expandedIds)
        {
            if (rootCategories != null && expandedIds != null && expandedIds.Count > 0)
            {
                foreach (var category in rootCategories)
                {
                    RestoreExpansionStateRecursive(category, expandedIds);
                }
            }
        }

        /// <summary>
        /// Saves the current expansion state from UI ViewModels
        /// </summary>
        public async Task SaveExpansionStateAsync(ObservableCollection<CategoryTreeItem> rootCategories)
        {
            var expandedIds = CollectExpandedCategoryIds(rootCategories);
            await _treeStateManager.SaveExpansionStateAsync(expandedIds);
        }

        /// <summary>
        /// Loads and applies expansion state to UI ViewModels
        /// </summary>
        public async Task<bool> LoadAndApplyExpansionStateAsync(ObservableCollection<CategoryTreeItem> rootCategories)
        {
            var expansionState = await _treeStateManager.LoadExpansionStateAsync();
            
            if (expansionState.Success)
            {
                RestoreExpansionState(rootCategories, expansionState.ExpandedCategoryIds);
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Recursively collects expanded category IDs
        /// </summary>
        private void CollectExpandedCategoriesRecursive(CategoryTreeItem category, List<string> expandedIds)
        {
            if (category?.Model != null && !string.IsNullOrWhiteSpace(category.Model.Id))
            {
                if (category.IsExpanded)
                {
                    expandedIds.Add(category.Model.Id);
                }

                // Recurse through subcategories
                if (category.SubCategories != null)
                {
                    foreach (var subCategory in category.SubCategories)
                    {
                        CollectExpandedCategoriesRecursive(subCategory, expandedIds);
                    }
                }
            }
        }

        /// <summary>
        /// Recursively restores expansion state
        /// </summary>
        private void RestoreExpansionStateRecursive(CategoryTreeItem category, HashSet<string> expandedIds)
        {
            if (category?.Model != null && !string.IsNullOrWhiteSpace(category.Model.Id))
            {
                if (expandedIds.Contains(category.Model.Id))
                {
                    category.IsExpanded = true;
                }

                // Recurse through subcategories
                if (category.SubCategories != null)
                {
                    foreach (var subCategory in category.SubCategories)
                    {
                        RestoreExpansionStateRecursive(subCategory, expandedIds);
                    }
                }
            }
        }
    }
}
