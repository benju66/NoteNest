using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NoteNest.Application.Tags.Services
{
    /// <summary>
    /// Unified view service that queries tags from both databases without synchronization.
    /// This respects the architecture where tree.db is rebuildable and todos.db is permanent.
    /// </summary>
    public interface IUnifiedTagViewService
    {
        /// <summary>
        /// Get all tags for an entity (queries the appropriate database based on entity type)
        /// </summary>
        Task<List<TagInfo>> GetEntityTagsAsync(Guid entityId, string entityType);

        /// <summary>
        /// Get all available tags across both databases for suggestions
        /// </summary>
        Task<List<string>> GetAllTagSuggestionsAsync(string prefix = "", int limit = 20);

        /// <summary>
        /// Get popular tags for a specific entity type
        /// </summary>
        Task<List<TagSuggestion>> GetPopularTagsAsync(string entityType, int limit = 10);

        /// <summary>
        /// Add tag to an entity (writes to appropriate database)
        /// </summary>
        Task AddTagAsync(Guid entityId, string entityType, string tagName, string source = "manual");

        /// <summary>
        /// Remove tag from an entity (removes from appropriate database)
        /// </summary>
        Task RemoveTagAsync(Guid entityId, string entityType, string tagName);

        /// <summary>
        /// Set all tags for an entity (replaces existing tags in appropriate database)
        /// </summary>
        Task SetEntityTagsAsync(Guid entityId, string entityType, List<string> tagNames, string source = "manual");
    }

    public class TagInfo
    {
        public string TagName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class TagSuggestion
    {
        public string Tag { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int UsageCount { get; set; }
    }
}