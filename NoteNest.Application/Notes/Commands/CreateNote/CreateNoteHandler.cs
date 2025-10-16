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
        private readonly IEventStore _eventStore;
        private readonly ICategoryRepository _categoryRepository;
        private readonly IFileService _fileService;

        public CreateNoteHandler(
            IEventStore eventStore,
            ICategoryRepository categoryRepository,
            IFileService fileService)
        {
            _eventStore = eventStore;
            _categoryRepository = categoryRepository;
            _fileService = fileService;
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
            note.SetFilePath(filePath);

            // Save to event store (persists events + publishes to projections)
            await _eventStore.SaveAsync(note);

            // Write to file system (RTF files remain source of truth)
            await _fileService.WriteNoteAsync(filePath, request.InitialContent);

            // Events automatically published to event bus for UI updates
            // Projections automatically updated
            // No manual event publishing needed!

            return Result.Ok(new CreateNoteResult
            {
                NoteId = note.NoteId.Value,
                FilePath = note.FilePath,
                Title = note.Title
            });
        }
    }
}
