using System.Collections.Generic;
using System.Threading.Tasks;

namespace NoteNest.Core.Interfaces.Services
{
    /// <summary>
    /// Service responsible for managing tree expansion state persistence.
    /// Type-safe replacement for TreeStateService that uses dynamic.
    /// </summary>
    public interface ITreeStateManager
    {
        /// <summary>
        /// Saves the expansion state of tree categories
        /// </summary>
        Task SaveExpansionStateAsync(List<string> expandedCategoryIds);
        
        /// <summary>
        /// Loads the expansion state from storage
        /// </summary>
        Task<TreeExpansionState> LoadExpansionStateAsync();
    }

    /// <summary>
    /// Container for tree expansion state data
    /// </summary>
    public class TreeExpansionState
    {
        public HashSet<string> ExpandedCategoryIds { get; set; } = new();
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        public static TreeExpansionState CreateSuccess(HashSet<string> expandedIds)
        {
            return new TreeExpansionState
            {
                ExpandedCategoryIds = expandedIds,
                Success = true
            };
        }

        public static TreeExpansionState CreateFailure(string errorMessage)
        {
            return new TreeExpansionState
            {
                ExpandedCategoryIds = new HashSet<string>(),
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }
}
