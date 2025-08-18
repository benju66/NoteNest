using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Interfaces;

namespace NoteNest.Tests.Services
{
    [TestFixture]
    public class NoteServiceTests
    {
        private NoteService _noteService;
        private MockFileSystemProvider _mockFileSystem;
        private ConfigurationService _configService;

        [SetUp]
        public void Setup()
        {
            _mockFileSystem = new MockFileSystemProvider();
            _configService = new ConfigurationService(_mockFileSystem);
            _noteService = new NoteService(_mockFileSystem, _configService);
        }

        [Test]
        public async Task CreateNote_ValidInput_CreatesFileAndReturnsModel()
        {
            // Arrange
            var category = new CategoryModel 
            { 
                Id = "test-category",
                Name = "Test",
                Path = "C:\\Test"
            };
            var title = "Test Note";
            var content = "Test content";

            // Act
            var result = await _noteService.CreateNoteAsync(category, title, content);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Title, Is.EqualTo(title));
            Assert.That(result.Content, Is.EqualTo(content));
            Assert.That(result.FilePath.Contains("Test Note.txt"), Is.True);
        }

        [Test]
        public async Task SaveNote_ValidNote_SavesContent()
        {
            // Arrange
            var note = new NoteModel
            {
                Title = "Test",
                FilePath = "C:\\Test\\note.txt",
                Content = "Updated content"
            };

            // Act
            await _noteService.SaveNoteAsync(note);

            // Assert
            Assert.That(note.IsDirty, Is.False);
            Assert.That(_mockFileSystem.Files["C:\\Test\\note.txt"], Is.EqualTo("Updated content"));
        }
    }

    // Mock implementation for testing
    public class MockFileSystemProvider : IFileSystemProvider
    {
        public Dictionary<string, string> Files { get; } = new Dictionary<string, string>();
        public HashSet<string> Directories { get; } = new HashSet<string>();

        public Task<string> ReadTextAsync(string path)
        {
            return Task.FromResult(Files.ContainsKey(path) ? Files[path] : string.Empty);
        }

        public Task WriteTextAsync(string path, string content)
        {
            Files[path] = content;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string path)
        {
            return Task.FromResult(Files.ContainsKey(path) || Directories.Contains(path));
        }

        public Task DeleteAsync(string path)
        {
            Files.Remove(path);
            Directories.Remove(path);
            return Task.CompletedTask;
        }

        public Task<FileInfo> GetFileInfoAsync(string path)
        {
            return Task.FromResult(new FileInfo(path));
        }

        public Task<IEnumerable<string>> GetFilesAsync(string directory, string searchPattern = "*.*")
        {
            return Task.FromResult(Files.Keys.Where(k => k.StartsWith(directory)));
        }

        public Task<IEnumerable<string>> GetDirectoriesAsync(string directory)
        {
            return Task.FromResult(Directories.Where(d => d.StartsWith(directory)));
        }

        public Task CreateDirectoryAsync(string path)
        {
            Directories.Add(path);
            return Task.CompletedTask;
        }

        public Task<Stream> OpenReadAsync(string path)
        {
            throw new NotImplementedException();
        }

        public Task<Stream> OpenWriteAsync(string path)
        {
            throw new NotImplementedException();
        }

        public Task CopyAsync(string sourcePath, string destinationPath, bool overwrite)
        {
            if (Files.ContainsKey(sourcePath))
            {
                if (!overwrite && Files.ContainsKey(destinationPath))
                    throw new IOException("Destination exists");
                Files[destinationPath] = Files[sourcePath];
            }
            return Task.CompletedTask;
        }

        public Task MoveAsync(string sourcePath, string destinationPath, bool overwrite)
        {
            if (Files.ContainsKey(sourcePath))
            {
                if (!overwrite && Files.ContainsKey(destinationPath))
                    throw new IOException("Destination exists");
                Files[destinationPath] = Files[sourcePath];
                Files.Remove(sourcePath);
            }
            return Task.CompletedTask;
        }

        public Task ReplaceAsync(string sourceFileName, string destinationFileName, string? backupFileName)
        {
            if (Files.ContainsKey(sourceFileName))
            {
                if (!string.IsNullOrEmpty(backupFileName) && Files.ContainsKey(destinationFileName))
                {
                    Files[backupFileName] = Files[destinationFileName];
                }
                Files[destinationFileName] = Files[sourceFileName];
                Files.Remove(sourceFileName);
            }
            return Task.CompletedTask;
        }
    }
}
