using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Domain.Common;
using NoteNest.Domain.Categories;
using NoteNest.Application.Common.Interfaces;

namespace NoteNest.Application.Categories.Commands.MoveCategory
{
    /// <summary>
    /// Handler for moving categories within the tree hierarchy.
    /// Event sourcing SIMPLIFIES this - CategoryMoved event handles path updates.
    /// 
    /// Process:
    /// 1. Validate move (circular reference, existence)
    /// 2. Load CategoryAggregate
    /// 3. Call Move() - generates CategoryMoved event
    /// 4. Save to event store
    /// 5. Projection handles all descendant path updates
    /// </summary>
    public class MoveCategoryHandler : IRequestHandler<MoveCategoryCommand, Result<MoveCategoryResult>>
    {
        private readonly IEventStore _eventStore;
        private readonly ICategoryRepository _categoryRepository;
        private readonly ITreeRepository _treeRepository;

        public MoveCategoryHandler(
            IEventStore eventStore,
            ICategoryRepository categoryRepository,
            ITreeRepository treeRepository)
        {
            _eventStore = eventStore;
            _categoryRepository = categoryRepository;
            _treeRepository = treeRepository;
        }

        public async Task<Result<MoveCategoryResult>> Handle(MoveCategoryCommand request, CancellationToken cancellationToken)
        {
            // Validate category exists
            var categoryId = CategoryId.From(request.CategoryId);
            var category = await _categoryRepository.GetByIdAsync(categoryId);
            
            if (category == null)
                return Result.Fail<MoveCategoryResult>("Category not found");

            var oldParentId = category.ParentId?.Value;

            // Check if already in target location
            if (oldParentId == request.NewParentId)
            {
                return Result.Ok(new MoveCategoryResult
                {
                    Success = true,
                    CategoryId = request.CategoryId,
                    CategoryName = category.Name,
                    OldParentId = oldParentId,
                    NewParentId = request.NewParentId,
                    AffectedDescendantCount = 0
                });
            }

            // Validate new parent exists (if not moving to root)
            Guid? newParentGuid = null;
            string newParentPath = null;
            
            if (!string.IsNullOrEmpty(request.NewParentId))
            {
                var newParentId = CategoryId.From(request.NewParentId);
                var newParent = await _categoryRepository.GetByIdAsync(newParentId);
                
                if (newParent == null)
                    return Result.Fail<MoveCategoryResult>("Target parent category not found");

                newParentGuid = Guid.Parse(newParent.Id.Value);
                newParentPath = newParent.Path;
                
                // Validate not moving to self
                if (request.CategoryId == request.NewParentId)
                    return Result.Fail<MoveCategoryResult>("Cannot move category to itself");

                // Validate not moving to own descendant (circular reference)
                Guid categoryGuid = Guid.Parse(request.CategoryId);
                var descendants = await _treeRepository.GetNodeDescendantsAsync(categoryGuid);
                if (descendants.Any(d => d.Id == newParentGuid))
                    return Result.Fail<MoveCategoryResult>("Cannot move category to its own descendant");
            }
            else
            {
                // Moving to root
                newParentPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NoteNest");
            }

            // Calculate new path
            var newPath = System.IO.Path.Combine(newParentPath, category.Name);

            // Load CategoryAggregate
            Guid catGuid = Guid.Parse(category.Id.Value);
            var categoryAggregate = await _eventStore.LoadAsync<NoteNest.Domain.Categories.CategoryAggregate>(catGuid);
            if (categoryAggregate == null)
                return Result.Fail<MoveCategoryResult>("Category aggregate not found");

            // Move category (generates CategoryMoved event with path changes)
            var moveResult = categoryAggregate.Move(newParentGuid, newPath);
            if (moveResult.IsFailure)
                return Result.Fail<MoveCategoryResult>(moveResult.Error);

            // Save to event store
            await _eventStore.SaveAsync(categoryAggregate);

            // Projection automatically updates all descendant paths!
            // No manual cascade needed - this is the beauty of event sourcing

            return Result.Ok(new MoveCategoryResult
            {
                Success = true,
                CategoryId = request.CategoryId,
                CategoryName = category.Name,
                OldParentId = oldParentId,
                NewParentId = request.NewParentId,
                AffectedDescendantCount = 0 // Projection handles this
            });
        }
    }
}
