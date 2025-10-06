using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public class CategoryManagementServiceTests
    {
        private CategoryManagementService _categoryService;
        private MockFileSystemProvider _mockFileSystem;
        private ConfigurationService _configService;
        private NoteService _noteService;
        private IServiceErrorHandler _errorHandler;
        private IAppLogger _logger;

        [SetUp]
        public void Setup()
        {
            _mockFileSystem = new MockFileSystemProvider();
            _configService = new ConfigurationService(_mockFileSystem);
            _logger = new MockLogger();
            _noteService = new NoteService(_mockFileSystem, _configService, _logger);
            _errorHandler = new ServiceErrorHandler(_logger, null);
            
            _categoryService = new CategoryManagementService(
                _noteService,
                _configService,
                _errorHandler,
                _logger,
                _mockFileSystem);
        }

        [TearDown]
        public void TearDown()
        {
            _noteService?.Dispose();
        }

        [Test]
        public async Task CreateCategory_ValidName_CreatesCategory()
        {
            // Arrange
            var categoryName = "Test Category";

            // Act
            var result = await _categoryService.CreateCategoryAsync(categoryName);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo(categoryName));
            Assert.That(result.Id, Is.Not.Null.And.Not.Empty);
            Assert.That(result.ParentId, Is.Null);
        }

        [Test]
        public async Task CreateSubCategory_ValidParent_CreatesSubCategory()
        {
            // Arrange
            var parent = await _categoryService.CreateCategoryAsync("Parent");
            var subName = "SubCategory";

            // Act
            var result = await _categoryService.CreateSubCategoryAsync(parent, subName);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo(subName));
            Assert.That(result.ParentId, Is.EqualTo(parent.Id));
        }

        [Test]
        public async Task DeleteCategory_ExistingCategory_ReturnsTrue()
        {
            // Arrange
            var category = await _categoryService.CreateCategoryAsync("ToDelete");

            // Act
            var result = await _categoryService.DeleteCategoryAsync(category);

            // Assert
            Assert.That(result, Is.True);
            var categories = await _categoryService.LoadCategoriesAsync();
            Assert.That(categories.Any(c => c.Id == category.Id), Is.False);
        }
    }
}