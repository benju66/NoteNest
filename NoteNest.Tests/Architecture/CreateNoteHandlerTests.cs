using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Application.Notes.Commands.CreateNote;
using NoteNest.Domain.Categories;
using NoteNest.Domain.Common;
using NoteNest.Domain.Notes;
using NoteNest.Domain.Notes.Events;

namespace NoteNest.Tests.Architecture
{
    [TestFixture]
    public class CreateNoteHandlerTests
    {
        private CreateNoteHandler _handler;
        private Mock<INoteRepository> _noteRepository;
        private Mock<ICategoryRepository> _categoryRepository;
        private Mock<IEventBus> _eventBus;
        private Mock<IFileService> _fileService;

        [SetUp]
        public void Setup()
        {
            _noteRepository = new Mock<INoteRepository>();
            _categoryRepository = new Mock<ICategoryRepository>();
            _eventBus = new Mock<IEventBus>();
            _fileService = new Mock<IFileService>();
            
            _handler = new CreateNoteHandler(
                _noteRepository.Object,
                _categoryRepository.Object,
                _eventBus.Object,
                _fileService.Object);
        }

        [Test]
        public async Task Handle_ValidCommand_CreatesNote()
        {
            // Arrange
            var categoryId = CategoryId.Create();
            var command = new CreateNoteCommand 
            { 
                CategoryId = categoryId.Value, 
                Title = "Test Note",
                InitialContent = "Test content"
            };
            
            var category = new Category(
                categoryId, 
                "Test Category", 
                @"C:\Test\Category",
                null);

            _categoryRepository.Setup(x => x.GetByIdAsync(categoryId))
                .ReturnsAsync(category);
            _noteRepository.Setup(x => x.TitleExistsInCategoryAsync(categoryId, "Test Note", null))
                .ReturnsAsync(false);
            _noteRepository.Setup(x => x.CreateAsync(It.IsAny<Note>()))
                .ReturnsAsync(Result.Ok());
            _fileService.Setup(x => x.GenerateNoteFilePath(category.Path, "Test Note"))
                .Returns(@"C:\Test\Category\Test Note.rtf");

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(result.Value.Title, Is.EqualTo("Test Note"));
            Assert.That(result.Value.FilePath, Is.EqualTo(@"C:\Test\Category\Test Note.rtf"));
            
            _noteRepository.Verify(x => x.CreateAsync(It.IsAny<Note>()), Times.Once);
            _fileService.Verify(x => x.WriteNoteAsync(@"C:\Test\Category\Test Note.rtf", "Test content"), Times.Once);
            _eventBus.Verify(x => x.PublishAsync(It.IsAny<NoteCreatedEvent>()), Times.Once);
        }

        [Test]
        public async Task Handle_CategoryNotFound_ReturnsFailure()
        {
            // Arrange
            var command = new CreateNoteCommand 
            { 
                CategoryId = "non-existent", 
                Title = "Test Note" 
            };

            _categoryRepository.Setup(x => x.GetByIdAsync(It.IsAny<CategoryId>()))
                .ReturnsAsync((Category)null);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo("Category not found"));
            
            _noteRepository.Verify(x => x.CreateAsync(It.IsAny<Note>()), Times.Never);
        }

        [Test]
        public async Task Handle_DuplicateTitle_ReturnsFailure()
        {
            // Arrange
            var categoryId = CategoryId.Create();
            var command = new CreateNoteCommand 
            { 
                CategoryId = categoryId.Value, 
                Title = "Existing Note" 
            };
            
            var category = new Category(
                categoryId, 
                "Test Category", 
                @"C:\Test\Category",
                null);

            _categoryRepository.Setup(x => x.GetByIdAsync(categoryId))
                .ReturnsAsync(category);
            _noteRepository.Setup(x => x.TitleExistsInCategoryAsync(categoryId, "Existing Note", null))
                .ReturnsAsync(true);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Does.Contain("already exists"));
            
            _noteRepository.Verify(x => x.CreateAsync(It.IsAny<Note>()), Times.Never);
        }
    }
}
