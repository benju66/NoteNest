using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Domain.Common;
using NoteNest.Domain.Notes;
using NoteNest.Domain.Categories;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Application.Queries;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Application.Notes.Commands.CreateNote
{
    public class CreateNoteHandler : IRequestHandler<CreateNoteCommand, Result<CreateNoteResult>>
    {
        private readonly IEventStore _eventStore;
        private readonly ICategoryRepository _categoryRepository;
        private readonly IFileService _fileService;
        private readonly ITagQueryService _tagQueryService;
        private readonly IProjectionOrchestrator _projectionOrchestrator;
        private readonly IAppLogger _logger;

        public CreateNoteHandler(
            IEventStore eventStore,
            ICategoryRepository categoryRepository,
            IFileService fileService,
            ITagQueryService tagQueryService,
            IProjectionOrchestrator projectionOrchestrator,
            IAppLogger logger)
        {
            _eventStore = eventStore;
            _categoryRepository = categoryRepository;
            _fileService = fileService;
            _tagQueryService = tagQueryService ?? throw new ArgumentNullException(nameof(tagQueryService));
            _projectionOrchestrator = projectionOrchestrator ?? throw new ArgumentNullException(nameof(projectionOrchestrator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<CreateNoteResult>> Handle(CreateNoteCommand request, CancellationToken cancellationToken)
        {
            // Validate category exists
            var categoryId = CategoryId.From(request.CategoryId);
            var category = await _categoryRepository.GetByIdAsync(categoryId);
            if (category == null)
                return Result.Fail<CreateNoteResult>("Category not found");

            // TODO: Check for duplicate title - will need query service
            // For now, skip this check during event sourcing migration

            // Create domain model
            var note = new Note(categoryId, request.Title, request.InitialContent);

            // Generate file path
            var filePath = _fileService.GenerateNoteFilePath(category.Path, request.Title);

            // CRITICAL: Write file BEFORE persisting event (prevents split-brain state)
            try
            {
                await _fileService.WriteNoteAsync(filePath, request.InitialContent);
                // Set FilePath only after successful file write
                note.SetFilePath(filePath);
            }
            catch (System.Exception ex)
            {
                // File write failed - event NOT persisted, no split-brain state
                return Result.Fail<CreateNoteResult>($"Failed to create note file: {ex.Message}");
            }

            // Save to event store ONLY if file operation succeeded (atomic consistency)
            await _eventStore.SaveAsync(note);

            // Apply folder tags to newly created note (non-fatal if fails)
            var noteGuid = Guid.Parse(note.NoteId.Value);
            var categoryGuid = Guid.Parse(categoryId.Value);
            await ApplyFolderTagsToNoteAsync(noteGuid, categoryGuid);

            return Result.Ok(new CreateNoteResult
            {
                NoteId = note.NoteId.Value,
                FilePath = note.FilePath,
                Title = note.Title
            });
        }
        
        /// <summary>
        /// Apply inherited folder tags to newly created note.
        /// Uses same pattern as TodoPlugin's tag inheritance.
        /// </summary>
        private async Task ApplyFolderTagsToNoteAsync(Guid noteId, Guid categoryId)
        {
            try
            {
                // Get all inherited tags from category hierarchy (with deduplication)
                var inheritedTags = await GetInheritedCategoryTagsAsync(categoryId);
                
                if (inheritedTags.Count > 0)
                {
                    _logger.Info($"Applying {inheritedTags.Count} inherited folder tags to note {noteId}");
                    
                    // Load note aggregate and set tags
                    var note = await _eventStore.LoadAsync<Note>(noteId);
                    if (note != null)
                    {
                        note.SetTags(inheritedTags);
                        await _eventStore.SaveAsync(note);
                        await _projectionOrchestrator.CatchUpAsync();
                        
                        _logger.Info($"✅ Applied inherited tags to note: {string.Join(", ", inheritedTags)}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to apply folder tags to note {noteId} (non-fatal)");
                // Don't fail note creation if tag inheritance fails
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
                // Get direct category tags
                var categoryTags = await _tagQueryService.GetTagsForEntityAsync(categoryId, "category");
                
                // Get parent category tags recursively
                var parentTags = await GetParentCategoryTagsRecursiveAsync(categoryId);
                
                // Merge with deduplication using Union (same pattern as TodoPlugin)
                var allTags = categoryTags.Select(t => t.DisplayName)
                    .Union(parentTags, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                
                return allTags;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get inherited tags for category {categoryId}");
                return new List<string>();
            }
        }
        
        /// <summary>
        /// Recursively get tags from parent categories.
        /// Walks up the tree collecting all ancestor tags.
        /// </summary>
        private async Task<List<string>> GetParentCategoryTagsRecursiveAsync(Guid categoryId)
        {
            try
            {
                var allParentTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                // Get parent category ID from repository
                var category = await _categoryRepository.GetByIdAsync(CategoryId.From(categoryId.ToString()));
                if (category?.ParentId == null)
                {
                    return allParentTags.ToList(); // No parent, return empty
                }
                
                var parentId = Guid.Parse(category.ParentId.Value);
                
                // Get parent's tags
                var parentTags = await _tagQueryService.GetTagsForEntityAsync(parentId, "category");
                foreach (var tag in parentTags)
                {
                    allParentTags.Add(tag.DisplayName);
                }
                
                // Recursively get grandparent's tags
                var ancestorTags = await GetParentCategoryTagsRecursiveAsync(parentId);
                foreach (var tag in ancestorTags)
                {
                    allParentTags.Add(tag); // HashSet prevents duplicates
                }
                
                return allParentTags.ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get parent tags for category {categoryId}");
                return new List<string>();
            }
        }
    }
}
