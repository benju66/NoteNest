using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Transaction;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Interfaces.Services;

namespace NoteNest.Tests.Services.Transaction
{
    [TestFixture]
    public class SaveAllDirtyStepTests
    {
        private MockSaveManager _mockSaveManager;
        private IAppLogger _logger;
        private SaveAllDirtyStep _step;

        [SetUp]
        public void Setup()
        {
            _mockSaveManager = new MockSaveManager();
            _logger = AppLogger.Instance;
            _step = new SaveAllDirtyStep(_mockSaveManager, _logger);
        }

        [TearDown]
        public void TearDown()
        {
            _mockSaveManager?.Dispose();
        }

        [Test]
        public async Task ExecuteAsync_WithNoDirtyNotes_ReturnsSuccess()
        {
            // Arrange
            _mockSaveManager.SetDirtyNotes(); // No dirty notes

            // Act
            var result = await _step.ExecuteAsync();

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(_step.State, Is.EqualTo(TransactionStepState.Completed));
        }

        [Test]
        public async Task ExecuteAsync_WithDirtyNotes_SavesAllSuccessfully()
        {
            // Arrange
            _mockSaveManager.SetDirtyNotes("note1", "note2", "note3");
            _mockSaveManager.SetSaveAllResult(3, 0); // 3 success, 0 failures

            // Act
            var result = await _step.ExecuteAsync();

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(_step.State, Is.EqualTo(TransactionStepState.Completed));
            Assert.That(_mockSaveManager.SaveAllDirtyAsyncCalled, Is.True);
        }

        [Test]
        public async Task ExecuteAsync_WithSaveFailures_ReturnsFailure()
        {
            // Arrange
            _mockSaveManager.SetDirtyNotes("note1", "note2", "note3");
            _mockSaveManager.SetSaveAllResult(2, 1, "note3"); // 2 success, 1 failure

            // Act
            var result = await _step.ExecuteAsync();

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(_step.State, Is.EqualTo(TransactionStepState.Failed));
            Assert.That(result.ErrorMessage, Does.Contain("Failed to save 1 out of 3 notes"));
            Assert.That(result.ErrorMessage, Does.Contain("note3"));
        }

        [Test]
        public async Task ExecuteAsync_WhenSaveManagerThrows_ReturnsFailure()
        {
            // Arrange
            _mockSaveManager.SetDirtyNotes("note1");
            _mockSaveManager.ThrowOnSaveAllDirty = true;

            // Act
            var result = await _step.ExecuteAsync();

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(_step.State, Is.EqualTo(TransactionStepState.Failed));
            Assert.That(result.Exception, Is.Not.Null);
        }

        [Test]
        public async Task RollbackAsync_Always_ReturnsSuccess()
        {
            // Arrange - execute the step first
            _mockSaveManager.SetDirtyNotes("note1");
            _mockSaveManager.SetSaveAllResult(1, 0);
            await _step.ExecuteAsync();

            // Act
            var result = await _step.RollbackAsync();

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(_step.CanRollback, Is.False); // Save operations cannot be rolled back
        }

        [Test]
        public void CanRollback_Always_ReturnsFalse()
        {
            // Act & Assert
            Assert.That(_step.CanRollback, Is.False);
        }

        [Test]
        public void Description_Always_ReturnsExpectedDescription()
        {
            // Act & Assert
            Assert.That(_step.Description, Is.EqualTo("Save all dirty notes before location change"));
        }
    }

    /// <summary>
    /// Mock implementation of ISaveManager for testing
    /// </summary>
    public class MockSaveManager : ISaveManager
    {
        private readonly List<string> _dirtyNoteIds = new();
        private BatchSaveResult _saveAllResult = new BatchSaveResult();
        
        public bool SaveAllDirtyAsyncCalled { get; private set; }
        public bool ThrowOnSaveAllDirty { get; set; }

        public void SetDirtyNotes(params string[] noteIds)
        {
            _dirtyNoteIds.Clear();
            _dirtyNoteIds.AddRange(noteIds);
        }

        public void SetSaveAllResult(int successCount, int failureCount, params string[] failedNoteIds)
        {
            _saveAllResult = new BatchSaveResult
            {
                SuccessCount = successCount,
                FailureCount = failureCount,
                FailedNoteIds = failedNoteIds.ToList()
            };
        }

        public async Task<BatchSaveResult> SaveAllDirtyAsync()
        {
            SaveAllDirtyAsyncCalled = true;
            
            if (ThrowOnSaveAllDirty)
                throw new InvalidOperationException("Mock exception for testing");
                
            await Task.Delay(10); // Simulate async work
            return _saveAllResult;
        }

        public IReadOnlyList<string> GetDirtyNoteIds() => _dirtyNoteIds.AsReadOnly();

        // Other ISaveManager methods (not needed for these tests)
        public Task<string> OpenNoteAsync(string filePath) => Task.FromResult("mock-note-id");
        public void UpdateContent(string noteId, string content) { }
        public Task<bool> SaveNoteAsync(string noteId) => Task.FromResult(true);
        public Task<bool> CloseNoteAsync(string noteId) => Task.FromResult(true);
        public bool IsNoteDirty(string noteId) => _dirtyNoteIds.Contains(noteId);
        public bool IsSaving(string noteId) => false;
        public string GetContent(string noteId) => "mock content";
        public string GetLastSavedContent(string noteId) => "mock saved content";
        public string GetFilePath(string noteId) => $"/mock/path/{noteId}.rtf";
        public string GetNoteIdForPath(string filePath) => "mock-note-id";
        public Task<bool> ResolveExternalChangeAsync(string noteId, ConflictResolution resolution) => Task.FromResult(true);
        public void UpdateFilePath(string noteId, string newFilePath) { }

        public event EventHandler<NoteSavedEventArgs> NoteSaved;
        public event EventHandler<SaveProgressEventArgs> SaveStarted;
        public event EventHandler<SaveProgressEventArgs> SaveCompleted;
        public event EventHandler<ExternalChangeEventArgs> ExternalChangeDetected;

        public void Dispose() { }
    }
}
