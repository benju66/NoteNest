using System;
using System.Collections.Generic;
using System.Linq;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Interfaces.Services;

namespace NoteNest.Core.Services
{
    public interface ITreeStructureValidationService
    {
        bool WouldCreateCircularReference(string categoryId, string newParentId, List<CategoryModel> allCategories);
        bool HasDuplicateName(string name, string parentId, string excludeCategoryId, List<CategoryModel> allCategories);
        bool ExceedsMaxDepth(string parentId, int additionalDepth, List<CategoryModel> allCategories);
        ValidationResult ValidateMove(string categoryId, string targetParentId, List<CategoryModel> allCategories);
        ValidationResult ValidateCreate(string name, string parentId, List<CategoryModel> allCategories);
    }
    
    public class TreeStructureValidationService : ITreeStructureValidationService
    {
        private readonly ConfigurationService _config;
        private readonly IAppLogger _logger;
        
        public TreeStructureValidationService(ConfigurationService config, IAppLogger logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public bool WouldCreateCircularReference(string categoryId, string newParentId, List<CategoryModel> allCategories)
        {
            if (string.IsNullOrEmpty(newParentId)) return false;
            if (categoryId == newParentId) return true;
            
            var current = newParentId;
            var visited = new HashSet<string>();
            
            while (!string.IsNullOrEmpty(current))
            {
                if (!visited.Add(current) || current == categoryId)
                    return true;
                    
                var parent = allCategories.FirstOrDefault(c => c.Id == current);
                current = parent?.ParentId;
            }
            
            return false;
        }
        
        public bool HasDuplicateName(string name, string parentId, string excludeCategoryId, List<CategoryModel> allCategories)
        {
            return allCategories.Any(c => 
                c.Id != excludeCategoryId &&
                c.ParentId == parentId &&
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        
        public bool ExceedsMaxDepth(string parentId, int additionalDepth, List<CategoryModel> allCategories)
        {
            var maxDepth = _config?.Settings?.MaxTreeDepth ?? 10;
            var currentDepth = GetDepth(parentId, allCategories);
            return (currentDepth + additionalDepth) > maxDepth;
        }
        
        private int GetDepth(string categoryId, List<CategoryModel> allCategories)
        {
            if (string.IsNullOrEmpty(categoryId)) return 0;
            
            var depth = 0;
            var current = categoryId;
            var visited = new HashSet<string>();
            
            while (!string.IsNullOrEmpty(current) && visited.Add(current))
            {
                depth++;
                var category = allCategories.FirstOrDefault(c => c.Id == current);
                current = category?.ParentId;
            }
            
            return depth;
        }
        
        public ValidationResult ValidateMove(string categoryId, string targetParentId, List<CategoryModel> allCategories)
        {
            if (WouldCreateCircularReference(categoryId, targetParentId, allCategories))
                return ValidationResult.Failed("Cannot move a category into its own descendant");
            
            var category = allCategories.FirstOrDefault(c => c.Id == categoryId);
            if (category != null && HasDuplicateName(category.Name, targetParentId, categoryId, allCategories))
                return ValidationResult.Failed("A category with this name already exists in the target location");
            
            // Calculate subtree depth
            var subtreeDepth = GetMaxSubtreeDepth(categoryId, allCategories);
            if (ExceedsMaxDepth(targetParentId, subtreeDepth, allCategories))
                return ValidationResult.Failed("Moving this category would exceed the maximum nesting depth");
            
            return ValidationResult.Success();
        }
        
        public ValidationResult ValidateCreate(string name, string parentId, List<CategoryModel> allCategories)
        {
            if (string.IsNullOrWhiteSpace(name))
                return ValidationResult.Failed("Category name cannot be empty");
            
            if (HasDuplicateName(name, parentId, string.Empty, allCategories))
                return ValidationResult.Failed("A category with this name already exists");
            
            if (ExceedsMaxDepth(parentId, 1, allCategories))
                return ValidationResult.Failed("Cannot create category: maximum nesting depth reached");
            
            return ValidationResult.Success();
        }
        
        private int GetMaxSubtreeDepth(string categoryId, List<CategoryModel> allCategories)
        {
            var children = allCategories.Where(c => c.ParentId == categoryId);
            if (!children.Any()) return 1;
            
            return 1 + children.Max(child => GetMaxSubtreeDepth(child.Id, allCategories));
        }
    }
}
