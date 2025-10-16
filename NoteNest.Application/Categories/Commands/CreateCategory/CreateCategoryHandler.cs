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
        private readonly IEventStore _eventStore;
        private readonly ICategoryRepository _categoryRepository;
        private readonly IFileService _fileService;

        public CreateCategoryHandler(
            IEventStore eventStore,
            ICategoryRepository categoryRepository,
            IFileService fileService)
        {
            _eventStore = eventStore;
            _categoryRepository = categoryRepository;
            _fileService = fileService;
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

            // Events automatically published to projections
            
            return Result.Ok(new CreateCategoryResult
            {
                CategoryId = categoryAggregate.Id.ToString(),
                CategoryPath = categoryAggregate.Path,
                Name = categoryAggregate.Name
            });
        }
    }
}

