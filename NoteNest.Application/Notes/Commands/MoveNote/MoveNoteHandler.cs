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
		private readonly INoteRepository _noteRepository;
		private readonly ICategoryRepository _categoryRepository;
		private readonly IFileService _fileService;
		private readonly IEventBus _eventBus;

		public MoveNoteHandler(
			INoteRepository noteRepository,
			ICategoryRepository categoryRepository,
			IFileService fileService,
			IEventBus eventBus)
		{
			_noteRepository = noteRepository;
			_categoryRepository = categoryRepository;
			_fileService = fileService;
			_eventBus = eventBus;
		}

		public async Task<Result<MoveNoteResult>> Handle(MoveNoteCommand request, CancellationToken cancellationToken)
		{
			// Validate note exists
			var noteId = NoteId.From(request.NoteId);
			var note = await _noteRepository.GetByIdAsync(noteId);
			
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

				// Update database
				var updateResult = await _noteRepository.UpdateAsync(note);
				if (updateResult.IsFailure)
				{
					// Rollback: move file back
					try
					{
						if (await _fileService.FileExistsAsync(newPath))
						{
							await _fileService.MoveFileAsync(newPath, oldPath);
						}
					}
					catch { /* Best effort rollback */ }
					
					return Result.Fail<MoveNoteResult>($"Failed to update note in database: {updateResult.Error}");
				}

				// Domain events are automatically published by the domain model
				// The Move() method on the Note entity already adds a domain event

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
