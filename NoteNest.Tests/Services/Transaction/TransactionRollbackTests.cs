using System;
using System.IO;
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
    public class TransactionRollbackTests
    {
        private string _tempTestPath;
        private IAppLogger _logger;
        private IServiceProvider _serviceProvider;

        [SetUp]
        public void Setup()
        {
            _tempTestPath = Path.Combine(Path.GetTempPath(), "RollbackTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempTestPath);
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
                var baseDir = Path.Combine(Path.GetTempPath(), "RollbackTests");
                if (Directory.Exists(baseDir))
                    Directory.Delete(baseDir, true);
            }
            catch { /* Ignore cleanup errors */ }
            
            (_serviceProvider as IDisposable)?.Dispose();
        }

        [Test]
        public async Task PathUpdateStep_RollbackAfterExecution_RestoresOriginalState()
        {
            // Arrange
            var originalPath = Path.Combine(_tempTestPath, "original");
            var newPath = Path.Combine(_tempTestPath, "new");
            Directory.CreateDirectory(originalPath);
            Directory.CreateDirectory(newPath);

            // Set initial state
            PathService.RootPath = originalPath;
            var originalRootPath = PathService.RootPath;

            var step = new PathUpdateStep(newPath, _serviceProvider, _logger);

            // Act - Execute step
            var executeResult = await step.ExecuteAsync();
            Assert.That(executeResult.Success, Is.True);
            Assert.That(PathService.RootPath, Is.EqualTo(newPath));

            // Act - Rollback step
            var rollbackResult = await step.RollbackAsync();

            // Assert
            Assert.That(rollbackResult.Success, Is.True);
            Assert.That(PathService.RootPath, Is.EqualTo(originalRootPath));
        }

        [Test]
        public async Task CreateSaveManagerStep_RollbackAfterFailure_CleansUpResources()
        {
            // Arrange
            var testPath = Path.Combine(_tempTestPath, "savemanager");
            Directory.CreateDirectory(testPath);

            var mockFactory = new MockSaveManagerFactory();
            var step = new CreateSaveManagerStep(testPath, mockFactory, _logger);

            // Simulate creation failure by creating step with invalid path
            var invalidPath = Path.Combine(_tempTestPath, "nonexistent", "deeply", "nested");
            var failingStep = new CreateSaveManagerStep(invalidPath, mockFactory, _logger);

            // Act
            var result = await failingStep.ExecuteAsync();

            // Assert - Step should fail
            Assert.That(result.Success, Is.False);

            // Act - Rollback should succeed even after failure
            var rollbackResult = await failingStep.RollbackAsync();
            Assert.That(rollbackResult.Success, Is.True);
        }

        [Test]
        public async Task MigrateDataStep_RollbackAfterExecution_RemovesCopiedData()
        {
            // Arrange
            var sourcePath = Path.Combine(_tempTestPath, "source");
            var destPath = Path.Combine(_tempTestPath, "dest");
            Directory.CreateDirectory(sourcePath);
            Directory.CreateDirectory(destPath);

            // Create test files in source
            var sourceFile = Path.Combine(sourcePath, "Notes", "test.rtf");
            Directory.CreateDirectory(Path.GetDirectoryName(sourceFile));
            await File.WriteAllTextAsync(sourceFile, "Test content");

            var step = new MigrateDataStep(sourcePath, destPath, keepOriginal: true, _logger);

            // Act - Execute migration
            var executeResult = await step.ExecuteAsync();
            Assert.That(executeResult.Success, Is.True);

            // Verify files were copied
            var destFile = Path.Combine(destPath, "Notes", "test.rtf");
            Assert.That(File.Exists(destFile), Is.True);

            // Act - Rollback migration
            var rollbackResult = await step.RollbackAsync();

            // Assert - Rollback should succeed and clean up copied files
            Assert.That(rollbackResult.Success, Is.True);
            Assert.That(File.Exists(destFile), Is.False);
        }

        [Test]
        public async Task TransactionStepBase_RollbackInWrongState_ReturnsFailure()
        {
            // Arrange
            var step = new TestableTransactionStep(_logger);

            // Act - Try to rollback without executing first
            var result = await step.RollbackAsync();

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("invalid state"));
        }

        [Test]
        public async Task TransactionStepBase_RollbackAfterRollback_ReturnsFailure()
        {
            // Arrange
            var step = new TestableTransactionStep(_logger);
            await step.ExecuteAsync(); // First execute

            // Act - First rollback
            var firstRollback = await step.RollbackAsync();
            Assert.That(firstRollback.Success, Is.True);

            // Act - Second rollback
            var secondRollback = await step.RollbackAsync();

            // Assert
            Assert.That(secondRollback.Success, Is.False);
            Assert.That(secondRollback.ErrorMessage, Does.Contain("invalid state"));
        }

        [Test]
        public async Task TransactionStepBase_RollbackWhenCanRollbackIsFalse_ReturnsFailure()
        {
            // Arrange
            var step = new NonRollbackableTestStep(_logger);
            await step.ExecuteAsync();

            // Act
            var result = await step.RollbackAsync();

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("cannot be rolled back"));
        }

        [Test]
        public async Task TransactionStepBase_RollbackWithException_ReturnsFailureWithException()
        {
            // Arrange
            var step = new FailingRollbackTestStep(_logger);
            await step.ExecuteAsync();

            // Act
            var result = await step.RollbackAsync();

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(result.Exception, Is.Not.Null);
            Assert.That(step.State, Is.EqualTo(TransactionStepState.RollbackFailed));
        }

        [Test]
        public async Task ValidationStep_RollbackAlways_ReturnsSuccess()
        {
            // Arrange
            var mockValidationService = new MockValidationService();
            mockValidationService.SetValidationResult(ValidationResult.Success());
            var step = new ValidationStep(_tempTestPath, StorageMode.Local, mockValidationService, _logger);

            // Execute first (not required for validation rollback, but for consistency)
            await step.ExecuteAsync();

            // Act
            var result = await step.RollbackAsync();

            // Assert
            Assert.That(result.Success, Is.True);
        }

        /// <summary>
        /// Testable transaction step for rollback testing
        /// </summary>
        private class TestableTransactionStep : TransactionStepBase
        {
            public override string Description => "Test transaction step";
            public override bool CanRollback => true;

            public TestableTransactionStep(IAppLogger logger) : base(logger) { }

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

        /// <summary>
        /// Non-rollbackable test step
        /// </summary>
        private class NonRollbackableTestStep : TransactionStepBase
        {
            public override string Description => "Non-rollbackable test step";
            public override bool CanRollback => false;

            public NonRollbackableTestStep(IAppLogger logger) : base(logger) { }

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

        /// <summary>
        /// Test step that fails during rollback
        /// </summary>
        private class FailingRollbackTestStep : TransactionStepBase
        {
            public override string Description => "Failing rollback test step";
            public override bool CanRollback => true;

            public FailingRollbackTestStep(IAppLogger logger) : base(logger) { }

            protected override async Task<TransactionStepResult> ExecuteStepAsync()
            {
                await Task.Delay(10);
                return TransactionStepResult.Succeeded();
            }

            protected override async Task<TransactionStepResult> RollbackStepAsync()
            {
                await Task.Delay(10);
                throw new InvalidOperationException("Test rollback failure");
            }
        }
    }
}
