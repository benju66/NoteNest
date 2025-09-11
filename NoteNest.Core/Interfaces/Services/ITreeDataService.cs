using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Core.Models;

namespace NoteNest.Core.Interfaces.Services
{
    /// <summary>
    /// Service responsible for loading and building tree data structures.
    /// Separated from UI concerns to follow SRP - works with pure data models.
    /// </summary>
    public interface ITreeDataService
    {
        /// <summary>
        /// Loads categories from storage and builds the hierarchical tree data structure
        /// </summary>
        Task<TreeDataResult> LoadTreeDataAsync();
        
        /// <summary>
        /// Builds a tree node and its children from flat category data
        /// </summary>
        Task<TreeNodeData> BuildTreeNodeAsync(CategoryModel category, List<CategoryModel> allCategories, int level);
        
        // Pinning functionality removed - will be reimplemented with better architecture later
    }
}
