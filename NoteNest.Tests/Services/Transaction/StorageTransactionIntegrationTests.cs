using System;
using System.IO;
using System.Linq;
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
    public class StorageTransactionIntegrationTests
    {
        private string _tempBasePath;
        private string _sourceDataPath;
        private string _destDataPath;
        private IAppLogger _logger;
        private IServiceProvider _serviceProvider;
        private StorageTransactionManager _transactionManager;
        private TestDataSetup _testData;

        [SetUp]
        public void Setup()
        {
            var testId = Guid.NewGuid().ToString("N");
            _tempBasePath = Path.Combine(Path.GetTempPath(), "IntegrationTests", testId);
            _sourceDataPath = Path.Combine(_tempBasePath, "SourceData");
            _destDataPath = Path.Combine(_tempBasePath, "DestData");

            Directory.CreateDirectory(_sourceDataPath);
            Directory.CreateDirectory(_destDataPath);

            _logger = AppLogger.Instance;
            _testData = new TestDataSetup(_sourceDataPath);

            // Setup realistic service provider
            var services = new ServiceCollection();
            services.AddSingleton<IAppLogger>(_logger);
            services.AddSingleton<ConfigurationService>(provider => CreateConfigService());
            services.AddSingleton<FileWatcherService>(provider => new MockFileWatcherService());
            services.AddSingleton<IValidationService>(provider => new ValidationService(_logger));
            services.AddSingleton<ISaveManager>(provider => 
            {
                // Create a real SaveManager for the source data path so migration can work
                var logger = provider.GetService<IAppLogger>();
                var statusNotifier = new BasicStatusNotifier(logger);
                return new RTFIntegratedSaveEngine(_sourceDataPath, statusNotifier);
            });
            
            _serviceProvider = services.BuildServiceProvider();

            // Create real components
            var saveManagerFactory = new SaveManagerFactory(_serviceProvider, _logger);
            var validationService = _serviceProvider.GetRequiredService<IValidationService>();

            _transactionManager = new StorageTransactionManager(
                saveManagerFactory,
                validationService,
                _serviceProvider,
                _logger);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                var baseDir = Path.Combine(Path.GetTempPath(), "IntegrationTests");
                if (Directory.Exists(baseDir))
                    Directory.Delete(baseDir, true);
            }
            catch { /* Ignore cleanup errors */ }
            
            (_serviceProvider as IDisposable)?.Dispose();
        }

        [Test]
        public async Task FullStorageLocationChange_WithDataMigration_CompletesSuccessfully()
        {
            // Arrange
            await _testData.CreateSampleDataset();

            var progressReports = new System.Collections.Generic.List<StorageTransactionProgress>();
            var progress = new Progress<StorageTransactionProgress>(p => progressReports.Add(p));

            bool transactionStarted = false;
            bool transactionCompleted = false;

            _transactionManager.TransactionStarted += (s, e) => transactionStarted = true;
            _transactionManager.TransactionCompleted += (s, e) => transactionCompleted = true;

            // Act
            var result = await _transactionManager.ChangeStorageLocationAsync(
                _destDataPath,
                StorageMode.Local,
                keepOriginalData: true,
                progress);

            // Assert - Transaction completed successfully
            Assert.That(result.Success, Is.True, $"Transaction failed: {result.ErrorMessage}");
            Assert.That(result.DataMigrated, Is.True);
            Assert.That(result.NewPath, Is.EqualTo(_destDataPath));
            Assert.That(transactionStarted, Is.True);
            Assert.That(transactionCompleted, Is.True);

            // Assert - Progress was reported
            Assert.That(progressReports.Count, Is.GreaterThan(0));
            Assert.That(progressReports.First().CurrentStep, Is.EqualTo(1));
            Assert.That(progressReports.Last().CurrentStep, Is.EqualTo(8));

            // Assert - Data was migrated correctly
            await _testData.VerifyDataMigrated(_destDataPath);

            // Assert - PathService was updated
            Assert.That(PathService.RootPath, Is.EqualTo(_destDataPath));
        }

        [Test]
        public async Task StorageLocationChange_WithSameLocation_SkipsMigrationButSucceeds()
        {
            // Arrange
            await _testData.CreateSampleDataset();

            // Act - Use same location as current
            var result = await _transactionManager.ChangeStorageLocationAsync(
                _sourceDataPath,
                StorageMode.Local);

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(result.DataMigrated, Is.False);
        }

        [Test]
        public async Task StorageLocationChange_WithInvalidDestination_FailsGracefully()
        {
            // Arrange
            await _testData.CreateSampleDataset();
            var invalidPath = Path.Combine(_tempBasePath, "invalid", "path", "that", "cannot", "be", "created");

            // Make the parent directory read-only to prevent creation
            var parentPath = Path.GetDirectoryName(invalidPath);
            Directory.CreateDirectory(Path.GetDirectoryName(parentPath));

            // Act
            var result = await _transactionManager.ChangeStorageLocationAsync(
                invalidPath,
                StorageMode.Local);

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.Not.Empty);
            
            // Assert - Original data is intact
            await _testData.VerifyDataIntact(_sourceDataPath);
        }

        [Test]
        public async Task StorageLocationChange_WithPartialFailure_RollsBackCompletely()
        {
            // Arrange
            await _testData.CreateSampleDataset();

            // Create a scenario that will cause failure in the middle of transaction
            // We'll create the destination but make it read-only after the first few steps
            var result1 = await _transactionManager.ChangeStorageLocationAsync(
                _destDataPath,
                StorageMode.Local,
                keepOriginalData: true);

            // Verify first transaction succeeded
            Assert.That(result1.Success, Is.True);

            // Now try to change to another location but simulate a failure
            var failPath = Path.Combine(_tempBasePath, "FailPath");
            Directory.CreateDirectory(failPath);

            // This should test rollback by creating an invalid state
            try
            {
                var dirInfo = new DirectoryInfo(failPath);
                dirInfo.Attributes |= FileAttributes.ReadOnly;

                var result2 = await _transactionManager.ChangeStorageLocationAsync(
                    failPath,
                    StorageMode.Local);

                // Assert - Second transaction should fail
                Assert.That(result2.Success, Is.False);

                // Assert - System should be in consistent state (rolled back)
                // The PathService should still point to the successful first location
                Assert.That(PathService.RootPath, Is.EqualTo(_destDataPath));
            }
            finally
            {
                // Cleanup
                try
                {
                    var dirInfo = new DirectoryInfo(failPath);
                    dirInfo.Attributes &= ~FileAttributes.ReadOnly;
                }
                catch { }
            }
        }

        [Test]
        public async Task StorageLocationChange_WithLargeDataset_CompletesInReasonableTime()
        {
            // Arrange
            await _testData.CreateLargeDataset(100); // 100 files

            var startTime = DateTime.UtcNow;

            // Act
            var result = await _transactionManager.ChangeStorageLocationAsync(
                _destDataPath,
                StorageMode.Local);

            var duration = DateTime.UtcNow - startTime;

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(duration, Is.LessThan(TimeSpan.FromMinutes(1)), "Transaction took too long");

            // Verify all files were migrated
            var sourceFiles = Directory.GetFiles(_sourceDataPath, "*", SearchOption.AllDirectories);
            var destFiles = Directory.GetFiles(_destDataPath, "*", SearchOption.AllDirectories);
            
            Assert.That(destFiles.Length, Is.GreaterThanOrEqualTo(sourceFiles.Length));
        }

        [Test]
        public async Task StorageLocationChange_WithDifferentStorageModes_HandlesCorrectly()
        {
            // Arrange
            await _testData.CreateSampleDataset();

            // Test Local to OneDrive mode change (even if path is same)
            var result = await _transactionManager.ChangeStorageLocationAsync(
                _destDataPath,
                StorageMode.OneDrive);

            // Assert
            Assert.That(result.Success, Is.True);
            // The transaction should detect mode change even with same-ish path
        }

        private ConfigurationService CreateConfigService()
        {
            var config = new ConfigurationService();
            config.Settings.DefaultNotePath = _sourceDataPath;
            config.Settings.MetadataPath = Path.Combine(_sourceDataPath, ".metadata");
            return config;
        }
    }

    /// <summary>
    /// Helper class to create test data for integration tests
    /// </summary>
    public class TestDataSetup
    {
        private readonly string _basePath;

        public TestDataSetup(string basePath)
        {
            _basePath = basePath;
        }

        public async Task CreateSampleDataset()
        {
            // Create directory structure
            var metadataDir = Path.Combine(_basePath, ".metadata");
            var notesDir = Path.Combine(_basePath, "Notes");
            var attachmentsDir = Path.Combine(_basePath, "Attachments");
            var templatesDir = Path.Combine(_basePath, "Templates");

            Directory.CreateDirectory(metadataDir);
            Directory.CreateDirectory(notesDir);
            Directory.CreateDirectory(attachmentsDir);
            Directory.CreateDirectory(templatesDir);

            // Create sample categories.json
            var categoriesPath = Path.Combine(metadataDir, "categories.json");
            var categoriesJson = @"[
                {""Id"": ""cat1"", ""Name"": ""Personal"", ""ParentId"": null},
                {""Id"": ""cat2"", ""Name"": ""Work"", ""ParentId"": null},
                {""Id"": ""cat3"", ""Name"": ""Projects"", ""ParentId"": ""cat2""}
            ]";
            await File.WriteAllTextAsync(categoriesPath, categoriesJson);

            // Create sample notes
            await File.WriteAllTextAsync(Path.Combine(notesDir, "note1.rtf"), 
                @"{\rtf1\ansi Sample note 1 content}");
            await File.WriteAllTextAsync(Path.Combine(notesDir, "note2.rtf"), 
                @"{\rtf1\ansi Sample note 2 content}");

            // Create subcategory notes
            var projectsDir = Path.Combine(notesDir, "Projects");
            Directory.CreateDirectory(projectsDir);
            await File.WriteAllTextAsync(Path.Combine(projectsDir, "project1.rtf"), 
                @"{\rtf1\ansi Project 1 notes}");

            // Create sample template
            await File.WriteAllTextAsync(Path.Combine(templatesDir, "meeting.rtf"), 
                @"{\rtf1\ansi Meeting template}");

            // Create sample attachment
            await File.WriteAllTextAsync(Path.Combine(attachmentsDir, "sample.txt"), 
                "Sample attachment content");
        }

        public async Task CreateLargeDataset(int fileCount)
        {
            await CreateSampleDataset(); // Base structure

            var notesDir = Path.Combine(_basePath, "Notes");
            
            // Create many files
            for (int i = 0; i < fileCount; i++)
            {
                var fileName = $"large_note_{i:D4}.rtf";
                var content = $@"{{\rtf1\ansi Large note {i} with content. This is note number {i} of {fileCount}.}}";
                await File.WriteAllTextAsync(Path.Combine(notesDir, fileName), content);
                
                // Create some in subdirectories too
                if (i % 10 == 0)
                {
                    var subDir = Path.Combine(notesDir, $"SubCategory_{i / 10}");
                    Directory.CreateDirectory(subDir);
                    await File.WriteAllTextAsync(Path.Combine(subDir, fileName), content);
                }
            }
        }

        public async Task VerifyDataMigrated(string destinationPath)
        {
            // Verify directory structure exists
            Assert.That(Directory.Exists(Path.Combine(destinationPath, ".metadata")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(destinationPath, "Notes")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(destinationPath, "Attachments")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(destinationPath, "Templates")), Is.True);

            // Verify key files exist
            Assert.That(File.Exists(Path.Combine(destinationPath, ".metadata", "categories.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(destinationPath, "Notes", "note1.rtf")), Is.True);
            Assert.That(File.Exists(Path.Combine(destinationPath, "Notes", "note2.rtf")), Is.True);

            // Verify content is correct
            var categoriesContent = await File.ReadAllTextAsync(
                Path.Combine(destinationPath, ".metadata", "categories.json"));
            Assert.That(categoriesContent, Does.Contain("Personal"));
            Assert.That(categoriesContent, Does.Contain("Work"));

            var note1Content = await File.ReadAllTextAsync(
                Path.Combine(destinationPath, "Notes", "note1.rtf"));
            Assert.That(note1Content, Does.Contain("Sample note 1 content"));
        }

        public async Task VerifyDataIntact(string originalPath)
        {
            // Verify original data still exists and is correct
            Assert.That(Directory.Exists(originalPath), Is.True);
            Assert.That(File.Exists(Path.Combine(originalPath, ".metadata", "categories.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(originalPath, "Notes", "note1.rtf")), Is.True);

            // Verify content hasn't been corrupted
            var note1Content = await File.ReadAllTextAsync(
                Path.Combine(originalPath, "Notes", "note1.rtf"));
            Assert.That(note1Content, Does.Contain("Sample note 1 content"));
        }
    }

}
