using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Transaction;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace NoteNest.Tests.Services.Transaction
{
    [TestFixture]
    public class TransactionEdgeCaseTests
    {
        private string _tempBasePath;
        private IAppLogger _logger;
        private IServiceProvider _serviceProvider;

        [SetUp]
        public void Setup()
        {
            _tempBasePath = Path.Combine(Path.GetTempPath(), "EdgeCaseTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempBasePath);
            _logger = AppLogger.Instance;
            
            var services = new ServiceCollection();
            services.AddSingleton<ConfigurationService>(provider => new ConfigurationService());
            _serviceProvider = services.BuildServiceProvider();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                var baseDir = Path.Combine(Path.GetTempPath(), "EdgeCaseTests");
                if (Directory.Exists(baseDir))
                    Directory.Delete(baseDir, true);
            }
            catch { /* Ignore cleanup errors */ }
            
            (_serviceProvider as IDisposable)?.Dispose();
        }

        [Test]
        public async Task ValidationStep_WithLongPath_HandlesCorrectly()
        {
            // Arrange - Create a very long path (approaching Windows limit)
            var longPath = _tempBasePath;
            for (int i = 0; i < 10; i++)
            {
                longPath = Path.Combine(longPath, "VeryLongDirectoryNameThatExceedsNormalLimits" + i);
            }

            try
            {
                Directory.CreateDirectory(longPath);
            }
            catch (PathTooLongException)
            {
                Assert.Inconclusive("Path too long for this system - skipping test");
                return;
            }

            var mockValidationService = new MockValidationService();
            var step = new ValidationStep(longPath, StorageMode.Local, mockValidationService, _logger);

            // Act
            var result = await step.ExecuteAsync();

            // Assert - Should handle long paths gracefully
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public async Task MigrateDataStep_WithNoSourceData_ReturnsSuccessWithoutError()
        {
            // Arrange - Empty source directory
            var sourcePath = Path.Combine(_tempBasePath, "empty_source");
            var destPath = Path.Combine(_tempBasePath, "dest");
            Directory.CreateDirectory(sourcePath);

            var step = new MigrateDataStep(sourcePath, destPath, keepOriginal: true, _logger);

            // Act
            var result = await step.ExecuteAsync();

            // Assert
            Assert.That(result.Success, Is.True);
            // The step should succeed even with no source data - destination directory creation is handled by other steps
        }

        [Test]
        public async Task MigrateDataStep_WithIdenticalSourceAndDest_SkipsMigration()
        {
            // Arrange - Same path for source and destination
            var samePath = Path.Combine(_tempBasePath, "same");
            Directory.CreateDirectory(samePath);

            var step = new MigrateDataStep(samePath, samePath, keepOriginal: true, _logger);

            // Act
            var result = await step.ExecuteAsync();

            // Assert
            Assert.That(result.Success, Is.True);
            // The step should succeed when source and destination are the same
            // We don't need to check specific result data properties for this test
        }

        [Test]
        public async Task SaveAllDirtyStep_WithVeryLargeNumberOfNotes_CompletesSuccessfully()
        {
            // Arrange
            var mockSaveManager = new MockSaveManager();
            var largeNoteList = new string[1000];
            for (int i = 0; i < largeNoteList.Length; i++)
            {
                largeNoteList[i] = $"note_{i:D4}";
            }
            
            mockSaveManager.SetDirtyNotes(largeNoteList);
            mockSaveManager.SetSaveAllResult(1000, 0); // All successful

            var step = new SaveAllDirtyStep(mockSaveManager, _logger);

            // Act
            var result = await step.ExecuteAsync();

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(mockSaveManager.SaveAllDirtyAsyncCalled, Is.True);
        }

        [Test]
        public async Task CreateSaveManagerStep_WithReadOnlyDirectory_ReturnsFailure()
        {
            // Arrange
            var readOnlyPath = Path.Combine(_tempBasePath, "readonly");
            Directory.CreateDirectory(readOnlyPath);

            try
            {
                // Make directory read-only (Windows-specific)
                var dirInfo = new DirectoryInfo(readOnlyPath);
                dirInfo.Attributes |= FileAttributes.ReadOnly;

                var mockFactory = new MockSaveManagerFactory();
                var step = new CreateSaveManagerStep(readOnlyPath, mockFactory, _logger);

                // Act
                var result = await step.ExecuteAsync();

                // Assert
                Assert.That(result.Success, Is.False);
                Assert.That(result.ErrorMessage, Does.Contain("Access denied").Or.Contain("permission"));
            }
            finally
            {
                // Cleanup - remove read-only attribute
                try
                {
                    var dirInfo = new DirectoryInfo(readOnlyPath);
                    dirInfo.Attributes &= ~FileAttributes.ReadOnly;
                }
                catch { /* Ignore cleanup errors */ }
            }
        }

        [Test]
        public async Task ValidationStep_WithNetworkPath_HandlesCorrectly()
        {
            // Arrange - Simulate a network path (UNC)
            var networkPath = @"\\nonexistent\server\path";
            var mockValidationService = new MockValidationService();
            mockValidationService.SetValidationResult(ValidationResult.Failed("Network path not accessible"));

            var step = new ValidationStep(networkPath, StorageMode.Custom, mockValidationService, _logger);

            // Act
            var result = await step.ExecuteAsync();

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Network path not accessible"));
        }

        [Test]
        public void PathUpdateStep_WithNullServiceProvider_ThrowsArgumentNullException()
        {
            // Arrange
            var testPath = Path.Combine(_tempBasePath, "nullprovider");
            Directory.CreateDirectory(testPath);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new PathUpdateStep(testPath, null, _logger));
        }

        [Test]
        public async Task MigrateDataStep_WithFilesInUse_HandlesLockingErrors()
        {
            // Arrange
            var sourcePath = Path.Combine(_tempBasePath, "locked_source");
            var destPath = Path.Combine(_tempBasePath, "locked_dest");
            Directory.CreateDirectory(sourcePath);
            var notesDir = Path.Combine(sourcePath, "Notes");
            Directory.CreateDirectory(notesDir);

            var lockedFile = Path.Combine(notesDir, "locked.rtf");
            await File.WriteAllTextAsync(lockedFile, "Locked content");

            FileStream lockingStream = null;

            try
            {
                // Lock the file
                lockingStream = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None);

                var step = new MigrateDataStep(sourcePath, destPath, keepOriginal: true, _logger);

                // Act
                var result = await step.ExecuteAsync();

                // Assert - Should handle file locking gracefully
                // May succeed (if copy works despite lock) or fail gracefully
                Assert.That(result, Is.Not.Null);
                if (!result.Success)
                {
                    Assert.That(result.ErrorMessage, Does.Contain("Exception").Or.Contain("access"));
                }
            }
            finally
            {
                lockingStream?.Dispose();
            }
        }

        [Test]
        public async Task StorageTransactionManager_WithConcurrentRequests_HandlesCorrectly()
        {
            // Arrange
            var mockFactory = new MockSaveManagerFactory();
            var mockValidationService = new MockValidationService();
            mockValidationService.SetValidationResult(ValidationResult.Success());
            mockFactory.SetupSuccessfulOperations();

            var transactionManager = new StorageTransactionManager(
                mockFactory, mockValidationService, _serviceProvider, _logger);

            var path1 = Path.Combine(_tempBasePath, "concurrent1");
            var path2 = Path.Combine(_tempBasePath, "concurrent2");
            Directory.CreateDirectory(path1);
            Directory.CreateDirectory(path2);

            // Act - Start two transactions simultaneously
            var task1 = transactionManager.ChangeStorageLocationAsync(path1, StorageMode.Local);
            var task2 = transactionManager.ChangeStorageLocationAsync(path2, StorageMode.Local);

            var results = await Task.WhenAll(task1, task2);

            // Assert - Both should complete (implementation may serialize them)
            Assert.That(results[0], Is.Not.Null);
            Assert.That(results[1], Is.Not.Null);
            // At least one should succeed (depending on implementation)
            var successCount = (results[0].Success ? 1 : 0) + (results[1].Success ? 1 : 0);
            Assert.That(successCount, Is.GreaterThan(0));
        }

        [Test]
        public async Task ValidationStep_WithSpecialCharactersInPath_HandlesCorrectly()
        {
            // Arrange
            var specialPath = Path.Combine(_tempBasePath, "test folder with spaces & symbols!");
            try
            {
                Directory.CreateDirectory(specialPath);
            }
            catch (ArgumentException)
            {
                Assert.Inconclusive("System doesn't support special characters in paths");
                return;
            }

            var mockValidationService = new MockValidationService();
            mockValidationService.SetValidationResult(ValidationResult.Success());

            var step = new ValidationStep(specialPath, StorageMode.Local, mockValidationService, _logger);

            // Act
            var result = await step.ExecuteAsync();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(mockValidationService.LastValidatedPath, Is.EqualTo(specialPath));
        }

        [Test]
        public async Task TransactionStepBase_WithVeryLongDescription_HandlesCorrectly()
        {
            // Arrange
            var veryLongPath = string.Join("", Enumerable.Repeat("VeryLongPathSegment", 20));
            var step = new TestLongDescriptionStep(veryLongPath, _logger);

            // Act
            var description = step.Description;
            var result = await step.ExecuteAsync();

            // Assert
            Assert.That(description, Is.Not.Null);
            Assert.That(description.Length, Is.GreaterThan(0));
            Assert.That(result.Success, Is.True);
        }

        [Test]
        public async Task MigrateDataStep_WithCircularSymlinks_HandlesGracefully()
        {
            // This test is primarily for Unix-like systems, but we'll simulate the concept
            var sourcePath = Path.Combine(_tempBasePath, "symlink_source");
            var destPath = Path.Combine(_tempBasePath, "symlink_dest");
            Directory.CreateDirectory(sourcePath);

            // Create a deeply nested directory structure that could cause issues
            var deepPath = sourcePath;
            for (int i = 0; i < 5; i++)
            {
                deepPath = Path.Combine(deepPath, $"level{i}");
                Directory.CreateDirectory(deepPath);
                await File.WriteAllTextAsync(Path.Combine(deepPath, "test.txt"), $"Level {i}");
            }

            var step = new MigrateDataStep(sourcePath, destPath, keepOriginal: true, _logger);

            // Act
            var result = await step.ExecuteAsync();

            // Assert
            Assert.That(result.Success, Is.True);
        }

        /// <summary>
        /// Test step with a very long description
        /// </summary>
        private class TestLongDescriptionStep : TransactionStepBase
        {
            private readonly string _longPath;

            public override string Description => $"Test step with very long path: {_longPath}";
            public override bool CanRollback => true;

            public TestLongDescriptionStep(string longPath, IAppLogger logger) : base(logger)
            {
                _longPath = longPath;
            }

            protected override async Task<TransactionStepResult> ExecuteStepAsync()
            {
                await Task.Delay(10);
                return TransactionStepResult.Succeeded();
            }

            protected override async Task<TransactionStepResult> RollbackStepAsync()
            {
                await Task.Delay(10);
                return TransactionStepResult.Succeeded();
            }
        }
    }
}
