using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Domain.Common;
using NoteNest.Domain.Notes;
using NoteNest.Domain.Categories;
using NoteNest.Application.Common.Interfaces;

namespace NoteNest.Application.Notes.Commands.CreateNote
{
    public class CreateNoteHandler : IRequestHandler<CreateNoteCommand, Result<CreateNoteResult>>
    {
        private readonly INoteRepository _noteRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly IEventBus _eventBus;
        private readonly IFileService _fileService;

        public CreateNoteHandler(
            INoteRepository noteRepository,
            ICategoryRepository categoryRepository,
            IEventBus eventBus,
            IFileService fileService)
        {
            _noteRepository = noteRepository;
            _categoryRepository = categoryRepository;
            _eventBus = eventBus;
            _fileService = fileService;
        }

        public async Task<Result<CreateNoteResult>> Handle(CreateNoteCommand request, CancellationToken cancellationToken)
        {
            // Validate category exists
            var categoryId = CategoryId.From(request.CategoryId);
            var category = await _categoryRepository.GetByIdAsync(categoryId);
            if (category == null)
                return Result.Fail<CreateNoteResult>("Category not found");

            // Check for duplicate title in category
            if (await _noteRepository.TitleExistsInCategoryAsync(categoryId, request.Title))
                return Result.Fail<CreateNoteResult>($"A note with title '{request.Title}' already exists in this category");

            // Create domain model
            var note = new Note(categoryId, request.Title, request.InitialContent);

            // Generate file path
            var filePath = _fileService.GenerateNoteFilePath(category.Path, request.Title);
            note.SetFilePath(filePath);

            // Save to repository
            var saveResult = await _noteRepository.CreateAsync(note);
            if (saveResult.IsFailure)
                return Result.Fail<CreateNoteResult>(saveResult.Error);

            // Write to file system
            await _fileService.WriteNoteAsync(filePath, request.InitialContent);

            // Publish domain events
            foreach (var domainEvent in note.DomainEvents)
            {
                await _eventBus.PublishAsync(domainEvent);
            }
            note.ClearDomainEvents();

            return Result.Ok(new CreateNoteResult
            {
                NoteId = note.Id.Value,
                FilePath = note.FilePath,
                Title = note.Title
            });
        }
    }
}
