using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Domain.Common;
using NoteNest.Domain.Notes;
using NoteNest.Domain.Categories;
using NoteNest.Application.Common.Interfaces;

namespace NoteNest.Application.Notes.Commands.MoveNote
{
	/// <summary>
	/// Handler for moving notes between categories.
	/// Updates physical file location, database, and notifies SaveManager.
	/// 
	/// Process:
	/// 1. Validate note and target category exist
	/// 2. Move physical file to new category directory
	/// 3. Update note in database (new path + category)
	/// 4. Notify SaveManager of path change (for open notes)
	/// 5. Publish domain event
	/// </summary>
	public class MoveNoteHandler : IRequestHandler<MoveNoteCommand, Result<MoveNoteResult>>
	{
		private readonly IEventStore _eventStore;
		private readonly ICategoryRepository _categoryRepository;
		private readonly IFileService _fileService;

		public MoveNoteHandler(
			IEventStore eventStore,
			ICategoryRepository categoryRepository,
			IFileService fileService)
		{
			_eventStore = eventStore;
			_categoryRepository = categoryRepository;
			_fileService = fileService;
		}

		public async Task<Result<MoveNoteResult>> Handle(MoveNoteCommand request, CancellationToken cancellationToken)
		{
			// Load note from event store
			var noteId = NoteId.From(request.NoteId);
			var noteGuid = Guid.Parse(noteId.Value);
			var note = await _eventStore.LoadAsync<Note>(noteGuid);
			
			if (note == null)
				return Result.Fail<MoveNoteResult>("Note not found");

			var oldPath = note.FilePath;
			var oldCategoryId = note.CategoryId.Value;

			// Validate target category exists
			var targetCategoryId = CategoryId.From(request.TargetCategoryId);
			var targetCategory = await _categoryRepository.GetByIdAsync(targetCategoryId);
			
			if (targetCategory == null)
				return Result.Fail<MoveNoteResult>("Target category not found");

			// Check if already in target category
			if (note.CategoryId == targetCategoryId)
			{
				return Result.Ok(new MoveNoteResult
				{
					Success = true,
					NoteId = request.NoteId,
					OldCategoryId = oldCategoryId,
					NewCategoryId = request.TargetCategoryId,
					OldPath = oldPath,
					NewPath = oldPath
				});
			}

			try
			{
				// Generate new file path
				var fileName = System.IO.Path.GetFileName(oldPath);
				var newPath = System.IO.Path.Combine(targetCategory.Path, fileName);
				
				// Handle file name collision
				if (await _fileService.FileExistsAsync(newPath))
				{
					var baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);
					var extension = System.IO.Path.GetExtension(fileName);
					int counter = 1;
					
					while (await _fileService.FileExistsAsync(newPath))
					{
						fileName = $"{baseName}_{counter++}{extension}";
						newPath = System.IO.Path.Combine(targetCategory.Path, fileName);
					}
				}

				// Ensure target directory exists
				if (!await _fileService.DirectoryExistsAsync(targetCategory.Path))
				{
					await _fileService.CreateDirectoryAsync(targetCategory.Path);
				}

				// Move physical file
				if (await _fileService.FileExistsAsync(oldPath))
				{
					await _fileService.MoveFileAsync(oldPath, newPath);
				}
				else
				{
					return Result.Fail<MoveNoteResult>($"Source file not found: {oldPath}");
				}

				// Update note domain model
				note.Move(targetCategoryId, newPath);

				// Save to event store (persists move event)
				await _eventStore.SaveAsync(note);
				
				// Events automatically published to projections and UI

				return Result.Ok(new MoveNoteResult
				{
					Success = true,
					NoteId = request.NoteId,
					OldCategoryId = oldCategoryId,
					NewCategoryId = request.TargetCategoryId,
					OldPath = oldPath,
					NewPath = newPath
				});
			}
			catch (Exception ex)
			{
				return Result.Fail<MoveNoteResult>($"Failed to move note: {ex.Message}");
			}
		}
	}
}
