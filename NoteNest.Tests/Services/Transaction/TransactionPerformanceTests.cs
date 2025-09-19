using System;
using System.Diagnostics;
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
    [Category("Performance")]
    public class TransactionPerformanceTests
    {
        private string _tempBasePath;
        private IAppLogger _logger;
        private IServiceProvider _serviceProvider;
        private StorageTransactionManager _transactionManager;

        [SetUp]
        public void Setup()
        {
            var testId = Guid.NewGuid().ToString("N");
            _tempBasePath = Path.Combine(Path.GetTempPath(), "PerformanceTests", testId);
            Directory.CreateDirectory(_tempBasePath);

            _logger = AppLogger.Instance;

            var services = new ServiceCollection();
            services.AddSingleton<IAppLogger>(_logger);
            services.AddSingleton<ConfigurationService>(provider => new ConfigurationService());
            services.AddSingleton<FileWatcherService>(provider => new MockFileWatcherService());
            services.AddSingleton<IValidationService>(provider => new ValidationService(_logger));
            services.AddSingleton<ISaveManager>(provider => new MockSaveManager());
            
            _serviceProvider = services.BuildServiceProvider();

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
                var baseDir = Path.Combine(Path.GetTempPath(), "PerformanceTests");
                if (Directory.Exists(baseDir))
                    Directory.Delete(baseDir, true);
            }
            catch { /* Ignore cleanup errors */ }
            
            (_serviceProvider as IDisposable)?.Dispose();
        }

        [Test]
        [TestCase(100, 500)] // 100 files, max 500ms
        [TestCase(500, 2000)] // 500 files, max 2s
        [TestCase(1000, 5000)] // 1000 files, max 5s
        public async Task StorageLocationChange_WithVariousDatasetSizes_CompletesWithinTimeLimit(
            int fileCount, int maxMilliseconds)
        {
            // Arrange
            var sourcePath = Path.Combine(_tempBasePath, "source");
            var destPath = Path.Combine(_tempBasePath, "dest");
            
            await CreateLargeTestDataset(sourcePath, fileCount);

            var stopwatch = Stopwatch.StartNew();

            // Act
            var result = await _transactionManager.ChangeStorageLocationAsync(
                destPath,
                StorageMode.Local);

            stopwatch.Stop();

            // Assert
            Assert.That(result.Success, Is.True, $"Transaction failed: {result.ErrorMessage}");
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(maxMilliseconds),
                $"Transaction took {stopwatch.ElapsedMilliseconds}ms, expected < {maxMilliseconds}ms for {fileCount} files");

            // Verify all files were migrated
            var sourceFiles = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
            var destFiles = Directory.GetFiles(destPath, "*", SearchOption.AllDirectories);
            Assert.That(destFiles.Length, Is.GreaterThanOrEqualTo(sourceFiles.Length));

            TestContext.WriteLine($"Migrated {fileCount} files in {stopwatch.ElapsedMilliseconds}ms " +
                                $"({stopwatch.ElapsedMilliseconds / (double)fileCount:F2}ms per file)");
        }

        [Test]
        public async Task SaveAllDirtyStep_WithManyDirtyNotes_PerformsWell()
        {
            // Arrange
            var mockSaveManager = new MockSaveManager();
            var noteCount = 1000;
            
            var dirtyNotes = new string[noteCount];
            for (int i = 0; i < noteCount; i++)
            {
                dirtyNotes[i] = $"note_{i:D4}";
            }
            
            mockSaveManager.SetDirtyNotes(dirtyNotes);
            mockSaveManager.SetSaveAllResult(noteCount, 0);

            var step = new SaveAllDirtyStep(mockSaveManager, _logger);
            var stopwatch = Stopwatch.StartNew();

            // Act
            var result = await step.ExecuteAsync();

            stopwatch.Stop();

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(1000), // Should complete in under 1 second
                $"SaveAllDirtyStep took {stopwatch.ElapsedMilliseconds}ms for {noteCount} notes");

            TestContext.WriteLine($"Saved {noteCount} dirty notes in {stopwatch.ElapsedMilliseconds}ms " +
                                $"({stopwatch.ElapsedMilliseconds / (double)noteCount:F2}ms per note)");
        }

        [Test]
        public async Task ValidationStep_WithComplexPath_CompletesQuickly()
        {
            // Arrange
            var complexPath = Path.Combine(_tempBasePath, "very", "deeply", "nested", "path", "structure");
            Directory.CreateDirectory(complexPath);

            var validationService = new ValidationService(_logger);
            var step = new ValidationStep(complexPath, StorageMode.Local, validationService, _logger);
            var stopwatch = Stopwatch.StartNew();

            // Act
            var result = await step.ExecuteAsync();

            stopwatch.Stop();

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(100), // Should be very fast
                $"ValidationStep took {stopwatch.ElapsedMilliseconds}ms for complex path");

            TestContext.WriteLine($"Validated complex path in {stopwatch.ElapsedMilliseconds}ms");
        }

        [Test]
        public async Task TransactionManager_MemoryUsage_RemainsReasonable()
        {
            // Arrange
            var sourcePath = Path.Combine(_tempBasePath, "source");
            var destPath = Path.Combine(_tempBasePath, "dest");
            
            await CreateLargeTestDataset(sourcePath, 100);

            // Measure memory before
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var memoryBefore = GC.GetTotalMemory(false);

            // Act
            var result = await _transactionManager.ChangeStorageLocationAsync(
                destPath,
                StorageMode.Local);

            // Measure memory after
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var memoryAfter = GC.GetTotalMemory(false);
            var memoryUsed = memoryAfter - memoryBefore;

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(memoryUsed, Is.LessThan(50 * 1024 * 1024), // Should use less than 50MB
                $"Transaction used {memoryUsed / 1024 / 1024}MB of memory");

            TestContext.WriteLine($"Memory usage: {memoryUsed / 1024 / 1024}MB for transaction with 100 files");
        }

        [Test]
        public async Task TransactionRollback_Performance_CompletesQuickly()
        {
            // Arrange - Create a scenario that will fail and require rollback
            var sourcePath = Path.Combine(_tempBasePath, "source");
            var invalidDestPath = Path.Combine(_tempBasePath, "invalid", "dest");
            
            await CreateLargeTestDataset(sourcePath, 50);
            
            // Make sure the destination path cannot be created
            var parentPath = Path.GetDirectoryName(invalidDestPath);
            Directory.CreateDirectory(parentPath);
            
            try
            {
                var dirInfo = new DirectoryInfo(parentPath);
                dirInfo.Attributes |= FileAttributes.ReadOnly;

                var stopwatch = Stopwatch.StartNew();

                // Act - This should fail and trigger rollback
                var result = await _transactionManager.ChangeStorageLocationAsync(
                    invalidDestPath,
                    StorageMode.Local);

                stopwatch.Stop();

                // Assert
                Assert.That(result.Success, Is.False); // Should fail
                Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(2000), // Rollback should be quick
                    $"Rollback took {stopwatch.ElapsedMilliseconds}ms");

                TestContext.WriteLine($"Transaction with rollback completed in {stopwatch.ElapsedMilliseconds}ms");
            }
            finally
            {
                // Cleanup
                try
                {
                    var dirInfo = new DirectoryInfo(parentPath);
                    dirInfo.Attributes &= ~FileAttributes.ReadOnly;
                }
                catch { }
            }
        }

        [Test]
        public async Task ConcurrentTransactions_Performance_HandledCorrectly()
        {
            // Arrange
            var path1 = Path.Combine(_tempBasePath, "concurrent1");
            var path2 = Path.Combine(_tempBasePath, "concurrent2");
            var path3 = Path.Combine(_tempBasePath, "concurrent3");
            
            Directory.CreateDirectory(path1);
            Directory.CreateDirectory(path2);
            Directory.CreateDirectory(path3);

            var stopwatch = Stopwatch.StartNew();

            // Act - Start multiple concurrent transactions
            var task1 = _transactionManager.ChangeStorageLocationAsync(path1, StorageMode.Local);
            var task2 = _transactionManager.ChangeStorageLocationAsync(path2, StorageMode.Local);
            var task3 = _transactionManager.ChangeStorageLocationAsync(path3, StorageMode.Local);

            var results = await Task.WhenAll(task1, task2, task3);

            stopwatch.Stop();

            // Assert
            var successCount = results.Count(r => r.Success);
            Assert.That(successCount, Is.GreaterThan(0), "At least one transaction should succeed");
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(5000), // Should handle concurrency efficiently
                $"Concurrent transactions took {stopwatch.ElapsedMilliseconds}ms");

            TestContext.WriteLine($"Concurrent transactions: {successCount}/3 succeeded in {stopwatch.ElapsedMilliseconds}ms");
        }

        private async Task CreateLargeTestDataset(string basePath, int fileCount)
        {
            // Create directory structure
            var metadataDir = Path.Combine(basePath, ".metadata");
            var notesDir = Path.Combine(basePath, "Notes");
            
            Directory.CreateDirectory(metadataDir);
            Directory.CreateDirectory(notesDir);

            // Create categories.json
            await File.WriteAllTextAsync(Path.Combine(metadataDir, "categories.json"), 
                @"[{""Id"": ""cat1"", ""Name"": ""Test Category""}]");

            // Create many note files
            var tasks = new Task[fileCount];
            for (int i = 0; i < fileCount; i++)
            {
                var fileName = $"note_{i:D4}.rtf";
                var content = $@"{{\rtf1\ansi Note {i} with some content for testing performance. This is note number {i} out of {fileCount} total notes.}}";
                
                tasks[i] = File.WriteAllTextAsync(Path.Combine(notesDir, fileName), content);
                
                // Create some in subdirectories for more realistic structure
                if (i % 20 == 0 && i > 0)
                {
                    var subDir = Path.Combine(notesDir, $"SubCategory_{i / 20}");
                    Directory.CreateDirectory(subDir);
                    tasks[i] = File.WriteAllTextAsync(Path.Combine(subDir, fileName), content);
                }
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Mock implementation of ISaveManager for testing
        /// </summary>
        public class MockSaveManager : ISaveManager
        {
            private readonly List<string> _dirtyNoteIds = new();
            private BatchSaveResult _saveAllResult = new BatchSaveResult();
            
            public bool SaveAllDirtyAsyncCalled { get; private set; }
            public bool ThrowOnSaveAllDirty { get; set; }

            public void SetDirtyNotes(params string[] noteIds)
            {
                _dirtyNoteIds.Clear();
                _dirtyNoteIds.AddRange(noteIds);
            }

            public void SetSaveAllResult(int successCount, int failureCount, params string[] failedNoteIds)
            {
                _saveAllResult = new BatchSaveResult
                {
                    SuccessCount = successCount,
                    FailureCount = failureCount,
                    FailedNoteIds = failedNoteIds.ToList()
                };
            }

            public async Task<BatchSaveResult> SaveAllDirtyAsync()
            {
                SaveAllDirtyAsyncCalled = true;
                
                if (ThrowOnSaveAllDirty)
                    throw new InvalidOperationException("Mock exception for testing");
                    
                await Task.Delay(10); // Simulate async work
                return _saveAllResult;
            }

            public IReadOnlyList<string> GetDirtyNoteIds() => _dirtyNoteIds.AsReadOnly();

            // Other ISaveManager methods (not needed for these tests)
            public Task<string> OpenNoteAsync(string filePath) => Task.FromResult("mock-note-id");
            public void UpdateContent(string noteId, string content) { }
            public Task<bool> SaveNoteAsync(string noteId) => Task.FromResult(true);
            public Task<bool> CloseNoteAsync(string noteId) => Task.FromResult(true);
            public bool IsNoteDirty(string noteId) => _dirtyNoteIds.Contains(noteId);
            public bool IsSaving(string noteId) => false;
            public string GetContent(string noteId) => "mock content";
            public string GetLastSavedContent(string noteId) => "mock saved content";
            public string GetFilePath(string noteId) => $"/mock/path/{noteId}.rtf";
            public string GetNoteIdForPath(string filePath) => "mock-note-id";
            public Task<bool> ResolveExternalChangeAsync(string noteId, ConflictResolution resolution) => Task.FromResult(true);
            public void UpdateFilePath(string noteId, string newFilePath) { }

            public event EventHandler<NoteSavedEventArgs> NoteSaved;
            public event EventHandler<SaveProgressEventArgs> SaveStarted;
            public event EventHandler<SaveProgressEventArgs> SaveCompleted;
            public event EventHandler<ExternalChangeEventArgs> ExternalChangeDetected;

            public void Dispose() { }
        }
    }
}
