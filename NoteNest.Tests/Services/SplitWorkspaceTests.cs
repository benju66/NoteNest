using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Interfaces.Split;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Implementation;

namespace NoteNest.Tests.Services
{
    [TestFixture]
    public class SplitWorkspaceTests
    {
        private WorkspaceService _workspaceService;

        [SetUp]
        public void Setup()
        {
            var fileSystem = new MockFileSystemProvider();
            var config = new ConfigurationService(fileSystem);
            var noteService = new NoteService(fileSystem, config);
            var logger = new MockLogger();
            var errorHandler = new ServiceErrorHandler(logger);
            var contentCache = new ContentCache();
            var noteOps = new NoteOperationsServiceMock();
            var saveManager = new MockSaveManager();

            _workspaceService = new WorkspaceService(contentCache, noteService, errorHandler, logger, noteOps, saveManager);
        }

        [Test]
        public async Task SplitPane_CreatesNewPane()
        {
            // Arrange
            var initialPane = _workspaceService.Panes.First();

            // Act
            var newPane = await _workspaceService.SplitPaneAsync(initialPane, SplitOrientation.Vertical);

            // Assert
            Assert.That(_workspaceService.Panes.Count, Is.EqualTo(2));
            Assert.That(newPane, Is.Not.Null);
            Assert.That(_workspaceService.Panes.Contains(newPane), Is.True);
        }

        [Test]
        public async Task ClosePane_MovesTabsToRemainingPane()
        {
            // Arrange
            var pane1 = _workspaceService.Panes.First();
            var pane2 = await _workspaceService.SplitPaneAsync(pane1, SplitOrientation.Vertical);
            var testTab = new DummyTabItem(new NoteModel { Title = "Test" });
            pane2.Tabs.Add(testTab);

            // Act
            await _workspaceService.ClosePaneAsync(pane2);

            // Assert
            Assert.That(_workspaceService.Panes.Count, Is.EqualTo(1));
            Assert.That(pane1.Tabs.Contains(testTab), Is.True);
        }

        [Test]
        public void SetActivePane_UpdatesIsActiveProperty()
        {
            // Arrange
            var pane = _workspaceService.Panes.First();

            // Act
            _workspaceService.ActivePane = pane;

            // Assert
            Assert.That(pane.IsActive, Is.True);
        }

        private class MockSaveManager : ISaveManager
        {
            public event EventHandler<NoteSavedEventArgs> NoteSaved;
            public event EventHandler<ExternalChangeEventArgs> ExternalChangeDetected;

            public async Task<string> OpenNoteAsync(string filePath) => System.Guid.NewGuid().ToString();
            public void UpdateContent(string noteId, string content) { }
            public async Task<bool> SaveNoteAsync(string noteId) => true;
            public async Task<BatchSaveResult> SaveAllDirtyAsync() => new BatchSaveResult();
            public bool IsNoteDirty(string noteId) => false;
            public string GetContent(string noteId) => "";
            public string GetFilePath(string noteId) => "";
            public IReadOnlyList<string> GetDirtyNoteIds() => new List<string>();
            public void CloseNote(string noteId) { }
            public void Dispose() { }
        }

        private class DummyTabItem : ITabItem
        {
            public DummyTabItem(NoteModel note)
            {
                Note = note;
                Id = System.Guid.NewGuid().ToString();
                NoteId = note.Id;
                Content = note.Content ?? string.Empty;
            }

            public string Id { get; }
            public string Title => Note?.Title ?? "Untitled";
            public NoteModel Note { get; }
            public bool IsDirty { get; set; }
            public string Content { get; set; }
            public string NoteId { get; }
        }

        private class NoteOperationsServiceMock : INoteOperationsService
        {
            public void ClearTrackedNotes() { }

            public Task<bool> MoveNoteAsync(NoteModel note, CategoryModel targetCategory)
            {
                return Task.FromResult(true);
            }

            public Task<NoteModel> CreateNoteAsync(CategoryModel category, string title, string content = "")
            {
                return Task.FromResult(new NoteModel { Title = title, Content = content });
            }

            public Task DeleteNoteAsync(NoteModel note)
            {
                return Task.CompletedTask;
            }

            // Removed SaveAllNotesAsync from interface

            public Task SaveNoteAsync(NoteModel note)
            {
                return Task.CompletedTask;
            }

            public Task<bool> RenameNoteAsync(NoteModel note, string newName)
            {
                note.Title = newName;
                return Task.FromResult(true);
            }

            public void TrackOpenNote(NoteModel note) { }
            public void UntrackOpenNote(NoteModel note) { }
        }
    }
}


