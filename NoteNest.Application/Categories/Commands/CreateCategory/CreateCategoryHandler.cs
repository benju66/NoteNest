using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Domain.Common;
using NoteNest.Domain.Categories;
using NoteNest.Application.Common.Interfaces;

namespace NoteNest.Application.Categories.Commands.CreateCategory
{
    /// <summary>
    /// Handler for creating new categories.
    /// Updates both database (tree_nodes) and file system (creates directory).
    /// Follows the same pattern as CreateNoteHandler.
    /// </summary>
    public class CreateCategoryHandler : IRequestHandler<CreateCategoryCommand, Result<CreateCategoryResult>>
    {
        private readonly ICategoryRepository _categoryRepository;
        private readonly IFileService _fileService;
        private readonly IEventBus _eventBus;

        public CreateCategoryHandler(
            ICategoryRepository categoryRepository,
            IFileService fileService,
            IEventBus eventBus)
        {
            _categoryRepository = categoryRepository;
            _fileService = fileService;
            _eventBus = eventBus;
        }

        public async Task<Result<CreateCategoryResult>> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
        {
            // Validate category name
            if (string.IsNullOrWhiteSpace(request.Name))
                return Result.Fail<CreateCategoryResult>("Category name cannot be empty");

            // Get parent category if specified
            Category parentCategory = null;
            string parentPath = null;
            
            if (!string.IsNullOrEmpty(request.ParentCategoryId))
            {
                var parentId = CategoryId.From(request.ParentCategoryId);
                parentCategory = await _categoryRepository.GetByIdAsync(parentId);
                
                if (parentCategory == null)
                    return Result.Fail<CreateCategoryResult>("Parent category not found");
                
                parentPath = parentCategory.Path;
            }
            else
            {
                // Root category - use notes root path
                // This should come from configuration, but we'll use a sensible default
                parentPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NoteNest");
            }

            // Generate category path
            var categoryPath = Path.Combine(parentPath, request.Name);

            // Check for duplicate
            if (await _fileService.DirectoryExistsAsync(categoryPath))
                return Result.Fail<CreateCategoryResult>("A category with this name already exists");

            // Create domain model (use parentCategory?.Id to avoid duplicate variable)
            var category = Category.Create(request.Name, categoryPath, parentCategory?.Id);

            // Save to repository (updates database - tree_nodes table)
            var saveResult = await _categoryRepository.CreateAsync(category);
            if (saveResult.IsFailure)
                return Result.Fail<CreateCategoryResult>(saveResult.Error);

            // Create physical directory
            try
            {
                await _fileService.CreateDirectoryAsync(categoryPath);
            }
            catch (Exception ex)
            {
                // Rollback: Delete from database if directory creation fails
                await _categoryRepository.DeleteAsync(category.Id);
                return Result.Fail<CreateCategoryResult>($"Failed to create directory: {ex.Message}");
            }

            // Note: Category domain model doesn't have DomainEvents yet
            // Event publishing can be added later if needed
            
            return Result.Ok(new CreateCategoryResult
            {
                CategoryId = category.Id.Value,
                CategoryPath = category.Path,
                Name = category.Name
            });
        }
    }
}

