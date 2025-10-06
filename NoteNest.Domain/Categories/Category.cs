using System;
using NoteNest.Domain.Common;

namespace NoteNest.Domain.Categories
{
    public class Category
    {
        public CategoryId Id { get; private set; }
        public string Name { get; private set; }
        public string Path { get; private set; }
        public CategoryId? ParentId { get; private set; }

        private Category() { } // For ORM

        public Category(CategoryId id, string name, string path, CategoryId? parentId = null)
        {
            Id = id;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Path = path ?? throw new ArgumentNullException(nameof(path));
            ParentId = parentId;
        }

        public static Category Create(string name, string path, CategoryId? parentId = null)
        {
            var id = CategoryId.From(path); // Use path as unique identifier
            return new Category(id, name, path, parentId);
        }

        public Result UpdateName(string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                return Result.Fail("Category name cannot be empty");

            Name = newName;
            return Result.Ok();
        }

        public Result UpdatePath(string newPath)
        {
            if (string.IsNullOrWhiteSpace(newPath))
                return Result.Fail("Category path cannot be empty");

            Path = newPath;
            return Result.Ok();
        }

        /// <summary>
        /// Moves category to a new parent in the tree hierarchy.
        /// Used by drag & drop and move operations.
        /// Note: Physical folder path does NOT change (matches rename behavior).
        /// </summary>
        public Result Move(CategoryId? newParentId)
        {
            // Allow moving to root (newParentId = null)
            ParentId = newParentId;
            return Result.Ok();
        }
    }
}
