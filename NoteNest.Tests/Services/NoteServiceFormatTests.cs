using NUnit.Framework;
using System.Threading.Tasks;
using NoteNest.Core.Services;
using NoteNest.Core.Models;
using NoteNest.Tests.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Tests.Services
{
	[TestFixture]
	public class NoteServiceFormatTests
	{
		[Test]
		public async Task SaveNote_WithConvertEnabled_ConvertsToMd()
		{
			var fs = new SharedMockFileSystemProvider();
			var config = new ConfigurationService(fs);
			await config.LoadSettingsAsync();
			// Align workspace root
			NoteNest.Core.Services.PathService.RootPath = "C:\\Test";
			config.Settings.DefaultNotePath = "C:\\Test";
			config.Settings.ConvertTxtToMdOnSave = true;
			var logger = AppLogger.Instance;
			var md = new MarkdownService(logger);
			var noteService = new NoteService(fs, config, logger, null, md);

			var category = new CategoryModel { Id = "cat1", Name = "Cat", Path = "C:\\Test\\Projects\\Cat" };
			await fs.CreateDirectoryAsync(category.Path);

			var note = await noteService.CreateNoteAsync(category, "Sample", string.Empty);
			// simulate plain text
			note.Format = NoteFormat.PlainText;
			note.Content = "hello";
			note.FilePath = System.IO.Path.Combine(category.Path, "Sample.txt");
			await noteService.SaveNoteAsync(note);

			Assert.That(note.Format, Is.EqualTo(NoteFormat.Markdown));
			Assert.That(note.FilePath.EndsWith(".md"), Is.True);
		}
	}
}


