using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Implementation
{
    public class CategoryManagementService : ICategoryManagementService
    {
        private readonly NoteService _noteService;
        private readonly ConfigurationService _configService;
        private readonly IServiceErrorHandler _errorHandler;
        private readonly IAppLogger _logger;
        private readonly IFileSystemProvider _fileSystem;
        
        public CategoryManagementService(
            NoteService noteService,
            ConfigurationService configService,
            IServiceErrorHandler errorHandler,
            IAppLogger logger,
            IFileSystemProvider fileSystem)
        {
            _noteService = noteService ?? throw new ArgumentNullException(nameof(noteService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            
            _logger.Debug("CategoryManagementService initialized");
        }
        
        public async Task<CategoryModel> CreateCategoryAsync(string name, string? parentId = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Category name cannot be empty.", nameof(name));

            return await _errorHandler.SafeExecuteAsync(async () =>
            {
                var safeName = PathService.SanitizeName(name);
                var parentPath = string.IsNullOrEmpty(parentId)
                    ? PathService.ProjectsPath
                    : await GetCategoryPathById(parentId);

                var category = new CategoryModel
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = parentId,
                    Name = name,
                    Path = Path.Combine(parentPath, safeName),
                    Tags = new List<string>(),
                    Level = string.IsNullOrEmpty(parentId) ? 0 : await GetCategoryLevel(parentId) + 1
                };

                // Create physical directory
                if (!await _fileSystem.ExistsAsync(category.Path))
                {
                    await _fileSystem.CreateDirectoryAsync(category.Path);
                }

                // Add to categories and save
                var allCategories = await LoadCategoriesAsync();
                allCategories.Add(category);
                await SaveCategoriesAsync(allCategories);

                _logger.Info($"Created category: {name}");
                return category;
            }, "Create Category");
        }
        
        public async Task<CategoryModel> CreateSubCategoryAsync(CategoryModel parent, string name)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Subcategory name cannot be empty.", nameof(name));

            return await CreateCategoryAsync(name, parent.Id);
        }
        
        public async Task<bool> DeleteCategoryAsync(CategoryModel category)
        {
            if (category == null)
                throw new ArgumentNullException(nameof(category));

            return await _errorHandler.SafeExecuteAsync(async () =>
            {
                // Delete physical directory
                if (await _fileSystem.ExistsAsync(category.Path))
                {
                    // Note: This is a recursive delete - be careful!
                    await DeleteDirectoryRecursive(category.Path);
                }

                // Remove from categories list
                var allCategories = await LoadCategoriesAsync();
                RemoveCategoryAndChildren(allCategories, category.Id);
                await SaveCategoriesAsync(allCategories);

                _logger.Info($"Deleted category: {category.Name}");
                return true;
            }, "Delete Category");
        }
        
        public async Task<bool> RenameCategoryAsync(CategoryModel category, string newName)
        {
            if (category == null)
                throw new ArgumentNullException(nameof(category));
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("New name cannot be empty.", nameof(newName));

            return await _errorHandler.SafeExecuteAsync(async () =>
            {
                var oldName = category.Name;
                category.Name = newName;

                // Note: We're not renaming the physical directory to avoid breaking file paths
                // This is a design decision - the directory name stays as originally created

                var allCategories = await LoadCategoriesAsync();
                var categoryToUpdate = allCategories.FirstOrDefault(c => c.Id == category.Id);
                if (categoryToUpdate != null)
                {
                    categoryToUpdate.Name = newName;
                    await SaveCategoriesAsync(allCategories);
                }

                _logger.Info($"Renamed category from '{oldName}' to '{newName}'");
                return true;
            }, "Rename Category");
        }
        
        public async Task<bool> ToggleCategoryPinAsync(CategoryModel category)
        {
            if (category == null)
                throw new ArgumentNullException(nameof(category));

            return await _errorHandler.SafeExecuteAsync(async () =>
            {
                category.Pinned = !category.Pinned;

                var allCategories = await LoadCategoriesAsync();
                var categoryToUpdate = allCategories.FirstOrDefault(c => c.Id == category.Id);
                if (categoryToUpdate != null)
                {
                    categoryToUpdate.Pinned = category.Pinned;
                    await SaveCategoriesAsync(allCategories);
                }

                _logger.Info($"Category '{category.Name}' pinned: {category.Pinned}");
                return true;
            }, "Toggle Category Pin");
        }
        
        public async Task<List<CategoryModel>> LoadCategoriesAsync()
        {
            var metadataPath = _configService.Settings?.MetadataPath ?? PathService.MetadataPath;
            return await _noteService.LoadCategoriesAsync(metadataPath);
        }
        
        public async Task SaveCategoriesAsync(List<CategoryModel> categories)
        {
            if (categories == null)
                throw new ArgumentNullException(nameof(categories));

            var metadataPath = _configService.Settings?.MetadataPath ?? PathService.MetadataPath;
            await _noteService.SaveCategoriesAsync(metadataPath, categories);
        }

        // Helper methods
        private void RemoveCategoryAndChildren(List<CategoryModel> allCategories, string categoryId)
        {
            // Find all children recursively
            var toRemove = new List<string> { categoryId };
            var queue = new Queue<string>();
            queue.Enqueue(categoryId);

            while (queue.Count > 0)
            {
                var currentId = queue.Dequeue();
                var children = allCategories.Where(c => c.ParentId == currentId).Select(c => c.Id).ToList();
                foreach (var childId in children)
                {
                    toRemove.Add(childId);
                    queue.Enqueue(childId);
                }
            }

            // Remove all found categories
            allCategories.RemoveAll(c => toRemove.Contains(c.Id));
        }

        private async Task<string> GetCategoryPathById(string categoryId)
        {
            var categories = await LoadCategoriesAsync();
            var category = categories.FirstOrDefault(c => c.Id == categoryId);
            return category?.Path ?? PathService.ProjectsPath;
        }

        private async Task<int> GetCategoryLevel(string categoryId)
        {
            var categories = await LoadCategoriesAsync();
            var category = categories.FirstOrDefault(c => c.Id == categoryId);
            return category?.Level ?? 0;
        }

        private async Task DeleteDirectoryRecursive(string path)
        {
            // For now, using System.IO directly
            // In production, this should use IFileSystemProvider
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            await Task.CompletedTask;
        }
    }
}