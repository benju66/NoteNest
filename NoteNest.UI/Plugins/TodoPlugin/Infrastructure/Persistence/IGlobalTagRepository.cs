using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence
{
    /// <summary>
    /// Repository for managing global tag vocabulary.
    /// Tracks usage statistics for autocomplete and popular tags.
    /// </summary>
    public interface IGlobalTagRepository
    {
        /// <summary>
        /// Get most popular tags (for quick suggestions).
        /// </summary>
        Task<List<TagSuggestion>> GetPopularTagsAsync(int limit = 20);

        /// <summary>
        /// Get tag suggestions matching a prefix (for autocomplete).
        /// </summary>
        Task<List<TagSuggestion>> GetSuggestionsAsync(string prefix, int limit = 20);

        /// <summary>
        /// Increment usage count for a tag (when tag is added).
        /// Creates tag if it doesn't exist.
        /// </summary>
        Task IncrementUsageAsync(string tagName);

        /// <summary>
        /// Decrement usage count for a tag (when tag is removed).
        /// </summary>
        Task DecrementUsageAsync(string tagName);

        /// <summary>
        /// Ensure a tag exists in global_tags table.
        /// Creates with usage_count = 0 if doesn't exist.
        /// </summary>
        Task EnsureExistsAsync(string tagName);
    }
}

