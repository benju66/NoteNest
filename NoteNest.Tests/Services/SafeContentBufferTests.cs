using System;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Services;
using NUnit.Framework;

namespace NoteNest.Tests.Services
{
    [TestFixture]
    public class SafeContentBufferTests
    {
        [Test]
        public void BufferContent_StoresContentCorrectly()
        {
            // Arrange
            var buffer = new SafeContentBuffer();
            var noteId = "test-note-1";
            var content = "This is test content";

            // Act
            buffer.BufferContent(noteId, content);
            var retrieved = buffer.GetLatestContent(noteId);

            // Assert
            Assert.That(retrieved, Is.EqualTo(content));
        }

        [Test]
        public void BufferContent_UpdatesExistingContent()
        {
            // Arrange
            var buffer = new SafeContentBuffer();
            var noteId = "test-note-1";
            var content1 = "Initial content";
            var content2 = "Updated content";

            // Act
            buffer.BufferContent(noteId, content1);
            buffer.BufferContent(noteId, content2);
            var retrieved = buffer.GetLatestContent(noteId);

            // Assert
            Assert.That(retrieved, Is.EqualTo(content2));
        }

        [Test]
        public void GetBufferedContent_ReturnsFullMetadata()
        {
            // Arrange
            var buffer = new SafeContentBuffer();
            var noteId = "test-note-1";
            var content = "Test content with metadata";

            // Act
            buffer.BufferContent(noteId, content);
            var buffered = buffer.GetBufferedContent(noteId);

            // Assert
            Assert.That(buffered, Is.Not.Null);
            Assert.That(buffered.NoteId, Is.EqualTo(noteId));
            Assert.That(buffered.Content, Is.EqualTo(content));
            Assert.That(buffered.UpdateCount, Is.EqualTo(1));
            Assert.That(buffered.Timestamp, Is.GreaterThan(DateTime.MinValue));
        }

        [Test]
        public void BufferContent_IncrementsUpdateCount()
        {
            // Arrange
            var buffer = new SafeContentBuffer();
            var noteId = "test-note-1";

            // Act
            buffer.BufferContent(noteId, "Version 1");
            buffer.BufferContent(noteId, "Version 2");
            buffer.BufferContent(noteId, "Version 3");
            var buffered = buffer.GetBufferedContent(noteId);

            // Assert
            Assert.That(buffered.UpdateCount, Is.EqualTo(3));
        }

        [Test]
        public void BufferContent_DoesNotIncrementForIdenticalContent()
        {
            // Arrange
            var buffer = new SafeContentBuffer();
            var noteId = "test-note-1";
            var content = "Same content";

            // Act
            buffer.BufferContent(noteId, content);
            buffer.BufferContent(noteId, content); // Same content
            buffer.BufferContent(noteId, content); // Same content again
            var buffered = buffer.GetBufferedContent(noteId);

            // Assert
            Assert.That(buffered.UpdateCount, Is.EqualTo(1)); // Should still be 1
        }

        [Test]
        public void ClearBuffer_RemovesContent()
        {
            // Arrange
            var buffer = new SafeContentBuffer();
            var noteId = "test-note-1";
            buffer.BufferContent(noteId, "Content to clear");

            // Act
            buffer.ClearBuffer(noteId);
            var retrieved = buffer.GetLatestContent(noteId);

            // Assert
            Assert.That(retrieved, Is.Null);
        }

        [Test]
        public void GetBufferCount_ReturnsCorrectCount()
        {
            // Arrange
            var buffer = new SafeContentBuffer();

            // Act
            buffer.BufferContent("note1", "Content 1");
            buffer.BufferContent("note2", "Content 2");
            buffer.BufferContent("note3", "Content 3");
            var count = buffer.GetBufferCount();

            // Assert
            Assert.That(count, Is.EqualTo(3));
        }

        [Test]
        public void GetBufferAge_ReturnsApproximateAge()
        {
            // Arrange
            var buffer = new SafeContentBuffer();
            var noteId = "test-note-1";

            // Act
            buffer.BufferContent(noteId, "Aged content");
            Thread.Sleep(100); // Wait 100ms
            var age = buffer.GetBufferAge(noteId);

            // Assert
            Assert.That(age.TotalMilliseconds, Is.GreaterThanOrEqualTo(90)); // Allow for some timing variance
        }

        [Test]
        public void MultipleNotes_MaintainsSeparateBuffers()
        {
            // Arrange
            var buffer = new SafeContentBuffer();
            var note1 = "note-1";
            var note2 = "note-2";
            var content1 = "Content for note 1";
            var content2 = "Content for note 2";

            // Act
            buffer.BufferContent(note1, content1);
            buffer.BufferContent(note2, content2);

            // Assert
            Assert.That(buffer.GetLatestContent(note1), Is.EqualTo(content1));
            Assert.That(buffer.GetLatestContent(note2), Is.EqualTo(content2));
        }

        [Test]
        public void EmptyNoteId_DoesNotBuffer()
        {
            // Arrange
            var buffer = new SafeContentBuffer();

            // Act
            buffer.BufferContent("", "Should not be stored");
            buffer.BufferContent(null, "Should not be stored either");

            // Assert
            Assert.That(buffer.GetBufferCount(), Is.EqualTo(0));
        }

        [Test]
        public async Task ConcurrentUpdates_HandledSafely()
        {
            // Arrange
            var buffer = new SafeContentBuffer();
            var noteId = "concurrent-note";
            var tasks = new Task[10];

            // Act - 10 concurrent updates
            for (int i = 0; i < 10; i++)
            {
                var index = i;
                tasks[i] = Task.Run(() => 
                {
                    buffer.BufferContent(noteId, $"Update {index}");
                });
            }
            await Task.WhenAll(tasks);

            // Assert - Should have the content from one of the updates
            var content = buffer.GetLatestContent(noteId);
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Does.StartWith("Update"));
        }
    }
}
