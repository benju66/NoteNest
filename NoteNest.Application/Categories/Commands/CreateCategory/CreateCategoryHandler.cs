using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Domain.Common;
using NoteNest.Domain.Categories;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Application.FolderTags.Repositories;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Application.Categories.Commands.CreateCategory
{
    /// <summary>
    /// Handler for creating new categories.
    /// Updates both database (tree_nodes) and file system (creates directory).
    /// Follows the same pattern as CreateNoteHandler.
    /// </summary>
    public class CreateCategoryHandler : IRequestHandler<CreateCategoryCommand, Result<CreateCategoryResult>>
    {
        private readonly IEventStore _eventStore;
        private readonly ICategoryRepository _categoryRepository;
        private readonly IFileService _fileService;
        private readonly IFolderTagRepository _folderTagRepository;
        private readonly IProjectionOrchestrator _projectionOrchestrator;
        private readonly IAppLogger _logger;

        public CreateCategoryHandler(
            IEventStore eventStore,
            ICategoryRepository categoryRepository,
            IFileService fileService,
            IFolderTagRepository folderTagRepository,
            IProjectionOrchestrator projectionOrchestrator,
            IAppLogger logger)
        {
            _eventStore = eventStore;
            _categoryRepository = categoryRepository;
            _fileService = fileService;
            _folderTagRepository = folderTagRepository;
            _projectionOrchestrator = projectionOrchestrator;
            _logger = logger;
        }

        public async Task<Result<CreateCategoryResult>> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
        {
            // Validate category name
            if (string.IsNullOrWhiteSpace(request.Name))
                return Result.Fail<CreateCategoryResult>("Category name cannot be empty");

            // Get parent category if specified
            Category parentCategory = null;
            string parentPath = null;
            Guid? parentGuid = null;
            
            if (!string.IsNullOrEmpty(request.ParentCategoryId))
            {
                var parentId = CategoryId.From(request.ParentCategoryId);
                parentCategory = await _categoryRepository.GetByIdAsync(parentId);
                
                if (parentCategory == null)
                    return Result.Fail<CreateCategoryResult>("Parent category not found");
                
                parentPath = parentCategory.Path;
                parentGuid = Guid.Parse(parentCategory.Id.Value);
            }
            else
            {
                // Root category - use notes root path
                parentPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NoteNest");
            }

            // Generate category path
            var categoryPath = Path.Combine(parentPath, request.Name);

            // Check for duplicate
            if (await _fileService.DirectoryExistsAsync(categoryPath))
                return Result.Fail<CreateCategoryResult>("A category with this name already exists");

            // Create CategoryAggregate
            var categoryAggregate = NoteNest.Domain.Categories.CategoryAggregate.Create(
                parentGuid,
                request.Name,
                categoryPath);

            // Save to event store (persists CategoryCreated event)
            await _eventStore.SaveAsync(categoryAggregate);

            // Create physical directory
            try
            {
                await _fileService.CreateDirectoryAsync(categoryPath);
            }
            catch (Exception ex)
            {
                // Directory creation failed - event already persisted
                // TODO: Implement compensating transaction or manual cleanup
                return Result.Fail<CreateCategoryResult>($"Failed to create directory: {ex.Message}");
            }

            // ✨ FIX #2: Apply folder tags to newly created category (inherit from parent)
            if (parentGuid.HasValue)
            {
                await ApplyFolderTagsToCategoryAsync(categoryAggregate.Id, parentGuid.Value);
            }
            
            return Result.Ok(new CreateCategoryResult
            {
                CategoryId = categoryAggregate.Id.ToString(),
                CategoryPath = categoryAggregate.Path,
                Name = categoryAggregate.Name
            });
        }
        
        /// <summary>
        /// Apply inherited folder tags to newly created category.
        /// Uses same pattern as CreateNoteHandler.
        /// </summary>
        private async Task ApplyFolderTagsToCategoryAsync(Guid categoryId, Guid parentCategoryId)
        {
            try
            {
                // Get all inherited tags from parent category hierarchy (with deduplication)
                var inheritedTags = await GetInheritedCategoryTagsAsync(parentCategoryId);
                
                if (inheritedTags.Count > 0)
                {
                    _logger.Info($"Applying {inheritedTags.Count} inherited folder tags to category {categoryId}");
                    
                    // Load category aggregate and set tags
                    var category = await _eventStore.LoadAsync<CategoryAggregate>(categoryId);
                    if (category != null)
                    {
                        category.SetTags(inheritedTags);
                        await _eventStore.SaveAsync(category);
                        await _projectionOrchestrator.CatchUpAsync();
                        
                        _logger.Info($"✅ Applied inherited tags to category: {string.Join(", ", inheritedTags)}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to apply folder tags to category {categoryId} (non-fatal)");
                // Don't fail category creation if tag inheritance fails
            }
        }
        
        /// <summary>
        /// Get all inherited tags from category and its ancestors with deduplication.
        /// Walks up the category tree collecting tags where InheritToChildren = true.
        /// </summary>
        private async Task<List<string>> GetInheritedCategoryTagsAsync(Guid categoryId)
        {
            try
            {
                var inheritedTags = await _folderTagRepository.GetInheritedTagsAsync(categoryId);
                return inheritedTags.Select(t => t.Tag).Distinct().ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get inherited tags for category {categoryId}");
                return new List<string>();
            }
        }
    }
}

