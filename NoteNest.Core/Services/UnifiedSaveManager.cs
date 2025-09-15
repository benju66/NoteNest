using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        private readonly ISupervisedTaskRunner _taskRunner;
        private readonly ConcurrentDictionary<string, NoteState> _notes;
        private readonly ConcurrentDictionary<string, NoteTimer> _timers;
        private readonly ConcurrentDictionary<string, string> _pathToNoteId;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _saveLocksPerFile;
        private readonly Timer _periodicCleanup;
        private readonly SemaphoreSlim _globalSaveSemaphore;
        private readonly string _emergencyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NoteNest_Emergency");
        private bool _disposed;

        public event EventHandler<NoteSavedEventArgs>? NoteSaved;
        public event EventHandler<SaveProgressEventArgs>? SaveStarted;
        public event EventHandler<SaveProgressEventArgs>? SaveCompleted;
        public event EventHandler<ExternalChangeEventArgs>? ExternalChangeDetected;

        public UnifiedSaveManager(IAppLogger logger, IWriteAheadLog wal, SaveConfiguration config = null, ISupervisedTaskRunner taskRunner = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _wal = wal ?? throw new ArgumentNullException(nameof(wal));
            _config = config ?? new SaveConfiguration();
            _taskRunner = taskRunner; // Allow null for backward compatibility
            
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
            if (_taskRunner != null)
            {
                _ = _taskRunner.RunAsync(RecoverFromWAL, "WAL Recovery", OperationType.Background);
            }
            else
            {
                Task.Run(RecoverFromWAL); // Fallback for backward compatibility
            }
            
            // Subscribe to save failures for additional handling if taskRunner available
            if (_taskRunner != null)
            {
                _taskRunner.SaveFailed += OnSaveFailure;
            }
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
            
            // Validate the path before doing anything
            try
            {
                ValidateFilePath(filePath);
            }
            catch (ArgumentException ex)
            {
                _logger.Error($"Invalid file path: {filePath} - {ex.Message}");
                throw; // Re-throw to caller
            }

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
                // Write to WAL immediately for crash protection - FIXED: No more silent failures
                if (_taskRunner != null)
                {
                    _ = _taskRunner.RunAsync(
                        async () => await _wal.AppendAsync(noteId, content),
                        $"Crash protection for {System.IO.Path.GetFileName(state.FilePath)}",
                        OperationType.WALWrite
                    );
                }
                else
                {
                    // Fallback for backward compatibility
                    _ = Task.Run(async () => await _wal.AppendAsync(noteId, content));
                }
                
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
            // FIXED: No more silent auto-save failures
            if (_taskRunner != null)
            {
                var fileName = _notes.TryGetValue(noteId, out var state) 
                    ? System.IO.Path.GetFileName(state.FilePath ?? noteId) 
                    : noteId;
                    
                _ = _taskRunner.RunAsync(
                    async () => await SaveNoteAsync(noteId),
                    $"Auto-save for {fileName}",
                    OperationType.AutoSave
                );
            }
            else
            {
                // Fallback for backward compatibility
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
                
                // EMERGENCY SAVE when file is locked
                var emergencySuccess = await EmergencySave(noteId, content);
                if (emergencySuccess)
                {
                    _logger.Info("Content saved to emergency location");
                }
                
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
                
                // EMERGENCY SAVE on any failure
                var emergencySuccess = await EmergencySave(noteId, content);
                if (emergencySuccess)
                {
                    _logger.Info("Content saved to emergency location due to save failure");
                }
                
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
            // Validate new path
            try
            {
                ValidateFilePath(newFilePath);
            }
            catch (ArgumentException ex)
            {
                _logger.Error($"Invalid new file path: {newFilePath} - {ex.Message}");
                return; // Don't update to invalid path
            }

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

        private async Task<bool> EmergencySave(string noteId, string content)
        {
            try
            {
                // Create emergency directory if it doesn't exist
                Directory.CreateDirectory(_emergencyDirectory);
                
                // Get original filename if possible
                var originalName = "unknown_note";
                if (_notes.TryGetValue(noteId, out var state))
                {
                    originalName = Path.GetFileNameWithoutExtension(state.FilePath);
                }
                
                // Create unique emergency filename with timestamp
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var fileName = $"EMERGENCY_{originalName}_{timestamp}.txt";
                var emergencyPath = Path.Combine(_emergencyDirectory, fileName);
                
                // Write content to emergency location
                await File.WriteAllTextAsync(emergencyPath, content, Encoding.UTF8);
                
                _logger.Warning($"EMERGENCY SAVE: Content saved to {emergencyPath}");
                
                // Notify UI that emergency save occurred
                SaveCompleted?.Invoke(this, new SaveProgressEventArgs
                {
                    NoteId = noteId,
                    FilePath = emergencyPath,
                    Success = true
                });
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Emergency save also failed!");
                return false;
            }
        }

        private void ValidateFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path cannot be empty");
            }
            
            // Windows path length limit (260 chars total, leave room for temp files)
            if (filePath.Length > 250)
            {
                throw new ArgumentException($"Path too long: {filePath.Length} characters (max 250)");
            }
            
            // Check for invalid path characters
            var invalidPathChars = Path.GetInvalidPathChars();
            if (filePath.Any(c => invalidPathChars.Contains(c)))
            {
                throw new ArgumentException($"Path contains invalid characters");
            }
            
            // Get just the filename without extension
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("File name cannot be empty");
            }
            
            // Check for invalid filename characters
            var invalidFileChars = Path.GetInvalidFileNameChars();
            if (fileName.Any(c => invalidFileChars.Contains(c)))
            {
                throw new ArgumentException($"File name contains invalid characters");
            }
            
            // Windows reserved filenames (CON, PRN, AUX, etc.)
            var reserved = new[] { 
                "CON", "PRN", "AUX", "NUL", 
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
            };
            
            var upperFileName = fileName.ToUpperInvariant();
            if (reserved.Contains(upperFileName))
            {
                throw new ArgumentException($"'{fileName}' is a reserved Windows filename");
            }
            
            // File names cannot end with space or period in Windows
            if (fileName.EndsWith(" ") || fileName.EndsWith("."))
            {
                throw new ArgumentException("File name cannot end with space or period");
            }
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
            
            // Unsubscribe from events
            if (_taskRunner != null)
            {
                _taskRunner.SaveFailed -= OnSaveFailure;
            }
        }

        private void OnSaveFailure(object? sender, SaveFailureEventArgs e)
        {
            // Additional handling for save failures
            if (e.Type == OperationType.AutoSave || e.Type == OperationType.WALWrite)
            {
                try
                {
                    // Try to extract noteId from operation name if possible
                    var noteId = ExtractNoteIdFromOperation(e.OperationName);
                    if (!string.IsNullOrEmpty(noteId) && _notes.TryGetValue(noteId, out var state))
                    {
                        // Ensure dirty indicator stays visible so user knows it needs saving
                        state.Touch();
                        _logger.Warning($"Save failure for {noteId} - keeping dirty state visible to user");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error handling save failure callback");
                }
            }
        }

        private string ExtractNoteIdFromOperation(string operationName)
        {
            // Simple extraction - could be enhanced if needed
            // For now, just return empty string as noteId extraction is complex
            return string.Empty;
        }
    }
}
