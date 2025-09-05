using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Lightweight append-only log that persists content changes to disk for crash recovery.
    /// Uses a simple line-based format for fast writes and easy recovery.
    /// </summary>
    public interface IContentPersistenceLog : IDisposable
    {
        Task LogChangeAsync(string noteId, string content);
        Task<Dictionary<string, string>> RecoverUnpersistedChangesAsync();
        Task MarkPersistedAsync(string noteId);
        Task ClearLogAsync();
    }

    public class ContentPersistenceLog : IContentPersistenceLog
    {
        private readonly string _logDirectory;
        private readonly IAppLogger _logger;
        private readonly Channel<LogEntry> _writeChannel;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task _writerTask;
        private readonly SemaphoreSlim _rotationLock = new(1, 1);
        private string _currentLogFile;

        private class LogEntry
        {
            public string Type { get; set; } = "CHANGE";
            public string NoteId { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
        }

        public ContentPersistenceLog(ConfigurationService configService, IAppLogger? logger = null)
        {
            _logger = logger ?? AppLogger.Instance;
            
            // Create log directory in user's local app data
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _logDirectory = Path.Combine(appData, "NoteNest", "persistence");
            Directory.CreateDirectory(_logDirectory);
            
            _currentLogFile = GetLogFileName();
            
            // Create unbounded channel for write operations
            _writeChannel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            
            _cancellationTokenSource = new CancellationTokenSource();
            _writerTask = Task.Run(() => WriterLoopAsync(_cancellationTokenSource.Token));
            
            _logger.Info($"ContentPersistenceLog initialized at: {_logDirectory}");
        }

        public async Task LogChangeAsync(string noteId, string content)
        {
            if (string.IsNullOrEmpty(noteId)) return;

            var entry = new LogEntry
            {
                Type = "CHANGE",
                NoteId = noteId,
                Content = content,
                Timestamp = DateTime.UtcNow
            };

            // Non-blocking write to channel
            if (!_writeChannel.Writer.TryWrite(entry))
            {
                _logger.Warning($"Failed to queue change log for note: {noteId}");
            }

            await Task.CompletedTask;
        }

        public async Task MarkPersistedAsync(string noteId)
        {
            if (string.IsNullOrEmpty(noteId)) return;

            var entry = new LogEntry
            {
                Type = "PERSISTED",
                NoteId = noteId,
                Content = string.Empty,
                Timestamp = DateTime.UtcNow
            };

            _writeChannel.Writer.TryWrite(entry);
            await Task.CompletedTask;
        }

        public async Task<Dictionary<string, string>> RecoverUnpersistedChangesAsync()
        {
            var changes = new Dictionary<string, string>();
            var persisted = new HashSet<string>();

            try
            {
                // Read all log files from the last 24 hours
                var cutoff = DateTime.UtcNow.AddDays(-1);
                var logFiles = Directory.GetFiles(_logDirectory, "persistence-*.log")
                    .Where(f => File.GetCreationTimeUtc(f) > cutoff)
                    .OrderBy(f => f);

                foreach (var logFile in logFiles)
                {
                    try
                    {
                        using var reader = new StreamReader(logFile, Encoding.UTF8);
                        string? line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            try
                            {
                                var entry = JsonSerializer.Deserialize<LogEntry>(line);
                                if (entry == null) continue;

                                if (entry.Type == "CHANGE")
                                {
                                    // Only keep if not marked as persisted
                                    if (!persisted.Contains(entry.NoteId))
                                    {
                                        changes[entry.NoteId] = entry.Content;
                                    }
                                }
                                else if (entry.Type == "PERSISTED")
                                {
                                    persisted.Add(entry.NoteId);
                                    changes.Remove(entry.NoteId);
                                }
                            }
                            catch (JsonException)
                            {
                                // Skip malformed entries
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to read log file {logFile}: {ex.Message}");
                    }
                }

                _logger.Info($"Recovered {changes.Count} unpersisted changes from logs");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to recover changes from persistence log");
            }

            return changes;
        }

        public async Task ClearLogAsync()
        {
            await _rotationLock.WaitAsync();
            try
            {
                // Delete old log files
                var logFiles = Directory.GetFiles(_logDirectory, "persistence-*.log");
                foreach (var file in logFiles)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch { }
                }

                // Create new log file
                _currentLogFile = GetLogFileName();
                _logger.Info("Persistence log cleared");
            }
            finally
            {
                _rotationLock.Release();
            }
        }

        private async Task WriterLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new List<LogEntry>();
            var flushTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Collect entries for up to 1 second or 100 entries
                    var collectTask = CollectEntriesAsync(buffer, 100, cancellationToken);
                    var timerTask = flushTimer.WaitForNextTickAsync(cancellationToken).AsTask();

                    var completedTask = await Task.WhenAny(collectTask, timerTask);

                    if (buffer.Count > 0)
                    {
                        await FlushBufferAsync(buffer);
                        buffer.Clear();
                    }

                    // Rotate log daily
                    await CheckLogRotationAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Persistence log writer failed");
            }
            finally
            {
                // Final flush
                if (buffer.Count > 0)
                {
                    await FlushBufferAsync(buffer);
                }
                flushTimer.Dispose();
            }
        }

        private async Task CollectEntriesAsync(List<LogEntry> buffer, int maxEntries, CancellationToken cancellationToken)
        {
            while (buffer.Count < maxEntries && !cancellationToken.IsCancellationRequested)
            {
                if (await _writeChannel.Reader.WaitToReadAsync(cancellationToken))
                {
                    if (_writeChannel.Reader.TryRead(out var entry))
                    {
                        buffer.Add(entry);
                    }
                }
                else
                {
                    break; // Channel completed
                }
            }
        }

        private async Task FlushBufferAsync(List<LogEntry> entries)
        {
            if (entries.Count == 0) return;

            try
            {
                var lines = entries.Select(e => JsonSerializer.Serialize(e, new JsonSerializerOptions
                {
                    WriteIndented = false
                }));

                var content = string.Join(Environment.NewLine, lines) + Environment.NewLine;

                // Append to log file
                await File.AppendAllTextAsync(_currentLogFile, content, Encoding.UTF8);

                // Log high-frequency writes for monitoring
                if (entries.Count > 50)
                {
                    _logger.Debug($"Flushed {entries.Count} entries to persistence log");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to flush {entries.Count} entries to persistence log");
            }
        }

        private async Task CheckLogRotationAsync()
        {
            var expectedFile = GetLogFileName();
            if (_currentLogFile != expectedFile)
            {
                await _rotationLock.WaitAsync();
                try
                {
                    _currentLogFile = expectedFile;
                    
                    // Clean up old logs (keep 7 days)
                    var cutoff = DateTime.UtcNow.AddDays(-7);
                    var oldFiles = Directory.GetFiles(_logDirectory, "persistence-*.log")
                        .Where(f => File.GetCreationTimeUtc(f) < cutoff);
                    
                    foreach (var oldFile in oldFiles)
                    {
                        try
                        {
                            File.Delete(oldFile);
                        }
                        catch { }
                    }
                }
                finally
                {
                    _rotationLock.Release();
                }
            }
        }

        private string GetLogFileName()
        {
            var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
            return Path.Combine(_logDirectory, $"persistence-{date}.log");
        }

        public void Dispose()
        {
            _logger.Info("ContentPersistenceLog disposing - flushing pending writes...");
            
            // Signal shutdown but don't cancel immediately - let pending writes complete
            _writeChannel.Writer.TryComplete();
            
            // Force a final flush by waiting for the writer to process remaining items
            try
            {
                // Give writer task time to flush remaining entries
                if (!_writerTask.Wait(TimeSpan.FromSeconds(10)))
                {
                    _logger.Warning("Persistence log writer did not complete in time - forcing shutdown");
                    _cancellationTokenSource.Cancel();
                    _writerTask.Wait(TimeSpan.FromSeconds(2));
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error during persistence log shutdown: {ex.Message}");
            }

            _cancellationTokenSource.Dispose();
            _rotationLock.Dispose();
            
            _logger.Info("ContentPersistenceLog disposed");
        }
    }
}
