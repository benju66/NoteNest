using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Domain.Common;
using NoteNest.Domain.Categories;
using NoteNest.Application.Common.Interfaces;

namespace NoteNest.Application.Categories.Commands.DeleteCategory
{
    /// <summary>
    /// Handler for deleting categories.
    /// Soft-deletes category + all descendants in database (for recovery).
    /// Physically deletes directory and all files.
    /// </summary>
    public class DeleteCategoryHandler : IRequestHandler<DeleteCategoryCommand, Result<DeleteCategoryResult>>
    {
        private readonly ICategoryRepository _categoryRepository;
        private readonly ITreeRepository _treeRepository;
        private readonly IFileService _fileService;
        private readonly IEventBus _eventBus;

        public DeleteCategoryHandler(
            ICategoryRepository categoryRepository,
            ITreeRepository treeRepository,
            IFileService fileService,
            IEventBus eventBus)
        {
            _categoryRepository = categoryRepository;
            _treeRepository = treeRepository;
            _fileService = fileService;
            _eventBus = eventBus;
        }

        public async Task<Result<DeleteCategoryResult>> Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
        {
            // Get category from repository
            var categoryId = CategoryId.From(request.CategoryId);
            var category = await _categoryRepository.GetByIdAsync(categoryId);
            
            if (category == null)
                return Result.Fail<DeleteCategoryResult>("Category not found");

            // Get all descendants (for count and database cleanup)
            Guid categoryGuid;
            if (!Guid.TryParse(request.CategoryId, out categoryGuid))
                return Result.Fail<DeleteCategoryResult>("Invalid category ID format");
                
            var descendants = await _treeRepository.GetNodeDescendantsAsync(categoryGuid);
            var descendantCount = descendants.Count;

            // Soft-delete in database (category + all descendants)
            // This allows recovery if needed
            try
            {
                // Soft-delete all descendants first
                foreach (var descendant in descendants)
                {
                    await _treeRepository.DeleteNodeAsync(descendant.Id, softDelete: true);
                }
                
                // Soft-delete the category itself
                var deleteResult = await _categoryRepository.DeleteAsync(categoryId);
                if (deleteResult.IsFailure)
                    return Result.Fail<DeleteCategoryResult>(deleteResult.Error);
            }
            catch (Exception ex)
            {
                return Result.Fail<DeleteCategoryResult>($"Failed to delete category from database: {ex.Message}");
            }

            // Delete physical directory if requested
            if (request.DeleteFiles)
            {
                try
                {
                    if (await _fileService.DirectoryExistsAsync(category.Path))
                    {
                        await _fileService.DeleteDirectoryAsync(category.Path, recursive: true);
                    }
                }
                catch (Exception ex)
                {
                    // Directory delete failed, but database is already updated
                    // This is acceptable - user can manually delete directory
                    // Don't rollback database (files can be orphaned, that's OK)
                    return Result.Fail<DeleteCategoryResult>(
                        $"Category deleted from database, but failed to delete directory: {ex.Message}. " +
                        $"You may need to manually delete: {category.Path}");
                }
            }

            // Note: Category domain model doesn't have DomainEvents yet
            // Event publishing can be added later if needed
            
            return Result.Ok(new DeleteCategoryResult
            {
                DeletedCategoryId = category.Id.Value,
                DeletedCategoryName = category.Name,
                DeletedDescendantCount = descendantCount
            });
        }
    }
}

