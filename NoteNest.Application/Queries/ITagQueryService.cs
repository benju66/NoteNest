using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NoteNest.Application.Queries
{
    /// <summary>
    /// Query service for tag projection.
    /// Provides unified tag queries across all entity types.
    /// </summary>
    public interface ITagQueryService
    {
        /// <summary>
        /// Get tags for a specific entity.
        /// </summary>
        Task<List<TagDto>> GetTagsForEntityAsync(Guid entityId, string entityType);
        
        /// <summary>
        /// Get all unique tags.
        /// </summary>
        Task<List<string>> GetAllTagsAsync();
        
        /// <summary>
        /// Get tag cloud with usage counts.
        /// </summary>
        Task<Dictionary<string, int>> GetTagCloudAsync(int topN = 50);
        
        /// <summary>
        /// Get tag suggestions with prefix matching.
        /// </summary>
        Task<List<TagSuggestion>> GetTagSuggestionsAsync(string prefix = "", int limit = 20);
        
        /// <summary>
        /// Search entities by tag.
        /// </summary>
        Task<List<EntityWithTag>> SearchByTagAsync(string tag);
        
        /// <summary>
        /// Get popular tags for an entity type.
        /// </summary>
        Task<List<TagSuggestion>> GetPopularTagsAsync(string entityType, int limit = 10);
    }
    
    public class TagDto
    {
        public string Tag { get; set; }
        public string DisplayName { get; set; }
        public string Source { get; set; }
        public DateTime CreatedAt { get; set; }
    }
    
    public class TagSuggestion
    {
        public string Tag { get; set; }
        public string DisplayName { get; set; }
        public int UsageCount { get; set; }
    }
    
    public class EntityWithTag
    {
        public Guid EntityId { get; set; }
        public string EntityType { get; set; }
        public string Tag { get; set; }
    }
}

