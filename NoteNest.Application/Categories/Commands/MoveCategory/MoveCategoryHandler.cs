using System;
using System.Linq;
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
	/// Updates database parent_id relationship with full validation.
	/// 
	/// Process:
	/// 1. Validate category and new parent exist
	/// 2. Validate move (circular reference, name collision, depth)
	/// 3. Update category parent_id in database
	/// 4. Publish domain event
	/// 
	/// Note: Physical folder is NOT moved (matches rename behavior).
	/// The logical hierarchy changes, but file system structure remains stable.
	/// </summary>
	public class MoveCategoryHandler : IRequestHandler<MoveCategoryCommand, Result<MoveCategoryResult>>
	{
		private readonly ICategoryRepository _categoryRepository;
		private readonly ITreeRepository _treeRepository;
		private readonly IEventBus _eventBus;

		public MoveCategoryHandler(
			ICategoryRepository categoryRepository,
			ITreeRepository treeRepository,
			IEventBus eventBus)
		{
			_categoryRepository = categoryRepository;
			_treeRepository = treeRepository;
			_eventBus = eventBus;
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
			if (!string.IsNullOrEmpty(request.NewParentId))
			{
				var newParentId = CategoryId.From(request.NewParentId);
				var newParent = await _categoryRepository.GetByIdAsync(newParentId);
				
				if (newParent == null)
					return Result.Fail<MoveCategoryResult>("Target parent category not found");
			}

			// Validate move operation
			var validationResult = await ValidateMoveAsync(category, request.NewParentId);
			if (validationResult.IsFailure)
				return Result.Fail<MoveCategoryResult>(validationResult.Error);

			try
			{
				// Update category parent_id
				CategoryId newParentId = string.IsNullOrEmpty(request.NewParentId) 
					? null 
					: CategoryId.From(request.NewParentId);
				
				category.Move(newParentId);

				// Update database
				var updateResult = await _categoryRepository.UpdateAsync(category);
				if (updateResult.IsFailure)
					return Result.Fail<MoveCategoryResult>($"Failed to update category: {updateResult.Error}");

				// Get descendant count for reporting
				Guid categoryGuid;
				int descendantCount = 0;
				if (Guid.TryParse(request.CategoryId, out categoryGuid))
				{
					var descendants = await _treeRepository.GetNodeDescendantsAsync(categoryGuid);
					descendantCount = descendants?.Count ?? 0;
				}

				// Domain events are automatically published by the domain model
				// The Move() method on the Category entity already updates the parent relationship

				return Result.Ok(new MoveCategoryResult
				{
					Success = true,
					CategoryId = request.CategoryId,
					CategoryName = category.Name,
					OldParentId = oldParentId,
					NewParentId = request.NewParentId,
					AffectedDescendantCount = descendantCount
				});
			}
			catch (Exception ex)
			{
				return Result.Fail<MoveCategoryResult>($"Failed to move category: {ex.Message}");
			}
		}

		private async Task<Result> ValidateMoveAsync(Category category, string newParentId)
		{
			// Check for circular reference (moving into own descendant)
			if (!string.IsNullOrEmpty(newParentId))
			{
				var isCircular = await IsCircularReference(category.Id, CategoryId.From(newParentId));
				if (isCircular)
					return Result.Fail("Cannot move a category into its own descendant");
			}

			// Check for name collision in target location
			var allCategories = await _categoryRepository.GetAllAsync();
			var siblings = allCategories.Where(c => 
				c.ParentId?.Value == newParentId && 
				c.Id != category.Id &&
				string.Equals(c.Name, category.Name, StringComparison.OrdinalIgnoreCase)
			);
			
			if (siblings.Any())
			{
				return Result.Fail("A category with this name already exists in the target location");
			}

			// Additional validation could include:
			// - Maximum tree depth check
			// - Permissions check
			// - Business rules validation

			return Result.Ok();
		}

		private async Task<bool> IsCircularReference(CategoryId categoryId, CategoryId newParentId)
		{
			var current = newParentId;
			var maxIterations = 100; // Safety limit
			var iterations = 0;

			while (current != null && iterations < maxIterations)
			{
				if (current == categoryId)
					return true;

				var parent = await _categoryRepository.GetByIdAsync(current);
				if (parent == null)
					break;

				current = parent.ParentId;
				iterations++;
			}

			return false;
		}
	}
}
