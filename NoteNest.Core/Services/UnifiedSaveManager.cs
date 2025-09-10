using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Events;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    public sealed class UnifiedSaveManager : ISaveManager
    {
        private readonly IAppLogger _logger;
        private readonly IWriteAheadLog _wal;
        private readonly SaveConfiguration _config;
        private readonly ConcurrentDictionary<string, NoteState> _notes;
        private readonly ConcurrentDictionary<string, NoteTimer> _timers;
        private readonly ConcurrentDictionary<string, string> _pathToNoteId;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _saveLocksPerFile;
        private readonly Timer _periodicCleanup;
        private readonly SemaphoreSlim _globalSaveSemaphore;
        private bool _disposed;

        public event EventHandler<NoteSavedEventArgs>? NoteSaved;
        public event EventHandler<SaveProgressEventArgs>? SaveStarted;
        public event EventHandler<SaveProgressEventArgs>? SaveCompleted;
        public event EventHandler<ExternalChangeEventArgs>? ExternalChangeDetected;

        public UnifiedSaveManager(IAppLogger logger, IWriteAheadLog wal, SaveConfiguration config = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _wal = wal ?? throw new ArgumentNullException(nameof(wal));
            _config = config ?? new SaveConfiguration();
            
            _notes = new ConcurrentDictionary<string, NoteState>();
            _timers = new ConcurrentDictionary<string, NoteTimer>();
            _pathToNoteId = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _saveLocksPerFile = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
            
            // Global semaphore for concurrent saves
            _globalSaveSemaphore = new SemaphoreSlim(_config.MaxConcurrentSaves, _config.MaxConcurrentSaves);
            
            // Cleanup timer
            var cleanupInterval = TimeSpan.FromMinutes(5);
            _periodicCleanup = new Timer(CleanupInactiveNotes, null, cleanupInterval, cleanupInterval);
            
            // Recover from WAL on startup
            Task.Run(RecoverFromWAL);
        }

        private class NoteState
        {
            private string _currentContent = string.Empty;
            private string _lastSavedContent = string.Empty;
            
            public string NoteId { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public DateTime LastAccessTime { get; set; } = DateTime.UtcNow;
            public DateTime LastSaveTime { get; set; } = DateTime.UtcNow;
            public bool IsOpen { get; set; }
            
            public string CurrentContent
            {
                get => _currentContent;
                set
                {
                    _currentContent = value;
                    Touch();
                }
            }
            
            public string LastSavedContent
            {
                get => _lastSavedContent;
                set
                {
                    _lastSavedContent = value;
                    LastSaveTime = DateTime.UtcNow;
                }
            }
            
            public bool IsDirty => _currentContent != _lastSavedContent;
            
            public void Touch() => LastAccessTime = DateTime.UtcNow;
        }

        private class NoteTimer : IDisposable
        {
            private readonly Action<string> _onTrigger;
            private readonly string _noteId;
            private readonly int _debounceMs;
            private readonly int _maxDelayMs;
            private Timer? _timer;
            private DateTime _firstChange;

            public NoteTimer(string noteId, Action<string> onTrigger, int debounceMs, int maxDelayMs)
            {
                _noteId = noteId;
                _onTrigger = onTrigger;
                _debounceMs = debounceMs;
                _maxDelayMs = maxDelayMs;
            }

            public void Reset()
            {
                if (_timer == null)
                {
                    _firstChange = DateTime.UtcNow;
                    _timer = new Timer(_ => _onTrigger(_noteId), null, _debounceMs, Timeout.Infinite);
                }
                else
                {
                    var elapsed = (DateTime.UtcNow - _firstChange).TotalMilliseconds;
                    if (elapsed >= _maxDelayMs)
                    {
                        // Force save now
                        _timer.Change(0, Timeout.Infinite);
                    }
                    else
                    {
                        // Reset debounce
                        _timer.Change(_debounceMs, Timeout.Infinite);
                    }
                }
            }

            public void Cancel()
            {
                _timer?.Change(Timeout.Infinite, Timeout.Infinite);
                _timer?.Dispose();
                _timer = null;
            }

            public void Dispose() => Cancel();
        }

        public async Task<string> OpenNoteAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be empty", nameof(filePath));

            var normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();
            
            // Check if already tracking this file
            if (_pathToNoteId.TryGetValue(normalizedPath, out var existingId))
            {
                if (_notes.TryGetValue(existingId, out var existingState))
                {
                    existingState.IsOpen = true;
                    existingState.Touch();
                    _logger.Info($"Reusing note from memory: {existingId} -> {filePath}");
                    return existingId;
                }
                
                // Orphaned mapping - clean it up
                _pathToNoteId.TryRemove(normalizedPath, out _);
                _logger.Debug($"Cleaned up orphaned path mapping for: {filePath}");
            }

            // Generate stable ID (not based on path)
            var noteId = GenerateStableNoteId();
            
            // Try to recover from WAL first
            var walContent = await _wal.RecoverAsync(noteId);
            
            // Load from disk if no WAL
            string content = walContent ?? "";
            if (string.IsNullOrEmpty(walContent) && File.Exists(filePath))
            {
                try
                {
                    content = await File.ReadAllTextAsync(filePath);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed to load note: {filePath}");
                    content = "";
                }
            }

            // Create state
            var state = new NoteState
            {
                NoteId = noteId,
                FilePath = filePath,
                CurrentContent = content,
                LastSavedContent = string.IsNullOrEmpty(walContent) ? content : "",
                IsOpen = true
            };

            _notes[noteId] = state;
            _pathToNoteId[normalizedPath] = noteId;

            if (!string.IsNullOrEmpty(walContent))
            {
                _logger.Info($"Recovered unsaved content for: {filePath}");
            }

            _logger.Info($"Opened note: {noteId} -> {filePath}");
            return noteId;
        }

        public void UpdateContent(string noteId, string content)
        {
            if (!_notes.TryGetValue(noteId, out var state))
            {
                _logger.Warning($"UpdateContent called for unknown note: {noteId}");
                return;
            }

            content = content ?? "";
            state.CurrentContent = content;

            if (state.IsDirty)
            {
                // Write to WAL immediately for crash protection
                _ = Task.Run(async () => await _wal.AppendAsync(noteId, content));
                
                // Start/reset save timer
                var timer = _timers.GetOrAdd(noteId, id => 
                    new NoteTimer(id, AutoSave, _config.AutoSaveDelayMs, _config.MaxAutoSaveDelayMs));
                timer.Reset();
                
                _logger.Debug($"Content updated for {noteId}, auto-save scheduled");
            }
            else
            {
                // Content matches saved - cancel timer
                if (_timers.TryRemove(noteId, out var timer))
                {
                    timer.Dispose();
                    _logger.Debug($"Content matches saved, cancelled auto-save for {noteId}");
                }
            }
        }

        private void AutoSave(string noteId)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await SaveNoteAsync(noteId);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Auto-save failed for note {noteId}");
                }
            });
        }

        public async Task<bool> SaveNoteAsync(string noteId)
        {
            if (!_notes.TryGetValue(noteId, out var state))
            {
                _logger.Warning($"SaveNoteAsync called for unknown note: {noteId}");
                return false;
            }

            // Cancel any pending auto-save timer
            if (_timers.TryRemove(noteId, out var timer))
            {
                timer.Dispose();
            }

            // Skip if not dirty
            if (!state.IsDirty)
            {
                _logger.Debug($"Note not dirty, skipping save: {noteId}");
                return true;
            }

            var filePath = state.FilePath;
            var content = state.CurrentContent;

            // Get file-specific lock to prevent concurrent saves to same file
            var fileLock = _saveLocksPerFile.GetOrAdd(
                filePath.ToLowerInvariant(), 
                _ => new SemaphoreSlim(1, 1));

            // Notify save started
            SaveStarted?.Invoke(this, new SaveProgressEventArgs 
            { 
                NoteId = noteId, 
                FilePath = filePath 
            });

            // Wait for global semaphore (limits total concurrent saves)
            await _globalSaveSemaphore.WaitAsync();
            
            try
            {
                // Wait for file-specific lock
                await fileLock.WaitAsync();
                
                try
                {
                    // Save to temp file with exclusive access
                    var tempPath = filePath + ".tmp";
                    var directory = Path.GetDirectoryName(filePath);
                    
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // Write with exclusive lock
                    using (var fs = new FileStream(tempPath, FileMode.Create, 
                        FileAccess.Write, FileShare.None, 4096, useAsync: true))
                    {
                        using (var writer = new StreamWriter(fs, System.Text.Encoding.UTF8))
                        {
                            await writer.WriteAsync(content);
                            await writer.FlushAsync();
                        }
                    }
                    
                    // Atomic rename
                    File.Move(tempPath, filePath, true);
                    
                    // Update state
                    state.LastSavedContent = content;
                    
                    // Clear WAL after successful save
                    await _wal.CommitAsync(noteId);
                    
                    _logger.Info($"Saved: {filePath} ({content.Length} chars)");
                    
                    // Notify save completed
                    NoteSaved?.Invoke(this, new NoteSavedEventArgs
                    {
                        NoteId = noteId,
                        FilePath = filePath,
                        SavedAt = DateTime.UtcNow,
                        WasAutoSave = true
                    });
                    
                    SaveCompleted?.Invoke(this, new SaveProgressEventArgs
                    {
                        NoteId = noteId,
                        FilePath = filePath,
                        Success = true
                    });
                    
                    return true;
                }
                finally
                {
                    fileLock.Release();
                }
            }
            catch (IOException ioEx) when (IsFileLocked(ioEx))
            {
                _logger.Warning($"File is locked by another process: {filePath}");
                SaveCompleted?.Invoke(this, new SaveProgressEventArgs
                {
                    NoteId = noteId,
                    FilePath = filePath,
                    Success = false
                });
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to save note: {filePath}");
                SaveCompleted?.Invoke(this, new SaveProgressEventArgs
                {
                    NoteId = noteId,
                    FilePath = filePath,
                    Success = false
                });
                return false;
            }
            finally
            {
                _globalSaveSemaphore.Release();
            }
        }

        private bool IsFileLocked(IOException ex)
        {
            // Check if it's a sharing violation or lock error
            var errorCode = ex.HResult & 0xFFFF;
            return errorCode == 32 || errorCode == 33; // ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION
        }

        public async Task<BatchSaveResult> SaveAllDirtyAsync()
        {
            var dirtyNotes = _notes.Where(kvp => kvp.Value.IsDirty)
                                   .Select(kvp => kvp.Key)
                                   .ToList();

            var result = new BatchSaveResult();
            
            // Save in parallel (different files can save simultaneously)
            var tasks = dirtyNotes.Select(async noteId =>
            {
                var success = await SaveNoteAsync(noteId);
                if (success)
                {
                    result.SuccessCount++;
                }
                else
                {
                    result.FailureCount++;
                    result.FailedNoteIds.Add(noteId);
                }
            });

            await Task.WhenAll(tasks);
            
            _logger.Info($"Batch save completed: {result.SuccessCount} succeeded, {result.FailureCount} failed");
            return result;
        }

        public async Task<bool> CloseNoteAsync(string noteId)
        {
            if (!_notes.TryGetValue(noteId, out var state))
                return false;

            // Cancel timer
            if (_timers.TryRemove(noteId, out var timer))
            {
                timer.Dispose();
            }

            // Save if dirty
            if (state.IsDirty)
            {
                await SaveNoteAsync(noteId);
            }

            // Mark as closed but keep in memory for reuse
            state.IsOpen = false;
            state.Touch();

            _logger.Debug($"Closed note: {noteId} (keeping in memory for reuse)");
            return true;
        }

        public void UpdateFilePath(string noteId, string newFilePath)
        {
            if (!_notes.TryGetValue(noteId, out var state))
            {
                _logger.Warning($"UpdateFilePath called for unknown note: {noteId}");
                return;
            }

            var oldNormalized = Path.GetFullPath(state.FilePath).ToLowerInvariant();
            var newNormalized = Path.GetFullPath(newFilePath).ToLowerInvariant();

            // Update state
            state.FilePath = newFilePath;
            state.Touch();

            // Update path mapping
            _pathToNoteId.TryRemove(oldNormalized, out _);
            _pathToNoteId[newNormalized] = noteId;

            _logger.Info($"Updated file path for {noteId}: {state.FilePath} -> {newFilePath}");
        }

        public bool IsNoteDirty(string noteId)
        {
            return _notes.TryGetValue(noteId, out var state) && state.IsDirty;
        }

        public bool IsSaving(string noteId)
        {
            // Simplified - we don't track this anymore
            return false;
        }

        public string GetContent(string noteId)
        {
            return _notes.TryGetValue(noteId, out var state) ? state.CurrentContent : "";
        }

        public string? GetLastSavedContent(string noteId)
        {
            return _notes.TryGetValue(noteId, out var state) ? state.LastSavedContent : null;
        }

        public string? GetFilePath(string noteId)
        {
            return _notes.TryGetValue(noteId, out var state) ? state.FilePath : null;
        }

        public string? GetNoteIdForPath(string filePath)
        {
            var normalized = Path.GetFullPath(filePath).ToLowerInvariant();
            return _pathToNoteId.TryGetValue(normalized, out var noteId) ? noteId : null;
        }

        public IReadOnlyList<string> GetDirtyNoteIds()
        {
            return _notes.Where(kvp => kvp.Value.IsDirty)
                        .Select(kvp => kvp.Key)
                        .ToList();
        }

        public Task<bool> ResolveExternalChangeAsync(string noteId, ConflictResolution resolution)
        {
            // Simplified for now
            return Task.FromResult(true);
        }

        private async Task RecoverFromWAL()
        {
            try
            {
                var recovered = await _wal.RecoverAllAsync();
                if (recovered.Count > 0)
                {
                    _logger.Info($"Found {recovered.Count} WAL entries to recover");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to recover from WAL");
            }
        }

        private void CleanupInactiveNotes(object? state)
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-_config.InactiveCleanupMinutes);
                var toRemove = _notes.Where(kvp => 
                    !kvp.Value.IsOpen && 
                    !kvp.Value.IsDirty && 
                    kvp.Value.LastAccessTime < cutoff)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var noteId in toRemove)
                {
                    if (_notes.TryRemove(noteId, out var noteState))
                    {
                        var normalized = Path.GetFullPath(noteState.FilePath).ToLowerInvariant();
                        _pathToNoteId.TryRemove(normalized, out _);
                        _logger.Debug($"Cleaned up inactive note: {noteId}");
                    }
                }

                if (toRemove.Count > 0)
                {
                    _logger.Info($"Cleaned up {toRemove.Count} inactive notes from memory");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during cleanup");
            }
        }

        private string GenerateStableNoteId()
        {
            // Generate a stable ID that doesn't change with file path
            return Guid.NewGuid().ToString("N").Substring(0, 22);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Save all dirty notes
            var saveTask = SaveAllDirtyAsync();
            if (!saveTask.Wait(TimeSpan.FromSeconds(30)))
            {
                _logger.Warning("Timeout saving notes during shutdown");
            }

            // Cleanup
            _periodicCleanup?.Dispose();
            
            foreach (var timer in _timers.Values)
            {
                timer?.Dispose();
            }
            
            foreach (var semaphore in _saveLocksPerFile.Values)
            {
                semaphore?.Dispose();
            }
            
            _globalSaveSemaphore?.Dispose();
            _wal?.Dispose();
        }
    }
}
