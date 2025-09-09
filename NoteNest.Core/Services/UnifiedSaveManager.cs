using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    public interface ISaveManager : IDisposable
    {
        Task<string> OpenNoteAsync(string filePath);
        void UpdateContent(string noteId, string content);
        Task<bool> SaveNoteAsync(string noteId);
        Task<BatchSaveResult> SaveAllDirtyAsync();
        bool IsNoteDirty(string noteId);
        string GetContent(string noteId);
        string GetFilePath(string noteId);
        IReadOnlyList<string> GetDirtyNoteIds();
        void CloseNote(string noteId);
        
        event EventHandler<NoteSavedEventArgs> NoteSaved;
        event EventHandler<ExternalChangeEventArgs> ExternalChangeDetected;
    }

    public class NoteSavedEventArgs : EventArgs
    {
        public string NoteId { get; set; }
        public string FilePath { get; set; }
        public DateTime SavedAt { get; set; }
    }

    public class ExternalChangeEventArgs : EventArgs
    {
        public string NoteId { get; set; }
        public string FilePath { get; set; }
        public DateTime DetectedAt { get; set; }
    }

    public class BatchSaveResult
    {
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<string> FailedNoteIds { get; set; } = new();
    }

    public enum SavePriority
    {
        AutoSave = 0,
        UserSave = 1,
        ShutdownSave = 2
    }

    public class SaveRequest
    {
        public string NoteId { get; set; }
        public SavePriority Priority { get; set; }
        public int DelayMs { get; set; }
        public DateTime QueuedAt { get; set; }
        public TaskCompletionSource<bool> CompletionSource { get; set; }
    }

    public class NoteState
    {
        public string NoteId { get; set; }
        public string FilePath { get; set; }
        public string LastSavedContent { get; set; }
        public string CurrentContent { get; set; }
        public DateTime LastSaveTime { get; set; }
        public DateTime FileLastModified { get; set; }
        public bool IsDirty => CurrentContent != null && CurrentContent != LastSavedContent;
    }

    public class UnifiedSaveManager : ISaveManager
    {
        private readonly Channel<SaveRequest> _saveChannel;
        private readonly Dictionary<string, NoteState> _notes = new();
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
        private readonly Dictionary<string, Timer> _autoSaveTimers = new();
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _processorTask;
        private readonly IAppLogger _logger;

        public event EventHandler<NoteSavedEventArgs> NoteSaved;
        public event EventHandler<ExternalChangeEventArgs> ExternalChangeDetected;

        public UnifiedSaveManager(IAppLogger logger)
        {
            _logger = logger;
            _saveChannel = Channel.CreateUnbounded<SaveRequest>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
            
            _processorTask = Task.Run(() => ProcessSaveQueue(_cancellation.Token));
        }

        public async Task<string> OpenNoteAsync(string filePath)
        {
            var noteId = Guid.NewGuid().ToString();
            
            // Read initial content
            string content = "";
            DateTime lastModified = DateTime.UtcNow;
            
            if (File.Exists(filePath))
            {
                content = await File.ReadAllTextAsync(filePath);
                lastModified = File.GetLastWriteTimeUtc(filePath);
            }
            
            // Store state
            _notes[noteId] = new NoteState
            {
                NoteId = noteId,
                FilePath = filePath,
                LastSavedContent = content,
                CurrentContent = content,
                LastSaveTime = DateTime.UtcNow,
                FileLastModified = lastModified
            };
            
            // Setup file watcher
            SetupFileWatcher(noteId, filePath);
            
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
            
            // Update content immediately
            state.CurrentContent = content;
            
            // Cancel existing auto-save timer
            if (_autoSaveTimers.TryGetValue(noteId, out var timer))
            {
                timer?.Dispose();
                _autoSaveTimers.Remove(noteId);
            }
            
            // Queue new auto-save with debounce
            if (state.IsDirty)
            {
                var autoSaveTimer = new Timer(_ => 
                {
                    var request = new SaveRequest
                    {
                        NoteId = noteId,
                        Priority = SavePriority.AutoSave,
                        DelayMs = 0,
                        QueuedAt = DateTime.UtcNow,
                        CompletionSource = new TaskCompletionSource<bool>()
                    };
                    
                    if (!_saveChannel.Writer.TryWrite(request))
                    {
                        _logger.Warning($"Failed to queue auto-save for: {noteId}");
                    }
                }, null, 2000, Timeout.Infinite);
                
                _autoSaveTimers[noteId] = autoSaveTimer;
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
            if (_autoSaveTimers.TryGetValue(noteId, out var timer))
            {
                timer?.Dispose();
                _autoSaveTimers.Remove(noteId);
            }
            
            var request = new SaveRequest
            {
                NoteId = noteId,
                Priority = SavePriority.UserSave,
                DelayMs = 0,
                QueuedAt = DateTime.UtcNow,
                CompletionSource = new TaskCompletionSource<bool>()
            };
            
            await _saveChannel.Writer.WriteAsync(request);
            return await request.CompletionSource.Task;
        }

        public async Task<BatchSaveResult> SaveAllDirtyAsync()
        {
            var result = new BatchSaveResult();
            var tasks = new List<Task<bool>>();
            
            foreach (var kvp in _notes)
            {
                if (kvp.Value.IsDirty)
                {
                    var request = new SaveRequest
                    {
                        NoteId = kvp.Key,
                        Priority = SavePriority.ShutdownSave,
                        DelayMs = 0,
                        QueuedAt = DateTime.UtcNow,
                        CompletionSource = new TaskCompletionSource<bool>()
                    };
                    
                    await _saveChannel.Writer.WriteAsync(request);
                    tasks.Add(request.CompletionSource.Task);
                }
            }
            
            var results = await Task.WhenAll(tasks);
            
            foreach (var success in results)
            {
                if (success) result.SuccessCount++;
                else result.FailureCount++;
            }
            
            return result;
        }

        private async Task ProcessSaveQueue(CancellationToken cancellation)
        {
            var priorityQueue = new SortedList<SavePriority, Queue<SaveRequest>>();
            priorityQueue[SavePriority.ShutdownSave] = new Queue<SaveRequest>();
            priorityQueue[SavePriority.UserSave] = new Queue<SaveRequest>();
            priorityQueue[SavePriority.AutoSave] = new Queue<SaveRequest>();
            
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    // Read from channel with timeout
                    var request = await _saveChannel.Reader.ReadAsync(cancellation);
                    
                    // Process by priority
                    SaveRequest toProcess = null;
                    
                    // Add to priority queue
                    priorityQueue[request.Priority].Enqueue(request);
                    
                    // Get highest priority item
                    foreach (var queue in priorityQueue.Values)
                    {
                        if (queue.Count > 0)
                        {
                            toProcess = queue.Dequeue();
                            break;
                        }
                    }
                    
                    if (toProcess != null)
                    {
                        await ExecuteSave(toProcess);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error in save queue processor");
                }
            }
        }

        private async Task ExecuteSave(SaveRequest request)
        {
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
                
                // Check for external modifications
                if (File.Exists(state.FilePath))
                {
                    var currentModified = File.GetLastWriteTimeUtc(state.FilePath);
                    if (currentModified > state.FileLastModified)
                    {
                        _logger.Warning($"External modification detected for: {state.FilePath}");
                        ExternalChangeDetected?.Invoke(this, new ExternalChangeEventArgs
                        {
                            NoteId = request.NoteId,
                            FilePath = state.FilePath,
                            DetectedAt = DateTime.UtcNow
                        });
                        request.CompletionSource?.TrySetResult(false);
                        return;
                    }
                }
                
                // Write to temp file
                var tempFile = state.FilePath + ".tmp";
                await File.WriteAllTextAsync(tempFile, state.CurrentContent);
                
                // Atomic rename with timeout
                var moveTask = Task.Run(() => File.Move(tempFile, state.FilePath, true));
                if (await Task.WhenAny(moveTask, Task.Delay(30000)) != moveTask)
                {
                    throw new TimeoutException($"Save timeout for {state.FilePath}");
                }
                
                // Update state
                state.LastSavedContent = state.CurrentContent;
                state.LastSaveTime = DateTime.UtcNow;
                state.FileLastModified = File.GetLastWriteTimeUtc(state.FilePath);
                
                // Raise event
                NoteSaved?.Invoke(this, new NoteSavedEventArgs
                {
                    NoteId = request.NoteId,
                    FilePath = state.FilePath,
                    SavedAt = state.LastSaveTime
                });
                
                _logger.Info($"Saved: {state.FilePath}");
                request.CompletionSource?.TrySetResult(true);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to save note: {request.NoteId}");
                request.CompletionSource?.TrySetResult(false);
            }
        }

        private void SetupFileWatcher(string noteId, string filePath)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                var fileName = Path.GetFileName(filePath);
                
                var watcher = new FileSystemWatcher(directory)
                {
                    Filter = fileName,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                };
                
                watcher.Changed += (s, e) =>
                {
                    if (_notes.TryGetValue(noteId, out var state))
                    {
                        var currentModified = File.GetLastWriteTimeUtc(filePath);
                        if (currentModified > state.FileLastModified)
                        {
                            ExternalChangeDetected?.Invoke(this, new ExternalChangeEventArgs
                            {
                                NoteId = noteId,
                                FilePath = filePath,
                                DetectedAt = DateTime.UtcNow
                            });
                        }
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

        public bool IsNoteDirty(string noteId)
        {
            return _notes.TryGetValue(noteId, out var state) && state.IsDirty;
        }

        public string GetContent(string noteId)
        {
            return _notes.TryGetValue(noteId, out var state) ? state.CurrentContent : "";
        }

        public string GetFilePath(string noteId)
        {
            return _notes.TryGetValue(noteId, out var state) ? state.FilePath : null;
        }

        public IReadOnlyList<string> GetDirtyNoteIds()
        {
            return _notes.Where(kvp => kvp.Value.IsDirty).Select(kvp => kvp.Key).ToList();
        }

        public void CloseNote(string noteId)
        {
            if (_watchers.TryGetValue(noteId, out var watcher))
            {
                watcher.Dispose();
                _watchers.Remove(noteId);
            }
            
            if (_autoSaveTimers.TryGetValue(noteId, out var timer))
            {
                timer.Dispose();
                _autoSaveTimers.Remove(noteId);
            }
            
            _notes.Remove(noteId);
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            _saveChannel.Writer.TryComplete();
            
            try
            {
                _processorTask.Wait(5000);
            }
            catch { }
            
            foreach (var watcher in _watchers.Values)
            {
                watcher?.Dispose();
            }
            
            foreach (var timer in _autoSaveTimers.Values)
            {
                timer?.Dispose();
            }
            
            _cancellation.Dispose();
        }
    }
}
