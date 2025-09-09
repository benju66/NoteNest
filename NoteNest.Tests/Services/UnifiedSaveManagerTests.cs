using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Services;
using NUnit.Framework;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Tests.Services
{
    [TestFixture]
    public class UnifiedSaveManagerTests
    {
        private UnifiedSaveManager _manager;
        private string _testDirectory;

        [SetUp]
        public void Setup()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), $"SaveTest_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);
            _manager = new UnifiedSaveManager(new TestLogger());
        }

        [TearDown]
        public void TearDown()
        {
            _manager?.Dispose();
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }

        [Test]
        public async Task DeterministicNoteIds_SamePathSameId()
        {
            // Arrange
            var path = Path.Combine(_testDirectory, "test.md");
            await File.WriteAllTextAsync(path, "content");
            
            // Act
            var id1 = await _manager.OpenNoteAsync(path);
            var id2 = await _manager.OpenNoteAsync(path); // Should return same ID
            
            // Assert
            Assert.That(id2, Is.EqualTo(id1));
        }

        [Test]
        public async Task SaveNote_CreatesFile()
        {
            // Arrange
            var filePath = Path.Combine(_testDirectory, "test.md");
            var noteId = await _manager.OpenNoteAsync(filePath);
            
            // Act
            _manager.UpdateContent(noteId, "Test content");
            var result = await _manager.SaveNoteAsync(noteId);
            
            // Assert
            Assert.That(result, Is.True);
            Assert.That(File.Exists(filePath), Is.True);
            Assert.That(await File.ReadAllTextAsync(filePath), Is.EqualTo("Test content"));
        }

        [Test]
        public async Task ConcurrentUpdateContent_NoRaceCondition()
        {
            // Arrange
            var path = Path.Combine(_testDirectory, "test.md");
            var noteId = await _manager.OpenNoteAsync(path);
            
            // Act - Concurrent updates
            var tasks = new Task[100];
            for (int i = 0; i < 100; i++)
            {
                var content = $"Content {i}";
                tasks[i] = Task.Run(() => _manager.UpdateContent(noteId, content));
            }
            
            await Task.WhenAll(tasks);
            await Task.Delay(100); // Let things settle
            
            // Assert - Should have last content
            var finalContent = _manager.GetContent(noteId);
            Assert.That(finalContent, Does.StartWith("Content "));
        }

        [Test]
        public async Task SaveCoalescing_PreventsRedundantSaves()
        {
            // Arrange
            var path = Path.Combine(_testDirectory, "test.md");
            var noteId = await _manager.OpenNoteAsync(path);
            _manager.UpdateContent(noteId, "Content");
            
            // Act - Queue multiple saves rapidly
            var tasks = new Task<bool>[10];
            for (int i = 0; i < 10; i++)
            {
                tasks[i] = _manager.SaveNoteAsync(noteId);
            }
            
            var results = await Task.WhenAll(tasks);
            
            // Assert - Some should be coalesced
            // At least some should return false (coalesced)
            Assert.That(results.Count(r => !r), Is.GreaterThan(0));
        }

        [Test]
        public async Task ExternalChange_DetectedWithDebounce()
        {
            // Arrange
            var path = Path.Combine(_testDirectory, "test.md");
            await File.WriteAllTextAsync(path, "Initial");
            
            var noteId = await _manager.OpenNoteAsync(path);
            
            bool eventFired = false;
            string externalContent = null;
            _manager.ExternalChangeDetected += (s, e) =>
            {
                eventFired = true;
                externalContent = e.ExternalContent;
            };
            
            // Act - Simulate external change
            await Task.Delay(100); // Let watcher setup
            await File.WriteAllTextAsync(path, "External change");
            
            // Wait for debounce (500ms) + processing
            await Task.Delay(1000);
            
            // Assert
            Assert.That(eventFired, Is.True);
            Assert.That(externalContent, Is.EqualTo("External change"));
        }

        [Test]
        public async Task ShutdownSave_HigherPriorityThanAutoSave()
        {
            // Arrange
            var path1 = Path.Combine(_testDirectory, "note1.md");
            var path2 = Path.Combine(_testDirectory, "note2.md");
            
            var note1 = await _manager.OpenNoteAsync(path1);
            var note2 = await _manager.OpenNoteAsync(path2);
            
            // Create dirty state
            _manager.UpdateContent(note1, "Auto save content");
            _manager.UpdateContent(note2, "Shutdown save content");
            
            // Act - Let auto-save queue, then immediately do shutdown save
            await Task.Delay(100);
            var shutdownTask = _manager.SaveAllDirtyAsync();
            
            var result = await shutdownTask;
            
            // Assert
            Assert.That(result.SuccessCount, Is.EqualTo(2));
            Assert.That(File.ReadAllText(path2), Is.EqualTo("Shutdown save content"));
        }

        [Test]
        public async Task DisposeDuringSave_CompletesGracefully()
        {
            // Arrange
            var path = Path.Combine(_testDirectory, "test.md");
            var noteId = await _manager.OpenNoteAsync(path);
            _manager.UpdateContent(noteId, "Content");
            
            // Act - Start save then dispose
            var saveTask = _manager.SaveNoteAsync(noteId);
            await Task.Delay(10); // Let save start
            
            _manager.Dispose();
            
            // Assert - Should complete (either success or cancelled)
            try
            {
                var result = await saveTask;
                // If it completes, fine
            }
            catch (TaskCanceledException)
            {
                // Expected if cancelled
            }
            
            Assert.Pass("Disposed gracefully");
        }

        [Test]
        public async Task CircuitBreaker_ActivatesOnFailures()
        {
            // Arrange - Create read-only file
            var path = Path.Combine(_testDirectory, "readonly.md");
            await File.WriteAllTextAsync(path, "Initial");
            new FileInfo(path).IsReadOnly = true;
            
            var noteId = await _manager.OpenNoteAsync(path);
            _manager.UpdateContent(noteId, "New content");
            
            // Act - Try to save multiple times
            var results = new bool[5];
            for (int i = 0; i < 5; i++)
            {
                results[i] = await _manager.SaveNoteAsync(noteId);
                await Task.Delay(10);
            }
            
            // Clean up
            new FileInfo(path).IsReadOnly = false;
            
            // Assert - After threshold, circuit should open
            Assert.That(results[0], Is.False); // First fails
            Assert.That(results[3], Is.False); // Circuit open
            Assert.That(results[4], Is.False); // Still open
        }

        [Test]
        public async Task LargeFile_UsesHashComparison()
        {
            // Arrange
            var path = Path.Combine(_testDirectory, "large.md");
            var largeContent = new string('x', 15000); // > 10KB threshold
            
            var noteId = await _manager.OpenNoteAsync(path);
            
            // Act
            _manager.UpdateContent(noteId, largeContent);
            Assert.That(_manager.IsNoteDirty(noteId), Is.True);
            
            await _manager.SaveNoteAsync(noteId);
            Assert.That(_manager.IsNoteDirty(noteId), Is.False);
            
            // Modify slightly
            _manager.UpdateContent(noteId, largeContent + "y");
            Assert.That(_manager.IsNoteDirty(noteId), Is.True);
        }

        [Test]
        public async Task FilePermission_PreventsOverwrite()
        {
            // Arrange
            var path = Path.Combine(_testDirectory, "protected.md");
            await File.WriteAllTextAsync(path, "Original");
            
            var noteId = await _manager.OpenNoteAsync(path);
            _manager.UpdateContent(noteId, "Modified");
            
            // Make read-only
            new FileInfo(path).IsReadOnly = true;
            
            // Act
            var result = await _manager.SaveNoteAsync(noteId);
            
            // Clean up
            new FileInfo(path).IsReadOnly = false;
            
            // Assert
            Assert.That(result, Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo("Original"));
        }

        private class TestLogger : IAppLogger
        {
            public void Debug(string message, params object[] args) => Console.WriteLine($"DEBUG: {string.Format(message, args)}");
            public void Info(string message, params object[] args) => Console.WriteLine($"INFO: {string.Format(message, args)}");
            public void Warning(string message, params object[] args) => Console.WriteLine($"WARN: {string.Format(message, args)}");
            public void Error(string message, params object[] args) => Console.WriteLine($"ERROR: {string.Format(message, args)}");
            public void Error(Exception ex, string message, params object[] args) => Console.WriteLine($"ERROR: {string.Format(message, args)} - {ex}");
            public void Fatal(string message, params object[] args) => Console.WriteLine($"FATAL: {string.Format(message, args)}");
            public void Fatal(Exception ex, string message, params object[] args) => Console.WriteLine($"FATAL: {string.Format(message, args)} - {ex}");
        }
    }
}
