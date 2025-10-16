using System;
using NoteNest.Domain.Common;
using NoteNest.Domain.Categories.Events;

namespace NoteNest.Domain.Categories
{
    /// <summary>
    /// Category aggregate root for managing folder hierarchy.
    /// Handles category lifecycle, renames, moves, and tree structure.
    /// </summary>
    public class CategoryAggregate : AggregateRoot
    {
        public Guid CategoryId { get; private set; }
        public override Guid Id => CategoryId;
        
        public Guid? ParentId { get; private set; }
        public string Name { get; private set; }
        public string Path { get; private set; }
        public bool IsPinned { get; private set; }
        public int SortOrder { get; private set; }
        
        public CategoryAggregate() { } // Public for event sourcing
        
        /// <summary>
        /// Create a new category.
        /// </summary>
        public static CategoryAggregate Create(Guid? parentId, string name, string path)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Category name cannot be empty", nameof(name));
            
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Category path cannot be empty", nameof(path));
            
            var category = new CategoryAggregate();
            var categoryId = Guid.NewGuid();
            
            category.AddDomainEvent(new CategoryCreated(categoryId, parentId, name, path));
            return category;
        }
        
        /// <summary>
        /// Rename the category.
        /// </summary>
        public Result Rename(string newName, string newPath)
        {
            if (string.IsNullOrWhiteSpace(newName))
                return Result.Fail("Category name cannot be empty");
            
            if (newName == Name)
                return Result.Fail("New name is the same as current name");
            
            AddDomainEvent(new CategoryRenamed(CategoryId, Name, newName, Path, newPath));
            return Result.Ok();
        }
        
        /// <summary>
        /// Move category to a new parent.
        /// </summary>
        public Result Move(Guid? newParentId, string newPath)
        {
            if (newParentId == ParentId)
                return Result.Fail("Category is already in this location");
            
            AddDomainEvent(new CategoryMoved(CategoryId, ParentId, newParentId, Path, newPath));
            return Result.Ok();
        }
        
        /// <summary>
        /// Delete the category.
        /// </summary>
        public Result Delete()
        {
            AddDomainEvent(new CategoryDeleted(CategoryId, Name, Path));
            return Result.Ok();
        }
        
        /// <summary>
        /// Pin the category for quick access.
        /// </summary>
        public void Pin()
        {
            if (!IsPinned)
            {
                AddDomainEvent(new CategoryPinned(CategoryId));
            }
        }
        
        /// <summary>
        /// Unpin the category.
        /// </summary>
        public void Unpin()
        {
            if (IsPinned)
            {
                AddDomainEvent(new CategoryUnpinned(CategoryId));
            }
        }
        
        /// <summary>
        /// Apply event to rebuild aggregate state from event stream.
        /// </summary>
        public override void Apply(IDomainEvent @event)
        {
            switch (@event)
            {
                case CategoryCreated e:
                    CategoryId = e.CategoryId;
                    ParentId = e.ParentId;
                    Name = e.Name;
                    Path = e.Path;
                    CreatedAt = e.OccurredAt;
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case CategoryRenamed e:
                    Name = e.NewName;
                    Path = e.NewPath;
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case CategoryMoved e:
                    ParentId = e.NewParentId;
                    Path = e.NewPath;
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case CategoryDeleted e:
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case CategoryPinned e:
                    IsPinned = true;
                    break;
                    
                case CategoryUnpinned e:
                    IsPinned = false;
                    break;
            }
        }
    }
}

