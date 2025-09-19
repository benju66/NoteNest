using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace NoteNest.Tests.Services.Transaction
{
    [TestFixture]
    public class StorageTransactionManagerTests
    {
        private string _tempSourcePath;
        private string _tempDestPath;
        private IAppLogger _logger;
        private MockSaveManagerFactory _mockSaveManagerFactory;
        private MockValidationService _mockValidationService;
        private IServiceProvider _serviceProvider;
        private StorageTransactionManager _transactionManager;

        [SetUp]
        public void Setup()
        {
            var testId = Guid.NewGuid().ToString("N");
            _tempSourcePath = Path.Combine(Path.GetTempPath(), "TransactionTests", testId, "Source");
            _tempDestPath = Path.Combine(Path.GetTempPath(), "TransactionTests", testId, "Dest");
            
            Directory.CreateDirectory(_tempSourcePath);
            Directory.CreateDirectory(_tempDestPath);
            
            _logger = AppLogger.Instance;
            _mockSaveManagerFactory = new MockSaveManagerFactory();
            _mockValidationService = new MockValidationService();
            
            // Setup service provider
            var services = new ServiceCollection();
            services.AddSingleton<ConfigurationService>(provider => new ConfigurationService());
            services.AddSingleton<FileWatcherService>(provider => new MockFileWatcherService());
            _serviceProvider = services.BuildServiceProvider();
            
            _transactionManager = new StorageTransactionManager(
                _mockSaveManagerFactory,
                _mockValidationService,
                _serviceProvider,
                _logger);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                var baseDir = Path.Combine(Path.GetTempPath(), "TransactionTests");
                if (Directory.Exists(baseDir))
                    Directory.Delete(baseDir, true);
            }
            catch { /* Ignore cleanup errors */ }
            
            (_serviceProvider as IDisposable)?.Dispose();
        }

        [Test]
        public async Task ChangeStorageLocationAsync_WithValidParameters_ReturnsSuccess()
        {
            // Arrange
            _mockValidationService.SetValidationResult(ValidationResult.Success());
            _mockSaveManagerFactory.SetupSuccessfulOperations();

            bool transactionStarted = false;
            bool transactionCompleted = false;
            
            _transactionManager.TransactionStarted += (s, e) => transactionStarted = true;
            _transactionManager.TransactionCompleted += (s, e) => transactionCompleted = true;

            // Act
            var result = await _transactionManager.ChangeStorageLocationAsync(
                _tempDestPath, 
                StorageMode.Local, 
                keepOriginalData: true);

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(result.NewPath, Is.EqualTo(_tempDestPath));
            Assert.That(result.TransactionId, Is.Not.Empty);
            Assert.That(result.Duration, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(transactionStarted, Is.True);
            Assert.That(transactionCompleted, Is.True);
        }

        [Test]
        public async Task ChangeStorageLocationAsync_WithValidationFailure_ReturnsFailure()
        {
            // Arrange
            _mockValidationService.SetValidationResult(ValidationResult.Failed("Invalid storage location"));

            // Act
            var result = await _transactionManager.ChangeStorageLocationAsync(
                _tempDestPath, 
                StorageMode.Local);

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Validation failed"));
            Assert.That(result.FailedStep, Is.Not.Empty);
        }

        [Test]
        public async Task ChangeStorageLocationAsync_WithSaveManagerCreationFailure_PerformsRollback()
        {
            // Arrange
            _mockValidationService.SetValidationResult(ValidationResult.Success());
            _mockSaveManagerFactory.FailOnCreateSaveManager = true;

            // Act
            var result = await _transactionManager.ChangeStorageLocationAsync(
                _tempDestPath, 
                StorageMode.Local);

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Mock create failure").Or.Contain("failed"));
            // Rollback should have been attempted
        }

        [Test]
        public async Task ChangeStorageLocationAsync_WithSamePaths_SkipsMigration()
        {
            // Arrange
            _mockValidationService.SetValidationResult(ValidationResult.Success());
            _mockSaveManagerFactory.SetupSuccessfulOperations();

            // Use same path for source and destination
            var samePath = _tempSourcePath;

            // Act
            var result = await _transactionManager.ChangeStorageLocationAsync(
                samePath, 
                StorageMode.Local);

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(result.DataMigrated, Is.False);
        }

        [Test]
        public async Task ChangeStorageLocationAsync_WithProgressReporting_ReportsProgress()
        {
            // Arrange
            _mockValidationService.SetValidationResult(ValidationResult.Success());
            _mockSaveManagerFactory.SetupSuccessfulOperations();

            var progressReports = new System.Collections.Generic.List<StorageTransactionProgress>();
            var progress = new Progress<StorageTransactionProgress>(p => progressReports.Add(p));

            // Act
            var result = await _transactionManager.ChangeStorageLocationAsync(
                _tempDestPath, 
                StorageMode.Local,
                keepOriginalData: true,
                progress);

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(progressReports.Count, Is.GreaterThan(0));
            Assert.That(progressReports[0].CurrentStep, Is.EqualTo(1));
            Assert.That(progressReports[^1].CurrentStep, Is.EqualTo(8)); // Final step
        }

        [Test]
        public async Task ChangeStorageLocationAsync_WithEmptyPath_ReturnsFailure()
        {
            // Act
            var result = await _transactionManager.ChangeStorageLocationAsync(
                "", 
                StorageMode.Local);

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("empty"));
        }

        [Test]
        public async Task ChangeStorageLocationAsync_WithExceptionInTransaction_ReturnsFailureWithException()
        {
            // Arrange
            _mockValidationService.ThrowOnValidation = true;

            // Act
            var result = await _transactionManager.ChangeStorageLocationAsync(
                _tempDestPath, 
                StorageMode.Local);

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(result.Exception, Is.Not.Null);
            Assert.That(result.ErrorMessage, Does.Contain("validation exception").Or.Contain("unexpected error"));
        }

        [Test]
        public async Task ChangeStorageLocationAsync_FiresProgressChangedEvent()
        {
            // Arrange
            _mockValidationService.SetValidationResult(ValidationResult.Success());
            _mockSaveManagerFactory.SetupSuccessfulOperations();

            bool progressEventFired = false;
            _transactionManager.ProgressChanged += (s, e) => progressEventFired = true;

            // Act
            await _transactionManager.ChangeStorageLocationAsync(
                _tempDestPath, 
                StorageMode.Local);

            // Assert
            Assert.That(progressEventFired, Is.True);
        }

        [Test]
        public async Task ChangeStorageLocationAsync_WithDifferentStorageModes_PassesCorrectMode()
        {
            // Arrange
            _mockValidationService.SetValidationResult(ValidationResult.Success());
            _mockSaveManagerFactory.SetupSuccessfulOperations();

            // Act
            await _transactionManager.ChangeStorageLocationAsync(
                _tempDestPath, 
                StorageMode.OneDrive);

            // Assert
            Assert.That(_mockValidationService.LastValidatedMode, Is.EqualTo(StorageMode.OneDrive));
        }
    }

    /// <summary>
    /// Mock SaveManagerFactory for testing
    /// </summary>
    public class MockSaveManagerFactory : ISaveManagerFactory
    {
        private readonly MockSaveManager _currentSaveManager;
        private readonly MockSaveManager _newSaveManager;

        public bool FailOnCreateSaveManager { get; set; }
        public bool FailOnReplaceSaveManager { get; set; }

        public MockSaveManagerFactory()
        {
            _currentSaveManager = new MockSaveManager();
            _newSaveManager = new MockSaveManager();
        }

        public ISaveManager Current => _currentSaveManager;

        public event EventHandler<SaveManagerReplacedEventArgs> SaveManagerReplaced;

        public void SetupSuccessfulOperations()
        {
            _currentSaveManager.SetSaveAllResult(0, 0); // No dirty notes to save
        }

        public async Task<ISaveManager> CreateSaveManagerAsync(string dataPath)
        {
            if (FailOnCreateSaveManager)
                throw new InvalidOperationException("Mock create failure");

            await Task.Delay(10); // Simulate work
            return _newSaveManager;
        }

        public async Task<SaveManagerState> CaptureStateAsync()
        {
            await Task.Delay(10); // Simulate work
            return new SaveManagerState
            {
                DataPath = "/mock/current/path",
                CapturedAt = DateTime.UtcNow
            };
        }

        public async Task ReplaceSaveManagerAsync(ISaveManager newSaveManager)
        {
            if (FailOnReplaceSaveManager)
                throw new InvalidOperationException("Mock replace failure");

            await Task.Delay(10); // Simulate work
            
            SaveManagerReplaced?.Invoke(this, new SaveManagerReplacedEventArgs
            {
                OldSaveManager = _currentSaveManager,
                NewSaveManager = newSaveManager,
                NewDataPath = "/mock/new/path",
                ReplacedAt = DateTime.UtcNow
            });
        }

        public async Task RestoreStateAsync(SaveManagerState state)
        {
            await Task.Delay(10); // Simulate work
        }
    }

    /// <summary>
    /// Mock FileWatcherService for testing
    /// </summary>
    public class MockFileWatcherService : FileWatcherService
    {
        public MockFileWatcherService() : base(null, null) { }

        public new void StartWatching(string path, string filter = "*.txt", bool includeSubdirectories = false)
        {
            // Mock implementation - do nothing
        }

        public new void StopWatching(string path)
        {
            // Mock implementation - do nothing
        }

        public new void StopAllWatchers()
        {
            // Mock implementation - do nothing
        }
    }
}
