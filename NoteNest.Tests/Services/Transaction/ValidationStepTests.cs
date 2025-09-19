using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using NoteNest.Core.Services.Transaction;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services;

namespace NoteNest.Tests.Services.Transaction
{
    [TestFixture]
    public class ValidationStepTests
    {
        private string _tempTestPath;
        private IAppLogger _logger;
        private MockValidationService _mockValidationService;

        [SetUp]
        public void Setup()
        {
            _tempTestPath = Path.Combine(Path.GetTempPath(), "ValidationStepTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempTestPath);
            _logger = AppLogger.Instance;
            _mockValidationService = new MockValidationService();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempTestPath))
                    Directory.Delete(_tempTestPath, true);
            }
            catch { /* Ignore cleanup errors */ }
        }

        [Test]
        public async Task ExecuteAsync_WithValidPath_ReturnsSuccess()
        {
            // Arrange
            _mockValidationService.SetValidationResult(ValidationResult.Success());
            var step = new ValidationStep(_tempTestPath, StorageMode.Local, _mockValidationService, _logger);

            // Act
            var result = await step.ExecuteAsync();

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(step.State, Is.EqualTo(TransactionStepState.Completed));
            Assert.That(_mockValidationService.ValidateStorageLocationAsyncCalled, Is.True);
        }

        [Test]
        public async Task ExecuteAsync_WithInvalidPath_ReturnsFailure()
        {
            // Arrange
            _mockValidationService.SetValidationResult(ValidationResult.Failed("Invalid storage location"));
            var step = new ValidationStep(_tempTestPath, StorageMode.Local, _mockValidationService, _logger);

            // Act
            var result = await step.ExecuteAsync();

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(step.State, Is.EqualTo(TransactionStepState.Failed));
            Assert.That(result.ErrorMessage, Does.Contain("Validation failed: Invalid storage location"));
        }

        [Test]
        public async Task ExecuteAsync_WithEmptyPath_ReturnsFailure()
        {
            // Arrange
            var step = new ValidationStep("", StorageMode.Local, _mockValidationService, _logger);

            // Act
            var result = await step.ExecuteAsync();

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(step.State, Is.EqualTo(TransactionStepState.Failed));
            Assert.That(result.ErrorMessage, Does.Contain("Storage path cannot be empty"));
        }

        [Test]
        public void Constructor_WithNullPath_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new ValidationStep(null, StorageMode.Local, _mockValidationService, _logger));
        }

        [Test]
        public async Task ExecuteAsync_WithInvalidPathFormat_ReturnsFailure()
        {
            // Arrange
            var invalidPath = "|||invalid|||path|||";
            var step = new ValidationStep(invalidPath, StorageMode.Local, _mockValidationService, _logger);

            // Act
            var result = await step.ExecuteAsync();

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(step.State, Is.EqualTo(TransactionStepState.Failed));
            Assert.That(result.ErrorMessage, Does.Contain("Invalid path format"));
        }

        [Test]
        public void Constructor_WithNullValidationService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new ValidationStep(_tempTestPath, StorageMode.Local, null, _logger));
        }

        [Test]
        public async Task ExecuteAsync_WhenValidationServiceThrows_ReturnsFailure()
        {
            // Arrange
            _mockValidationService.ThrowOnValidation = true;
            var step = new ValidationStep(_tempTestPath, StorageMode.Local, _mockValidationService, _logger);

            // Act
            var result = await step.ExecuteAsync();

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(step.State, Is.EqualTo(TransactionStepState.Failed));
            Assert.That(result.Exception, Is.Not.Null);
        }

        [Test]
        public async Task ExecuteAsync_WithDifferentStorageModes_PassesCorrectMode()
        {
            // Arrange
            _mockValidationService.SetValidationResult(ValidationResult.Success());
            var step = new ValidationStep(_tempTestPath, StorageMode.OneDrive, _mockValidationService, _logger);

            // Act
            await step.ExecuteAsync();

            // Assert
            Assert.That(_mockValidationService.LastValidatedMode, Is.EqualTo(StorageMode.OneDrive));
        }

        [Test]
        public async Task RollbackAsync_Always_ReturnsSuccess()
        {
            // Arrange
            var step = new ValidationStep(_tempTestPath, StorageMode.Local, _mockValidationService, _logger);

            // Act
            var result = await step.RollbackAsync();

            // Assert
            Assert.That(result.Success, Is.True);
        }

        [Test]
        public void CanRollback_Always_ReturnsFalse()
        {
            // Arrange
            var step = new ValidationStep(_tempTestPath, StorageMode.Local, _mockValidationService, _logger);

            // Act & Assert
            Assert.That(step.CanRollback, Is.False);
        }

        [Test]
        public void Description_Always_ContainsPathAndMode()
        {
            // Arrange
            var step = new ValidationStep(_tempTestPath, StorageMode.Custom, _mockValidationService, _logger);

            // Act & Assert
            Assert.That(step.Description, Does.Contain(_tempTestPath));
            Assert.That(step.Description, Does.Contain("Custom"));
        }
    }

    /// <summary>
    /// Mock validation service for testing
    /// </summary>
    public class MockValidationService : IValidationService
    {
        private ValidationResult _validationResult = ValidationResult.Success();
        
        public bool ValidateStorageLocationAsyncCalled { get; private set; }
        public StorageMode LastValidatedMode { get; private set; }
        public string LastValidatedPath { get; private set; }
        public bool ThrowOnValidation { get; set; }

        public void SetValidationResult(ValidationResult result)
        {
            _validationResult = result;
        }

        public async Task<ValidationResult> ValidateStorageLocationAsync(string path, StorageMode mode)
        {
            ValidateStorageLocationAsyncCalled = true;
            LastValidatedPath = path;
            LastValidatedMode = mode;

            if (ThrowOnValidation)
                throw new InvalidOperationException("Mock validation exception");

            await Task.Delay(10); // Simulate async work
            return _validationResult;
        }

        public Task<ValidationResult> ValidateNoteNestDatasetAsync(string path)
        {
            return Task.FromResult(ValidationResult.Success());
        }

        public bool IsValidNoteName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && !name.Contains("\\") && !name.Contains("/");
        }

        public string SanitizeNoteName(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? "Untitled" : name.Replace("\\", "_").Replace("/", "_");
        }
    }
}
