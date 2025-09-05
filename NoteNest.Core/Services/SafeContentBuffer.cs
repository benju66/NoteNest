using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Thread-safe buffer that captures note content immediately as users type,
    /// ensuring no data loss even if the main save pipeline fails or is delayed.
    /// </summary>
    public interface ISafeContentBuffer
    {
        void BufferContent(string noteId, string content);
        string? GetLatestContent(string noteId);
        BufferedContent? GetBufferedContent(string noteId);
        void ClearBuffer(string noteId);
        int GetBufferCount();
        TimeSpan GetBufferAge(string noteId);
    }

    public class BufferedContent
    {
        public string NoteId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public int UpdateCount { get; set; }
        public long ContentHash { get; set; }
    }

    public class SafeContentBuffer : ISafeContentBuffer, IDisposable
    {
        private readonly ConcurrentDictionary<string, BufferedContent> _buffer = new();
        private readonly IAppLogger _logger;
        private readonly Timer _cleanupTimer;
        private readonly TimeSpan _bufferExpiration;
        private int _totalBufferUpdates;

        public SafeContentBuffer(IAppLogger? logger = null, TimeSpan? bufferExpiration = null)
        {
            _logger = logger ?? AppLogger.Instance;
            _bufferExpiration = bufferExpiration ?? TimeSpan.FromMinutes(30);
            
            // Cleanup old entries periodically
            _cleanupTimer = new Timer(CleanupExpiredBuffers, null, 
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            
            _logger.Info("SafeContentBuffer initialized with expiration: " + _bufferExpiration);
        }

        public void BufferContent(string noteId, string content)
        {
            if (string.IsNullOrEmpty(noteId)) return;

            var hash = ComputeQuickHash(content);
            
            _buffer.AddOrUpdate(noteId,
                id => new BufferedContent
                {
                    NoteId = id,
                    Content = content,
                    Timestamp = DateTime.UtcNow,
                    UpdateCount = 1,
                    ContentHash = hash
                },
                (id, existing) =>
                {
                    // Only update if content actually changed
                    if (existing.ContentHash != hash)
                    {
                        existing.Content = content;
                        existing.Timestamp = DateTime.UtcNow;
                        existing.UpdateCount++;
                        existing.ContentHash = hash;
                        Interlocked.Increment(ref _totalBufferUpdates);
                    }
                    return existing;
                });

            // Log high-frequency updates for monitoring
            if (_totalBufferUpdates % 100 == 0)
            {
                _logger.Debug($"SafeContentBuffer: {_totalBufferUpdates} total updates, {_buffer.Count} active buffers");
            }
        }

        public string? GetLatestContent(string noteId)
        {
            return _buffer.TryGetValue(noteId, out var buffered) ? buffered.Content : null;
        }

        public BufferedContent? GetBufferedContent(string noteId)
        {
            return _buffer.TryGetValue(noteId, out var buffered) ? buffered : null;
        }

        public void ClearBuffer(string noteId)
        {
            if (_buffer.TryRemove(noteId, out var removed))
            {
                _logger.Debug($"Cleared buffer for note {noteId} after {removed.UpdateCount} updates");
            }
        }

        public int GetBufferCount() => _buffer.Count;

        public TimeSpan GetBufferAge(string noteId)
        {
            if (_buffer.TryGetValue(noteId, out var buffered))
            {
                return DateTime.UtcNow - buffered.Timestamp;
            }
            return TimeSpan.Zero;
        }

        private void CleanupExpiredBuffers(object? state)
        {
            try
            {
                var expiredCount = 0;
                var cutoff = DateTime.UtcNow - _bufferExpiration;

                foreach (var kvp in _buffer)
                {
                    if (kvp.Value.Timestamp < cutoff)
                    {
                        if (_buffer.TryRemove(kvp.Key, out _))
                        {
                            expiredCount++;
                        }
                    }
                }

                if (expiredCount > 0)
                {
                    _logger.Debug($"Cleaned up {expiredCount} expired buffers");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during buffer cleanup");
            }
        }

        private static long ComputeQuickHash(string content)
        {
            // Simple hash for quick comparison - not cryptographic
            if (string.IsNullOrEmpty(content)) return 0;
            
            unchecked
            {
                long hash = 17;
                hash = hash * 31 + content.Length;
                
                // Sample the content for performance
                int step = Math.Max(1, content.Length / 100);
                for (int i = 0; i < content.Length; i += step)
                {
                    hash = hash * 31 + content[i];
                }
                
                return hash;
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _buffer.Clear();
            _logger.Info($"SafeContentBuffer disposed after {_totalBufferUpdates} total updates");
        }
    }
}
