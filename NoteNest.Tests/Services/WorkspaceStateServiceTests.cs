using System;
using System.Threading.Tasks;
using NUnit.Framework;
using NoteNest.Core.Models;
using NoteNest.Core.Services;

namespace NoteNest.Tests.Services
{
	[TestFixture]
	public class WorkspaceStateServiceTests
	{
		[Test]
		public async Task SaveNoteAsync_WritesLatestStateContent()
		{
			// Arrange
			var fs = new SharedMockFileSystemProvider();
			var config = new ConfigurationService(fs);
			var noteService = new NoteService(fs, config);
			var state = new WorkspaceStateService(noteService);
			var note = new NoteModel
			{
				Id = Guid.NewGuid().ToString(),
				Title = "Test",
				FilePath = "C:\\Test\\state.txt",
				Content = "original"
			};
			await state.OpenNoteAsync(note);
			state.UpdateNoteContent(note.Id, "edited in editor");

			// Act
			var result = await state.SaveNoteAsync(note.Id);

			// Assert
			Assert.That(result.Success, Is.True);
			Assert.That(fs.Files["C:\\Test\\state.txt"], Is.EqualTo("edited in editor"));
		}
	}
}
