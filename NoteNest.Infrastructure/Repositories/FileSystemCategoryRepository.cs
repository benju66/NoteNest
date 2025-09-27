using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Common;
using NoteNest.Domain.Categories;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Interfaces;

namespace NoteNest.Infrastructure.Repositories
{
    public class FileSystemCategoryRepository : ICategoryRepository
    {
        private readonly IFileSystemProvider _fileSystem;
        private readonly IAppLogger _logger;
        private readonly string _rootPath;

        public FileSystemCategoryRepository(
            IFileSystemProvider fileSystem, 
            IAppLogger logger,
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _fileSystem = fileSystem;
            _logger = logger;
            
            // Get root path from configuration or default
            _rootPath = configuration?.GetSection("NotesPath")?.Value 
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoteNest");
                
            _logger.Info($"FileSystemCategoryRepository initialized with root path: {_rootPath}");
        }

        public async Task<Category> GetByIdAsync(CategoryId id)
        {
            try
            {
                var allCategories = await GetAllCategoriesFromFileSystemAsync();
                return allCategories.FirstOrDefault(c => c.Id.Value == id.Value);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get category by id: {id.Value}");
                return null;
            }
        }

        public async Task<IReadOnlyList<Category>> GetAllAsync()
        {
            try
            {
                return await GetAllCategoriesFromFileSystemAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get all categories");
                return new List<Category>();
            }
        }

        public async Task<IReadOnlyList<Category>> GetRootCategoriesAsync()
        {
            try
            {
                var allCategories = await GetAllCategoriesFromFileSystemAsync();
                return allCategories.Where(c => c.ParentId == null).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get root categories");
                return new List<Category>();
            }
        }

        public async Task<Result> CreateAsync(Category category)
        {
            try
            {
                // Ensure the directory exists
                if (!await _fileSystem.ExistsAsync(category.Path))
                {
                    await _fileSystem.CreateDirectoryAsync(category.Path);
                    _logger.Info($"Created category directory: {category.Path}");
                    return Result.Ok();
                }
                return Result.Fail("Directory already exists");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to create category: {category.Name}");
                return Result.Fail($"Failed to create category: {ex.Message}");
            }
        }

        public async Task<Result> UpdateAsync(Category category)
        {
            // For file system, we typically rename the directory
            // This is a simplified implementation
            _logger.Info($"Updated category: {category.Name}");
            await Task.CompletedTask;
            return Result.Ok();
        }

        public async Task<Result> DeleteAsync(CategoryId id)
        {
            try
            {
                var category = await GetByIdAsync(id);
                if (category == null)
                    return Result.Fail("Category not found");

                if (await _fileSystem.ExistsAsync(category.Path))
                {
                    // TODO: Implement safe directory deletion (check for notes, etc.)
                    _logger.Info($"Would delete category directory: {category.Path}");
                    return Result.Ok();
                }
                return Result.Fail("Directory not found");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to delete category: {id.Value}");
                return Result.Fail($"Failed to delete category: {ex.Message}");
            }
        }

        public async Task<bool> ExistsAsync(CategoryId id)
        {
            var category = await GetByIdAsync(id);
            return category != null;
        }

        private async Task<IReadOnlyList<Category>> GetAllCategoriesFromFileSystemAsync()
        {
            var categories = new List<Category>();
            
            try
            {
                // Ensure root path exists
                if (!await _fileSystem.ExistsAsync(_rootPath))
                {
                    _logger.Warning($"Root path does not exist: {_rootPath}");
                    await _fileSystem.CreateDirectoryAsync(_rootPath);
                    _logger.Info($"Created root directory: {_rootPath}");
                }

                // Recursively scan directories
                await ScanDirectoryAsync(_rootPath, categories, null);
                
                _logger.Info($"Loaded {categories.Count} categories from filesystem");
                return categories;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load categories from filesystem");
                return new List<Category>();
            }
        }

        private async Task ScanDirectoryAsync(string path, List<Category> categories, CategoryId parentId)
        {
            try
            {
                var directories = await _fileSystem.GetDirectoriesAsync(path);
                
                foreach (var directory in directories)
                {
                    // Skip hidden directories and metadata
                    var dirName = Path.GetFileName(directory);
                    if (dirName.StartsWith('.') || dirName.StartsWith('_'))
                        continue;

                    // Create category from directory
                    var categoryId = CategoryId.From(directory); // Use directory path as unique ID
                    var category = new Category(categoryId, dirName, directory, parentId);

                    categories.Add(category);

                    // Recursively scan subdirectories
                    await ScanDirectoryAsync(directory, categories, categoryId);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to scan directory {path}: {ex.Message}");
            }
        }
    }
}
