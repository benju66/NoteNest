using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Configuration;
using NoteNest.Core.Interfaces.Search;
using NoteNest.Core.Models;
using NoteNest.Core.Models.Search;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Search
{
    /// <summary>
    /// Manages search index updates and file system integration
    /// Single Responsibility: Coordinate between file system changes and search index
    /// </summary>
    public class Fts5IndexManager : ISearchIndexManager
    {
        private readonly IFts5Repository _repository;
        private readonly ISearchResultMapper _mapper;
        private readonly IStorageOptions _storageOptions;
        private readonly IAppLogger? _logger;
        private readonly object _lockObject = new();
        private readonly List<string> _recentlyProcessedFiles = new();
        private const int MaxRecentFiles = 50;

        private volatile bool _isIndexing = false;
        private IndexingProgress? _currentProgress;
        private IndexManagerSettings _settings;

        #region Properties and Events

        public bool IsIndexing => _isIndexing;
        public IndexingProgress? CurrentProgress => _currentProgress;
        public IndexManagerSettings Settings => _settings;

        public event EventHandler<IndexingStartedEventArgs>? IndexingStarted;
        public event EventHandler<IndexingProgressEventArgs>? IndexingProgress;
        public event EventHandler<IndexingCompletedEventArgs>? IndexingCompleted;
        public event EventHandler<IndexingErrorEventArgs>? IndexingError;

        #endregion

        public Fts5IndexManager(
            IFts5Repository repository, 
            ISearchResultMapper mapper, 
            IStorageOptions storageOptions,
            IAppLogger? logger = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _storageOptions = storageOptions ?? throw new ArgumentNullException(nameof(storageOptions));
            _logger = logger;
            _settings = new IndexManagerSettings(); // Default settings
        }

        #region Initialization

        public async Task InitializeAsync(IFts5Repository repository, IndexManagerSettings settings)
        {
            _settings = settings ?? new IndexManagerSettings();
            
            if (!repository.IsInitialized)
            {
                throw new InvalidOperationException("Repository must be initialized before index manager");
            }

            _logger?.Info("FTS5 Index Manager initialized");
        }

        public void UpdateSettings(IndexManagerSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger?.Info("Index manager settings updated");
        }

        #endregion

        #region File System Event Handling

        public async Task HandleFileCreatedAsync(string filePath)
        {
            if (!ShouldProcessFile(filePath))
                return;

            try
            {
                var document = await CreateSearchDocumentFromFileAsync(filePath);
                if (document != null)
                {
                    await _repository.IndexDocumentAsync(document);
                    AddToRecentlyProcessed(filePath);
                    _logger?.Debug($"Indexed new file: {filePath}");
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to index new file: {filePath}");
                IndexingError?.Invoke(this, new IndexingErrorEventArgs(filePath, ex));
            }
        }

        public async Task HandleFileModifiedAsync(string filePath)
        {
            if (!ShouldProcessFile(filePath))
                return;

            try
            {
                var document = await CreateSearchDocumentFromFileAsync(filePath);
                if (document != null)
                {
                    await _repository.UpdateDocumentAsync(document);
                    AddToRecentlyProcessed(filePath);
                    _logger?.Debug($"Updated index for modified file: {filePath}");
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to update index for file: {filePath}");
                IndexingError?.Invoke(this, new IndexingErrorEventArgs(filePath, ex));
            }
        }

        public async Task HandleFileDeletedAsync(string filePath)
        {
            try
            {
                await _repository.RemoveByFilePathAsync(filePath);
                _logger?.Debug($"Removed deleted file from index: {filePath}");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to remove deleted file from index: {filePath}");
                IndexingError?.Invoke(this, new IndexingErrorEventArgs(filePath, ex));
            }
        }

        public async Task HandleFileRenamedAsync(string oldPath, string newPath)
        {
            try
            {
                // Remove old entry
                await _repository.RemoveByFilePathAsync(oldPath);

                // Add new entry if it should be indexed
                if (ShouldProcessFile(newPath))
                {
                    await HandleFileCreatedAsync(newPath);
                }

                _logger?.Debug($"Handled file rename: {oldPath} -> {newPath}");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to handle file rename: {oldPath} -> {newPath}");
                IndexingError?.Invoke(this, new IndexingErrorEventArgs(newPath, ex));
            }
        }

        #endregion

        #region Bulk Operations

        public async Task RebuildIndexAsync(IProgress<IndexingProgress>? progress = null)
        {
            if (_isIndexing)
            {
                _logger?.Warning("Index rebuild already in progress");
                return;
            }

            _isIndexing = true;
            var startTime = DateTime.Now;
            var processedFiles = 0;
            var errorCount = 0;

            try
            {
                _logger?.Info("Starting complete index rebuild");

                // Clear existing index
                await _repository.ClearIndexAsync();

                // Find all note files to index
                var notePaths = await DiscoverNoteFilesAsync();
                var totalFiles = notePaths.Count;

                IndexingStarted?.Invoke(this, new IndexingStartedEventArgs(totalFiles, "Complete Rebuild"));

                _currentProgress = new IndexingProgress
                {
                    Total = totalFiles,
                    Stage = "Rebuilding Index"
                };

                // Process files in batches
                var batchSize = _settings.BatchSize;
                var batches = SplitIntoBatches(notePaths, batchSize);

                foreach (var batch in batches)
                {
                    var documents = new List<SearchDocument>();

                    foreach (var filePath in batch)
                    {
                        try
                        {
                        // Note: IProgress doesn't have cancellation token, 
                        // cancellation would be handled at higher level
                        // if (cancellationToken.IsCancellationRequested) break;

                            var document = await CreateSearchDocumentFromFileAsync(filePath);
                            if (document != null)
                            {
                                documents.Add(document);
                                AddToRecentlyProcessed(filePath);
                            }

                            processedFiles++;
                            _currentProgress.Processed = processedFiles;
                            _currentProgress.CurrentFile = filePath;

                            progress?.Report(_currentProgress);
                            IndexingProgress?.Invoke(this, new IndexingProgressEventArgs(_currentProgress));
                        }
                        catch (Exception ex)
                        {
                            errorCount++;
                            _logger?.Error(ex, $"Failed to process file during rebuild: {filePath}");
                            IndexingError?.Invoke(this, new IndexingErrorEventArgs(filePath, ex));
                        }
                    }

                    // Batch index documents
                    if (documents.Any())
                    {
                        try
                        {
                            await _repository.IndexDocumentsBatchAsync(documents);
                        }
                        catch (Exception ex)
                        {
                            errorCount++;
                            _logger?.Error(ex, $"Failed to batch index {documents.Count} documents");
                        }
                    }
                }

                // Optimize index after rebuild
                if (_settings.AutoOptimizeAfterBatch)
                {
                    _currentProgress.Stage = "Optimizing Index";
                    progress?.Report(_currentProgress);
                    await OptimizeIndexAsync();
                }

                var duration = DateTime.Now - startTime;
                _logger?.Info($"Index rebuild completed: {processedFiles} files processed in {duration.TotalSeconds:F1}s ({errorCount} errors)");

                IndexingCompleted?.Invoke(this, new IndexingCompletedEventArgs(processedFiles, duration, errorCount));
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Index rebuild failed");
                throw;
            }
            finally
            {
                _isIndexing = false;
                _currentProgress = null;
            }
        }

        public async Task RebuildDirectoryAsync(string directoryPath, IProgress<IndexingProgress>? progress = null)
        {
            if (!Directory.Exists(directoryPath))
            {
                _logger?.Warning($"Directory does not exist for rebuild: {directoryPath}");
                return;
            }

            var filesToProcess = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(ShouldProcessFile)
                .ToList();

            if (!filesToProcess.Any())
            {
                _logger?.Info($"No indexable files found in directory: {directoryPath}");
                return;
            }

            var startTime = DateTime.Now;
            var processedFiles = 0;
            var errorCount = 0;

            try
            {
                _logger?.Info($"Starting directory rebuild: {directoryPath} ({filesToProcess.Count} files)");

                IndexingStarted?.Invoke(this, new IndexingStartedEventArgs(filesToProcess.Count, $"Directory Rebuild: {directoryPath}"));

                foreach (var filePath in filesToProcess)
                {
                    try
                    {
                        var document = await CreateSearchDocumentFromFileAsync(filePath);
                        if (document != null)
                        {
                            await _repository.UpdateDocumentAsync(document);
                            AddToRecentlyProcessed(filePath);
                        }
                        processedFiles++;
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        _logger?.Error(ex, $"Failed to rebuild file: {filePath}");
                        IndexingError?.Invoke(this, new IndexingErrorEventArgs(filePath, ex));
                    }

                    // Report progress
                    progress?.Report(new IndexingProgress
                    {
                        Processed = processedFiles,
                        Total = filesToProcess.Count,
                        CurrentFile = filePath,
                        Stage = "Processing Directory"
                    });
                }

                var duration = DateTime.Now - startTime;
                _logger?.Info($"Directory rebuild completed: {processedFiles} files in {duration.TotalSeconds:F1}s");

                IndexingCompleted?.Invoke(this, new IndexingCompletedEventArgs(processedFiles, duration, errorCount));
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Directory rebuild failed: {directoryPath}");
                throw;
            }
        }

        public async Task OptimizeIndexAsync()
        {
            try
            {
                await _repository.OptimizeIndexAsync();
                _logger?.Info("Search index optimized");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to optimize search index");
                throw;
            }
        }

        public async Task<IndexValidationResult> ValidateIndexAsync()
        {
            var result = new IndexValidationResult();

            try
            {
                _logger?.Info("Starting index validation");

                // Get all files currently in index
                var stats = await _repository.GetStatisticsAsync();
                
                // Discover all note files on disk
                var diskFiles = await DiscoverNoteFilesAsync();

                // TODO: More sophisticated validation could be implemented here
                // For now, return basic validation result
                result.ValidEntries = stats.TotalDocuments;
                
                _logger?.Info($"Index validation completed: {result.ValidEntries} valid entries");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Index validation failed");
                throw;
            }

            return result;
        }

        #endregion

        #region Status and Monitoring

        public async Task<IndexStatistics> GetIndexStatisticsAsync()
        {
            try
            {
                var stats = await _repository.GetStatisticsAsync();
                
                return new IndexStatistics
                {
                    TotalDocuments = stats.TotalDocuments,
                    DatabaseSizeBytes = stats.DatabaseSizeBytes,
                    LastRebuild = DateTime.MinValue, // Would track this in metadata
                    LastOptimized = DateTime.MinValue, // Would track this in metadata
                    RecentErrors = 0, // Would track recent errors
                    AverageIndexingTimeMs = 0 // Would calculate from metrics
                };
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to get index statistics");
                return new IndexStatistics();
            }
        }

        public async Task<List<string>> GetRecentlyProcessedFilesAsync(int count = 10)
        {
            await Task.CompletedTask; // Make async for interface compatibility

            lock (_lockObject)
            {
                return _recentlyProcessedFiles
                    .TakeLast(Math.Min(count, _recentlyProcessedFiles.Count))
                    .ToList();
            }
        }

        #endregion

        #region Private Helper Methods

        private bool ShouldProcessFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            // Check file extension
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (!_settings.IndexedExtensions.Contains(extension))
                return false;

            // Check file size
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (_settings.MaxFileSizeBytes > 0 && fileInfo.Length > _settings.MaxFileSizeBytes)
                    return false;
            }
            catch
            {
                return false; // Can't access file
            }

            // Check if in excluded directory
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                var directoryName = Path.GetFileName(directory);
                if (_settings.ExcludedDirectories.Any(excluded => 
                    directory.Contains(excluded, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }

            // Check hidden files
            if (!_settings.ProcessHiddenFiles)
            {
                try
                {
                    var attributes = File.GetAttributes(filePath);
                    if (attributes.HasFlag(FileAttributes.Hidden))
                        return false;
                }
                catch
                {
                    // If we can't read attributes, assume it's not hidden
                }
            }

            return true;
        }

        private async Task<SearchDocument?> CreateSearchDocumentFromFileAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                // Read RTF content
                var rtfContent = await File.ReadAllTextAsync(filePath);
                _logger?.Debug($"[INDEXING] Processing file: {Path.GetFileName(filePath)} ({rtfContent.Length} chars)");
                
                // Extract plain text using enhanced SmartRtfExtractor
                var plainText = NoteNest.Core.Utils.SmartRtfExtractor.ExtractPlainText(rtfContent);
                _logger?.Debug($"[INDEXING] Extracted plain text: '{plainText.Substring(0, Math.Min(100, plainText.Length))}...' ({plainText.Length} chars)");
                
                // Generate smart preview at index-time (one-time cost for optimal search performance)
                var smartPreview = NoteNest.Core.Utils.SmartRtfExtractor.GenerateSmartPreview(plainText, 150);
                _logger?.Debug($"[INDEXING] Generated preview: '{smartPreview}' ({smartPreview.Length} chars)");

                // Create note model for mapping
                var noteModel = new NoteModel
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = Path.GetFileNameWithoutExtension(filePath),
                    FilePath = filePath,
                    Content = rtfContent,
                    LastModified = File.GetLastWriteTime(filePath),
                    CategoryId = ExtractCategoryIdFromPath(filePath),
                    IsDirty = false
                };

                // Map to search document with enhanced preview
                var searchDocument = _mapper.MapFromNoteModel(noteModel, plainText);
                
                // Set the pre-generated smart preview
                if (searchDocument != null)
                {
                    searchDocument.ContentPreview = smartPreview;
                    _logger?.Debug($"[INDEXING] ContentPreview set: '{smartPreview}' for document {searchDocument.NoteId}");
                }
                
                return searchDocument;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to create search document from file: {filePath}");
                return null;
            }
        }

        private async Task<List<string>> DiscoverNoteFilesAsync()
        {
            var files = new List<string>();

            try
            {
                // Use clean configuration - NotesPath already includes the full path
                var notesPath = _storageOptions.NotesPath;
                
                if (!Directory.Exists(notesPath))
                {
                    _logger?.Warning($"Notes directory does not exist: {notesPath}");
                    return files;
                }

                // Find all RTF files
                foreach (var extension in _settings.IndexedExtensions)
                {
                    var pattern = $"*{extension}";
                    var foundFiles = Directory.GetFiles(notesPath, pattern, SearchOption.AllDirectories);
                    files.AddRange(foundFiles.Where(ShouldProcessFile));
                }

                _logger?.Debug($"Discovered {files.Count} indexable files");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to discover note files");
            }

            return files;
        }

        private string ExtractCategoryIdFromPath(string filePath)
        {
            try
            {
                // Extract category from directory structure
                // Assume structure like: Notes/CategoryName/note.rtf
                var directory = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(directory))
                    return string.Empty;

                var notesPath = Path.Combine(_storageOptions.NotesPath, "Notes");
                if (directory.StartsWith(notesPath, StringComparison.OrdinalIgnoreCase))
                {
                    var relativePath = directory.Substring(notesPath.Length).TrimStart(Path.DirectorySeparatorChar);
                    var categoryName = relativePath.Split(Path.DirectorySeparatorChar).FirstOrDefault();
                    return categoryName ?? string.Empty;
                }
            }
            catch
            {
                // Ignore errors in category extraction
            }

            return string.Empty;
        }

        private void AddToRecentlyProcessed(string filePath)
        {
            lock (_lockObject)
            {
                _recentlyProcessedFiles.Add(filePath);
                
                // Keep only recent files
                if (_recentlyProcessedFiles.Count > MaxRecentFiles)
                {
                    _recentlyProcessedFiles.RemoveRange(0, _recentlyProcessedFiles.Count - MaxRecentFiles);
                }
            }
        }

        private static IEnumerable<List<T>> SplitIntoBatches<T>(List<T> items, int batchSize)
        {
            for (int i = 0; i < items.Count; i += batchSize)
            {
                yield return items.Skip(i).Take(batchSize).ToList();
            }
        }

        #endregion
    }
}
