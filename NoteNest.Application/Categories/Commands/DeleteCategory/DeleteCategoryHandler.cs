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
        private readonly IEventStore _eventStore;
        private readonly ICategoryRepository _categoryRepository;
        private readonly ITreeRepository _treeRepository;
        private readonly IFileService _fileService;

        public DeleteCategoryHandler(
            IEventStore eventStore,
            ICategoryRepository categoryRepository,
            ITreeRepository treeRepository,
            IFileService fileService)
        {
            _eventStore = eventStore;
            _categoryRepository = categoryRepository;
            _treeRepository = treeRepository;
            _fileService = fileService;
        }

        public async Task<Result<DeleteCategoryResult>> Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
        {
            // Get category for path info
            var categoryId = CategoryId.From(request.CategoryId);
            var category = await _categoryRepository.GetByIdAsync(categoryId);
            
            if (category == null)
                return Result.Fail<DeleteCategoryResult>("Category not found");

            // Get descendants count
            Guid categoryGuid;
            if (!Guid.TryParse(request.CategoryId, out categoryGuid))
                return Result.Fail<DeleteCategoryResult>("Invalid category ID format");
                
            var descendants = await _treeRepository.GetNodeDescendantsAsync(categoryGuid);
            var descendantCount = descendants.Count;

            // Load CategoryAggregate and delete
            var categoryAggregate = await _eventStore.LoadAsync<NoteNest.Domain.Categories.CategoryAggregate>(categoryGuid);
            if (categoryAggregate != null)
            {
                categoryAggregate.Delete();
                await _eventStore.SaveAsync(categoryAggregate);
            }
            
            // Delete descendants (TODO: Handle via events in future)
            // For now, projection will handle cascade delete when it sees CategoryDeleted event

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
                    // Directory delete failed but event persisted
                    return Result.Fail<DeleteCategoryResult>(
                        $"Category deleted from event store, but failed to delete directory: {ex.Message}. " +
                        $"You may need to manually delete: {category.Path}");
                }
            }

            // Events automatically published to projections
            
            return Result.Ok(new DeleteCategoryResult
            {
                DeletedCategoryId = category.Id.Value,
                DeletedCategoryName = category.Name,
                DeletedDescendantCount = descendantCount
            });
        }
    }
}

