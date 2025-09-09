using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    public class NoteState
    {
        private string? _currentContent;
        private string? _lastSavedContent;
        private string? _currentHash;
        private string? _lastSavedHash;
        
        public string NoteId { get; set; } = "";
        public string FilePath { get; set; } = "";
        public DateTime LastSaveTime { get; set; }
        public DateTime FileLastModified { get; set; }
        
        public string? CurrentContent => _currentContent;
        public string? LastSavedContent => _lastSavedContent;
        
        public bool IsDirty
        {
            get
            {
                if (_currentContent == null) return false;
                if (_lastSavedContent == null) return true;
                
                // Use hash for large content
                if (_currentContent.Length > 10000 || _lastSavedContent.Length > 10000)
                {
                    return _currentHash != _lastSavedHash;
                }
                
                return _currentContent != _lastSavedContent;
            }
        }
        
        public void UpdateContent(string? content)
        {
            _currentContent = content;
            if (content != null && content.Length > 10000)
            {
                using (var sha256 = SHA256.Create())
                {
                    var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
                    _currentHash = Convert.ToBase64String(bytes);
                }
            }
            else
            {
                _currentHash = null;
            }
        }
        
        public void UpdateLastSaved(string? content)
        {
            _lastSavedContent = content;
            if (content != null && content.Length > 10000)
            {
                using (var sha256 = SHA256.Create())
                {
                    var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
                    _lastSavedHash = Convert.ToBase64String(bytes);
                }
            }
            else
            {
                _lastSavedHash = null;
            }
        }
    }
    
    public class CircuitBreaker
    {
        private int _failureCount;
        private DateTime _lastFailureTime;
        private readonly int _threshold;
        private readonly TimeSpan _timeout;
        
        public CircuitBreaker(int threshold = 3, int timeoutSeconds = 30)
        {
            _threshold = threshold;
            _timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }
        
        public bool IsOpen => _failureCount >= _threshold && 
                              DateTime.UtcNow - _lastFailureTime < _timeout;
        
        public void RecordSuccess() => _failureCount = 0;
        
        public void RecordFailure()
        {
            _failureCount++;
            _lastFailureTime = DateTime.UtcNow;
        }
    }

    public class UnifiedSaveManager : ISaveManager
    {
        private const int MAX_FILE_SIZE = 50 * 1024 * 1024; // 50MB
        
        private readonly Channel<SaveRequest> _saveChannel;
        private readonly Dictionary<string, NoteState> _notes = new();
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
        private readonly Dictionary<string, Timer> _autoSaveTimers = new();
        private readonly Dictionary<string, Timer> _externalChangeDebounceTimers = new();
        private readonly Dictionary<string, SaveRequest> _pendingSaves = new();
        private readonly Dictionary<string, CircuitBreaker> _pathCircuitBreakers = new();
        private readonly Dictionary<string, string> _pathToNoteId = new();
        private readonly HashSet<string> _savingNotes = new();
        
        private readonly ReaderWriterLockSlim _stateLock = new();
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _processorTask;
        private readonly IAppLogger _logger;

        public event EventHandler<NoteSavedEventArgs>? NoteSaved;
        public event EventHandler<SaveProgressEventArgs>? SaveStarted;
        public event EventHandler<SaveProgressEventArgs>? SaveCompleted;
        public event EventHandler<ExternalChangeEventArgs>? ExternalChangeDetected;

        public UnifiedSaveManager(IAppLogger logger)
        {
            _logger = logger;
            
            _saveChannel = Channel.CreateBounded<SaveRequest>(new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
            
            _processorTask = Task.Run(() => ProcessSaveQueue(_cancellation.Token));
        }

        public async Task<string> OpenNoteAsync(string filePath)
        {
            // Normalize path for consistency
            var normalizedPath = NormalizePath(filePath);
            
            // Check file size limit
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > MAX_FILE_SIZE)
                {
                    throw new NotSupportedException($"File too large: {fileInfo.Length:N0} bytes (max {MAX_FILE_SIZE:N0})");
                }
            }
            
            // Check if already open
            _stateLock.EnterReadLock();
            try
            {
                if (_pathToNoteId.TryGetValue(normalizedPath, out var existingId))
                {
                    _logger.Info($"Note already open: {existingId} -> {filePath}");
                    return existingId;
                }
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
            
            // Generate deterministic ID
            string noteId = GenerateNoteId(normalizedPath);
            
            // Handle unlikely collision
            _stateLock.EnterWriteLock();
            try
            {
                var baseId = noteId;
                var counter = 1;
                while (_notes.ContainsKey(noteId))
                {
                    noteId = $"{baseId}_{counter}";
                    counter++;
                }
            }
            finally
            {
                _stateLock.ExitWriteLock();
            }
            
            // Read initial content asynchronously
            string content = "";
            DateTime lastModified = DateTime.UtcNow;
            
            if (File.Exists(filePath))
            {
                content = await File.ReadAllTextAsync(filePath);
                lastModified = await Task.Run(() => File.GetLastWriteTimeUtc(filePath));
            }
            
            // Store state
            _stateLock.EnterWriteLock();
            try
            {
                var state = new NoteState
            {
                NoteId = noteId,
                FilePath = filePath,
                LastSaveTime = DateTime.UtcNow,
                FileLastModified = lastModified
            };
                state.UpdateContent(content);
                state.UpdateLastSaved(content);
                
                _notes[noteId] = state;
                _pathToNoteId[normalizedPath] = noteId;
                _pathCircuitBreakers[normalizedPath] = new CircuitBreaker();
            
            // Setup file watcher
            SetupFileWatcher(noteId, filePath);
            }
            finally
            {
                _stateLock.ExitWriteLock();
            }
            
            _logger.Info($"Opened note: {noteId} -> {filePath}");
            return noteId;
        }

        public void UpdateContent(string noteId, string content)
        {
            _stateLock.EnterWriteLock();
            try
        {
            if (!_notes.TryGetValue(noteId, out var state))
            {
                _logger.Warning($"UpdateContent called for unknown note: {noteId}");
                return;
            }
            
                state.UpdateContent(content);
                
            if (state.IsDirty)
                {
                    // Reuse existing timer if possible
                    if (_autoSaveTimers.TryGetValue(noteId, out var timer))
                    {
                        // Reset existing timer
                        timer.Change(2000, Timeout.Infinite);
                    }
                    else
                    {
                        // Create new timer only if needed
                        var newTimer = new Timer(_ => QueueAutoSave(noteId), 
                                               null, 2000, Timeout.Infinite);
                        _autoSaveTimers[noteId] = newTimer;
                    }
                }
                else if (_autoSaveTimers.TryGetValue(noteId, out var timer))
                {
                    // Content is not dirty, disable timer
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
            finally
            {
                _stateLock.ExitWriteLock();
            }
        }

        public async Task<bool> SaveNoteAsync(string noteId)
        {
            if (!_notes.ContainsKey(noteId))
            {
                _logger.Warning($"SaveNoteAsync called for unknown note: {noteId}");
                return false;
            }
            
            // Cancel any pending auto-save
            _stateLock.EnterWriteLock();
            try
            {
            if (_autoSaveTimers.TryGetValue(noteId, out var timer))
                {
                    timer?.Change(Timeout.Infinite, Timeout.Infinite);
                }
                
                // Coalesce with existing save if present
                if (_pendingSaves.TryGetValue(noteId, out var existing))
                {
                    if (existing.Priority < SavePriority.UserSave)
                    {
                        // Upgrade priority
                        existing.Priority = SavePriority.UserSave;
                    }
                    // Return the existing task
                    return await existing.CompletionSource.Task;
                }
            }
            finally
            {
                _stateLock.ExitWriteLock();
            }
            
            var request = new SaveRequest
            {
                NoteId = noteId,
                Priority = SavePriority.UserSave,
                QueuedAt = DateTime.UtcNow,
                CompletionSource = new TaskCompletionSource<bool>()
            };
            
            await _saveChannel.Writer.WriteAsync(request);
            return await request.CompletionSource.Task;
        }

        public async Task<BatchSaveResult> SaveAllDirtyAsync()
        {
            var result = new BatchSaveResult();
            var tasks = new List<Task<(string noteId, bool success)>>();
            
            List<string> dirtyNotes;
            _stateLock.EnterReadLock();
            try
            {
                dirtyNotes = _notes.Where(kvp => kvp.Value.IsDirty)
                                   .Select(kvp => kvp.Key)
                                   .ToList();
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
            
            foreach (var noteId in dirtyNotes)
                {
                    var request = new SaveRequest
                    {
                    NoteId = noteId,
                        Priority = SavePriority.ShutdownSave,
                        QueuedAt = DateTime.UtcNow,
                        CompletionSource = new TaskCompletionSource<bool>()
                    };
                    
                    await _saveChannel.Writer.WriteAsync(request);
                
                tasks.Add(request.CompletionSource.Task.ContinueWith(t => 
                    (noteId, t.Result)));
            }
            
            var results = await Task.WhenAll(tasks);
            
            foreach (var (noteId, success) in results)
            {
                if (success)
                {
                    result.SuccessCount++;
                }
                else
                {
                    result.FailureCount++;
                    result.FailedNoteIds.Add(noteId);
                }
            }
            
            return result;
        }

        private async Task ProcessSaveQueue(CancellationToken cancellation)
        {
            var priorityQueues = new Dictionary<SavePriority, Queue<SaveRequest>>();
            foreach (SavePriority priority in Enum.GetValues(typeof(SavePriority)))
            {
                priorityQueues[priority] = new Queue<SaveRequest>();
            }
            
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    // First, process ALL pending priority items
                    bool hasProcessedItem;
                    do
                    {
                        hasProcessedItem = false;
                        
                        // Check each priority level in order (highest to lowest)
                        for (int p = (int)SavePriority.ShutdownSave; p >= (int)SavePriority.AutoSave; p--)
                        {
                            var priority = (SavePriority)p;
                            if (priorityQueues[priority].Count > 0)
                            {
                                var request = priorityQueues[priority].Dequeue();
                                
                                // Remove from pending saves
                                _stateLock.EnterWriteLock();
                                try
                                {
                                    _pendingSaves.Remove(request.NoteId);
                                }
                                finally
                                {
                                    _stateLock.ExitWriteLock();
                                }
                                
                                await ExecuteSave(request);
                                hasProcessedItem = true;
                                break; // Start from highest priority again
                            }
                        }
                    } while (hasProcessedItem && !cancellation.IsCancellationRequested);
                    
                    // Now wait for new items with timeout
                    var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    timeoutCts.CancelAfter(100); // Check every 100ms
                    
                    try
                    {
                        var request = await _saveChannel.Reader.ReadAsync(timeoutCts.Token);
                        
                        // Check for coalescing opportunity
                        _stateLock.EnterWriteLock();
                        try
                        {
                            if (_pendingSaves.TryGetValue(request.NoteId, out var existing))
                            {
                                // Coalesce - complete old request and use new one
                                existing.CompletionSource?.TrySetResult(false);
                                existing.IsCoalesced = true;
                                
                                // Upgrade priority if needed
                                if (request.Priority > existing.Priority)
                                {
                                    request.Priority = existing.Priority;
                                }
                            }
                            
                            _pendingSaves[request.NoteId] = request;
                        }
                        finally
                        {
                            _stateLock.ExitWriteLock();
                        }
                        
                        priorityQueues[request.Priority].Enqueue(request);
                    }
                    catch (OperationCanceledException)
                    {
                        // Timeout is expected, continue loop
                        continue;
                    }
                }
                catch (OperationCanceledException)
                {
                    break; // Shutdown requested
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error in save queue processor");
                }
            }
            
            // Process remaining items on shutdown
            foreach (var queue in priorityQueues.Values)
            {
                while (queue.Count > 0)
                {
                    try
                    {
                        var request = queue.Dequeue();
                        await ExecuteSave(request);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error processing remaining saves on shutdown");
                    }
                }
            }
        }

        private async Task ExecuteSave(SaveRequest request)
        {
            string contentToSave;
            string filePath;
            DateTime fileLastModified;
            
            // Raise SaveStarted event
            SaveStarted?.Invoke(this, new SaveProgressEventArgs
            {
                NoteId = request.NoteId,
                FilePath = _notes.ContainsKey(request.NoteId) ? _notes[request.NoteId].FilePath : "",
                Priority = request.Priority,
                Timestamp = DateTime.UtcNow
            });
            
            // Mark as saving
            _stateLock.EnterWriteLock();
            try
            {
                _savingNotes.Add(request.NoteId);
            }
            finally
            {
                _stateLock.ExitWriteLock();
            }
            
            try
            {
                // Capture state atomically
                _stateLock.EnterWriteLock();
                try
                {
                    if (!_notes.TryGetValue(request.NoteId, out var state))
                {
                    request.CompletionSource?.TrySetResult(false);
                    return;
                }
                
                if (!state.IsDirty)
                {
                    request.CompletionSource?.TrySetResult(true);
                    return;
                }
                
                    // Capture content and metadata while locked
                    contentToSave = state.CurrentContent ?? "";
                    filePath = state.FilePath;
                    fileLastModified = state.FileLastModified;
                }
                finally
                {
                    _stateLock.ExitWriteLock();
                }
                
                // Check circuit breaker
                var normalizedPath = NormalizePath(filePath);
                CircuitBreaker breaker;
                _stateLock.EnterReadLock();
                try
                {
                    _pathCircuitBreakers.TryGetValue(normalizedPath, out breaker);
                }
                finally
                {
                    _stateLock.ExitReadLock();
                }
                
                if (breaker?.IsOpen == true)
                {
                    _logger.Warning($"Circuit breaker open for: {filePath}");
                    request.CompletionSource?.TrySetResult(false);
                    return;
                }
                
                // Perform I/O outside of lock
                
                // Check file permissions
                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.IsReadOnly)
                    {
                        _logger.Warning($"File is read-only: {filePath}");
                        breaker?.RecordFailure();
                        request.CompletionSource?.TrySetResult(false);
                        return;
                    }
                    
                    // Check for external modifications
                    var currentModified = await Task.Run(() => File.GetLastWriteTimeUtc(filePath));
                    if (currentModified > fileLastModified.AddSeconds(1))
                    {
                        _logger.Warning($"External modification detected for: {filePath}");
                        
                        // Read external content for event
                        var externalContent = await File.ReadAllTextAsync(filePath);
                        
                        ExternalChangeDetected?.Invoke(this, new ExternalChangeEventArgs
                        {
                            NoteId = request.NoteId,
                            FilePath = filePath,
                            ExternalContent = externalContent,
                            DetectedAt = DateTime.UtcNow
                        });
                        
                        request.CompletionSource?.TrySetResult(false);
                        return;
                    }
                }
                
                // Ensure directory exists
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                // Write to temp file
                var tempFile = filePath + ".tmp";
                await File.WriteAllTextAsync(tempFile, contentToSave);
                
                // Verify written content
                var writtenContent = await File.ReadAllTextAsync(tempFile);
                if (writtenContent != contentToSave)
                {
                    throw new IOException($"Content verification failed for {tempFile}");
                }
                
                // Atomic rename with retry
                int retries = 3;
                Exception lastException = null;
                
                while (retries > 0)
                {
                    try
                    {
                        await Task.Run(() => File.Move(tempFile, filePath, true));
                        break;
                    }
                    catch (IOException ex) when (retries > 1)
                    {
                        lastException = ex;
                        retries--;
                        await Task.Delay(100);
                    }
                }
                
                if (retries == 0 && lastException != null)
                {
                    breaker?.RecordFailure();
                    throw lastException;
                }
                
                // Update state after successful save
                var newLastModified = await Task.Run(() => File.GetLastWriteTimeUtc(filePath));
                
                _stateLock.EnterWriteLock();
                try
                {
                    if (_notes.TryGetValue(request.NoteId, out var state))
                    {
                        // Only update if content hasn't changed since we captured it
                        if ((state.CurrentContent ?? "") == contentToSave)
                        {
                            state.UpdateLastSaved(contentToSave);
                        }
                state.LastSaveTime = DateTime.UtcNow;
                        state.FileLastModified = newLastModified;
                    }
                }
                finally
                {
                    _stateLock.ExitWriteLock();
                }
                
                breaker?.RecordSuccess();
                
                // Raise event
                NoteSaved?.Invoke(this, new NoteSavedEventArgs
                {
                    NoteId = request.NoteId,
                    FilePath = filePath,
                    SavedAt = DateTime.UtcNow,
                    WasAutoSave = request.Priority == SavePriority.AutoSave
                });
                
                _logger.Info($"Saved: {filePath}");
                request.CompletionSource?.TrySetResult(true);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to save note: {request.NoteId}");
                request.CompletionSource?.TrySetResult(false);
            }
            finally
            {
                // Remove from saving set
                _stateLock.EnterWriteLock();
                try
                {
                    _savingNotes.Remove(request.NoteId);
                }
                finally
                {
                    _stateLock.ExitWriteLock();
                }
                
                // Raise SaveCompleted event
                SaveCompleted?.Invoke(this, new SaveProgressEventArgs
                {
                    NoteId = request.NoteId,
                    FilePath = _notes.ContainsKey(request.NoteId) ? _notes[request.NoteId].FilePath : "",
                    Priority = request.Priority,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        public async Task<bool> ResolveExternalChangeAsync(string noteId, ConflictResolution resolution)
        {
            _stateLock.EnterWriteLock();
            try
            {
                if (!_notes.TryGetValue(noteId, out var state))
                    return false;
                
                switch (resolution)
                {
                    case ConflictResolution.KeepLocal:
                        // Force save local content
                        state.FileLastModified = DateTime.MinValue;
                        break;
                        
                    case ConflictResolution.KeepExternal:
                        // Reload from file
                        if (File.Exists(state.FilePath))
                        {
                            var content = await File.ReadAllTextAsync(state.FilePath);
                            state.UpdateContent(content);
                            state.UpdateLastSaved(content);
                            state.FileLastModified = File.GetLastWriteTimeUtc(state.FilePath);
                        }
                        break;
                        
                    case ConflictResolution.Merge:
                        // TODO: Implement three-way merge
                        _logger.Warning("Merge resolution not yet implemented");
                        return false;
                }
                
                return true;
            }
            finally
            {
                _stateLock.ExitWriteLock();
            }
        }

        private void SetupFileWatcher(string noteId, string filePath)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(directory))
                    return;
                    
                var fileName = Path.GetFileName(filePath);
                
                var watcher = new FileSystemWatcher(directory)
                {
                    Filter = fileName,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = false // Will enable after setup
                };
                
                watcher.Changed += (s, e) =>
                {
                    _stateLock.EnterWriteLock();
                    try
                    {
                        // Don't trigger if we're saving
                        if (_savingNotes.Contains(noteId))
                            return;
                            
                        // Cancel existing debounce timer
                        if (_externalChangeDebounceTimers.TryGetValue(noteId, out var existingTimer))
                        {
                            existingTimer?.Dispose();
                            _externalChangeDebounceTimers.Remove(noteId);
                        }
                        
                        // Create new debounce timer (500ms)
                        var debounceTimer = new Timer(_ =>
                        {
                            CheckExternalChange(noteId, filePath);
                        }, null, 500, Timeout.Infinite);
                        
                        _externalChangeDebounceTimers[noteId] = debounceTimer;
                    }
                    finally
                    {
                        _stateLock.ExitWriteLock();
                    }
                };
                
                watcher.EnableRaisingEvents = true;
                _watchers[noteId] = watcher;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to setup file watcher for: {filePath} - {ex.Message}");
            }
        }

        private async void CheckExternalChange(string noteId, string filePath)
        {
            try
            {
                _stateLock.EnterReadLock();
                try
                {
                    if (!_notes.TryGetValue(noteId, out var state))
                        return;
                        
                    if (_savingNotes.Contains(noteId))
                        return;
                }
                finally
                {
                    _stateLock.ExitReadLock();
                }
                
                if (File.Exists(filePath))
                {
                    var currentModified = await Task.Run(() => File.GetLastWriteTimeUtc(filePath));
                    var externalContent = await File.ReadAllTextAsync(filePath);
                    
                    _stateLock.EnterReadLock();
                    try
                    {
                        if (_notes.TryGetValue(noteId, out var state))
                        {
                            if (currentModified > state.FileLastModified.AddSeconds(1))
                            {
                                ExternalChangeDetected?.Invoke(this, new ExternalChangeEventArgs
                                {
                                    NoteId = noteId,
                                    FilePath = filePath,
                                    ExternalContent = externalContent,
                                    DetectedAt = DateTime.UtcNow
                                });
                            }
                        }
                    }
                    finally
                    {
                        _stateLock.ExitReadLock();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error checking external change for {filePath}");
            }
        }

        private void QueueAutoSave(string noteId)
        {
            // Check if there's already a pending save
            _stateLock.EnterReadLock();
            try
            {
                if (_pendingSaves.ContainsKey(noteId))
                    return;
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
            
            var request = new SaveRequest
            {
                NoteId = noteId,
                Priority = SavePriority.AutoSave,
                QueuedAt = DateTime.UtcNow,
                CompletionSource = new TaskCompletionSource<bool>()
            };
            
            if (!_saveChannel.Writer.TryWrite(request))
            {
                _logger.Warning($"Failed to queue auto-save for: {noteId}");
            }
        }

        private string NormalizePath(string path)
        {
            return Path.GetFullPath(path)
                .Replace('\\', '/')
                .ToLowerInvariant();
        }

        private string GenerateNoteId(string normalizedPath)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath));
                return Convert.ToBase64String(hashBytes)
                    .Replace("/", "_")
                    .Replace("+", "-")
                    .Replace("=", "")
                    .Substring(0, Math.Min(22, Convert.ToBase64String(hashBytes).Length));
            }
        }

        public bool IsNoteDirty(string noteId)
        {
            _stateLock.EnterReadLock();
            try
            {
                return _notes.TryGetValue(noteId, out var state) && state.IsDirty;
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        public bool IsSaving(string noteId)
        {
            _stateLock.EnterReadLock();
            try
            {
                return _savingNotes.Contains(noteId);
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        public string GetContent(string noteId)
        {
            _stateLock.EnterReadLock();
            try
            {
                return _notes.TryGetValue(noteId, out var state) ? state.CurrentContent ?? "" : "";
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        public string? GetLastSavedContent(string noteId)
        {
            _stateLock.EnterReadLock();
            try
            {
                return _notes.TryGetValue(noteId, out var state) ? state.LastSavedContent : null;
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        public string? GetFilePath(string noteId)
        {
            _stateLock.EnterReadLock();
            try
            {
                return _notes.TryGetValue(noteId, out var state) ? state.FilePath : null;
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        public string? GetNoteIdForPath(string filePath)
        {
            var normalizedPath = NormalizePath(filePath);
            _stateLock.EnterReadLock();
            try
            {
                return _pathToNoteId.TryGetValue(normalizedPath, out var noteId) ? noteId : null;
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        public IReadOnlyList<string> GetDirtyNoteIds()
        {
            _stateLock.EnterReadLock();
            try
            {
                return _notes.Where(kvp => kvp.Value.IsDirty)
                            .Select(kvp => kvp.Key)
                            .ToList();
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        public async Task<bool> CloseNoteAsync(string noteId)
        {
            // Wait for any pending save to complete
            SaveRequest pendingSave = null;
            _stateLock.EnterReadLock();
            try
            {
                _pendingSaves.TryGetValue(noteId, out pendingSave);
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
            
            if (pendingSave != null)
            {
                try
                {
                    await pendingSave.CompletionSource.Task;
                }
                catch
                {
                    // Ignore save failures during close
                }
            }
            
            _stateLock.EnterWriteLock();
            try
            {
                // Get file path for cleanup
                string filePath = null;
                if (_notes.TryGetValue(noteId, out var state))
                {
                    filePath = state.FilePath;
                }
                
                // Dispose watcher
                if (_watchers.TryGetValue(noteId, out var watcher))
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                    _watchers.Remove(noteId);
                }
                
                // Dispose auto-save timer
                if (_autoSaveTimers.TryGetValue(noteId, out var timer))
                {
                    timer.Dispose();
                    _autoSaveTimers.Remove(noteId);
                }
                
                // Dispose external change debounce timer
                if (_externalChangeDebounceTimers.TryGetValue(noteId, out var debounceTimer))
                {
                    debounceTimer.Dispose();
                    _externalChangeDebounceTimers.Remove(noteId);
                }
                
                // Remove from collections
                _notes.Remove(noteId);
                _savingNotes.Remove(noteId);
                _pendingSaves.Remove(noteId);
                
                // Remove path mapping
                if (filePath != null)
                {
                    var normalizedPath = NormalizePath(filePath);
                    _pathToNoteId.Remove(normalizedPath);
                }
                
                return true;
            }
            finally
            {
                _stateLock.ExitWriteLock();
            }
        }

        public void Dispose()
        {
            // Set shutdown flag
            _cancellation.Cancel();
            
            // Complete the channel
            _saveChannel.Writer.TryComplete();
            
            // Cancel all pending completion sources
            _stateLock.EnterWriteLock();
            try
            {
                foreach (var pending in _pendingSaves.Values)
                {
                    pending.CompletionSource?.TrySetCanceled();
                }
                _pendingSaves.Clear();
            }
            finally
            {
                _stateLock.ExitWriteLock();
            }
            
            // Wait for processor to finish with timeout
            try
            {
                if (!_processorTask.Wait(30000)) // 30 seconds
                {
                    _logger.Warning("Save processor did not complete within timeout");
                }
            }
            catch (AggregateException ex)
            {
                _logger.Error(ex.InnerException, "Error during save processor shutdown");
            }
            
            // Clean up resources
            _stateLock.EnterWriteLock();
            try
            {
                // Dispose all timers
                foreach (var timer in _autoSaveTimers.Values)
                {
                    timer?.Dispose();
                }
                _autoSaveTimers.Clear();
                
                foreach (var timer in _externalChangeDebounceTimers.Values)
                {
                    timer?.Dispose();
                }
                _externalChangeDebounceTimers.Clear();
                
                // Dispose all watchers
                foreach (var watcher in _watchers.Values)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher?.Dispose();
                }
                _watchers.Clear();
            }
            finally
            {
                _stateLock.ExitWriteLock();
            }
            
            _stateLock?.Dispose();
            _cancellation?.Dispose();
        }
    }
}
