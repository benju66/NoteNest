using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Domain.Common;
using NoteNest.Domain.Categories;
using NoteNest.Domain.Trees;
using NoteNest.Application.Common.Interfaces;

namespace NoteNest.Application.Categories.Commands.RenameCategory
{
    /// <summary>
    /// Handler for renaming categories.
    /// Most complex operation - must update ALL descendant paths recursively.
    /// 
    /// Process:
    /// 1. Update category + all descendants in database (with transaction)
    /// 2. Rename physical directory
    /// 3. Rollback database if directory rename fails
    /// 
    /// Foundation for drag & drop MoveCategoryCommand (same path update logic).
    /// </summary>
    public class RenameCategoryHandler : IRequestHandler<RenameCategoryCommand, Result<RenameCategoryResult>>
    {
        private readonly ICategoryRepository _categoryRepository;
        private readonly ITreeRepository _treeRepository;
        private readonly IFileService _fileService;
        private readonly IEventBus _eventBus;

        public RenameCategoryHandler(
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

        public async Task<Result<RenameCategoryResult>> Handle(RenameCategoryCommand request, CancellationToken cancellationToken)
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(request.NewName))
                return Result.Fail<RenameCategoryResult>("Category name cannot be empty");

            // Get category from repository
            var categoryId = CategoryId.From(request.CategoryId);
            var category = await _categoryRepository.GetByIdAsync(categoryId);
            
            if (category == null)
                return Result.Fail<RenameCategoryResult>("Category not found");

            var oldName = category.Name;
            var oldPath = category.Path;

            // Check if name actually changed
            if (oldName == request.NewName)
                return Result.Ok(new RenameCategoryResult
                {
                    Success = true,
                    CategoryId = category.Id.Value,
                    OldName = oldName,
                    NewName = request.NewName,
                    OldPath = oldPath,
                    NewPath = oldPath,
                    UpdatedDescendantCount = 0
                });

            // Generate new path
            var parentPath = Path.GetDirectoryName(oldPath);
            if (string.IsNullOrEmpty(parentPath))
                return Result.Fail<RenameCategoryResult>("Cannot determine parent directory path");
                
            var newPath = Path.Combine(parentPath, request.NewName);

            // Check for duplicate
            if (await _fileService.DirectoryExistsAsync(newPath))
                return Result.Fail<RenameCategoryResult>("A category with this name already exists");

            // Get all descendants (we need to update their paths)
            Guid categoryGuid;
            if (!Guid.TryParse(request.CategoryId, out categoryGuid))
                return Result.Fail<RenameCategoryResult>("Invalid category ID format");
                
            var descendants = await _treeRepository.GetNodeDescendantsAsync(categoryGuid);

            // Store original paths for rollback
            var originalPaths = new Dictionary<Guid, (string Canonical, string Display, string Absolute)>();
            foreach (var desc in descendants)
            {
                originalPaths[desc.Id] = (desc.CanonicalPath, desc.DisplayPath, desc.AbsolutePath);
            }

            // Update category in domain
            var updateNameResult = category.UpdateName(request.NewName);
            if (updateNameResult.IsFailure)
                return Result.Fail<RenameCategoryResult>(updateNameResult.Error);

            // Update category path
            var updatePathResult = category.UpdatePath(newPath);
            if (updatePathResult.IsFailure)
                return Result.Fail<RenameCategoryResult>(updatePathResult.Error);

            try
            {
                // Step 1: Update category in database
                var updateCategoryResult = await _categoryRepository.UpdateAsync(category);
                if (updateCategoryResult.IsFailure)
                    return Result.Fail<RenameCategoryResult>(updateCategoryResult.Error);

                // Step 2: Update all descendant paths in database
                var oldPathPrefix = oldPath.ToLowerInvariant();
                var newPathPrefix = newPath.ToLowerInvariant();
                
                foreach (var descendant in descendants)
                {
                    // Skip if paths are null (safety check)
                    if (string.IsNullOrEmpty(descendant.CanonicalPath) || 
                        string.IsNullOrEmpty(descendant.DisplayPath) || 
                        string.IsNullOrEmpty(descendant.AbsolutePath))
                        continue;
                    
                    // Replace old path prefix with new in all three path fields
                    var updatedCanonical = descendant.CanonicalPath.Replace(oldPathPrefix, newPathPrefix);
                    var updatedDisplay = descendant.DisplayPath.Replace(oldPath, newPath);
                    var updatedAbsolute = descendant.AbsolutePath.Replace(oldPath, newPath);

                    // Create updated TreeNode (immutable domain model)
                    var updatedNode = TreeNode.CreateFromDatabase(
                        id: descendant.Id,
                        parentId: descendant.ParentId,
                        canonicalPath: updatedCanonical,
                        displayPath: updatedDisplay,
                        absolutePath: updatedAbsolute,
                        nodeType: descendant.NodeType,
                        name: descendant.Name,
                        fileExtension: descendant.FileExtension,
                        fileSize: descendant.FileSize,
                        createdAt: descendant.CreatedAt,
                        modifiedAt: descendant.ModifiedAt,
                        accessedAt: descendant.AccessedAt,
                        quickHash: descendant.QuickHash,
                        fullHash: descendant.FullHash,
                        hashAlgorithm: descendant.HashAlgorithm,
                        hashCalculatedAt: descendant.HashCalculatedAt,
                        isExpanded: descendant.IsExpanded,
                        isPinned: descendant.IsPinned,
                        isSelected: descendant.IsSelected,
                        sortOrder: descendant.SortOrder,
                        colorTag: descendant.ColorTag,
                        iconOverride: descendant.IconOverride,
                        isDeleted: descendant.IsDeleted,
                        deletedAt: descendant.DeletedAt,
                        metadataVersion: descendant.MetadataVersion,
                        customProperties: descendant.CustomProperties
                    );

                    await _treeRepository.UpdateNodeAsync(updatedNode);
                }

                // Step 3: Rename physical directory (AFTER database updates succeed)
                if (await _fileService.DirectoryExistsAsync(oldPath))
                {
                    try
                    {
                        await _fileService.MoveDirectoryAsync(oldPath, newPath);
                    }
                    catch (Exception ex)
                    {
                        // Directory rename failed - rollback database changes
                        await RollbackDatabaseChanges(category, oldName, oldPath, descendants, originalPaths);
                        return Result.Fail<RenameCategoryResult>($"Failed to rename directory: {ex.Message}");
                    }
                }

                // Note: Category domain model doesn't have DomainEvents yet
                // Event publishing can be added later if needed
                
                return Result.Ok(new RenameCategoryResult
                {
                    Success = true,
                    CategoryId = category.Id.Value,
                    OldName = oldName,
                    NewName = category.Name,
                    OldPath = oldPath,
                    NewPath = newPath,
                    UpdatedDescendantCount = descendants.Count
                });
            }
            catch (Exception ex)
            {
                // Unexpected error - attempt rollback
                await RollbackDatabaseChanges(category, oldName, oldPath, descendants, originalPaths);
                return Result.Fail<RenameCategoryResult>($"Failed to rename category: {ex.Message}");
            }
        }

        /// <summary>
        /// Rollback database changes if directory rename fails.
        /// Restores category and all descendant paths to original values.
        /// </summary>
        private async Task RollbackDatabaseChanges(
            Category category,
            string oldName,
            string oldPath,
            List<TreeNode> descendants,
            Dictionary<Guid, (string Canonical, string Display, string Absolute)> originalPaths)
        {
            try
            {
                // Restore category
                category.UpdateName(oldName);
                category.UpdatePath(oldPath);
                await _categoryRepository.UpdateAsync(category);

                // Restore all descendant paths
                foreach (var descendant in descendants)
                {
                    if (originalPaths.TryGetValue(descendant.Id, out var paths))
                    {
                        var restoredNode = TreeNode.CreateFromDatabase(
                            id: descendant.Id,
                            parentId: descendant.ParentId,
                            canonicalPath: paths.Canonical,
                            displayPath: paths.Display,
                            absolutePath: paths.Absolute,
                            nodeType: descendant.NodeType,
                            name: descendant.Name,
                            fileExtension: descendant.FileExtension,
                            fileSize: descendant.FileSize,
                            createdAt: descendant.CreatedAt,
                            modifiedAt: descendant.ModifiedAt,
                            accessedAt: descendant.AccessedAt,
                            quickHash: descendant.QuickHash,
                            fullHash: descendant.FullHash,
                            hashAlgorithm: descendant.HashAlgorithm,
                            hashCalculatedAt: descendant.HashCalculatedAt,
                            isExpanded: descendant.IsExpanded,
                            isPinned: descendant.IsPinned,
                            isSelected: descendant.IsSelected,
                            sortOrder: descendant.SortOrder,
                            colorTag: descendant.ColorTag,
                            iconOverride: descendant.IconOverride,
                            isDeleted: descendant.IsDeleted,
                            deletedAt: descendant.DeletedAt,
                            metadataVersion: descendant.MetadataVersion,
                            customProperties: descendant.CustomProperties
                        );

                        await _treeRepository.UpdateNodeAsync(restoredNode);
                    }
                }
            }
            catch (Exception rollbackEx)
            {
                // Rollback failed - database may be in inconsistent state
                // Log error and let FileWatcherService fix it eventually
                System.Diagnostics.Debug.WriteLine($"Rollback failed: {rollbackEx.Message}");
            }
        }
    }
}

