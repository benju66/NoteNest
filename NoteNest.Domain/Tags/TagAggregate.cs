using System;
using NoteNest.Domain.Common;
using NoteNest.Domain.Tags.Events;

namespace NoteNest.Domain.Tags
{
    /// <summary>
    /// Tag aggregate root for managing tag vocabulary and usage.
    /// Tracks tag lifecycle, usage counts, and metadata.
    /// </summary>
    public class TagAggregate : AggregateRoot
    {
        public Guid TagId { get; private set; }
        public override Guid Id => TagId;
        
        public string Name { get; private set; }          // Normalized (lowercase)
        public string DisplayName { get; private set; }   // Original casing
        public int UsageCount { get; private set; }
        public string Category { get; private set; }
        public string Color { get; private set; }
        
        public TagAggregate() { } // Public for event sourcing
        
        /// <summary>
        /// Create a new tag.
        /// </summary>
        public static TagAggregate Create(string name, string displayName = null)
        {
            var tag = new TagAggregate();
            var tagId = Guid.NewGuid();
            var normalized = name.ToLowerInvariant().Trim();
            var display = displayName?.Trim() ?? name.Trim();
            
            tag.AddDomainEvent(new TagCreated(tagId, normalized, display));
            return tag;
        }
        
        /// <summary>
        /// Increment usage count when tag is applied to an entity.
        /// </summary>
        public void IncrementUsage()
        {
            AddDomainEvent(new TagUsageIncremented(TagId));
        }
        
        /// <summary>
        /// Decrement usage count when tag is removed from an entity.
        /// </summary>
        public void DecrementUsage()
        {
            if (UsageCount > 0)
            {
                AddDomainEvent(new TagUsageDecremented(TagId));
            }
        }
        
        /// <summary>
        /// Set tag category for organization.
        /// </summary>
        public void SetCategory(string category)
        {
            if (Category != category)
            {
                AddDomainEvent(new TagCategorySet(TagId, category));
            }
        }
        
        /// <summary>
        /// Set tag color for visual distinction.
        /// </summary>
        public void SetColor(string color)
        {
            if (Color != color)
            {
                AddDomainEvent(new TagColorSet(TagId, color));
            }
        }
        
        /// <summary>
        /// Apply event to rebuild aggregate state from event stream.
        /// </summary>
        public override void Apply(IDomainEvent @event)
        {
            switch (@event)
            {
                case TagCreated e:
                    TagId = e.TagId;
                    Name = e.Name;
                    DisplayName = e.DisplayName;
                    UsageCount = 0;
                    CreatedAt = e.OccurredAt;
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case TagUsageIncremented e:
                    UsageCount++;
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case TagUsageDecremented e:
                    if (UsageCount > 0)
                        UsageCount--;
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case TagCategorySet e:
                    Category = e.Category;
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case TagColorSet e:
                    Color = e.Color;
                    UpdatedAt = e.OccurredAt;
                    break;
            }
        }
    }
}

