using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NoteNest.Core.Services;
using NUnit.Framework;

namespace NoteNest.Tests.Services
{
    [TestFixture]
    public class ContentPersistenceLogTests : IDisposable
    {
        private ContentPersistenceLog _log;
        private string _testDirectory;
        private ConfigurationService _config;

        [SetUp]
        public void Setup()
        {
            // Create a temporary directory for tests
            _testDirectory = Path.Combine(Path.GetTempPath(), $"NoteNestTest_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);

            // Create ConfigurationService (it accepts null parameters)
            _config = new ConfigurationService(null, null);
            
            // Override the log directory by setting environment variable temporarily
            Environment.SetEnvironmentVariable("LOCALAPPDATA", _testDirectory);
            
            _log = new ContentPersistenceLog(_config);
        }

        [TearDown]
        public void TearDown()
        {
            _log?.Dispose();
            
            // Clean up test directory
            if (Directory.Exists(_testDirectory))
            {
                try
                {
                    Directory.Delete(_testDirectory, true);
                }
                catch { }
            }
        }

        [Test]
        public async Task LogChangeAsync_CreatesLogFile()
        {
            // Arrange
            var noteId = "test-note-1";
            var content = "Test content";

            // Act
            await _log.LogChangeAsync(noteId, content);
            
            // Give the background writer time to process
            await Task.Delay(1500);

            // Assert
            var logDir = Path.Combine(_testDirectory, "NoteNest", "persistence");
            Assert.That(Directory.Exists(logDir), Is.True);
            
            var logFiles = Directory.GetFiles(logDir, "persistence-*.log");
            Assert.That(logFiles.Length, Is.GreaterThan(0));
        }

        [Test]
        public async Task RecoverUnpersistedChangesAsync_ReturnsLoggedChanges()
        {
            // Arrange
            var noteId1 = "test-note-1";
            var content1 = "Content 1";
            var noteId2 = "test-note-2";
            var content2 = "Content 2";

            await _log.LogChangeAsync(noteId1, content1);
            await _log.LogChangeAsync(noteId2, content2);
            
            // Give the background writer time to flush
            await Task.Delay(1500);

            // Act
            var recovered = await _log.RecoverUnpersistedChangesAsync();

            // Assert
            Assert.That(recovered.Count, Is.EqualTo(2));
            Assert.That(recovered[noteId1], Is.EqualTo(content1));
            Assert.That(recovered[noteId2], Is.EqualTo(content2));
        }

        [Test]
        public async Task MarkPersistedAsync_RemovesFromRecovery()
        {
            // Arrange
            var noteId = "test-note-1";
            var content = "Test content";

            await _log.LogChangeAsync(noteId, content);
            await Task.Delay(1500); // Let it flush
            
            // Mark as persisted
            await _log.MarkPersistedAsync(noteId);
            await Task.Delay(1500); // Let it flush

            // Act
            var recovered = await _log.RecoverUnpersistedChangesAsync();

            // Assert
            Assert.That(recovered.ContainsKey(noteId), Is.False);
        }

        [Test]
        public async Task MultipleChanges_KeepsLatestContent()
        {
            // Arrange
            var noteId = "test-note-1";
            var content1 = "Version 1";
            var content2 = "Version 2";
            var content3 = "Version 3";

            // Act
            await _log.LogChangeAsync(noteId, content1);
            await _log.LogChangeAsync(noteId, content2);
            await _log.LogChangeAsync(noteId, content3);
            await Task.Delay(1500);

            var recovered = await _log.RecoverUnpersistedChangesAsync();

            // Assert
            Assert.That(recovered[noteId], Is.EqualTo(content3));
        }

        [Test]
        public async Task ClearLogAsync_RemovesAllLogs()
        {
            // Arrange
            await _log.LogChangeAsync("note1", "content1");
            await _log.LogChangeAsync("note2", "content2");
            await Task.Delay(1500);

            // Act
            await _log.ClearLogAsync();
            var recovered = await _log.RecoverUnpersistedChangesAsync();

            // Assert
            Assert.That(recovered.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task EmptyNoteId_DoesNotLog()
        {
            // Act
            await _log.LogChangeAsync("", "content");
            await _log.LogChangeAsync(null, "content");
            await Task.Delay(1500);

            // Assert
            var recovered = await _log.RecoverUnpersistedChangesAsync();
            Assert.That(recovered.Count, Is.EqualTo(0));
        }

        public void Dispose()
        {
            _log?.Dispose();
        }
    }
}
