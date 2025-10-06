using System;
using System.Threading.Tasks;
using NUnit.Framework;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Implementation;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Tests.Services
{
    [TestFixture]
    public class NoteOperationsServiceTests
    {
        private NoteOperationsService _noteOperationsService;
        private MockFileSystemProvider _mockFileSystem;
        private ConfigurationService _configService;
        private NoteService _noteService;
        private IServiceErrorHandler _errorHandler;
        private IAppLogger _logger;
        private ContentCache _contentCache;
        private MockSaveManager _mockSaveManager;

        [SetUp]
        public void Setup()
        {
            _mockFileSystem = new MockFileSystemProvider();
            _configService = new ConfigurationService(_mockFileSystem, null);
            _logger = new MockLogger();
            var bus = new EventBus();
            // Ensure tests operate under the workspace root
            NoteNest.Core.Services.PathService.RootPath = "C:\\Test";
            _noteService = new NoteService(_mockFileSystem, _configService, _logger, bus);
            // Wire config to bus to capture NoteSavedEvent for recent files
            _configService = new ConfigurationService(_mockFileSystem, bus);
            _errorHandler = new ServiceErrorHandler(_logger, null);
            _contentCache = new ContentCache();
            _mockSaveManager = new MockSaveManager();
            
            _noteOperationsService = new NoteOperationsService(
                _noteService,
                _errorHandler,
                _logger,
                _mockFileSystem,
                _configService,
                _contentCache,
                _mockSaveManager);
        }

        [TearDown]
        public void TearDown()
        {
            _noteService?.Dispose();
            _contentCache?.Dispose();
            _mockSaveManager?.Dispose();
        }

        [Test]
        public async Task CreateNote_ValidInput_CreatesNote()
        {
            // Arrange
            var category = new CategoryModel
            {
                Id = "test-cat",
                Name = "Test Category",
                Path = "C:\\Test"
            };
            var title = "Test Note";

            // Act
            var result = await _noteOperationsService.CreateNoteAsync(category, title);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Title, Is.EqualTo(title));
            Assert.That(result.CategoryId, Is.EqualTo(category.Id));
        }

        [Test]
        public async Task SaveNote_DirtyNote_SavesAndUpdatesRecent()
        {
            // Arrange
            var note = new NoteModel
            {
                Title = "Test",
                FilePath = "C:\\Test\\note.txt",
                Content = "Test content",
                IsDirty = true
            };

            // Act
            await _noteOperationsService.SaveNoteAsync(note);

            // Assert
            Assert.That(note.IsDirty, Is.False);
            Assert.That(_configService.Settings.RecentFiles, Contains.Item(note.FilePath));
        }

        [Test]
        public async Task RenameNote_NewName_UpdatesFileAndModel()
        {
            // Arrange
            var note = new NoteModel
            {
                Title = "OldName",
                FilePath = "C:\\Test\\OldName.txt"
            };
            _mockFileSystem.Files["C:\\Test\\OldName.txt"] = "Content";

            // Act
            var result = await _noteOperationsService.RenameNoteAsync(note, "NewName");

            // Assert
            Assert.That(result, Is.True);
            Assert.That(note.Title, Is.EqualTo("NewName"));
            Assert.That(note.FilePath, Contains.Substring("NewName.txt"));
        }

        private class MockSaveManager : ISaveManager
        {
            public event EventHandler<NoteSavedEventArgs>? NoteSaved;
            public event EventHandler<SaveProgressEventArgs>? SaveStarted;
            public event EventHandler<SaveProgressEventArgs>? SaveCompleted;
            public event EventHandler<ExternalChangeEventArgs>? ExternalChangeDetected;

            public async Task<string> OpenNoteAsync(string filePath) => System.Guid.NewGuid().ToString();
            public void UpdateContent(string noteId, string content) { }
            public void UpdateFilePath(string noteId, string newFilePath) { }
            public async Task<bool> SaveNoteAsync(string noteId) => true;
            public async Task<BatchSaveResult> SaveAllDirtyAsync() => new BatchSaveResult();
            public async Task<bool> CloseNoteAsync(string noteId) => true;
            public bool IsNoteDirty(string noteId) => false;
            public bool IsSaving(string noteId) => false;
            public string GetContent(string noteId) => string.Empty;
            public string? GetLastSavedContent(string noteId) => null;
            public string? GetFilePath(string noteId) => null;
            public string? GetNoteIdForPath(string filePath) => null;
            public IReadOnlyList<string> GetDirtyNoteIds() => new List<string>();
            public async Task<bool> ResolveExternalChangeAsync(string noteId, ConflictResolution resolution) => true;
            public void Dispose() { }
        }
    }
}